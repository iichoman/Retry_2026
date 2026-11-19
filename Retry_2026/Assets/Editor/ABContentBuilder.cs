using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class ABContentBuilder
{
    [Serializable]
    public class Row
    {
        public string id, label;
        public int effect, tier, value, kind;
    }
    [Serializable]
    public class Data
    {
        public Row[] items;
    }
    public static void Build()
    {
        if (System.Runtime.InteropServices.Marshal.SizeOf<ABCommand>() != 20 ||
            System.Runtime.InteropServices.Marshal.SizeOf<ABSnapshot>() != 84 ||
            System.Runtime.InteropServices.Marshal.SizeOf<ABActorState>() != 28)
            throw new Exception("AB packet size mismatch");
        BuildVfx();
        var catalog = ItemCatalog.Load();
        var all = new List<ItemData>(catalog.Items);
        const string folder = "Assets/CombatContent/Items/AB";
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        foreach (var row in ABItemDefinitions.All)
        {
            var item = catalog.Resolve(row.id);
            bool existing = item != null;
            string path = folder + "/" + row.id + ".asset";
            if (item == null)
                item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null && row.kind == 2 && !(item is EquipmentItemData))
            {
                path = AssetDatabase.GetAssetPath(item);
                all.Remove(item);
                item = ScriptableObject.CreateInstance<EquipmentItemData>();
                AssetDatabase.CreateAsset(item, path);
                existing = false;
            }
            if (item == null)
            {
                item = row.kind == 2 ? ScriptableObject.CreateInstance<EquipmentItemData>()
                                     : ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(item, path);
            }
            var so = new SerializedObject(item);
            so.FindProperty("itemId").stringValue = row.id;
            so.FindProperty("displayName").stringValue = row.label;
            so.FindProperty("itemType").enumValueIndex = (int)(row.kind == 0   ? ItemType.Consumable
                                                               : row.kind == 2 ? ItemType.Weapon
                                                                               : row.effect==15?ItemType.Active:ItemType.Passive);
            so.FindProperty("maxStack").intValue = row.kind == 0 ? 16 : 1;
            so.FindProperty("extractionValue").intValue = row.value;
            so.FindProperty("rarity").enumValueIndex = row.kind == 2 ? (row.effect == 0 ? 0 : 2) : row.tier;
            so.FindProperty("description").stringValue = row.description;
            if (row.kind == 2)
                so.FindProperty("weaponId").stringValue = row.effect >= 5 && row.effect <= 7 ? GreatswordWeapon.Id : row.effect == 4 ? "test_gun" : "test_sword";
            var source = catalog.Resolve(row.kind == 2 ? "sword_001" : row.kind == 0 ? "health_potion" : "rare_gem");
            if (source != null && so.FindProperty("icon").objectReferenceValue == null)
                so.FindProperty("icon").objectReferenceValue = source.Icon;
            if (row.id != "sword_001" && row.id != "gun_001" && row.id != "health_potion")
                so.FindProperty("icon").objectReferenceValue = ABIconBuilder.Get(row);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (!existing && !all.Contains(item))
                all.Add(item);
        }
        catalog.EditorSetItems(all);
        EditorUtility.SetDirty(catalog);
        if (!catalog.ValidateCatalog())
            throw new Exception("Invalid AB catalog");
        AssetDatabase.SaveAssets();
    }
    static void BuildVfx()
    {
        const string folder = "Assets/Resources/AB";
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        string[] names = { "Flame", "Ice", "Beam" };
        Color[] colors = { new Color(1, .012f, .002f), new Color(.30f, .75f, 1), new Color(.025f, 1, .08f) };
        for (int i = 0; i < 3; i++)
        {
            string path = folder + "/" + names[i] + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Retry/VFX/Combat Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", colors[i]);
            mat.SetFloat("_Intensity", 3);
            EditorUtility.SetDirty(mat);
            var go = new GameObject(names[i]);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = mat;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = i == 2 ? .11f : .095f;
            line.endWidth = i == 2 ? .08f : .055f;
            if (i != 2)
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = .3f;
                main.startSize = .07f;
                main.startSpeed = .2f;
                main.maxParticles = 24;
                main.startColor = colors[i];
                var emission = ps.emission;
                emission.rateOverTime = 16;
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = CombatVfxCatalog.Load().ParticleMaterial;
            }
            else
            {
                var core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                core.name = "Beam Core";
                core.transform.SetParent(go.transform, false);
                core.transform.localRotation = Quaternion.Euler(90, 0, 0);
                core.GetComponent<Renderer>().sharedMaterial = mat;
                UnityEngine.Object.DestroyImmediate(core.GetComponent<Collider>());
                var hiltMat = AssetDatabase.LoadAssetAtPath<Material>(folder + "/BeamHilt.mat");
                if (hiltMat == null)
                {
                    hiltMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(hiltMat, folder + "/BeamHilt.mat");
                }
                hiltMat.SetColor("_BaseColor", new Color(.08f, .1f, .12f));
                hiltMat.SetFloat("_Metallic", .85f);
                EditorUtility.SetDirty(hiltMat);
                for (int part = 0; part < 3; part++)
                {
                    var hilt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    hilt.name = "Beam hilt " + part;
                    hilt.transform.SetParent(go.transform, false);
                    hilt.transform.localPosition = new Vector3(0, 0, -.06f - .09f * part);
                    hilt.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    hilt.transform.localScale = new Vector3(part == 1 ? .055f : .08f, .045f, part == 1 ? .055f : .08f);
                    hilt.GetComponent<Renderer>().sharedMaterial = hiltMat;
                    UnityEngine.Object.DestroyImmediate(hilt.GetComponent<Collider>());
                }
            }
            PrefabUtility.SaveAsPrefabAsset(go, folder + "/" + names[i] + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
        string[] statusNames = { "Burn", "Freeze", "Stun", "Poison" };
        Color[] statusColors = { new Color(1, .2f, .025f), new Color(.2f, .75f, 1), new Color(1, .85f, .1f),
                                 new Color(.4f, 1, .18f) };
        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject(statusNames[i]);
            go.transform.position = Vector3.up * .9f;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = .6f;
            main.startSize = .08f;
            main.startSpeed = .15f;
            main.maxParticles = 24;
            main.startColor = statusColors[i];
            var emission = ps.emission;
            emission.rateOverTime = 20;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .38f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = CombatVfxCatalog.Load().ParticleMaterial;
            PrefabUtility.SaveAsPrefabAsset(go, folder + "/" + statusNames[i] + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
