using UnityEngine;
using System;

public class Player_State : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int defense = 0;
    [SerializeField] private string hitAnimationStateName = "Hit";
    [SerializeField, Min(0.01f)] private float hitStunDuration = 0.35f;

    public event Action<int, int> HpChanged;
    public bool SuppressHitReaction { get; private set; }

    public int MaxHp => maxHp;
    public int CurrentHp { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsHit { get; private set; }
    float offlineParryUntil;
    public bool IsOfflineParryStunned=>!NetworkRuntime.IsOnline&&Time.time<offlineParryUntil;

    private bool hitAnimationRequested;
    private bool hitAnimationInProgress;
    private float hitFallbackTimer;
    private PlayerDefenseController defenseController;

    private void Awake()
    {
        CurrentHp = maxHp;
        defenseController = GetComponent<PlayerDefenseController>();
    }

    private void Start()
    {
        HpChanged?.Invoke(CurrentHp, maxHp);
    }

    private void Update()
    {
        if (!IsHit || hitAnimationInProgress)
        {
            return;
        }

        hitFallbackTimer -= Time.deltaTime;
        if (hitFallbackTimer <= 0f)
        {
            EndHit();
        }
    }

    public void TakeDamage(int damage, GameObject attacker)
    {
        if (NetworkRuntime.IsOnline) return;
        if (IsDead) return;

        int finalDamage = Mathf.Max(1, damage - defense);
        CurrentHp = Mathf.Max(0, CurrentHp - finalDamage);
        HpChanged?.Invoke(CurrentHp, maxHp);

        if (CurrentHp == 0)
        {
            Die(attacker);
            return;
        }

        RequestHit();
    }

    // Only explicit melee sources may be parried/evaded in the offline prototype.
    // Server absolute HP and generic damage always bypass this path.
    public bool TryTakeMeleeDamage(int damage, GameObject attacker)
    {
        if (NetworkRuntime.IsOnline || IsDead || damage <= 0) return false;
        if (defenseController != null && defenseController.TryDefendMelee(attacker)) return false;
        TakeDamage(damage, attacker);
        return true;
    }

    public void ApplyOfflineParryStun()
    {
        if (NetworkRuntime.IsOnline || IsDead) return;
        offlineParryUntil=Time.time+.75f;
        (GetComponent<StunReactionView>()??gameObject.AddComponent<StunReactionView>()).StunOffline(.75f);
        defenseController?.CancelDefense();
        GetComponent<Player_Attack>()?.CancelCurrentAttack();
        RequestHit();
    }

    public void Heal(int amount)
    {
        if (NetworkRuntime.IsOnline) return;
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHp = Mathf.Min(maxHp, CurrentHp + amount);
        HpChanged?.Invoke(CurrentHp, maxHp);
    }

    private void Die(GameObject attacker)
    {
        defenseController?.CancelDefense();
        GetComponent<Player_Attack>()?.CancelCurrentAttack();
        IsDead = true;
        EndHit();

        if (DungeonExitManager.Instance != null)
        {
            DungeonExitManager.Instance.RegisterPlayerKill(GetComponent<Player>(), attacker);
        }
    }

    public bool TryConsumeHitAnimationRequest(out string stateName)
    {
        stateName = string.Empty;

        if (!hitAnimationRequested)
        {
            return false;
        }

        hitAnimationRequested = false;
        stateName = hitAnimationStateName;
        return true;
    }

    public void NotifyHitAnimationStarted()
    {
        IsHit = true;
        hitAnimationInProgress = true;
    }

    public void NotifyHitAnimationCompleted()
    {
        EndHit();
    }
    // Only called by a paid offline recovery roll or an accepted server recovery roll.
    public void CancelHitRecovery(){if(!IsDead)EndHit();}

    public void NotifyHitAnimationUnavailable()
    {
        hitAnimationInProgress = false;
    }

    private void RequestHit()
    {
        IsHit = true;
        hitAnimationRequested = true;
        hitAnimationInProgress = false;
        hitFallbackTimer = hitStunDuration;
    }

    private void EndHit()
    {
        IsHit = false;
        hitAnimationRequested = false;
        hitAnimationInProgress = false;
        hitFallbackTimer = 0f;
    }

    // ──────────────────────────────────────────────────────────────
    //  네트워크(서버 권위) 연동 — 서버가 계산한 내 HP/사망을 반영.
    // ──────────────────────────────────────────────────────────────

    // 서버가 보낸 절대 HP 반영: HP바 갱신 + 감소 시 피격 애니 + 0이면 사망.
    public void ApplyABHealth(int maximum,int hp){maxHp=Mathf.Max(1,maximum);NetworkApplyHp(hp);}
    public void NetworkApplyHp(int newHp, bool hitReaction = true)
    {
        if (IsDead) return;
        bool damaged = newHp < CurrentHp;
        CurrentHp = Mathf.Clamp(newHp, 0, maxHp);
        SuppressHitReaction = !hitReaction;
        try { HpChanged?.Invoke(CurrentHp, maxHp); }
        finally { SuppressHitReaction = false; }

        if (CurrentHp <= 0) { NetworkDie(); return; }
        if (damaged && hitReaction) RequestHit();
    }

    // 서버 사망 통보: IsDead 설정(이동/공격 잠금). PauseMenu가 이를 보고 사망 메뉴를 띄움.
    public void NetworkDie()
    {
        if (IsDead) return;
        defenseController?.CancelDefense();
        GetComponent<Player_Attack>()?.CancelCurrentAttack();
        CurrentHp=0;
        IsDead = true;
        EndHit();
        HpChanged?.Invoke(CurrentHp,maxHp);
    }
}
