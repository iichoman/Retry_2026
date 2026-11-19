using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SkillValidation
{
    const string Key="Retry.TrapsQE";
    static IEnumerator routine;static int frame=-1,checks;static double began;static string failure;
    static SkillValidation(){EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;}
    public static void Start(){
        if(!Application.isBatchMode)throw new Exception("Isolated batch fixture only");
        Directory.CreateDirectory("Artifacts/TrapsQE");File.WriteAllText("Artifacts/TrapsQE/runtime.txt","RUNNING");
        SkillContentBuilder.Build();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Debug.Log("QE PASS "+text);}
    static void Mode(PlayModeStateChange mode){if(!SessionState.GetBool(Key,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;routine=Run();}
        if(mode==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(failure==null?0:1);}
    }
    static void Tick(){if(routine==null||!EditorApplication.isPlaying||frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup-began>90)throw new Exception("timeout");if(routine.MoveNext())return;}
        catch(Exception e){failure=e.ToString();Debug.LogException(e);}
        routine=null;File.WriteAllText("Artifacts/TrapsQE/runtime.txt",failure??$"PASS {checks} actual-prefab skill/trap checks; no live server session.");EditorApplication.ExitPlaymode();
    }
    static IEnumerator Run(){
        Check(Marshal.SizeOf<SkillCommand>()==20&&Marshal.SizeOf<SkillState>()==64&&Marshal.SizeOf<SkillEvent>()==56,"wire layouts match server");
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.localScale=new Vector3(70,.2f,70);ground.transform.position=Vector3.down*.1f;
        var camera=new GameObject("Test camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(9,10,-10);camera.transform.LookAt(new Vector3(0,0,3));camera.farClipPlane=120;
        var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,25,0);RenderSettings.ambientLight=Color.gray;
        var holder=new GameObject("Fixture");holder.SetActive(false);var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=b is Player_Attack||b is Weapon_Sword||b is WeaponHitbox||b is Player_State;
        holder.SetActive(true);player.GetComponent<Player_Attack>().enabled=false;player.GetComponent<CharacterController>().enabled=false;
        var animator=player.GetComponent<Animator>();var view=player.AddComponent<PlayerSkillView>();
        Check(animator.HasState(0,Animator.StringToHash("SkillE"))&&animator.HasState(0,Animator.StringToHash("SkillQ")),"real controller contains dedicated skill states");
        foreach(var name in new[]{"SkillE","SkillQ"}){var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/PlayerDefense/Animations/Skills/{name}.anim");Check(clip!=null&&clip.isHumanMotion&&AnimationUtility.GetAnimationEvents(clip).Length==0,name+" humanoid motion without basic attack events");}
        var ev=new SkillEvent{version=1,entityId=1,serial=1,skill=2,phase=1,dz=1,remaining=1.2f};view.Begin(ev);
        yield return null;Check(view.IsCasting&&animator.GetCurrentAnimatorStateInfo(0).IsName("SkillQ"),"Q windup samples its motion");
        CombatContentBuilder.CaptureCamera(camera,Path.GetFullPath("Artifacts/TrapsQE/Q-charge.png"));
        ev.remaining=1.2f;view.Begin(ev);Check(view.IsCasting,"duplicate cast does not cancel animation");
        float until=Time.unscaledTime+1.3f;while(Time.unscaledTime<until)yield return null;
        Check(!view.IsCasting,"Q finishes without lingering action lock");
        ev.serial=2;ev.skill=1;ev.remaining=.63f;view.Begin(ev);yield return null;Check(animator.GetCurrentAnimatorStateInfo(0).IsName("SkillE"),"E motion plays");
        view.Cancel();Check(!PlayerSkillView.Busy(player),"cancel releases skill lock");
        var effect=SkillEffect.CreateWave();ev.x=0;ev.y=0;ev.z=3;ev.remaining=1;effect.Flight(ev);yield return null;
        CombatContentBuilder.CaptureCamera(camera,Path.GetFullPath("Artifacts/TrapsQE/E-wave.png"));
        var trap=new GameObject("Spikes fixture").AddComponent<EnvironmentTrapView>();
        trap.Apply(new EnvironmentState{version=1,id=1,kind=1,state=2,serial=1,x=-5,y=0,z=0,dz=1,length=4,width=4,deadline=ServerClock.NowMs+500});yield return null;
        Check(trap.transform.Find("Visuals/Pressure plate").localScale.x==4,"spike plate doubles both dimensions");
        CombatContentBuilder.CaptureCamera(camera,Path.GetFullPath("Artifacts/TrapsQE/traps.png"));
        var arrow=new GameObject("Arrow fixture").AddComponent<EnvironmentTrapView>();
        arrow.Apply(new EnvironmentState{version=1,id=2,kind=2,state=2,serial=1,x=0,y=0,z=0,dx=1,length=20,width=.8f,deadline=ServerClock.NowMs+650});yield return null;
        var emitter=arrow.transform.Find("Visuals").GetChild(0);Check(emitter.localPosition.x>20,"emitter is placed on distant wall");
        var arrowObject=arrow.transform.Find("Visuals/Arrow");Check(arrowObject.gameObject.activeSelf,"long-distance arrow remains visible during flight");
        float old=arrowObject.localPosition.x;yield return null;Check(arrowObject.localPosition.x<old,"arrow travels from wall towards plate");
        Cursor.lockState=CursorLockMode.Locked;camera.transform.rotation=Quaternion.Euler(20,90,0);
        Check(Vector3.Dot(SkillClient.AimDirection(player.transform,camera),Vector3.right)>.99f,"locked mouse aims along camera yaw");
    }
}
