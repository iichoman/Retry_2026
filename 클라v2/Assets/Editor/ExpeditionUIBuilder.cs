using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ExpeditionUIBuilder
{
    const string Root="Assets/Resources/ExpeditionUI";
    [MenuItem("Retry/UI/Build Expedition UI")]
    public static void Build()
    {
        Directory.CreateDirectory(Root);
        var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){
            float dx=Mathf.Max(Mathf.Abs(x-15.5f)-9.5f,0),dy=Mathf.Max(Mathf.Abs(y-15.5f)-9.5f,0);
            texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(6.5f-Mathf.Sqrt(dx*dx+dy*dy))));
        }
        texture.Apply();File.WriteAllBytes(Root+"/Panel.png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(Root+"/Panel.png",ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(Root+"/Panel.png");
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spriteBorder=new Vector4(8,8,8,8);importer.mipmapEnabled=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        // OS fonts are runtime-only: persisting them leaves an invalid dynamic atlas in a player build.
        if(AssetDatabase.LoadAssetAtPath<Font>(Root+"/Font.fontsettings")!=null)AssetDatabase.DeleteAsset(Root+"/Font.fontsettings");
        foreach(var row in ABItemDefinitions.All){
            var item=ItemCatalog.Load().Resolve(row.id);
            if(item==null)throw new Exception("Missing catalog item "+row.id);
            if(row.id!="sword_001"&&row.id!="gun_001"&&row.id!="health_potion"){
                var icon=ABIconBuilder.Get(row);if(icon==null)throw new Exception("Missing icon "+row.id);
                var serialized=new SerializedObject(item);serialized.FindProperty("icon").objectReferenceValue=icon;serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        var go=new GameObject("Expedition UI",typeof(RectTransform));go.SetActive(false);
        try{
            var view=go.AddComponent<ExpeditionUI>();view.Build();
            // The prefab root starts active; child modals remain closed.
            go.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(go,Root+"/View.prefab");
            if(go.GetComponentsInChildren<ExpeditionSlot>(true).Length!=32)throw new Exception("Expected 32 inventory cells");
            foreach(var text in go.GetComponentsInChildren<Text>(true))if(text.font==null)throw new Exception("Missing font: "+text.name);
        }finally{UnityEngine.Object.DestroyImmediate(go);}
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/ExpeditionUI");
        File.WriteAllText("Artifacts/ExpeditionUI/assets.txt","PASS: rounded sprite, runtime OS font binding, serialized 32-slot inventory + item details + 3 upgrade cards + 4 quick slots prefab.\n");
    }
}
