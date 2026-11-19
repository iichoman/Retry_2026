using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Explicit offline practice scene only. Never spawns in SampleScene or network sessions.</summary>
public sealed class OfflineDefensePractice : MonoBehaviour
{
    [SerializeField] private Player_State player;
    [SerializeField] private Transform dummy;
    [SerializeField] private Renderer indicator;
    private float strikeAt;
    private bool active;
    private string lastResult = "연습 시작: T";
    private MaterialPropertyBlock block;
    private Font font;
    private GUIStyle style;

    private void Start()
    {
        block = new MaterialPropertyBlock();
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 20);
    }

    private void Update()
    {
        if (NetworkRuntime.IsOnline || player == null || dummy == null) { active = false; return; }
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            active = !active; strikeAt = Time.time + 2f;
            lastResult = active ? "노란색 준비 표시가 붉게 바뀔 때 공격합니다." : "연습 중지";
        }
        float remaining = strikeAt - Time.time;
        if (indicator != null)
        {
            block.SetColor("_BaseColor", !active ? Color.gray : remaining < .7f ? Color.yellow : new Color(.5f, .22f, .1f));
            indicator.SetPropertyBlock(block);
        }
        if (!active || remaining > 0) return;
        strikeAt = Time.time + 2.4f;
        if (player.IsDead) { active = false; lastResult = "연습 종료 · Play를 다시 시작하세요."; return; }
        Vector3 offset = player.transform.position - dummy.position;
        offset.y = 0;
        if (offset.sqrMagnitude > 3f * 3f) { lastResult = "공격 범위 밖 (3m)"; return; }
        if (player.CurrentHp <= 20) player.Heal(player.MaxHp); // Endless practice; production health code is unchanged.
        bool damaged = player.TryTakeMeleeDamage(10, dummy.gameObject);
        lastResult = damaged ? "피격: -10 HP" : "회피 / 패링으로 근접 공격 차단";
        if (indicator != null) { block.SetColor("_BaseColor", Color.red); indicator.SetPropertyBlock(block); }
    }

    private void OnGUI()
    {
        if (NetworkRuntime.IsOnline || player == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.box) { font = font, fontSize = 18, alignment = TextAnchor.UpperLeft, padding = new RectOffset(14,14,12,12) };
        float remaining = active ? Mathf.Max(0, strikeAt - Time.time) : 0;
        GUI.Box(new Rect(18,18,570,155), "오프라인 회피·패링 연습장\nWASD 이동 / Space 회피 / 검 + 우클릭 패링\nT: 허수아비 공격 시작·중지 (2.4초 간격, 3m 범위)\n" +
            $"HP {player.CurrentHp}/{player.MaxHp} · 다음 공격 {remaining:0.0}초\n{lastResult}", style);
    }

    private void OnDestroy() { if (font != null) Destroy(font); }
}
