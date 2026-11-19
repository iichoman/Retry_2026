using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerCombatAnimationValidation
{
    const string Key="Retry.PlayerCombatAnimation.Validation";
    static PlayerCombatAnimationValidation() {EditorApplication.playModeStateChanged+=Mode;}
    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode");
        // Unity's play-mode start scene preserves the user's current unsaved editor scenes.
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        const string path="Assets/Editor/PlayerCombatValidation.unity";
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        EditorSceneManager.SaveScene(scene,path); EditorSceneManager.CloseScene(scene,true);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        SessionState.SetBool(Key,true);
        EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            try {Validate(); File.WriteAllText("Artifacts/PlayerCombatAnimation/runtime.txt","PASS: real prefab PlayMode four sword attacks and recovery, damage reaction and recovery, death interruption/hold, remote hit/death events, unchanged gameplay root. No live networking.\n"); BeginCapture();}
            catch(Exception ex) {File.WriteAllText("Artifacts/PlayerCombatAnimation/runtime.txt","FAIL\n"+ex); Debug.LogException(ex); EditorApplication.ExitPlaymode();}
        }
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key,false);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        }
    }
    static void Check(bool value,string message) {if(!value) throw new Exception(message); File.AppendAllText("Artifacts/PlayerCombatAnimation/checks.txt","PASS "+message+"\n");}
    static void Validate()
    {
        File.WriteAllText("Artifacts/PlayerCombatAnimation/checks.txt","");
        var holder=new GameObject("Setup"); holder.SetActive(false);
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var component in go.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
        go.transform.SetParent(null); go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        var animator=go.GetComponent<Animator>(); animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
        var view=go.GetComponent<Player_Animation>(); var attack=go.GetComponent<Player_Attack>(); var health=go.GetComponent<Player_State>();
        var sword=go.GetComponentInChildren<Weapon_Sword>(true); sword.Initialize(attack); attack.EquipWeapon(sword.WeaponId);
        var update=typeof(Player_Animation).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
        void Tick(float seconds) {for(int i=0;i<Mathf.CeilToInt(seconds/.02f);i++){update.Invoke(view,null); animator.Update(.02f);}}
        for(int i=1;i<=4;i++)
        {
            sword.RequestAttack(); update.Invoke(view,null); animator.Update(.1f);
            Check(view.IsAttacking && attack.ActiveAnimationComboIndex==i,"Sword combo "+i+" enters attack state");
            Tick(1.25f); Check(!view.IsAttacking && attack.ActiveAnimationComboIndex==0,"Sword combo "+i+" returns to locomotion");
            Check(go.transform.position.sqrMagnitude<.00001f && Quaternion.Angle(go.transform.rotation,Quaternion.identity)<.01f,"Sword animation preserves gameplay root");
        }
        health.TakeDamage(10,null); Tick(.1f); Check(view.IsHit && health.IsHit,"Real damage starts populated hit animation");
        Tick(.7f); Check(!view.IsHit && !health.IsHit,"Hit animation completes and releases control");
        sword.RequestAttack(); Tick(.1f); health.TakeDamage(10000,null); Tick(.2f);
        Check(health.IsDead && attack.ActiveAnimationComboIndex==0 && animator.GetCurrentAnimatorStateInfo(0).IsName("Die"),"Death interrupts attack and enters death clip");
        Tick(2); Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Die"),"Death holds final pose instead of returning to idle");
        float time=animator.GetCurrentAnimatorStateInfo(0).normalizedTime; health.NetworkDie(); Tick(.1f);
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime>=time,"Repeated death does not restart animation");
        var registry=new GameObject("Remote fixture").AddComponent<RemotePlayerRegistry>();
        registry.RemotePlayerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RemotePlayerPrefab.prefab");
        registry.OnEnterView(new PlayerEnterView{clientId=77,hp=100});
        var remote=registry.GetComponentInChildren<Animator>();
        for(int i=1;i<=4;i++)
        {
            registry.OnAttackBroadcast(new PlayerAttackBroadcast{attackerId=77,weaponKind=(int)WeaponKind.SWORD,comboIndex=i}); remote.Update(.1f);
            Check(remote.GetCurrentAnimatorStateInfo(0).IsName("sword_attack_"+i),"Remote attack event resolves actual controller state "+i);
        }
        registry.OnHpChanged(77,90); remote.Update(.1f);
        Check(remote.GetCurrentAnimatorStateInfo(0).IsName("Hit"),"Remote server HP decrease plays hit");
        registry.OnPlayerDied(77); remote.Update(.2f);
        Check(remote.GetCurrentAnimatorStateInfo(0).IsName("Die"),"Remote server death plays Die state without requiring a trigger");
        ValidateMonsterHit();
        UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(registry.gameObject);
    }
    static void ValidateMonsterHit()
    {
        var monster=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Monster/Monster1.prefab"));
        try
        {
            foreach(var b in monster.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled=false;
            var nav=monster.GetComponent<UnityEngine.AI.NavMeshAgent>(); if(nav!=null) nav.enabled=false;
            var a=monster.GetComponent<Animator>(); a.applyRootMotion=false; a.cullingMode=AnimatorCullingMode.AlwaysAnimate; a.Rebind();
            Check(a.isHuman,"Monster avatar supports shared humanoid hit clip");
            a.Play("Locomotion",0,0); a.Update(.05f);
            var chest=a.GetBoneTransform(HumanBodyBones.Chest); var idle=chest.localRotation;
            var state=monster.GetComponent<Monster_State>(); var view=monster.GetComponent<Monster_Animation>();
            var update=typeof(Monster_Animation).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            state.NetworkSetMax(100,100); state.NetworkApplyHp(90);
            var origin=monster.transform.position;
            update.Invoke(view,null); a.Update(.12f);
            Check(a.GetCurrentAnimatorStateInfo(0).IsName("Hit") && a.GetCurrentAnimatorClipInfo(0).Length>0,"Server HP decrease plays populated monster Hit state");
            Check(Quaternion.Angle(idle,chest.localRotation)>1,"Monster retargeted chest visibly reacts");
            for(int i=0;i<35;i++) {update.Invoke(view,null); a.Update(.02f);}
            Check(!state.IsHit && a.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"),"Monster hit finishes and returns to locomotion");
            Check((monster.transform.position-origin).sqrMagnitude<.00001f,"Monster hit never moves authoritative root");
        }
        finally {UnityEngine.Object.DestroyImmediate(monster);}
    }
    static Animator captureAnimator;
    static Camera captureCamera;
    static int sampleIndex, sampleFrame;
    static bool sampleReady;
    static bool contactFailed;
    static readonly string[] StateNames={"sword_attack_1","sword_attack_2","sword_attack_3","sword_attack_4","Hit","Die"};
    static readonly string[] ClipNames={"Sword1","Sword2","Sword3","Sword4","PlayerHit","PlayerDeath"};
    static void BeginCapture()
    {
        Directory.CreateDirectory("Artifacts/SwordGrounding");
        File.WriteAllText("Artifacts/SwordGrounding/runtime.txt","RUNNING actual Animator grounded sword poses\n");
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
        foreach(var component in player.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
        foreach(var gun in player.GetComponentsInChildren<Weapon_Gun>(true)) gun.gameObject.SetActive(false);
        foreach(var particle in player.GetComponentsInChildren<ParticleSystem>(true)) particle.gameObject.SetActive(false);
        captureAnimator=player.GetComponent<Animator>(); captureAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate; captureAnimator.speed=0; captureAnimator.fireEvents=false;
        captureCamera=new GameObject("Capture camera").AddComponent<Camera>(); captureCamera.transform.position=new Vector3(2.5f,1.8f,3); captureCamera.transform.LookAt(new Vector3(0,.9f,0)); captureCamera.fieldOfView=40;
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name="Foot contact reference floor";
        ground.transform.position=new Vector3(0,-.05f,0); ground.transform.localScale=new Vector3(12,.1f,12);
        var light=new GameObject("Key light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(40,-30,0);
        sampleIndex=0; sampleReady=false; contactFailed=false; EditorApplication.update+=CaptureStep;
    }
    static void CaptureStep()
    {
        try
        {
            if(!EditorApplication.isPlaying || captureAnimator==null) {EditorApplication.update-=CaptureStep; return;}
            if(!sampleReady)
            {
                if(!captureAnimator.HasState(0,Animator.StringToHash(StateNames[sampleIndex/3]))) throw new Exception("Capture state missing");
                captureAnimator.Play(StateNames[sampleIndex/3],0,new[]{.25f,.5f,.95f}[sampleIndex%3]); captureAnimator.Update(0);
                sampleFrame=Time.frameCount; sampleReady=true; return;
            }
            if(Time.frameCount<=sampleFrame+1) return;
            CombatContentBuilder.CaptureCamera(captureCamera,"Artifacts/PlayerCombatAnimation/Runtime_"+ClipNames[sampleIndex/3]+"_"+new[]{25,50,95}[sampleIndex%3]+".png");
            if(sampleIndex<12)
            {
                float support=SwordGroundingBuilder.LowestFoot(captureAnimator);
                float raised=SwordGroundingBuilder.HigherFootContact(captureAnimator);
                File.AppendAllText("Artifacts/SwordGrounding/runtime.txt",$"{ClipNames[sampleIndex/3]} sample {sampleIndex%3}: support height={support:F4}, higher foot={raised:F4}\n");
                if(support<-.06f || support>.07f) contactFailed=true;
                // The free foot may step naturally; only the support foot must stay near ground.
            }
            sampleReady=false; sampleIndex++;
            if(sampleIndex==18) {if(contactFailed) throw new Exception("Support-height review failed; all captures saved for inspection."); File.AppendAllText("Artifacts/SwordGrounding/runtime.txt","PASS twelve sword support-height checks; native-frame captures completed. Bone measurements do not prove exact sole contact.\n"); EditorApplication.update-=CaptureStep; EditorApplication.ExitPlaymode();}
        }
        catch(Exception ex) {File.AppendAllText("Artifacts/SwordGrounding/runtime.txt","FAIL "+ex+"\n"); EditorApplication.update-=CaptureStep; Debug.LogException(ex); EditorApplication.ExitPlaymode();}
    }
}
