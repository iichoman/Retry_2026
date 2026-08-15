using UnityEngine;

[DisallowMultipleComponent]
public class Player_Animation : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Player_Movement playerMovement;
    [SerializeField] private Player_Attack playerAttack;
    [SerializeField] private Player_State playerState;
    [SerializeField] private string blendParameterName = "speed";
    [SerializeField] private string crouchWalkStateName = "CrouchWalk";
    [SerializeField] private string crouchAnimSpeedParameterName = "crouchAnimSpeed";
    [SerializeField, Min(0f)] private float blendDampTime = 0.1f;
    [SerializeField, Min(0f)] private float crouchTransitionDuration = 0.08f;
    [SerializeField, Min(0f)] private float crouchMoveThreshold = 0.05f;
    [SerializeField, Min(0f)] private float attackTransitionDuration = 0.05f;
    [SerializeField, Min(0f)] private float hitTransitionDuration = 0.03f;
    [SerializeField] private string attackStateTag = "Attack";
    [SerializeField] private string hitStateTag = "Hit";

    private int blendHash;
    private int crouchAnimSpeedHash;
    private int crouchWalkStateHash;
    private int locomotionStateHash;
    private bool isPlayingCrouch;
    private bool wasInAttackState;
    private bool wasInHitState;
    private int currentComboIndex;

    public bool IsAttacking => animator != null && IsInAttackState();
    public bool IsHit => animator != null && IsInHitState();

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

        if (playerState == null)
        {
            playerState = GetComponent<Player_State>();
        }

        blendHash = Animator.StringToHash(blendParameterName);
        crouchAnimSpeedHash = Animator.StringToHash(crouchAnimSpeedParameterName);
        crouchWalkStateHash = Animator.StringToHash(crouchWalkStateName);
        locomotionStateHash = Animator.StringToHash("Blend Tree");
    }

    private void Update()
    {
        if (animator == null || playerMovement == null)
        {
            return;
        }

        animator.SetFloat(blendHash, playerMovement.CurrentSpeed, blendDampTime, Time.deltaTime);
        UpdateCrouchState();
        UpdateHitState();
        PlayPendingHit();

        if ((playerState != null && (playerState.IsDead || playerState.IsHit)) || IsInHitState())
        {
            return;
        }

        UpdateAttackState();
        PlayPendingAttack();
    }

    private void OnDisable()
    {
        isPlayingCrouch = false;
        wasInAttackState = false;
        wasInHitState = false;
        currentComboIndex = 0;
    }

    private void UpdateCrouchState()
    {
        if (playerMovement.IsCrouching)
        {
            bool hasCrouchState = animator.HasState(0, crouchWalkStateHash);
            if (!hasCrouchState)
            {
                return;
            }

            float crouchAnimSpeed = playerMovement.CurrentSpeed > crouchMoveThreshold ? 1f : 0f;
            animator.SetFloat(crouchAnimSpeedHash, crouchAnimSpeed);

            if (!isPlayingCrouch && !IsInAttackState() && !IsInHitState())
            {
                animator.CrossFadeInFixedTime(crouchWalkStateHash, crouchTransitionDuration, 0, 0f);
                isPlayingCrouch = true;
            }

            return;
        }

        if (!isPlayingCrouch)
        {
            return;
        }

        animator.SetFloat(crouchAnimSpeedHash, 1f);
        if (animator.HasState(0, locomotionStateHash))
        {
            animator.CrossFadeInFixedTime(locomotionStateHash, crouchTransitionDuration, 0, 0f);
        }

        isPlayingCrouch = false;
    }

    private void UpdateHitState()
    {
        bool isInHitState = IsInHitState();

        if (wasInHitState && !isInHitState && playerState != null)
        {
            playerState.NotifyHitAnimationCompleted();
        }

        wasInHitState = isInHitState;
    }

    private void PlayPendingHit()
    {
        if (playerState == null)
        {
            return;
        }

        if (!playerState.TryConsumeHitAnimationRequest(out string stateName))
        {
            return;
        }

        if (!TryResolveStateHash(stateName, out int stateHash))
        {
            string controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "None";
            Debug.LogWarning(
                $"Hit animation state was not found: {stateName} (controller: {controllerName})",
                this
            );
            playerState.NotifyHitAnimationUnavailable();
            return;
        }

        if (currentComboIndex > 0 && playerAttack != null)
        {
            playerAttack.NotifyAttackAnimationCompleted(currentComboIndex);
            currentComboIndex = 0;
        }

        isPlayingCrouch = false;
        playerAttack?.CancelCurrentAttack();
        animator.CrossFadeInFixedTime(stateHash, hitTransitionDuration, 0, 0f);
        playerState.NotifyHitAnimationStarted();
        wasInHitState = true;
        wasInAttackState = false;
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
        if (playerAttack == null)
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

        if (!TryResolveStateHash(stateName, out int stateHash))
        {
            string controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "None";
            Debug.LogWarning(
                $"Attack animation state was not found: {stateName} (controller: {controllerName})",
                this
            );
            return;
        }

        if (comboIndex == currentComboIndex && IsInAttackState())
        {
            return;
        }

        animator.CrossFadeInFixedTime(stateHash, attackTransitionDuration, 0, 0f);

        isPlayingCrouch = false;
        currentComboIndex = comboIndex;
        playerAttack.NotifyAttackAnimationStarted(comboIndex);
        wasInAttackState = true;
    }

    private bool TryResolveStateHash(string requestedStateName, out int stateHash)
    {
        stateHash = 0;

        if (animator == null || string.IsNullOrWhiteSpace(requestedStateName))
        {
            return false;
        }

        stateHash = Animator.StringToHash(requestedStateName);
        if (animator.HasState(0, stateHash))
        {
            return true;
        }

        int lastSeparatorIndex = requestedStateName.LastIndexOf('.');
        if (lastSeparatorIndex < 0 || lastSeparatorIndex >= requestedStateName.Length - 1)
        {
            return false;
        }

        string shortStateName = requestedStateName.Substring(lastSeparatorIndex + 1);
        stateHash = Animator.StringToHash(shortStateName);
        return animator.HasState(0, stateHash);
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
}
