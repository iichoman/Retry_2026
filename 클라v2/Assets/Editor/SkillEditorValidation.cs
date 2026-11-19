using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A one-shot request runs on the editor thread, in an isolated preview scene.
// It never closes/saves the user's scenes or enters/exits Play Mode.
[InitializeOnLoad]
public static class SkillEditorValidation
{
    const string Request="Artifacts/TrapsQE/editor-validation.request";
    static SkillEditorValidation(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(Application.isBatchMode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        File.Delete(Request);
        try{Validate();}catch(Exception e){File.WriteAllText("Artifacts/TrapsQE/editor-validation.txt","FAIL\n"+e);Debug.LogException(e);}
    }
    [MenuItem("Retry/Skills/Build and validate Q E (preserve open scenes)")]
    public static void Validate(){
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Preview requires Edit Mode; no Play Mode changes are performed.");
        Directory.CreateDirectory("Artifacts/TrapsQE");SkillContentBuilder.Build();
        var setups=EditorSceneManager.GetSceneManagerSetup();var preview=EditorSceneManager.NewPreviewScene();int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        try{
            Check(System.Runtime.InteropServices.Marshal.SizeOf<SkillCommand>()==20&&System.Runtime.InteropServices.Marshal.SizeOf<SkillState>()==64&&System.Runtime.InteropServices.Marshal.SizeOf<SkillEvent>()==56,"skill packet sizes");
            var root=new GameObject("Skill preview");SceneManager.MoveGameObjectToScene(root,preview);root.SetActive(false);
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),root.transform);
            foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var camera in player.GetComponentsInChildren<Camera>(true))camera.enabled=false;
            root.SetActive(true);var animator=player.GetComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
            var cameraGo=new GameObject("Preview camera");SceneManager.MoveGameObjectToScene(cameraGo,preview);var cam=cameraGo.AddComponent<Camera>();cam.scene=preview;cam.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(preview);
            cam.transform.position=new Vector3(5,4,-7);cam.transform.LookAt(new Vector3(0,1,1));cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.045f,.055f,.075f);cam.nearClipPlane=.1f;
            var light=new GameObject("Preview light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,preview);light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,-30,0);light.intensity=2;
            foreach(var name in new[]{"SkillE","SkillQ"}){
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/PlayerDefense/Animations/Skills/{name}.anim");
                Check(clip!=null&&clip.isHumanMotion&&clip.length>0,"humanoid skill clip");Check(AnimationUtility.GetAnimationEvents(clip).Length==0,"no basic attack events in skill");
                Check(animator.HasState(0,Animator.StringToHash(name)),"skill state wired to shared player controller");
                animator.Play(name,0,.2f);animator.Update(0);Check(animator.GetCurrentAnimatorStateInfo(0).IsName(name),"actual skill state evaluates");
                CombatContentBuilder.CaptureCamera(cam,Path.GetFullPath($"Artifacts/TrapsQE/{name}-windup.png"));
                animator.Play(name,0,.45f);animator.Update(0);CombatContentBuilder.CaptureCamera(cam,Path.GetFullPath($"Artifacts/TrapsQE/{name}-strike.png"));
            }
            var trap=new GameObject("Trap preview");SceneManager.MoveGameObjectToScene(trap,preview);
            var spikes=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("EContent/Spikes"),trap.transform);spikes.transform.localScale=new Vector3(2,1,2);
            Bounds bounds=spikes.GetComponentsInChildren<Renderer>().First().bounds;foreach(var r in spikes.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
            Check(Mathf.Abs(bounds.size.x-4)<.2f&&Mathf.Abs(bounds.size.z-4)<.2f,"spike model footprint is 4 by 4");
            player.SetActive(false);cam.transform.position=new Vector3(5,5,-6);cam.transform.LookAt(Vector3.zero);CombatContentBuilder.CaptureCamera(cam,Path.GetFullPath("Artifacts/TrapsQE/spikes-4x4.png"));
            Check(TeamPalette.Get(0)!=TeamPalette.Get(1),"previous team palette preserved");
        }finally{EditorSceneManager.ClosePreviewScene(preview);}
        var after=EditorSceneManager.GetSceneManagerSetup();
        Check(setups.Length==after.Length&&setups.Zip(after,(a,b)=>a.path==b.path&&a.isActive==b.isActive&&a.isLoaded==b.isLoaded).All(v=>v),"open scene setup preserved");
        File.WriteAllText("Artifacts/TrapsQE/editor-validation.txt",$"PASS {checks} editor preview checks; actual humanoid clips sampled and screenshots saved. Open scenes and Play Mode preserved. Not a live multiplayer test.");
    }
}
