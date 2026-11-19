using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Correct the existing four clips on the actual avatar; gameplay root motion stays disabled.
public static class SwordGroundingBuilder
{
    const string Folder="Assets/PlayerDefense/Animations/Combat", Output="Artifacts/SwordGrounding";
    static EditorCurveBinding Binding(string name)=>EditorCurveBinding.FloatCurve("",typeof(Animator),name);
    static AnimationCurve Curve(AnimationClip c,string name)=>AnimationUtility.GetEditorCurve(c,Binding(name));
    static void Write(AnimationClip c,string name,AnimationCurve curve)=>AnimationUtility.SetEditorCurve(c,Binding(name),curve);
    [MenuItem("Retry/Player/Ground Existing Sword Animations")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode");
        Directory.CreateDirectory(Output); Directory.CreateDirectory(Output+"/Before");
        var scene=EditorSceneManager.NewPreviewScene();
        var report=new List<string>();
        try
        {
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player,scene);
            foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled=false;
            foreach(var ps in player.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach(var gun in player.GetComponentsInChildren<Weapon_Gun>(true)) gun.gameObject.SetActive(false);
            var animator=player.GetComponent<Animator>(); animator.applyRootMotion=false; animator.fireEvents=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Play("Blend Tree",0,0); animator.Update(.05f);
            float contact=LowestFoot(animator); float scale=animator.humanScale;
            report.Add($"Avatar humanScale={scale:F4} locomotion support height={contact:F4}");
            animator.runtimeAnimatorController=null;
            string[] sources={"Sword_Regular_A","Sword_Regular_B","Sword_Attack","Sword_Regular_C"};
            for(int index=0;index<4;index++)
            {
                string path=Folder+"/Sword"+(index+1)+".anim";
                if(!File.Exists(Output+"/Before/Sword"+(index+1)+".anim")) File.Copy(path,Output+"/Before/Sword"+(index+1)+".anim");
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                var events=AnimationUtility.GetAnimationEvents(clip); float length=clip.length;
                var original=AssetDatabase.LoadAllAssetsAtPath("Assets/PlayerDefense/Animations/"+(index==2?"Quaternius":"Quaternius2")+".fbx")
                    .OfType<AnimationClip>().First(c=>c.name.EndsWith("|"+sources[index]));
                // Restore authored body/leg relationships instead of suppressing leg movement.
                foreach(var binding in AnimationUtility.GetCurveBindings(original))
                    AnimationUtility.SetEditorCurve(clip,binding,AnimationUtility.GetEditorCurve(original,binding));
                if(index==3)
                {
                    const string donorPath="Assets/GameObjects/Player/Player_Model/chA/Sword2.fbx";
                    var donor=AssetDatabase.LoadAllAssetsAtPath(donorPath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
                    CopyLowerBody(clip,donor);
                    report.Add("Sword4 lower body/pelvis: "+donorPath+"; upper-body muscles retained from Sword_Regular_C.");
                }
                AlignInitialHeading(clip,index==1 || index==3);
                AnimationUtility.SetAnimationEvents(clip,events);
                clip.EnsureQuaternionContinuity();
                EditorUtility.SetDirty(clip);
                var after=Measure(animator,clip,121);
                var raised=Measure(animator,clip,121,true);
                report.Add($"Sword{index+1}: support [{after.Min():F3},{after.Max():F3}], higher foot max={raised.Max():F3}; duration={clip.length:F3}, events={events.Length}");
                if(Mathf.Abs(clip.length-length)>.001f) throw new Exception("Duration changed");
            }
            AssetDatabase.SaveAssets();
            report.Add("Authored motion restored; fourth attack uses legacy Sword2 lower body. Height metrics are bone references, not proof of sole contact.");
        }
        catch(Exception ex) {report.Add("FAIL "+ex); throw;}
        finally {EditorSceneManager.ClosePreviewScene(scene); File.WriteAllLines(Output+"/bake.txt",report);}
    }
    static void CopyLowerBody(AnimationClip target,AnimationClip donor)
    {
        float timeScale=target.length/donor.length;
        foreach(var binding in AnimationUtility.GetCurveBindings(donor))
        {
            string name=binding.propertyName;
            if(!(name.StartsWith("RootT.") || name.StartsWith("RootQ.") || name.Contains(" Leg ") || name.Contains(" Foot ") || name.Contains(" Toes "))) continue;
            var keys=AnimationUtility.GetEditorCurve(donor,binding).keys;
            for(int i=0;i<keys.Length;i++)
            {
                keys[i].time*=timeScale;
                keys[i].inTangent/=timeScale; keys[i].outTangent/=timeScale;
            }
            AnimationUtility.SetEditorCurve(target,binding,new AnimationCurve(keys));
        }
    }
    static void AlignInitialHeading(AnimationClip clip,bool faceForward)
    {
        var q="xyzw".Select(a=>Curve(clip,"RootQ."+a)).ToArray();
        Quaternion At(float t)=>new Quaternion(q[0].Evaluate(t),q[1].Evaluate(t),q[2].Evaluate(t),q[3].Evaluate(t)).normalized;
        var align=Quaternion.AngleAxis(-At(0).eulerAngles.y,Vector3.up);
        var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
        Quaternion previous=Quaternion.identity;
        int samples=Mathf.CeilToInt(clip.length*90);
        for(int i=0;i<=samples;i++)
        {
            float t=clip.length*i/samples; var rotation=align*At(t);
            // Keep the actor-facing direction for attacks 2/4; preserve authored tilt and legs.
            if(faceForward) rotation=Quaternion.AngleAxis(-rotation.eulerAngles.y,Vector3.up)*rotation;
            if(i>0 && Quaternion.Dot(rotation,previous)<0) rotation=new Quaternion(-rotation.x,-rotation.y,-rotation.z,-rotation.w);
            previous=rotation;
            for(int axis=0;axis<4;axis++) curves[axis].AddKey(t,rotation[axis]);
        }
        for(int axis=0;axis<4;axis++) Write(clip,"RootQ."+"xyzw"[axis],curves[axis]);
        // Match the same constant heading correction on the visual body translation.
        var x=Curve(clip,"RootT.x"); var z=Curve(clip,"RootT.z");
        var px=new AnimationCurve(); var pz=new AnimationCurve();
        for(int i=0;i<=samples;i++)
        {
            float t=clip.length*i/samples;
            var position=align*new Vector3(x.Evaluate(t),0,z.Evaluate(t));
            px.AddKey(t,position.x); pz.AddKey(t,position.z);
        }
        Write(clip,"RootT.x",px); Write(clip,"RootT.z",pz);
    }
    public static float LowestFoot(Animator a)
    {
        float y=float.MaxValue;
        foreach(var bone in new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes})
        {var t=a.GetBoneTransform(bone); if(t!=null) y=Mathf.Min(y,t.position.y);}
        return y-a.transform.position.y;
    }
    public static float HigherFootContact(Animator a)
    {
        float Contact(HumanBodyBones foot,HumanBodyBones toes)
        {
            var f=a.GetBoneTransform(foot); var t=a.GetBoneTransform(toes);
            return Mathf.Min(f.position.y,t!=null?t.position.y:f.position.y)-a.transform.position.y;
        }
        return Mathf.Max(Contact(HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes),Contact(HumanBodyBones.RightFoot,HumanBodyBones.RightToes));
    }
    public static float[] Measure(Animator animator,AnimationClip clip,int count,bool higherFoot=false)
    {
        var graph=PlayableGraph.Create("Sword support measurement"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        try
        {
            var playable=AnimationClipPlayable.Create(graph,clip); playable.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph,"Sword",animator).SetSourcePlayable(playable); graph.Play();
            var feet=new float[count];
            for(int i=0;i<count;i++)
            {
                playable.SetTime(clip.length*i/(double)(count-1)); graph.Evaluate(0);
                feet[i]=higherFoot?HigherFootContact(animator):LowestFoot(animator);
                if(animator.transform.position.sqrMagnitude>.00001f || Quaternion.Angle(animator.transform.rotation,Quaternion.identity)>.01f)
                    throw new Exception("Clip moved gameplay root");
            }
            return feet;
        }
        finally { graph.Destroy(); }
    }
}
