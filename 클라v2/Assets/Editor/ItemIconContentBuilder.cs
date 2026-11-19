#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Small, hand-defined shapes: no downloaded art and no runtime texture generation.</summary>
public static class ItemIconContentBuilder
{
    public const string IconFolder = "Assets/Resources/UI/ItemIcons";
    private const string Output = "Artifacts/ItemIcons";
    private const int Size = 128;
    private const int Samples = 3;
    private static readonly string[] Ids = { "gold_coin", "iron_ore", "health_potion", "rare_gem", "boss_core", "sword_001", "gun_001", "item_unknown" };
    private static readonly Color Ink = C(0x18222d);
    private static readonly Color Gold = C(0xe5b94f);
    private static readonly Color GoldLight = C(0xffe8a0);
    private static readonly Color Steel = C(0xa7c6d2);

    [MenuItem("Retry/Build And Validate Item Icons")]
    public static void BuildAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build icons outside Play mode.");
        Directory.CreateDirectory(IconFolder);
        Directory.CreateDirectory(Output);
        foreach (string id in Ids)
        {
            var texture = Draw(id);
            try { File.WriteAllBytes(PathFor(id), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(PathFor(id), ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PathFor(id));
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Size;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogBuilder.CatalogPath);
        if (catalog == null) throw new InvalidOperationException("Build the existing ItemCatalog first.");
        BindAvailableIcons(catalog);
        NormalizeSlotCountLayout();
        var fallback = AssetDatabase.LoadAssetAtPath<Sprite>(PathFor("item_unknown"));
        foreach (var guid in AssetDatabase.FindAssets("t:UITextureSet"))
        {
            var set = AssetDatabase.LoadAssetAtPath<UITextureSet>(AssetDatabase.GUIDToAssetPath(guid));
            if (set == null || set.missingIconSprite != null) continue;
            set.missingIconSprite = fallback;
            set.missingIconFallbackColor = Color.white;
            EditorUtility.SetDirty(set);
        }
        AssetDatabase.SaveAssets();
        Validate(catalog);
        ExportPreview();
    }

    // A catalog rebuild updates definitions while retaining artist-authored/custom icons.
    public static void BindAvailableIcons(ItemCatalog catalog)
    {
        if (catalog == null) return;
        foreach (var item in catalog.Items)
        {
            if (item == null) continue;
            if (item.Icon != null && !AssetDatabase.GetAssetPath(item.Icon).StartsWith(IconFolder + "/", StringComparison.Ordinal)) continue;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PathFor(item.ItemId));
            if (sprite == null) continue;
            var serialized = new SerializedObject(item);
            serialized.FindProperty("icon").objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
        }
    }

    private static void NormalizeSlotCountLayout()
    {
        const string path = "Assets/GameObjects/UI/Slot/Slot.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var slot = prefab.GetComponent<InventorySlotUI>();
            var count = new SerializedObject(slot).FindProperty("countText").objectReferenceValue as Text;
            if (count == null) throw new InvalidOperationException("Existing slot prefab countText is missing.");
            var rect = count.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-6f, 4f);
            rect.sizeDelta = new Vector2(54f, 24f);
            count.alignment = TextAnchor.LowerRight;
            count.fontSize = 18;
            count.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    private static void Validate(ItemCatalog catalog)
    {
        var report = new List<string>();
        foreach (string id in Ids)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PathFor(id));
            Require(sprite != null && sprite.rect.size == new Vector2(Size, Size), "128px Sprite: " + id, report);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PathFor(id));
            Require(!importer.isReadable && !importer.mipmapEnabled && importer.alphaIsTransparency, "UI importer: " + id, report);
            if (id != "item_unknown") Require(catalog.Resolve(id)?.Icon != null, "ItemData binding: " + id, report);
            var t = Draw(id);
            try
            {
                var pixels = t.GetPixels32();
                Require(pixels[0].a == 0 && pixels[pixels.Length - 1].a == 0 && pixels.Count(p => p.a > 200) > 900, "Transparent silhouette: " + id, report);
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }
        var slotAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/UI/Slot/Slot.prefab").GetComponent<InventorySlotUI>();
        var countText = new SerializedObject(slotAsset).FindProperty("countText").objectReferenceValue as Text;
        Require(countText != null && countText.alignment == TextAnchor.LowerRight && countText.rectTransform.anchorMax == new Vector2(1f, 0f), "Slot quantity stays inside bottom-right corner", report);
        ValidateViews(catalog, report);
        File.WriteAllLines(Output + "/validation.txt", report);
        Debug.Log($"[ItemIcons] PASS {report.Count} checks; 7 catalog icons + common fallback. Preview: {Output}/ItemIcons.png");
    }

    private static void ValidateViews(ItemCatalog catalog, List<string> report)
    {
        var root = new GameObject("Item icon validation", typeof(RectTransform), typeof(Canvas));
        try
        {
            var slot = new GameObject("Inventory/local loot slot", typeof(RectTransform)).AddComponent<InventorySlotUI>();
            slot.transform.SetParent(root.transform, false);
            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>(); icon.transform.SetParent(slot.transform, false);
            var serialized = new SerializedObject(slot); serialized.FindProperty("iconImage").objectReferenceValue = icon; serialized.ApplyModifiedPropertiesWithoutUndo();
            foreach (var item in catalog.Items)
            {
                slot.Refresh(new InventorySlot { item = item, count = 2 }, null, false);
                Require(icon.sprite == item.Icon && icon.preserveAspect, "Inventory/local loot display: " + item.ItemId, report);
            }
            var server = new GameObject("Server loot", typeof(RectTransform)).AddComponent<ServerLootUI>(); server.transform.SetParent(root.transform, false);
            server.Open(_ => { }, () => { }, () => { });
            var entries = catalog.Items.Select(item => new ItemStack { itemHash = ItemHash.Of(item.ItemId), count = 2 }).ToArray();
            server.Refresh(new LootSpawnData { entries = entries, entryCount = entries.Length }, catalog, true, _ => false);
            foreach (var item in catalog.Items) Require(server.GetComponentsInChildren<Image>().Any(image => image.sprite == item.Icon), "Server loot display: " + item.ItemId, report);
            var resultObject = new GameObject("Result", typeof(RectTransform)); resultObject.transform.SetParent(root.transform, false);
            var resultUI = resultObject.AddComponent<DungeonResultUI>();
            var text = new GameObject("Items", typeof(RectTransform)).AddComponent<Text>(); text.transform.SetParent(resultObject.transform, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.rectTransform.sizeDelta = new Vector2(672f, 288f);
            serialized = new SerializedObject(resultUI); serialized.FindProperty("itemsText").objectReferenceValue = text; serialized.ApplyModifiedPropertiesWithoutUndo();
            var result = new DungeonExitResult { serverConfirmedInventory = true };
            foreach (var item in catalog.Items) result.AddItem(item, 2, 13);
            resultUI.Show(result, null);
            foreach (var item in catalog.Items) Require(resultObject.GetComponentsInChildren<Image>().Any(image => image.sprite == item.Icon), "Extraction display: " + item.ItemId, report);
            Require(resultObject.GetComponentsInChildren<Text>().Count(label => label.text.Contains("총 가치 26")) == 7, "Extraction uses snapshot values", report);
            resultUI.Show(result, null);
            Require(resultObject.GetComponentsInChildren<ScrollRect>().Single().content.childCount == 7, "Result rows reused", report);
            resultUI.Show(null, null);
            Require(resultObject.GetComponentsInChildren<ScrollRect>().Length == 0 && !text.text.Contains("확정"), "Null result clears stale rewards", report);
            resultUI.Show(result, null);
            result.snapshotUnavailable = true; resultUI.Show(result, null);
            Require(resultObject.GetComponentsInChildren<ScrollRect>().Length == 0, "Unconfirmed snapshot hides icon rows", report);
            ItemIconPresentation.Apply(icon, null);
            Require(icon.sprite != null && icon.sprite == ItemIconPresentation.Fallback, "Missing item fallback", report);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool value, string label, List<string> report)
    {
        if (!value) throw new InvalidOperationException("[ItemIcons] " + label);
        report.Add("PASS " + label);
    }
    private static string PathFor(string id) => IconFolder + "/" + id + ".png";

    [MenuItem("Retry/Preview Item UI")]
    public static void RenderUiPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Preview outside Play mode.");
        Directory.CreateDirectory(Output);
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogBuilder.CatalogPath);
        var source = UnityEngine.Object.FindObjectsByType<DungeonResultUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(view => view.gameObject.scene.path == "Assets/Scenes/SampleScene.unity");
        if (catalog == null || source == null) throw new InvalidOperationException("Load SampleScene with its DungeonResultUI before rendering UI preview.");
        var scene = EditorSceneManager.NewPreviewScene();
        var stage = new GameObject("Item UI Preview");
        SceneManager.MoveGameObjectToScene(stage, scene);
        var report = new List<string>();
        try
        {
            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.transform.localPosition = new Vector3(0f, 0f, -1000f);
            camera.orthographic = true; camera.orthographicSize = 540f;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = C(0x202934);
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene); camera.cullingMask = 1 << 31;
            var canvasObject = new GameObject("Preview Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(stage.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920f, 1080f);

            // Duplicate the actual scene panel without saving or modifying its source object.
            var resultView = UnityEngine.Object.Instantiate(source.gameObject, canvas.transform).GetComponent<DungeonResultUI>();
            var result = new DungeonExitResult { serverConfirmedInventory = true, elapsedSeconds = 367f, escapedPlayerCount = 1, monsterKillCount = 12, gold = 42, bossKillCount = 1 };
            result.escapedPlayerNames.Add("아이콘 미리보기");
            foreach (var item in catalog.Items) { result.AddItem(item, 2); result.totalValue += item.ExtractionValue * 2L; }
            resultView.Show(result, null);
            Canvas.ForceUpdateCanvases();
            var scroll = resultView.GetComponentInChildren<ScrollRect>();
            Require(scroll != null && scroll.content.childCount == 7, "Actual result panel has seven icon rows", report);
            Require(scroll.viewport.rect.height > 200f && scroll.content.rect.height > scroll.viewport.rect.height, "Actual result viewport scrolls seven rows", report);
            scroll.verticalNormalizedPosition = 1f;
            RenderUi(camera, stage, "Result_Top.png");
            Require(Mathf.Abs(scroll.content.anchoredPosition.y) < 1f, "Top scroll position exposes first row", report);
            scroll.verticalNormalizedPosition = 0f;
            RenderUi(camera, stage, "Result_Bottom.png");
            Require(Mathf.Abs(scroll.content.anchoredPosition.y - (scroll.content.rect.height - scroll.viewport.rect.height)) < 1f, "Bottom scroll position exposes seventh row", report);
            resultView.gameObject.SetActive(false);

            var slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/UI/Slot/Slot.prefab");
            if (slotPrefab == null) throw new InvalidOperationException("Existing inventory slot prefab is missing.");
            for (int i = 0; i < catalog.Items.Count; i++)
            {
                var slot = UnityEngine.Object.Instantiate(slotPrefab, canvas.transform).GetComponent<InventorySlotUI>();
                var rect = (RectTransform)slot.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-765f + (i % 4) * 160f, 130f - (i / 4) * 170f);
                rect.sizeDelta = new Vector2(144f, 144f);
                slot.Refresh(new InventorySlot { item = catalog.Items[i], count = 2 }, null, false);
            }
            var gridTitle = new GameObject("Inventory Preview Label", typeof(RectTransform)).AddComponent<Text>();
            gridTitle.transform.SetParent(canvas.transform, false);
            gridTitle.rectTransform.anchorMin = gridTitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            gridTitle.rectTransform.pivot = new Vector2(0f, 1f);
            gridTitle.rectTransform.anchoredPosition = new Vector2(-837f, 290f);
            gridTitle.rectTransform.sizeDelta = new Vector2(680f, 50f);
            gridTitle.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            gridTitle.fontSize = 26; gridTitle.color = Color.white; gridTitle.text = "Inventory / Local loot";
            var online = new GameObject("Online Loot Preview", typeof(RectTransform)).AddComponent<ServerLootUI>();
            // ServerLootUI owns a root overlay Canvas. Nesting it under the world-space
            // preview Canvas changes that parent's render mode and moves the entire grid.
            // Keep it on a separate root, then convert only its Canvas for the camera.
            online.transform.SetParent(stage.transform, false);
            online.Open(_ => { }, () => { }, () => { });
            var onlineCanvas = online.GetComponent<Canvas>(); onlineCanvas.renderMode = RenderMode.WorldSpace; onlineCanvas.worldCamera = camera;
            var scaler = online.GetComponent<CanvasScaler>(); if (scaler != null) scaler.enabled = false;
            var onlineRect = (RectTransform)online.transform;
            onlineRect.anchorMin = onlineRect.anchorMax = onlineRect.pivot = new Vector2(0.5f, 0.5f);
            onlineRect.sizeDelta = new Vector2(720f, 760f);
            onlineRect.localPosition = new Vector3(470f, 0f, 0f); onlineRect.localScale = Vector3.one;
            var entries = catalog.Items.Select(item => new ItemStack { itemHash = ItemHash.Of(item.ItemId), count = 2 }).ToArray();
            online.Refresh(new LootSpawnData { entries = entries, entryCount = entries.Length }, catalog, true, _ => false);
            Require(canvas.renderMode == RenderMode.WorldSpace && canvas.transform.localPosition.sqrMagnitude < 0.01f, "Inventory preview Canvas preserved", report);
            RenderUi(camera, stage, "Inventory_And_Loot.png");
            online.Close();
            report.Add("Preview: Inventory_And_Loot.png uses actual inventory/local-loot slot prefab and online loot view.");
            File.WriteAllLines(Output + "/ui-validation.txt", report);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stage);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void RenderUi(Camera camera, GameObject stage, string name)
    {
        foreach (var node in stage.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(1920, 1080, 24);
        var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes(Output + "/" + name, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    private static Color C(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    private static Texture2D Draw(string id)
    {
        var p = new Painter();
        switch (id)
        {
            case "gold_coin":
                Coin(p, 79, 57, 25); Coin(p, 48, 81, 27); Coin(p, 78, 86, 26); break;
            case "iron_ore":
                p.Poly(C(0x607483), 5, 20,85, 30,51, 52,26, 82,31, 108,65, 97,97, 51,106);
                p.Poly(C(0x9cb0ba), 0, 30,51, 52,26, 67,57, 47,80, 20,85);
                p.Poly(C(0xd3dfe2), 0, 52,26, 82,31, 87,65, 67,57);
                p.Poly(C(0x465867), 0, 47,80, 67,57, 87,65, 97,97, 51,106);
                p.Line(C(0xe1ebe8), 3, 41,49, 50,38); p.Line(C(0x8b633f), 5, 54,89, 70,72); break;
            case "health_potion":
                p.Poly(C(0xa6c7cc), 5, 49,27, 79,27, 79,50, 98,70, 101,95, 87,111, 41,111, 27,95, 30,70, 49,50);
                p.Poly(C(0xbe4051), 0, 33,76, 59,79, 96,75, 95,94, 83,105, 45,105, 33,94);
                p.Poly(C(0xec6670), 0, 34,76, 59,79, 69,79, 60,105, 45,105, 33,94);
                p.Poly(C(0xc79867), 4, 45,15, 83,15, 80,34, 48,34);
                p.Line(C(0xf3e5c6), 3, 53,20, 73,20);
                p.Line(Color.white, 5, 42,59, 37,69); p.Line(C(0xfdd4c5), 4, 69,87, 69,96); p.Line(C(0xfdd4c5), 4, 64,92, 74,92); break;
            case "rare_gem":
                p.Poly(C(0x438ddb), 5, 42,24, 86,24, 110,52, 64,111, 18,52);
                p.Poly(C(0xb4edff), 0, 42,24, 64,24, 48,52, 18,52);
                p.Poly(C(0x76cbf6), 0, 64,24, 86,24, 79,52, 48,52);
                p.Poly(C(0x2467aa), 0, 86,24, 110,52, 79,52);
                p.Poly(C(0x3483d0), 0, 18,52, 48,52, 64,111);
                p.Poly(C(0x82dafa), 0, 48,52, 79,52, 64,111);
                p.Line(Ink, 2, 18,52, 110,52); p.Line(C(0xe2fbff), 4, 40,36, 31,46); break;
            case "boss_core":
                p.Poly(Gold, 5, 49,17, 79,17, 104,42, 104,83, 79,109, 49,109, 24,83, 24,42);
                p.Poly(C(0x39424e), 3, 52,30, 76,30, 90,47, 90,78, 76,95, 52,95, 38,78, 38,47);
                p.Poly(C(0xb82f42), 3, 64,35, 84,62, 64,92, 44,62);
                p.Poly(C(0xff725d), 0, 64,35, 64,63, 44,62);
                p.Poly(C(0xf0b173), 0, 64,46, 74,62, 64,79, 57,62);
                p.Line(GoldLight, 4, 35,45, 48,30); p.Line(GoldLight, 4, 78,96, 95,80);
                p.Poly(Gold, 3, 20,21, 39,31, 29,44); p.Poly(Gold, 3, 108,21, 89,31, 99,44); break;
            case "sword_001":
                p.Poly(Steel, 5, 105,16, 99,45, 59,86, 43,70, 84,28);
                p.Poly(C(0xe6f1e9), 0, 105,16, 51,78, 43,70, 84,28);
                p.Line(C(0x7994a5), 2, 98,29, 58,73);
                p.Poly(C(0x694b39), 4, 44,75, 54,85, 34,108, 21,96);
                p.Line(C(0xb98b55), 3, 34,87, 44,96);
                p.Poly(Gold, 4, 31,67, 39,61, 68,88, 62,97, 50,84, 43,84);
                p.Poly(Gold, 4, 20,95, 35,108, 27,116, 13,103); break;
            case "gun_001":
                p.Poly(C(0x785746), 5, 36,62, 60,65, 53,99, 31,99, 35,76, 19,75, 19,58);
                p.Poly(C(0x758d9c), 5, 21,35, 106,35, 111,57, 65,61, 57,69, 25,63, 17,54);
                p.Poly(C(0xbbd2d7), 0, 24,39, 102,39, 104,46, 23,46);
                p.Poly(C(0x415869), 2, 73,48, 109,48, 109,56, 70,56);
                p.Line(C(0x25343e), 4, 37,48, 37,57); p.Line(C(0x25343e), 4, 45,48, 45,57);
                p.Line(Ink, 5, 63,63, 61,79); p.Line(Ink, 5, 61,79, 52,81);
                p.Line(Gold, 4, 40,78, 48,78); p.Line(Gold, 4, 38,88, 45,88);
                p.Line(Ink, 5, 96,31, 96,35); break;
            default:
                p.Poly(C(0x75858d), 5, 64,22, 104,45, 104,91, 64,113, 24,91, 24,45);
                p.Poly(C(0xb4c2c6), 0, 64,22, 104,45, 64,68, 24,45);
                p.Line(Ink, 4, 64,68, 64,113); p.Line(Ink, 4, 24,45, 64,68); p.Line(Ink, 4, 64,68, 104,45);
                p.Line(GoldLight, 5, 43,35, 83,57); break;
        }
        return p.Texture();
    }

    private static void Coin(Painter p, float x, float y, float r)
    {
        p.Ellipse(C(0xa26e32), 4, x, y + 4, r, r * 0.72f);
        p.Ellipse(Gold, 4, x, y, r, r * 0.72f);
        p.Ellipse(C(0xefce72), 2, x, y, r * 0.73f, r * 0.49f);
        p.Poly(C(0xbd8738), 0, x,y-9, x+6,y, x,y+9, x-6,y);
        p.Line(GoldLight, 3, x-r*0.5f,y-r*0.32f, x-r*0.15f,y-r*0.5f);
    }

    private sealed class Painter
    {
        private const int Resolution = Size * Samples;
        private readonly Color[] pixels = new Color[Resolution * Resolution];
        public void Poly(Color fill, float stroke, params float[] coords)
        {
            var points = new Vector2[coords.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(coords[i*2], coords[i*2+1]);
            Raster(fill, stroke, point => Contains(point, points), point => EdgeDistance(point, points));
        }
        public void Ellipse(Color fill, float stroke, float x, float y, float rx, float ry)
        {
            Raster(fill, stroke, point => Mathf.Pow((point.x-x)/rx, 2) + Mathf.Pow((point.y-y)/ry, 2) <= 1f,
                point => Mathf.Abs(Mathf.Sqrt(Mathf.Pow((point.x-x)/rx, 2) + Mathf.Pow((point.y-y)/ry, 2))-1f)*Mathf.Min(rx,ry));
        }
        public void Line(Color color, float width, float x1, float y1, float x2, float y2)
        {
            var a = new Vector2(x1,y1); var b = new Vector2(x2,y2);
            for (int y=0; y<Resolution; y++) for (int x=0; x<Resolution; x++)
                if (Distance(new Vector2((x+.5f)/Samples,(y+.5f)/Samples),a,b) <= width*.5f) pixels[y*Resolution+x] = color;
        }
        private void Raster(Color fill, float stroke, Func<Vector2,bool> inside, Func<Vector2,float> edge)
        {
            for (int y=0; y<Resolution; y++) for (int x=0; x<Resolution; x++)
            {
                var point = new Vector2((x+.5f)/Samples,(y+.5f)/Samples);
                bool body = inside(point);
                if (stroke>0f && edge(point)<=stroke*.5f) pixels[y*Resolution+x]=Ink;
                else if (body) pixels[y*Resolution+x]=fill;
            }
        }
        public Texture2D Texture()
        {
            var result = new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            var low = new Color[Size*Size];
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                Color sum=Color.clear;
                for(int sy=0;sy<Samples;sy++) for(int sx=0;sx<Samples;sx++) sum+=pixels[(y*Samples+sy)*Resolution+x*Samples+sx];
                float alpha=sum.a/(Samples*Samples);
                low[(Size-1-y)*Size+x] = alpha>0 ? new Color(sum.r/sum.a,sum.g/sum.a,sum.b/sum.a,alpha) : Color.clear;
            }
            result.SetPixels(low); result.Apply(); return result;
        }
        private static bool Contains(Vector2 point, Vector2[] points)
        {
            bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
            {
                var a=points[i]; var b=points[j];
                if((a.y>point.y)!=(b.y>point.y) && point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
            }
            return inside;
        }
        private static float EdgeDistance(Vector2 point, Vector2[] points)
        {
            float min=float.MaxValue;
            for(int i=0;i<points.Length;i++) min=Mathf.Min(min,Distance(point,points[i],points[(i+1)%points.Length]));
            return min;
        }
        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var delta=b-a; float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.0001f,delta.sqrMagnitude));
            return Vector2.Distance(p,a+t*delta);
        }
    }

    private static void ExportPreview()
    {
        const int cell = 160;
        var sheet = new Texture2D(cell * Ids.Length, 208, TextureFormat.RGBA32, false);
        var pixels = Enumerable.Repeat(C(0x202934), sheet.width * sheet.height).ToArray();
        sheet.SetPixels(pixels);
        for (int i = 0; i < Ids.Length; i++)
        {
            var icon = Draw(Ids[i]);
            try
            {
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    var c = icon.GetPixel(x,y); sheet.SetPixel(i*cell+16+x,64+y,Color.Lerp(C(0x202934),c,c.a));
                }
                for (int y=0;y<32;y++) for (int x=0;x<32;x++)
                {
                    var c=icon.GetPixelBilinear((x+.5f)/32,(y+.5f)/32); sheet.SetPixel(i*cell+64+x,16+y,Color.Lerp(C(0x202934),c,c.a));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(icon); }
        }
        sheet.Apply(); File.WriteAllBytes(Output + "/ItemIcons.png", sheet.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(sheet);
        File.WriteAllText(Output + "/README.txt", "Left to right: " + string.Join(", ", Ids) + "\nTop: 128px. Bottom: 32px UI readability preview. Transparent PNG assets are in " + IconFolder + ".");
    }
}
#endif
