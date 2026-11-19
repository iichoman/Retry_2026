using UnityEngine;

// One owner for the cursor; lobby preview characters must never capture it.
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class GameplayCursorController : MonoBehaviour
{
    public static GameplayCursorController Active { get; private set; }
    public bool WantsCapture { get; private set; }
    NetworkBootstrap bootstrap;
    GameObject owner;
    Defalult_Input input;
    Player_State state;
    bool focused, paused;

    void Awake()
    {
        Active = this;
        bootstrap = GetComponent<NetworkBootstrap>();
        focused = Application.isFocused;
    }
    void OnEnable() => Refresh();
    void LateUpdate() => Refresh();
    void OnApplicationFocus(bool value) { focused = value; Refresh(); }
    void OnApplicationPause(bool value) { paused = value; Refresh(); }
    void OnDisable() { if (Active == this) Apply(false); }
    void OnDestroy() { if (Active == this) { Apply(false); Active = null; } }

    public void Refresh()
    {
        var local = bootstrap != null ? bootstrap.LocalPlayer : null;
        if (owner != local)
        {
            owner = local;
            input = owner != null ? owner.GetComponent<Defalult_Input>() : null;
            state = owner != null ? owner.GetComponent<Player_State>() : null;
        }
        bool playing = bootstrap != null && bootstrap.Identity != null
            && bootstrap.Identity.IsConnectedToSession && bootstrap.Session != null && bootstrap.Session.IsConnected && bootstrap.SessionGameStarted
            && owner != null && owner.activeInHierarchy && input != null && input.isActiveAndEnabled
            && input.IsGameplayInputEnabled && (state == null || !state.IsDead);
        bool panelOpen = PauseMenu.IsOpen || GameOptions.IsOpen || ChatUI.IsTyping
            || EconomyClient.Active?.IsOpen == true || ServerLootUI.Active != null
            || GameUIController.Instance?.HasOpenPanels == true
            || ExpeditionUI.Active?.InventoryOpen == true || ExpeditionUI.Active?.UpgradeOpen == true;
        Apply(isActiveAndEnabled && focused && !paused && playing && !panelOpen);
    }

    void Apply(bool capture)
    {
        if (WantsCapture != capture) input?.ResetLookInput();
        WantsCapture = capture;
        var mode = capture ? CursorLockMode.Locked : CursorLockMode.None;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        if (Cursor.visible == capture) Cursor.visible = !capture;
    }
}
