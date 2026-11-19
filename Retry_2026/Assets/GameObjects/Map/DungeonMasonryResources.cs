using System.Collections.Generic;
using UnityEngine;

// Own only generated meshes, never project materials/assets. Also releases resources on regeneration.
[ExecuteAlways]
public sealed class DungeonMasonryResources : MonoBehaviour
{
    [SerializeField, HideInInspector] private List<Mesh> meshes = new();
    [SerializeField, HideInInspector] private List<Material> materials = new();
    public void Own(Mesh mesh) { if(mesh!=null){mesh.hideFlags=HideFlags.DontSave;meshes.Add(mesh);} }
    public Material[] RoughMaterials(Material[] source){var result=new Material[source.Length];for(int i=0;i<source.Length;i++){
        if(source[i]==null)continue;var mat=new Material(source[i]){name=source[i].name+" rough stone",hideFlags=HideFlags.DontSave};
        if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",.16f);
        if(mat.HasProperty("_BumpScale"))mat.SetFloat("_BumpScale",.3f);
        materials.Add(mat);result[i]=mat;
    }return result;}
    private void OnDestroy()
    {
        foreach(var mesh in meshes)if(mesh!=null){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
        meshes.Clear();
        foreach(var mat in materials)if(mat!=null){if(Application.isPlaying)Destroy(mat);else DestroyImmediate(mat);}materials.Clear();
    }
}
