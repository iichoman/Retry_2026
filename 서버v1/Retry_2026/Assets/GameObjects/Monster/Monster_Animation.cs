using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class Monster_Animation : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Monster_Attack monsterAttack;
    [SerializeField] private Monster_State monsterState;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private string blendParameterName = "Mspeed";
    [SerializeField, Min(0f)] private float blendDampTime = 0.1f;
    [SerializeField, Min(0f)] private float attackTransitionDuration = 0.05f;
    [SerializeField, Min(0f)] private float hitTransitionDuration = 0.03f;
    [SerializeField, Min(0f)] private float deathTransitionDuration = 0.05f;
    [SerializeField] private string attackStateTag = "Attack";
    [SerializeField] private string hitStateTag = "Hit";

    private int blendHash;
    private bool wasInAttackState;
    private bool wasInHitState;
    private Vector3 previousPosition;
    private bool hasBlendParameter;
    private bool hasWarnedMissingBlendParameter;

    public bool IsAttacking => animator != null && IsInAttackState();

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (monsterAttack == null)
        {
            monsterAttack = GetComponent<Monster_Attack>();
        }

        if (monsterState == null)
        {
            monsterState = GetComponent<Monster_State>();
        }

        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        blendHash = Animator.StringToHash(blendParameterName);
        hasBlendParameter = HasFloatParameter(blendParameterName);
        previousPosition = transform.position;
    }

    private void Update()
    {
        if (animator == null)
        {
            return;
        }

        if (hasBlendParameter)
        {
            animator.SetFloat(blendHash, GetCurrentMoveSpeed(), blendDampTime, Time.deltaTime);
        }
        else
        {
            if (!hasWarnedMissingBlendParameter)
            {
                Debug.LogWarning(
                    $"Animator parameter '{blendParameterName}' was not found on {animator.runtimeAnimatorController?.name ?? "None"}.",
                    this
                );
                hasWarnedMissingBlendParameter = true;
            }
        }

        UpdateHitState();
        PlayPendingDeath();
        PlayPendingHit();

        if (monsterState != null && (monsterState.IsDead || monsterState.IsHit))
        {
            return;
        }

        UpdateAttackState();
        PlayPendingAttack();
    }

    private void OnDisable()
    {
        wasInAttackState = false;
        wasInHitState = false;
        if (monsterAttack != null)
        {
            monsterAttack.CancelAttack();
        }
    }

    private float GetCurrentMoveSpeed()
    {
        Vector3 currentPosition = transform.position;

        if (navMeshAgent != null && navMeshAgent.enabled)
        {
            previousPosition = currentPosition;
            return navMeshAgent.velocity.magnitude;
        }

        Vector3 frameDelta = currentPosition - previousPosition;
        previousPosition = currentPosition;
        return frameDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    private void UpdateAttackState()
    {
        bool isInAttackState = IsInAttackState();

        if (wasInAttackState && !isInAttackState && monsterAttack != null)
        {
            monsterAttack.NotifyAttackAnimationCompleted();
        }

        wasInAttackState = isInAttackState;
    }

    private void UpdateHitState()
    {
        bool isInHitState = IsInHitState();

        if (wasInHitState && !isInHitState && monsterState != null)
        {
            monsterState.NotifyHitAnimationCompleted();
        }

        wasInHitState = isInHitState;
    }

    private void PlayPendingHit()
    {
        if (monsterState == null)
        {
            return;
        }

        if (!monsterState.TryConsumeHitAnimationRequest(out string stateName))
        {
            return;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, stateHash))
        {
            Debug.LogWarning($"Hit animation state was not found: {stateName}", this);
            monsterState.NotifyHitAnimationUnavailable();
            return;
        }

        monsterAttack?.CancelAttack();
        animator.CrossFadeInFixedTime(stateHash, hitTransitionDuration, 0, 0f);
        monsterState.NotifyHitAnimationStarted();
        wasInHitState = true;
        wasInAttackState = false;
    }

    private void PlayPendingDeath()
    {
        if (monsterState == null)
        {
            return;
        }

        if (!monsterState.TryConsumeDeathAnimationRequest(out string stateName))
        {
            return;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, stateHash))
        {
            Debug.LogWarning($"Death animation state was not found: {stateName}", this);
            return;
        }

        monsterAttack?.CancelAttack();

        if (navMeshAgent != null && navMeshAgent.enabled)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.isStopped = true;
        }

        animator.CrossFadeInFixedTime(stateHash, deathTransitionDuration, 0, 0f);
        wasInAttackState = false;
        wasInHitState = false;
    }

    private void PlayPendingAttack()
    {
        if (monsterAttack == null)
        {
            return;
        }

        if (!monsterAttack.TryConsumeAttackAnimationRequest(out string stateName))
        {
            return;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, stateHash))
        {
            Debug.LogWarning($"Attack animation state was not found: {stateName}", this);
            return;
        }

        animator.CrossFadeInFixedTime(stateHash, attackTransitionDuration, 0, 0f);
        monsterAttack.NotifyAttackAnimationStarted();
        wasInAttackState = true;
    }

    private bool IsInAttackState()
    {
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.IsTag(attackStateTag))
        {
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            if (nextState.IsTag(attackStateTag))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInHitState()
    {
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.IsTag(hitStateTag))
        {
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            if (nextState.IsTag(hitStateTag))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasFloatParameter(string parameterName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type != AnimatorControllerParameterType.Float)
            {
                continue;
            }

            if (parameters[i].name == parameterName)
            {
                return true;
            }
        }

        return false;
    }
}
