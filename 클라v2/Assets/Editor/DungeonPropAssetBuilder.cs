using System;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

public static class DungeonPropAssetBuilder
{
    const string Folder = "Assets/DungeonProps";
    static readonly string[] Models = { "Box_02", "Table_01", "Chair_05", "Jug_02", "Pot_01", "Rock_01", "Wood_02", "Book_09", "Bottle_05", "Candle_01" };

    public static DungeonPropPalette Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "DungeonProps");
        var palette = AssetDatabase.LoadAssetAtPath<DungeonPropPalette>("Assets/Resources/DungeonPropPalette.asset");
        if (palette == null) { palette = ScriptableObject.CreateInstance<DungeonPropPalette>(); AssetDatabase.CreateAsset(palette, "Assets/Resources/DungeonPropPalette.asset"); }
        palette.entries = new DungeonPropPalette.Entry[Models.Length];
        for (int i = 0; i < Models.Length; i++)
        {
            var kind = (DungeonPropLayout.Kind)i;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LowPolyDungeonsLite/Prefabs/" + Models[i] + ".prefab");
            if (source == null) throw new InvalidOperationException("Missing prop source " + Models[i]);
            var root = new GameObject("Prop_" + kind);
            try
            {
                root.layer = LayerMask.NameToLayer("Wall");
                var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
                int members = kind == DungeonPropLayout.Kind.Wood ? 3 : 1;
                float supportTop = 0f;
                for (int member = 0; member < members; member++)
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    model.transform.SetParent(visual, false);
                    // Three thin boards form a shallow pile, rather than stretching one board into a beam.
                    if (members > 1)
                    {
                        model.transform.localRotation = Quaternion.Euler(0, 90 + (member - 1) * 8, 0);
                        model.transform.localPosition = new Vector3((member - 1) * .06f, 0, (member - 1) * .07f);
                        var plankBounds = MeshBounds(model);
                        model.transform.localPosition += Vector3.up * (supportTop - plankBounds.min.y);
                        supportTop += plankBounds.size.y;
                    }
                    foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                    foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                    foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(script);
                }
                Bounds bounds = MeshBounds(root);
                if (Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z) <= .00001f) throw new InvalidOperationException("Degenerate prop mesh " + kind);
                var scale = new Vector3(1 / bounds.size.x, 1 / bounds.size.y, 1 / bounds.size.z);
                visual.localScale = scale;
                visual.localPosition = -Vector3.Scale(bounds.center, scale);
                root.AddComponent<BoxCollider>().size = Vector3.one;
                var volume = root.AddComponent<NavMeshModifierVolume>();
                volume.center = Vector3.zero; volume.size = Vector3.one; volume.area = 1; // Not Walkable
                var saved = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Prop_" + kind + ".prefab");
                palette.entries[i] = new DungeonPropPalette.Entry { kind = kind, prefab = saved };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        EditorUtility.SetDirty(palette); AssetDatabase.SaveAssets();
        return palette;
    }

    public static Bounds MeshBounds(GameObject root)
    {
        bool found = false; Bounds result = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer) continue;
            if (!found) { result = renderer.bounds; found = true; } else result.Encapsulate(renderer.bounds);
        }
        if (!found) throw new InvalidOperationException("No mesh renderers in " + root.name);
        return result;
    }
}
