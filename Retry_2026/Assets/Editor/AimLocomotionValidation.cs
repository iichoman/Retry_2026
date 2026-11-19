using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AimLocomotionValidation
{
    const string Key = "Retry.AimLocomotionValidation";
    const string Output = KnightLocomotionBuilder.Output;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static IEnumerator routine;
    static int frame;
    static string failure;
    static double began;
    static NavMeshDataInstance navigation;
    static AimLocomotionValidation() { EditorApplication.playModeStateChanged += Mode; EditorApplication.update += Tick; }
    public static void Batch()
    {
        try
        {
            KnightLocomotionBuilder.Build();
            File.WriteAllText(Output + "/validation.txt", "RUNNING\n");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.playModeStartScene = null;
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Output + "/validation.txt", "FAIL " + e); EditorApplication.Exit(1); }
    }
    static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { routine = Run(); frame = -1; began = EditorApplication.timeSinceStartup; }
        if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key, false); EditorApplication.Exit(failure == null ? 0 : 1); }
    }
    static void Tick()
    {
        if (routine == null || !EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 60) throw new TimeoutException();
            if (routine.MoveNext()) return;
        }
        catch (Exception e) { failure = e.ToString(); Debug.LogException(e); }
        routine = null;
        if (navigation.valid) navigation.Remove();
        File.AppendAllText(Output + "/validation.txt", failure == null ? "PASS all aim, offline root motion, online gait and attack-isolation checks\n" : "FAIL " + failure);
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); File.AppendAllText(Output + "/validation.txt", "PASS " + label + "\n"); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static IEnumerator Run()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        // Real gameplay components, with their automatic updates disabled for deterministic camera input.
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
        foreach (var b in player.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        var input = player.GetComponent<Defalult_Input>();
        var rig = player.GetComponent<Player_Camera_Controller>();
        var lens = new GameObject("Aim lens").AddComponent<Camera>(); lens.tag = "MainCamera";
        foreach (var c in player.GetComponentsInChildren<Camera>(true)) { c.enabled = false; c.tag = "Untagged"; }
        Set(rig, "cameraTransform", lens.transform); Set(rig, "cameraAction", null); Set(rig, "lockOnSystem", null);
        Call(rig, "Start"); Set(rig, "aimWeight", 1f); Set(rig, "aimYaw", 0f); Set(rig, "aimPitch", 12f); Set(rig, "currentZoomDistance", 2.2f);
        var skills = new GameObject("Aim test").AddComponent<SkillClient>(); skills.enabled = false; Set(skills, "aiming", true);
        Set(input, "lookInput", new Vector2(20, -8));
        Check(input.Look.sqrMagnitude > 1, "Q aiming accepts mouse look input");
        Set(rig, "lookInput", input.Look); Call(rig, "HandleAimLook"); Call(rig, "HandleFollow"); Call(skills, "UpdateAim", player);
        var targetA = Get<Vector3>(skills, "aimPoint");
        Check(Get<float>(rig, "aimYaw") > 1 && Get<float>(rig, "aimPitch") > 12, "aim camera rotates horizontally and vertically");
        Set(rig, "lookInput", new Vector2(-40, 0)); Call(rig, "HandleAimLook"); Call(rig, "HandleFollow"); Call(skills, "UpdateAim", player);
        Check(Vector3.Distance(targetA, Get<Vector3>(skills, "aimPoint")) > 1, "ground target follows camera centre instead of free cursor");
        Set(skills, "aiming", false); Set(skills, "pending", true); Set(skills, "pendingPoint", true);
        Check(SkillClient.IsJudgementPending, "aim framing survives the pending server response");
        Set(skills, "pending", false);
        var view = player.GetComponent<PlayerSkillView>() ?? player.AddComponent<PlayerSkillView>();
        view.Begin(new SkillEvent { serial = 1, skill = 4, windup = 1.2f, remaining = 1.65f, dz = 1, area = 1 });
        Check(view.IsGreatswordQ && PlayerSkillView.BlocksMovement(player) && input.Look.sqrMagnitude > 1, "Q cast permits camera look while retaining the movement lock");
        view.Cancel(); Object.Destroy(player); Object.Destroy(skills.gameObject); Object.Destroy(lens.gameObject);
        yield return null;

        var settings = NavMesh.GetSettingsByIndex(0);
        var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(40,.4f,40), transform = Matrix4x4.TRS(Vector3.down*.2f, Quaternion.identity, Vector3.one), area = 0 } };
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(45,8,45)), Vector3.zero, Quaternion.identity);
        navigation = NavMesh.AddNavMeshData(data);
        var knight = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KnightGreatswordBuilder.KnightPath));
        knight.GetComponent<Monster>().enabled = false;
        knight.GetComponent<Monster_Attack>().enabled = false;
        var agent = knight.GetComponent<NavMeshAgent>();
        var animation = knight.GetComponentInChildren<Animator>();
        var locomotion = knight.GetComponentInChildren<KnightLocomotion>();
        yield return null;
        Check(agent.isOnNavMesh && locomotion.WorldCycleDistance > 1, "knight has NavMesh and measured stride");
        agent.isStopped = false; agent.SetDestination(Vector3.forward * 12);
        float start = Time.time; Vector3 origin = knight.transform.position;
        while (Time.time - start < 2.5f) yield return null;
        float moved = Vector3.Distance(origin, knight.transform.position);
        Check(!agent.updatePosition && moved > 1.5f && moved < 3.2f, $"offline root motion moves through NavMesh: {moved:F2}m in 2.5s");
        Check(animation.GetFloat("WalkRate") > 1.4f && animation.GetFloat("WalkRate") < 1.9f, "chase gait accelerates to match the authored stride");
        agent.isStopped = true; agent.ResetPath(); origin = knight.transform.position;
        start = Time.time; while (Time.time - start < .4f) yield return null;
        Check(Vector3.Distance(origin, knight.transform.position) < .02f, "stopping leaves no residual root displacement");
        agent.enabled = false; animation.applyRootMotion = false;
        yield return null;
        var server = knight.AddComponent<KnightServerMotionFixture>(); server.Speed = 1f;
        start = Time.time; while (Time.time - start < 1f) yield return null;
        float fullRate = animation.GetFloat("WalkRate");
        Check(fullRate > 1.4f && fullRate < 1.9f && server.DriftFrames == 0, "online gait follows server-position displacement without moving the actor twice");
        server.Speed = .5f;
        start = Time.time; while (Time.time - start < 1f) yield return null;
        float slowRate = animation.GetFloat("WalkRate");
        Check(Mathf.Abs(slowRate / fullRate - .5f) < .12f, $"slow effect halves gait playback: {fullRate:F3}x -> {slowRate:F3}x");
        CaptureKnight(knight);
        var controller = animation.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        foreach (var state in controller.layers[0].stateMachine.states)
            if (state.state.name != "Locomotion") Check(!state.state.speedParameterActive, state.state.name + " keeps independent attack/hit/death timing");
        Object.Destroy(knight);
    }
    static void CaptureKnight(GameObject knight)
    {
        var camera = new GameObject("Walking preview").AddComponent<Camera>();
        camera.transform.position = knight.transform.position + new Vector3(5,3,4);
        camera.transform.LookAt(knight.transform.position + Vector3.up*1.4f);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f,.05f,.07f);
        var light = new GameObject("Walking light").AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.7f; light.transform.rotation = Quaternion.Euler(45,-35,0);
        CombatContentBuilder.CaptureCamera(camera, Output + "/walking.png");
    }
}

// Runs in the same Update phase as RemoteMonsterRegistry, before the gait's late Update.
public sealed class KnightServerMotionFixture : MonoBehaviour
{
    public float Speed;
    public int DriftFrames;
    Vector3 expected;
    void Update() { transform.position += Vector3.forward * Speed * Time.deltaTime; expected = transform.position; }
    void LateUpdate() { if (Vector3.Distance(transform.position, expected) > .001f) DriftFrames++; }
}
