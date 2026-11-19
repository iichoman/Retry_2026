using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
public static class DefenseClipBuilder
{
    public static void TuneRoll()
    {
        const string scenePath="Assets/Scenes/SampleScene.unity";
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
        if(scene.IsValid() && scene.isLoaded && scene.isDirty) throw new System.InvalidOperationException("Save SampleScene before applying roll tuning.");
        const string prefabPath="Assets/Prefabs/LocalPlayerPrefab.prefab";
        var root=PrefabUtility.LoadPrefabContents(prefabPath);
        try {Tune(root); PrefabUtility.SaveAsPrefabAsset(root,prefabPath);}
        finally {PrefabUtility.UnloadPrefabContents(root);}
        bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened) scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
        foreach(var go in scene.GetRootGameObjects()) Tune(go);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        if(opened) EditorSceneManager.CloseScene(scene,true);
        AssetDatabase.SaveAssets();
    }
    static void Tune(GameObject root)
    {
        foreach(var defense in root.GetComponentsInChildren<PlayerDefenseController>(true))
        {
            var serialized=new SerializedObject(defense);
            serialized.FindProperty("dashDuration").floatValue=PlayerDefenseController.DefaultDashDuration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    public static void Build()
    {
        Inspect();
        var roll=Extract("Quaternius","Roll");
        var parry=Extract("Quaternius2","Sword_Block");
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/GameObjects/Player/Player_Animation/Player_AnimCtrler.controller");
        SetState(controller,PlayerDefenseAnimation.RollState,roll);
        var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/PlayerDefense/Animations/ParryUpperBody.mask");
        if(mask==null) {mask=new AvatarMask(); AssetDatabase.CreateAsset(mask,"Assets/PlayerDefense/Animations/ParryUpperBody.mask");}
        for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
        foreach(var part in new[]{AvatarMaskBodyPart.Body,AvatarMaskBodyPart.Head,AvatarMaskBodyPart.LeftArm,AvatarMaskBodyPart.RightArm,AvatarMaskBodyPart.LeftFingers,AvatarMaskBodyPart.RightFingers}) mask.SetHumanoidBodyPartActive(part,true);
        int layer=System.Array.FindIndex(controller.layers,l=>l.name=="Defense Upper Body");
        if(layer<0) {controller.AddLayer("Defense Upper Body"); layer=controller.layers.Length-1;}
        var layers=controller.layers; layers[layer].avatarMask=mask; layers[layer].defaultWeight=0; controller.layers=layers;
        SetState(controller,PlayerDefenseAnimation.ParryState,parry,layer);
        var obsolete=controller.layers[0].stateMachine.states.FirstOrDefault(s=>s.state.name==PlayerDefenseAnimation.ParryState).state;
        if(obsolete!=null) controller.layers[0].stateMachine.RemoveState(obsolete);
        EditorUtility.SetDirty(mask);
        const string prefabPath="Assets/Prefabs/LocalPlayerPrefab.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        Connect(prefab); PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath); PrefabUtility.UnloadPrefabContents(prefab);
        foreach(var path in new[]{"Assets/Scenes/SampleScene.unity","Assets/Scenes/DefensePractice.unity"})
        {
            var scene=EditorSceneManager.OpenScene(path);
            foreach(var root in scene.GetRootGameObjects()) Connect(root);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Artifacts/DefenseClips/content.txt","PASS: Humanoid Roll and Sword_Block extracted; Animator states and player prefab/scenes connected. Root motion off; no animation damage events.\n");
        DefenseClipValidation.Start();
    }
    static void Connect(GameObject root)
    {
        foreach(var defense in root.GetComponentsInChildren<PlayerDefenseController>(true))
        {
            if(defense.GetComponent<PlayerDefenseAnimation>()==null) defense.gameObject.AddComponent<PlayerDefenseAnimation>();
            var animator=defense.GetComponent<Animator>();
            if(animator==null || !animator.isHuman) throw new System.InvalidOperationException("Player requires valid Humanoid Animator");
            animator.applyRootMotion=false;
        }
    }
    static AnimationClip Extract(string model,string name)
    {
        var original=AssetDatabase.LoadAllAssetsAtPath("Assets/PlayerDefense/Animations/"+model+".fbx").OfType<AnimationClip>()
            .First(c=>!c.name.StartsWith("__preview__") && c.name.EndsWith("|"+name));
        if(!original.humanMotion) throw new System.InvalidOperationException("Non-humanoid clip");
        string path="Assets/PlayerDefense/Animations/"+name+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null) {clip=Object.Instantiate(original); AssetDatabase.CreateAsset(clip,path);}
        else EditorUtility.CopySerialized(original,clip);
        clip.name=name;
        AnimationUtility.SetAnimationEvents(clip,new AnimationEvent[0]);
        var settings=AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime=false; settings.keepOriginalOrientation=true;
        settings.loopBlendOrientation=true; settings.loopBlendPositionY=true; settings.loopBlendPositionXZ=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings); EditorUtility.SetDirty(clip);
        return clip;
    }
    static void SetState(AnimatorController controller,string name,AnimationClip clip,int layer=0)
    {
        var machine=controller.layers[layer].stateMachine;
        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name) ?? machine.AddState(name);
        state.motion=clip; state.speed=0; state.writeDefaultValues=true;
        EditorUtility.SetDirty(controller);
    }
    public static void Inspect()
    {
        foreach(var path in new[]{"Assets/PlayerDefense/Animations/Quaternius.fbx","Assets/PlayerDefense/Animations/Quaternius2.fbx"}) {
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
        Directory.CreateDirectory("Artifacts/DefenseClips");
        File.WriteAllLines("Artifacts/DefenseClips/"+Path.GetFileNameWithoutExtension(path)+".txt",AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>c.name+" | "+c.length+" | human="+c.humanMotion));
        }
    }
}
