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
public static class PlayerDefenseValidation
{
    private const string Key = "Retry.PlayerDefenseValidation";
    private const string PrefabPath = "Assets/Prefabs/LocalPlayerPrefab.prefab";
    private static string Output => Path.GetFullPath("Artifacts/Defense/validation.txt");
    private static readonly List<string> checks = new();
    private static readonly List<string> errors = new();
    private static GameObject stage, player, attacker;
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
    private static InputActionAsset syntheticActions;
    private static Keyboard syntheticKeyboard;
    private static Mouse syntheticMouse;
    [Serializable] private sealed class SavedScenes { public SavedScene[] scenes; }
    [Serializable] private sealed class SavedScene { public string path; public bool loaded; public bool active; }

    static PlayerDefenseValidation()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        EditorApplication.update += Tick;
    }

    [MenuItem("Retry/Player Defense/Run PlayMode Validation")]
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
        File.WriteAllText(Output, "RUNNING\nActual player prefab, offline Play Mode, no network connection.\n");
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
            stage = player = attacker = null;
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
        attacker = new GameObject("Melee attacker");
        attacker.transform.SetParent(stage.transform);
        attacker.AddComponent<Player_State>();
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
        NewPlayer(); yield return 0.25f;
        Check(capsule.isGrounded, "Actual CharacterController settles on floor");
        Check(attack.EquipWeapon("test_sword"), "Actual sword profile equips");
        input.SetInputMode(PlayerInputMode.UI);
        Check(!defense.TryStartDash(Vector3.forward) && !defense.TryStartParry(), "UI mode blocks dash and parry");
        input.SetInputMode(PlayerInputMode.Gameplay);
        attack.NotifyAttackAnimationStarted(1);
        Check(!defense.TryStartDash(Vector3.forward) && !defense.TryStartParry(), "Active attack blocks defense cancellation exploit");
        attack.CancelCurrentAttack();

        Check(defense.TryStartDash(Vector3.zero), "No-direction dash starts while grounded");
        Check(Vector3.Dot(defense.DashDirection, -player.transform.forward) > .99f, "No-direction dash goes backward");
        Check(!defense.TryStartDash(Vector3.right) && !defense.TryStartParry(), "Active dash blocks repeated dash and parry");
        int hp = state.CurrentHp;
        Check(!state.TryTakeMeleeDamage(10, attacker) && state.CurrentHp == hp, "Offline dash initial melee immunity blocks real health loss");
        yield return 0.25f;
        Check(state.TryTakeMeleeDamage(10, attacker) && state.CurrentHp < hp, "Offline dash loses melee immunity after initial window");
        Check(!defense.IsBusy, "Receiving actual damage cancels defense");

        NewPlayer(); yield return 0.25f;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Native dash collision wall"; wall.transform.SetParent(stage.transform);
        wall.transform.position = new Vector3(0, 1.5f, 2f); wall.transform.localScale = new Vector3(5f, 3f, .2f);
        Physics.SyncTransforms();
        Vector3 initial = player.transform.position;
        Check(defense.TryStartDash(Vector3.forward), "Forward dash starts before native wall");
        float peak = initial.y;
        float until = Time.time + .52f;
        while (Time.time < until) { peak = Mathf.Max(peak, player.transform.position.y); yield return null; }
        Check(player.transform.position.z > initial.z + .25f, "Dash produces actual CharacterController horizontal movement");
        Check(player.transform.position.z <= 1.9f - capsule.radius + capsule.skinWidth + .04f,
            "Dash capsule stops before wall and cannot pass through it");
        Check(peak <= initial.y + .04f, "Dash replacement does not add a jump impulse");
        Check(!defense.IsDashing, "Dash action ends on schedule");
        Check(!defense.TryStartDash(Vector3.right), "Dash cooldown remains after movement ends");
        wall.SetActive(false); UnityEngine.Object.Destroy(wall);
        yield return .35f;
        Check(defense.TryStartDash(Vector3.right), "Dash becomes available after cooldown");
        defense.CancelDefense();
        Check(!defense.IsBusy && !defense.TryStartDash(Vector3.forward), "Cancellation preserves dash cooldown");

        NewPlayer(3f); yield return .05f;
        Check(!capsule.isGrounded && !defense.TryStartDash(Vector3.forward), "Airborne dash is rejected");
        float airborneHeight = player.transform.position.y;
        yield return .25f;
        Check(player.transform.position.y < airborneHeight - .1f, "Removing jump preserves gravity");
        yield return .9f;
        Check(capsule.isGrounded && Mathf.Abs(player.transform.position.y) < .15f, "Falling character lands normally");

        NewPlayer(); yield return .25f;
        attacker.transform.position = player.transform.position + Vector3.forward * 1.5f;
        Check(defense.TryStartParry(), "Sword starts offline parry");
        Check(defense.IsParrying && !defense.TryStartDash(Vector3.right), "Parry locks conflicting dash");
        hp = state.CurrentHp;
        Check(!state.TryTakeMeleeDamage(10, attacker) && state.CurrentHp == hp, "Front melee inside parry window is blocked");
        Check(attacker.GetComponent<Player_State>().IsHit, "Successful offline parry staggers the melee attacker");
        Check(!defense.TryDefendMelee(attacker), "Successful parry cannot consume the same action twice");
        defense.CancelDefense();
        Check(!defense.TryStartParry(), "Parry cancellation retains cooldown");

        NewPlayer(); yield return .25f;
        attacker.transform.position = player.transform.position - Vector3.forward * 1.5f;
        Check(defense.TryStartParry(), "Rear-hit fixture starts parry");
        hp = state.CurrentHp;
        Check(state.TryTakeMeleeDamage(10, attacker) && state.CurrentHp < hp, "Rear melee bypasses frontal parry");
        Check(!defense.IsBusy && !defense.TryStartParry(), "Hit stun cancels parry and blocks immediate retry");

        NewPlayer(); yield return .25f;
        attacker.transform.position = player.transform.position + Vector3.forward * 1.5f;
        Check(defense.TryStartParry(), "Late-hit fixture starts parry");
        yield return .26f;
        hp = state.CurrentHp;
        Check(state.TryTakeMeleeDamage(10, attacker) && state.CurrentHp < hp, "Melee after parry window causes damage");

        NewPlayer(); yield return .25f;
        Check(defense.TryStartParry(), "Generic damage fixture starts parry");
        hp = state.CurrentHp;
        state.TakeDamage(10, attacker);
        Check(state.CurrentHp < hp && !defense.IsBusy, "Generic/non-melee damage is never parried");

        NewPlayer(); yield return .25f;
        Check(defense.TryStartDash(Vector3.forward), "Generic damage fixture starts offline dash");
        hp = state.CurrentHp;
        state.TakeDamage(10, attacker);
        Check(state.CurrentHp < hp && !defense.IsBusy, "Offline dash never blocks generic/projectile damage");

        NewPlayer(); yield return .25f;
        Check(defense.TryStartParry(), "Invalid attacker fixture starts parry");
        Check(!defense.TryDefendMelee(null), "Missing melee attacker cannot fabricate parry success");
        attacker.transform.position = player.transform.position + Vector3.forward * 10f;
        Check(!defense.TryDefendMelee(attacker), "Out-of-range melee attacker cannot be parried");

        NewPlayer(); yield return .25f;
        Check(attack.EquipWeapon("test_gun"), "Actual gun profile remains available without changes");
        Check(!defense.TryStartParry(), "Gun cannot parry");
        Check(attack.EquipWeapon("test_sword") && defense.TryStartParry(), "Sword switch permits parry");
        attack.EquipWeapon("test_gun");
        yield return null;
        Check(!defense.IsBusy, "Weapon change cancels active defense");
        state.NetworkDie();
        Check(!defense.TryStartDash(Vector3.forward) && !defense.TryStartParry(), "Dead player cannot start defense");

        NewPlayer(); yield return .25f;
        Check(defense.TryStartParry(), "UI cancellation fixture starts parry");
        input.SetInputMode(PlayerInputMode.UI); yield return null;
        Check(!defense.IsBusy, "Opening UI cancels active defense");

        NewPlayer(); yield return .25f;
        SetOnline(true);
        Check(defense.TryStartDash(Vector3.right), "Online dash presentation starts without a new network request");
        Check(!defense.TryDefendMelee(attacker), "Online dash never grants client-authoritative damage immunity");
        float firstX = player.transform.position.x, firstTime = Time.time;
        yield return .25f;
        float onlineSpeed = (player.transform.position.x - firstX) / Mathf.Max(.001f, Time.time - firstTime);
        Check(onlineSpeed <= 10.1f && onlineSpeed > 0f, "Online dash stays inside existing 10 m/s sprint limit");
        state.NetworkApplyHp(73);
        Check(state.CurrentHp == 73 && !defense.IsBusy, "Server HP applies during online dash and cancels its visual state");
        SetOnline(false);

        NewPlayer(); yield return .25f;
        SetOnline(true);
        Check(defense.TryStartParry(), "Online sword parry can display a preparation pose");
        attacker.transform.position = player.transform.position + Vector3.forward;
        Check(!defense.TryDefendMelee(attacker), "Online preparation never fabricates a successful parry");
        state.NetworkApplyHp(61);
        Check(state.CurrentHp == 61 && !defense.IsBusy, "Server damage is not suppressed by online parry pose");
        SetOnline(false);

        NewPlayer(); yield return .25f;
        SetOnline(true);
        Check(defense.TryStartDash(Vector3.right), "Unchanged HP fixture starts online dash");
        state.NetworkApplyHp(state.CurrentHp);
        Check(defense.IsDashing, "Repeated identical server HP does not cancel dash");
        state.NetworkDie();
        Check(state.IsDead && !defense.IsBusy, "Direct server death cancels defense immediately without another frame");
        SetOnline(false);

        NewPlayer(); yield return .25f;
        SetOnline(true);
        Check(defense.TryStartDash(Vector3.right), "Storm fixture starts dash");
        state.NetworkApplyHp(90,false);state.ApplyABHealth(100,90);
        Check(state.CurrentHp==90&&!state.IsHit&&defense.IsDashing,"Storm damage and following snapshot preserve dash without hit stun");
        state.NetworkApplyHp(80);
        Check(state.IsHit&&!defense.IsBusy,"Combat damage after storm still causes hit stun");
        state.NetworkApplyHp(0,false);
        Check(state.IsDead,"Lethal storm damage still kills the player");
        SetOnline(false);

        NewPlayer(); yield return .25f;
        state.TakeDamage(20, attacker);
        state.NotifyHitAnimationCompleted();
        Check(defense.TryStartParry(), "Healing fixture starts offline parry");
        hp = state.CurrentHp;
        state.Heal(5);
        Check(state.CurrentHp == hp + 5 && defense.IsParrying, "Healing does not cancel active parry");
        state.NetworkApplyHp(state.CurrentHp);
        Check(defense.IsParrying, "Unchanged HP refresh does not cancel active parry");

        NewPlayer(); yield return .25f;
        SetupSyntheticInput();
        // Regression: attack follows the character even when the orbit camera is 30 degrees left.
        var cameraFixture = new GameObject("Sword facing camera at -30 degrees");
        cameraFixture.transform.SetParent(stage.transform);
        cameraFixture.transform.rotation = Quaternion.Euler(15, -30, 0);
        var cameraController = player.GetComponent<Player_Camera_Controller>();
        if (cameraController != null)
            typeof(Player_Camera_Controller).GetField("cameraTransform", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(cameraController, cameraFixture.transform);
        typeof(Player_Attack).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attack, null);
        var movementFixture = player.GetComponent<Player_Movement>();
        movementFixture.enabled = false;
        player.transform.rotation = Quaternion.identity;
        var syntheticAttack = syntheticActions.FindAction("Player/Attack", true);
        Check(HasBinding(syntheticAttack, "<Mouse>/leftButton"), "Shipped attack action has a left mouse binding");
        syntheticAttack.performed += input.OnAttack; syntheticAttack.canceled += input.OnAttack;
        syntheticAttack.Enable();
        InputSystem.QueueStateEvent(syntheticMouse, new MouseState().WithButton(MouseButton.Left));
        InputSystem.Update(); yield return .06f;
        Check(input.Attack, "Actual left mouse press reaches Defalult_Input.OnAttack in Play Mode");
        Check(attack.TryConsumeAttackAnimationRequest(out int swordCombo) && swordCombo == 1,
            "Left click requests the equipped sword's first combo");
        Check(Quaternion.Angle(player.transform.rotation, Quaternion.identity) < .001f,
            "Sword attack preserves facing with camera 30 degrees left");
        Check(Vector3.Angle(player.transform.forward, Vector3.forward) < .001f,
            "Existing network attack transform.forward remains aligned with player facing");
        InputSystem.QueueStateEvent(syntheticMouse, new MouseState()); InputSystem.Update(); yield return .04f;
        syntheticAttack.Disable(); attack.CancelCurrentAttack();
        movementFixture.enabled = true;
        PressSyntheticSpace(true);
        yield return .06f;
        Check(input.Dash && !input.Jump && defense.IsDashing, "Actual Space binding routes input to dash, never jump");
        yield return .9f;
        Check(input.Dash && !defense.IsBusy, "Holding Space through action and cooldown does not repeat dash");
        PressSyntheticSpace(false); yield return .04f;
        PressSyntheticSpace(true); yield return .06f;
        Check(defense.IsDashing, "A fresh Space press after release starts the next dash");
        // Roll duration is .7s; the old .5s wait attempted parry before recovery ended.
        PressSyntheticSpace(false); yield return PlayerDefenseController.DefaultDashDuration + .1f;
        PressSyntheticParry(true); yield return .06f;
        Check(input.Parry && defense.IsParrying, "Actual right mouse binding starts sword parry");
        yield return .75f;
        Check(input.Parry && !defense.IsBusy, "Holding right mouse through recovery and cooldown does not repeat parry");
        PressSyntheticParry(false); yield return .04f;
        input.SetInputMode(PlayerInputMode.UI);
        PressSyntheticSpace(true); PressSyntheticParry(true); yield return .06f;
        Check(!input.Dash && !input.Parry && !defense.IsBusy, "Actual Space/right mouse events are suppressed while UI is open");
        PressSyntheticSpace(false); PressSyntheticParry(false);
        input.SetInputMode(PlayerInputMode.Gameplay);
        CleanupSyntheticInput();
        Check(UnityEngine.Object.FindObjectsByType<NetworkBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0,
            "All checks completed without NetworkBootstrap or a live server");
    }

    private static void SetupSyntheticInput()
    {
        var hardware = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        Check(hardware != null && hardware.actions != null && hardware.defaultActionMap == "Player",
            "Actual prefab PlayerInput starts the Player action map");
        var dash = hardware.actions.FindAction("Player/Dash", true);
        var parry = hardware.actions.FindAction("Player/Parry", true);
        Check(HasBinding(dash, "<Keyboard>/space"), "Actual Dash action has a Space binding");
        Check(HasBinding(parry, "<Mouse>/rightButton"), "Actual Parry action has a right mouse binding");
        Check(hardware.actions.FindAction("Player/Jump", false) == null, "Old Jump action is absent from active input asset");
        // Clone the shipped action definitions and restrict them to new synthetic devices.
        // No real device is disabled, removed, re-paired, or has its state overwritten.
        syntheticKeyboard = InputSystem.AddDevice<Keyboard>("DefenseValidationKeyboard");
        syntheticMouse = InputSystem.AddDevice<Mouse>("DefenseValidationMouse");
        syntheticActions = UnityEngine.Object.Instantiate(hardware.actions);
        syntheticActions.name = "Defense validation cloned action bindings";
        syntheticActions.devices = new InputDevice[] { syntheticKeyboard, syntheticMouse };
        var syntheticDash = syntheticActions.FindAction("Player/Dash", true);
        var syntheticParry = syntheticActions.FindAction("Player/Parry", true);
        syntheticDash.performed += input.OnJump; syntheticDash.canceled += input.OnJump;
        syntheticParry.performed += input.OnParry; syntheticParry.canceled += input.OnParry;
        syntheticDash.Enable(); syntheticParry.Enable();
    }

    private static bool HasBinding(InputAction action, string path)
    {
        foreach (var binding in action.bindings) if (string.Equals(binding.effectivePath, path, StringComparison.Ordinal)) return true;
        return false;
    }

    private static void PressSyntheticSpace(bool held)
    {
        var sample = held ? new KeyboardState(UnityEngine.InputSystem.Key.Space) : new KeyboardState();
        InputSystem.QueueStateEvent(syntheticKeyboard, sample);
        InputSystem.Update();
    }

    private static void PressSyntheticParry(bool held)
    {
        InputSystem.QueueStateEvent(syntheticMouse, new MouseState().WithButton(MouseButton.Right, held));
        InputSystem.Update();
    }

    private static void CleanupSyntheticInput()
    {
        if (syntheticActions != null) { syntheticActions.Disable(); UnityEngine.Object.DestroyImmediate(syntheticActions); }
        syntheticActions = null;
        if (syntheticKeyboard != null && syntheticKeyboard.added) InputSystem.RemoveDevice(syntheticKeyboard);
        if (syntheticMouse != null && syntheticMouse.added) InputSystem.RemoveDevice(syntheticMouse);
        syntheticKeyboard = null; syntheticMouse = null;
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
                "\nScope: actual Unity Play Mode + real player prefab + native physics. " +
                "Network mode is a local flag fixture; no two-client, latency, or live-server acceptance test was performed.\n");
            if (passed) Debug.Log("PLAYER_DEFENSE_VALIDATION_PASS: " + Output);
            else Debug.LogError("PLAYER_DEFENSE_VALIDATION_FAIL: " + report);
        }
    }
}
