using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LockOnCameraValidation
{
    const string Key="Retry.LockOnCameraTest";
    const string Output="Artifacts/LockOnCamera";
    static Camera previewCamera;
    static int previewFrame;
    static LockOnCameraValidation() { EditorApplication.playModeStateChanged+=Mode; }
    [MenuItem("Retry/Player/Validate Duel Camera")]
    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode");
        Directory.CreateDirectory(Output);
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Editor/PlayerCombatValidation.unity");
        SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            try { Run(); File.AppendAllText(Output+"/checks.txt","PASS all checks; no live networking\n"); previewFrame=Time.frameCount; EditorApplication.update+=Capture; }
            catch(Exception ex) { File.AppendAllText(Output+"/checks.txt","FAIL "+ex+"\n"); Debug.LogException(ex); EditorApplication.ExitPlaymode(); }
        }
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key,false);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        }
    }
    static void Capture()
    {
        if (!EditorApplication.isPlaying || previewCamera==null) {EditorApplication.update-=Capture; return;}
        if(Time.frameCount <= previewFrame+2) return;
        EditorApplication.update-=Capture;
        try {CombatContentBuilder.CaptureCamera(previewCamera,Output+"/duel.png");}
        finally {EditorApplication.ExitPlaymode();}
    }
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    static T Get<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    static void Call(object obj,string name)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
    static void Check(bool value,string label) {if(!value) throw new Exception(label); File.AppendAllText(Output+"/checks.txt","PASS "+label+"\n");}
    static void Run()
    {
        File.WriteAllText(Output+"/checks.txt","");
        var holder=new GameObject("Setup"); holder.SetActive(false);
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var component in player.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
        player.transform.SetParent(null); player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        var camera=new GameObject("Duel Camera").AddComponent<Camera>(); camera.aspect=16f/9; camera.fieldOfView=60; camera.nearClipPlane=.1f;
        camera.transform.position=new Vector3(0,3,-7); camera.transform.LookAt(new Vector3(0,1.2f,4));
        var controller=player.GetComponent<Player_Camera_Controller>(); var locked=player.GetComponent<Player_LockOnSystem>();
        Set(controller,"cameraTransform",camera.transform); Set(controller,"cameraAction",null); Set(controller,"input",null); Call(controller,"Start");
        Set(locked,"targetCamera",camera); Set(locked,"input",null);
        var target=GameObject.CreatePrimitive(PrimitiveType.Capsule); target.name="Target"; target.layer=8; target.transform.position=new Vector3(0,1,5);
        var health=target.AddComponent<Monster_State>(); health.enabled=false;
        void Frame() {Physics.SyncTransforms(); Call(locked,"Update"); Call(controller,"LateUpdate");}
        void Settle() {for(int i=0;i<240;i++)Frame();}
        Check(locked.TryLockOn(),"Acquire living visible target");
        foreach(float distance in new[]{2f,5f,12f,18f})
        {
            target.transform.position=new Vector3(0,1,distance); Settle();
            Vector3 a=camera.WorldToViewportPoint(player.transform.position+Vector3.up), b=camera.WorldToViewportPoint(locked.TargetAimPosition);
            Check(a.z>0 && b.z>0 && a.x>.02f && a.x<.98f && b.x>.02f && b.x<.98f && a.y>.02f && a.y<.98f && b.y>.02f && b.y<.98f,"Both actors framed at "+distance+" metres");
        }
        target.transform.position=new Vector3(0,1,5); Settle();
        for(int i=0;i<360;i++)
        {
            float angle=i*Mathf.Deg2Rad; target.transform.position=new Vector3(Mathf.Sin(angle)*4,1,Mathf.Cos(angle)*4);
            float before=Get<float>(controller,"yaw"); Frame();
            CheckYaw(controller,before);
        }
        Check(true,"Orbit rotation respects 130 degree/second cap");
        target.transform.position=new Vector3(0,1,.05f); Settle(); float closeYaw=Get<float>(controller,"yaw");
        target.transform.position=new Vector3(0,1,-.05f); Frame();
        Check(Mathf.Abs(Mathf.DeltaAngle(closeYaw,Get<float>(controller,"yaw")))<.001f,"Crossing at contact distance does not flip yaw");
        target.transform.position=new Vector3(0,1,5); Settle();
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="Wall"; wall.layer=10; wall.transform.localScale=new Vector3(30,8,.2f); wall.transform.position=new Vector3(0,2,-2);
        Frame(); Check(camera.transform.position.z> -1.8f,"New wall retracts camera immediately");
        wall.SetActive(false); float near=Vector3.Distance(camera.transform.position,player.transform.position); Frame();
        Check(Vector3.Distance(camera.transform.position,player.transform.position)<8,"Wall exit does not snap to full distance");
        Settle();
        wall.transform.position=new Vector3(0,2,2); wall.SetActive(true); Frame(); Check(locked.IsLockedOn,"Brief occlusion keeps lock");
        for(int i=0;i<240;i++)Frame(); Check(!locked.IsLockedOn,"Sustained wall occlusion releases lock");
        Check(!locked.TryLockOn(),"Cannot acquire through wall"); wall.SetActive(false); Physics.SyncTransforms(); Check(locked.TryLockOn(),"Reacquire after wall clears");
        target.transform.position=new Vector3(0,1,22); Frame(); Check(locked.IsLockedOn,"Release distance hysteresis avoids edge flicker");
        target.transform.position=new Vector3(0,1,25); Frame(); Check(!locked.IsLockedOn,"Release beyond 24 metres");
        target.transform.position=new Vector3(0,1,5); Physics.SyncTransforms(); Check(locked.TryLockOn(),"Reacquire for death test"); health.NetworkDie(); Frame(); Check(!locked.IsLockedOn,"Server-confirmed monster death releases lock");
        target.SetActive(false);
        var registry=new GameObject("Remote players").AddComponent<RemotePlayerRegistry>(); registry.enabled=false;
        registry.RemotePlayerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RemotePlayerPrefab.prefab");
        registry.OnEnterView(new PlayerEnterView{clientId=77,hp=100,posZ=5,playerName="CameraTest"}); Physics.SyncTransforms();
        Check(locked.TryLockOn(),"Remote player selectable with disabled CharacterController"); Settle();
        var light=new GameObject("Light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(45,-30,0);
        registry.OnPlayerDied(77); Frame(); Check(!locked.IsLockedOn,"Remote player death releases lock");
        Check(player.transform.position==Vector3.zero && Quaternion.Angle(player.transform.rotation,Quaternion.identity)<.001f,"Camera never modifies gameplay root or facing");
        Vector3 old=camera.transform.position; Frame(); Check(Vector3.Distance(old,camera.transform.position)<1,"Unlock blends back without a position snap");
        registry.OnLeaveView(77);
        registry.OnEnterView(new PlayerEnterView{clientId=78,hp=100,posZ=3,rotY=180,playerName="CameraPreview"});
        Physics.SyncTransforms(); Check(locked.TryLockOn(),"Preview reacquires living opponent"); Settle();
        foreach(var animator in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
        { animator.Play("Blend Tree",0,0); animator.Update(.1f); }
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.position=new Vector3(0,-.02f,0); ground.transform.localScale=Vector3.one*3;
        previewCamera=camera;
    }
    static void CheckYaw(Player_Camera_Controller controller,float before)
    {
        float change=Mathf.Abs(Mathf.DeltaAngle(before,Get<float>(controller,"yaw")));
        if(change>130*Time.deltaTime+.001f) throw new Exception("Yaw speed cap exceeded");
    }
}
