using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputKey = UnityEngine.InputSystem.Key;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GameplayCursorValidation
{
    const string Key = "Retry.CursorValidation", Output = "Artifacts/StoneWalls/cursor-validation.txt";
    static IEnumerator routine;
    static int frame;
    static double began;
    static string failure;
    static NetworkBootstrap bootstrap;
    static System.Net.Sockets.TcpListener listener;
    static System.Net.Sockets.TcpClient peer;
    static Keyboard keyboard;
    static bool nativeLockObserved;
    static InputSettings originalInputSettings, fixtureInputSettings;
    static float resumeAt;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GameplayCursorValidation() { EditorApplication.playModeStateChanged += Mode; EditorApplication.update += Tick; }
    public static void Batch()
    {
        if (!Application.isBatchMode) throw new Exception("Run in the isolated batch project.");
        Directory.CreateDirectory("Artifacts/StoneWalls"); File.WriteAllText(Output, "RUNNING\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.playModeStartScene = null;
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { began = EditorApplication.timeSinceStartup; frame = -1; routine = Run(); }
        if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key, false); EditorApplication.Exit(failure == null ? 0 : 1); }
    }
    static void Tick()
    {
        if (routine == null || !EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 60) throw new TimeoutException();
            if (Time.unscaledTime < resumeAt) return;
            if (routine.MoveNext()) { resumeAt = Time.unscaledTime + (routine.Current is float delay ? delay : 0); return; }
        }
        catch (Exception e) { failure = e.ToString(); Debug.LogException(e); }
        routine = null;
        bootstrap?.Session?.Disconnect(); peer?.Close(); listener?.Stop();
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (originalInputSettings != null) InputSystem.settings = originalInputSettings;
        if (fixtureInputSettings != null) Object.Destroy(fixtureInputSettings);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        File.AppendAllText(Output, failure == null ? "PASS all cursor/UI and skill-direction checks; native lock observed=" + nativeLockObserved + ". Isolated local connection, no game server used.\n" : "FAIL " + failure);
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); File.AppendAllText(Output, "PASS " + message + "\n"); }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static void Property(object target, string name, object value) => target.GetType().GetProperty(name).GetSetMethod(true).Invoke(target, new[] { value });
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Press(params InputKey[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

    static IEnumerator Run()
    {
        Application.runInBackground = true;
        // Route synthetic keys to the hidden Game View without changing the project's input asset.
        originalInputSettings = InputSystem.settings;
        fixtureInputSettings = Object.Instantiate(originalInputSettings);
        fixtureInputSettings.hideFlags = HideFlags.DontSave;
        InputSystem.settings = fixtureInputSettings;
        fixtureInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        fixtureInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var camera = new GameObject("Aim camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        bootstrap = new GameObject("Cursor test bootstrap").AddComponent<NetworkBootstrap>(); bootstrap.enabled = false; bootstrap.DebugUI.enabled = false;
        var cursor = bootstrap.GetComponent<GameplayCursorController>();
        Check(cursor != null, "normal bootstrap installs cursor owner without prefab/scene edits");
        var player = new GameObject("Local input owner");
        var input = player.AddComponent<Defalult_Input>(); var inventory = player.AddComponent<PlayerInventory>(); var health = player.AddComponent<Player_State>();
        var legacy = new GameObject("Inventory adapter").AddComponent<InventoryUI>(); legacy.RebindOwner(inventory, null);
        var ui = new GameObject("Game UI").AddComponent<GameUIController>();
        Set(ui, "inventoryUI", legacy); Set(ui, "playerInput", input); Set(ui, "bootstrap", bootstrap);
        var pause = new GameObject("Pause menu").AddComponent<PauseMenu>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        yield return null; yield return null;
        Call(cursor, "OnApplicationFocus", true);
        void Expect(bool capture, string message)
        {
            cursor.Refresh(); Check(cursor.WantsCapture == capture, message);
            if (capture) nativeLockObserved |= Cursor.lockState == CursorLockMode.Locked && !Cursor.visible;
            else Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, message + " (visible/unlocked)");
        }
        Expect(false, "lobby never captures mouse");
        listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Check(bootstrap.Session.ConnectAndAuth("127.0.0.1", port, 923, 11), "isolated session connection");
        peer = listener.AcceptTcpClient();
        bootstrap.Identity.IsConnectedToSession = true;
        Property(bootstrap, "LocalPlayer", player); ui.RebindPlayerInput();
        Expect(false, "loading/waiting for start keeps mouse free");
        Property(bootstrap, "SessionGameStarted", true); Expect(true, "session start captures mouse");

        Press(InputKey.I); yield return .1f; Press(); yield return .1f;
        Check(ExpeditionUI.Active.InventoryOpen, "I opens actual inventory"); Expect(false, "inventory releases mouse");
        Press(InputKey.Escape); yield return .1f; Press(); yield return .1f;
        Check(!ExpeditionUI.Active.InventoryOpen && !PauseMenu.IsOpen, "ESC closes inventory without also opening pause");
        Expect(true, "closing inventory resumes capture");
        Press(InputKey.Escape); yield return .1f; Press(); yield return .1f;
        Check(PauseMenu.IsOpen && !input.IsGameplayInputEnabled, "ESC menu blocks gameplay including skill input"); Expect(false, "pause releases mouse");
        ui.SetInventoryOpen(true); ui.SetInventoryOpen(false);
        Expect(false, "closing another panel cannot steal cursor from pause");
        GameOptions.Open(); Expect(false, "settings over pause keep mouse free");
        var options = bootstrap.GetComponent<GameOptions>(); Call(options, "Close");
        Expect(false, "closing settings leaves pause in control");
        Press(InputKey.Escape); yield return .1f; Press(); yield return .1f;
        Check(!PauseMenu.IsOpen && input.IsGameplayInputEnabled, "ESC resumes gameplay"); Expect(true, "resume captures mouse again");

        var loot = new GameObject("Loot panel").AddComponent<ServerLootUI>();
        loot.Open(null, null, () => input.SetInputMode(PlayerInputMode.Gameplay)); input.SetInputMode(PlayerInputMode.UI);
        Expect(false, "loot releases mouse"); loot.Close(); Expect(true, "loot close resumes capture");
        input.SetInputMode(PlayerInputMode.UI); Expect(false, "chat/UI input mode releases mouse");
        input.SetInputMode(PlayerInputMode.Gameplay); Expect(true, "gameplay mode resumes capture");
        Call(cursor, "OnApplicationFocus", false); Expect(false, "focus loss releases mouse");
        Call(cursor, "OnApplicationFocus", true); Expect(true, "focus return respects gameplay state");
        ui.SetInventoryOpen(true); Call(cursor, "OnApplicationFocus", false); Call(cursor, "OnApplicationFocus", true);
        Expect(false, "focus return never locks over open inventory"); ui.SetInventoryOpen(false);

        foreach(float yaw in new[]{0f,90f,180f,270f})foreach(var mode in new[]{CursorLockMode.Locked,CursorLockMode.None})
        {
            Cursor.lockState = mode; camera.transform.rotation = Quaternion.Euler(40, yaw, 0);
            var expected = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Check(Vector3.Dot(SkillClient.AimDirection(player.transform, camera), expected) > .999f, "skill follows view yaw " + yaw + " with " + mode);
        }
        health.NetworkDie(); Expect(false, "death releases mouse");
        Property(bootstrap, "LocalPlayer", null); bootstrap.Identity.IsConnectedToSession = false;
        ui.RefreshCurrentInputMode(); Expect(false, "returning to lobby cannot recapture mouse");
        EconomyClient.Open(); Expect(false, "lobby shop leaves mouse usable"); EconomyClient.Active.Close();
        Expect(false, "closing lobby shop leaves mouse usable");
        Object.Destroy(loot.gameObject);
    }
}
