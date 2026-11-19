using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Read-only imported-asset dependency snapshot. No assets or scenes are saved.
public static class AssetCleanupAudit
{
    [Serializable] public class Entry { public string path; public string[] dependencies; }
    [Serializable] public class Snapshot { public Entry[] assets; }

    public static void ValidateAndBuild()
    {
        // Force a fresh importer pass so cached materials/textures cannot hide removed dependencies.
        var models = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
            && new[] { ".fbx", ".dae", ".obj" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
        foreach (var path in models)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Run();
        DistributionBuild.Batch();
    }

    public static void Run()
    {
        var output = Environment.GetEnvironmentVariable("RETRY_ASSET_AUDIT_OUTPUT");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("RETRY_ASSET_AUDIT_OUTPUT is required");
        var entries = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new Entry {
                path = p,
                dependencies = AssetDatabase.GetDependencies(p, false)
                    .Where(d => d != p).OrderBy(d => d, StringComparer.Ordinal).ToArray()
            }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(new Snapshot { assets = entries }, true));
        Debug.Log("Asset cleanup audit: " + entries.Length + " imported assets, snapshot " + output);
    }
}
