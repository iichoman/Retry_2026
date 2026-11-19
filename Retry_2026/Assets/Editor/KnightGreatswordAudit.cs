using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class KnightGreatswordAudit
{
    public static void Run()
    {
        var log = new StringBuilder();
        string[] paths = {
            "Assets/GameObjects/Monster/knight/knight.fbx",
            "Assets/GameObjects/Player/Player_Model/chA/greatSword.fbx",
            "Assets/GameObjects/Player/Player_Model/chA/greatSwordAni.fbx",
            "Assets/Prefabs/LocalPlayerPrefab.prefab", "Assets/Prefabs/RemotePlayerPrefab.prefab",
            "Assets/Prefabs/Monster1.prefab" };
        foreach (var path in paths)
        {
            log.AppendLine(path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null) log.AppendLine($"Import {importer.animationType}, avatar={importer.sourceAvatar}, scale={importer.globalScale}");
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")))
                log.AppendLine($"Clip {clip.name} length={clip.length} fps={clip.frameRate} human={clip.humanMotion}");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var go = UnityEngine.Object.Instantiate(asset);
            foreach (var animator in go.GetComponentsInChildren<Animator>(true))
                log.AppendLine($"Animator {animator.name} avatar={animator.avatar} human={animator.isHuman} controller={animator.runtimeAnimatorController}");
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if(path.EndsWith(".fbx") || t.GetComponents<MonoBehaviour>().Length>0 || t.name.Contains("Hand"))
                    log.AppendLine($"  {AnimationUtility.CalculateTransformPath(t,go.transform)} pos={t.localPosition:F3} rot={t.localEulerAngles:F1} scale={t.localScale:F4} components={string.Join(",",t.GetComponents<Component>().Select(c=>c==null?"MISSING":c.GetType().Name))}");
            }
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                log.AppendLine($"Renderer {renderer.name} bounds={renderer.bounds} mats={string.Join(",",renderer.sharedMaterials.Select(m=>m==null?"NULL":m.name+":"+m.shader.name))}");
            foreach(var mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
                log.AppendLine($"Mesh {mesh.name} bounds={mesh.bounds}");
            UnityEngine.Object.DestroyImmediate(go);
        }
        Directory.CreateDirectory("Artifacts/KnightGreatsword");
        File.WriteAllText("Artifacts/KnightGreatsword/audit.txt",log.ToString());
        Debug.Log("KnightGreatsword audit complete");
    }
}
