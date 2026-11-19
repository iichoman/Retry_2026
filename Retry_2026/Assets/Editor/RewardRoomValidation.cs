using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class RewardRoomValidation
{
    const string Output = "Artifacts/RewardRooms";
    static RewardRoomValidation() { EditorApplication.update += Poll; }
    static async void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Output + "/build.request")) return;
        File.Delete(Output + "/build.request");
        try { await BuildAndValidate(); File.WriteAllText(Output + "/result.txt", "PASS reward floor/entrance/guard parity, single-box props, client/server bounds, both room renders\n"); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", "FAIL\n" + e); Debug.LogException(e); }
    }
    static void Check(bool value, string label) { if (!value) throw new Exception(label); Debug.Log("PASS " + label); }
    static void BuildAssets()
    {
        var physics = Resources.Load<PhysicsMaterial>("DungeonPropSlide");
        if (physics == null) { physics = new PhysicsMaterial("DungeonPropSlide"); AssetDatabase.CreateAsset(physics, "Assets/Resources/DungeonPropSlide.physicMaterial"); }
        physics.dynamicFriction = physics.staticFriction = physics.bounciness = 0;
        physics.frictionCombine = physics.bounceCombine = PhysicsMaterialCombine.Minimum; EditorUtility.SetDirty(physics);
        var paths = DungeonPropPalette.Load().entries.Select(e => AssetDatabase.GetAssetPath(e.prefab)).Concat(new[] { "Assets/Resources/RoomBlueprints/Bookcase.prefab", "Assets/Resources/EContent/Chest.prefab", "Assets/Resources/EContent/ChestGold.prefab" }).Distinct();
        foreach (string path in paths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool chest = path.Contains("Chest");
                var box = root.GetComponent<BoxCollider>(); if (box == null) box = root.AddComponent<BoxCollider>();
                foreach (var old in root.GetComponentsInChildren<Collider>(true)) if (old != box) Object.DestroyImmediate(old);
                box.center = chest ? new Vector3(0, .525f, 0) : Vector3.zero;
                box.size = chest ? new Vector3(1.5f, 1.05f, 1.1f) : Vector3.one;
                box.isTrigger = false; box.enabled = true; box.sharedMaterial = physics;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var palette = DungeonPresentationPalette.Load();
        var theme = new DungeonPresentationPalette.Theme { style = DungeonRoomStyle.Treasure };
        var colors = new[] { new Color(.22f, .32f, .34f), new Color(.72f, .49f, .16f), new Color(.38f, .09f, .14f) };
        var materials = new Material[3]; string[] names = { "Stone", "Border", "Accent" };
        for (int i = 0; i < 3; i++)
        {
            string path = "Assets/DungeonPresentation/Materials/Treasure_" + names[i] + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", colors[i]); mat.SetFloat("_Smoothness", i == 1 ? .55f : .25f); mat.SetFloat("_Metallic", i == 1 ? .45f : 0);
            mat.enableInstancing = true; EditorUtility.SetDirty(mat); materials[i] = mat;
        }
        theme.stone = materials[0]; theme.border = materials[1]; theme.accent = materials[2];
        palette.themes = palette.themes.Where(t => t.style != DungeonRoomStyle.Treasure).Concat(new[] { theme }).ToArray();
        EditorUtility.SetDirty(palette); AssetDatabase.SaveAssets();
    }
    [MenuItem("Retry/Dungeon/Validate reward rooms")]
    static async void ValidateMenu() => await BuildAndValidate();
    public static async Task BuildAndValidate()
    {
        Directory.CreateDirectory(Output); BuildAssets();
        var source = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DungeonGenerator_ChunkMesh>(true)).First();
        typeof(RoomBlueprintBuilder).GetMethod("ValidateParity", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source });
        var data = File.ReadAllLines(Output + "/server-rewards.csv").Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var fixture = new GameObject("Reward parity"); SceneManager.MoveGameObjectToScene(fixture, preview);
            var generator = fixture.AddComponent<DungeonGenerator_ChunkMesh>(); EditorUtility.CopySerialized(source, generator);
            generator.randomizeSeedOnGenerate = generator.generateOnStart = false;
            foreach (int seed in data.Select(r => (int)r[0]).Distinct())
            {
                generator.seed = seed; generator.GenerateDataForValidation();
                foreach (var group in data.Where(r => r[0] == seed).GroupBy(r => (int)r[1]))
                {
                    var room = generator.Rooms.Single(r => r.id == group.Key); var first = group.First();
                    Check(RoomBlueprint.Kind(seed, room) == (int)first[2] && RoomBlueprint.GuardCount(seed, room) == (int)first[3], $"reward kind and guard count {seed}/{room.id}: client {RoomBlueprint.Kind(seed, room)}/{RoomBlueprint.GuardCount(seed, room)}, server {first[2]}/{first[3]}, bounds {room.bounds}, rewards {string.Join(",", generator.Rooms.Where(r => r.type == RoomType.Reward).Select(r => r.id))}");
                    foreach (var row in group)
                    {
                        var tile = generator.WorldToTile(new Vector3(row[5], row[6], row[7])); tile.y = 0;
                        Check(room.floorTiles.Contains(tile) && !room.blockedTiles.Contains(tile), "server reward placement lies on client floor");
                        if (row[4] == 0 || row[4] == 3) Check(RoomBlueprint.Entrance(room) == new Vector3Int((int)row[8], 0, (int)row[9]), "chest faces matching entrance");
                    }
                }
            }
            Object.DestroyImmediate(fixture);
            foreach (int kind in new[] { 5, 8 })
            {
                var sample = data.First(r => (int)r[2] == kind);
                await Render(source, preview, data.Where(r => r[0] == sample[0] && r[1] == sample[1]).ToArray(), kind);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
    static Task NextEditorFrame()
    {
        var ready = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; ready.SetResult(true); }
        EditorApplication.update += Tick; EditorApplication.QueuePlayerLoopUpdate();
        return ready.Task;
    }
    static async Task Render(DungeonGenerator_ChunkMesh source, Scene scene, float[][] rows, int kind)
    {
        var stage = new GameObject("Reward room " + kind); SceneManager.MoveGameObjectToScene(stage, scene);
        var generator = stage.AddComponent<DungeonGenerator_ChunkMesh>(); EditorUtility.CopySerialized(source, generator);
        generator.seed = (int)rows[0][0]; generator.randomizeSeedOnGenerate = generator.generateOnStart = false;
        generator.GenerateDataForValidation();
        var room = generator.Rooms.Single(r => r.id == (int)rows[0][1]);
        // Render the real room footprint with low cutaway walls so its contents stay visible.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var mesh = cube.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cube);
        var floor = new System.Collections.Generic.List<CombineInstance>(); var walls = new System.Collections.Generic.List<CombineInstance>();
        foreach (var tile in room.floorTiles)
        {
            var center = generator.TileToWorldCenter(tile);
            floor.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(center + Vector3.up * .5f, Quaternion.identity, Vector3.one) });
            foreach (var dir in new[] { Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back })
                if (generator.IsWallTile(tile + dir)) walls.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(generator.TileToWorldCenter(tile + dir) + Vector3.up * 1.5f, Quaternion.identity, new Vector3(1, 2, 1)) });
        }
        var owned = new System.Collections.Generic.List<Mesh>();
        foreach (var layer in new[] { floor, walls })
        {
            var combined = new Mesh { indexFormat = IndexFormat.UInt32 }; combined.CombineMeshes(layer.ToArray()); owned.Add(combined);
            var go = new GameObject(layer == floor ? "Floor" : "Cutaway walls"); go.transform.SetParent(stage.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = combined; go.AddComponent<MeshRenderer>().sharedMaterial = layer == floor ? source.floorMaterial : source.wallMaterial;
        }
        var surfaces = stage.AddComponent<DungeonRoomPresentation>(); surfaces.palette = DungeonPresentationPalette.Load(); surfaces.Rebuild();
        var dress = stage.AddComponent<DungeonSetDressing>(); dress.Rebuild();
        foreach (Transform group in stage.transform.Find("DungeonRoomPresentation")) if (!group.name.StartsWith("Room_" + room.id + "_")) group.gameObject.SetActive(false);
        foreach (Transform group in stage.transform.Find("DungeonSetDressing")) if (!group.name.StartsWith("Room_" + room.id + "_")) group.gameObject.SetActive(false);
        foreach (var slot in DungeonPropLayout.GenerateRoom(generator, room))
        {
            var prefab = slot.PropKind == DungeonPropLayout.Kind.Book && !slot.Supported ? Resources.Load<GameObject>("RoomBlueprints/Bookcase") : DungeonPropPalette.Load().Resolve(slot.PropKind);
            var prop = Object.Instantiate(prefab, stage.transform); prop.transform.SetPositionAndRotation(slot.Center, Quaternion.Euler(0, slot.Yaw, 0));
            prop.transform.localScale = Mathf.RoundToInt(slot.Yaw/90f)%2!=0 ? new Vector3(slot.Size.z,slot.Size.y,slot.Size.x) : slot.Size;
            SimplePropCollider.Apply(prop, Vector3.zero, Vector3.one, !slot.Supported);
            Check(prop.GetComponentsInChildren<Collider>().Count(c => c.enabled) == (slot.Supported ? 0 : 1), "furniture has one simple collider");
        }
        foreach (var row in rows)
        {
            Vector3 position = new Vector3(row[5], row[6], row[7]); int type = (int)row[4];
            if (type == 0 || type == 3)
            {
                var chest = Object.Instantiate(Resources.Load<GameObject>(type == 3 ? "EContent/ChestGold" : "EContent/Chest"), stage.transform);
                chest.transform.SetPositionAndRotation(position, Quaternion.LookRotation(new Vector3(row[8], 0, row[9])));
                var box = SimplePropCollider.Apply(chest, new Vector3(0, .525f, 0), new Vector3(1.5f, 1.05f, 1.1f));
                bool swapped = row[8] != 0;
                Check(Mathf.Abs(box.bounds.extents.x - (swapped ? .55f : .75f)) < .001f && Mathf.Abs(box.bounds.extents.z - (swapped ? .75f : .55f)) < .001f, "rotated chest bounds match server");
            }
            else
            {
                var go = new GameObject("Trap " + type); go.transform.SetParent(stage.transform, false); var trap = go.AddComponent<EnvironmentTrapView>();
                trap.Apply(new EnvironmentState { version = 1, id = 1, kind = type, serial = 1, x = position.x, y = position.y, z = position.z, dx = row[8], dz = row[9], length = row[10], width = type == 1 ? 4 : .8f, state = 2, deadline = ServerClock.NowMs + 500 });
                typeof(EnvironmentTrapView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(trap, null);
            }
        }
        for (int i = 0; i < RoomBlueprint.GuardCount(generator.seed, room); i++)
        {
            var knight = Object.Instantiate(Resources.Load<GameObject>("KnightElite"), stage.transform);
            knight.transform.position = generator.TileToWorldCenter(RoomBlueprint.GuardTile(generator.seed, room, i)) + Vector3.up;
            foreach (var behaviour in knight.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            foreach (var canvas in knight.GetComponentsInChildren<Canvas>()) canvas.enabled = false;
            var animator = knight.GetComponentInChildren<Animator>(); animator.Rebind(); animator.Update(0);
        }
        var camera = new GameObject("Reward camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
        camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        var middle = generator.TileToWorld(room.bounds.position) + new Vector3(room.bounds.size.x * .5f, 1, room.bounds.size.z * .5f);
        var facing = (Vector3)RoomBlueprint.Entrance(room); float span = Mathf.Max(room.bounds.size.x, room.bounds.size.z);
        camera.transform.position = middle + Vector3.up * span * 1.15f + facing * span * .55f; camera.transform.LookAt(middle);
        camera.orthographic = true; camera.orthographicSize = span * .6f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .055f, .07f);
        var light = new GameObject("Reward light").AddComponent<Light>(); SceneManager.MoveGameObjectToScene(light.gameObject, scene); light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(55, -30, 0);
        try
        {
            // Allow Unity's renderer/culling state to register newly instantiated prefabs.
            await NextEditorFrame(); await NextEditorFrame();
            CombatContentBuilder.CaptureCamera(camera, Output + (kind == 8 ? "/trapped-vault.png" : "/treasure-vault.png"));
        }
        finally { Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(stage); foreach (var ownedMesh in owned) Object.DestroyImmediate(ownedMesh); }
    }
}
