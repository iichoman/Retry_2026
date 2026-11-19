using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class CContentBuilder {
    [MenuItem("Retry/Content/Build lobby and camera options")]
    public static void Build(){
        Directory.CreateDirectory("Assets/Resources/Presentation");AssetDatabase.Refresh();
        const string path="Assets/Resources/Presentation/CameraOptions.asset";
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
        if(!profile.TryGet<MotionBlur>(out var blur)){blur=profile.Add<MotionBlur>(true);AssetDatabase.AddObjectToAsset(blur,profile);}
        blur.mode.Override(MotionBlurMode.CameraOnly);blur.quality.Override(MotionBlurQuality.Low);blur.intensity.Override(0);blur.clamp.Override(.035f);
        EditorUtility.SetDirty(blur);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
        if(Marshal.SizeOf<EconomyCommand>()!=24||Marshal.SizeOf<EconomyEntry>()!=16||Marshal.SizeOf<EconomySnapshot>()!=1572||Marshal.OffsetOf<EconomySnapshot>("credits").ToInt32()!=12||Marshal.OffsetOf<EconomySnapshot>("entries").ToInt32()!=36||Marshal.SizeOf<StormState>()!=48||Marshal.OffsetOf<StormState>("time").ToInt32()!=24)throw new Exception("C/storm wire layout mismatch");
        Directory.CreateDirectory("Artifacts/C");File.WriteAllText("Artifacts/C/content.txt","PASS camera-only URP volume asset; C commands 210/24, 211/1572 v2, storm 205/48 offsets verified\n");
    }
}
