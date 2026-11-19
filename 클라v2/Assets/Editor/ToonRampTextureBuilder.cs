using UnityEditor;
using UnityEngine;

public static class ToonRampTextureBuilder
{
    [MenuItem("Tools/Retry/Toon/Apply Ramp Import Settings To Selected")]
    public static void ApplyRampSettingsToSelected()
    {
        foreach (Object selected in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            ApplyRampImportSettings(path, FilterMode.Point);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void ApplyRampImportSettings(string path, FilterMode filterMode)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = filterMode;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
    }
}
