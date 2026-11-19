using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Asynchronous Play Mode runner for native prop physics. It saves the editor scene setup,
/// enters an empty scene before Play Mode (so SampleScene cannot connect or generate a dungeon),
/// checks real palette colliders with a local Physics3D scene, then restores the prior scene setup.
/// </summary>
[InitializeOnLoad]
public static class DungeonPropPhysicsRuntimeValidation
{
    private const string Key = "Retry.DungeonPropPhysicsRuntimeValidation";
    private const string SetupKey = Key + ".SceneSetup";
    private const string ResultKey = Key + ".Result";
    private const string PassedKey = Key + ".Passed";
    private const string CountKey = Key + ".Checks";
    private const string BlankKey = Key + ".WasBlankScene";
    private static GameObject stage;
    private static double began;
    private static bool tested;
    private static bool finishing;
    private static readonly List<string> errors = new();
    private static string Output => Path.GetFullPath("Artifacts/RoomComposition/physics-runtime-validation.txt");

    [Serializable] private sealed class SavedScenes { public SavedScene[] scenes; }
    [Serializable] private sealed class SavedScene { public string path; public bool loaded; public bool active; }

    static DungeonPropPhysicsRuntimeValidation()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        EditorApplication.update += Tick;
    }

    [MenuItem("Retry/Dungeon/Run Prop Physics PlayMode Validation")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key, false))
            throw new InvalidOperationException("Prop physics validation requires idle Edit Mode.");
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        var current = EditorSceneManager.GetSceneManagerSetup();
        bool wasBlank = current.Length == 1 && string.IsNullOrEmpty(current[0].path);
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
            if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.GetRootGameObjects().Length != 0))
                throw new InvalidOperationException("Save the open scene before the prop physics test: " + scene.name +
                    ". The validation runner does not discard or automatically save user scene changes.");
        }
        var saved = new SavedScenes { scenes = new SavedScene[current.Length] };
        for (int i = 0; i < current.Length; i++)
            saved.scenes[i] = new SavedScene { path = current[i].path, loaded = current[i].isLoaded, active = current[i].isActive };
        SessionState.SetString(SetupKey, JsonUtility.ToJson(saved));
        SessionState.SetBool(BlankKey, wasBlank);
        SessionState.SetString(ResultKey, "Interrupted before native physics checks completed.");
        SessionState.SetBool(PassedKey, false);
        SessionState.SetInt(CountKey, 0);
        SessionState.SetBool(Key, true);
        File.WriteAllText(Output, "RUNNING\nPending actual Play Mode capsule/controller/projectile checks and editor scene restoration.");
        try
        {
            // Close the saved scene setup before entering Play Mode. Unlike disabling roots
            // after EnteredPlayMode, this prevents NetworkBootstrap.Awake/Start side effects.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex)
        {
            SessionState.SetString(ResultKey, ex.ToString());
            RestoreAndReport();
            throw;
        }
    }

    private static void OnModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            try { SetupRuntimeFixtures(); }
            catch (Exception ex) { Finish(ex); }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Application.logMessageReceived -= OnLog;
            stage = null;
            RestoreAndReport();
        }
    }

    private static void SetupRuntimeFixtures()
    {
        errors.Clear(); tested = false; finishing = false; began = EditorApplication.timeSinceStartup;
        Application.logMessageReceived += OnLog;
        if (UnityEngine.Object.FindObjectsByType<NetworkBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 0)
            throw new InvalidOperationException("NetworkBootstrap exists in the empty physics validation scene.");
        var palette = DungeonPropPalette.Load();
        if (palette == null) throw new InvalidOperationException("Build DungeonPropPalette assets before running prop physics validation.");

        stage = new GameObject("Dungeon prop native physics source fixtures");
        // Use actual shared planner dimensions and orientation, not test-only size constants.
        var room = new Room { id = 1, type = RoomType.Normal, shape = RoomShape.Normal,
            layoutType = RoomLayoutType.Open, bounds = new BoundsInt(0, 0, 0, 40, 10, 32) };
        for (int x = 0; x < 40; x++) for (int z = 0; z < 32; z++) room.floorTiles.Add(new Vector3Int(x, 0, z));
        room.doorwayFloorTiles.Add(new Vector3Int(0, 0, 16));
        room.doorwayFloorTiles.Add(new Vector3Int(39, 0, 16));
        room.doorwayFloorTiles.Add(new Vector3Int(20, 0, 0));
        var chosen = new HashSet<DungeonPropLayout.Kind>();
        for (int seed = 12345; seed < 12357 && chosen.Count < 3; seed++)
        {
            foreach (var slot in DungeonPropLayout.Generate(seed, room, Vector3.zero, _ => false))
            {
                if (slot.Supported || chosen.Contains(slot.PropKind)) continue;
                if (slot.PropKind != DungeonPropLayout.Kind.Crate && slot.PropKind != DungeonPropLayout.Kind.Table &&
                    slot.PropKind != DungeonPropLayout.Kind.Rock) continue;
                var prefab = palette.Resolve(slot.PropKind);
                if (prefab == null) throw new InvalidOperationException("Missing palette source " + slot.PropKind);
                var prop = UnityEngine.Object.Instantiate(prefab, slot.Center, Quaternion.Euler(0, slot.Yaw, 0), stage.transform);
                bool quarterTurn = Mathf.RoundToInt(slot.Yaw / 90f) % 2 != 0;
                prop.transform.localScale = quarterTurn ? new Vector3(slot.Size.z, slot.Size.y, slot.Size.x) : slot.Size;
                var marker = prop.GetComponent<DungeonPropObstacle>() ?? prop.AddComponent<DungeonPropObstacle>();
                marker.Kind = slot.PropKind; marker.RoomId = slot.RoomId; marker.GroupId = slot.GroupId; marker.Supported = false;
                // Do not add/repair colliders here: the actual generated palette must provide them.
                chosen.Add(slot.PropKind);
            }
        }
        if (chosen.Count != 3) throw new InvalidOperationException("Shared planner did not provide floor crate/table/rock fixtures.");
        Physics.SyncTransforms();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || finishing || stage == null) return;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - began;
            if (elapsed > 15) throw new TimeoutException("Prop physics validation or local scene cleanup exceeded 15 seconds.");
            if (!tested && elapsed >= .2)
            {
                int count = DungeonPropPhysicsValidation.Run(stage);
                SessionState.SetInt(CountKey, count);
                tested = true;
            }
            if (tested && (DungeonPropPhysicsValidation.PendingSceneUnload == null || DungeonPropPhysicsValidation.PendingSceneUnload.isDone))
            {
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
                Finish(null);
            }
        }
        catch (Exception ex) { Finish(ex); }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }

    private static void Finish(Exception error)
    {
        if (finishing) return;
        finishing = true;
        Application.logMessageReceived -= OnLog;
        SessionState.SetBool(PassedKey, error == null);
        SessionState.SetString(ResultKey, error == null
            ? "Native checks completed and isolated physics scene unloaded. Pending editor scene restoration."
            : error.ToString());
        File.WriteAllText(Output, "RUNNING\n" + SessionState.GetString(ResultKey, "") + "\nWaiting for Edit Mode restoration.");
        if (stage != null) UnityEngine.Object.Destroy(stage);
        stage = null;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else RestoreAndReport();
    }

    private static void RestoreAndReport()
    {
        bool passed = SessionState.GetBool(PassedKey, false);
        string detail = SessionState.GetString(ResultKey, "No native physics result.");
        int count = SessionState.GetInt(CountKey, 0);
        try
        {
            if (SessionState.GetBool(BlankKey, false))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
            {
                var saved = JsonUtility.FromJson<SavedScenes>(SessionState.GetString(SetupKey, ""));
                if (saved?.scenes == null || saved.scenes.Length == 0)
                    throw new InvalidOperationException("Previous editor scene setup could not be read.");
                var setup = new SceneSetup[saved.scenes.Length];
                for (int i = 0; i < setup.Length; i++) setup[i] = new SceneSetup {
                    path = saved.scenes[i].path, isLoaded = saved.scenes[i].loaded, isActive = saved.scenes[i].active };
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                var actual = EditorSceneManager.GetSceneManagerSetup();
                if (actual.Length != setup.Length) throw new InvalidOperationException("Restored editor scene count differs.");
                for (int i = 0; i < actual.Length; i++)
                    if (actual[i].path != setup[i].path || actual[i].isLoaded != setup[i].isLoaded || actual[i].isActive != setup[i].isActive)
                        throw new InvalidOperationException("Restored editor scene setup differs at index " + i);
            }
        }
        catch (Exception ex) { passed = false; detail += "\nScene restoration failed: " + ex; }
        finally
        {
            SessionState.SetBool(Key, false);
            string summary = (passed ? "PASS " + count + " native physics assertions" : "FAIL") +
                "\nActual Unity Play Mode; isolated local Physics3D scene; generated palette fixtures sized by the shared prop planner." +
                "\nCrate/Table/Rock: player capsule and skin-adjusted capsule, actual CharacterController.Move, Wall raycast, " +
                "0.12m projectile sphere sweep, clear-lane controls." +
                "\nMain gameplay scene was closed before Play Mode; no live server/login or NavMesh test was performed." +
                (passed ? "\nTemporary physics scene unloaded and previous editor scene setup restored." : "\n" + detail);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, summary + "\n");
            if (passed) Debug.Log("DUNGEON_PROP_PHYSICS_RUNTIME_PASS: " + count + " assertions; editor scene setup restored.");
            else Debug.LogError("DUNGEON_PROP_PHYSICS_RUNTIME_FAIL: " + detail);
        }
    }
}
