using UnityEngine;


[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Defalult_Input))]
public class Player_Movement : MonoBehaviour
{
    private CharacterController CController;
    private Defalult_Input playerInput;
    private Player_Animation playerAnimation;
    private Player_State playerState;

    // Move Properties
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintSpeed = 10f;
    [SerializeField] private float crouchSpeed = 3f;
    [SerializeField] private float jumpSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;

    // 카메라 지정
    [SerializeField] private Transform cameraTransform;

    private float verticalVelocity;
    public float CurrentSpeed { get; private set; }
    public bool IsCrouching { get; private set; }

    private void Awake()
    {
        CController = GetComponent<CharacterController>();
        playerInput = GetComponent<Defalult_Input>();
        playerAnimation = GetComponent<Player_Animation>();
        playerState = GetComponent<Player_State>();
    }
    private void Update()
    {
        // Requirment Check
        if (playerInput == null || CController == null)
        {
            return;
        }


        // 플레이어 움직임
        Vector2 input = playerInput.Move;
        Vector3 move = GetMoveDirection(input);

        if ((playerAnimation != null && playerAnimation.IsAttacking)
            || (playerState != null && (playerState.IsDead || playerState.IsHit)))
        {
            move = Vector3.zero;
        }

        bool canCrouch = CController.isGrounded && (playerState == null || (!playerState.IsDead && !playerState.IsHit));
        IsCrouching = playerInput.Crouch && canCrouch;

        float targetSpeed = IsCrouching ? crouchSpeed : playerInput.Sprint ? sprintSpeed : walkSpeed;
        Vector3 horizontal = move * targetSpeed;
        CurrentSpeed = horizontal.magnitude;

        if (CController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (!IsCrouching && playerInput.Jump && CController.isGrounded && (playerState == null || (!playerState.IsDead && !playerState.IsHit)))
        {
            verticalVelocity = jumpSpeed;
        }
        verticalVelocity += gravity * Time.deltaTime;
        Vector3 velocity = new Vector3(horizontal.x, verticalVelocity, horizontal.z);
        CController.Move(velocity * Time.deltaTime);

        // 플레이어 회전
        if (move.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.deltaTime);
        }

    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        // cameraTransform가 비어있으면(프리팹에 카메라 없어 할당 불가) 런타임에 메인 카메라 사용.
        // null이면 자기 자신 기준이 되어 A/D가 제자리 회전하는 버그가 생김 → 메인 카메라로 고정.
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        // 현재 카메라 방향을 기준으로 움직임
        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            right = cameraTransform.right;
        }

        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 direction = forward * input.y + right * input.x;
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }
        return direction;
    }
}