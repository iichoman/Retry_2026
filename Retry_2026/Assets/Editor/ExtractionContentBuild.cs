using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit file requests let the already-open editor build content without closing the user's workspace.
[InitializeOnLoad]
public static class ExtractionContentBuild
{
    private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string Artifacts => Path.Combine(Root, "Artifacts");
    private static double nextPoll;
    static ExtractionContentBuild() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string request = Path.Combine(Root, "content-build.request");
        if (!File.Exists(request)) return;
        string action = File.ReadAllText(request).Trim();
        File.Delete(request);
        Directory.CreateDirectory(Artifacts);
        try
        {
            switch (action)
            {
                case "content": BuildContent(); break;
                case "validate": Validate(); break;
                case "clienttest": InvokeBuilder("ClientExtractionValidation", "Run"); break;
                case "vfxtest": InvokeBuilder("CombatVfxRuntimeValidation", "Start"); break;
                case "preview": InvokeBuilder("CombatContentBuilder", "RenderPreview"); break;
                case "player": BuildPlayer(); break;
                case "demoplayer": BuildDemoPlayer(); break;
                case "composition": InvokeBuilder("RoomCompositionValidation", "Run"); break;
                case "propphysics": InvokeBuilder("DungeonPropPhysicsRuntimeValidation", "Start"); break;
                case "buildwarnings": ExportBuildWarnings(BuildReport.GetLatestReport()); break;
                case "timingaudit": InvokeBuilder("CombatTimingAudit", "Run"); break;
                case "ccontent": InvokeBuilder("CContentBuilder", "Build"); break;
                case "econtent": InvokeBuilder("EContentBuilder", "Build"); break;
                case "expeditionui": InvokeBuilder("ExpeditionUIBuilder", "Build"); break;
                case "expeditiontest": InvokeBuilder("ExpeditionUIValidation", "Start"); break;
                case "abtest": InvokeBuilder("ABRuntimeValidation", "Start"); break;
                case "abcontent": InvokeBuilder("ABContentBuilder", "Build"); break;
                case "refresh": AssetDatabase.Refresh(); break;
                case "rolltest": InvokeBuilder("DefenseClipValidation", "Start"); break;
                case "rolltune": InvokeBuilder("DefenseClipBuilder", "TuneRoll"); break;
                case "localboss": InvokeBuilder("LocalBossBuilder", "Build"); break;
                case "localbosstest": InvokeBuilder("LocalBossValidation", "Start"); break;
                case "playercombatclips": InvokeBuilder("PlayerCombatAnimationBuilder", "Build"); break;
                case "playercombattest": InvokeBuilder("PlayerCombatAnimationValidation", "Start"); break;
                case "lockoncameratest": InvokeBuilder("LockOnCameraValidation", "Start"); break;
                case "duelcombat": InvokeBuilder("DuelCombatContentBuilder", "Build"); break;
                case "duelcombattest": InvokeBuilder("DuelCombatValidation", "Start"); break;
                case "swordgrounding": InvokeBuilder("SwordGroundingBuilder", "Build"); break;
                case "monsterhit": InvokeBuilder("PlayerCombatAnimationBuilder", "BuildMonsterHit"); break;
                case "bossclienttest": InvokeBuilder("BossClientCompatibilityValidation", "Start"); break;
                case "defense": InvokeBuilder("PlayerDefenseContentBuilder", "BuildAndValidate"); break;
                case "defensetest": InvokeBuilder("PlayerDefenseValidation", "Start"); break;
                case "icons": InvokeBuilder("ItemIconContentBuilder", "BuildAndValidate"); break;
                case "practice": InvokeBuilder("OfflineDefensePracticeBuilder", "Build"); break;
                case "iconpreview": InvokeBuilder("ItemIconContentBuilder", "RenderUiPreview"); break;
                case "practiceopen": InvokeBuilder("OfflineDefensePracticeBuilder", "Open"); break;
                case "sampleopen": InvokeBuilder("OfflineDefensePracticeBuilder", "ReturnToSample"); break;
                case "swordanalyze": InvokeBuilder("SwordAlignmentValidation", "Analyze"); break;
                case "swordvalidate": InvokeBuilder("SwordAlignmentValidation", "BuildAndValidate"); break;
                case "dungeonpresent": InvokeBuilder("DungeonPresentationBuilder", "BuildAndValidate"); break;
                case "dashspeed": InvokeBuilder("PlayerDefenseContentBuilder", "ApplyDashSpeed"); break;
                case "hotkeytest": InvokeBuilder("DashHotkeyValidation", "Start"); break;
                case "exitportal": InvokeBuilder("ExitPortalValidation", "BuildAndValidate"); break;
                default: throw new InvalidOperationException("Unknown content build action: " + action);
            }
            File.WriteAllText(Path.Combine(Artifacts, action + "-result.txt"), "PASS " + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Artifacts, action + "-result.txt"), "FAIL\n" + ex);
            Debug.LogException(ex);
        }
    }

    [MenuItem("Retry/Extraction/Build and Connect Content")]
    public static void BuildContent()
    {
        InvokeBuilder("ItemCatalogBuilder", "Build");
        InvokeBuilder("CombatContentBuilder", "BuildAll");
        WireScene();
        AssetDatabase.SaveAssets();
        Validate();
    }

    private static void InvokeBuilder(string typeName, string method)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
        var entry = type?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
        if (entry == null) throw new InvalidOperationException(typeName + "." + method + " not found");
        entry.Invoke(null, null);
    }

    private static void WireScene()
    {
        const string path = "Assets/Scenes/SampleScene.unity";
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var boss = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CombatContent/Prefabs/DungeonGuardian.prefab");
        var catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>("Assets/Resources/CombatVfxCatalog.asset");
        if (boss == null || catalog == null) throw new InvalidOperationException("Boss/VFX catalog asset missing");
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var bs in root.GetComponentsInChildren<NetworkBootstrap>(true))
        {
            var serialized = new SerializedObject(bs);
            serialized.FindProperty("bossPrefab").objectReferenceValue = boss;
            serialized.FindProperty("combatVfxCatalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Expected one NetworkBootstrap, got " + count);
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var result in root.GetComponentsInChildren<DungeonResultUI>(true)) LayoutResult(result);
            foreach (var inventory in root.GetComponentsInChildren<InventoryUI>(true)) LayoutInventory(inventory);
            foreach (var placer in root.GetComponentsInChildren<RoomObjectPlacer2>(true))
            {
                var serialized = new SerializedObject(placer);
                var iterator = serialized.GetIterator();
                while (iterator.Next(true))
                    if (iterator.propertyPath.StartsWith("propLibrary.", StringComparison.Ordinal) && iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                        iterator.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }

    private static void LayoutResult(DungeonResultUI result)
    {
        var serialized = new SerializedObject(result);
        var items = serialized.FindProperty("itemsText").objectReferenceValue as Text;
        var gold = serialized.FindProperty("goldText").objectReferenceValue as Text;
        if (items == null || gold == null) throw new InvalidOperationException("Result text references missing");
        var window = items.transform.parent as RectTransform;
        if (window != null) window.sizeDelta = new Vector2(760, 760);
        SetTopRect(gold.rectTransform, 44, 268, 672, 62);
        gold.fontSize = 21;
        SetTopRect(items.rectTransform, 44, 346, 672, 288);
        items.fontSize = 20;
        items.verticalOverflow = VerticalWrapMode.Truncate;
        items.horizontalOverflow = HorizontalWrapMode.Wrap;
        EditorUtility.SetDirty(items); EditorUtility.SetDirty(gold);
    }

    private static void LayoutInventory(InventoryUI inventory)
    {
        var serialized = new SerializedObject(inventory);
        var slotRoot = serialized.FindProperty("slotRoot").objectReferenceValue as Transform;
        var detail = serialized.FindProperty("itemDetailText").objectReferenceValue as Text;
        if (slotRoot == null) throw new InvalidOperationException("Inventory slot root missing");
        var grid = slotRoot.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 6;
            grid.cellSize = new Vector2(64, 64); grid.spacing = new Vector2(6, 6);
            grid.childAlignment = TextAnchor.UpperLeft;
        }
        SetTopRect(slotRoot as RectTransform, 20, 70, 420, 420);
        if (detail != null) { SetTopRect(detail.rectTransform, 20, 506, 420, 250); detail.fontSize = 18; }
    }

    private static void SetTopRect(RectTransform rect, float x, float down, float width, float height)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -down); rect.sizeDelta = new Vector2(width, height);
    }

    [MenuItem("Retry/Extraction/Validate Content")]
    public static void Validate()
    {
        Directory.CreateDirectory(Artifacts);
        var errors = new List<string>();
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/CombatContent" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
        paths.Add("Assets/Prefabs/LocalPlayerPrefab.prefab");
        paths.Add("Assets/Resources/ServerLootUI.prefab");
        foreach (string path in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) { errors.Add("Missing prefab " + path); continue; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) errors.Add("Missing script " + path + "/" + t.name);
                foreach (var r in t.GetComponents<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m == null || m.shader == null || !m.shader.isSupported) errors.Add("Missing/unsupported material " + path + "/" + t.name);
            }
        }
        ExportPacketLayout();
        string report = "Unity " + Application.unityVersion + "\nCompiled editor assemblies loaded.\nChecked prefabs: " + paths.Count + "\n" + string.Join("\n", errors);
        File.WriteAllText(Path.Combine(Artifacts, "unity-validation.txt"), report);
        if (errors.Count != 0) throw new InvalidOperationException(report);
        Debug.Log("[ExtractionValidation] PASS: " + paths.Count + " prefabs, protocol layouts exported.");
    }

    private static void ExportPacketLayout()
    {
        var lines = new List<string>();
        foreach (PacketType value in Enum.GetValues(typeof(PacketType))) lines.Add("enum," + value + "," + (int)value);
        foreach (var t in typeof(PacketType).Assembly.GetTypes().Where(t => t.IsValueType && !t.IsEnum && t.StructLayoutAttribute?.Pack == 1))
        {
            lines.Add("size," + t.Name + "," + Marshal.SizeOf(t));
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                lines.Add("field," + t.Name + "," + f.Name + "," + Marshal.OffsetOf(t, f.Name));
        }
        File.WriteAllLines(Path.Combine(Artifacts, "client-packet-layout.csv"), lines);
    }

    [MenuItem("Retry/Extraction/Build Windows Player")]
    public static void BuildPlayer()
    {
        Directory.CreateDirectory(Artifacts);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = Path.Combine(Artifacts, "WindowsPlayer", "Retry.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        File.WriteAllText(Path.Combine(Artifacts, "unity-player-build.txt"), report.summary.result + " errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings + " duration=" + report.summary.totalTime);
        ExportBuildWarnings(report);
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Player build: " + report.summary.result);
    }

    [MenuItem("Retry/Demo/Build Windows Demo Player")]
    public static void BuildDemoPlayer()
    {
        string output=Path.GetFullPath(Path.Combine(Root,"..","BuildClient_DemoCheats_20261110","Retry.exe"));
        Directory.CreateDirectory(Path.Combine(Artifacts,"DemoCheats1110"));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
            locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None
        });
        File.WriteAllText(Path.Combine(Artifacts,"DemoCheats1110","build-result.txt"),report.summary.result+" errors="+report.summary.totalErrors+" warnings="+report.summary.totalWarnings+" duration="+report.summary.totalTime);
        if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Demo build: "+report.summary.result);
    }

    private static void ExportBuildWarnings(BuildReport report)
    {
        if (report == null) throw new InvalidOperationException("No Unity build report available.");
        var warnings = report.steps.SelectMany(step => step.messages)
            .Where(message => message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
            .Select(message => message.type + ": " + message.content);
        File.WriteAllLines(Path.Combine(Artifacts, "unity-player-build-warnings.txt"), warnings);
    }
}
