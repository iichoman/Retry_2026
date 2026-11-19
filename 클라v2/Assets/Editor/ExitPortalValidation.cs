using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Wires the mandatory exit portal and exercises the actual SampleScene placement path.</summary>
public static class ExitPortalValidation
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PortalPath = "Assets/GameObjects/Exit/ExitPotal.prefab";
    private static readonly string Output = Path.GetFullPath("Artifacts/ExitPortal");

    [MenuItem("Tools/Retry/Dungeon/Configure and Validate Exit Portal")]
    public static void BuildAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Configure exit portals outside Play Mode.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            throw new InvalidOperationException("Save SampleScene changes before configuring the exit portal.");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Directory.CreateDirectory(Output);
        var report = new List<string> { "Actual SampleScene generator and enabled RoomObjectPlacer2, isolated editor preview.",
            "No server, protocol or F9/F10 modification. Preview render is not a live network session." };
        try
        {
            var generator = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<DungeonGenerator_ChunkMesh>(true)).Single();
            var placer = generator.GetComponents<RoomObjectPlacer2>().Single(x => x.enabled);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PortalPath);
            Require(prefab != null && prefab.GetComponentInChildren<ExitPortal>(true) != null, "existing exit portal prefab resolves", report);
            generator.createExitRoom = true;
            EditorUtility.SetDirty(generator);
            var serialized = new SerializedObject(placer);
            var portalReference = serialized.FindProperty("exitPortalPrefab");
            if (portalReference == null) throw new InvalidOperationException("Mandatory exitPortalPrefab field is missing.");
            portalReference.objectReferenceValue = prefab;
            int removed = RemovePortalEntries(serialized.FindProperty("propLibrary"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(placer);
            foreach (var legacy in generator.GetComponents<RoomObjectPlacer>().Where(x => !x.enabled))
            {
                var legacyData = new SerializedObject(legacy);
                foreach (string property in new[] { "floorRules", "decorRules", "wallRules" })
                    removed += RemovePortalEntries(legacyData.FindProperty(property));
                legacyData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(legacy);
            }
            report.Add("Removed " + removed + " exit portal entries from optional prop pools; other props are preserved.");
            Validate(generator, placer, report);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SampleScene save failed.");
            AssetDatabase.SaveAssets();
            Require(new SerializedObject(placer).FindProperty("exitPortalPrefab").objectReferenceValue == prefab,
                "SampleScene saves its mandatory exit portal prefab reference", report);
            report.Add("PASS exit portal configuration and validation completed.");
            File.WriteAllLines(Path.Combine(Output, "validation.txt"), report);
            Debug.Log("EXIT_PORTAL_VALIDATION_PASS: six seeds, one exit room and one portal, re-placement and clear, actual mesh render.");
        }
        catch (Exception error)
        {
            report.Add("FAIL " + error);
            File.WriteAllLines(Path.Combine(Output, "validation.txt"), report);
            throw;
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static int RemovePortalEntries(SerializedProperty property)
    {
        if (property == null) return 0;
        int removed = 0;
        if (property.isArray && property.propertyType != SerializedPropertyType.String)
        {
            for (int i = property.arraySize - 1; i >= 0; --i)
            {
                var item = property.GetArrayElementAtIndex(i);
                if (item.propertyType == SerializedPropertyType.ObjectReference &&
                    item.objectReferenceValue is GameObject prefab && prefab.GetComponentInChildren<ExitPortal>(true) != null)
                {
                    int count = property.arraySize;
                    property.DeleteArrayElementAtIndex(i);
                    if (property.arraySize == count) property.DeleteArrayElementAtIndex(i);
                    ++removed;
                }
                else removed += RemovePortalEntries(item);
            }
            return removed;
        }
        if (!property.hasVisibleChildren || property.propertyType == SerializedPropertyType.ObjectReference) return 0;
        var child = property.Copy();
        var end = property.GetEndProperty();
        if (child.NextVisible(true))
            do
            {
                if (SerializedProperty.EqualContents(child, end)) break;
                removed += RemovePortalEntries(child);
            } while (child.NextVisible(false));
        return removed;
    }

    private static void Validate(DungeonGenerator_ChunkMesh source, RoomObjectPlacer2 sourcePlacer, List<string> report)
    {
        Scene active = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject stage = null;
        try
        {
            stage = new GameObject("Exit portal isolated validation");
            SceneManager.MoveGameObjectToScene(stage, preview);
            var generator = stage.AddComponent<DungeonGenerator_ChunkMesh>();
            EditorUtility.CopySerialized(source, generator);
            generator.generateOnStart = false;
            generator.randomizeSeedOnGenerate = false;
            var placer = stage.AddComponent<RoomObjectPlacer2>();
            EditorUtility.CopySerialized(sourcePlacer, placer);
            var serialized = new SerializedObject(placer);
            serialized.FindProperty("dungeonGenerator").objectReferenceValue = generator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            bool testedBlockedCenter = false;
            foreach (int seed in new[] { 12345, 0, 1, 2, 42, 2026 })
            {
                placer.ClearPlacedObjects();
                generator.seed = seed;
                generator.GenerateDataForValidation();
                Require(generator.Rooms.Count(x => x.type == RoomType.Exit) == 1, "seed " + seed + ": exactly one exit room", report);
                string geometry = GeometrySignature(generator);
                Require(placer.TryGetExitPortalFloorPosition(out Vector3 expected), "seed " + seed + ": safe portal floor position resolves", report);
                placer.PlaceRoomObjects();
                Require(placer.TryGetExitPortalFloorPosition(out expected), "seed " + seed + ": portal floor remains safe after decoration", report);
                CheckPortal(generator, placer, expected, report);
                testedBlockedCenter |= CheckBlockedCenter(generator, expected, report);
                placer.PlaceRoomObjects();
                Require(stage.GetComponentsInChildren<ExitPortal>(true).Length == 1, "seed " + seed + ": repeat placement keeps exactly one portal", report);
                Require(geometry == GeometrySignature(generator), "seed " + seed + ": placement preserves floor, wall, ceiling and blocked tiles", report);
                placer.ClearPlacedObjects();
                Require(stage.GetComponentsInChildren<ExitPortal>(true).Length == 0, "seed " + seed + ": clear removes portal", report);
            }

            for (int seed = 3; seed < 19 && !testedBlockedCenter; seed++)
            {
                generator.seed = seed;
                generator.GenerateDataForValidation();
                if (generator.ExitRoom == null || generator.ExitRoom.layoutType != RoomLayoutType.CenterBlock) continue;
                string geometry = GeometrySignature(generator);
                placer.PlaceRoomObjects();
                Require(placer.TryGetExitPortalFloorPosition(out Vector3 safe), "blocked-center seed " + seed + ": safe fallback resolves", report);
                CheckPortal(generator, placer, safe, report);
                testedBlockedCenter = CheckBlockedCenter(generator, safe, report);
                Require(geometry == GeometrySignature(generator), "blocked-center fallback preserves authoritative geometry", report);
                placer.ClearPlacedObjects();
            }
            if (!testedBlockedCenter) report.Add("NOT COVERED: the bounded seed sample did not select a CenterBlock exit room.");

            generator.seed = 12345;
            generator.GenerateDungeon();
            placer.PlaceRoomObjects();
            Require(placer.TryGetExitPortalFloorPosition(out Vector3 center), "actual mesh generation resolves portal floor", report);
            CheckPortal(generator, placer, center, report);
            var room = generator.Rooms.Single(x => x.type == RoomType.Exit);
            var exact = (Vector3)generator.WorldOffset + new Vector3(room.bounds.center.x, center.y - generator.WorldOffset.y, room.bounds.center.z);
            Require(Vector2.Distance(new Vector2(center.x, center.z), new Vector2(exact.x, exact.z)) < .001f,
                "seed 12345: portal stands at the exact exit room center XZ", report);
            var sourcePresentation = source.GetComponent<DungeonRoomPresentation>();
            if (sourcePresentation != null && sourcePresentation.enabled)
            {
                var presentation = stage.AddComponent<DungeonRoomPresentation>();
                EditorUtility.CopySerialized(sourcePresentation, presentation);
                presentation.Rebuild();
            }
            Render(generator, center, report);
        }
        finally
        {
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
        }
    }

    private static bool CheckBlockedCenter(DungeonGenerator_ChunkMesh generator, Vector3 position, List<string> report)
    {
        var room = generator.ExitRoom;
        if (room == null || room.layoutType != RoomLayoutType.CenterBlock) return false;
        Vector3 center = (Vector3)generator.WorldOffset + new Vector3(room.bounds.center.x, 0, room.bounds.center.z);
        Require(Vector2.Distance(new Vector2(position.x, position.z), new Vector2(center.x, center.z)) > 1f,
            "seed " + generator.seed + ": blocked center uses nearby safe floor within the same exit room", report);
        return true;
    }

    private static void CheckPortal(DungeonGenerator_ChunkMesh generator, RoomObjectPlacer2 placer, Vector3 expected, List<string> report)
    {
        string label = "seed " + generator.seed + ": ";
        var portals = generator.GetComponentsInChildren<ExitPortal>(true);
        Require(portals.Length == 1, label + "exactly one portal instance", report);
        var portal = portals[0];
        var room = generator.Rooms.Single(x => x.type == RoomType.Exit);
        Require(portal.name == "ExitPortal_Room_" + room.id, label + "portal belongs to the only exit room", report);
        Require(Mathf.Abs(portal.transform.position.x - expected.x) < .001f && Mathf.Abs(portal.transform.position.z - expected.z) < .001f,
            label + "portal XZ matches the selected floor position", report);
        var renderers = portal.GetComponentsInChildren<Renderer>(true);
        Require(renderers.Length > 0 && renderers.All(x => x.sharedMaterials.Length > 0 && x.sharedMaterials.All(m => m != null && m.shader != null && !ShaderUtil.ShaderHasError(m.shader))),
            label + "portal renderers have valid materials and shaders", report);
        Require(Mathf.Abs(renderers.Min(x => x.bounds.min.y) - expected.y) < .01f, label + "portal visual base rests on the floor", report);
        Require(portal.GetComponentsInChildren<Collider>(true).Any(x => x.enabled && x.isTrigger), label + "portal has an enabled interaction trigger", report);
        Require(portal.GetComponentsInChildren<ExitPortalTrigger>(true).Any(x => new SerializedObject(x).FindProperty("exitPortal").objectReferenceValue == portal),
            label + "child trigger forwards interaction to this portal", report);
        Vector3 local = expected - (Vector3)generator.WorldOffset;
        foreach (Vector3 offset in new[] { Vector3.zero, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
        {
            Vector3Int tile = new Vector3Int(Mathf.FloorToInt(local.x + offset.x), 0, Mathf.FloorToInt(local.z + offset.z));
            if (!room.floorTiles.Contains(tile) || room.blockedTiles.Contains(tile))
                throw new InvalidOperationException(label + "portal footprint is not on walkable exit-room floor: " + tile);
        }
        report.Add("PASS " + label + "portal and immediate approaches are on walkable room floor; position " + expected.ToString("F3"));
    }

    private static string GeometrySignature(DungeonGenerator_ChunkMesh generator)
    {
        string Tiles(IEnumerable<Vector3Int> tiles) => string.Join(";", tiles.OrderBy(x => x.x).ThenBy(x => x.y).ThenBy(x => x.z));
        return Tiles(generator.FloorTiles) + "|" + Tiles(generator.WallTiles) + "|" + Tiles(generator.CeilingTiles) + "|" +
            string.Join("|", generator.Rooms.OrderBy(x => x.id).Select(x => x.id + ":" + Tiles(x.blockedTiles)));
    }

    private static void Render(DungeonGenerator_ChunkMesh generator, Vector3 center, List<string> report)
    {
        Color ambient = RenderSettings.ambientLight;
        AmbientMode ambientMode = RenderSettings.ambientMode;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f, .42f, .42f);
            var ceiling = generator.transform.Find("DungeonRoot_ChunkMesh/CeilingChunks");
            if (ceiling != null) ceiling.gameObject.SetActive(false);
            var cameraObject = new GameObject("Exit portal preview camera");
            cameraObject.transform.SetParent(generator.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = generator.gameObject.scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(generator.gameObject.scene);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .05f, .06f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200f;
            camera.fieldOfView = 55f;
            camera.transform.position = center + new Vector3(-8f, 5f, -10f);
            camera.transform.LookAt(center + Vector3.up * 1.6f);
            var lightObject = new GameObject("Exit portal preview light");
            lightObject.transform.SetParent(generator.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            CombatContentBuilder.CaptureCamera(camera, Path.Combine(Output, "ExitPortal_Seed12345.png"));
            report.Add("Rendered ExitPortal_Seed12345.png from actual generated meshes and configured prefab. Preview lighting, ceiling hidden.");
        }
        finally { RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = ambientMode; }
    }

    private static void Require(bool condition, string message, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Add("PASS " + message);
    }
}
