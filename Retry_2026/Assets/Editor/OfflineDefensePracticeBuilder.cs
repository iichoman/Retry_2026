using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OfflineDefensePracticeBuilder
{
    public const string ScenePath = "Assets/Scenes/DefensePractice.unity";
    [MenuItem("Retry/Player Defense/Open Offline Practice Scene")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene before opening practice.");
        EditorSceneManager.OpenScene(ScenePath);
    }

    public static void ReturnToSample()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
    }
    [MenuItem("Retry/Player Defense/Build Offline Practice Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            string folder = "Assets/PlayerDefense";
            Directory.CreateDirectory(folder);
            var stone = Material(folder + "/PracticeStone.mat", new Color(.16f,.2f,.25f));
            var clay = Material(folder + "/PracticeDummy.mat", new Color(.6f,.3f,.12f));
            Cube("Floor", new Vector3(0,-.5f,0), new Vector3(22,1,22), stone);
            Cube("North Wall", new Vector3(0,1.5f,10), new Vector3(22,3,.3f), stone);
            Cube("West Wall", new Vector3(-10,1.5f,0), new Vector3(.3f,3,22), stone);
            Cube("East Wall", new Vector3(10,1.5f,0), new Vector3(.3f,3,22), stone);
            Cube("South Wall", new Vector3(0,1.5f,-10), new Vector3(22,3,.3f), stone);
            Cube("Dash Obstacle", new Vector3(-3,.75f,0), new Vector3(2,1.5f,2), stone);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0,4,-7); cam.transform.LookAt(new Vector3(0,1,0));
            cam.GetComponent<Camera>().backgroundColor = new Color(.04f,.06f,.08f);
            var light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(50,-30,0);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.name = "Offline Practice Player"; player.transform.position = new Vector3(0,.05f,-2);
            // Fixed practice camera keeps mouse aiming from hiding the temporary pose.
            var cameraControl = player.GetComponent<Player_Camera_Controller>();
            if (cameraControl != null) { cameraControl.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(cameraControl); }
            var cameraAction = player.GetComponent<Player_Camera_Action>();
            if (cameraAction != null) { cameraAction.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(cameraAction); }
            player.SetActive(true);
            var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = "Offline Melee Practice Dummy"; dummy.transform.position = new Vector3(0,1,0);
            dummy.GetComponent<Renderer>().sharedMaterial = clay;
            var practice = new GameObject("Offline Defense Practice").AddComponent<OfflineDefensePractice>();
            var serialized = new SerializedObject(practice);
            serialized.FindProperty("player").objectReferenceValue = player.GetComponent<Player_State>();
            serialized.FindProperty("dummy").objectReferenceValue = dummy.transform;
            serialized.FindProperty("indicator").objectReferenceValue = dummy.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorSceneManager.CloseScene(scene,true);
            if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
        }
    }

    private static Material Material(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color); AssetDatabase.CreateAsset(material,path);
        }
        return material;
    }

    private static void Cube(string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name;
        go.transform.position=pos; go.transform.localScale=size;
        go.layer=LayerMask.NameToLayer("Wall"); go.GetComponent<Renderer>().sharedMaterial=mat;
    }
}
