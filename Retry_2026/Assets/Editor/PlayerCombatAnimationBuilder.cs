using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerCombatAnimationBuilder
{
    const string Folder="Assets/PlayerDefense/Animations/Combat";
    const string Controller="Assets/GameObjects/Player/Player_Animation/Player_AnimCtrler.controller";
    public static void Build()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        var all=States(controller.layers[0].stateMachine).ToList();
        var locomotion=all.First(s=>s.name=="Blend Tree");
        string[] names={"Sword_Regular_A","Sword_Regular_B","Sword_Attack","Sword_Regular_C"};
        float[] durations={.75f,.8f,.9f,1f};
        for(int i=0;i<4;i++)
        {
            var clip=Copy(names[i],"Sword"+(i+1),i==2?"Quaternius":"Quaternius2");
            AnimationUtility.SetAnimationEvents(clip,new[]{Event(clip,.20f,"AnimEvent_BeginSwing"),Event(clip,.53f,"AnimEvent_EndSwing"),Event(clip,.3f,"AnimEvent_ComboWindowOpen"),Event(clip,.78f,"AnimEvent_ComboCommit"),Event(clip,.9f,"AnimEvent_ComboWindowClose")});
            var state=all.First(s=>s.name=="sword_attack_"+(i+1));
            Configure(state,clip,durations[i],"Attack",locomotion);
        }
        var hit=all.First(s=>s.name=="Hit"); Configure(hit,Copy("Hit_Chest","PlayerHit","Quaternius"),.38f,"Hit",locomotion);
        var death=all.FirstOrDefault(s=>s.name=="Die") ?? controller.layers[0].stateMachine.AddState("Die");
        Configure(death,Copy("Death01","PlayerDeath","Quaternius"),1.6f,"Death",null);
        AssetDatabase.SaveAssets();
        SwordGroundingBuilder.Build();
        BuildMonsterHit();
    }
    public static void BuildMonsterHit()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/GameObjects/Monster/Monster1.controller");
        var states=States(controller.layers[0].stateMachine).ToList();
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/PlayerHit.anim");
        if(clip==null || !clip.humanMotion) throw new Exception("Missing humanoid hit clip");
        Configure(states.First(s=>s.name=="Hit"),clip,.38f,"Hit",states.First(s=>s.name=="Locomotion"));
        DuelCombatContentBuilder.BuildSwordImpact();
        AssetDatabase.SaveAssets();
    }
    static AnimationEvent Event(AnimationClip clip,float t,string function)=>new AnimationEvent{time=clip.length*t,functionName=function};
    static AnimationClip Copy(string source,string name,string model)
    {
        var original=AssetDatabase.LoadAllAssetsAtPath("Assets/PlayerDefense/Animations/"+model+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__") && c.name.EndsWith("|"+source));
        if(!original.humanMotion) throw new Exception("Non humanoid source: "+source);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/"+name+".anim");
        if(clip==null) {clip=UnityEngine.Object.Instantiate(original); AssetDatabase.CreateAsset(clip,Folder+"/"+name+".anim");} else EditorUtility.CopySerialized(original,clip);
        clip.name=name; AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        var settings=AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime=false;
        settings.keepOriginalOrientation=true; settings.loopBlendOrientation=true; settings.loopBlendPositionXZ=true;
        settings.loopBlendPositionY=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
        NormalizeBodyRoot(clip,name.StartsWith("Sword"));
        EditorUtility.SetDirty(clip); return clip;
    }
    static void NormalizeBodyRoot(AnimationClip clip,bool sword)
    {
        // Imported mannequin faces a different axis and carries translation in its humanoid body curves.
        // Convert to an in-place action facing the gameplay root, retaining pitch/roll and body twist.
        EditorCurveBinding Binding(string property)=>EditorCurveBinding.FloatCurve("",typeof(Animator),property);
        var q=new[]{"x","y","z","w"}.Select(axis=>AnimationUtility.GetEditorCurve(clip,Binding("RootQ."+axis))).ToArray();
        if(q.Any(c=>c==null)) throw new Exception("Missing humanoid root rotation");
        Quaternion Sample(float t)=>new Quaternion(q[0].Evaluate(t),q[1].Evaluate(t),q[2].Evaluate(t),q[3].Evaluate(t)).normalized;
        float yaw=Sample(0).eulerAngles.y;
        var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
        Quaternion previous=Quaternion.identity;
        float previousTurn=0, accumulatedTurn=0;
        int count=Mathf.CeilToInt(clip.length*60);
        for(int i=0;i<=count;i++)
        {
            float t=clip.length*i/count; var value=Quaternion.AngleAxis(-yaw,Vector3.up)*Sample(t);
            if(sword)
            {
                float turn=Mathf.DeltaAngle(0,value.eulerAngles.y);
                accumulatedTurn+=Mathf.DeltaAngle(previousTurn,turn); previousTurn=turn;
                value=Quaternion.AngleAxis(Mathf.Clamp(accumulatedTurn,-45,45)-turn,Vector3.up)*value;
            }
            if(i>0 && Quaternion.Dot(value,previous)<0) value=new Quaternion(-value.x,-value.y,-value.z,-value.w);
            previous=value;
            curves[0].AddKey(t,value.x); curves[1].AddKey(t,value.y); curves[2].AddKey(t,value.z); curves[3].AddKey(t,value.w);
        }
        for(int i=0;i<4;i++) AnimationUtility.SetEditorCurve(clip,Binding("RootQ."+"xyzw"[i]),curves[i]);
        foreach(string axis in new[]{"x","z"}) AnimationUtility.SetEditorCurve(clip,Binding("RootT."+axis),AnimationCurve.Constant(0,clip.length,0));
        clip.EnsureQuaternionContinuity();
    }
    static void Configure(AnimatorState state,AnimationClip clip,float seconds,string tag,AnimatorState next)
    {
        state.motion=clip; state.speed=clip.length/seconds; state.speedParameterActive=false; state.tag=tag;
        foreach(var transition in state.transitions.ToArray()) state.RemoveTransition(transition);
        if(next!=null)
        {
            var transition=state.AddTransition(next); transition.hasExitTime=true; transition.exitTime=.95f;
            transition.hasFixedDuration=true; transition.duration=.12f;
        }
        EditorUtility.SetDirty(state);
    }
    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach(var state in machine.states) yield return state.state;
        foreach(var child in machine.stateMachines) foreach(var state in States(child.stateMachine)) yield return state;
    }
}
