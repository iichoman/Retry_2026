using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ElementalInventoryValidation
{
    const string Output = "Artifacts/ElementalInventory";
    static ElementalInventoryValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Output + "/build.request")) return;
        File.Delete(Output + "/build.request");
        try { BuildAndValidate(); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", "FAIL\n" + e); Debug.LogException(e); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("PASS " + message); }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    [MenuItem("Retry/Inventory/Build and validate elemental equipment")]
    public static void BuildAndValidate()
    {
        Directory.CreateDirectory(Output);
        var catalog = ItemCatalog.Load();
        var items = catalog.Items.ToList();
        foreach (var definition in ABItemDefinitions.All)
        {
            if (definition.kind == 0)
            {
                var so = new SerializedObject(catalog.Resolve(definition.id));
                so.FindProperty("maxStack").intValue = 16;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (definition.effect < 6 || definition.effect > 7 || definition.kind != 2) continue;
            string path = "Assets/CombatContent/KnightGreatsword/" + definition.id + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<EquipmentItemData>(path);
            if (item == null)
            {
                item = Object.Instantiate((EquipmentItemData)catalog.Resolve("greatsword_001"));
                AssetDatabase.CreateAsset(item, path);
                items.Add(item);
            }
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = definition.id;
            serialized.FindProperty("displayName").stringValue = definition.label;
            serialized.FindProperty("description").stringValue = definition.description;
            serialized.FindProperty("extractionValue").intValue = definition.value;
            serialized.FindProperty("icon").objectReferenceValue = ABIconBuilder.Get(definition);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        catalog.EditorSetItems(items); EditorUtility.SetDirty(catalog);
        foreach (string path in new[] { "Assets/Prefabs/LocalPlayerPrefab.prefab", "Assets/Prefabs/RemotePlayerPrefab.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.GetComponentInChildren<GreatswordWeapon>(true).CorrectBladeFacing();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Validate();
        typeof(ExtractionContentBuild).GetMethod("ExportPacketLayout", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        KnightGreatswordValidation.Render();
        File.WriteAllText(Output + "/result.txt", "PASS catalog, potion stacks, duplicate equipment selection, equipped slots and renders\n");
    }

    public static void Validate()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var catalog = ItemCatalog.Load(); Check(catalog.ValidateCatalog(), "catalog is valid");
            foreach (string id in new[] { "flame_greatsword", "frost_greatsword" })
                Check(catalog.Resolve(id) is EquipmentItemData item && item.WeaponId == GreatswordWeapon.Id && item.MaxStack == 1, id);
            var owner = new GameObject("Inventory fixture"); SceneManager.MoveGameObjectToScene(owner, scene);
            var inventory = owner.AddComponent<PlayerInventory>(); Call(inventory, "Awake"); inventory.ClearAll();
            var potion = catalog.Resolve("health_potion"); var sword = catalog.Resolve("flame_greatsword");
            Check(inventory.TryAddDetailed(potion, 30).FullyAdded && inventory.UsedSlots == 2, "30 potions occupy 2 slots");
            Check(inventory.GetSlot(0).count == 16 && inventory.GetSlot(1).count == 14, "16 potion stack maximum");
            Check(inventory.TryAddDetailed(sword, 28).FullyAdded && inventory.UsedSlots == 30, "28 separate swords fit beside 30 potions");
            Check(inventory.TryAddDetailed(potion, 3).AddedCount == 2 && !inventory.TryAdd(sword), "full bag fills only existing partial stack");
            inventory.SetServerOwned();
            var snapshot = new InventorySyncData { entryCount = 3, totalCount = 34, entries = new ItemStack[32] };
            snapshot.entries[0] = new ItemStack { itemHash = ItemHash.Of("health_potion"), count = 30 };
            snapshot.entries[1] = new ItemStack { itemHash = ItemHash.Of("flame_greatsword"), count = 2 };
            snapshot.entries[2] = new ItemStack { itemHash = ItemHash.Of("life_amulet_t1"), count = 2 };
            Check(inventory.ApplyServerSnapshot(snapshot, catalog) && inventory.UsedSlots == 6, "server snapshot accepts 34 units in 6 slots");
            var host = new GameObject("Network fixture"); SceneManager.MoveGameObjectToScene(host, scene);
            var bootstrap = host.AddComponent<NetworkBootstrap>(); var client = host.AddComponent<ABClient>();
            Set(bootstrap, "<Identity>k__BackingField", host.AddComponent<LocalIdentity>());
            Set(client, "bootstrap", bootstrap); Set(client, "<Supported>k__BackingField", true);
            var state = new ABSnapshot { version = 1, hp = 100, weapon = snapshot.entries[1].itemHash, passive = snapshot.entries[2].itemHash };
            Set(client, "<State>k__BackingField", state);
            var view = Object.Instantiate(Resources.Load<GameObject>("ExpeditionUI/View")); SceneManager.MoveGameObjectToScene(view, scene);
            var ui = view.GetComponent<ExpeditionUI>(); ui.enabled = false;
            ui.StyleInventory(); ui.StyleQuickAccess(); ui.StyleUpgradeCards(); ui.Bind(inventory, null);
            Set(ui, "client", client); Call(ui, "Refresh");
            var cells = Field<ExpeditionUI.Cell[]>(ui, "cells");
            Check(cells.Count(c => c.tag.text == "장착 중") == 2 && cells[2].tag.text == "장착 중" && cells[4].tag.text == "장착 중", "one copy of each equipped item is marked");
            Set(ui, "pendingItemSequence", 1); Set(ui, "pendingEquipHash", state.weapon); Set(ui, "pendingEquipOccurrence", 1);
            state.sequence = 1; Set(client, "<State>k__BackingField", state); Call(ui, "ServerChanged");
            Check(cells[2].tag.text == "" && cells[3].tag.text == "장착 중", "server acknowledgement marks the selected duplicate");
            Set(ui, "pendingItemSequence", 2); Set(ui, "pendingEquipHash", state.passive); Set(ui, "pendingEquipOccurrence", 1);
            state.sequence = 2; Set(client, "<State>k__BackingField", state); Call(ui, "ServerChanged");
            Check(cells[4].tag.text == "" && cells[5].tag.text == "장착 중", "passive duplicate selection follows acknowledgement");
            Check(Field<Image>(ui, "equippedWeaponIcon").sprite == sword.Icon, "dedicated weapon slot shows equipped icon");
            ui.SetInventoryVisible(true); ui.Select(3); Capture(view, scene);
            Set(ui, "client", null);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void Capture(GameObject view, Scene scene)
    {
        var camera = new GameObject("Inventory camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
        camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.03f,.04f,.05f);
        var canvas = view.GetComponent<Canvas>(); view.GetComponent<CanvasScaler>().enabled = false;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        var font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 20);
        foreach (var label in view.GetComponentsInChildren<Text>(true)) label.font = font;
        var rt = new RenderTexture(1920,1080,24); var texture = new Texture2D(1920,1080,TextureFormat.RGB24,false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt; texture.ReadPixels(new Rect(0,0,1920,1080),0,0); texture.Apply();
            File.WriteAllBytes(Output + "/inventory.png",texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(font); }
    }
}
