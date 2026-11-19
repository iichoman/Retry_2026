using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class MummyContentBuilder
{
    public const string Model="Assets/GameObjects/Monster/mummy/mummy.fbx",Folder="Assets/CombatContent/Mummy",Prefab="Assets/Resources/Mummy.prefab",Output="Artifacts/Mummy";
    static bool running;
    static MummyContentBuilder(){EditorApplication.update+=Poll;}
    static async void Poll()
    {
        if(running||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Output+"/build.request"))return;
        try{File.Delete(Output+"/build.request");}catch(IOException){return;}
        running=true;
        try{Build();await Validate();File.WriteAllText(Output+"/result.txt","PASS mummy prefab, animations, gas presentation, network spawn and client geometry\n");}
        catch(Exception e){File.WriteAllText(Output+"/result.txt","FAIL\n"+e);Debug.LogException(e);}
        finally{running=false;}
    }
    static void Set(Object target,string name,object value)=>KnightGreatswordBuilder.Set(target,name,value);
    [MenuItem("Retry/Mummy/Build Content")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);Directory.CreateDirectory(Output);AssetDatabase.Refresh();
        var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
        bool reimport=importer.avatarSetup!=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        var importedClips=importer.clipAnimations;
        foreach(var clip in importedClips)if(clip.name=="mummy_die"&&clip.lastFrame<280){clip.lastFrame=280;reimport=true;}
        if(reimport){importer.clipAnimations=importedClips;importer.SaveAndReimport();}
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var report=new StringBuilder();
        foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>())report.AppendLine($"CLIP {clip.name} {clip.length} seconds {clip.frameRate} fps");
        foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)
            report.AppendLine($"RENDERER {renderer.name} material {material?.name} texture {material?.mainTexture?.name} path {AssetDatabase.GetAssetPath(material?.mainTexture)} bounds {renderer.bounds}");
        report.AppendLine("BONES "+string.Join(", ",source.GetComponentsInChildren<Transform>(true).Select(t=>t.name)));
        File.WriteAllText(Output+"/source.txt",report.ToString());
        AnimationClip walk=Clip("mummy_walk","MummyWalk",true),attack=Clip("mummy_rangeAttack","MummyAttack",false),hit=Clip("mummy_runHit","MummyHit",false),death=Clip("mummy_die","MummyDeath",false);
        var idle=Clip("mummy_walk","MummyIdle",true);
        foreach(var binding in AnimationUtility.GetCurveBindings(idle)){var curve=AnimationUtility.GetEditorCurve(idle,binding);AnimationUtility.SetEditorCurve(idle,binding,AnimationCurve.Constant(0,1,curve.Evaluate(0)));}
        EditorUtility.SetDirty(idle);
        string controllerPath=Folder+"/Mummy.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine=controller.layers[0].stateMachine;
        if(!controller.parameters.Any(p=>p.name=="Mspeed"))controller.AddParameter("Mspeed",AnimatorControllerParameterType.Float);
        var locomotion=State(machine,"Locomotion");
        var blend=locomotion.motion as BlendTree;
        if(blend==null){blend=new BlendTree{name="Mummy Locomotion",blendParameter="Mspeed",useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(blend,controller);locomotion.motion=blend;}
        blend.children=Array.Empty<ChildMotion>();blend.AddChild(idle,0);blend.AddChild(walk,1.5f);machine.defaultState=locomotion;
        Configure(State(machine,"Attack"),attack,2.7f,"Attack",locomotion);
        Configure(State(machine,"Hit"),hit,.35f,"Hit",locomotion);
        Configure(State(machine,"Die"),death,.9f,"Death",null);EditorUtility.SetDirty(controller);
        var root=new GameObject("Mummy");root.SetActive(false);
        try {
            root.layer=LayerMask.NameToLayer("Monster");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source);model.name="Mummy Model";model.transform.SetParent(root.transform,false);
            idle.SampleAnimation(model,0);
            var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // Imported renderer bounds also contain the death/attack poses, below the feet.
            // Skin the standing pose so placement uses the soles instead of those bounds.
            Bounds bounds=PoseBounds(renderers);
            float scale=1.9f/Mathf.Max(.01f,bounds.size.y);model.transform.localScale*=scale;model.transform.localPosition-=Vector3.up*bounds.min.y*scale;
            for(int r=0;r<renderers.Length;r++) {
                var materials=renderers[r].sharedMaterials;
                for(int i=0;i<materials.Length;i++) {
                    var original=materials[i];string path=$"{Folder}/Mummy_{r}_{i}.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
                    var texture=original?.mainTexture??AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Monster/mummy/Image"+Mathf.Min(r,1)+".jpg");
                    material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.12f);material.SetFloat("_Cull",0);materials[i]=material;EditorUtility.SetDirty(material);
                }
                renderers[r].sharedMaterials=materials;renderers[r].updateWhenOffscreen=true;
            }
            var animator=model.GetComponent<Animator>();if(animator==null)animator=model.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var health=root.AddComponent<Monster_State>();Set(health,"maxHp",100);Set(health,"attackPower",8);Set(health,"monsterType",(int)MonsterType.Normal);
            var body=root.AddComponent<CapsuleCollider>();body.center=Vector3.up*.95f;body.height=1.9f;body.radius=.38f;
            var agent=root.AddComponent<NavMeshAgent>();agent.height=1.9f;agent.radius=.38f;agent.speed=1.5f;agent.acceleration=8;agent.angularSpeed=180;agent.stoppingDistance=5.6f;agent.updateRotation=false;
            var view=root.AddComponent<MummyBreathView>();
            string gasPath=Folder+"/PoisonGas.mat";var gasMaterial=AssetDatabase.LoadAssetAtPath<Material>(gasPath);
            var smoke=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/Materials/Torch_Smoke.mat");
            if(gasMaterial==null){gasMaterial=new Material(smoke);AssetDatabase.CreateAsset(gasMaterial,gasPath);}else EditorUtility.CopySerialized(smoke,gasMaterial);
            gasMaterial.name="PoisonGas";gasMaterial.SetColor("_BaseColor",Color.white);gasMaterial.SetFloat("_Cull",0);
            // Keep the poisonous green readable in the dungeon's dark lighting.
            gasMaterial.EnableKeyword("_EMISSION");gasMaterial.SetTexture("_EmissionMap",Texture2D.whiteTexture);gasMaterial.SetColor("_EmissionColor",new Color(.16f,.38f,.025f));
            EditorUtility.SetDirty(gasMaterial);Set(view,"gasMaterial",gasMaterial);
            var head=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.Equals("Head",StringComparison.OrdinalIgnoreCase)||t.name.EndsWith(":Head"));
            if(head!=null){var mouth=new GameObject("Gas mouth").transform;mouth.SetParent(head,false);mouth.position=root.transform.position+Vector3.up*1.55f+Vector3.forward*.22f;Set(view,"mouth",mouth);}
            var ai=root.AddComponent<MummyMonster>();Set(ai,"monsterId","mummy");Set(ai,"detectRange",14f);Set(ai,"attackRange",6f);Set(ai,"moveSpeed",1.5f);
            var attackComponent=root.AddComponent<Monster_Attack>();Set(attackComponent,"monster",ai);Set(attackComponent,"monsterState",health);Set(attackComponent,"attackCooldown",4.2f);
            var serialized=new SerializedObject(attackComponent);serialized.FindProperty("attackHitboxes").arraySize=0;serialized.ApplyModifiedPropertiesWithoutUndo();
            var animation=root.AddComponent<Monster_Animation>();Set(animation,"animator",animator);Set(animation,"monsterAttack",attackComponent);Set(animation,"monsterState",health);Set(animation,"navMeshAgent",agent);
            root.AddComponent<MonsterAlertEffect>();root.AddComponent<HitEffectReceiver>();
            var loot=root.AddComponent<MonsterLoot>();Set(loot,"monsterState",health);Set(loot,"lootContainer",root.GetComponent<LootContainer>());
            var drops=new SerializedObject(loot);drops.FindProperty("drops").arraySize=2;
            for(int i=0;i<2;i++){var drop=drops.FindProperty("drops").GetArrayElementAtIndex(i);drop.FindPropertyRelative("item").objectReferenceValue=ItemCatalog.Load().Resolve(i==0?"health_potion":"poison_thorn_t1");drop.FindPropertyRelative("minAmount").intValue=1;drop.FindPropertyRelative("maxAmount").intValue=1;drop.FindPropertyRelative("dropChance").floatValue=i==0?.5f:.15f;}drops.ApplyModifiedPropertiesWithoutUndo();
            var hpSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monster1.prefab").transform.Find("Monster HP Bar");
            var hp=Object.Instantiate(hpSource.gameObject,root.transform);hp.name="Mummy HP Bar";hp.transform.localPosition=Vector3.up*2.2f;Set(hp.GetComponent<MonsterHealthBarUI>(),"monsterState",health);Set(hp.GetComponent<MonsterHealthBarUI>(),"root",hp);
            root.SetActive(true);PrefabUtility.SaveAsPrefabAsset(root,Prefab);
        }finally{Object.DestroyImmediate(root);}
        SwingChargeContentBuilder.BuildMummy();
        AssetDatabase.SaveAssets();
    }
    static AnimatorState State(AnimatorStateMachine machine,string name)=>machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);
    static Bounds PoseBounds(SkinnedMeshRenderer[] renderers)
    {
        Bounds bounds=default;bool first=true;
        foreach(var renderer in renderers) {
            var mesh=renderer.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes;
            var matrices=renderer.bones.Select((bone,i)=>bone.localToWorldMatrix*bind[i]).ToArray();
            for(int i=0;i<vertices.Length;i++) {
                var w=weights[i];var v=vertices[i];
                Vector3 point=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1
                    +matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }
    static void Configure(AnimatorState state,AnimationClip clip,float duration,string tag,AnimatorState next)
    {
        state.motion=clip;state.speed=clip.length/duration;state.tag=tag;foreach(var transition in state.transitions.ToArray())state.RemoveTransition(transition);
        if(next!=null){var transition=state.AddTransition(next);transition.hasExitTime=true;transition.exitTime=1;transition.duration=.08f;transition.hasFixedDuration=true;}
    }
    static AnimationClip Clip(string sourceName,string name,bool loop)
    {
        var source=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().First(c=>c.name==sourceName);
        string path=Folder+"/"+name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
        clip.name=name;clip.wrapMode=loop?WrapMode.Loop:WrapMode.ClampForever;
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;settings.loopBlendPositionXZ=true;settings.loopBlendPositionY=true;settings.keepOriginalPositionY=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
        AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());EditorUtility.SetDirty(clip);return clip;
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Debug.Log("PASS "+message);}
    static async Task Validate()
    {
        Check(System.Runtime.InteropServices.Marshal.SizeOf<MummyBreathState>()==40,"mummy packet layout");
        Check(MummyBreathView.Contains(Vector3.zero,Vector3.forward,.6f,new Vector3(0,0,2))&&!MummyBreathView.Contains(Vector3.zero,Vector3.forward,.6f,new Vector3(2,0,2))&&!MummyBreathView.Contains(Vector3.zero,Vector3.forward,.6f,new Vector3(0,0,-1)),"gas direction, width and rear dodge");
        var scene=EditorSceneManager.NewPreviewScene();
        try {
            var registry=new GameObject("Mummy registry fixture").AddComponent<RemoteMonsterRegistry>();SceneManager.MoveGameObjectToScene(registry.gameObject,scene);
            registry.OnEnterView(new MonsterEnterView{monsterId=900,monsterKind=(int)MonsterKind.MUMMY,hp=100,maxHp=100});
            var root=registry.GetActor(900);Check(root!=null&&root.GetComponent<MummyMonster>()!=null&&!root.GetComponent<MummyMonster>().enabled&&!root.GetComponent<NavMeshAgent>().enabled,"mummy network prefab disables local AI and navigation");
            foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
            var animator=root.GetComponentInChildren<Animator>();animator.Rebind();animator.Update(0);
            Check(animator.avatar!=null&&animator.avatar.isValid,"mummy animation rig is valid");
            var view=root.GetComponent<MummyBreathView>();view.enabled=true;
            foreach(string state in new[]{"Locomotion","Attack","Die"}) {
                animator.Play(state,0,state=="Die"?.999f:state=="Attack"?.40f:0);animator.Update(0);
                if(state=="Attack")registry.OnMummyBreath(new MummyBreathState{version=1,monsterId=900,serial=1,phase=2,dz=1,elapsed=.7f});else view.Stop();
                animator.Update(0);await Capture(scene,root,state);
                var bones=root.GetComponentsInChildren<Transform>();
                var poseBounds=PoseBounds(root.GetComponentsInChildren<SkinnedMeshRenderer>());
                File.AppendAllText(Output+"/source.txt",$"{state}: mesh Y={poseBounds.min.y:F3}..{poseBounds.max.y:F3}, head={bones.First(t=>t.name=="Head").position}, mouth={bones.First(t=>t.name=="Gas mouth").position}\n");
                if(state=="Locomotion")Check(Mathf.Abs(poseBounds.min.y)<.04f&&Mathf.Abs(poseBounds.size.y-1.9f)<.04f,"standing mummy is 1.9m high and grounded");
                foreach(var particle in root.GetComponentsInChildren<ParticleSystem>())File.AppendAllText(Output+"/source.txt",$"{state}: particles={particle.particleCount} paused={particle.isPaused} bounds={particle.GetComponent<Renderer>().bounds} scene={particle.gameObject.scene.name}\n");
            }
            registry.OnMummyBreath(new MummyBreathState{version=1,monsterId=900,serial=1,phase=0});Check(view.Phase==0,"server cancellation clears poison cloud");
            registry.OnMummyBreath(new MummyBreathState{version=1,monsterId=900,serial=0,phase=2,dz=1,elapsed=.2f});Check(view.Phase==0,"older breath cannot restart a cancelled cloud");
            registry.OnDied(900);root.GetComponent<Monster_Animation>().ShowCorpse();
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Die")&&animator.speed==0,"mummy death holds its final pose");
        }finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    static async Task Capture(Scene scene,GameObject root,string name)
    {
        var camera=new GameObject("Mummy preview camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position=new Vector3(6,4,8);camera.transform.LookAt(new Vector3(0,1,1.4f));camera.orthographic=true;camera.orthographicSize=3.1f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.055f,.065f);
        var light=new GameObject("Mummy key").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=2.4f;light.transform.rotation=Quaternion.Euler(40,210,0);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(0,-.12f,1.5f);floor.transform.localScale=new Vector3(9,.2f,9);
        var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.09f,.11f,.13f));floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var target=new RenderTexture(1200,900,24);var texture=new Texture2D(1200,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;bool asyncShaders=ShaderUtil.allowAsyncCompilation;
        try {
            await WeaponIconBuilder.NextFrame();await WeaponIconBuilder.NextFrame();camera.targetTexture=target;
            // Reapply the pose after the editor has processed preview hierarchy changes.
            var animator=root.GetComponentInChildren<Animator>();animator.Play(name,0,name=="Die"?.999f:name=="Attack"?.59f:0);animator.Update(0);
            root.GetComponent<MummyBreathView>().Render();ShaderUtil.allowAsyncCompilation=false;
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1200,900),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name.ToLowerInvariant()+".png",texture.EncodeToPNG());
        }finally{ShaderUtil.allowAsyncCompilation=asyncShaders;camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(texture);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(floor);Object.DestroyImmediate(floorMaterial);}
    }
}
