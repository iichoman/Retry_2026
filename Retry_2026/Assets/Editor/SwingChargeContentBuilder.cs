using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class SwingChargeContentBuilder
{
    const string Output="Artifacts/SwingCharge";
    static SwingChargeContentBuilder(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Output+"/build.request"))return;File.Delete(Output+"/build.request");Run(false);}
    public static void Batch()=>Run(true);
    static void Run(bool batch){Directory.CreateDirectory(Output);try{Build();File.WriteAllText(Output+"/result.txt","PASS greatsword timing, elemental materials, mummy charge and retargeted knight hit\n");if(batch)EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Output+"/result.txt",e.ToString());Debug.LogException(e);if(batch)EditorApplication.Exit(1);}}
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Debug.Log("PASS "+text);}
    [MenuItem("Retry/Combat/Apply Swing Charge Update")]
    public static void Build(){
        Directory.CreateDirectory(Output);
        string folder="Assets/Resources/AB/WeaponSkins";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        string[] ids={"flame_sword","frost_sword","flame_greatsword","frost_greatsword"};
        string[] textures={"fire_sword","ice_sword","fire_greatSword","ice_greatSword"};
        for(int i=0;i<4;i++){
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Player/Player_Model/"+textures[i]+".png");Check(texture!=null,textures[i]+" exists");
            var path=folder+"/"+ids[i]+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Metallic",.35f);material.SetFloat("_Smoothness",.3f);EditorUtility.SetDirty(material);
        }
        for(int i=1;i<=3;i++){
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(KnightGreatswordBuilder.Folder+"/GreatswordCombo"+i+".anim");
            var events=AnimationUtility.GetAnimationEvents(clip);var begin=events.First(e=>e.functionName=="AnimEvent_BeginSwing");
            begin.time=clip.length*GreatswordWeapon.SwingStartFor(i)/GreatswordWeapon.AttackSeconds;AnimationUtility.SetAnimationEvents(clip,events);EditorUtility.SetDirty(clip);
            Check(Mathf.Abs(begin.time/clip.length*1.6f-(i==3?.72f:.56f))<.001f,"combo "+i+" delayed swing event");
        }
        BuildMummy();AssetDatabase.SaveAssets();
    }
    public static void BuildMummy(){
        string folder=MummyContentBuilder.Folder;
        var source=AssetDatabase.LoadAllAssetsAtPath(MummyContentBuilder.Model).OfType<AnimationClip>().First(c=>c.name=="mummy_runHit");
        var charge=Copy(source,folder+"/MummyCharge.anim");
        // Movement belongs to NavMesh/server simulation, never to animation root motion.
        foreach(var binding in AnimationUtility.GetCurveBindings(charge))if(binding.type==typeof(Transform)&&binding.path.EndsWith("Hips")&&(binding.propertyName=="m_LocalPosition.x"||binding.propertyName=="m_LocalPosition.z")){
            var curve=AnimationUtility.GetEditorCurve(charge,binding);AnimationUtility.SetEditorCurve(charge,binding,AnimationCurve.Constant(0,charge.length,curve.Evaluate(0)));
        }
        var windup=Copy(charge,folder+"/MummyChargeWindup.anim");
        foreach(var binding in AnimationUtility.GetCurveBindings(windup)){var curve=AnimationUtility.GetEditorCurve(windup,binding);AnimationUtility.SetEditorCurve(windup,binding,AnimationCurve.Constant(0,.6f,curve.Evaluate(0)));}
        var hit=RetargetHit();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(folder+"/Mummy.controller");var machine=controller.layers[0].stateMachine;var idle=machine.states.First(s=>s.state.name=="Locomotion").state;
        void State(string name,AnimationClip clip,float duration,string tag,bool exit){
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);state.motion=clip;state.speed=clip.length/duration;state.tag=tag;
            foreach(var old in state.transitions.ToArray())state.RemoveTransition(old);
            if(exit){var transition=state.AddTransition(idle);transition.hasExitTime=true;transition.exitTime=1;transition.duration=.08f;transition.hasFixedDuration=true;}EditorUtility.SetDirty(state);
        }
        State("Charge",charge,.8f,"Attack",true);State("ChargeWindup",windup,.6f,"Attack",false);State("Hit",hit,.35f,"Hit",true);EditorUtility.SetDirty(controller);
        Check(charge!=hit&&AnimationUtility.GetCurveBindings(hit).Length>0,"mummy charge and hit use separate animated clips");
    }
    static AnimationClip Copy(AnimationClip source,string path){var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(clip==null){clip=Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);clip.name=Path.GetFileNameWithoutExtension(path);AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);return clip;}
    static AnimationClip RetargetHit(){
        string knight=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/HitSourceRetarget.fbx"),mummy=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/HitTargetRetarget.fbx");GameObject model=null;
        try{
            foreach(var pair in new[]{(KnightGreatswordBuilder.KnightModel,knight),(MummyContentBuilder.Model,mummy)}){
                AssetDatabase.CopyAsset(pair.Item1,pair.Item2);var importer=(ModelImporter)AssetImporter.GetAtPath(pair.Item2);importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.preserveHierarchy=true;importer.SaveAndReimport();
            }
            var clips=AssetDatabase.LoadAllAssetsAtPath(knight).OfType<AnimationClip>().ToArray();
            var source=clips.First(c=>c.name.IndexOf("Attacked",StringComparison.OrdinalIgnoreCase)>=0);
            model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(mummy));var animator=model.GetComponent<Animator>();
            Check(animator.avatar!=null&&animator.avatar.isValid&&animator.isHuman&&source.isHumanMotion,"knight hit can retarget to mummy humanoid skeleton");animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
            var baked=CombatRefinementBuilder.Bake(model,animator,"MummyHitRetarget",source,null,0,.35f,.07f,.1855f);
            var hit=Copy(baked,MummyContentBuilder.Folder+"/MummyHit.anim");AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(baked));return hit;
        }finally{if(model!=null)Object.DestroyImmediate(model);AssetDatabase.DeleteAsset(knight);AssetDatabase.DeleteAsset(mummy);}
    }
}
