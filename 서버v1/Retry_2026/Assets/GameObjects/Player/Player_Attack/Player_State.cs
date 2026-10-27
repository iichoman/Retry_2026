using UnityEngine;
using System;

public class Player_State : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int defense = 0;
    [SerializeField] private string hitAnimationStateName = "Hit";
    [SerializeField, Min(0.01f)] private float hitStunDuration = 0.35f;

    public event Action<int, int> HpChanged;

    public int MaxHp => maxHp;
    public int CurrentHp { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsHit { get; private set; }

    private bool hitAnimationRequested;
    private bool hitAnimationInProgress;
    private float hitFallbackTimer;

    private void Awake()
    {
        CurrentHp = maxHp;
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

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHp = Mathf.Min(maxHp, CurrentHp + amount);
        HpChanged?.Invoke(CurrentHp, maxHp);
    }

    private void Die(GameObject attacker)
    {
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
    public void NetworkApplyHp(int newHp)
    {
        if (IsDead) return;
        bool damaged = newHp < CurrentHp;
        CurrentHp = Mathf.Clamp(newHp, 0, maxHp);
        HpChanged?.Invoke(CurrentHp, maxHp);

        if (CurrentHp <= 0) { NetworkDie(); return; }
        if (damaged) RequestHit();
    }

    // 서버 사망 통보: IsDead 설정(이동/공격 잠금). PauseMenu가 이를 보고 사망 메뉴를 띄움.
    public void NetworkDie()
    {
        if (IsDead) return;
        IsDead = true;
        EndHit();
    }
}