using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Isolated Play Mode fixture. Never connects to a server or saves user scenes.
[InitializeOnLoad]
public static class RevisionValidation
{
    const string Key="Retry.Revision1110";
    static IEnumerator routine;
    static double began;
    static string failure;
    static int frame=-1, checks;
    static RevisionValidation(){EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;}
    public static void Start()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Run this isolated fixture in batch mode.");
        Directory.CreateDirectory("Artifacts/Revision1110");
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
        var dungeon=UnityEngine.Object.FindFirstObjectByType<DungeonGenerator_ChunkMesh>();
        Check(dungeon!=null && dungeon.targetRoomCount==36 && dungeon.minLeafSize==48,"shipped scene uses increased room settings");
        typeof(RoomBlueprintBuilder).GetMethod("ValidateParity",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{dungeon});
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Debug.Log("REVISION PASS "+message);}
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;routine=Run();}
        if(mode==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(failure==null?0:1);}
    }
    static void Tick()
    {
        if(routine==null || !EditorApplication.isPlaying || frame==Time.frameCount)return;
        frame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup-began>90)throw new Exception("Fixture timed out");if(routine.MoveNext())return;}
        catch(Exception e){failure=e.ToString();Debug.LogException(e);}
        routine=null;File.WriteAllText("Artifacts/Revision1110/runtime.txt",failure??$"PASS {checks} runtime checks; isolated real-prefab fixture, no live multiplayer session.");
        EditorApplication.ExitPlaymode();
    }
    static IEnumerator Run()
    {
        Check(Marshal.SizeOf<PlayerPresentation>()==36 && Marshal.OffsetOf<PlayerPresentation>("dx").ToInt32()==28,"presentation wire layout");
        var shader=Resources.Load<Shader>("StormBoundary");Check(shader!=null && !ShaderUtil.ShaderHasError(shader),"storm shader imports without errors");
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(0,-.5f,0);ground.transform.localScale=new Vector3(100,1,100);
        var holder=new GameObject("Fixture");holder.SetActive(false);
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true))
            b.enabled=b is Defalult_Input || b is Player_Movement || b is Player_State || b is Player_Attack || b is PlayerDefenseController || b is PlayerDefenseAnimation || b is PlayerDefensePose || b is Weapon_Sword || b is WeaponHitbox;
        player.transform.position=new Vector3(0,.05f,0);holder.SetActive(true);
        var input=player.GetComponent<Defalult_Input>();input.SetInputMode(PlayerInputMode.Gameplay);
        var hardware=player.GetComponent<UnityEngine.InputSystem.PlayerInput>();if(hardware!=null)hardware.enabled=false;
        var defense=player.GetComponent<PlayerDefenseController>();var attack=player.GetComponent<Player_Attack>();
        var animator=player.GetComponent<Animator>();animator.applyRootMotion=false;
        for(int i=0;i<25;i++)yield return null;
        Check(player.GetComponent<CharacterController>().isGrounded,"real player grounded");
        attack.NotifyAttackAnimationStarted(1);attack.AnimEvent_BeginSwing();
        animator.Play("Base Layer.test.sword_attack_1",0,0.25f);animator.Update(0);
        Check(attack.ActiveAnimationComboIndex==1,"attack active before roll");
        Check(defense.TryStartDash(Vector3.right),"roll starts during attack");
        Check(attack.ActiveAnimationComboIndex==0,"roll cancels attack state and hit window");
        yield return null;
        Check(defense.IsDashing && player.GetComponent<PlayerDefenseAnimation>().IsPlaying,"roll animation is driven by defense");
        defense.CancelDefense();
        player.GetComponent<Player_Movement>().enabled=false;input.enabled=false;
        defense.SetRemotePresentation();
        var snapshot=new PlayerPresentation{version=1,entityId=2,team=1,action=4,serial=7,remaining=.4f,duration=.7f,dx=1};
        defense.ApplyRemoteDefense(snapshot);
        Check(defense.IsDashing && defense.ActionProgress>.4f,"remote roll enters at snapshot progress");
        float start=(float)typeof(PlayerDefenseController).GetField("startedAt",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(defense);
        snapshot.remaining=.7f;defense.ApplyRemoteDefense(snapshot);
        Check(start==(float)typeof(PlayerDefenseController).GetField("startedAt",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(defense),"duplicate snapshot does not restart roll");
        snapshot.serial=8;snapshot.action=5;snapshot.remaining=snapshot.duration=.5f;defense.ApplyRemoteDefense(snapshot);
        yield return null;
        Check(defense.IsParrying && !defense.IsDashing,"remote parry uses shared pose driver");
        snapshot.action=0;defense.ApplyRemoteDefense(snapshot);Check(!defense.IsBusy,"server cancellation stops remote action");
        var registry=new GameObject("Remote registry fixture").AddComponent<RemotePlayerRegistry>();
        registry.RemotePlayerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RemotePlayerPrefab.prefab");
        registry.OnEnterView(new PlayerEnterView{clientId=2,playerName="원격 플레이어",hp=100});
        snapshot.serial=9;snapshot.action=4;snapshot.remaining=snapshot.duration=.7f;registry.OnPresentation(snapshot);
        yield return null;
        var remote=registry.GetActor(2);
        Check(remote.GetComponent<PlayerDefenseController>().IsDashing && remote.GetComponent<PlayerDefenseAnimation>().IsPlaying,"actual remote prefab receives and animates roll");
        Check(!remote.GetComponent<Player_Movement>().enabled && !remote.GetComponent<Defalult_Input>().enabled,"remote animation never enables input or movement");
        snapshot.serial=10;snapshot.action=5;snapshot.remaining=snapshot.duration=.5f;registry.OnPresentation(snapshot);
        yield return null;
        Check(remote.GetComponent<PlayerDefenseController>().IsParrying && remote.GetComponent<PlayerDefensePose>().HasRequiredBones,"actual remote prefab binds parry bones");
        var colors=new System.Collections.Generic.HashSet<Color>();for(int team=0;team<10;team++)colors.Add(TeamPalette.Get(team));
        Check(colors.Count==10,"ten distinct team colors");
        var portrait=PlayerPortrait.Ensure(player.transform);yield return null;
        Check(portrait.GetComponentsInChildren<Light>(true).Length==0,"portrait creates no world light");
        var monster=GameObject.CreatePrimitive(PrimitiveType.Capsule);var original=monster.GetComponent<Renderer>().material.color;
        MonsterHitFlash.Play(monster);Check(monster.GetComponent<Renderer>().material.color!=original,"ordinary monster flashes on hit");
        float until=Time.unscaledTime+.2f;while(Time.unscaledTime<until)yield return null;
        Check(monster.GetComponent<Renderer>().material.color==original,"monster restores original color");
        var portal=GameObject.CreatePrimitive(PrimitiveType.Cube);portal.AddComponent<ExitPortal>();yield return null;
        Check(portal.GetComponent<Renderer>().shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"portal casts no shadow");
    }
}
