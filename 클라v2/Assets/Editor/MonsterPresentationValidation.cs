using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MonsterPresentationValidation
{
    const string Output = "Artifacts/MonsterPresentation";
    static MonsterPresentationValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(Output + "/build.request")) return;
        File.Delete(Output + "/build.request");
        try { BuildAndValidate(); File.WriteAllText(Output + "/result.txt", "PASS"); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", e.ToString()); Debug.LogException(e); }
    }
    static void BuildAndValidate()
    {
        KnightDeathBuilder.Build();
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(KnightGreatswordBuilder.Folder + "/KnightElite.controller");
        var death = controller.layers[0].stateMachine.states.First(s => s.state.name == "Die").state;
        death.speed = 1; EditorUtility.SetDirty(controller);
        var knight = PrefabUtility.LoadPrefabContents(KnightGreatswordBuilder.KnightPath);
        try { knight.transform.localScale = Vector3.one * 1.3f; PrefabUtility.SaveAsPrefabAsset(knight, KnightGreatswordBuilder.KnightPath); }
        finally { PrefabUtility.UnloadPrefabContents(knight); }
        AssetDatabase.SaveAssets();
        KnightGreatswordValidation.Render();
        Validate();
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("PASS " + message); }
    static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    static void Validate()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            Check(ABItemDefinitions.Find(ItemHash.Of("beam_greatsword")) == null && ItemCatalog.Load().Resolve("beam_greatsword") == null, "retired weapon is absent");
            var host = new GameObject("Server monster presentation test"); SceneManager.MoveGameObjectToScene(host, scene);
            var registry = host.AddComponent<RemoteMonsterRegistry>();
            registry.MonsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monster1.prefab");
            registry.BossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CombatContent/Prefabs/DungeonGuardian.prefab");
            var actor = Enter(registry, 1, MonsterKind.ELITE, 300);
            Check(Mathf.Abs(actor.transform.localScale.x - 1.3f) < .001f, "knight is 30 percent larger");
            var outline = actor.GetComponent<EliteMonsterOutline>(); Call(outline, "OnEnable");
            Check(actor.GetComponentsInChildren<Renderer>().Any(r => r.name == "Elite Outline Hull"), "elite outline hulls created");
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("EliteOutline")), "outline shader compiles");
            var alert = actor.GetComponent<MonsterAlertEffect>(); Call(alert, "Awake");
            registry.OnMove(new MonsterMove { monsterId = 1, aiState = (int)MonsterAiState.CHASE });
            Check(Field<Canvas>(alert, "canvas").enabled, "server chase displays alert");
            Set(alert, "visibleTimer", .4f); Set(alert, "cooldownTimer", 0f);
            registry.OnMove(new MonsterMove { monsterId = 1, aiState = (int)MonsterAiState.ATTACK });
            Check(Field<float>(alert, "visibleTimer") == .4f, "chase to attack does not repeat alert");
            registry.OnMove(new MonsterMove { monsterId = 1, aiState = (int)MonsterAiState.IDLE });
            registry.OnMove(new MonsterMove { monsterId = 1, aiState = (int)MonsterAiState.CHASE });
            Check(Field<float>(alert, "visibleTimer") > 1, "reacquiring target displays alert again");
            foreach (var kind in new[] { MonsterKind.ELITE, MonsterKind.NORMAL, MonsterKind.BOSS })
            {
                int id = kind == MonsterKind.ELITE ? 1 : kind == MonsterKind.NORMAL ? 2 : 5;
                var go = id == 1 ? actor : Enter(registry, id, kind, 100);
                var animation = go.GetComponent<Monster_Animation>(); Call(animation, "Awake");
                var animator = go.GetComponentInChildren<Animator>(); animator.Rebind(); animator.Update(0);
                var view = go.AddComponent<ABActorView>(); Call(view, "Awake");
                view.Apply(new ABActorState { entityId = -id, statusMask = 4, attackSpeed = 1 }); Call(view, "LateUpdate");
                registry.OnDied(id); Call(animation, "Update");
                Check(animator.speed == 1, kind + " death ignores stun");
                for (int frame = 0; frame < 180; frame++) { animator.Update(1f / 30); Call(animation, "LateUpdate"); Call(view, "LateUpdate"); }
                Check(go.GetComponent<Monster_State>().IsDead && animator.speed == 0 && animator.GetCurrentAnimatorStateInfo(0).IsName("Die"), kind + " keeps final death state");
                var bones = go.GetComponentsInChildren<Transform>().ToArray(); var poses = bones.Select(t => t.localPosition).ToArray();
                registry.OnMove(new MonsterMove { monsterId = id, aiState = (int)MonsterAiState.CHASE, posX = 10 });
                registry.OnMonsterAttack(new MonsterAttackEvent { monsterId = id });
                animator.Update(3); Call(animation, "LateUpdate"); Call(view, "LateUpdate");
                Check(animator.speed == 0 && bones.Select((t, i) => Vector3.Distance(t.localPosition, poses[i])).Max() < .001f, kind + " delayed movement and attacks do not revive corpse");
                Check(!Field<Canvas>(go.GetComponent<MonsterAlertEffect>(), "canvas").enabled, kind + " dead alert hidden");
            }
            // Re-enter view after death: apply the final pose immediately.
            var corpse = Enter(registry, 3, MonsterKind.ELITE, 0);
            var corpseAnimation = corpse.GetComponent<Monster_Animation>(); Call(corpseAnimation, "Awake");
            corpse.GetComponentInChildren<Animator>().Rebind(); corpseAnimation.ShowCorpse();
            Check(corpse.GetComponentInChildren<Animator>().speed == 0, "already dead entry holds corpse pose");
            var live = Enter(registry, 4, MonsterKind.ELITE, 300);
            var liveAnimator = live.GetComponentInChildren<Animator>(); liveAnimator.Rebind(); liveAnimator.Update(0);
            Call(live.GetComponent<EliteMonsterOutline>(), "OnEnable");
            actor.transform.position = new Vector3(1.7f, 0, .8f); live.transform.position = new Vector3(-1.4f, 0, 0);
            registry.GetActor(2).SetActive(false); registry.GetActor(5).SetActive(false); corpse.SetActive(false);
            Capture(scene, live, actor);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static GameObject Enter(RemoteMonsterRegistry registry, int id, MonsterKind kind, int hp)
    {
        registry.OnEnterView(new MonsterEnterView { monsterId = id, monsterKind = (int)kind, hp = hp, maxHp = 300 });
        var go = registry.GetActor(id);
        // Edit-mode fixtures initialize the same components Awake initializes in play mode.
        if (hp > 0) { Call(go.GetComponent<Monster_State>(), "Awake"); go.GetComponent<Monster_State>().NetworkSetMax(300, hp); }
        Call(go.GetComponent<MonsterAlertEffect>(), "Awake");
        return go;
    }
    static void Capture(Scene scene, GameObject live, GameObject corpse)
    {
        var camera = new GameObject("Preview Camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
        camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position = new Vector3(5, 5, 8); camera.transform.LookAt(new Vector3(0, 1, 0)); camera.orthographic = true; camera.orthographicSize = 3.3f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .15f, .2f);
        var light = new GameObject("Preview Light").AddComponent<Light>(); SceneManager.MoveGameObjectToScene(light.gameObject, scene);
        light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var alert = live.GetComponent<MonsterAlertEffect>(); Set(alert, "targetCamera", camera); alert.Show();
        foreach (var canvas in corpse.GetComponentsInChildren<Canvas>()) canvas.enabled = false;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ground, scene);
        ground.transform.position = Vector3.down * .1f; ground.transform.localScale = new Vector3(15, .2f, 15);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(.2f, .23f, .27f); ground.GetComponent<Renderer>().sharedMaterial = material;
        var baked = new List<Mesh>();
        foreach (var skin in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>()))
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh, true); baked.Add(mesh);
            var copy = new GameObject("Preview pose"); copy.transform.SetParent(skin.transform, false);
            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = copy.AddComponent<MeshRenderer>(); renderer.sharedMaterials = skin.sharedMaterials; renderer.shadowCastingMode = skin.shadowCastingMode; skin.enabled = false;
        }
        var rt = new RenderTexture(1280, 800, 24); var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false); var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply(); File.WriteAllBytes(Output + "/elite-and-corpse.png", texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(material); foreach (var mesh in baked) Object.DestroyImmediate(mesh); }
    }
}
