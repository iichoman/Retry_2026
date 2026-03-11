using UnityEngine;

[DisallowMultipleComponent]
public class Player_Animation : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Player_Movement playerMovement;
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private string blendParameterName = "speed"; // float speed값에 따라 blend 
    [SerializeField, Min(0f)] private float blendDampTime = 0.1f;
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string attackStateTag = "Attack";  // Trigger Attack 

    private int blendHash;
    private int attackTriggerHash;
    private bool previousAttackInput;
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

        if (playerInput == null)
        {
            playerInput = GetComponent<Defalult_Input>();
        }

        blendHash = Animator.StringToHash(blendParameterName);
        attackTriggerHash = Animator.StringToHash(attackTriggerName);
    }

    private void Update()
    {
        if (animator == null || playerMovement == null)
        {
            return;
        }

        animator.SetFloat(blendHash, playerMovement.CurrentSpeed, blendDampTime, Time.deltaTime);
        HandleAttackTrigger();
    }

    private void HandleAttackTrigger()
    {
        bool currentAttackInput = playerInput != null && playerInput.Attack;
        bool attackPressedThisFrame = currentAttackInput && !previousAttackInput;
        previousAttackInput = currentAttackInput;

        if (!attackPressedThisFrame)
        {
            return;
        }

        if (IsInAttackState())
        {
            return;
        }

        animator.SetTrigger(attackTriggerHash);
    }

    private bool IsInAttackState() // 공격중인지 확인
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
