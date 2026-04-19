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
    [SerializeField] private string attackStateTag = "Attack";

    private int blendHash;
    private bool wasInAttackState;
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

        if (!hasBlendParameter)
        {
            if (!hasWarnedMissingBlendParameter)
            {
                Debug.LogWarning(
                    $"Animator parameter '{blendParameterName}' was not found on {animator.runtimeAnimatorController?.name ?? "None"}.",
                    this
                );
                hasWarnedMissingBlendParameter = true;
            }

            return;
        }

        animator.SetFloat(blendHash, GetCurrentMoveSpeed(), blendDampTime, Time.deltaTime);

        if (monsterState != null && monsterState.IsDead)
        {
            return;
        }

        UpdateAttackState();
        PlayPendingAttack();
    }

    private void OnDisable()
    {
        wasInAttackState = false;
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
