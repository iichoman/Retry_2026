using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Connects the actual online prefab and the separate offline scene player template.</summary>
public static class PlayerDefenseContentBuilder
{
    private const string PlayerPath = "Assets/Prefabs/LocalPlayerPrefab.prefab";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private static readonly string Output = Path.GetFullPath("Artifacts/PlayerDefense");

    [MenuItem("Retry/Player Defense/Apply Faster Offline Dash")]
    public static void ApplyDashSpeed()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save SampleScene edits first.");
        var prefab = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            SetDashSpeed(prefab);
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects()) SetDashSpeed(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        AssetDatabase.SaveAssets();
    }

    private static void SetDashSpeed(GameObject root)
    {
        foreach (var defense in root.GetComponentsInChildren<PlayerDefenseController>(true))
        {
            var serialized = new SerializedObject(defense);
            serialized.FindProperty("offlineDashSpeed").floatValue = PlayerDefenseController.DefaultOfflineDashSpeed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defense);
        }
    }

    [MenuItem("Retry/Player Defense/Build and Validate")]
    public static void BuildAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Defense content must be built outside Play Mode.");
        Scene existing = SceneManager.GetSceneByPath(ScenePath);
        if (existing.IsValid() && existing.isLoaded && existing.isDirty)
            throw new InvalidOperationException("SampleScene contains unsaved edits. Save them before building defense content.");

        Directory.CreateDirectory(Output);
        var report = new List<string> { "Client-only defense content", "Server and protocol assets are not touched." };
        var prefab = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Connect(prefab, report, PlayerPath);
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        Scene scene = existing;
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int playerCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var movement in root.GetComponentsInChildren<Player_Movement>(true))
            {
                if (movement.GetComponent<Player_Attack>() == null) continue;
                Connect(movement.gameObject, report, ScenePath + "/" + movement.name);
                playerCount++;
            }
            if (playerCount == 0) throw new InvalidOperationException("Offline scene player template is missing.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Add("Connected offline scene players: " + playerCount);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        AssetDatabase.SaveAssets();
        RenderAndValidatePoses(report);
        File.WriteAllLines(Path.Combine(Output, "pose-validation.txt"), report);
        Debug.Log("[PlayerDefense] PASS: local prefab and offline player connected; pose preview and restoration validated.");
    }

    [MenuItem("Retry/Player Defense/Render Pose Preview")]
    public static void RenderPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Render the pose preview outside Play Mode.");
        Directory.CreateDirectory(Output);
        var report = new List<string>();
        RenderAndValidatePoses(report);
        File.WriteAllLines(Path.Combine(Output, "pose-preview-validation.txt"), report);
    }

    private static void Connect(GameObject player, List<string> report, string path)
    {
        var input = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (input != null && string.IsNullOrEmpty(input.defaultActionMap))
        {
            input.defaultActionMap = "Player";
            EditorUtility.SetDirty(input);
        }
        if (player.GetComponent<PlayerDefenseController>() == null) player.AddComponent<PlayerDefenseController>();
        var pose = player.GetComponent<PlayerDefensePose>();
        if (pose == null) pose = player.AddComponent<PlayerDefensePose>();
        if (!pose.ConfigureReferences()) throw new InvalidOperationException("Missing defense pose bone/blade references: " + path);
        var animator = player.GetComponentInChildren<Animator>(true);
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
            throw new InvalidOperationException("Expected a valid humanoid avatar: " + path);
        foreach (var node in player.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) != 0)
                throw new InvalidOperationException("Missing script: " + path + "/" + node.name);
        var serialized = new SerializedObject(pose);
        var property = serialized.GetIterator();
        while (property.NextVisible(true))
            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null)
                throw new InvalidOperationException("Missing pose reference: " + path + "/" + property.propertyPath);
        EditorUtility.SetDirty(pose);
        report.Add("PASS refs: " + path + " (14 body bones, blade base/tip, humanoid Animator)");
    }

    private static void RenderAndValidatePoses(List<string> report)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var stage = new GameObject("Player Defense Visual Preview");
        SceneManager.MoveGameObjectToScene(stage, scene);
        var activeScene = SceneManager.GetActiveScene();
        var ambient = RenderSettings.ambientLight;
        var ambientMode = RenderSettings.ambientMode;
        Material floorMaterial = null;
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var labels = new[] { "Idle", "Dash", "Parry" };
            for (int i = 0; i < 3; i++)
            {
                var player = UnityEngine.Object.Instantiate(asset, stage.transform);
                player.name = labels[i];
                foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null) behaviour.enabled = false;
                foreach (var particle in player.GetComponentsInChildren<ParticleSystem>(true))
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var weapon in player.GetComponentsInChildren<Weapon_Gun>(true)) weapon.gameObject.SetActive(false);
                // Camera looks down -Z, so its screen-right is world -X.
                player.transform.position = new Vector3((1 - i) * 2f, 0f, 0f);
                var animator = player.GetComponent<Animator>();
                animator.Rebind();
                animator.Update(0f);
                animator.applyRootMotion = false;
                var pose = player.GetComponent<PlayerDefensePose>();
                pose.ConfigureReferences();
                ValidatePoseRestoration(player, pose, report);
                pose.PreviewPose(i == 1 ? 1f : 0f, i == 2 ? 1f : 0f, Vector3.forward);
                foreach (var mesh in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) mesh.updateWhenOffscreen = true;
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Preview Ground";
            floor.transform.SetParent(stage.transform);
            floor.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            floor.transform.localScale = new Vector3(9f, 0.2f, 5f);
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor", new Color(0.085f, 0.105f, 0.135f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var lightObject = new GameObject("Preview Key");
            lightObject.transform.SetParent(stage.transform);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.2f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var cameraObject = new GameObject("Preview Camera");
            cameraObject.transform.SetParent(stage.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 2.6f, 10f);
            camera.transform.LookAt(new Vector3(0f, 1f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 1.75f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.045f, 0.065f);
            camera.scene = scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.cullingMask = 1 << 31;
            CreatePreviewLabels(stage.transform, camera);
            foreach (var node in stage.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.56f);
            SaveImage(camera, Path.Combine(Output, "Idle_Dash_Parry.png"));
            report.Add("Preview: Idle_Dash_Parry.png (left: idle, center: dash, right: parry; actual character and sword meshes)");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stage);
            if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
            EditorSceneManager.ClosePreviewScene(scene);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientMode = ambientMode;
        }
    }

    private static void CreatePreviewLabels(Transform parent, Camera camera)
    {
        var labelObject = new GameObject("Preview Labels", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        labelObject.transform.SetParent(parent, false);
        var canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 100;
        var scaler = labelObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 810);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        AddPreviewLabel(labelObject.transform, font, "CLIENT DEFENSE / TEMPORARY POSES", 0.5f, 0.958f, 1200f, 36f, 28, Color.white);
        AddPreviewLabel(labelObject.transform, font, "Existing character rig  |  No animation clips  |  Physics root unchanged", 0.5f, 0.912f, 1200f, 26f, 18, new Color(0.67f, 0.74f, 0.84f));
        string[] titles = { "IDLE", "DASH", "SWORD PARRY" };
        string[] descriptions = { "Original animation", "Lower stance + directional lean", "Blade raised in front of chest" };
        for (int i = 0; i < titles.Length; i++)
        {
            float horizontal = 0.5f + (i - 1) * 0.32f;
            AddPreviewLabel(labelObject.transform, font, titles[i], horizontal, 0.15f, 430f, 40f, 28,
                i == 0 ? new Color(0.8f, 0.84f, 0.91f) : new Color(0.94f, 0.77f, 0.4f));
            AddPreviewLabel(labelObject.transform, font, descriptions[i], horizontal, 0.104f, 430f, 30f, 18,
                new Color(0.7f, 0.77f, 0.86f));
        }
    }

    private static void AddPreviewLabel(Transform parent, Font font, string content, float x, float y,
        float width, float height, int fontSize, Color color)
    {
        var go = new GameObject(content, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(x, y);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.text = content;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private static void ValidatePoseRestoration(GameObject player, PlayerDefensePose pose, List<string> report)
    {
        var nodes = player.GetComponentsInChildren<Transform>(true);
        var positions = new Vector3[nodes.Length];
        var rotations = new Quaternion[nodes.Length];
        for (int i = 0; i < nodes.Length; i++) { positions[i] = nodes[i].localPosition; rotations[i] = nodes[i].localRotation; }
        Vector3 rootPosition = player.transform.position;
        Quaternion rootRotation = player.transform.rotation;
        var capsule = player.GetComponent<CharacterController>();
        float radius = capsule.radius, height = capsule.height;
        Vector3 center = capsule.center;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            pose.PreviewPose(1f, 0f, iteration % 2 == 0 ? Vector3.forward : Vector3.right);
            pose.PreviewPose(0f, 1f, Vector3.forward);
        }
        if ((player.transform.position - rootPosition).sqrMagnitude > 0.000001f || Quaternion.Angle(player.transform.rotation, rootRotation) > 0.01f ||
            radius != capsule.radius || height != capsule.height || center != capsule.center)
            throw new InvalidOperationException("Visual pose modified the physics root/capsule.");
        pose.ResetPose();
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!float.IsFinite(nodes[i].localPosition.sqrMagnitude) ||
                (nodes[i].localPosition - positions[i]).sqrMagnitude > 0.000001f || Quaternion.Angle(nodes[i].localRotation, rotations[i]) > 0.05f)
                throw new InvalidOperationException("Pose restoration failed: " + nodes[i].name);
        }
        report.Add("PASS pose reset: " + player.name + ", 200 alternating poses; no transform drift/root/capsule change");
    }

    private static void SaveImage(Camera camera, string path)
    {
        var renderTarget = new RenderTexture(1440, 810, 24);
        var image = new Texture2D(1440, 810, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTarget;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = renderTarget });
            RenderTexture.active = renderTarget;
            image.ReadPixels(new Rect(0, 0, 1440, 810), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(renderTarget);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
