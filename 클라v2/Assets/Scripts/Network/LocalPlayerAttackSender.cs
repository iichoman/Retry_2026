using UnityEngine;

// ============================================================================
//  LocalPlayerAttackSender (서버 장착 무기의 좌클릭 공격 전송)
//
//   마우스 왼쪽 클릭 → 서버 승인 장착 무기. 실제 데미지/판정/투사체는 서버가 처리.
//   ※ 임시 우클릭(무기 던지기/원거리) 기능은 제거됨.
// ============================================================================
[RequireComponent(typeof(Defalult_Input))]
// Subscribe before the animation driver starts; requests follow committed swings.
[DefaultExecutionOrder(-10)]
public class LocalPlayerAttackSender : MonoBehaviour
{
    [Tooltip("공격 패킷 출발 높이 보정 (Y축). 발바닥이 아닌 눈높이에서 출발.")]
    [SerializeField] private float launchHeightOffset = 1.6f;

    private NetworkBootstrap bootstrap;
    private Defalult_Input input;
    private Player_Attack attackComponent;
    private Player_State playerState;
    private PlayerDefenseController defense;
    public int RequestsSent { get; private set; }

    public void Initialize(NetworkBootstrap bs) { bootstrap = bs; }

    private void Awake()
    {
        input = GetComponent<Defalult_Input>();
        attackComponent = GetComponent<Player_Attack>();
        playerState = GetComponent<Player_State>();
        defense = GetComponent<PlayerDefenseController>();
        if (attackComponent != null) attackComponent.AttackAnimationStarted += OnAttackStarted;
    }

    private void OnDestroy()
    {
        if (attackComponent != null) attackComponent.AttackAnimationStarted -= OnAttackStarted;
    }

    private void OnAttackStarted(int comboIndex)
    {
        if (!isActiveAndEnabled) return;
        if (bootstrap == null) return;
        if (bootstrap.Session == null || !bootstrap.Session.IsConnected) return;
        if (input == null || !input.isActiveAndEnabled || !input.IsGameplayInputEnabled) return;
        if (attackComponent == null || !attackComponent.isActiveAndEnabled || (defense != null && defense.IsBusy)) return;
        if (!bootstrap.SessionGameStarted || bootstrap.IsAwaitingDebugTeleport) return;
        if (!bootstrap.TryGetAttackWeaponKind(attackComponent, out int weaponKind)) return;
        if (playerState != null && (playerState.IsDead || playerState.IsHit)) return;

        // 서버 장비 우선. 구형 서버 호환 모드에서는 기본 검의 공격 의도만 전송한다.
        if(ABClient.Active!=null && ABClient.Active.Supported && (ABClient.Active.State.statusMask&4)!=0)return;
        if(PlayerSkillView.Busy(gameObject) || SkillClient.HasPendingCast)return;
        SendAttack(weaponKind, comboIndex);
    }

    private void SendAttack(int weaponKind, int comboIndex)
    {
        // 서버는 마지막으로 받은 위치에서 판정하므로 공격 직전 위치를 먼저 보낸다.
        GetComponent<LocalPlayerInputSender>()?.SendNow();
        if (attackComponent != null && attackComponent.ActiveAnimationComboIndex == 0)
            attackComponent.AlignAttackToLockOn();

        Vector3 origin = transform.position + Vector3.up * launchHeightOffset;
        Vector3 dir = transform.forward;       // 미쿠가 보는 방향

        var packet = new PlayerAttackRequest
        {
            weaponKind = weaponKind,
            comboIndex = comboIndex,
            originX = origin.x,
            originY = origin.y,
            originZ = origin.z,
            dirX = dir.x,
            dirY = dir.y,
            dirZ = dir.z,
            timestamp = System.DateTime.UtcNow.Ticks,
        };
        bootstrap.Session.SendPlayerAttack(packet);
        RequestsSent++;
    }

}
