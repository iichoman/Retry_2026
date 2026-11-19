using UnityEngine;

/// <summary>Presentation only. Timings and dimensions come exclusively from authoritative boss packets.</summary>
public sealed class BossVfxController : MonoBehaviour
{
    public CombatVfxCatalog Catalog;
    public Transform VisualRoot;
    public Renderer[] BodyRenderers;
    public Renderer[] CoreRenderers;
    public ParticleSystem ChargeEnergy;
    public ParticleSystem ChargeDust;
    public ParticleSystem PhaseAura;
    public TrailRenderer ChargeTrail;
    public string BossName = "던전 수호자";
    public AudioSource AudioHook;
    [Tooltip("Time from the reused Attack clip's start to its impact pose.")]
    public float AttackAnimationLeadSeconds = 0.6f;
    private BossTelegraphView telegraph;
    private MaterialPropertyBlock bodyProperties;
    private MaterialPropertyBlock coreProperties;
    private BossTelegraphData attack;
    private int id, phase = 1, hp, maxHp = 1, lastSequence, resolvedSequence;
    readonly HealthTrail healthTrail=new();
    private bool dead, hasAttack, presentationVisible, wasCharging, entrancePlayed, attackAnimationPlayed;
    private Animator animator;
    private float lastHit = -10f, hitUntil, shownAt, diedAt;
    private Vector3 visualPosition;
    private Quaternion visualRotation;
    private GUIStyle titleStyle, phaseStyle;
    private Font hudFont;
    private bool hasRoomBounds;
    private Bounds roomBounds;
    private bool hasPhaseState;
    private float basicAttackUntil;
    private float stagger, staggerMax=500;
    private long groggyUntil;

    public bool IsPresentationVisible => presentationVisible;
    public bool HasPhaseState => hasPhaseState;
    public int DisplayedHp => hp;
    public int DisplayedPhase => phase;

    public void Initialize(int monsterId, int health, int maximum)
    {
        id = monsterId; hp = Mathf.Max(0, health); maxHp = Mathf.Max(1, maximum); dead = hp <= 0;
        phase = 1; hasPhaseState = false; basicAttackUntil = 0; shownAt = Time.unscaledTime;
        if (Catalog == null) Catalog = CombatVfxCatalog.Load();
        animator = GetComponent<Animator>();
        if (VisualRoot != null) { visualPosition = VisualRoot.localPosition; visualRotation = VisualRoot.localRotation; }
        if (ChargeTrail != null) { ChargeTrail.emitting = false; ChargeTrail.Clear(); }
        ApplyPhaseColors();
        ApplyColors();
    }
    public void ApplyState(BossStateData state)
    {
        if (state.monsterId != id) return;
        hasPhaseState = true;
        ServerClock.Observe(state.serverTimeMs);
        stagger=state.stagger;staggerMax=state.staggerMax>0?state.staggerMax:500;
        groggyUntil=state.groggyRemaining>0?state.serverTimeMs+(long)(state.groggyRemaining*1000):0;
        if(state.groggyRemaining>0)CancelAttack();
        hp = Mathf.Max(0, state.hp); maxHp = Mathf.Max(1, state.maxHp);
        if (state.state != 0 || hp == 0) { OnDeath(); return; }
        if (dead) return;
        if (state.phase > phase)
        {
            phase = state.phase;
            ApplyPhaseColors();
            if (presentationVisible) PlayEffect(CombatVfxKind.PhaseChange, transform.position, 2.4f);
            ApplyColors();
        }
    }
    public void SetHp(int health, int maximum)
    {
        hp = Mathf.Max(0, health); maxHp = Mathf.Max(1, maximum);
        if (hp == 0) OnDeath();
    }
    // Basic servers provide a single-target attack start, not an authoritative area or phase.
    // Keep the existing attack animation and show a short label without fabricating a skill.
    public void OnBasicAttackStarted()
    {
        if (!dead && !hasPhaseState) basicAttackUntil = Time.unscaledTime + AttackAnimationLeadSeconds;
    }
    public void Telegraph(BossTelegraphData data)
    {
        if (dead || groggyUntil>ServerClock.NowMs || data.monsterId != id || data.sequence <= lastSequence || data.sequence <= resolvedSequence) return;
        ServerClock.Observe(data.serverTimeMs);
        lastSequence = data.sequence;
        if (ServerClock.NowMs >= data.endTimeMs) return;
        CancelAttack(); attack = data; hasAttack = true;
        ShowActiveWarning();
    }
    private void ShowActiveWarning()
    {
        var data = attack;
        if (Catalog == null) Catalog = CombatVfxCatalog.Load();
        if (presentationVisible && ServerClock.NowMs < data.resolveTimeMs && Catalog != null)
        {
            if (telegraph == null)
            {
                telegraph = Catalog.TelegraphPrefab != null ? Instantiate(Catalog.TelegraphPrefab) : new GameObject("Boss Telegraph").AddComponent<BossTelegraphView>();
            }
            telegraph.Show(new Vector3(data.originX, data.originY, data.originZ), new Vector3(data.targetX, data.targetY, data.targetZ), data.radius, data.pattern == 2, data.phase, Catalog.GroundMaterial);
            if (hasRoomBounds) telegraph.SetRoomBounds(roomBounds);
        }
        if (data.pattern == 2 && ChargeEnergy != null && presentationVisible) ChargeEnergy.Play();
    }
    public void Resolve(BossResolveData data)
    {
        if (dead || data.monsterId != id || data.sequence <= resolvedSequence) return;
        ServerClock.Observe(data.serverTimeMs); resolvedSequence = data.sequence;
        if (hasAttack && data.sequence >= attack.sequence) CancelAttack();
        // Replayed historic impacts are discarded; a later state never replays an earlier attack.
        if (data.pattern==0 || !presentationVisible || ServerClock.NowMs - data.serverTimeMs > 650) return;
        var kind = data.pattern == 1 ? CombatVfxKind.SlamImpact : data.pattern == 2 ? CombatVfxKind.ChargeStop : CombatVfxKind.Shockwave;
        var fx = PlayEffect(kind, new Vector3(data.posX, data.posY, data.posZ), data.radius);
        if (fx != null && data.phase >= 2) fx.Color = new Color(1f, 0.18f, 0.08f, 0.8f);
    }
    public void OnHit(int damage)
    {
        if (dead || !presentationVisible || Time.unscaledTime - lastHit < 0.10f) return;
        lastHit = Time.unscaledTime; hitUntil = lastHit + 0.07f;
        PlayEffect(CombatVfxKind.BossHit, transform.position + Vector3.up * 1.4f, damage >= 40 ? 1.1f : 0.65f);
    }
    public void OnDeath()
    {
        if (dead) return;
        dead = true; hp = 0; diedAt = Time.unscaledTime;
        CancelAttack(); PooledVfxSpawner.StopOwner(id);
        if (PhaseAura != null) PhaseAura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (presentationVisible) PlayEffect(CombatVfxKind.BossDeath, transform.position, 2f);
        // Loot visuals are created exclusively by RemoteLootRegistry on LOOT_SPAWN.
    }
    public void SetPresentationVisible(bool visible)
    {
        if (presentationVisible == visible) return;
        presentationVisible = visible;
        if (!visible)
        {
            ClearAttackVisuals(); PooledVfxSpawner.StopOwner(id);
            if (PhaseAura != null) PhaseAura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else if (!dead)
        {
            shownAt = Time.unscaledTime;
            if (!entrancePlayed) { entrancePlayed = true; PlayEffect(CombatVfxKind.BossEntrance, transform.position, 2f); }
            if (hasAttack && ServerClock.NowMs < attack.endTimeMs) ShowActiveWarning();
        }
    }
    public void SetRoomBounds(Bounds bounds)
    {
        hasRoomBounds = true; roomBounds = bounds;
        if (telegraph != null) telegraph.SetRoomBounds(bounds);
        var clipping = new MaterialPropertyBlock(); clipping.SetFloat("_ClipRoom",1f);
        clipping.SetVector("_RoomMinMax",new Vector4(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z));
        if (ChargeTrail != null) ChargeTrail.SetPropertyBlock(clipping);
        if (ChargeEnergy != null) ChargeEnergy.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(clipping);
        if (ChargeDust != null) ChargeDust.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(clipping);
        if (PhaseAura != null) PhaseAura.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(clipping);
    }
    private PooledCombatEffect PlayEffect(CombatVfxKind kind, Vector3 position, float scale)
    {
        var effect = PooledVfxSpawner.Play(kind, position, Quaternion.identity, scale, id);
        if (effect != null && hasRoomBounds) effect.SetRoomBounds(roomBounds);
        return effect;
    }
    private void Update()
    {
        healthTrail.Set(hp/(float)Mathf.Max(1,maxHp));healthTrail.Advance(Time.unscaledDeltaTime);
        if (dead) { ApplyColors(); return; }
        long now = ServerClock.NowMs;
        if(groggyUntil>now){CancelAttack();if(VisualRoot!=null)VisualRoot.localRotation=visualRotation*Quaternion.Euler(12,0,0);ApplyColors();return;}
        if(!hasAttack && VisualRoot!=null){VisualRoot.localRotation=visualRotation;VisualRoot.localPosition=visualPosition;}
        if (hasAttack)
        {
            float progress = Mathf.Clamp01((float)(now - attack.startTimeMs) / Mathf.Max(1f, attack.resolveTimeMs - attack.startTimeMs));
            bool charging = attack.pattern == 2 && now >= attack.resolveTimeMs && now < attack.endTimeMs;
            if (attack.pattern != 2 && !attackAnimationPlayed && now >= attack.resolveTimeMs - (long)(AttackAnimationLeadSeconds * 1000f))
            {
                attackAnimationPlayed = true;
                if (animator != null && animator.HasState(0, Animator.StringToHash("Attack")) && now < attack.resolveTimeMs)
                {
                    // A late warning joins the existing clip at the matching preparation pose.
                    float elapsed = Mathf.Max(0, AttackAnimationLeadSeconds - (attack.resolveTimeMs - now) / 1000f);
                    animator.CrossFadeInFixedTime("Attack", 0.05f, 0, elapsed);
                }
            }
            if (telegraph != null)
            {
                if (now >= attack.resolveTimeMs) telegraph.Hide();
                else telegraph.SetProgress(progress);
            }
            if (ChargeTrail != null) ChargeTrail.emitting = charging && presentationVisible;
            if (charging && !wasCharging && ChargeEnergy != null) ChargeEnergy.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (ChargeDust != null && charging && presentationVisible && !ChargeDust.isPlaying) ChargeDust.Play();
            if (ChargeDust != null && !charging && ChargeDust.isPlaying) ChargeDust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            wasCharging = charging;
            if (VisualRoot != null)
            {
                float lean = charging ? 17f : attack.pattern == 1 ? -12f * progress : -4f * progress;
                VisualRoot.localRotation = visualRotation * Quaternion.Euler(lean, 0, 0);
                VisualRoot.localPosition = visualPosition + Vector3.down * (charging ? 0.04f : progress * 0.10f);
            }
            if (now > attack.endTimeMs + 250) CancelAttack();
        }
        ApplyColors();
    }
    private void ApplyColors()
    {
        if (bodyProperties == null) bodyProperties = new MaterialPropertyBlock();
        if (coreProperties == null) coreProperties = new MaterialPropertyBlock();
        Color energy = phase >= 2 ? new Color(1f, 0.12f, 0.035f) : new Color(1f, 0.62f, 0.06f);
        float brightness = dead ? Mathf.Clamp01(1f - (Time.unscaledTime - diedAt) / 1.5f) * 0.3f : 1.0f + 0.08f * Mathf.Sin(Time.unscaledTime * 2f);
        bool flash = !dead && Time.unscaledTime < hitUntil;
        bodyProperties.SetColor("_BaseColor", flash ? new Color(0.48f, 0.40f, 0.28f) : new Color(0.22f, 0.24f, 0.27f));
        bodyProperties.SetColor("_RimColor", energy);
        coreProperties.SetColor("_BaseColor", energy * (flash ? 1.4f : brightness));
        coreProperties.SetColor("_EmissionColor", energy * (flash ? 2.2f : 1.5f * brightness));
        if (BodyRenderers != null) foreach (var r in BodyRenderers) if (r != null) r.SetPropertyBlock(bodyProperties);
        if (CoreRenderers != null) foreach (var r in CoreRenderers) if (r != null) r.SetPropertyBlock(coreProperties);
        if (PhaseAura != null && phase >= 2 && !dead && presentationVisible && !PhaseAura.isPlaying) PhaseAura.Play();
    }
    private void ApplyPhaseColors()
    {
        Color energy = phase >= 2 ? new Color(1f,0.14f,0.04f,0.45f) : new Color(1f,0.62f,0.06f,0.45f);
        if (ChargeTrail != null) { ChargeTrail.startColor = energy; energy.a = 0f; ChargeTrail.endColor = energy; }
        if (ChargeEnergy != null) { var main = ChargeEnergy.main; energy.a = 0.7f; main.startColor = energy; }
    }
    private void CancelAttack()
    {
        hasAttack = false; attackAnimationPlayed = false; ClearAttackVisuals();
    }
    private void ClearAttackVisuals()
    {
        wasCharging = false;
        if (telegraph != null) telegraph.Hide();
        if (ChargeTrail != null) { ChargeTrail.emitting = false; ChargeTrail.Clear(); }
        if (ChargeEnergy != null) ChargeEnergy.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (ChargeDust != null) ChargeDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (VisualRoot != null) { VisualRoot.localPosition = visualPosition; VisualRoot.localRotation = visualRotation; }
    }
    private void OnGUI()
    {
        if (dead || !presentationVisible || hp <= 0) return;
        if (titleStyle == null)
        {
            hudFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial", "sans-serif" }, 22);
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold, font = hudFont };
            phaseStyle = new GUIStyle(titleStyle) { fontSize = 14, fontStyle = FontStyle.Normal };
        }
        float width = Mathf.Min(600, Screen.width * 0.64f), x = (Screen.width - width) * 0.5f;
        var previous = GUI.color;
        GUI.color = new Color(1, 1, 1, Mathf.Clamp01((Time.unscaledTime - shownAt) / 0.45f));
        float top=Screen.safeArea.yMax<Screen.height?Screen.height-Screen.safeArea.yMax:0;
        top+=132*Screen.height/1080f;
        GUI.Box(new Rect(x-12,top,width+24,90),GUIContent.none);
        GUI.color=new Color(.15f,.15f,.17f);GUI.DrawTexture(new Rect(x,top+8,width,16),Texture2D.whiteTexture);
        GUI.color=Color.white;GUI.DrawTexture(new Rect(x,top+8,width*healthTrail.Delayed,16),Texture2D.whiteTexture);
        GUI.color=new Color(.88f,.12f,.15f);GUI.DrawTexture(new Rect(x,top+8,width*Mathf.Clamp01((float)hp/maxHp),16),Texture2D.whiteTexture);
        GUI.color=Color.white;GUI.Label(new Rect(x,top+26,width,22),$"체력  {hp:N0} / {maxHp:N0}",phaseStyle);
        GUI.color=new Color(.12f,.12f,.16f);GUI.DrawTexture(new Rect(x,top+51,width,10),Texture2D.whiteTexture);
        GUI.color=new Color(1,.83f,.05f);GUI.DrawTexture(new Rect(x,top+51,width*Mathf.Clamp01(stagger/staggerMax),10),Texture2D.whiteTexture);
        GUI.color=Color.white;GUI.Label(new Rect(x,top+63,width,22),$"그로기  {stagger:0} / {staggerMax:0}",phaseStyle);
        GUI.color = previous;
    }
    private void OnDisable()
    {
        CancelAttack(); PooledVfxSpawner.StopOwner(id);
        if (PhaseAura != null) PhaseAura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (AudioHook != null) AudioHook.Stop();
    }
    private void OnDestroy()
    {
        if (telegraph != null) Destroy(telegraph.gameObject);
        if (hudFont != null) Destroy(hudFont);
    }
}
