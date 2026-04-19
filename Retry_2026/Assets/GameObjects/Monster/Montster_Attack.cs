using UnityEngine;

[DisallowMultipleComponent]
public class Monster_Attack : MonoBehaviour
{
    [SerializeField] private Monster monster;
    [SerializeField] private Monster_State monsterState;
    [SerializeField] private MonsterHitbox[] attackHitboxes;
    [SerializeField] private string attackAnimationStateName = "Attack";
    [SerializeField, Min(0f)] private float attackCooldown = 1.2f;
    [SerializeField, Min(0)] private int fallbackDamage = 10;

    private float cooldownTimer;
    private bool attackRequested;
    private bool isAttacking;

    public bool IsAttacking => isAttacking;
    public bool CanAttack => !isAttacking && cooldownTimer <= 0f && monsterState != null && !monsterState.IsDead;
    public int CurrentAttackDamage => monsterState != null ? monsterState.AttackPower : fallbackDamage;
    public string AttackAnimationStateName => attackAnimationStateName;

    private void Awake()
    {
        if (monster == null)
        {
            monster = GetComponent<Monster>();
        }

        if (monsterState == null)
        {
            monsterState = GetComponent<Monster_State>();
        }

        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
            {
                attackHitboxes[i].SetOwner(this);
                attackHitboxes[i].SetHitboxActive(false);
            }
        }
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }
    }

    private void OnDisable()
    {
        CancelAttack();
    }

    public bool RequestAttack()
    {
        if (!CanAttack)
        {
            return false;
        }

        attackRequested = true;
        return true;
    }

    public bool TryConsumeAttackAnimationRequest(out string stateName)
    {
        stateName = string.Empty;

        if (!attackRequested)
        {
            return false;
        }

        attackRequested = false;
        stateName = attackAnimationStateName;
        return true;
    }

    public void NotifyAttackAnimationStarted()
    {
        isAttacking = true;
        cooldownTimer = attackCooldown;
    }

    public void NotifyAttackAnimationCompleted()
    {
        EndSwing();
        isAttacking = false;
    }

    public void CancelAttack()
    {
        attackRequested = false;
        isAttacking = false;
        EndSwing();
    }

    public void AnimEvent_BeginSwing()
    {
        BeginSwing();
    }

    public void AnimEvent_EndSwing()
    {
        EndSwing();
    }

    private void BeginSwing()
    {
        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
            {
                attackHitboxes[i].BeginSwing();
            }
        }
    }

    private void EndSwing()
    {
        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
            {
                attackHitboxes[i].EndSwing();
            }
        }
    }
}
