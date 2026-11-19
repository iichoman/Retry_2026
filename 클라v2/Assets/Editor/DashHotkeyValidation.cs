using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Client-only Play Mode checks with the real player prefab and native CharacterController.
/// The normal scene is closed before Play Mode; this runner never connects to a server.
/// </summary>
[InitializeOnLoad]
public static class DashHotkeyValidation
{
    private const string Key = "Retry.DashHotkeyValidation";
    private const string PrefabPath = "Assets/Prefabs/LocalPlayerPrefab.prefab";
    private static string Output => Path.GetFullPath("Artifacts/DashHotkeys/validation.txt");
    private static readonly List<string> checks = new();
    private static readonly List<string> errors = new();
    private static GameObject stage, player;
    private static GameUIController gameUi;
    private static NetworkBootstrap bootstrap;
    private static LocalIdentity identity;
    private static DebugCheatKeys cheats;
    private static DungeonGenerator_ChunkMesh dungeon;
    private static PlayerDefenseController defense;
    private static Player_State state;
    private static Player_Attack attack;
    private static Defalult_Input input;
    private static CharacterController capsule;
    private static IEnumerator routine;
    private static float resumeAt;
    private static double began;
    private static int lastFrame;
    private static bool finishing, priorOnline, priorBackground;
    private static float priorTimeScale;
    private static Keyboard syntheticKeyboard;
    [Serializable] private sealed class SavedScenes { public SavedScene[] scenes; }
    [Serializable] private sealed class SavedScene { public string path; public bool loaded; public bool active; }

    static DashHotkeyValidation()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        EditorApplication.update += Tick;
    }

    [MenuItem("Retry/Player Defense/Run Dash and Hotkey Validation")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key, false))
            throw new InvalidOperationException("Defense validation requires idle Edit Mode.");
        var current = EditorSceneManager.GetSceneManagerSetup();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
            if (scene.isDirty || string.IsNullOrEmpty(scene.path) && scene.GetRootGameObjects().Length > 0)
                throw new InvalidOperationException("Save the open scene before defense validation: " + scene.name +
                    ". User scene changes are never discarded or automatically saved.");
        }
        var saved = new SavedScenes { scenes = new SavedScene[current.Length] };
        for (int i = 0; i < current.Length; i++) saved.scenes[i] = new SavedScene {
            path = current[i].path, loaded = current[i].isLoaded, active = current[i].isActive };
        SessionState.SetString(Key + ".Scenes", JsonUtility.ToJson(saved));
        SessionState.SetBool(Key + ".Blank", current.Length == 1 && string.IsNullOrEmpty(current[0].path));
        SessionState.SetBool(Key + ".Passed", false);
        SessionState.SetString(Key + ".Report", "Interrupted before checks completed.");
        SessionState.SetBool(Key, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        File.WriteAllText(Output, "RUNNING\nActual player prefab, synthetic function keys, offline Play Mode, no network connection.\n");
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex)
        {
            SessionState.SetString(Key + ".Report", ex.ToString());
            RestoreAndReport();
            throw;
        }
    }

    private static void OnModeChanged(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            try { Setup(); }
            catch (Exception ex) { Finish(ex); }
        }
        else if (mode == PlayModeStateChange.EnteredEditMode)
        {
            Application.logMessageReceived -= OnLog;
            CleanupSyntheticInput();
            stage = player = null;
            routine = null;
            RestoreAndReport();
        }
    }

    private static void Setup()
    {
        checks.Clear(); errors.Clear(); finishing = false;
        began = EditorApplication.timeSinceStartup; resumeAt = 0; lastFrame = -1;
        priorOnline = NetworkRuntime.IsOnline;
        priorBackground = Application.runInBackground;
        priorTimeScale = Time.timeScale;
        Application.runInBackground = true;
        Time.timeScale = 1f;
        SetOnline(false);
        Application.logMessageReceived += OnLog;
        Check(UnityEngine.Object.FindObjectsByType<NetworkBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0,
            "Empty test scene has no NetworkBootstrap; no login/session connection is started");
        stage = new GameObject("Defense validation fixtures");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Native physics floor";
        ground.transform.SetParent(stage.transform);
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        int wallLayer = LayerMask.NameToLayer("Wall");
        ground.layer = wallLayer >= 0 ? wallLayer : 0;
        routine = RunChecks();
    }

    private static void NewPlayer(float height = 0.05f)
    {
        if (player != null) { player.SetActive(false); UnityEngine.Object.Destroy(player); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Missing real player prefab.");
        // Instantiate inactive so camera/UI/network components cannot run Start/OnEnable.
        var holder = new GameObject("Inactive fixture holder");
        holder.transform.SetParent(stage.transform); holder.SetActive(false);
        player = UnityEngine.Object.Instantiate(prefab, holder.transform);
        player.name = "Defense validation actual player";
        player.transform.SetPositionAndRotation(new Vector3(0f, height, 0f), Quaternion.identity);
        foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            bool required = behaviour is Defalult_Input || behaviour is Player_Movement || behaviour is Player_State ||
                behaviour is Player_Attack || behaviour is PlayerDefenseController || behaviour is PlayerDefensePose ||
                behaviour is Weapon_Sword || behaviour is Weapon_Gun || behaviour is WeaponHitbox;
            behaviour.enabled = required;
        }
        foreach (var animator in player.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
        defense = player.GetComponent<PlayerDefenseController>();
        capsule = player.GetComponent<CharacterController>();
        input = player.GetComponent<Defalult_Input>();
        attack = player.GetComponent<Player_Attack>();
        state = player.GetComponent<Player_State>();
        Check(defense != null && capsule != null && input != null && attack != null && state != null,
            "Actual player prefab has movement/input/state/attack/defense references");
        holder.SetActive(true);
        // Preserve the real input component's configured references, but prevent hardware events
        // from changing deterministic fixtures while direct action APIs are being exercised.
        var hardware = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (hardware != null && hardware.actions != null) hardware.actions.Disable();
        Physics.SyncTransforms();
    }

    private static IEnumerator RunChecks()
    {
        NewPlayer(); yield return .25f;
        Check(capsule.isGrounded, "Real prefab CharacterController is grounded");
        Check(Mathf.Approximately(defense.DashSpeed, 24f), "Shipped prefab uses faster offline dash speed 24 m/s");
        Check(typeof(PlayerDefenseController).GetMethod("OnGUI", BindingFlags.NonPublic | BindingFlags.Instance) == null,
            "Player defense component no longer draws dash instructions or notices");
        SetField(input, "sprint", true);
        Check(defense.TryStartDash(Vector3.right), "Unobstructed faster dash starts");
        Check(PlayerStamina.Local != null && PlayerStamina.Local.Current == 80,
            "Standalone player creates the existing stamina pool and spends exactly 20 of 100");
        Check(!defense.TryStartDash(Vector3.right) && PlayerStamina.Local.Current == 80,
            "Busy dash retry does not spend stamina twice");
        var move = player.GetComponent<Player_Movement>();
        float fastest = 0f;
        float until = Time.time + .5f;
        while (Time.time < until) { fastest = Mathf.Max(fastest, move.CurrentSpeed); yield return null; }
        Check(fastest >= 23.5f && fastest <= 24.5f && player.transform.position.x > 2f,
            "Native CharacterController displacement reaches 24 m/s during an unobstructed dash");
        Check(PlayerStamina.Local.Current == 80, "Holding Shift during dash adds no sprint cost or immediate regeneration");
        SetField(input, "sprint", false);
        NewPlayer(); yield return .25f;
        var wall = Box("Dash collision wall", new Vector3(0, 1.5f, 2), new Vector3(5, 3, .2f));
        Physics.SyncTransforms();
        Check(defense.TryStartDash(Vector3.forward), "Faster dash starts through real defense controller");
        yield return .5f;
        Check(player.transform.position.z > .25f && player.transform.position.z < 2f - capsule.radius,
            "24 m/s dash moves and stops before a physical wall");
        Check(capsule.isGrounded && player.transform.position.y < .2f, "Faster dash preserves floor contact and adds no jump");
        wall.SetActive(false);
        SetOnline(true);
        Check(defense.DashSpeed <= 10f, "Online dash stays within existing server movement speed cap");
        SetOnline(false);

        var stamina = PlayerStamina.Local;
        Check(stamina.Current == 80, "Wall cancellation does not refund or repeat the accepted dash cost");
        yield return .35f;
        SetProperty(stamina, "Current", 19);
        Check(!defense.TryStartDash(Vector3.right) && stamina.Current == 19, "Insufficient stamina refuses dash without charging");
        SetProperty(stamina, "Current", 20);
        Check(defense.TryStartDash(Vector3.right) && stamina.Current == 0, "Exactly one dash cost is sufficient and never makes stamina negative");
        defense.CancelDefense();
        Check(!defense.TryStartDash(Vector3.right) && stamina.Current == 0, "Cooldown rejection does not charge stamina");
        yield return .85f;
        SetProperty(stamina, "Current", 20);
        input.SetInputMode(PlayerInputMode.UI);
        Check(!defense.TryStartDash(Vector3.right) && stamina.Current == 20, "UI rejection does not charge stamina");
        input.SetInputMode(PlayerInputMode.Gameplay);
        SetOnline(true);
        Check(defense.TryStartDash(Vector3.right) && stamina.Current == 0, "Online presentation also spends the existing client stamina pool");
        defense.CancelDefense();
        SetOnline(false);
        yield return 1.15f;
        Check(stamina.Current > 0, "Existing regeneration resumes after the dash spend delay");
        SetField(stamina, "maxStamina", 150); stamina.Refill();
        Check(stamina.DashCost == 20, "Dash cost remains fixed at 20 with maximum stamina 150");
        for (int i = 0; i < 7; i++) Check(stamina.TrySpendDash(), "Full pool pays fixed dash charge " + (i + 1));
        Check(stamina.Current == 10 && !stamina.TrySpendDash(), "Seven charges leave 10; eighth is rejected");
        SetField(stamina, "maxStamina", 100); stamina.Refill();

        // Bootstrap remains inactive for its entire lifetime: no Awake, clients, sockets or connection.
        var networkFixture = new GameObject("Inactive network identity fixture");
        networkFixture.transform.SetParent(stage.transform); networkFixture.SetActive(false);
        bootstrap = networkFixture.AddComponent<NetworkBootstrap>();
        bootstrap.enabled = false;
        identity = networkFixture.AddComponent<LocalIdentity>();
        SetProperty(bootstrap, "Identity", identity); SetProperty(bootstrap, "LocalPlayer", player);
        Check(bootstrap.Session == null && bootstrap.Lobby == null, "Inactive identity fixture creates no network clients");
        var uiRoot = new GameObject("Hotkey UI fixture", typeof(RectTransform), typeof(Canvas));
        uiRoot.transform.SetParent(stage.transform);
        uiRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        gameUi = uiRoot.AddComponent<GameUIController>();
        SetField(gameUi, "bootstrap", bootstrap); SetField(gameUi, "playerInput", input);
        // Added components can receive Awake on the next player-loop tick when created from
        // EditorApplication.update. Let the real UI lifecycle construct its child views first.
        yield return .05f;
        gameUi.enabled = false; // Dispatch Update once per synthetic sample, without double-dispatch from editor timing.
        Check(GetField<GameObject>(gameUi, "minimapRoot") != null, "Real Canvas lifecycle constructs the minimap view");
        syntheticKeyboard = InputSystem.AddDevice<Keyboard>("DashHotkeyValidationKeyboard");
        Check(!gameUi.IsMinimapVisible, "Minimap starts hidden");
        PressKey(gameUi, UnityEngine.InputSystem.Key.F11);
        yield return .05f;
        Check(!gameUi.IsMinimapVisible, "F11 cannot expose the minimap in the lobby");
        identity.IsConnectedToSession = true;
        InvokeUpdate(gameUi);
        Check(!gameUi.IsMinimapVisible, "Entering a new session preserves default OFF");
        PressKey(gameUi, UnityEngine.InputSystem.Key.F11);
        yield return .05f;
        Check(gameUi.IsMinimapVisible, "Actual F11 key sample through Update enables the minimap");
        PressKey(gameUi, UnityEngine.InputSystem.Key.F11);
        yield return .05f;
        Check(!gameUi.IsMinimapVisible, "Second F11 press disables the minimap");
        input.SetInputMode(PlayerInputMode.UI);
        PressKey(gameUi, UnityEngine.InputSystem.Key.F11);
        yield return .05f;
        Check(!gameUi.IsMinimapVisible, "UI input mode blocks minimap hotkey");
        input.SetInputMode(PlayerInputMode.Gameplay);
        SetStaticProperty(typeof(ChatUI), "IsTyping", true);
        PressKey(gameUi, UnityEngine.InputSystem.Key.F11);
        yield return .05f;
        Check(!gameUi.IsMinimapVisible, "Chat typing blocks minimap hotkey");
        SetStaticProperty(typeof(ChatUI), "IsTyping", false);
        PressKey(gameUi, UnityEngine.InputSystem.Key.F11);
        yield return .05f;
        Check(gameUi.IsMinimapVisible, "Minimap can be reopened after UI and chat close");
        identity.IsConnectedToSession = false; InvokeUpdate(gameUi);
        Check(!gameUi.IsMinimapVisible, "Leaving the session hides minimap and clears preference");
        identity.IsConnectedToSession = true; InvokeUpdate(gameUi);
        Check(!gameUi.IsMinimapVisible, "Next session again starts with minimap OFF");

        BuildTeleportFixture();
        identity.IsConnectedToSession = false;
        Vector3 before = player.transform.position;
        PressKey(cheats, UnityEngine.InputSystem.Key.F9);
        Check(player.transform.position.x > 20 && Vector3.Distance(before, player.transform.position) > 10,
            "Actual F9 key dispatch reaches offline exit room");
        Check(capsule.enabled, "Offline debug move restores the CharacterController");
        Check(!OverlapsObstacle(), "Exit debug position avoids center obstacle and wall colliders");
        PressKey(cheats, UnityEngine.InputSystem.Key.F10);
        Check(player.transform.position.x > 40, "Actual F10 key dispatch reaches offline boss room");
        Check(!OverlapsObstacle(), "Boss debug position avoids solid props");
        before = player.transform.position;
        input.SetInputMode(PlayerInputMode.UI);
        PressKey(cheats, UnityEngine.InputSystem.Key.F9);
        Check(Vector3.Distance(before, player.transform.position) < .001f, "UI mode suppresses F9 teleport");
        input.SetInputMode(PlayerInputMode.Gameplay);
        SetStaticProperty(typeof(ChatUI), "IsTyping", true);
        PressKey(cheats, UnityEngine.InputSystem.Key.F9);
        Check(Vector3.Distance(before, player.transform.position) < .001f, "Chat typing suppresses F9 teleport");
        SetStaticProperty(typeof(ChatUI), "IsTyping", false);
        SetOnline(true);
        Check(!cheats.TryTeleportOffline(RoomType.Exit), "Online flag prevents offline teleport fallback");
        PressKey(cheats, UnityEngine.InputSystem.Key.F9);
        Check(Vector3.Distance(before, player.transform.position) < .001f,
            "Disconnected online request never locally teleports or bypasses server movement");
        SetOnline(false);
        Check(!cheats.TryTeleportOffline(RoomType.Reward), "Unsupported room destination is rejected");
        GetField<HashSet<Vector3Int>>(dungeon, "floorTiles").Clear();
        Check(!cheats.TryTeleportOffline(RoomType.Exit), "Missing dungeon floor data refuses unsafe teleport");
        Check(bootstrap.Session == null && bootstrap.Lobby == null, "All hotkey checks finish without a server connection");
    }

    private static void BuildTeleportFixture()
    {
        var generator = new GameObject("Offline deterministic room fixture");
        generator.transform.SetParent(stage.transform);
        dungeon = generator.AddComponent<DungeonGenerator_ChunkMesh>();
        dungeon.generateOnStart = false; dungeon.centerMapAtOrigin = false;
        AddRoom(RoomType.Exit, 20, 0);
        AddRoom(RoomType.Boss, 40, 1);
        DebugCheatKeys.EnsureOfflineHandler();
        cheats=UnityEngine.Object.FindFirstObjectByType<DebugCheatKeys>();
        Check(cheats!=null,"Offline dungeon installs F9/F10 handler without a bootstrap");
        DebugCheatKeys.EnsureOfflineHandler();
        Check(UnityEngine.Object.FindObjectsByType<DebugCheatKeys>(FindObjectsSortMode.None).Length==1,"Repeated setup does not duplicate hotkey handlers");
        cheats.transform.SetParent(stage.transform);
        cheats.Initialize(bootstrap);
        SetField(cheats, "dungeon", dungeon); SetField(cheats, "localPlayer", player.transform);
        Physics.SyncTransforms();
    }

    private static void AddRoom(RoomType type, int origin, int id)
    {
        var room = new Room { id = id, type = type, bounds = new BoundsInt(origin, 0, 0, 10, 5, 10) };
        var floors = GetField<HashSet<Vector3Int>>(dungeon, "floorTiles");
        for (int x = origin; x < origin + 10; x++)
            for (int z = 0; z < 10; z++) { var tile = new Vector3Int(x, 0, z); room.floorTiles.Add(tile); floors.Add(tile); }
        var blocked = new Vector3Int(origin + 5, 0, 5); room.blockedTiles.Add(blocked);
        GetField<List<Room>>(dungeon, "rooms").Add(room);
        if (type == RoomType.Exit) SetField(dungeon, "exitRoom", room);
        Box(type + " floor", new Vector3(origin + 5, .5f, 5), new Vector3(10, 1, 10));
        Box(type + " center prop", new Vector3(origin + 5.5f, 2.5f, 5.5f), new Vector3(3, 3, 3));
        Box(type + " wall", new Vector3(origin + .1f, 2.5f, 5), new Vector3(.2f, 3, 10));
    }

    private static GameObject Box(string name, Vector3 position, Vector3 scale)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
        box.transform.SetParent(stage.transform); box.transform.position = position; box.transform.localScale = scale;
        return box;
    }

    private static bool OverlapsObstacle()
    {
        Physics.SyncTransforms();
        Vector3 center = player.transform.TransformPoint(capsule.center);
        float half = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
        foreach (var hit in Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
            Mathf.Max(.01f, capsule.radius - .06f), ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(player.transform)) continue;
            if (hit.name.Contains("prop") || hit.name.Contains("wall")) return true;
        }
        return false;
    }

    private static void PressKey(MonoBehaviour recipient, UnityEngine.InputSystem.Key key)
    {
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState(key)); InputSystem.Update();
        syntheticKeyboard.MakeCurrent(); // Editor focus/hardware events must not select a different keyboard for this fixture.
        InvokeUpdate(recipient);
        // Release only. InputSystem's edge flags can survive another manual update in the same
        // rendered frame, so dispatching runtime Update again could toggle the same press twice.
        InputSystem.QueueStateEvent(syntheticKeyboard, new KeyboardState()); InputSystem.Update();
    }

    private static void InvokeUpdate(MonoBehaviour target) => target.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T GetField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name).GetSetMethod(true).Invoke(target, new[] { value });
    private static void SetStaticProperty(Type type, string name, object value) => type.GetProperty(name).GetSetMethod(true).Invoke(null, new[] { value });

    private static void CleanupSyntheticInput()
    {
        if (syntheticKeyboard != null && syntheticKeyboard.added) InputSystem.RemoveDevice(syntheticKeyboard);
        syntheticKeyboard = null;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || finishing || routine == null) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 60) throw new TimeoutException("Defense checks exceeded 60 seconds.");
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (lastFrame == Time.frameCount || Time.time < resumeAt) return;
            lastFrame = Time.frameCount;
            if (!routine.MoveNext()) { Finish(null); return; }
            resumeAt = routine.Current is float duration ? Time.time + duration : Time.time;
        }
        catch (Exception ex) { Finish(ex); }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed: " + description);
        checks.Add("PASS " + description);
    }

    private static void SetOnline(bool value)
    {
        // Editor test assembly may not directly access the runtime assembly's internal setter.
        var setter = typeof(NetworkRuntime).GetProperty(nameof(NetworkRuntime.IsOnline), BindingFlags.Static | BindingFlags.Public)?.GetSetMethod(true);
        if (setter == null) throw new InvalidOperationException("NetworkRuntime.IsOnline setter is missing.");
        setter.Invoke(null, new object[] { value });
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }

    private static void Finish(Exception error)
    {
        if (finishing) return;
        finishing = true;
        Application.logMessageReceived -= OnLog;
        (routine as IDisposable)?.Dispose(); routine = null;
        CleanupSyntheticInput();
        SetStaticProperty(typeof(ChatUI), "IsTyping", false);
        SetOnline(priorOnline);
        Time.timeScale = priorTimeScale;
        Application.runInBackground = priorBackground;
        if (stage != null) UnityEngine.Object.Destroy(stage);
        SessionState.SetBool(Key + ".Passed", error == null);
        SessionState.SetString(Key + ".Report", string.Join("\n", checks) + (error != null ? "\n" + error : ""));
        File.WriteAllText(Output, "RUNNING\n" + SessionState.GetString(Key + ".Report", "") + "\nRestoring editor scene setup.\n");
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else RestoreAndReport();
    }

    private static void RestoreAndReport()
    {
        bool passed = SessionState.GetBool(Key + ".Passed", false);
        string report = SessionState.GetString(Key + ".Report", "No result.");
        try
        {
            if (SessionState.GetBool(Key + ".Blank", false)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
            {
                var saved = JsonUtility.FromJson<SavedScenes>(SessionState.GetString(Key + ".Scenes", ""));
                if (saved?.scenes == null || saved.scenes.Length == 0) throw new InvalidOperationException("Saved scene setup missing.");
                var setup = new SceneSetup[saved.scenes.Length];
                for (int i = 0; i < setup.Length; i++) setup[i] = new SceneSetup {
                    path = saved.scenes[i].path, isLoaded = saved.scenes[i].loaded, isActive = saved.scenes[i].active };
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                var actual = EditorSceneManager.GetSceneManagerSetup();
                if (actual.Length != setup.Length) throw new InvalidOperationException("Restored scene count differs.");
                for (int i = 0; i < actual.Length; i++)
                    if (actual[i].path != setup[i].path || actual[i].isLoaded != setup[i].isLoaded || actual[i].isActive != setup[i].isActive)
                        throw new InvalidOperationException("Restored scene setup differs at " + i);
            }
            report += "\nPASS Previous editor scene setup restored.\n";
        }
        catch (Exception ex) { passed = false; report += "\nScene restoration failed: " + ex; }
        finally
        {
            SessionState.SetBool(Key, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, (passed ? "PASS" : "FAIL") + "\n" + report +
                "\nScope: actual Unity Play Mode + real player prefab + native physics + synthetic key dispatch through runtime Update. " +
                "Network mode is a local flag fixture; no two-client, latency, or live-server acceptance test was performed.\n");
            if (passed) Debug.Log("DASH_HOTKEY_VALIDATION_PASS: " + Output);
            else Debug.LogError("DASH_HOTKEY_VALIDATION_FAIL: " + report);
        }
    }
}
