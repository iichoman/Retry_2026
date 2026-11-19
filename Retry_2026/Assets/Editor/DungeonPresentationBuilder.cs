using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Builds client visual assets and validates them against a real SampleScene dungeon.</summary>
public static class DungeonPresentationBuilder
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string AssetRoot = "Assets/DungeonPresentation";
    private const string PalettePath = "Assets/Resources/DungeonPresentationPalette.asset";
    private static readonly string Output = Path.GetFullPath("Artifacts/DungeonPresentation");
    private static readonly Vector3Int[] Neighbors = { Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back };

    [MenuItem("Tools/Retry/Dungeon/Build and Validate Presentation")]
    public static void BuildAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build dungeon presentation outside Play Mode.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            throw new InvalidOperationException("Save SampleScene edits before building dungeon presentation.");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Directory.CreateDirectory(Output);
        var report = new List<string> { "Actual SampleScene configuration, seed 12345. Client presentation only.",
            "Cutaway renders hide ceilings for inspection; they are not in-game camera screenshots.",
            "Before/after compare presentation off/on using the same generated geometry and existing props." };
        try
        {
            var source = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<DungeonGenerator_ChunkMesh>(true)).Single();
            var palette = BuildPalette(source.floorMaterial);
            ValidateAndRender(source, palette, report);
            source.coherentWallMaterialBands = true;
            EditorUtility.SetDirty(source);
            var presentation = source.GetComponent<DungeonRoomPresentation>() ?? source.gameObject.AddComponent<DungeonRoomPresentation>();
            presentation.palette = palette;
            EditorUtility.SetDirty(presentation);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SampleScene save failed.");
            report.Add("PASS SampleScene Map has DungeonRoomPresentation and the serialized palette asset.");
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Path.Combine(Output, "validation.txt"), report);
            Debug.Log("DUNGEON_PRESENTATION_PASS: actual seed12345 map, physics/layout invariants and cutaway renders.");
        }
        catch (Exception error)
        {
            report.Add("FAIL " + error);
            File.WriteAllLines(Path.Combine(Output, "validation.txt"), report);
            throw;
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static DungeonPresentationPalette BuildPalette(Material floor)
    {
        EnsureFolder(AssetRoot + "/Materials"); EnsureFolder("Assets/Resources");
        var palette = AssetDatabase.LoadAssetAtPath<DungeonPresentationPalette>(PalettePath);
        if (palette == null) { palette = ScriptableObject.CreateInstance<DungeonPresentationPalette>(); AssetDatabase.CreateAsset(palette, PalettePath); }
        var colors = new[]
        {
            new[] { new Color(.48f,.42f,.33f), new Color(.30f,.23f,.14f), new Color(.47f,.29f,.14f) },
            new[] { new Color(.36f,.42f,.41f), new Color(.46f,.40f,.24f), new Color(.12f,.28f,.27f) },
            new[] { new Color(.40f,.41f,.45f), new Color(.27f,.28f,.33f), new Color(.31f,.22f,.36f) },
            new[] { new Color(.39f,.32f,.29f), new Color(.52f,.38f,.19f), new Color(.40f,.12f,.09f) },
            new[] { new Color(.36f,.44f,.42f), new Color(.44f,.49f,.35f), new Color(.12f,.34f,.30f) },
            new[] { new Color(.22f,.32f,.34f), new Color(.72f,.49f,.16f), new Color(.38f,.09f,.14f) }
        };
        var styles = (DungeonRoomStyle[])Enum.GetValues(typeof(DungeonRoomStyle));
        palette.themes = new DungeonPresentationPalette.Theme[styles.Length];
        for (int i = 0; i < styles.Length; i++)
        {
            palette.themes[i] = new DungeonPresentationPalette.Theme
            {
                style = styles[i],
                stone = MaterialAsset(styles[i] + "_Stone", colors[i][0], floor),
                border = MaterialAsset(styles[i] + "_Border", colors[i][1], null),
                accent = MaterialAsset(styles[i] + "_Accent", colors[i][2], null)
            };
        }
        EditorUtility.SetDirty(palette); AssetDatabase.SaveAssets(); return palette;
    }

    private static Material MaterialAsset(string name, Color color, Material textureSource)
    {
        string path = AssetRoot + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("URP Lit is missing or has shader errors.");
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader; material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", .12f); material.SetFloat("_Metallic", 0);
        material.SetTexture("_BaseMap", textureSource != null && textureSource.HasProperty("_BaseMap") ? textureSource.GetTexture("_BaseMap") : null);
        material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
    }

    private static void ValidateAndRender(DungeonGenerator_ChunkMesh source, DungeonPresentationPalette palette, List<string> report)
    {
        Scene active = SceneManager.GetActiveScene();
        Color previousAmbient = RenderSettings.ambientLight;
        AmbientMode previousAmbientMode = RenderSettings.ambientMode;
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject stage = null;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.48f,.48f,.48f);
            stage = new GameObject("Actual Sample Dungeon / isolated editor validation");
            SceneManager.MoveGameObjectToScene(stage, preview);
            var generator = stage.AddComponent<DungeonGenerator_ChunkMesh>();
            EditorUtility.CopySerialized(source, generator);
            generator.seed = 12345; generator.randomizeSeedOnGenerate = false; generator.generateOnStart = false;
            generator.coherentWallMaterialBands = true;
            CopyPlacer<RoomObjectPlacer>(source.gameObject, stage, generator);
            CopyPlacer<RoomObjectPlacer2>(source.gameObject, stage, generator);
            generator.GenerateDungeon();
            // These runtime components do not execute OnEnable in Edit Mode, so their
            // DungeonGenerated handlers are not subscribed in this preview scene.
            var legacyPlacer = stage.GetComponent<RoomObjectPlacer>();
            if (legacyPlacer != null && legacyPlacer.PlaceOnDungeonGenerated) legacyPlacer.PlaceRoomObjects();
            var compositionPlacer = stage.GetComponent<RoomObjectPlacer2>();
            if (compositionPlacer != null && compositionPlacer.PlaceOnDungeonGenerated) compositionPlacer.PlaceRoomObjects();
            if (compositionPlacer != null && compositionPlacer.UsesRoomComposition && compositionPlacer.PlaceOnDungeonGenerated)
                Require(stage.GetComponentsInChildren<DungeonPropObstacle>(true).Length > 0, "actual enabled room placer produces physical prop instances", report);
            var before = Snapshot(generator);
            WriteMapAnalysis(generator, report);
            var presentation = stage.AddComponent<DungeonRoomPresentation>(); presentation.palette = palette;
            presentation.Rebuild();
            Require(Snapshot(generator) == before, "floor/wall/blocked/doorway/prop/collider signatures unchanged after visual generation", report);
            Transform visuals = stage.transform.Find("DungeonRoomPresentation");
            Require(visuals != null, "presentation root exists", report);
            int meshes = visuals.GetComponentsInChildren<MeshFilter>(true).Length;
            int vertices = visuals.GetComponentsInChildren<MeshFilter>(true).Sum(x => x.sharedMesh.vertexCount);
            Require(meshes > 0 && meshes <= generator.Rooms.Count, "at most one batched presentation mesh per room", report);
            Require(visuals.GetComponentsInChildren<Collider>(true).Length == 0, "presentation has no collision components", report);
            Require(visuals.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true).Length == 0, "presentation has no navigation obstacles", report);
            foreach (var renderer in visuals.GetComponentsInChildren<MeshRenderer>(true))
            {
                var modifier = renderer.GetComponentInParent<NavMeshModifier>();
                if (modifier == null || !modifier.ignoreFromBuild) throw new InvalidOperationException(renderer.name + " is not excluded from NavMesh build.");
                if (renderer.sharedMaterials.Any(x => x == null || x.shader == null || ShaderUtil.ShaderHasError(x.shader)))
                    throw new InvalidOperationException(renderer.name + " has a missing or invalid material.");
            }
            Require(true, "all new renderers explicitly excluded from NavMesh sources and have valid URP materials", report);
            ValidateFloorCoverage(generator, visuals, report);
            string visualSignature = VisualSignature(visuals);
            presentation.Rebuild(); visuals = stage.transform.Find("DungeonRoomPresentation");
            Require(visualSignature == VisualSignature(visuals), "repeat Rebuild produces identical meshes/material assignments", report);
            Require(Snapshot(generator) == before, "repeat Rebuild preserves all authoritative geometry and collider signatures", report);
            report.Add($"Presentation budget: {meshes} room meshes, {vertices} vertices, no Update loop or new lights.");
            File.WriteAllText(Path.Combine(Output, "invariant-signatures.txt"), before);
            RenderRooms(generator, visuals, report);
            presentation.Clear();
            Require(Snapshot(generator) == before, "Clear restores presentation without modifying geometry/props", report);
        }
        finally
        {
            // Generated dungeon meshes are temporary runtime objects, not asset files.
            if (stage != null)
            {
                var meshes = stage.GetComponentsInChildren<MeshFilter>(true).Select(x => x.sharedMesh)
                    .Concat(stage.GetComponentsInChildren<MeshCollider>(true).Select(x => x.sharedMesh))
                    .Where(x => x != null && !EditorUtility.IsPersistent(x)).Distinct().ToArray();
                UnityEngine.Object.DestroyImmediate(stage);
                foreach (var mesh in meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
            EditorSceneManager.ClosePreviewScene(preview);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            RenderSettings.ambientLight = previousAmbient; RenderSettings.ambientMode = previousAmbientMode;
        }
    }

    private static void CopyPlacer<T>(GameObject source, GameObject destination, DungeonGenerator_ChunkMesh generator) where T : MonoBehaviour
    {
        var original = source.GetComponent<T>(); if (original == null || !original.enabled) return;
        var clone = destination.AddComponent<T>(); EditorUtility.CopySerialized(original, clone);
        var serialized = new SerializedObject(clone);
        serialized.FindProperty("dungeonGenerator").objectReferenceValue = generator;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string Snapshot(DungeonGenerator_ChunkMesh generator)
    {
        var result = new StringBuilder();
        result.AppendLine("floor " + Hash(Tiles(generator.FloorTiles)));
        result.AppendLine("wall " + Hash(Tiles(generator.WallTiles)));
        result.AppendLine("ceiling " + Hash(Tiles(generator.CeilingTiles)));
        result.AppendLine("blocked " + Hash(string.Join("|", generator.Rooms.OrderBy(x => x.id).Select(x => x.id + ":" + Tiles(x.blockedTiles)))));
        result.AppendLine("doorways " + Hash(string.Join("|", generator.Rooms.OrderBy(x => x.id).Select(x => x.id + ":" + Tiles(x.doorwayFloorTiles)))));
        result.AppendLine("props " + Hash(string.Join("|", generator.GetComponentsInChildren<DungeonPropObstacle>(true).Select(x =>
            x.RoomId + ":" + x.GroupId + ":" + x.Kind + ":" + x.Supported + ":" + V(x.transform.position) + ":" + V(x.transform.eulerAngles) + ":" + V(x.transform.lossyScale)).OrderBy(x => x))));
        var colliders = generator.GetComponentsInChildren<Collider>(true);
        result.AppendLine("colliderCount " + colliders.Length);
        result.AppendLine("colliders " + Hash(string.Join("|", colliders.Select(x => ColliderText(x)).OrderBy(x => x))));
        return result.ToString();
    }

    private static string ColliderText(Collider x)
    {
        string shape = x is BoxCollider b ? V(b.center) + ":" + V(b.size) : x is MeshCollider m && m.sharedMesh != null ? MeshHash(m.sharedMesh) : "";
        return x.GetType().Name + ":" + x.name + ":" + x.enabled + ":" + x.isTrigger + ":" + x.gameObject.layer + ":" + V(x.transform.position) + ":" + V(x.transform.eulerAngles) + ":" + V(x.transform.lossyScale) + ":" + shape;
    }
    private static string VisualSignature(Transform root) => Hash(string.Join("|", root.GetComponentsInChildren<MeshFilter>(true).Select(x =>
        x.name + ":" + V(x.transform.position) + ":" + MeshHash(x.sharedMesh) + ":" + string.Join(",", x.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m.name))).OrderBy(x => x)));
    private static string MeshHash(Mesh mesh) => mesh.isReadable ? Hash(string.Join(";", mesh.vertices.Select(V)) + "|" + string.Join(",", mesh.triangles)) : AssetDatabase.GetAssetPath(mesh) + ":" + mesh.vertexCount;
    private static string V(Vector3 p) => p.x.ToString("R", CultureInfo.InvariantCulture) + "," + p.y.ToString("R", CultureInfo.InvariantCulture) + "," + p.z.ToString("R", CultureInfo.InvariantCulture);
    private static string Tiles(IEnumerable<Vector3Int> tiles) => string.Join(";", tiles.OrderBy(x => x.x).ThenBy(x => x.y).ThenBy(x => x.z).Select(x => x.x + "," + x.y + "," + x.z));
    private static string Hash(string value) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }

    private static void ValidateFloorCoverage(DungeonGenerator_ChunkMesh generator, Transform visuals, List<string> report)
    {
        int checkedTriangles = 0;
        foreach (var filter in visuals.GetComponentsInChildren<MeshFilter>(true))
        {
            int roomId = int.Parse(filter.name.Split('_')[1]);
            Room room = generator.Rooms.Single(x => x.id == roomId);
            var vertices = filter.sharedMesh.vertices; var indices = filter.sharedMesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i+1]], c = vertices[indices[i+2]];
                if (a.y > 1.1f || b.y > 1.1f || c.y > 1.1f) continue;
                Vector3 center = (a+b+c)/3;
                foreach (var point in new[] { center, Vector3.Lerp(a,center,.01f), Vector3.Lerp(b,center,.01f), Vector3.Lerp(c,center,.01f) })
                {
                    var tile = generator.WorldToTile(point); tile.y = 0;
                    if (!room.floorTiles.Contains(tile) || room.blockedTiles.Contains(tile) || generator.IsWallTile(tile))
                        throw new InvalidOperationException($"Room {roomId} surface extends outside walkable floor at {tile}.");
                }
                checkedTriangles++;
            }
        }
        Require(checkedTriangles > 0, $"{checkedTriangles} floor triangles stay inside room floor and outside blocked/wall cells", report);
    }

    private static void WriteMapAnalysis(DungeonGenerator_ChunkMesh generator, List<string> report)
    {
        var remaining = new HashSet<Vector3Int>(generator.FloorTiles); var sizes = new List<int>();
        while (remaining.Count > 0)
        {
            var queue = new Queue<Vector3Int>(); var first = remaining.First(); remaining.Remove(first); queue.Enqueue(first); int count = 0;
            while (queue.Count > 0) { var tile = queue.Dequeue(); count++; foreach (var dir in Neighbors) if (remaining.Remove(tile + dir)) queue.Enqueue(tile + dir); }
            sizes.Add(count);
        }
        sizes.Sort((a,b) => b.CompareTo(a));
        var lines = new List<string>
        {
            "ACTUAL SAMPLESCENE DUNGEON ANALYSIS / seed 12345",
            $"Map {generator.mapSize} x {generator.mapSize}m, wall height {generator.mapHeight}m, corridor width {generator.corridorWidth}m.",
            $"{generator.Rooms.Count} rooms, {generator.GetAssignedStartRooms().Count} assigned start rooms, {generator.Corridors.Count} corridor sections.",
            $"{generator.FloorTiles.Count} floor cells; connected floor components: {sizes.Count}; component sizes: {string.Join(",", sizes)}.",
            $"{generator.GetComponentsInChildren<DungeonPropObstacle>(true).Length} existing solid prop instances.",
            "RoomId,Type,Shape,Layout,SizeX,SizeZ,FloorCells,BlockedCells,DoorwayCells,GraphNeighbors,Style,PropCount"
        };
        foreach (var room in generator.Rooms.OrderBy(x => x.id)) lines.Add($"{room.id},{room.type},{room.shape},{room.layoutType},{room.bounds.size.x},{room.bounds.size.z},{room.floorTiles.Count},{room.blockedTiles.Count},{room.doorwayFloorTiles.Count},{room.neighbors.Count},{DungeonRoomPresentation.StyleFor(generator, room)},{generator.GetComponentsInChildren<DungeonPropObstacle>(true).Count(p => p.RoomId == room.id)}");
        lines.Add("Layout topology, room count, boundaries, doors and obstacle positions are preserved for server parity.");
        lines.Add("Visual distinction is added through room palettes, floor borders/motifs and wall banners; there are no new gameplay routes or interactables.");
        File.WriteAllLines(Path.Combine(Output, "map-analysis.txt"), lines);
        report.AddRange(lines.Take(5));
        RenderPlan(generator);
    }

    private static void RenderPlan(DungeonGenerator_ChunkMesh generator)
    {
        int size = generator.mapSize; var pixels = Enumerable.Repeat(new Color32(15,19,24,255), size * size).ToArray();
        foreach (var tile in generator.FloorTiles) if (tile.x >= 0 && tile.x < size && tile.z >= 0 && tile.z < size) pixels[tile.z * size + tile.x] = new Color32(76,86,94,255);
        Color32[] colors = { new Color32(151,115,65,255), new Color32(66,142,139,255), new Color32(130,109,151,255), new Color32(173,67,49,255), new Color32(81,168,109,255) };
        foreach (var room in generator.Rooms) foreach (var tile in room.floorTiles) if (tile.x >= 0 && tile.x < size && tile.z >= 0 && tile.z < size) pixels[tile.z * size + tile.x] = colors[(int)DungeonRoomPresentation.StyleFor(generator, room)];
        foreach (var room in generator.Rooms) foreach (var tile in room.doorwayFloorTiles) for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
        { int x = tile.x + dx, z = tile.z + dz; if (x >= 0 && x < size && z >= 0 && z < size) pixels[z * size + x] = new Color32(248,229,163,255); }
        var texture = new Texture2D(size,size,TextureFormat.RGBA32,false); texture.SetPixels32(pixels); texture.Apply();
        File.WriteAllBytes(Path.Combine(Output,"Seed12345_ActualLayout.png"), texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
    }

    private static void RenderRooms(DungeonGenerator_ChunkMesh generator, Transform visuals, List<string> report)
    {
        var selected = generator.Rooms.Where(x => x.type == RoomType.Normal).OrderByDescending(x => x.floorTiles.Count)
            .GroupBy(x => DungeonRoomPresentation.StyleFor(generator,x)).Select(x => x.First()).Take(3).ToList();
        if (selected.Count < 3) selected.AddRange(generator.Rooms.Where(x => !selected.Contains(x)).Take(3 - selected.Count));
        selected.AddRange(generator.Rooms.Where(x => (x.type == RoomType.Boss || x.type == RoomType.Exit) && !selected.Contains(x)));
        var cameraObject = new GameObject("Generated room cutaway camera"); cameraObject.transform.SetParent(generator.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f,.07f,.085f);
        camera.scene = generator.gameObject.scene;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(generator.gameObject.scene);
        camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 1500f;
        var lightObject = new GameObject("Preview key light"); lightObject.transform.SetParent(generator.transform, false);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.color = new Color(1f,.9f,.78f); light.shadows = LightShadows.None;
        light.transform.rotation = Quaternion.Euler(48,-35,0);
        var allRenderers = generator.GetComponentsInChildren<Renderer>(true); var allLights = generator.GetComponentsInChildren<Light>(true);
        Vector3 Focus(Room room)
        {
            Vector3 center = generator.TileToWorld(Vector3Int.FloorToInt(room.bounds.center)); center.y = 1f;
            var bounds = new Bounds(center, new Vector3(room.bounds.size.x + 4, generator.mapHeight * 3, room.bounds.size.z + 4));
            foreach (var renderer in allRenderers)
            {
                bool ceiling = renderer.transform.IsChildOf(generator.transform.Find("DungeonRoot_ChunkMesh/CeilingChunks"));
                renderer.enabled = !ceiling && bounds.Intersects(renderer.bounds);
            }
            foreach (var lamp in allLights) if (lamp != light) lamp.enabled = bounds.Contains(lamp.transform.position);
            return center;
        }
        foreach (var room in selected)
        {
            Vector3 center = Focus(room);
            float span = Mathf.Max(room.bounds.size.x,room.bounds.size.z);
            camera.orthographicSize = span * .62f;
            camera.transform.position = center + new Vector3(-span * .7f, span * 1.15f, -span * .8f);
            camera.transform.LookAt(center + Vector3.up * 1.5f);
            string prefix = $"Room_{room.id}_{DungeonRoomPresentation.StyleFor(generator,room)}";
            visuals.gameObject.SetActive(false); CombatContentBuilder.CaptureCamera(camera, Path.Combine(Output,prefix + "_Before.png"));
            visuals.gameObject.SetActive(true); CombatContentBuilder.CaptureCamera(camera, Path.Combine(Output,prefix + "_After.png"));
            report.Add($"Rendered actual Room {room.id}, {room.bounds.size.x}x{room.bounds.size.z}, {room.layoutType}, {DungeonRoomPresentation.StyleFor(generator,room)} before/after cutaway.");
        }
        var perspectiveRoom = selected.FirstOrDefault(x => x.type == RoomType.Normal && x.layoutType == RoomLayoutType.Open);
        if (perspectiveRoom != null)
        {
            Focus(perspectiveRoom); camera.orthographic = false; camera.fieldOfView = 62;
            var filter = visuals.GetComponentsInChildren<MeshFilter>().Single(x => x.name.StartsWith("Room_" + perspectiveRoom.id + "_", StringComparison.Ordinal));
            var vertices = filter.sharedMesh.vertices;
            Vector3 standard = generator.TileToWorld(Vector3Int.FloorToInt(perspectiveRoom.bounds.center));
            Vector3 towardRoom = Vector3.back;
            for (int i = 0; i + 3 < vertices.Length; i += 4)
            {
                if (vertices[i].y < 2 || vertices[i+1].y < 2 || Mathf.Abs(vertices[i].y - vertices[i+1].y) < .1f) continue;
                standard = (vertices[i]+vertices[i+1]+vertices[i+2]+vertices[i+3])*.25f;
                towardRoom = filter.sharedMesh.normals[i]; break;
            }
            var prop = generator.GetComponentsInChildren<DungeonPropObstacle>(true)
                .Where(x => x.RoomId == perspectiveRoom.id && !x.Supported)
                .OrderBy(x => Vector3.SqrMagnitude(x.transform.position - standard)).FirstOrDefault();
            Vector3 target = prop != null ? Vector3.Lerp(standard, prop.transform.position, .45f) : standard;
            Vector3 across = Vector3.Cross(Vector3.up,towardRoom);
            camera.transform.position = target + towardRoom * 15 + across * 5;
            var position = camera.transform.position; position.y = 2.8f; camera.transform.position = position;
            target.y = 2.7f; camera.transform.LookAt(target);
            visuals.gameObject.SetActive(true);
            CombatContentBuilder.CaptureCamera(camera, Path.Combine(Output,$"Room_{perspectiveRoom.id}_HumanScale_Perspective.png"));
            report.Add($"Rendered actual Room {perspectiveRoom.id} perspective at world Y=2.8m (1.8m above floor), existing prop and mounted standard. Preview lighting; not live gameplay.");
        }
    }

    private static void Require(bool condition, string message, List<string> report)
    { if (!condition) throw new InvalidOperationException(message); report.Add("PASS " + message); }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\','/'); if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
