using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Authored silhouettes and socket compositions; no runtime material or mesh creation.
public static class DungeonGardenBuilder
{
    const string Root="Assets/Resources/EContent/";
    static T Save<T>(T value,string name) where T:Object
    {
        var path=Root+name+".asset";var old=AssetDatabase.LoadAssetAtPath<T>(path);
        if(old==null){AssetDatabase.CreateAsset(value,path);return value;}
        EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);return old;
    }
    static Material LeafMaterial(string name,Color color)
    {
        string path=Root+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Cull",0);m.SetFloat("_Smoothness",.15f);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    static GameObject Fern(string name,float radius,float height,Material[] materials)
    {
        var go=new GameObject(name);var vertices=new List<Vector3>();var triangles=new[]{new List<int>(),new List<int>()};
        for(int frond=0;frond<13;frond++)
        {
            float angle=frond*2.39996f;var forward=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));var side=Vector3.Cross(Vector3.up,forward);
            float length=radius*(.72f+.28f*Mathf.Sin(frond*5.1f));
            for(int j=1;j<=8;j++)
            {
                float t=j/9f;Vector3 center=forward*length*t+Vector3.up*(height*Mathf.Sin(t*Mathf.PI*.8f));
                float spread=(1-t)*.6f*radius;
                float previous=(j-1)/9f;var stemBase=forward*length*previous+Vector3.up*(height*Mathf.Sin(previous*Mathf.PI*.8f));int stemIndex=vertices.Count;
                vertices.Add(stemBase-side*.015f);vertices.Add(stemBase+side*.015f);vertices.Add(center+side*.015f);vertices.Add(center-side*.015f);triangles[0].AddRange(new[]{stemIndex,stemIndex+1,stemIndex+2,stemIndex,stemIndex+2,stemIndex+3});
                for(int sign=-1;sign<=1;sign+=2)
                {
                    var tip=center+side*spread*sign+forward*length*.14f+Vector3.up*.07f;
                    var cross=forward*length*.1f;int index=vertices.Count;
                    vertices.Add(center);vertices.Add((center+tip)*.5f+cross);vertices.Add(tip);vertices.Add((center+tip)*.5f-cross);
                    triangles[(frond+j)%2].AddRange(new[]{index,index+1,index+2,index,index+2,index+3});
                }
            }
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.subMeshCount=2;for(int i=0;i<2;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh=Save(mesh,name+"Mesh");go.AddComponent<MeshRenderer>().sharedMaterials=materials;
        var prefab=PrefabUtility.SaveAsPrefabAsset(go,Root+name+".prefab");Object.DestroyImmediate(go);return prefab;
    }
    static void Place(GameObject asset,Transform root,Vector3 position,Vector3 scale,Quaternion rotation)
    {
        var go=Object.Instantiate(asset,root);go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=rotation;
    }
    public static void Build(DungeonRoomSets catalog)
    {
        var materials=new[]{LeafMaterial("FernShadow",new Color(.1f,.24f,.1f)),LeafMaterial("FernTips",new Color(.27f,.43f,.12f))};
        catalog.largeBush=Fern("LargeFern",1.25f,.85f,materials);catalog.smallBush=Fern("SmallFern",.62f,.4f,materials);
        catalog.hangingBush=Fern("HangingFern",1.05f,1.45f,materials);
        foreach(var set in catalog.sets)
        {
            var module=new GameObject(set.label+"Composition");
            Place(set.feature,module.transform,Vector3.zero,Vector3.one,Quaternion.identity);
            for(int side=-1;side<=1;side+=2)
                Place(set.banner,module.transform,new Vector3(side*2.2f,.2f,-.25f),Vector3.one*.8f,Quaternion.identity);
            for(int side=-1;side<=1;side+=2){var pillar=EContentBuilder.Model("pillar_decorated",new Vector3(.5f,3,.4f));pillar.name="Carved frame";pillar.transform.SetParent(module.transform,false);pillar.transform.localPosition=new Vector3(side*3.05f,-.3f,-.3f);}
            var prefab=PrefabUtility.SaveAsPrefabAsset(module,Root+set.label+"Composition.prefab");Object.DestroyImmediate(module);set.feature=prefab;
        }
        EditorUtility.SetDirty(catalog);
    }
}
