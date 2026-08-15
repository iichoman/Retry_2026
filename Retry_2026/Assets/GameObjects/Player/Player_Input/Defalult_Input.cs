using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerInputMode
{
    Gameplay,
    UI
}

[DisallowMultipleComponent]
public class Defalult_Input : MonoBehaviour
{
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool sprint;
    private bool crouch;

    public bool Attack { get; private set; }
    public bool Interact { get; private set; }
    public bool LockOn { get; private set; }
    public bool Inventory { get; private set; }
    public bool Jump { get; private set; }
    public PlayerInputMode InputMode { get; private set; } = PlayerInputMode.Gameplay;
    public bool IsGameplayInputEnabled => InputMode == PlayerInputMode.Gameplay;

    public event Action HandleLockon;
    public event Action InventoryPressed;

    // ── Inventory 입력 안전장치 ────────────────────────────────────
    // 이 프로젝트의 PlayerInput은 Behavior=InvokeUnityEvents 이고,
    // 이벤트가 "액션 GUID"로만 연결된다. 액션을 다시 만들면 GUID가 바뀌어
    // 프리팹의 기존 연결과 어긋나고, 그러면 아무 로그도 없이 조용히 죽는다.
    // 게다가 PlayerInput의 Default Action Map이 비어 있어 맵이 자동으로 켜지지도 않는다.
    // → GUID가 아니라 "이름"으로 직접 연결하고, 액션을 명시적으로 Enable 한다.
    //   그래도 액션 자체가 없으면 i 키 직접 폴링으로 폴백한다.
    private InputAction inventoryAction;
    private int lastInventoryFrame = -1;   // UnityEvent와 중복 발화 방지

    private void Awake()
    {
        var pi = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (pi != null && pi.actions != null)
        {
            inventoryAction = pi.actions.FindAction("Player/Inventory", false)
                           ?? pi.actions.FindAction("Inventory", false);
        }

        if (inventoryAction != null)
        {
            inventoryAction.performed += HandleInventoryPerformed;
            inventoryAction.Enable();   // Default Action Map이 비어 있어도 확실히 켠다
        }
        else
        {
            Debug.LogWarning("[Defalult_Input] Inventory 액션을 찾지 못했습니다. " +
                             "InputSystem_Actions의 Player 맵에 Inventory(<Keyboard>/i)를 추가하세요. " +
                             "지금은 i 키 직접 입력으로 동작합니다.", this);
        }
    }

    private void OnDestroy()
    {
        if (inventoryAction != null)
        {
            inventoryAction.performed -= HandleInventoryPerformed;
        }
    }

    private void Update()
    {
        // 액션이 없을 때만 쓰는 폴백. (액션이 있으면 위 경로로 이미 처리됨)
        if (inventoryAction != null) return;

        var kb = Keyboard.current;
        if (kb != null && kb.iKey.wasPressedThisFrame)
        {
            FireInventory();
        }
    }

    private void HandleInventoryPerformed(InputAction.CallbackContext _) => FireInventory();

    // 어느 경로로 들어오든 한 프레임에 한 번만 발화
    private void FireInventory()
    {
        if (lastInventoryFrame == Time.frameCount) return;
        lastInventoryFrame = Time.frameCount;

        Debug.Log("Inventory input performed.", this);
        InventoryPressed?.Invoke();
    }

    public Vector2 Move => IsGameplayInputEnabled ? moveInput : Vector2.zero;
    public Vector2 Look => IsGameplayInputEnabled ? lookInput : Vector2.zero;
    public bool Sprint => IsGameplayInputEnabled && sprint;
    public bool Crouch => IsGameplayInputEnabled && crouch;

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            lookInput = Vector2.zero;
            return;
        }

        lookInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            Jump = false;
            return;
        }

        Jump = context.ReadValueAsButton();
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            sprint = false;
            return;
        }

        sprint = context.ReadValueAsButton();
    }

    public void OnCrouch(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            crouch = false;
            return;
        }

        crouch = context.ReadValueAsButton();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            Attack = false;
            return;
        }

        Attack = context.ReadValueAsButton();
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            Interact = false;
            return;
        }

        Interact = context.ReadValueAsButton();
    }

    public void OnLockon(InputAction.CallbackContext context)
    {
        if (!IsGameplayInputEnabled)
        {
            LockOn = false;
            return;
        }

        LockOn = context.ReadValueAsButton();

        if (context.performed)
        {
            Debug.Log("LockOn input performed.", this);
            HandleLockon?.Invoke();
        }
    }

    // PlayerInput의 UnityEvent가 연결돼 있으면 이 경로로도 들어온다(중복은 FireInventory가 차단).
    public void OnInventory(InputAction.CallbackContext context)
    {
        Inventory = context.ReadValueAsButton();

        if (context.performed)
        {
            FireInventory();
        }
    }

    public void SetInputMode(PlayerInputMode mode)
    {
        if (InputMode == mode)
        {
            return;
        }

        InputMode = mode;

        if (!IsGameplayInputEnabled)
        {
            ClearGameplayInput();
        }
    }

    private void ClearGameplayInput()
    {
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        sprint = false;
        crouch = false;
        Jump = false;
        Attack = false;
        Interact = false;
        LockOn = false;
    }
}