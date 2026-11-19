using UnityEngine;

// ============================================================================
//  PlayerStamina — 로컬 플레이어의 스태미나.
//
//  소모
//    · 달리기 : 0.2초마다 1  (SHIFT를 0.2초 이상 누르고 실제로 움직일 때만)
//    · 공격   : 1회 2
//  회복
//    · 0.1초마다 1. 걸어다니는 건(WASD/방향키) 회복을 막지 않는다.
//    · 단 마지막 소모 후 regenDelay 동안만 잠긴다. 달리는 중에는 0.2초마다
//      소모가 일어나 잠금이 계속 갱신되므로 자연히 회복이 안 된다.
//      (이 잠금이 없으면 전투 중에도 초당 10씩 차서 스태미나가 무의미해진다)
//
//    · 공격 모션이 도는 동안에는 회복이 아예 잠긴다. 콤보 타와 타 사이
//      간격(약 0.8초)이 regenDelay보다 길어서, 그것만으로는 사이사이 차버린다.
//
//  0이 되면 달리기가 멈추고 공격이 나가지 않는다.
//    · 달리기 차단 : Defalult_Input.Sprint 가 CanSprint를 함께 본다
//    · 공격 차단   : Player_Attack 이 CanAttack을 먼저 본다
//
//  NetworkBootstrap이 자식으로 만든다. 플레이어 프리팹은 건드리지 않는다.
// ============================================================================
public class PlayerStamina : MonoBehaviour
{
    public static PlayerStamina Local { get; private set; }

    [SerializeField] private int maxStamina = 100;
    [SerializeField] private int attackCost = 2;
    [SerializeField] private float sprintTickSeconds = 0.2f;   // 이 주기마다 1 소모
    [SerializeField] private float sprintStartDelay = 0.2f;    // SHIFT 누르고 이만큼 뒤부터 소모
    [SerializeField] private float regenTickSeconds = 0.1f;    // 이 주기마다 1 회복
    [SerializeField] private float regenDelaySeconds = 1.0f;   // 소모 직후 회복 잠금

    public int Current { get; private set; }
    public int Max => maxStamina;
    public float Normalized => maxStamina > 0 ? Current / (float)maxStamina : 0f;
    public bool CanSprint => Current > 0;
    public bool CanAttack => Current >= attackCost;

    private Defalult_Input input;
    private Player_Movement movement;
    private Player_Animation anim;
    private float sprintHeldFor;
    private float sprintTickAt;
    private float regenTickAt;
    private float lockRegenUntil;

    private void Awake()
    {
        Local = this;
        Current = maxStamina;
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    /// <summary>실제로 휘두른 1타분을 깎는다. 모션이 시작될 때 딱 한 번 불린다.</summary>
    public void SpendAttack()
    {
        Current = Mathf.Max(0, Current - attackCost);
        lockRegenUntil = Time.time + regenDelaySeconds;
    }

    public void Refill() => Current = maxStamina;

    private void Update()
    {
        ResolveLocalPlayer();
        if (input == null) return;

        bool moving = movement != null && movement.CurrentSpeed > 0.1f;
        bool wantsSprint = input.Sprint && moving;

        if (wantsSprint)
        {
            // SHIFT를 짧게 툭 친 건 달리기가 아니다. 애니메이션이 나오는 시점부터 깎는다.
            sprintHeldFor += Time.deltaTime;
            if (sprintHeldFor >= sprintStartDelay && Time.time >= sprintTickAt)
            {
                sprintTickAt = Time.time + sprintTickSeconds;
                Current = Mathf.Max(0, Current - 1);
                lockRegenUntil = Time.time + regenDelaySeconds;
            }
        }
        else
        {
            sprintHeldFor = 0f;
            sprintTickAt = 0f;
        }

        // 회복: 공격 모션이 끝나고 regenDelay까지 지나야 시작. 이동은 상관없다.
        // (콤보 1→2→3→4 사이 간격이 regenDelay보다 길어 사이사이 차버리던 문제)
        bool attacking = anim != null && anim.IsAttacking;

        if (!attacking && Time.time >= lockRegenUntil && Current < maxStamina)
        {
            if (Time.time >= regenTickAt)
            {
                regenTickAt = Time.time + regenTickSeconds;
                Current = Mathf.Min(maxStamina, Current + 1);
            }
        }
        else
        {
            regenTickAt = 0f;
        }
    }

    // 로컬 플레이어는 세션 입장 때 런타임 생성된다. 바뀌면 다시 잡는다.
    private void ResolveLocalPlayer()
    {
        if (input != null && input.isActiveAndEnabled) return;

        input = null;
        movement = null;
        anim = null;
        foreach (var sender in FindObjectsByType<LocalPlayerInputSender>(FindObjectsSortMode.None))
        {
            input = sender.GetComponent<Defalult_Input>();
            movement = sender.GetComponent<Player_Movement>();
            anim = sender.GetComponent<Player_Animation>();
            if (input != null) break;
        }
        if (input != null) return;

        // 폴백: 조작이 켜져 있는 입력 컴포넌트를 찾는다(로비 더미는 꺼져 있다).
        foreach (var i in FindObjectsByType<Defalult_Input>(FindObjectsSortMode.None))
        {
            if (!i.isActiveAndEnabled) continue;
            input = i;
            movement = i.GetComponent<Player_Movement>();
            anim = i.GetComponent<Player_Animation>();
            break;
        }
    }
}
