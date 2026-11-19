using UnityEngine;

/// <summary>
/// Predict local defense; server validates stamina, parry and stun. Remote mode only presents snapshots.
/// Online dash keeps the existing movement cap and does not grant invulnerability.
/// </summary>
[DefaultExecutionOrder(-40), DisallowMultipleComponent]
[RequireComponent(typeof(Player_Movement), typeof(Defalult_Input))]
public sealed class PlayerDefenseController : MonoBehaviour
{
    public const float DefaultOfflineDashSpeed = 24f;
    public const float DefaultDashDuration = .7f;
    private const float DashInputBuffer = .2f;
    [SerializeField, Min(.1f)] private float dashDuration = DefaultDashDuration;
    [SerializeField, Min(0)] private float dashCooldown = .8f;
    [SerializeField, Min(1)] private float offlineDashSpeed = DefaultOfflineDashSpeed;
    [SerializeField, Min(0)] private float offlineDodgeWindow = .18f;
    [SerializeField, Min(.1f)] private float parryDuration = .5f;
    [SerializeField, Min(0)] private float parryCooldown = .65f;
    [SerializeField, Min(0)] private float parryWindow = .35f;
    [SerializeField, Range(0, 180)] private float parryHalfAngle = 60f;
    [SerializeField, Min(.1f)] private float parryReach = 4f;

    private enum DefenseAction { None, Dash, Parry }
    private DefenseAction action;
    private Defalult_Input input;
    private Player_Movement movement;
    private CharacterController controller;
    private Player_State state;
    private Player_Attack attack;
    private Player_Animation animationView;
    private Player_LockOnSystem lockOn;
    private float dashRequestedUntil = float.NegativeInfinity;
    private Vector3 requestedDashDirection;
    private float startedAt, nextDashAt, nextParryAt;
    private bool previousDash, previousParry, parryConsumed, beganOnline;
    private int lastHp;
    public bool IsRemotePresentation { get; private set; }
    private int remoteSerial = -1;
    private int pendingSequence;
    private bool pendingRecovery;
    private float pendingAt,nextRecoveryAt;
    private Vector3 pendingRecoveryDirection;
    public void SetRemotePresentation() { IsRemotePresentation = true; enabled = true; }
    public void ApplyRemoteDefense(PlayerPresentation data)
    {
        if (!IsRemotePresentation || data.serial < remoteSerial) return;
        if (data.action == 0) { remoteSerial=data.serial; CancelDefense(); return; }
        if (data.serial == remoteSerial) return; // snapshots must not restart the clip
        remoteSerial=data.serial;
        if(data.action==4)state?.CancelHitRecovery();
        attack?.CancelCurrentAttack();
        action=data.action==4?DefenseAction.Dash:DefenseAction.Parry;
        if(action==DefenseAction.Dash)dashDuration=data.duration;else parryDuration=data.duration;
        DashDirection=new Vector3(data.dx,0,data.dz);
        startedAt=Time.time-(data.duration-data.remaining);
    }
    public void ReconcileDefense(int sequence, int result)
    {
        if(pendingSequence==0 || sequence!=pendingSequence)return;
        pendingSequence=0;
        if(pendingRecovery){
            pendingRecovery=false;
            if(result==0 && CanControl(true)){
                DashDirection=pendingRecoveryDirection;state?.CancelHitRecovery();Begin(DefenseAction.Dash);
                nextDashAt=nextRecoveryAt=Time.time+Mathf.Max(dashDuration,dashCooldown);PlayDashEffect();
            }
        }else if(result!=0)CancelDefense();
    }


    public bool IsDashing => action == DefenseAction.Dash && Time.time - startedAt < dashDuration;
    public bool IsParrying => action == DefenseAction.Parry && Time.time - startedAt < parryDuration;
    public bool IsBusy => IsDashing || IsParrying;
    public Vector3 DashDirection { get; private set; }
    public float DashSpeed => NetworkRuntime.IsOnline ? Mathf.Min(10f, movement.SprintSpeed) : offlineDashSpeed;
    public float ActionProgress => !IsBusy ? 0f : Mathf.Clamp01((Time.time - startedAt) / (IsDashing ? dashDuration : parryDuration));
    public bool CanAvoidOfflineMelee => !NetworkRuntime.IsOnline && !beganOnline && CanControl() && IsDashing && Time.time - startedAt < offlineDodgeWindow;

    private void Awake()
    {
        input = GetComponent<Defalult_Input>();
        movement = GetComponent<Player_Movement>();
        controller = GetComponent<CharacterController>();
        state = GetComponent<Player_State>();
        attack = GetComponent<Player_Attack>();
        animationView = GetComponent<Player_Animation>();
        lockOn = GetComponent<Player_LockOnSystem>();
    }

    private void OnEnable()
    {
        if (attack != null) attack.EquippedWeaponChanged += OnWeaponChanged;
        if (state != null) { lastHp = state.CurrentHp; state.HpChanged += OnHealthChanged; }
    }

    private void OnDisable()
    {
        if (attack != null) attack.EquippedWeaponChanged -= OnWeaponChanged;
        if (state != null) state.HpChanged -= OnHealthChanged;
        CancelDefense();
        if(parryOutline!=null)parryOutline.enabled=false;
        pendingRecovery=false;pendingSequence=0;
        previousDash = previousParry = false;
    }

    EliteMonsterOutline parryOutline;
    private void LateUpdate(){
        if(IsParrying && parryOutline==null)parryOutline=gameObject.AddComponent<EliteMonsterOutline>();
        if(parryOutline!=null)parryOutline.enabled=IsParrying && (state==null||!state.IsDead);
    }

    private void Update()
    {
        if(IsRemotePresentation) return;
        if(pendingRecovery&&Time.unscaledTime-pendingAt>2){pendingRecovery=false;pendingSequence=0;}
        bool dash = input != null && input.Dash;
        bool parry = input != null && input.Parry;
        bool dashPressed = dash && !previousDash;
        bool parryPressed = parry && !previousParry;
        previousDash = dash; previousParry = parry;
        if (!CanControl(true) || (IsBusy && beganOnline != NetworkRuntime.IsOnline)) { CancelDefense(); return; }
        if (action != DefenseAction.None && !IsBusy) action = DefenseAction.None;
        // One action per press. Dash wins simultaneous Space/right-click; held inputs never repeat.
        if (dashPressed)
        {
            requestedDashDirection = movement.ResolveMoveDirection(input.Move);
            if (requestedDashDirection.sqrMagnitude < .001f && lockOn != null && lockOn.IsLockedOn)
                requestedDashDirection = Vector3.ProjectOnPlane(transform.position - lockOn.CurrentTarget.position, Vector3.up);
            dashRequestedUntil = Time.time + DashInputBuffer;
        }
        if (Time.time <= dashRequestedUntil)
        {
            if (TryStartDash(requestedDashDirection)) dashRequestedUntil = float.NegativeInfinity;
        }
        else if (parryPressed) TryStartParry();
    }

    private bool CanControl(bool allowHit = false) => !IsRemotePresentation && isActiveAndEnabled && input != null && input.isActiveAndEnabled && input.IsGameplayInputEnabled
        && movement != null && movement.isActiveAndEnabled && controller != null && controller.enabled
        && (state == null || (!state.IsDead && !state.IsOfflineParryStunned && (allowHit || !state.IsHit))) && Time.timeScale > 0 && !(ABClient.Active!=null && ABClient.Active.Supported && (ABClient.Active.State.statusMask&4)!=0);

    private bool CanStart(bool cancelAttack = false, bool allowHit = false) => CanControl(allowHit) && !IsBusy && !pendingRecovery && controller.isGrounded
        && (cancelAttack || attack == null || attack.ActiveAnimationComboIndex == 0)
        && (animationView == null || ((cancelAttack || !animationView.IsAttacking) && (allowHit || !animationView.IsHit)));

    public bool TryStartDash(Vector3 worldDirection)
    {
        bool recovery=(state!=null&&state.IsHit)||(animationView!=null&&animationView.IsHit)||
            (ABClient.Active!=null&&ABClient.Active.Supported&&(ABClient.Active.State.statusMask&32)!=0);
        if (!CanStart(true,true) || Time.time < (recovery?nextRecoveryAt:nextDashAt)) return false;
        if (!float.IsFinite(worldDirection.x) || !float.IsFinite(worldDirection.z)) return false;
        // Bootstrap owns the usual pool; standalone practice creates the same component locally.
        var stamina = PlayerStamina.Local;
        if (stamina == null) stamina = GetComponent<PlayerStamina>() ?? gameObject.AddComponent<PlayerStamina>();
        stamina.BindPlayer(input);
        worldDirection.y = 0;
        if (worldDirection.sqrMagnitude < .001f) worldDirection = -transform.forward;
        DashDirection = worldDirection.normalized;
        if (!stamina.TrySpendDash(DashDirection,recovery)) return false;
        if(ABClient.Active!=null && ABClient.Active.Supported){
            pendingSequence=ABClient.Active.LastRequestSequence;
            if(recovery){pendingRecovery=true;pendingRecoveryDirection=DashDirection;pendingAt=Time.unscaledTime;return true;}
        }
        if(recovery){state?.CancelHitRecovery();nextRecoveryAt=Time.time+Mathf.Max(dashDuration,dashCooldown);}
        Begin(DefenseAction.Dash);
        PlayDashEffect();
        nextDashAt = Time.time + Mathf.Max(dashDuration, dashCooldown);
        return true;
    }
    private void PlayDashEffect()
    {
        // Movement feedback only; this is not an online dodge-success or invulnerability effect.
        PooledVfxSpawner.Play(CombatVfxKind.DodgeDust, transform.position + Vector3.up * .04f,
            Quaternion.LookRotation(DashDirection), 1f, GetInstanceID());
    }

    public bool TryStartParry()
    {
        if (PlayerSkillView.Busy(gameObject) || !CanStart() || attack == null || !attack.IsSwordEquipped || Time.time < nextParryAt) return false;
        if(ABClient.Active!=null && ABClient.Active.Supported) {
            if(ABClient.Active.State.stamina<5)return false;
            ABClient.Active.Request(5,0); pendingSequence=ABClient.Active.LastRequestSequence;
        }
        Begin(DefenseAction.Parry);
        nextParryAt = Time.time + Mathf.Max(parryDuration, parryCooldown);
        parryConsumed = false;
        return true;
    }

    private void Begin(DefenseAction next)
    {
        GetComponent<PlayerSkillView>()?.Cancel();
        attack?.CancelCurrentAttack();
        var animator=GetComponentInChildren<Animator>();
        if(animator!=null && animator.HasState(0,Animator.StringToHash("Blend Tree")))
            animator.Play("Blend Tree",0,0);
        action = next; startedAt = Time.time; beganOnline = NetworkRuntime.IsOnline;
        dashRequestedUntil = float.NegativeInfinity;
    }

    public void CancelDefense()
    {
        action = DefenseAction.None; DashDirection = Vector3.zero;
        dashRequestedUntil = float.NegativeInfinity;
        PooledVfxSpawner.StopOwner(GetInstanceID());
    }

    /// <summary>Called only by typed, local melee damage. Generic/projectile and server damage bypass this.</summary>
    public bool TryDefendMelee(GameObject attacker)
    {
        if (NetworkRuntime.IsOnline || beganOnline || !CanControl()) return false;
        if (CanAvoidOfflineMelee) return true;
        if (!IsParrying || parryConsumed || Time.time - startedAt >= parryWindow || attack == null || !attack.IsSwordEquipped || attacker == null) return false;
        Vector3 toward = attacker.transform.position - transform.position;
        if (Mathf.Abs(toward.y) > 2.5f) return false;
        toward.y = 0;
        if (toward.sqrMagnitude < .001f || toward.sqrMagnitude > parryReach * parryReach
            || Vector3.Dot(transform.forward, toward.normalized) < Mathf.Cos(parryHalfAngle * Mathf.Deg2Rad)) return false;
        parryConsumed = true;
        var monster = attacker.GetComponentInParent<Monster_State>();
        if (monster != null && monster.Type != MonsterType.Boss && !monster.IsNetworkBoss) monster.ApplyOfflineParryStun();
        var player = attacker.GetComponentInParent<Player_State>();
        if (player != null && player != state) player.ApplyOfflineParryStun();
        return true;
    }

    private void OnWeaponChanged(string _) => CancelDefense();
    // HpChanged occurs before RequestHit/Die, so cancel immediately rather than waiting a frame.
    private void OnHealthChanged(int hp, int maxHp)
    {
        if (hp <= 0 || hp < lastHp && !state.SuppressHitReaction) CancelDefense();
        lastHp = hp;
    }
}
