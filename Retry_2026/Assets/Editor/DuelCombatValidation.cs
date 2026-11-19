using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputKey = UnityEngine.InputSystem.Key;

[InitializeOnLoad]
public static class DuelCombatValidation
{
    const string Key="Retry.DuelCombatTest", Output="Artifacts/DuelCombat";
    static GameObject player,target;
    static Camera camera;
    static Player_Attack attack;
    static PlayerDefenseController defense;
    static Player_LockOnSystem locked;
    static Defalult_Input input;
    static Player_Movement movement;
    static CharacterController capsule;
    static Animator animator;
    static PlayerStamina stamina;
    static Keyboard keyboard;
    static Mouse mouse;
    static InputActionAsset actions;
    static IEnumerator routine;
    static float resume;
    static int lastFrame;
    static double began;
    static string error;
    static bool priorBackground;
    static DuelCombatValidation() { EditorApplication.playModeStateChanged+=Mode; EditorApplication.update+=Tick; }

    [MenuItem("Retry/Player/Validate Locked Combat")]
    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode");
        Directory.CreateDirectory(Output); File.WriteAllText(Output+"/checks.txt","RUNNING\n");
        SessionState.SetString(Key+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Editor/PlayerCombatValidation.unity");
        SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            error=null; lastFrame=-1; resume=0; began=EditorApplication.timeSinceStartup;
            priorBackground=Application.runInBackground; Application.runInBackground=true;
            Application.logMessageReceived+=Log;
            routine=Checks();
        }
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key,false);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".scene",""));
            if(Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key+".pass",false)?0:1);
        }
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || routine==null) return;
        try
        {
            if(EditorApplication.timeSinceStartup-began>90) throw new TimeoutException("Runtime validation timed out");
            if(error!=null) throw new Exception(error);
            if(lastFrame==Time.frameCount || Time.time<resume) return;
            lastFrame=Time.frameCount;
            if(!routine.MoveNext()) { Finish(null); return; }
            resume=Time.time+(routine.Current is float seconds?seconds:0);
        }
        catch(Exception ex) { Finish(ex); }
    }
    static void Finish(Exception ex)
    {
        routine=null; Application.logMessageReceived-=Log;
        if(actions!=null) { actions.Disable(); UnityEngine.Object.DestroyImmediate(actions); }
        if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if(mouse!=null && mouse.added) InputSystem.RemoveDevice(mouse);
        Online(false); Application.runInBackground=priorBackground;
        File.AppendAllText(Output+"/checks.txt",ex==null?"PASS all runtime checks. Offline fixture and local online-mode flag; no live server/multiplayer latency test.\n":"FAIL "+ex+"\n");
        SessionState.SetBool(Key+".pass",ex==null); EditorApplication.ExitPlaymode();
    }
    static void Log(string message,string stack,LogType type)
    { if(type==LogType.Exception || type==LogType.Assert || type==LogType.Error) error=message+"\n"+stack; }
    static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);
    static void Online(bool value)=>typeof(NetworkRuntime).GetProperty("IsOnline").GetSetMethod(true).Invoke(null,new object[]{value});
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); File.AppendAllText(Output+"/checks.txt","PASS "+name+"\n"); }
    static void Keys(params InputKey[] keys) { InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys)); InputSystem.Update(); }
    static void Click(bool held) { InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left,held)); InputSystem.Update(); }
    static void ResetActor()
    {
        Keys(); Click(false); defense.CancelDefense(); attack.CancelCurrentAttack();
        animator.Play("Blend Tree",0,0); animator.Update(0);
        capsule.enabled=false; player.transform.SetPositionAndRotation(new Vector3(0,.03f,0),Quaternion.identity); capsule.enabled=true;
        Set(defense,"nextDashAt",0f);
        typeof(PlayerStamina).GetProperty("Current").GetSetMethod(true).Invoke(stamina,new object[]{100});
        Set(stamina,"lockRegenUntil",Time.time+20);
        Physics.SyncTransforms(); capsule.Move(Vector3.down*.1f);
        target.transform.position=new Vector3(0,1,5); Physics.SyncTransforms();
    }
    static IEnumerator Checks()
    {
        Online(false);
        Check(UnityEngine.Object.FindObjectsByType<NetworkBootstrap>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0,"Fixture never connects to a session");
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=new Vector3(0,-.5f,0); floor.transform.localScale=new Vector3(80,1,80);
        camera=new GameObject("Combat preview").AddComponent<Camera>(); camera.tag="MainCamera";
        camera.transform.position=new Vector3(0,3,-7); camera.transform.rotation=Quaternion.Euler(12,0,0); camera.nearClipPlane=.1f;
        var light=new GameObject("Preview light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.8f; light.transform.rotation=Quaternion.Euler(45,-30,0);
        var holder=new GameObject("Inactive holder"); holder.SetActive(false);
        player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true))
            b.enabled=b is Defalult_Input || b is Player_Movement || b is Player_State || b is Player_Attack || b is Player_Animation
                || b is PlayerDefenseController || b is PlayerDefenseAnimation || b is PlayerDefensePose || b is Player_LockOnSystem
                || b is Weapon_Sword || b is Weapon_Gun || b is WeaponHitbox || b is WeaponSwingTrail;
        attack=player.GetComponent<Player_Attack>(); defense=player.GetComponent<PlayerDefenseController>();
        input=player.GetComponent<Defalult_Input>(); movement=player.GetComponent<Player_Movement>();
        locked=player.GetComponent<Player_LockOnSystem>(); capsule=player.GetComponent<CharacterController>(); animator=player.GetComponent<Animator>();
        Set(locked,"targetCamera",camera); Set(movement,"cameraTransform",camera.transform);
        holder.SetActive(true);
        player.GetComponent<UnityEngine.InputSystem.PlayerInput>().actions.Disable();
        stamina=player.AddComponent<PlayerStamina>(); stamina.BindPlayer(input);
        keyboard=InputSystem.AddDevice<Keyboard>(); mouse=InputSystem.AddDevice<Mouse>();
        actions=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
        actions.devices=new InputDevice[]{keyboard,mouse};
        var dash=actions.FindAction("Player/Dash",true); dash.performed+=input.OnJump; dash.canceled+=input.OnJump;
        var move=actions.FindAction("Player/Move",true); move.performed+=input.OnMove; move.canceled+=input.OnMove;
        var swing=actions.FindAction("Player/Attack",true); swing.performed+=input.OnAttack; swing.canceled+=input.OnAttack;
        actions.FindActionMap("Player",true).Enable();
        target=GameObject.CreatePrimitive(PrimitiveType.Capsule); target.name="Locked target"; target.layer=8;
        target.transform.position=new Vector3(0,1,5); target.AddComponent<Monster_State>().enabled=false;
        ResetActor(); yield return .2f;
        Check(capsule.isGrounded && attack.IsSwordEquipped,"Real prefab grounded with sword equipped");
        Check(locked.TryLockOn(),"Acquire visible target using real lock-on selector");
        player.transform.rotation=Quaternion.Euler(0,-90,0); Click(true); yield return .08f;
        Check(attack.ActiveAnimationComboIndex>0,"Actual mouse binding starts sword animation");
        Check(Vector3.Dot(player.transform.forward,Vector3.forward)>.999f,"Locked sword attacks target instead of previous facing");
        Click(false);
        for(int combo=1;combo<=4;combo++)
        {
            target.transform.position=new Vector3(combo,1,5); attack.NotifyAttackAnimationStarted(combo);
            var aim=Vector3.ProjectOnPlane(target.transform.position-player.transform.position,Vector3.up).normalized;
            Check(Vector3.Dot(player.transform.forward,aim)>.999f,"Combo "+combo+" commits target direction");
        }
        ResetActor(); yield return .12f;
        var directions=new[]{new[]{InputKey.W},new[]{InputKey.D},new[]{InputKey.S},new[]{InputKey.A},new[]{InputKey.W,InputKey.D},new[]{InputKey.S,InputKey.A}};
        foreach(var direction in directions)
        {
            ResetActor(); yield return .12f;
            Keys(direction); yield return .025f;
            var expected=movement.ResolveMoveDirection(input.Move).normalized;
            var before=player.transform.position;
            var pressed=new InputKey[direction.Length+1]; direction.CopyTo(pressed,0); pressed[direction.Length]=InputKey.Space;
            Keys(pressed); yield return .045f;
            Check(defense.IsDashing && Vector3.Dot(defense.DashDirection,expected)>.999f,"Locked Space roll follows "+string.Join("+",direction));
            Check(stamina.Current==80,"Roll spends exactly 20 stamina");
            var heading=player.transform.rotation; attack.AlignAttackToLockOn();
            Check(Quaternion.Angle(heading,player.transform.rotation)<.001f,"Attack aiming cannot rotate an active roll");
            yield return .08f;
            Check(Vector3.Dot(player.transform.position-before,expected)>.5f && locked.IsLockedOn,"Native controller moves while lock stays acquired");
            if(direction.Length==1 && direction[0]==InputKey.D)
                CombatContentBuilder.CaptureCamera(camera,Output+"/locked-roll.png");
        }
        ResetActor(); yield return .12f;
        player.transform.rotation=Quaternion.Euler(0,90,0); Keys(InputKey.Space); yield return .045f;
        Check(defense.IsDashing && Vector3.Dot(defense.DashDirection,Vector3.back)>.999f,"No-input locked roll retreats from target despite sideways actor facing");
        yield return .85f;
        Check(!defense.IsDashing,"Held Space does not repeat after cooldown");
        ResetActor(); yield return .12f;
        attack.NotifyAttackAnimationStarted(1); Keys(InputKey.Space); yield return .05f;
        Check(!defense.IsDashing,"Attack cannot be canceled during active swing by rolling");
        attack.CancelCurrentAttack(); yield return .04f;
        Check(defense.IsDashing,"Buffered Space starts roll once attack finishes");
        ResetActor(); yield return .12f;
        input.SetInputMode(PlayerInputMode.UI); Keys(InputKey.Space); yield return .04f;
        Check(!defense.IsBusy,"UI suppresses roll input"); input.SetInputMode(PlayerInputMode.Gameplay); Keys();
        yield return .24f; Check(!defense.IsBusy,"UI exit does not replay a stale roll");
        ResetActor(); yield return .12f;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(1,1,0); wall.transform.localScale=new Vector3(.2f,2,8); Physics.SyncTransforms();
        Keys(InputKey.D,InputKey.Space); yield return .13f;
        Check(player.transform.position.x<.9f && !defense.IsBusy,"Locked roll stops at native wall collision"); wall.SetActive(false);
        ResetActor(); yield return .12f;
        Online(true); Keys(InputKey.D,InputKey.Space); yield return .045f;
        Check(defense.IsDashing && defense.DashSpeed<=Mathf.Min(10,movement.SprintSpeed),"Online roll preserves existing server-compatible speed cap");
        Check(!defense.CanAvoidOfflineMelee && !defense.TryDefendMelee(target),"Online roll never intercepts damage or claims dodge success");
        Online(false); ResetActor(); yield return .12f;
        locked.ClearLockOn(); player.transform.rotation=Quaternion.Euler(0,70,0); attack.NotifyAttackAnimationStarted(1);
        Check(Quaternion.Angle(player.transform.rotation,Quaternion.Euler(0,70,0))<.001f,"Unlocked attack retains original facing");
        ResetActor(); yield return .12f;
        var catalog=CombatVfxCatalog.Load();
        var cameraPosition=camera.transform.position; var cameraRotation=camera.transform.rotation;
        camera.transform.position=new Vector3(2,1.6f,-1.6f); camera.transform.LookAt(new Vector3(0,.6f,1));
        capsule.enabled=false; player.transform.position=new Vector3(-4,0,0); capsule.enabled=true;
        foreach(var kind in new[]{CombatVfxKind.DodgeDust,CombatVfxKind.SwordHit})
        {
            var effect=PooledVfxSpawner.Play(kind,new Vector3(0,kind==CombatVfxKind.SwordHit?1f:.08f,1),Quaternion.identity,1,731);
            Check(effect!=null && effect.gameObject.activeSelf,"Catalog spawns "+kind);
            foreach(var r in effect.GetComponentsInChildren<Renderer>())
                Check(r.sharedMaterial!=null && r.sharedMaterial.shader.isSupported,"Supported effect material: "+r.name);
            yield return .05f;
            bool particles=false; foreach(var ps in effect.Particles) particles|=ps.particleCount>0;
            Check(particles,"Particles actually emit for "+kind);
            CombatContentBuilder.CaptureCamera(camera,Output+"/"+kind+".png");
            yield return .7f;
            Check(!effect.gameObject.activeSelf,"Effect lifetime returns "+kind+" to pool");
            var again=PooledVfxSpawner.Play(kind,Vector3.zero,Quaternion.identity,1,731);
            Check(ReferenceEquals(effect,again),"Reuses pooled "+kind);
            PooledVfxSpawner.StopOwner(731); Check(!again.gameObject.activeSelf,"Owner cancellation clears effect");
        }
        Online(true);
        var registry=new GameObject("Confirmed monster impact fixture").AddComponent<RemoteMonsterRegistry>();
        registry.MonsterPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Monster/Monster1.prefab");
        registry.OnEnterView(new MonsterEnterView {monsterId=831,monsterKind=(int)MonsterKind.NORMAL,posZ=1,hp=100,maxHp=100});
        yield return .05f; Physics.SyncTransforms();
        var body=registry.GetComponentInChildren<Monster_State>().GetComponent<Collider>();
        registry.OnHpChanged(831,100,0,(int)WeaponKind.SWORD);
        bool ActiveImpact() {
            foreach(var fx in UnityEngine.Object.FindObjectsByType<PooledCombatEffect>(FindObjectsSortMode.None))
                if(fx.name.StartsWith("SwordHit") && fx.gameObject.activeSelf) return true;
            return false;
        }
        Check(!ActiveImpact(),"Zero damage does not create monster impact");
        foreach(var kind in new[]{WeaponKind.SWORD,WeaponKind.BIG_SWORD})
        {
            registry.OnHpChanged(831,kind==WeaponKind.SWORD?90:80,10,(int)kind);
            Check(ActiveImpact(),"Confirmed monster damage spawns impact for "+kind);
            foreach(var fx in UnityEngine.Object.FindObjectsByType<PooledCombatEffect>(FindObjectsSortMode.None))
                if(fx.name.StartsWith("SwordHit") && fx.gameObject.activeSelf)
                    Check(Vector3.Distance(body.ClosestPoint(fx.transform.position),fx.transform.position)>.04f,"Impact starts outside opaque monster body");
            yield return .05f;
            CombatContentBuilder.CaptureCamera(camera,Output+"/MonsterImpact_"+kind+".png");
            yield return .6f;
            Check(!ActiveImpact(),"Monster impact expires");
        }
        Online(false);
        var hitbox=player.GetComponentInChildren<WeaponHitbox>();
        typeof(WeaponHitbox).GetMethod("PlayHitEffect",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(hitbox,new object[]{body});
        Check(ActiveImpact(),"Offline collider contact emits the shared impact");
        foreach(var fx in UnityEngine.Object.FindObjectsByType<PooledCombatEffect>(FindObjectsSortMode.None))
            if(fx.name.StartsWith("SwordHit") && fx.gameObject.activeSelf)
                Check(Vector3.Distance(body.ClosestPoint(fx.transform.position),fx.transform.position)>.01f,"Offline impact is outside body and points outward");
        yield return .6f;
        UnityEngine.Object.Destroy(registry.gameObject);
        camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation); ResetActor(); yield return .12f;
        var trail=player.GetComponentInChildren<WeaponSwingTrail>();
        Check(trail!=null,"Sword trail connected on real prefab");
        Online(true); // No self attack broadcast is needed for a cosmetic local ribbon.
        trail.BeginTrail();
        for(int i=0;i<6;i++) { player.transform.rotation=Quaternion.Euler(0,i*12,0); yield return .02f; }
        Check(trail.GetComponent<MeshFilter>().sharedMesh.vertexCount>=4,"Sword ribbon builds a visible mesh");
        CombatContentBuilder.CaptureCamera(camera,Output+"/SwordTrail.png");
        trail.EndTrail(); yield return .25f;
        Check(!trail.GetComponent<MeshRenderer>().enabled,"Sword ribbon expires after attack");
        Online(false);
    }
}
