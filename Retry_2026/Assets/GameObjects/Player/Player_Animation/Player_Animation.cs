using UnityEngine;

[DisallowMultipleComponent]
public class Player_Animation : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Player_Movement playerMovement;
    [SerializeField] private Player_Attack playerAttack;
    [SerializeField] private string blendParameterName = "speed";
    [SerializeField, Min(0f)] private float blendDampTime = 0.1f;
    [SerializeField, Min(0f)] private float attackTransitionDuration = 0.05f;
    [SerializeField] private string attackStateTag = "Attack";

    private int blendHash;
    private bool wasInAttackState;
    private int currentComboIndex;

    public bool IsAttacking => animator != null && IsInAttackState();

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (playerMovement == null)
        {
            playerMovement = GetComponent<Player_Movement>();
        }

        if (playerAttack == null)
        {
            playerAttack = GetComponent<Player_Attack>();
        }

        blendHash = Animator.StringToHash(blendParameterName);
    }

    private void Update()
    {
        if (animator == null || playerMovement == null)
        {
            return;
        }

        animator.SetFloat(blendHash, playerMovement.CurrentSpeed, blendDampTime, Time.deltaTime);
        UpdateAttackState();
        PlayPendingAttack();
    }

    private void OnDisable()
    {
        wasInAttackState = false;
        currentComboIndex = 0;
    }

    private void UpdateAttackState()
    {
        bool isInAttackState = IsInAttackState();

        if (wasInAttackState && !isInAttackState && playerAttack != null && currentComboIndex > 0)
        {
            playerAttack.NotifyAttackAnimationCompleted(currentComboIndex);
            currentComboIndex = 0;
        }

        wasInAttackState = isInAttackState;
    }

    private void PlayPendingAttack()
    {
        if (playerAttack == null || IsInAttackState())
        {
            return;
        }

        if (!playerAttack.TryConsumeAttackAnimationRequest(out int comboIndex))
        {
            return;
        }

        if (!playerAttack.TryGetAttackAnimationState(comboIndex, out string stateName))
        {
            Debug.LogWarning($"No attack animation state mapped for combo {comboIndex}.", this);
            return;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, stateHash))
        {
            Debug.LogWarning($"Attack animation state was not found: {stateName}", this);
            return;
        }

        animator.CrossFadeInFixedTime(stateHash, attackTransitionDuration, 0, 0f);

        currentComboIndex = comboIndex;
        playerAttack.NotifyAttackAnimationStarted(comboIndex);
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
}
