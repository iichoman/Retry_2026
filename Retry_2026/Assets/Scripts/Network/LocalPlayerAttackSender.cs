using UnityEngine;

// ============================================================================
//  LocalPlayerAttackSender (좌클릭 근접 공격 전송)
//
//   마우스 왼쪽 클릭 → 근접 공격. 실제 데미지/판정/투사체는 서버가 처리.
//   ※ 임시 우클릭(무기 던지기/원거리) 기능은 제거됨.
// ============================================================================
[RequireComponent(typeof(Defalult_Input))]
public class LocalPlayerAttackSender : MonoBehaviour
{
    [Tooltip("공격 패킷 출발 높이 보정 (Y축). 발바닥이 아닌 눈높이에서 출발.")]
    [SerializeField] private float launchHeightOffset = 1.6f;

    private NetworkBootstrap bootstrap;
    private Defalult_Input input;
    private Player_Attack attackComponent;
    private bool previousAttackInput;

    public void Initialize(NetworkBootstrap bs) { bootstrap = bs; }

    private void Awake()
    {
        input = GetComponent<Defalult_Input>();
        attackComponent = GetComponent<Player_Attack>();
    }

    private void Update()
    {
        if (bootstrap == null) return;
        if (bootstrap.Session == null || !bootstrap.Session.IsConnected) return;
        if (input == null) return;

        // ── 마우스 왼쪽 클릭: 근접 공격 ──
        bool leftNow = input.Attack;
        bool leftPressed = leftNow && !previousAttackInput;
        previousAttackInput = leftNow;
        if (leftPressed)
        {
            string weaponId = attackComponent != null ? attackComponent.EquippedWeaponId : "";
            int wk = MapWeaponKind(weaponId);
            if (wk == (int)WeaponKind.BOW || wk == (int)WeaponKind.GUN)
                wk = (int)WeaponKind.SWORD;     // 좌클릭은 근접 전용
            SendAttack(wk);
        }
    }

    private void SendAttack(int weaponKind)
    {
        int comboIndex = attackComponent != null
            ? Mathf.Max(1, attackComponent.ActiveAnimationComboIndex) : 1;

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
        Debug.Log($"[Send] ATTACK weapon={(WeaponKind)weaponKind}");
    }

    private static int MapWeaponKind(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return (int)WeaponKind.SWORD;
        string id = weaponId.ToLowerInvariant();
        if (id.Contains("big") && id.Contains("sword")) return (int)WeaponKind.BIG_SWORD;
        if (id.Contains("bow")) return (int)WeaponKind.BOW;
        if (id.Contains("gun")) return (int)WeaponKind.GUN;
        return (int)WeaponKind.SWORD;
    }
}