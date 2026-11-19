using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run in the isolated batch project; uses real PhysX queries and the production controller.
[InitializeOnLoad]
public static class CameraCollisionValidation
{
    const string PlayBatchKey = "Retry.CameraCollision.PlayBatch";
    static CameraCollisionValidation() => EditorApplication.playModeStateChanged += PlayState;
    public static void BatchPlay()
    {
        SessionState.SetBool(PlayBatchKey, true);
        LockOnCameraValidation.Start();
    }
    static void PlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PlayBatchKey, false)) return;
        SessionState.SetBool(PlayBatchKey, false);
        var report = File.ReadAllText("Artifacts/LockOnCamera/checks.txt");
        EditorApplication.Exit(report.Contains("PASS all checks") && !report.Contains("FAIL") ? 0 : 1);
    }
    public static void Batch()
    {
        Directory.CreateDirectory("Artifacts/StoneWalls");
        try { File.WriteAllText("Artifacts/StoneWalls/camera-validation.txt", Run()); EditorApplication.Exit(0); }
        catch(Exception e) { File.WriteAllText("Artifacts/StoneWalls/camera-validation.txt", "FAIL\n" + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static T Call<T>(object target, string name, params object[] args) => (T)target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Check(bool value, string label) { if (!value) throw new Exception("Camera: " + label); }

    public static string Run()
    {
        var fixture = new GameObject("Camera collision regression");
        // Keep the test clear of the currently loaded asset scene, without changing scene state.
        var origin = new Vector3(10000, 100, 10000);
        var player = new GameObject("Camera owner"); player.transform.SetParent(fixture.transform); player.transform.position = origin;
        var camera = new GameObject("Test lens").AddComponent<Camera>(); camera.transform.SetParent(fixture.transform);
        camera.nearClipPlane = .1f; camera.fieldOfView = 60; camera.aspect = 16f / 9;
        var controller = player.AddComponent<Player_Camera_Controller>(); controller.enabled = false;
        var authored = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab").GetComponent<Player_Camera_Controller>();
        Set(controller, "collisionMask", (LayerMask)typeof(Player_Camera_Controller).GetField("collisionMask", Private).GetValue(authored));
        Set(controller, "cameraTransform", camera.transform); Set(controller, "cameraAction", null);
        Set(controller, "preventPathClipping", true); Set(controller, "input", null); Set(controller, "lockOnSystem", null);
        Call<object>(controller, "Start");
        float Radius() => (float)typeof(Player_Camera_Controller).GetProperty("EffectiveCollisionRadius", Private).GetValue(controller);
        Vector3 Clamp(Vector3 from, Vector3 to) => Call<Vector3>(controller, "ClampToClearSegment", from, to);
        bool Clear(Vector3 p) => Call<bool>(controller, "HasCameraClearance", p);
        BoxCollider Box(string name, Vector3 position, Vector3 size, int layer = 10)
        {
            var go = new GameObject(name); go.transform.SetParent(fixture.transform); go.transform.position = origin + position; go.layer = layer;
            var box = go.AddComponent<BoxCollider>(); box.size = size; return box;
        }
        void Follow() { Physics.SyncTransforms(); Call<object>(controller, "HandleFollow"); Check(Clear(camera.transform.position), "final lens does not overlap solids"); }
        try
        {
            var wall = Box("Wall", new Vector3(0, 2, -2), new Vector3(30, 8, .2f));
            var side = Box("Corner wall", new Vector3(-2, 2, 0), new Vector3(.2f, 8, 30));
            Physics.SyncTransforms();
            var pivot = origin + Vector3.up * 1.6f;
            var clipped = Clamp(pivot, pivot + Vector3.back * 7);
            Check(Clear(clipped) && clipped.z > origin.z - 1.9f + Radius(), "wall pulls camera forward immediately");
            // Start already intersecting the front face; a SphereCast alone misses this object.
            var recovered = Clamp(origin + new Vector3(0, 1.6f, -1.925f), pivot + Vector3.back * 7);
            Check(Clear(recovered) && recovered.z > origin.z - 1.9f, "overlapping cast origin recovers on player's side; result="+(recovered-origin)+", clear="+Clear(recovered)+", resolved="+(Call<Vector3>(controller,"ResolveCameraOverlap",origin+new Vector3(0,1.6f,-1.925f))-origin));
            recovered = Call<Vector3>(controller, "ResolveCameraOverlap", origin + new Vector3(-1.85f, 1.6f, -1.85f));
            Check(Clear(recovered), "two-wall corner depenetration");
            // Generated dungeon walls use a non-convex MeshCollider rather than a box.
            wall.gameObject.SetActive(false);
            var meshWall=GameObject.CreatePrimitive(PrimitiveType.Cube); meshWall.transform.SetParent(fixture.transform);
            meshWall.name="Original flat wall collision"; meshWall.layer=10; meshWall.transform.position=wall.transform.position; meshWall.transform.localScale=wall.size;
            Object.DestroyImmediate(meshWall.GetComponent<BoxCollider>());
            meshWall.AddComponent<MeshCollider>().sharedMesh=meshWall.GetComponent<MeshFilter>().sharedMesh;
            Physics.SyncTransforms();
            recovered=Clamp(origin+new Vector3(0,1.6f,-1.8f),pivot+Vector3.back*7);
            Check(Clear(recovered)&&recovered.z>origin.z-1.9f,"overlapping non-convex mesh surface recovers");
            camera.transform.position=wall.transform.position; Set(controller,"hasValidCameraPosition",true); Follow();
            Check(camera.transform.position.z>origin.z-1.9f,"camera inside mesh wall recovers from player anchor");
            Object.DestroyImmediate(meshWall); wall.gameObject.SetActive(true); Physics.SyncTransforms();

            var shelf = Box("Default-layer shelf", new Vector3(0, 1.6f, -.8f), new Vector3(1.4f, 3, .3f), 0);
            Physics.SyncTransforms(); clipped = Clamp(pivot, pivot + Vector3.back * 7);
            Check(Clear(clipped) && clipped.z > origin.z - .65f, "default-layer furniture blocks camera");
            shelf.isTrigger = true; Physics.SyncTransforms(); clipped = Clamp(pivot, pivot + Vector3.back * 7);
            Check(clipped.z < origin.z - 1, "interaction triggers do not block camera"); shelf.gameObject.SetActive(false);

            camera.nearClipPlane = .4f; camera.fieldOfView = 100; camera.aspect = 21f / 9;
            float expected = camera.nearClipPlane * Mathf.Sqrt(1 + Mathf.Pow(Mathf.Tan(50 * Mathf.Deg2Rad), 2) * (1 + camera.aspect * camera.aspect));
            Check(Radius() >= expected, "wide-FOV near-plane corners are inside collision sphere");
            clipped = Clamp(pivot, pivot + Vector3.back * 7); Check(Clear(clipped), "wide lens stays clear");
            camera.transform.SetPositionAndRotation(clipped, Quaternion.identity);
            var corners = new Vector3[4]; camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);
            foreach (var corner in corners) Check(!Physics.CheckSphere(camera.transform.TransformPoint(corner), .005f, 1025, QueryTriggerInteraction.Ignore), "near-plane corner outside wall");
            camera.nearClipPlane = .1f; camera.fieldOfView = 60; camera.aspect = 16f / 9;

            // Force both NonAlloc query buffers to overflow with ignored player attachments.
            for (int i = 0; i < 70; i++) { var box = player.AddComponent<BoxCollider>(); box.center = new Vector3(0, 1.6f, -.7f); box.size = new Vector3(.1f, .1f, .1f); }
            Physics.SyncTransforms(); clipped = Clamp(pivot, pivot + Vector3.back * 7);
            Check(Clear(clipped) && clipped.z > origin.z - 1.9f + Radius(), "cast overflow retains actual blocking wall");
            Check(Clear(origin + new Vector3(0, 1.6f, -.7f)), "overlap overflow ignores player attachments");

            camera.transform.position = wall.transform.position; Set(controller, "hasValidCameraPosition", true);
            Follow(); Check(camera.transform.position.z > origin.z - 1.9f, "camera spawned inside wall recovers");
            for (int angle = 0; angle < 360; angle += 5) { Set(controller, "yaw", (float)angle); Follow(); }
            // A prop moves over the previous lens between frames.
            shelf.isTrigger = false; shelf.gameObject.SetActive(true); shelf.transform.position = camera.transform.position;
            Follow(); shelf.gameObject.SetActive(false);
            player.transform.position = origin + Vector3.right * 25; Follow();
            Check(Vector3.Distance(camera.transform.position, player.transform.position) < 13, "teleport discards stale obstructed camera path");
            Check(player.transform.position == origin + Vector3.right * 25, "collision never moves the player");
            return "PASS camera: walls, furniture, overlapping starts, concave corners, 72-angle orbit, wide near plane, query overflow, moved props and teleport recovery (real PhysX).";
        }
        finally { Object.DestroyImmediate(fixture); }
    }
}
