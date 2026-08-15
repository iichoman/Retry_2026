using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class GameUIController : MonoBehaviour
{
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private EquipmentUI equipmentUI;
    [SerializeField] private LootUI lootUI;
    [SerializeField] private InputActionReference cancelAction;
    [SerializeField] private bool showCursorWhenUiOpen = true;
    [SerializeField] private CursorLockMode gameplayCursorLockMode = CursorLockMode.Locked;

    private bool inventoryOpen;
    private bool lootOpen;
    private NetworkBootstrap bootstrap;   // 실제 세션 플레이어 판별용

    public static GameUIController Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }

        ResolveReferences();
        GameUIVisualPolish.ApplyTo(GetComponentInParent<Canvas>());
        EnsurePremiumHud();
        EnsureMinimap();
        SetInventoryVisible(false);
        SetLootVisible(false);
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (playerInput != null)
        {
            playerInput.InventoryPressed += ToggleInventory;
        }

        if (lootUI != null)
        {
            lootUI.Closed += HandleLootClosed;
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            cancelAction.action.performed += HandleCancelPerformed;
            cancelAction.action.Enable();
        }
    }

    // 플레이어는 세션 접속 시 런타임에 생성되므로 OnEnable 시점엔 씬에 없을 수 있다.
    // (그때 playerInput=null이면 구독이 영영 안 됨)
    // 또 로비 디오라마 캐릭터의 Defalult_Input이 켜진 채 남아 있어 그쪽을 잘못 잡기도 한다.
    // → 매 프레임 올바른 대상인지 확인하고, 바뀌었으면 구독을 옮긴다.
    private void Update()
    {
        ApplySessionVisibility();

        var desired = FindPlayerInput();
        if (desired == null || desired == playerInput) return;

        if (playerInput != null)
        {
            playerInput.InventoryPressed -= ToggleInventory;
        }

        playerInput = desired;
        playerInput.InventoryPressed += ToggleInventory;
    }

    // 인게임 세션에 들어와 있는지. (로비/타이틀에서는 false)
    private bool InSession()
    {
        if (bootstrap == null)
        {
            bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        }
        var id = bootstrap != null ? bootstrap.Identity : null;
        return id != null && id.IsConnectedToSession;
    }

    // HUD(HP/스태미나)와 미니맵은 세션에 입장했을 때만 표시.
    // 인벤토리도 로비에서는 열리지 않게 하고, 로비로 나가면 강제로 닫는다.
    private void ApplySessionVisibility()
    {
        bool inSession = InSession();

        if (hudRoot == null || minimapRoot == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var hud = canvas.GetComponentInChildren<GameHUDOverlay>(true);
                if (hud != null) hudRoot = hud.gameObject;
                var mini = canvas.GetComponentInChildren<GameMinimapUI>(true);
                if (mini != null) minimapRoot = mini.gameObject;
            }
        }

        if (hudRoot != null && hudRoot.activeSelf != inSession) hudRoot.SetActive(inSession);
        if (minimapRoot != null && minimapRoot.activeSelf != inSession) minimapRoot.SetActive(inSession);

        if (!inSession && IsAnyUiOpen())
        {
            CloseAll();
        }
    }

    private GameObject hudRoot;
    private GameObject minimapRoot;

    // 실제 세션 로컬 플레이어를 최우선. 없으면 씬에서 활성화된 것 아무거나(단독 실행 대비).
    private Defalult_Input FindPlayerInput()
    {
        if (bootstrap == null)
        {
            bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        }

        if (bootstrap != null && bootstrap.LocalPlayer != null)
        {
            var real = bootstrap.LocalPlayer.GetComponent<Defalult_Input>();
            if (real != null) return real;
        }

        var all = FindObjectsByType<Defalult_Input>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].isActiveAndEnabled) return all[i];
        }
        return null;
    }

    private void OnDisable()
    {
        if (playerInput != null)
        {
            playerInput.InventoryPressed -= ToggleInventory;
        }

        if (lootUI != null)
        {
            lootUI.Closed -= HandleLootClosed;
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            cancelAction.action.performed -= HandleCancelPerformed;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ToggleInventory()
    {
        Debug.Log($"ToggleInventory requested. inventoryOpen={inventoryOpen}, lootOpen={lootOpen}", this);

        if (!InSession())
        {
            // 로비/타이틀에서는 인벤토리를 열지 않는다.
            return;
        }

        if (lootOpen)
        {
            CloseAll();
            return;
        }

        SetInventoryOpen(!inventoryOpen);
    }

    public void SetInventoryOpen(bool open)
    {
        Debug.Log($"SetInventoryOpen({open}). inventoryUI={inventoryUI != null}, equipmentUI={equipmentUI != null}", this);

        inventoryOpen = open;
        SetInventoryVisible(open);
        RefreshInputMode();
    }

    public bool OpenLoot(LootContainer container, PlayerInventory targetInventory)
    {
        if (lootUI == null || !lootUI.Open(container, targetInventory))
        {
            return false;
        }

        lootOpen = true;
        inventoryOpen = true;
        SetInventoryVisible(true);
        BringToFront(lootUI);
        RefreshInputMode();
        return true;
    }

    public void CloseLoot()
    {
        if (lootUI != null)
        {
            lootUI.Close();
        }

        lootOpen = false;
        RefreshInputMode();
    }

    public void CloseAll()
    {
        inventoryOpen = false;
        lootOpen = false;
        SetInventoryVisible(false);
        SetLootVisible(false);
        RefreshInputMode();
    }

    public void OnCancel(InputAction.CallbackContext context)
    {
        if (context.performed && IsAnyUiOpen())
        {
            CloseAll();
        }
    }

    private void HandleCancelPerformed(InputAction.CallbackContext context)
    {
        if (IsAnyUiOpen())
        {
            CloseAll();
        }
    }

    private void HandleLootClosed()
    {
        lootOpen = false;
        inventoryOpen = false;
        SetInventoryVisible(false);
        RefreshInputMode();
    }

    private void RefreshInputMode()
    {
        bool uiOpen = IsAnyUiOpen();

        if (playerInput != null)
        {
            playerInput.SetInputMode(uiOpen ? PlayerInputMode.UI : PlayerInputMode.Gameplay);
        }

        if (showCursorWhenUiOpen)
        {
            // 이 프로젝트는 커서 잠금을 쓰지 않는다(마우스 룩 상시, 로비/일시정지 UI 클릭 필요).
            // 기존엔 UI를 닫을 때 gameplayCursorLockMode(Locked)를 적용해서
            // 로비에서 인벤토리를 열었다 닫으면 마우스가 사라지는 버그가 있었다.
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void SetInventoryVisible(bool visible)
    {
        if (inventoryUI != null)
        {
            inventoryUI.SetVisible(visible);
        }

        if (equipmentUI != null)
        {
            equipmentUI.SetVisible(visible);
        }

        if (visible)
        {
            BringToFront(inventoryUI);
            BringToFront(equipmentUI);
        }
    }

    private void SetLootVisible(bool visible)
    {
        if (!visible && lootUI != null)
        {
            lootUI.Close();
        }
    }

    private bool IsAnyUiOpen()
    {
        return inventoryOpen || lootOpen;
    }

    private void ResolveReferences()
    {
        if (playerInput == null)
        {
            playerInput = FindPlayerInput();
        }

        if (inventoryUI == null)
        {
            inventoryUI = FindFirstObjectByType<InventoryUI>();
        }

        if (equipmentUI == null)
        {
            equipmentUI = FindFirstObjectByType<EquipmentUI>();
        }

        if (lootUI == null)
        {
            lootUI = FindFirstObjectByType<LootUI>(FindObjectsInactive.Include);
        }
    }

    private void EnsurePremiumHud()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
        {
            return;
        }

        if (canvas.GetComponentInChildren<GameHUDOverlay>(true) != null)
        {
            canvas.GetComponentInChildren<GameHUDOverlay>(true).transform.SetAsLastSibling();
            return;
        }

        GameObject hudObject = new GameObject("Premium Game HUD", typeof(RectTransform), typeof(GameHUDOverlay));
        RectTransform rect = hudObject.GetComponent<RectTransform>();
        hudObject.transform.SetParent(canvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        hudObject.transform.SetAsLastSibling();
    }

    private void EnsureMinimap()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
        {
            return;
        }

        GameMinimapUI existingMinimap = canvas.GetComponentInChildren<GameMinimapUI>(true);
        if (existingMinimap != null)
        {
            existingMinimap.transform.SetAsLastSibling();
            return;
        }

        GameObject minimapObject = new GameObject("Game Minimap HUD", typeof(RectTransform), typeof(GameMinimapUI));
        RectTransform rect = minimapObject.GetComponent<RectTransform>();
        minimapObject.transform.SetParent(canvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        minimapObject.transform.SetAsLastSibling();
    }

    private static void BringToFront(Component component)
    {
        if (component != null)
        {
            component.transform.SetAsLastSibling();
        }
    }
}