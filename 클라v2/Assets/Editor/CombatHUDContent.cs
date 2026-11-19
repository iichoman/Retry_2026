using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class CombatHUDContent
{
    const string Output="Artifacts/CombatHUD",Request=Output+"/build.request";
    static double readyAt;
    static CombatHUDContent(){readyAt=EditorApplication.timeSinceStartup+1;EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=_=>readyAt=EditorApplication.timeSinceStartup+1;}
    static void Poll()
    {
        if(Application.isPlaying||EditorApplication.timeSinceStartup<readyAt||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        try{File.Delete(Request);}catch(IOException){return;}
        try{Build();Validate();HudPreviewValidation.Run();Check(File.ReadAllText("Artifacts/HUD1110/validation.txt").StartsWith("PASS"),"HUD render validation");File.WriteAllText(Output+"/result.txt","PASS monster heavy animation, 2500 HP boss prefabs, wire sizes, delayed health behavior, HUD render at four resolutions.\n");}
        catch(Exception e){File.WriteAllText(Output+"/result.txt","FAIL\n"+e);Debug.LogException(e);}
    }
    public static void BuildKnightAttack(AnimatorController controller)
    {
        string folder=KnightGreatswordBuilder.Folder,path=folder+"/KnightHeavyAttack.anim";
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"/KnightAttack.anim");
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}
        clip.name="KnightHeavyAttack";AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        var machine=controller.layers[0].stateMachine;
        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="HeavyAttack")??machine.AddState("HeavyAttack");
        state.motion=clip;state.speed=clip.length/1.8f;state.tag="Attack";
        foreach(var transition in state.transitions.ToArray())state.RemoveTransition(transition);
        var exit=state.AddTransition(machine.states.First(s=>s.state.name=="Locomotion").state);exit.hasExitTime=true;exit.exitTime=1;exit.hasFixedDuration=true;exit.duration=.08f;
        EditorUtility.SetDirty(clip);EditorUtility.SetDirty(controller);
    }
    [MenuItem("Retry/Combat/Build patterns and HUD")]
    public static void Build()
    {
        Directory.CreateDirectory(Output);
        BuildKnightAttack(AssetDatabase.LoadAssetAtPath<AnimatorController>(KnightGreatswordBuilder.Folder+"/KnightElite.controller"));
        foreach(string path in new[]{"Assets/CombatContent/Prefabs/DungeonGuardian.prefab",LocalBossBuilder.PrefabPath}){
            var root=PrefabUtility.LoadPrefabContents(path);
            try{var state=new SerializedObject(root.GetComponent<Monster_State>());state.FindProperty("maxHp").intValue=2500;state.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssets();
    }
    static void Check(bool pass,string label){if(!pass)throw new Exception(label);Debug.Log("PASS "+label);}
    static void Validate()
    {
        Check(System.Runtime.InteropServices.Marshal.SizeOf<BossTelegraphData>()==76&&System.Runtime.InteropServices.Marshal.SizeOf<BossResolveData>()==40&&System.Runtime.InteropServices.Marshal.SizeOf<BossStateData>()==48,"monster pattern wire layout");
        var trail=new HealthTrail();trail.Set(1);trail.Set(.4f);trail.Advance(.2f);trail.Set(.4f);
        Check(trail.Current==.4f&&trail.Delayed==1,"red health drops immediately and duplicate snapshots preserve white trail");
        trail.Advance(.4f);Check(trail.Delayed<1&&trail.Delayed>.4f,"white health follows after hold");trail.Advance(2);Check(trail.Delayed==.4f,"white health reaches current health");trail.Set(.8f);Check(trail.Delayed==.8f,"healing updates both bars");
        Check(KnightHeavyAttack.Contains(Vector3.zero,Vector3.forward*6f,6f,new Vector3(0,0,3))&&!KnightHeavyAttack.Contains(Vector3.zero,Vector3.forward*6f,6f,new Vector3(5,0,1))&&!KnightHeavyAttack.Contains(Vector3.zero,Vector3.forward*6f,6f,Vector3.back),"knight heavy direction and dodge geometry");
        var scene=EditorSceneManager.NewPreviewScene();
        try{
            var registry=new GameObject("Combat registry fixture").AddComponent<RemoteMonsterRegistry>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(registry.gameObject,scene);
            registry.ElitePrefab=Resources.Load<GameObject>("KnightElite");registry.BossPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CombatContent/Prefabs/DungeonGuardian.prefab");
            registry.OnEnterView(new MonsterEnterView{monsterId=901,monsterKind=1,hp=300,maxHp=300});
            var knight=registry.GetActor(901);var animator=knight.GetComponentInChildren<Animator>();Check(animator.HasState(0,Animator.StringToHash("HeavyAttack")),"knight heavy animator state");
            long now=ServerClock.NowMs;
            registry.OnKnightTelegraph(new BossTelegraphData{monsterId=901,sequence=1,pattern=4,phase=1,targetZ=3.6f,radius=.95f,startTimeMs=now,resolveTimeMs=now+1100,endTimeMs=now+1150,serverTimeMs=now});
            Check(knight.GetComponent<KnightHeavyAttack>().IsActive,"network knight starts heavy warning");
            var bar=knight.GetComponentInChildren<MonsterHealthBarUI>();bar.SendMessage("Awake");bar.SendMessage("OnEnable");
            knight.GetComponent<Monster_State>().NetworkApplyHp(180);knight.GetComponent<Monster_State>().NetworkApplyHp(180);
            var white=bar.GetComponentsInChildren<Image>().First(i=>i.name=="Delayed Health White");
            Check(white.rectTransform.anchorMax.x==1&&bar.GetComponentInChildren<Slider>().value==180,"monster UI keeps white damage trail across repeated HP snapshots");
            CaptureKnight(scene,knight,bar);
            registry.OnKnightResolve(new BossResolveData{monsterId=901,sequence=1,pattern=0,serverTimeMs=now});Check(!knight.GetComponent<KnightHeavyAttack>().IsActive,"network knight cancels warning");
            now=ServerClock.NowMs;
            registry.OnBossState(new BossStateData{monsterId=902,phase=1,hp=2500,maxHp=2500,roomId=2,serverTimeMs=now});
            registry.OnBossTelegraph(new BossTelegraphData{monsterId=902,sequence=1,pattern=2,phase=1,targetZ=14,radius=1.65f,startTimeMs=now,resolveTimeMs=now+1400,endTimeMs=now+2400,serverTimeMs=now});
            registry.OnEnterView(new MonsterEnterView{monsterId=902,monsterKind=2,hp=2500,maxHp=2500});var boss=registry.GetActor(902).GetComponent<BossVfxController>();
            Check(boss.HasPhaseState&&boss.DisplayedHp==2500,"late entering boss receives phase and HP");
            Check((bool)typeof(BossVfxController).GetField("hasAttack",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(boss),"late entering boss receives active charge");
        }finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void CaptureKnight(UnityEngine.SceneManagement.Scene scene,GameObject knight,MonsterHealthBarUI bar)
    {
        var camera=new GameObject("Knight warning preview").AddComponent<Camera>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
        camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.transform.position=new Vector3(7,7,10);camera.transform.LookAt(new Vector3(0,1,1.5f));camera.orthographic=true;camera.orthographicSize=4.2f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.05f,.06f);
        bar.transform.rotation=Quaternion.LookRotation(bar.transform.position-camera.transform.position,Vector3.up);
        var sun=new GameObject("Preview light").AddComponent<Light>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sun.gameObject,scene);sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(50,210,0);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(0,-.12f,1.5f);floor.transform.localScale=new Vector3(12,.2f,12);
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.12f,.14f,.17f));floor.GetComponent<Renderer>().sharedMaterial=material;
        var target=new RenderTexture(1200,800,24);var texture=new Texture2D(1200,800,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{
            var animator=knight.GetComponentInChildren<Animator>();animator.Play("HeavyAttack",0,.35f);animator.Update(0);
            foreach(var warning in scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<BossTelegraphView>()))warning.SetProgress(.65f);
            Canvas.ForceUpdateCanvases();camera.targetTexture=target;
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1200,800),0,0);texture.Apply();File.WriteAllBytes(Output+"/knight-heavy-health.png",texture.EncodeToPNG());
        }finally{RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(target);Object.DestroyImmediate(texture);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);Object.DestroyImmediate(floor);Object.DestroyImmediate(material);}
    }
}
