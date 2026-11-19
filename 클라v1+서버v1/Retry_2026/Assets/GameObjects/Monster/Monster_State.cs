using System;
using UnityEngine;

public enum MonsterType
{
    Normal,
    Elite,
    Boss
}

public enum MonsterElement
{    None
}

[DisallowMultipleComponent]
public class Monster_State : MonoBehaviour, IDamageable
{
    [Header("Monster Info")]
    [SerializeField] private MonsterType monsterType = MonsterType.Normal;
    [SerializeField] private MonsterElement monsterElement = MonsterElement.None;

    [Header("Stats")]
    [SerializeField, Min(1)] private int maxHp = 100;
    [SerializeField, Min(0)] private int attackPower = 10;
    [SerializeField, Min(0)] private int defense = 0;
    [SerializeField] private string hitAnimationStateName = "Hit";
    [SerializeField] private string deathAnimationStateName = "Die";
    [SerializeField, Min(0.01f)] private float hitStunDuration = 0.35f;

    public event Action<int, int> HpChanged;
    public event Action<Monster_State, GameObject> Died;

    public MonsterType Type => monsterType;
    public MonsterElement Element => monsterElement;
    public int MaxHp => maxHp;
    public int CurrentHp { get; private set; }
    public int AttackPower => attackPower;
    public int Defense => defense;
    public bool IsDead { get; private set; }
    public bool IsHit { get; private set; }

    private bool hitAnimationRequested;
    private bool hitAnimationInProgress;
    private bool deathAnimationRequested;
    private float hitFallbackTimer;

    private void Awake()
    {
        CurrentHp = maxHp;
        IsDead = false;
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
        if (IsDead)
        {
            return;
        }

        int finalDamage = Mathf.Max(1, damage - defense);
        CurrentHp = Mathf.Max(0, CurrentHp - finalDamage);
        HpChanged?.Invoke(CurrentHp, maxHp);

        string attackerName = attacker != null ? attacker.name : "Unknown";
        Debug.Log(
            $"[{name}] hit by [{attackerName}] damage={finalDamage}, hp={CurrentHp}/{maxHp}",
            this
        );

        if (CurrentHp <= 0)
        {
            Die(attacker);
            return;
        }

        RequestHit();
    }

    private void Die(GameObject attacker)
    {
        IsDead = true;
        EndHit();
        RequestDeath();

        if (DungeonExitManager.Instance != null)
        {
            DungeonExitManager.Instance.RegisterMonsterKill(attacker);
        }

        Died?.Invoke(this, attacker);
    }

    public bool TryConsumeDeathAnimationRequest(out string stateName)
    {
        stateName = string.Empty;

        if (!deathAnimationRequested)
        {
            return false;
        }

        deathAnimationRequested = false;
        stateName = deathAnimationStateName;
        return true;
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

    private void RequestDeath()
    {
        deathAnimationRequested = true;
    }

    // ──────────────────────────────────────────────────────────────
    //  네트워크(서버 권위) 연동 — 서버가 계산한 HP/사망을 그대로 반영.
    //  reflection으로 backing field만 바꾸면 HpChanged 이벤트(HP바)와
    //  피격/죽음 애니가 동작하지 않으므로, 아래 메서드로 정상 경로를 탄다.
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

    // 서버 사망 통보: 죽음 애니 재생. 엔티티(시체)는 삭제하지 않고 그대로 둔다.
    public void NetworkDie()
    {
        if (IsDead) return;
        IsDead = true;
        EndHit();
        RequestDeath();
        Died?.Invoke(this, null);
    }

    // 스폰 시 서버 권위 최대/현재 HP 설정 → HP바 스케일을 서버와 일치(클라 기본 maxHp와 불일치 방지).
    public void NetworkSetMax(int newMaxHp, int newHp)
    {
        maxHp = Mathf.Max(1, newMaxHp);
        CurrentHp = Mathf.Clamp(newHp, 0, maxHp);
        HpChanged?.Invoke(CurrentHp, maxHp);
    }
}