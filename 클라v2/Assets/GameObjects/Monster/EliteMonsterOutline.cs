using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Separate hulls follow the original bones without replacing hit-flash or elemental materials.
[DisallowMultipleComponent]
public sealed class EliteMonsterOutline : MonoBehaviour
{
    private readonly List<(Renderer source, Renderer outline)> renderers = new();
    private Material material;
    private Material maskMaterial;

    private void OnEnable()
    {
        if (material != null) return;
        var shader = Resources.Load<Shader>("EliteOutline");
        var maskShader = Resources.Load<Shader>("EliteOutlineMask");
        if (shader == null || maskShader == null) return;
        material = new Material(shader) { name = "Elite Yellow Outline" };
        maskMaterial = new Material(maskShader) { name = "Elite Silhouette Mask" };
        foreach (var source in GetComponentsInChildren<Renderer>(true))
        {
            if (source is not (SkinnedMeshRenderer or MeshRenderer)) continue;
            var filter=source.GetComponent<MeshFilter>();
            Mesh mesh = source is SkinnedMeshRenderer skin ? skin.sharedMesh : filter!=null?filter.sharedMesh:null;
            if (mesh == null) continue;
            AddSurface(source, mesh, maskMaterial, "Elite Outline Mask");
            AddSurface(source, mesh, material, "Elite Outline Hull");
        }
        LateUpdate();
    }
    private void AddSurface(Renderer source, Mesh mesh, Material surface, string name)
    {
            var hull = new GameObject(name);
            hull.layer = source.gameObject.layer;
            hull.transform.SetParent(source.transform, false);
            Renderer outline;
            if (source is SkinnedMeshRenderer skinned)
            {
                var copy = hull.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = mesh; copy.bones = skinned.bones; copy.rootBone = skinned.rootBone;
                copy.localBounds = skinned.localBounds; copy.updateWhenOffscreen = true;
                outline = copy;
            }
            else
            {
                hull.AddComponent<MeshFilter>().sharedMesh = mesh;
                outline = hull.AddComponent<MeshRenderer>();
            }
            var materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = surface;
            outline.sharedMaterials = materials;
            outline.shadowCastingMode = ShadowCastingMode.Off;
            outline.receiveShadows = false;
            outline.lightProbeUsage = LightProbeUsage.Off;
            outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderers.Add((source, outline));
    }

    private void LateUpdate()
    {
        foreach (var pair in renderers)
            if (pair.outline != null) pair.outline.enabled = enabled && pair.source != null && pair.source.enabled;
    }
    private void OnDisable() { foreach (var pair in renderers) if (pair.outline != null) pair.outline.enabled = false; }
    private void OnDestroy()
    {
        foreach (var pair in renderers) if (pair.outline != null) Release(pair.outline.gameObject);
        if (material != null) Release(material);
        if (maskMaterial != null) Release(maskMaterial);
    }
    private static void Release(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
