using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class GreatswordSkillValidation
{
    const string Output="Artifacts/GreatswordSkills";
    static void Check(bool pass,string name){if(!pass)throw new Exception(name);Debug.Log("PASS "+name);}
    static Task Frame(){var done=new TaskCompletionSource<bool>();void Tick(){EditorApplication.update-=Tick;done.SetResult(true);}EditorApplication.update+=Tick;EditorApplication.QueuePlayerLoopUpdate();return done.Task;}
    public static async void Batch(){Directory.CreateDirectory(Output);try{await Run();File.WriteAllText(Output+"/validation.txt","PASS Unity runtime/editor compile, skill movement flags, remote parry outline activation/deactivation, Q targeting/descent/impact renders.\n");EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Output+"/validation.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
    static async Task Run(){
        var scene=EditorSceneManager.NewPreviewScene();
        try{
            var root=new GameObject("Greatsword presentation fixture");SceneManager.MoveGameObjectToScene(root,scene);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.localScale=new Vector3(30,.2f,30);floor.transform.position=new Vector3(0,-.15f,0);
            var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.color=new Color(.06f,.075f,.09f);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            var light=new GameObject("Key light").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var camera=new GameObject("Effect camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.055f);camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.fieldOfView=55;camera.transform.position=new Vector3(12,10,16);camera.transform.LookAt(new Vector3(0,3,0));
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),root.transform);actor.transform.position=new Vector3(-5,0,0);
            foreach(var c in actor.GetComponentsInChildren<Camera>(true))c.enabled=false;
            var defense=actor.GetComponent<PlayerDefenseController>();defense.SendMessage("Awake");defense.SetRemotePresentation();defense.ApplyRemoteDefense(new PlayerPresentation{action=5,serial=1,duration=.5f,remaining=.5f});defense.SendMessage("LateUpdate");
            var outline=actor.GetComponent<EliteMonsterOutline>();Check(outline!=null&&outline.enabled,"remote parry starts yellow hull");outline.SendMessage("OnEnable");
            Check(actor.GetComponentsInChildren<Renderer>(true).Any(r=>r.name=="Elite Outline Hull"),"outline follows actual player meshes");
            var fx=GreatswordSkillEffect.Marker(Vector3.zero,3.5f);fx.transform.SetParent(root.transform,true);await Frame();await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/target-parry.png");Object.DestroyImmediate(fx.gameObject);
            defense.CancelDefense();defense.SendMessage("LateUpdate");Check(!outline.enabled,"parry end disables outline");
            var view=actor.GetComponent<PlayerSkillView>()??actor.AddComponent<PlayerSkillView>();view.SendMessage("Awake");
            view.Begin(new SkillEvent{serial=1,skill=3,remaining=2.25f,windup=1.8f,dz=1,area=1});Check(PlayerSkillView.BlocksMovement(actor),"ordinary spin locks movement");view.Cancel();
            view.Begin(new SkillEvent{serial=2,skill=5,remaining=2.25f,windup=1.8f,dz=1,area=1});Check(view.IsCasting&&!PlayerSkillView.BlocksMovement(actor),"mobile spin retains cast but unlocks movement");view.Cancel();
            fx=GreatswordSkillEffect.Descent(Vector3.up*.01f,1,1.2f);fx.transform.SetParent(root.transform,true);fx.Sample(.96f);await Frame();await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/descent.png");Object.DestroyImmediate(fx.gameObject);
            fx=GreatswordSkillEffect.Impact(Vector3.up*.01f,1);fx.transform.SetParent(root.transform,true);fx.Sample(.2f);
            foreach(var kind in new[]{CombatVfxKind.SlamImpact,CombatVfxKind.Shockwave}){var prefab=CombatVfxCatalog.Load().Find(kind);if(prefab==null)continue;var burst=Object.Instantiate(prefab,root.transform);burst.Play(Vector3.zero,Quaternion.identity,3.5f*.65f,0,1);burst.SetProgress(.2f);foreach(var particles in burst.GetComponentsInChildren<ParticleSystem>())particles.Simulate(.2f,true,false);}
            await Frame();await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/impact.png");Object.DestroyImmediate(fx.gameObject);
            Object.DestroyImmediate(floorMaterial);
        }finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
