using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class CombatRefinementBuilder
{
    const string Folder=KnightGreatswordBuilder.Folder, Output="Artifacts/CombatRefinement";
    static readonly List<string> changed=new();
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Debug.Log("PASS "+message);}
    static AnimationClip PlayerClip(int index)=>AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/PlayerDefense/Animations/Combat/Sword{index}.anim");
    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine){foreach(var child in machine.states)yield return child.state;foreach(var child in machine.stateMachines)foreach(var state in States(child.stateMachine))yield return state;}
    static void State(AnimatorState state,AnimationClip clip,float duration,AnimatorState exit){
        state.motion=clip;state.speed=clip.length/duration;state.speedParameterActive=false;state.tag="Attack";
        foreach(var old in state.transitions.ToArray())state.RemoveTransition(old);
        var transition=state.AddTransition(exit);transition.hasExitTime=true;transition.exitTime=1;transition.hasFixedDuration=true;transition.duration=.08f;EditorUtility.SetDirty(state);
    }
    public static void Build(){
        Directory.CreateDirectory(Output);changed.Clear();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(KnightGreatswordBuilder.PlayerController);
        var machine=controller.layers[0].stateMachine;var attacks=machine.stateMachines.FirstOrDefault(m=>m.stateMachine.name=="test").stateMachine??machine;
        var idle=States(machine).First(s=>s.name=="Blend Tree");
        for(int i=1;i<=3;i++){
            string path=Folder+"/GreatswordCombo"+i+".anim";
            var source=i==3?AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/GreatswordCleave.anim"):PlayerClip(i);
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null){clip=Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
            clip.name="GreatswordCombo"+i;
            AnimationEvent E(float t,string function)=>new AnimationEvent{time=t*clip.length,functionName=function};
            AnimationUtility.SetAnimationEvents(clip,new[]{E(GreatswordWeapon.SwingStartFor(i)/GreatswordWeapon.AttackSeconds,"AnimEvent_BeginSwing"),E(i==3?.65f:.53f,"AnimEvent_EndSwing"),E(.3f,"AnimEvent_ComboWindowOpen"),E(.9f,"AnimEvent_ComboCommit"),E(.95f,"AnimEvent_ComboWindowClose")});
            State(States(machine).FirstOrDefault(s=>s.name=="greatsword_attack_"+i)??attacks.AddState("greatsword_attack_"+i),clip,GreatswordWeapon.AttackSeconds,idle);
            EditorUtility.SetDirty(clip);changed.Add(path);
        }
        EditorUtility.SetDirty(controller);changed.Add(KnightGreatswordBuilder.PlayerController);
        BakeKnight();AssetDatabase.SaveAssets();File.WriteAllLines(Output+"/changed-assets.txt",changed);
    }
    static void BakeKnight(){
        string temporary=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/KnightCombatRetarget.fbx");GameObject model=null;
        try{
            AssetDatabase.CopyAsset(KnightGreatswordBuilder.KnightModel,temporary);
            var importer=(ModelImporter)AssetImporter.GetAtPath(temporary);importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.preserveHierarchy=true;importer.importAnimation=false;importer.SaveAndReimport();
            model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(temporary));var animator=model.GetComponent<Animator>();
            Check(animator.avatar!=null&&animator.avatar.isValid&&animator.isHuman,"knight accepts player humanoid motions");animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var normal=Bake(model,animator,"KnightAttack",PlayerClip(1),PlayerClip(2),.15f,1.35f,.72f,1.08f);
            AnimationUtility.SetAnimationEvents(normal,new[]{new AnimationEvent{time=.72f,functionName="AnimEvent_BeginSwing"},new AnimationEvent{time=1.08f,functionName="AnimEvent_EndSwing"}});
            var heavy=Bake(model,animator,"KnightHeavyAttack",PlayerClip(3),null,0,1.8f,1.1f,1.45f);
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder+"/KnightElite.controller");var states=States(controller.layers[0].stateMachine).ToArray();
            State(states.First(s=>s.name=="Attack"),normal,1.35f,states.First(s=>s.name=="Locomotion"));
            State(states.First(s=>s.name=="HeavyAttack"),heavy,1.8f,states.First(s=>s.name=="Locomotion"));
            EditorUtility.SetDirty(normal);EditorUtility.SetDirty(heavy);EditorUtility.SetDirty(controller);changed.Add(Folder+"/KnightElite.controller");
        }finally{if(model!=null)Object.DestroyImmediate(model);AssetDatabase.DeleteAsset(temporary);}
    }
    internal static AnimationClip Bake(GameObject model,Animator animator,string name,AnimationClip source,AnimationClip blend,float weight,float duration,float swingStart,float swingEnd){
        var graph=PlayableGraph.Create(name);graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        try{
            animator.Rebind();var a=AnimationClipPlayable.Create(graph,source);var mixer=AnimationMixerPlayable.Create(graph,2);graph.Connect(a,0,mixer,0);mixer.SetInputWeight(0,1-weight);
            AnimationClipPlayable b=default;if(blend!=null){b=AnimationClipPlayable.Create(graph,blend);graph.Connect(b,0,mixer,1);mixer.SetInputWeight(1,weight);}
            AnimationPlayableOutput.Create(graph,name,animator).SetSourcePlayable(mixer);graph.Play();
            var bones=model.GetComponentsInChildren<Transform>().Where(t=>t!=model.transform).ToArray();
            var curves=bones.Select(_=>Enumerable.Range(0,7).Select(i=>new AnimationCurve()).ToArray()).ToArray();var previous=new Quaternion[bones.Length];int frames=Mathf.CeilToInt(duration*30);
            for(int frame=0;frame<=frames;frame++){
                float time=duration*frame/frames;
                float progress=time<=swingStart?Mathf.Lerp(0,.2f,time/swingStart):time<=swingEnd?Mathf.Lerp(.2f,.53f,(time-swingStart)/(swingEnd-swingStart)):Mathf.Lerp(.53f,1,(time-swingEnd)/(duration-swingEnd));
                a.SetTime(source.length*progress);if(blend!=null)b.SetTime(blend.length*progress);graph.Evaluate(0);
                for(int i=0;i<bones.Length;i++){var p=bones[i].localPosition;var q=bones[i].localRotation;if(frame>0&&Quaternion.Dot(previous[i],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[i]=q;
                    for(int axis=0;axis<3;axis++)curves[i][axis].AddKey(time,p[axis]);for(int axis=0;axis<4;axis++)curves[i][axis+3].AddKey(time,q[axis]);}
            }
            string path=Folder+"/"+name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(clip==null){clip=new AnimationClip{name=name};AssetDatabase.CreateAsset(clip,path);}clip.ClearCurves();clip.frameRate=30;
            for(int i=0;i<bones.Length;i++){string binding=AnimationUtility.CalculateTransformPath(bones[i],model.transform);
                for(int axis=0;axis<7;axis++){var curve=curves[i][axis];for(int key=0;key<curve.length;key++){AnimationUtility.SetKeyLeftTangentMode(curve,key,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,key,AnimationUtility.TangentMode.Linear);}
                    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(binding,typeof(Transform),axis<3?"m_LocalPosition."+"xyz"[axis]:"m_LocalRotation."+"xyzw"[axis-3]),KnightDeathBuilder.Reduce(curve,axis<3?.0002f:.0005f));}}
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.startTime=0;settings.stopTime=duration;settings.loopTime=settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());EditorUtility.SetDirty(clip);changed.Add(path);return clip;
        }finally{graph.Destroy();}
    }
    static Task Frame(){var t=new TaskCompletionSource<bool>();void Done(){EditorApplication.update-=Done;t.SetResult(true);}EditorApplication.update+=Done;EditorApplication.QueuePlayerLoopUpdate();return t.Task;}
    public static async void Batch(){Directory.CreateDirectory(Output);try{Build();await Validate();File.WriteAllText(Output+"/validation.txt","PASS three combo clips/events, knight retarget, matching sector geometry, rear aim camera, elemental Q rendering.\n");EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Output+"/validation.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
    static async Task Validate(){
        Check(KnightHeavyAttack.Contains(Vector3.zero,Vector3.forward*6,6,new Vector3(3,0,3))&&!KnightHeavyAttack.Contains(Vector3.zero,Vector3.forward*6,6,new Vector3(5,0,1))&&!KnightHeavyAttack.Contains(Vector3.zero,Vector3.forward*6,6,Vector3.back),"knight heavy 120-degree sector");
        var scene=EditorSceneManager.NewPreviewScene();Material floorMat=null;
        try{
            var root=new GameObject("Combat refinement fixture");SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),root.transform);
            var knight=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KnightGreatswordBuilder.KnightPath),root.transform);knight.transform.position=Vector3.right*5;
            foreach(var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;foreach(var c in root.GetComponentsInChildren<Camera>(true))c.enabled=false;root.SetActive(true);
            var playerAnimator=player.GetComponentInChildren<Animator>();playerAnimator.Rebind();playerAnimator.applyRootMotion=false;
            foreach(var weapon in player.GetComponentsInChildren<Weapon_Sword>(true))weapon.gameObject.SetActive(weapon is GreatswordWeapon);
            var sword=player.GetComponentInChildren<GreatswordWeapon>();sword.Initialize(player.GetComponent<Player_Attack>());
            for(int i=1;i<=3;i++){if(i==1)sword.RequestAttack();Check(sword.TryConsumeAnimationRequest(out int requested)&&requested==i,"queued greatsword combo "+i);sword.OnAnimationStarted(i);if(i<3){sword.OnComboWindowOpened(i);sword.RequestAttack();sword.OnComboWindowCommitted(i);}else{sword.OnAnimationCompleted(i);Check(!sword.TryGetAnimationStateName(4,out _),"fourth hit rejected");}}
            var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.065f);camera.fieldOfView=48;camera.nearClipPlane=.1f;
            var light=new GameObject("Key light").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.localScale=new Vector3(32,.2f,32);floor.transform.position=new Vector3(0,-.12f,0);floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.09f,.11f,.14f);floor.GetComponent<Renderer>().sharedMaterial=floorMat;
            var knightAnimator=knight.GetComponentInChildren<Animator>();knightAnimator.Rebind();knightAnimator.applyRootMotion=false;
            for(int i=1;i<=3;i++){
                Check(playerAnimator.HasState(0,Animator.StringToHash("greatsword_attack_"+i))||playerAnimator.HasState(0,Animator.StringToHash("Base Layer.test.greatsword_attack_"+i)),"shared local/remote state "+i);
                playerAnimator.Play("greatsword_attack_"+i,0,.4f);playerAnimator.Update(0);
                knightAnimator.Play(i==3?"HeavyAttack":"Attack",0,i==3?.7f:.6f);knightAnimator.Update(0);
                camera.transform.position=new Vector3(10,5,10);camera.transform.LookAt(new Vector3(2.5f,1.2f,0));await Frame();await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/attacks-"+i+".png");
            }
            playerAnimator.Play("Blend Tree",0,0);playerAnimator.Update(0);
            var cameraRig=player.GetComponent<Player_Camera_Controller>();
            void Set(string field,object value)=>typeof(Player_Camera_Controller).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(cameraRig,value);
            Set("cameraTransform",camera.transform);cameraRig.SendMessage("Start");Set("aimWeight",1f);Set("aimYaw",0f);Set("currentZoomDistance",2.2f);cameraRig.SendMessage("HandleFollow");
            Check(camera.transform.position.z<0&&camera.transform.position.magnitude<3.5f&&camera.transform.position.y>1.5f,"aim lens behind head with collision checks");await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/aim-camera.png");
            player.SetActive(false);knight.SetActive(false);
            foreach(string id in new[]{"greatsword_001","flame_greatsword","frost_greatsword"}){
                var fx=GreatswordSkillEffect.Impact(new Vector3(0,.01f,0),1,ItemHash.Of(id));fx.transform.SetParent(root.transform,true);fx.Sample(.15f);
                camera.transform.position=new Vector3(10,8,13);camera.transform.LookAt(new Vector3(0,3,0));await Frame();await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/q-"+id+".png");Object.DestroyImmediate(fx.gameObject);
            }
        }finally{EditorSceneManager.ClosePreviewScene(scene);if(floorMat!=null)Object.DestroyImmediate(floorMat);}
    }
}
