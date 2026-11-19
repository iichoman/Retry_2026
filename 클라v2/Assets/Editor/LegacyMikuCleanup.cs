using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit migration of disabled prototype visuals; never runs automatically.
public static class LegacyMikuCleanup
{
    const string Folder = "Assets/GameObjects/Player/Player_Model/hatsune-miku (2)";
    const string Output = "Artifacts/AssetAudit_20261119/MikuCleanup";
    static readonly string[] Prefabs = {
        "Assets/CombatContent/Prefabs/DungeonGuardian.prefab",
        "Assets/CombatContent/Prefabs/DungeonGuardianOffline.prefab"
    };
    static readonly StringBuilder Report = new StringBuilder();
    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Report.AppendLine("PASS " + message);
    }
    static bool Inside(Object obj, Transform branch)
    {
        var t = obj is GameObject go ? go.transform : (obj as Component)?.transform;
        return t != null && (t == branch || t.IsChildOf(branch));
    }
    static string PathOf(Transform t, Transform root) => AnimationUtility.CalculateTransformPath(t, root);
    static string AssetId(Object obj) => obj == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        try
        {
            foreach (var path in Prefabs)
            {
                File.Copy(path, Output + "/" + Path.GetFileName(path) + ".before", true);
                var before = SampleGoblin(path);
                CleanPrefab(path);
                var after = SampleGoblin(path);
                Check(before.Keys.OrderBy(k => k).SequenceEqual(after.Keys.OrderBy(k => k)), path + " active goblin identity unchanged");
                foreach (var key in before.Keys)
                {
                    Check(before[key].Length == after[key].Length, key + " vertex count unchanged");
                    float delta = before[key].Zip(after[key], (a, b) => Vector3.Distance(a, b)).DefaultIfEmpty(0).Max();
                    Check(delta < 0.0001f, key + " animated mesh unchanged; max delta=" + delta);
                }
            }
            ClearRecoveryAvatar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            DeleteUnreferencedFolder();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var path in Prefabs)
                Check(!AssetDatabase.GetDependencies(path, true).Any(p => p.StartsWith(Folder + "/", StringComparison.Ordinal)), path + " has no legacy model dependencies");
            AssetCleanupAudit.Run();
            File.WriteAllText(Output + "/result.txt", Report.ToString() + "PASS cleanup complete\n");
            DistributionBuild.Batch();
        }
        catch (Exception e)
        {
            File.WriteAllText(Output + "/result.txt", Report.ToString() + "FAIL " + e);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void CleanPrefab(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var legacy = root.transform.Find("miku");
            Check(legacy != null && !legacy.gameObject.activeSelf, path + " prototype root is explicitly disabled");
            var vfx = root.GetComponent<BossVfxController>();
            Check(vfx != null && vfx.CoreRenderers.All(r => r != null && !Inside(r, legacy)), "All boss cores belong to retained hierarchy");
            var kept = root.GetComponentsInChildren<Transform>(true).Where(t => !Inside(t, legacy)).ToArray();
            var poses = kept.Select(t => (t, t.localPosition, t.localRotation, t.localScale, t.gameObject.activeSelf)).ToArray();
            // Refuse to destroy a prototype used by any component except the known obsolete VFX list.
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || Inside(component, legacy)) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || !Inside(property.objectReferenceValue, legacy)) continue;
                    bool expected = component == root.transform && property.propertyPath.StartsWith("m_Children.", StringComparison.Ordinal)
                        || component == vfx && property.propertyPath.StartsWith("BodyRenderers.", StringComparison.Ordinal);
                    Check(expected, "Reviewed legacy reference: " + component.GetType().Name + "." + property.propertyPath);
                }
            }
            int objects = legacy.GetComponentsInChildren<Transform>(true).Length;
            int oldRenderers = vfx.BodyRenderers.Length;
            vfx.BodyRenderers = vfx.BodyRenderers.Where(r => r != null && !Inside(r, legacy)).ToArray();
            Object.DestroyImmediate(legacy.gameObject);
            Check(vfx.BodyRenderers.Length == 3 && oldRenderers - vfx.BodyRenderers.Length == 9,
                path + " kept goblin and two shoulder renderers; removed nine inactive renderer references");
            foreach (var pose in poses)
                Check(pose.t != null && pose.t.localPosition == pose.localPosition && pose.t.localRotation == pose.localRotation
                    && pose.t.localScale == pose.localScale && pose.t.gameObject.activeSelf == pose.activeSelf,
                    "Retained transform " + PathOf(pose.t, root.transform));
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Check(saved != null, path + " removed " + objects + " disabled prototype objects");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Dictionary<string, Vector3[]> SampleGoblin(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        var result = new Dictionary<string, Vector3[]>();
        try
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            SceneManager.MoveGameObjectToScene(root, scene);
            var animator = root.GetComponent<Animator>();
            Check(animator != null && AssetDatabase.GetAssetPath(animator.avatar) == "Assets/GameObjects/Monster/goblin/goblin.fbx", path + " uses goblin avatar");
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
            Check(renderers.Length == 1 && AssetDatabase.GetAssetPath(renderers[0].sharedMesh) == "Assets/GameObjects/Monster/goblin/goblin.fbx", path + " visible skinned mesh is goblin only");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (string state in new[] { "Locomotion", "Attack", "Hit", "Die" })
            foreach (float time in new[] { 0.2f, 0.7f })
            {
                animator.Rebind(); animator.Update(0);
                Check(animator.HasState(0, Animator.StringToHash("Base Layer." + state)), "Animator has " + state);
                animator.Play(state, 0, time); animator.Update(0);
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName(state), "Animator samples " + state);
                foreach (var renderer in renderers)
                {
                    var mesh = new Mesh();
                    try
                    {
                        renderer.BakeMesh(mesh);
                        Check(mesh.vertexCount > 0, "Baked goblin geometry exists");
                        string key = path + "|" + state + "|" + time + "|" + PathOf(renderer.transform, root.transform)
                            + "|" + AssetId(renderer.sharedMesh) + "|" + string.Join(",", renderer.sharedMaterials.Select(AssetId));
                        result.Add(key, mesh.vertices.Select(v => root.transform.InverseTransformPoint(renderer.transform.TransformPoint(v))).ToArray());
                    }
                    finally { Object.DestroyImmediate(mesh); }
                }
            }
            return result;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void ClearRecoveryAvatar()
    {
        const string path = "Assets/_Recovery/0.unity";
        var avatarPath = Folder + "/source/hatsune-miku/source/Hatsune Miku/miku_prefab.fbx";
        string guid = AssetDatabase.AssetPathToGUID(avatarPath);
        Check(!string.IsNullOrEmpty(guid), "Legacy recovery avatar identified");
        string text = File.ReadAllText(path);
        string oldValue = "m_Avatar: {fileID: 9000000, guid: " + guid + ", type: 3}";
        Check(text.Split(new[] { oldValue }, StringSplitOptions.None).Length == 2, "Recovery scene has one stale avatar reference");
        File.Copy(path, Output + "/Recovery0.unity.before", true);
        // The historical scene's old model is already missing. Do not assign an unrelated avatar.
        File.WriteAllText(path, text.Replace(oldValue, "m_Avatar: {fileID: 0}"));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        Check(!File.ReadAllText(path).Contains(guid), "Cleared stale recovery avatar without changing other scene objects");
    }

    static void DeleteUnreferencedFolder()
    {
        string assetsRoot = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Folder);
        Check(full.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase) && AssetDatabase.IsValidFolder(Folder), "Deletion stays inside the reviewed model folder");
        foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !p.StartsWith(Folder + "/", StringComparison.Ordinal) && File.Exists(p)))
            if (AssetDatabase.GetDependencies(path, false).Any(d => d == Folder || d.StartsWith(Folder + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Legacy dependency remains: " + path);
        var files = Directory.GetFiles(full, "*", SearchOption.AllDirectories).Concat(new[] { full + ".meta" }).ToArray();
        var guids = files.Where(p => p.EndsWith(".meta", StringComparison.Ordinal)).Select(p => Regex.Match(File.ReadAllText(p), @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value).Where(g => g.Length == 32).ToHashSet();
        var extensions = new HashSet<string> { ".meta", ".prefab", ".unity", ".asset", ".mat", ".controller", ".overridecontroller", ".anim", ".json" };
        foreach (var path in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories).Concat(Directory.GetFiles("ProjectSettings", "*.asset")))
        {
            if (Path.GetFullPath(path).StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path) == full + ".meta" || !extensions.Contains(Path.GetExtension(path))) continue;
            if (Regex.Matches(File.ReadAllText(path), @"guid:\s*([a-f0-9]{32})").Cast<Match>().Any(m => guids.Contains(m.Groups[1].Value)))
                throw new InvalidOperationException("Serialized legacy GUID remains: " + path);
        }
        File.WriteAllLines(Output + "/deleted-files.tsv", files.Select(p => p + "\t" + new FileInfo(p).Length));
        Check(AssetDatabase.DeleteAsset(Folder), "Deleted remaining unused model folder and metadata");
        Check(!Directory.Exists(full) && !File.Exists(full + ".meta"), "Legacy folder fully removed");
    }
}
