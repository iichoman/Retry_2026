using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Samples the shipped humanoid sword clips without changing scenes or gameplay state.</summary>
public static class SwordAlignmentValidation
{
    private const string PlayerPath = "Assets/Prefabs/LocalPlayerPrefab.prefab";
    private const string FirstClipPath = "Assets/GameObjects/Player/Player_Model/chA/Sword1.fbx";
    private static string Output => Path.GetFullPath("Artifacts/SwordAlignment");

    [MenuItem("Retry/Player/Fix and Validate Sword Facing")]
    public static void BuildAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        const string scenePath = "Assets/Scenes/SampleScene.unity";
        var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
            throw new InvalidOperationException("Save SampleScene changes before configuring sword facing.");
        Directory.CreateDirectory(Output);
        var report = new List<string> { "Sword facing validation: real assets/input/animation; no server/protocol changes." };
        foreach (string path in new[] { PlayerPath, "Assets/Prefabs/RemotePlayerPrefab.prefab" })
        {
            Backup(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.GetComponent<Animator>().applyRootMotion = false;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Check(!root.GetComponent<Animator>().applyRootMotion, "Serialized root motion disabled: " + path, report);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        bool opened = !loaded.IsValid() || !loaded.isLoaded;
        if (opened) loaded = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            Backup(scenePath);
            int count = 0;
            foreach (var root in loaded.GetRootGameObjects())
            foreach (var player in root.GetComponentsInChildren<Player_Animation>(true))
            {
                var animator = player.GetComponent<Animator>();
                if (animator == null) continue;
                animator.applyRootMotion = false;
                EditorUtility.SetDirty(animator);
                count++;
            }
            Check(count > 0, "Offline SampleScene player root motion disabled", report);
            EditorSceneManager.MarkSceneDirty(loaded);
            EditorSceneManager.SaveScene(loaded);
        }
        finally { if (opened) EditorSceneManager.CloseScene(loaded, true); }
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (string path in new[] { PlayerPath, "Assets/Prefabs/RemotePlayerPrefab.prefab" })
            {
                var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player, scene);
                foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour != null) behaviour.enabled = false;
                foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                try
                {
                    ValidateClips(player, scene, path == PlayerPath ? "Local" : "Remote", report);
                }
                finally { UnityEngine.Object.DestroyImmediate(player); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        File.WriteAllLines(Path.Combine(Output, "validation.txt"), report);
        Debug.Log("[SwordAlignment] PASS: all four sword clips preserve local/remote roots. Live input is covered by PlayerDefenseValidation.");
    }

    private static void ValidateClips(GameObject player, UnityEngine.SceneManagement.Scene scene, string label, List<string> report)
    {
        var animator = player.GetComponent<Animator>();
        animator.applyRootMotion = true; // Cover old prefabs/scenes that have not been rebuilt, too.
        Invoke(player.GetComponent<Player_Animation>(), "Awake");
        Check(!animator.applyRootMotion, label + " Player_Animation initialization disables imported root motion", report);
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var runtime = animator.runtimeAnimatorController;
        var clips = runtime.animationClips;
        animator.runtimeAnimatorController = null;
        for (int combo = 1; combo <= 4; combo++)
        {
            string clipName = "Sword" + combo;
            var clip = clips.First(c => string.Equals(c.name, clipName, StringComparison.OrdinalIgnoreCase));
            Check(clip.events.Any(e => e.functionName == "AnimEvent_BeginSwing") &&
                clip.events.Any(e => e.functionName == "AnimEvent_EndSwing"), label + " combo " + combo + " retains swing events", report);
            var graph = PlayableGraph.Create("Sword facing validation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Sword", animator).SetSourcePlayable(playable);
                graph.Play();
                foreach (float heading in new[] { 0f, 90f, 180f, 270f })
                {
                    var rotation = Quaternion.Euler(0, heading, 0);
                    player.transform.SetPositionAndRotation(Vector3.zero, rotation);
                    for (int frame = 0; frame < 120; frame++)
                    {
                        playable.SetTime(frame / 120d * clip.length); graph.Evaluate(0f);
                        if (Quaternion.Angle(player.transform.rotation, rotation) > .001f || player.transform.position.sqrMagnitude > .000001f)
                            throw new InvalidOperationException(label + " combo " + combo + " modifies the gameplay root at heading " + heading);
                    }
                    Check(true, label + " combo " + combo + " 120 animation samples preserve root at " + heading + " degrees", report);
                }
                if (label == "Local" && combo == 1)
                {
                    player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    playable.SetTime(.4330097 * clip.length); graph.Evaluate(0);
                    RenderSample(player, scene, "Aligned_First_Swing");
                    var pose = player.GetComponent<PlayerDefensePose>();
                    Check(pose != null && pose.ConfigureReferences(), "Defense pose bone/blade references remain connected", report);
                    pose.PreviewPose(0, 1, Vector3.forward); pose.ResetPose();
                    Check(player.transform.position == Vector3.zero && Quaternion.Angle(player.transform.rotation, Quaternion.identity) < .001f,
                        "Parry preview and reset preserve the gameplay root", report);
                }
            }
            finally { graph.Destroy(); }
        }
    }

    private static void Invoke(MonoBehaviour component, string method) => component.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
    private static void Check(bool condition, string description, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.Add("PASS " + description);
    }
    private static void Backup(string asset)
    {
        string target = Path.GetFullPath(Path.Combine("..", "WorkBackup_MapSword_20260930", "Retry_2026", asset));
        if (File.Exists(target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(asset, target);
    }

    [MenuItem("Retry/Player/Sample Sword Alignment")]
    public static void Analyze()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        Directory.CreateDirectory(Output);
        var report = new List<string> { "Sword animation diagnostic (degrees; positive = right)",
            "state,mode,t,rootYaw,bodyYaw,bladeTipYaw,bladeX,bladeZ" };
        var scene = EditorSceneManager.NewPreviewScene();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var first = AssetDatabase.LoadAllAssetsAtPath(FirstClipPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        try
        {
            for (int attack = 1; attack <= 4; attack++)
            for (int mode = 0; mode < (attack == 1 ? 4 : 1); mode++)
            {
                var player = UnityEngine.Object.Instantiate(asset);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player, scene);
                foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour != null) behaviour.enabled = false;
                foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                var animator = player.GetComponent<Animator>();
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = mode == 3;
                AnimationClip copy = null;
                var source = attack == 1 ? first : AssetDatabase.LoadAllAssetsAtPath("Assets/GameObjects/Player/Player_Model/swordAnimation.fbx")
                    .OfType<AnimationClip>().First(c => c.name == "sword" + attack);
                if (mode == 1 || mode == 2)
                {
                    copy = UnityEngine.Object.Instantiate(first);
                    var settings = AnimationUtility.GetAnimationClipSettings(copy);
                    if (mode == 1) settings.orientationOffsetY += 30;
                    else settings.keepOriginalOrientation = true;
                    AnimationUtility.SetAnimationClipSettings(copy, settings);
                }
                animator.runtimeAnimatorController = null;
                var graph = PlayableGraph.Create("Sword Alignment Preview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = copy != null ? copy : source;
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Sword", animator).SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0);
                var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                var tip = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "Blade Trail Tip");
                string label = new[] { "shipped", "offset+30", "original", "rootMotion" }[mode];
                foreach (float time in new[] { 0f, .2f, .37864077f, .405f, .4330097f, .6f, .8f })
                {
                    player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    playable.SetTime(time * clip.length);
                    graph.Evaluate(0f);
                    if (mode == 3) graph.Evaluate(.01667f);
                    Vector3 bodyForward = Vector3.Cross(right.position - left.position, Vector3.up);
                    Vector3 blade = tip.position - player.transform.position;
                    report.Add(string.Join(",", attack, label, F(time), F(Yaw(player.transform.forward)),
                        F(Yaw(bodyForward)), F(Yaw(blade)), F(blade.x), F(blade.z)));
                    if (attack == 1 && Mathf.Approximately(time, .405f))
                        RenderSample(player, scene, label);
                }
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(player);
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        File.WriteAllLines(Path.Combine(Output, "analysis.csv"), report);
        Debug.Log("[SwordAlignment] Diagnostic written to " + Output);
    }

    private static void RenderSample(GameObject player, UnityEngine.SceneManagement.Scene scene, string label)
    {
        foreach (var weapon in player.GetComponentsInChildren<Weapon_Gun>(true)) weapon.gameObject.SetActive(false);
        foreach (var particle in player.GetComponentsInChildren<ParticleSystem>(true)) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", Color.cyan);
        var forward = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(forward, scene);
        forward.transform.position = new Vector3(0, .01f, 1.25f);
        forward.transform.localScale = new Vector3(.04f, .02f, 2.5f);
        forward.GetComponent<Renderer>().sharedMaterial = material;
        var lightObject = new GameObject("Preview Key");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 2;
        lightObject.transform.rotation = Quaternion.Euler(50, -25, 0);
        var cameraObject = new GameObject("Preview Camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 7f, -3f);
        camera.transform.LookAt(new Vector3(0, .4f, .6f));
        camera.orthographic = true; camera.orthographicSize = 2f;
        camera.backgroundColor = new Color(.12f, .13f, .16f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.scene = scene;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        var target = new RenderTexture(800, 800, 24);
        var image = new Texture2D(800, 800, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 800, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, label + ".png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(forward); UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    private static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
