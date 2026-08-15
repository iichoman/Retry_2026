using UnityEngine;

public class InventoryInputController : MonoBehaviour
{
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private bool showCursorWhenOpen = true;
    [SerializeField] private CursorLockMode gameplayCursorLockMode = CursorLockMode.Locked;

    private bool isOpen;
    private bool subscribed;

    private void Awake()
    {
        if (playerInput == null)
        {
            playerInput = FindFirstObjectByType<Defalult_Input>();
        }

        if (inventoryUI == null)
        {
            inventoryUI = FindFirstObjectByType<InventoryUI>();
        }
    }

    private void OnEnable()
    {
        if (GameUIController.Instance != null)
        {
            return;
        }

        if (playerInput != null)
        {
            playerInput.InventoryPressed += ToggleInventory;
            subscribed = true;
        }
    }

    private void Start()
    {
        if (GameUIController.Instance != null)
        {
            Unsubscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (playerInput != null)
        {
            playerInput.InventoryPressed -= ToggleInventory;
        }

        subscribed = false;
    }

    public void ToggleInventory()
    {
        if (GameUIController.Instance != null)
        {
            return;
        }

        SetOpen(!isOpen);
    }

    public void SetOpen(bool open)
    {
        isOpen = open;

        if (inventoryUI != null)
        {
            inventoryUI.SetVisible(isOpen);
        }

        if (playerInput != null)
        {
            playerInput.SetInputMode(isOpen ? PlayerInputMode.UI : PlayerInputMode.Gameplay);
        }

        if (showCursorWhenOpen)
        {
            Cursor.visible = isOpen;
            Cursor.lockState = isOpen ? CursorLockMode.None : gameplayCursorLockMode;
        }
    }
}
