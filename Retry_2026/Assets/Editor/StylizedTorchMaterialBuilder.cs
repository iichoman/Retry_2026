using System.IO;
using UnityEditor;
using UnityEngine;

public static class StylizedTorchMaterialBuilder
{
    private const string MaterialFolder = "Assets/GameObjects/Map/Materials";
    private const string FlameMaterialPath = MaterialFolder + "/Torch_Flame.mat";
    private const string SmokeMaterialPath = MaterialFolder + "/Torch_Smoke.mat";
    private const string FlameTexturePath = MaterialFolder + "/Torch_Flame_Alpha.png";
    private const string SmokeTexturePath = MaterialFolder + "/Torch_Smoke_Alpha.png";

    [MenuItem("Tools/Retry/Create Stylized Torch Materials")]
    public static void CreateMaterials()
    {
        EnsureMaterialFolder();

        Texture2D flameTexture = CreateOrUpdateTexture(FlameTexturePath, CreateFlameTexture(128, 128));
        Texture2D smokeTexture = CreateOrUpdateTexture(SmokeTexturePath, CreateSmokeTexture(128, 128));

        Material flame = CreateOrUpdateMaterial(
            FlameMaterialPath,
            "Torch_Flame",
            new Color(1f, 0.42f, 0.06f, 0.82f),
            true,
            flameTexture
        );

        Material smoke = CreateOrUpdateMaterial(
            SmokeMaterialPath,
            "Torch_Smoke",
            new Color(0.18f, 0.16f, 0.14f, 0.28f),
            true,
            smokeTexture
        );

        AssignToSelectedTorch(flame, smoke);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created stylized torch materials: {FlameMaterialPath}, {SmokeMaterialPath}");
    }

    [MenuItem("Tools/Retry/Assign Stylized Torch Materials To Selected")]
    public static void AssignMaterialsToSelected()
    {
        Material flame = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath);
        Material smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);

        if (flame == null || smoke == null)
        {
            Debug.LogWarning("Torch materials are missing. Run Tools/Retry/Create Stylized Torch Materials first.");
            return;
        }

        AssignToSelectedTorch(flame, smoke);
    }

    private static void AssignToSelectedTorch(Material flame, Material smoke)
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            return;
        }

        StylizedTorchFlame effect = selected.GetComponent<StylizedTorchFlame>();
        if (effect == null)
        {
            Debug.LogWarning("Selected object has no StylizedTorchFlame component.", selected);
            return;
        }

        Undo.RecordObject(effect, "Assign Stylized Torch Materials");
        effect.SetMaterials(flame, smoke);
        EditorUtility.SetDirty(effect);
        Debug.Log("Assigned stylized torch materials to selected torch.", selected);
    }

    private static void EnsureMaterialFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialFolder))
        {
            return;
        }

        string[] parts = MaterialFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static Material CreateOrUpdateMaterial(string path, string name, Color color, bool transparent, Texture2D texture)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(FindParticleShader())
            {
                name = name
            };

            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = FindParticleShader();
        ApplyCommonSettings(material, color, transparent, texture);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D CreateOrUpdateTexture(string path, Texture2D generatedTexture)
    {
        byte[] png = generatedTexture.EncodeToPNG();
        File.WriteAllBytes(path, png);
        Object.DestroyImmediate(generatedTexture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Shader FindParticleShader()
    {
        return
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color");
    }

    private static void ApplyCommonSettings(Material material, Color color, bool transparent, Texture2D texture)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (texture != null && material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        if (texture != null && material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", transparent ? 1f : 0f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
        }

        material.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
        material.enableInstancing = true;

        if (transparent)
        {
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }

    private static Texture2D CreateFlameTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float centeredX = Mathf.Abs(u - 0.5f) * 2f;
                float widthAtY = Mathf.Lerp(0.08f, 0.78f, Mathf.Sin(v * Mathf.PI));
                widthAtY *= Mathf.Lerp(1.15f, 0.45f, v);

                float body = Mathf.Clamp01(1f - centeredX / Mathf.Max(0.001f, widthAtY));
                float bottomFade = Mathf.SmoothStep(0f, 0.16f, v);
                float topFade = 1f - Mathf.SmoothStep(0.78f, 1f, v);
                float tip = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 5f) * Mathf.SmoothStep(0.55f, 1f, v);
                float alpha = Mathf.Clamp01(Mathf.Max(body, tip * 0.85f) * bottomFade * topFade);
                alpha = Mathf.Pow(alpha, 1.4f);

                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                byte r = 255;
                byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(80f, 245f, v));
                byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(8f, 34f, v));
                texture.SetPixel(x, y, new Color32(r, g, b, a));
            }
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D CreateSmokeTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1);
                float dx = (u - 0.5f) * 2f;
                float dy = (v - 0.5f) * 2f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha *= 0.38f;
                alpha *= Mathf.SmoothStep(0f, 0.2f, v) * (1f - Mathf.SmoothStep(0.85f, 1f, v));

                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                byte c = (byte)Mathf.RoundToInt(Mathf.Lerp(70f, 98f, v));
                texture.SetPixel(x, y, new Color32(c, (byte)(c - 5), (byte)(c - 12), a));
            }
        }

        texture.Apply();
        return texture;
    }
}
