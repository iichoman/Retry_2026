using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ClientBossContentBuilder
{
    const string ScenePath = "Assets/Scenes/BossPreview.unity";
    const string BatchKey = "Retry.ClientBoss.Batch";
    static ClientBossContentBuilder() { EditorApplication.update += MonitorBatch; }

    [MenuItem("Tools/Retry/Boss/Open Client Preview")]
    public static void Open()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(ScenePath)) Build();
        EditorSceneManager.OpenScene(ScenePath);
    }

    public static void Build()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>("Assets/Resources/CombatVfxCatalog.asset");
        if (catalog == null || catalog.BossPrefab == null || catalog.BossPrefab.GetComponent<BossVfxController>() == null)
            throw new InvalidOperationException("Boss catalog/prefab/controller missing.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Client Boss Presentation Preview");
        root.AddComponent<BossPresentationPreview>().Catalog = catalog;
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/CombatContent/Materials/Preview_Floor.mat");
        if (material == null) throw new InvalidOperationException("Preview floor material missing.");
        Block("Arena Floor", new Vector3(0,-.25f,0),new Vector3(48,.5f,48),material);
        Block("North Wall",new Vector3(0,1,24),new Vector3(49,2,1),material);
        Block("South Wall",new Vector3(0,1,-24),new Vector3(49,2,1),material);
        Block("East Wall",new Vector3(24,1,0),new Vector3(1,2,48),material);
        Block("West Wall",new Vector3(-24,1,0),new Vector3(1,2,48),material);
        var light = new GameObject("Preview Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(55,-35,0);
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.transform.position = new Vector3(-19,23,-24);
        camera.transform.LookAt(new Vector3(0,0,5)); camera.fieldOfView = 55;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.045f,.07f);
        camera.gameObject.AddComponent<AudioListener>();
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/ClientBoss");
        File.WriteAllText("Artifacts/ClientBoss/content-validation.txt", "PASS: real boss prefab/controller/catalog and floor material resolved; preview scene saved. No network bootstrap in preview. Production scene unchanged.\n");
    }

    static void Block(string name,Vector3 position,Vector3 scale,Material material)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = name;
        block.transform.position = position; block.transform.localScale = scale;
        block.GetComponent<Renderer>().sharedMaterial = material;
    }

    // No -quit: PlayMode test spans a domain reload; monitor exits after its report is complete.
    public static void RunBatch()
    {
        Build();
        // Keep preview Start and its global pool out of the isolated runtime fixture.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(BatchKey,true);
        CombatVfxRuntimeValidation.Start();
    }
    static void MonitorBatch()
    {
        if (!SessionState.GetBool(BatchKey,false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string reportPath = "Artifacts/CombatVfx/runtime-validation.txt";
        if (!File.Exists(reportPath) || File.ReadAllText(reportPath).StartsWith("RUNNING")) return;
        SessionState.SetBool(BatchKey,false);
        try
        {
            string report = File.ReadAllText(reportPath);
            if (!report.StartsWith("PASS")) throw new InvalidOperationException(report);
            ExtractionContentBuild.BuildPlayer();
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
