using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Explicit preview-scene event source. Never creates online AI, damage or loot.</summary>
public sealed class BossPresentationPreview : MonoBehaviour
{
    public CombatVfxCatalog Catalog;
    private const int PreviewId = 900099;
    private BossVfxController boss;
    private BossTelegraphData skill;
    private bool active, dead;
    private int sequence, phase = 1, hp = 4000, followup;
    private long followupAt;
    private Font font;
    private GUIStyle style;

    private void Start() { if (!NetworkRuntime.IsOnline) Restart(); }

    public void Restart()
    {
        if (NetworkRuntime.IsOnline) return;
        if (boss != null) { boss.gameObject.SetActive(false); Destroy(boss.gameObject); }
        if (Catalog == null) Catalog = CombatVfxCatalog.Load();
        if (Catalog == null || Catalog.BossPrefab == null) { enabled = false; return; }
        var holder = new GameObject("Preview boss holder");
        holder.transform.SetParent(transform, false); holder.SetActive(false);
        var instance = Instantiate(Catalog.BossPrefab, holder.transform);
        instance.transform.position = Vector3.zero;
        foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = behaviour is BossVfxController;
        var nav = instance.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = false;
        boss = instance.GetComponent<BossVfxController>();
        if (boss == null) { Destroy(holder); enabled = false; return; }
        instance.transform.SetParent(transform, true); // Awake now runs with local combat disabled.
        Destroy(holder);
        phase = 1; hp = 4000; sequence = followup = 0; active = dead = false;
        boss.Initialize(PreviewId, hp, 4000);
        boss.SetRoomBounds(new Bounds(Vector3.zero, new Vector3(48, 30, 48)));
        boss.SetPresentationVisible(true);
        State();
    }

    private void State() => boss.ApplyState(new BossStateData { monsterId = PreviewId,
        phase = phase, hp = hp, maxHp = 4000, roomId = 1, state = dead ? 1 : 0, serverTimeMs = ServerClock.NowMs });

    public void PlayPattern(int pattern)
    {
        if (NetworkRuntime.IsOnline || boss == null || dead || active || pattern < 1 || pattern > 3) return;
        long now = ServerClock.NowMs;
        // Snapshot fixtures from the previous BossCombat.h; online playback always uses received values.
        long warning = phase == 2 ? (pattern == 1 ? 1200 : 1300) : pattern == 1 ? 1400 : 1600;
        float radius = pattern == 1 ? 4.5f : pattern == 2 ? 1.5f : phase == 2 ? 10f : 8f;
        Vector3 origin = boss.transform.position;
        Vector3 destination = pattern == 2 ? origin + (origin.z > 0 ? Vector3.back : Vector3.forward) * 14f : origin;
        long duration = pattern == 2 ? (long)System.Math.Ceiling(14000.0 / (phase == 2 ? 22 : 16)) : 0;
        skill = new BossTelegraphData { monsterId = PreviewId, sequence = ++sequence, pattern = pattern, phase = phase,
            originX = origin.x, originY = origin.y, originZ = origin.z,
            targetX = destination.x, targetY = destination.y, targetZ = destination.z, radius = radius,
            startTimeMs = now, resolveTimeMs = now + warning, endTimeMs = now + warning + duration, serverTimeMs = now };
        boss.transform.rotation = Quaternion.LookRotation(pattern == 2 ? destination - origin : Vector3.back);
        boss.Telegraph(skill); active = true;
    }

    private void Update()
    {
        if (NetworkRuntime.IsOnline)
        {
            if (boss != null) boss.gameObject.SetActive(false);
            active = false; enabled = false; return;
        }
        var keyboard = Keyboard.current;
        if (boss == null) return;
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame) Restart();
            if (keyboard.digit1Key.wasPressedThisFrame) PlayPattern(1);
            if (keyboard.digit2Key.wasPressedThisFrame) PlayPattern(2);
            if (keyboard.digit3Key.wasPressedThisFrame) { followup = phase == 2 && !active ? 1 : 0; PlayPattern(3); }
            if (keyboard.pKey.wasPressedThisFrame && !dead) { phase = 2; hp = 2000; State(); }
            if (keyboard.hKey.wasPressedThisFrame && !dead) { hp = Mathf.Max(1, hp - 100); boss.SetHp(hp, 4000); boss.OnHit(100); }
            if (keyboard.kKey.wasPressedThisFrame && !dead)
            {
                dead = true; active = false; followup = 0; hp = 0; State();
                var animator = boss.GetComponent<Animator>();
                if (animator != null && animator.HasState(0, Animator.StringToHash("Die"))) animator.CrossFadeInFixedTime("Die", .05f);
            }
        }
        if (boss == null || dead) return;
        long now = ServerClock.NowMs;
        if (!active && followup > 0 && now >= followupAt) { followup = 0; PlayPattern(3); }
        if (!active) return;
        if (skill.pattern == 2 && now >= skill.resolveTimeMs)
            boss.transform.position = Vector3.Lerp(new Vector3(skill.originX, skill.originY, skill.originZ),
                new Vector3(skill.targetX, skill.targetY, skill.targetZ), (float)(now - skill.resolveTimeMs) / Mathf.Max(1, skill.endTimeMs - skill.resolveTimeMs));
        if (now < skill.endTimeMs) return;
        var position = boss.transform.position;
        boss.Resolve(new BossResolveData { monsterId = PreviewId, sequence = skill.sequence, pattern = skill.pattern, phase = skill.phase,
            posX = position.x, posY = position.y, posZ = position.z, radius = skill.radius, serverTimeMs = now });
        active = false; followupAt = now + 250;
    }

    private void OnGUI()
    {
        if (NetworkRuntime.IsOnline) return;
        if (style == null)
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 18);
            style = new GUIStyle(GUI.skin.box) { font = font, fontSize = 18, alignment = TextAnchor.MiddleLeft };
        }
        GUI.Box(new Rect(16, Screen.height - 100, 650, 84),
            "보스 연출 미리보기 · 전투 판정/보상 없음\n1 내려찍기  2 돌진  3 충격파  P 2페이즈\nH 피격  K 사망  R 초기화", style);
    }
    private void OnDestroy() { if (font != null) Destroy(font); }
}
