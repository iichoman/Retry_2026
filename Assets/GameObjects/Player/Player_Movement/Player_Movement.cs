using UnityEngine;


[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Defalult_Input))]
public class Player_Movement : MonoBehaviour
{
    private CharacterController CController;
    private Defalult_Input playerInput;
    private Player_Animation playerAnimation;

    // Move Properties
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintSpeed = 10f;
    [SerializeField] private float jumpSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;

    // 카메라 지정
    [SerializeField] private Transform cameraTransform;

    private float verticalVelocity;
    public float CurrentSpeed { get; private set; }

    private void Awake()
    {
        CController = GetComponent<CharacterController>();
        playerInput = GetComponent<Defalult_Input>();
        playerAnimation = GetComponent<Player_Animation>();
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

        if (playerAnimation != null && playerAnimation.IsAttacking)
        {
            move = Vector3.zero;
        }

        float targetSpeed = playerInput.Sprint ? sprintSpeed : walkSpeed;
        Vector3 horizontal = move * targetSpeed;
        CurrentSpeed = horizontal.magnitude;
        
        if(CController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (playerInput.Jump && CController.isGrounded)
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
