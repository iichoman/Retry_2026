using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Short actual PlayMode native lifecycle test, using explicit server-packet fixtures, no network login.</summary>
[InitializeOnLoad]
public static class CombatVfxRuntimeValidation
{
    private const string Key = "Retry.CombatVfxRuntimeValidation";
    private const int MonsterId = 900001;
    private static GameObject stage;
    private static BossVfxController boss;
    private static Camera camera;
    private static CombatVfxCatalog catalog;
    private static BossTelegraphData current;
    private static double began;
    private static int step;
    private static readonly List<string> checks = new();
    private static readonly List<string> errors = new();
    private static string Output => Path.GetFullPath("Artifacts/CombatVfx");

    static CombatVfxRuntimeValidation()
    {
        EditorApplication.playModeStateChanged += OnModeChanged;
        EditorApplication.update += Tick;
    }
    [MenuItem("Tools/Retry/Combat/Run VFX PlayMode Validation")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("VFX validation requires edit mode.");
        Directory.CreateDirectory(Output);
        SessionState.SetBool(Key, true);
        File.WriteAllText(Output + "/runtime-validation.txt", "RUNNING: PlayMode with server event fixtures; no live networking.");
        EditorApplication.EnterPlaymode();
    }
    private static void OnModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            try { Setup(); } catch (Exception ex) { Finish(ex); }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Application.logMessageReceived -= OnLog;
            if (stage != null) UnityEngine.Object.DestroyImmediate(stage);
        }
    }
    private static void Setup()
    {
        checks.Clear(); errors.Clear(); step = 0;
        // These changes exist only in the PlayMode scene clone and are discarded on exit.
        for (int i = 0; i < SceneManager.sceneCount; ++i)
            foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
        catalog = CombatVfxCatalog.Load();
        if (catalog == null || catalog.BossPrefab == null) throw new InvalidOperationException("Generated VFX catalog missing.");
        ValidateBasicServer();
        stage = new GameObject("Combat VFX Runtime Validation");
        boss = UnityEngine.Object.Instantiate(catalog.BossPrefab, stage.transform).GetComponent<BossVfxController>();
        boss.transform.position = Vector3.zero;
        var state = boss.GetComponent<Monster_State>(); if (state != null) state.NetworkSetMax(300,300);
        ServerClock.Reset(); ServerClock.Observe(100000);
        boss.Initialize(MonsterId,300,300);
        boss.SetRoomBounds(new Bounds(Vector3.zero,new Vector3(13,20,13)));
        boss.SetPresentationVisible(true);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Runtime Test Floor"; floor.transform.SetParent(stage.transform);
        floor.transform.position = new Vector3(0,-0.13f,0); floor.transform.localScale = new Vector3(15,0.2f,15);
        floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/CombatContent/Materials/Preview_Floor.mat");
        var lightObject = new GameObject("Runtime Test Light"); lightObject.transform.SetParent(stage.transform);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f; light.transform.rotation = Quaternion.Euler(50,-35,0);
        var cameraObject = new GameObject("Runtime Test Camera"); cameraObject.transform.SetParent(stage.transform); camera = cameraObject.AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(-7,6.8f,10.5f); camera.transform.LookAt(new Vector3(0,0.95f,0)); camera.fieldOfView = 43;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f,0.045f,0.07f);
        began = EditorApplication.timeSinceStartup;
        Application.logMessageReceived += OnLog;
        checks.Add("Entered actual PlayMode; existing scene roots disabled only in runtime clone. No login/socket action issued.");
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || stage == null) return;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - began;
            if (elapsed > 12) throw new TimeoutException("VFX runtime test did not complete within 12 seconds.");
            if (current.pattern == 2 && ServerClock.NowMs >= current.resolveTimeMs && ServerClock.NowMs < current.endTimeMs)
                boss.transform.position = Vector3.Lerp(Vector3.zero,new Vector3(current.targetX,0,current.targetZ),(float)(ServerClock.NowMs-current.resolveTimeMs)/(current.endTimeMs-current.resolveTimeMs));
            switch (step)
            {
                case 0 when elapsed > 0.2:
                    current = Warning(1,1,1,3.2f,1100); boss.Telegraph(current); step++; break;
                case 1 when elapsed > 0.5:
                    Assert(WarningCount()==1,"Phase1 slam creates exactly one telegraph.");
                    boss.Telegraph(current); Assert(WarningCount()==1,"Duplicate telegraph does not create another view.");
                    boss.SetPresentationVisible(false); Assert(WarningCount()==0,"Room exit hides telegraph.");
                    boss.SetPresentationVisible(true); Assert(WarningCount()==1,"Room reentry restores remaining warning.");
                    Capture("Runtime_Phase1_Slam.png"); step++; break;
                case 2 when elapsed > 1.4:
                    boss.Resolve(Impact(current)); Assert(WarningCount()==0,"Slam resolve removes telegraph.");
                    boss.Resolve(Impact(current)); Assert(OwnedEffects()==1,"Duplicate resolve does not replay impact.");
                    step++; break;
                case 3 when elapsed > 1.65:
                    boss.ApplyState(new BossStateData{monsterId=MonsterId,phase=2,hp=140,maxHp=300,roomId=1,serverTimeMs=ServerClock.NowMs});
                    current=Warning(2,3,2,5.2f,900); boss.Telegraph(current); step++; break;
                case 4 when elapsed > 2.1:
                    Assert(boss.PhaseAura.isPlaying,"Phase2 persistent aura runs."); Capture("Runtime_Phase2_Wave.png"); step++; break;
                case 5 when elapsed > 2.65:
                    boss.Resolve(Impact(current)); current=Warning(3,3,2,5.2f,650); boss.Telegraph(current); step++; break;
                case 6 when elapsed > 3.4:
                    boss.Resolve(Impact(current)); current=Warning(4,2,2,0.85f,800,650); current.targetX=-3.5f; current.targetZ=1.0f; boss.Telegraph(current); step++; break;
                case 7 when elapsed > 3.9:
                    Capture("Runtime_Charge_Warning.png"); step++; break;
                case 8 when elapsed > 4.5:
                    Assert(boss.ChargeTrail.emitting,"Charge trail emits only during packet movement interval.");
                    Capture("Runtime_Charge_Trail.png"); step++; break;
                case 9 when elapsed > 4.95:
                    var impact=Impact(current); impact.posX=current.targetX; impact.posZ=current.targetZ; boss.Resolve(impact);
                    Assert(!boss.ChargeTrail.emitting,"Charge completion clears trail.");
                    boss.OnHit(50); int count=OwnedEffects(); boss.OnHit(50); Assert(OwnedEffects()==count,"Repeated hit flash is rate limited."); step++; break;
                case 10 when elapsed > 5.2:
                    current=Warning(5,1,2,3.2f,1000); boss.Telegraph(current); boss.OnDeath();
                    boss.GetComponent<Monster_State>().NetworkDie();
                    Assert(WarningCount()==0 && !boss.ChargeTrail.emitting && !boss.PhaseAura.isPlaying,"Death clears pending warning, trail and phase aura.");
                    Assert(OwnedEffects()==1,"Death cancels prior owner effects and leaves only finite death effect.");
                    var stale=Warning(6,1,2,3.2f,1000); boss.Telegraph(stale); Assert(WarningCount()==0,"Dead boss cannot replay a new telegraph.");
                    Assert(UnityEngine.Object.FindObjectsByType<LootVfxController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Length==0,"Boss death never creates fake loot before LOOT_SPAWN.");
                    step++; break;
                case 11 when elapsed > 5.7:
                    Assert(boss.GetComponent<Monster_State>().IsDead,"Existing Monster_State and death animation receive server death.");
                    Capture("Runtime_Boss_Death.png"); step++; break;
                case 12 when elapsed > 6.7:
                    Assert(OwnedEffects()==0,"All finite boss VFX returned to pool after lifetime.");
                    if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n",errors));
                    Finish(null); break;
            }
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static void ValidateBasicServer()
    {
        var fixture = new GameObject("Basic server boss fixture");
        fixture.SetActive(false); // Never run Bootstrap.Awake or dungeon generation/socket setup.
        var bootstrap = fixture.AddComponent<NetworkBootstrap>();
        var dungeon = fixture.AddComponent<DungeonGenerator_ChunkMesh>();
        var player = new GameObject("Fixture player"); player.transform.SetParent(fixture.transform);
        typeof(NetworkBootstrap).GetProperty("LocalPlayer").GetSetMethod(true).Invoke(bootstrap,new object[]{player});
        var rooms = (List<Room>)typeof(DungeonGenerator_ChunkMesh).GetField("rooms",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(dungeon);
        var room = new Room { id=17, type=RoomType.Boss, bounds=new BoundsInt(-10,0,-10,20,1,20) };
        room.floorTiles.Add(Vector3Int.zero); rooms.Add(room);
        player.transform.position = dungeon.TileToWorldCenter(Vector3Int.zero);
        var registry = fixture.AddComponent<RemoteMonsterRegistry>();
        registry.BossPrefab=catalog.BossPrefab; registry.Initialize(bootstrap,dungeon);
        var position=player.transform.position;
        var enter=new MonsterEnterView {monsterId=900088,monsterKind=(int)MonsterKind.BOSS,hp=4000,maxHp=4000,posX=position.x,posY=position.y,posZ=position.z};
        registry.OnEnterView(enter);
        var view=fixture.GetComponentInChildren<BossVfxController>(true);
        Assert(view!=null && view.IsPresentationVisible,"Basic MONSTER_ENTER_VIEW displays boss in same room without BOSS_STATE.");
        Assert(!view.HasPhaseState && view.DisplayedHp==4000,"Basic server HP is displayed without fabricated phase state.");
        view.SetHp(1900,4000);
        Assert(!view.HasPhaseState && view.DisplayedPhase==1,"HP below half does not invent a server phase transition.");
        registry.OnEnterView(enter);
        Assert(fixture.GetComponentsInChildren<BossVfxController>(true).Length==1,"Duplicate enter keeps exactly one boss.");
        foreach(var box in view.GetComponentsInChildren<MonsterHitbox>(true)) Assert(!box.enabled,"Network boss local damage hitbox disabled.");
        var ai=view.GetComponent<Monster>(); Assert(ai==null || !ai.enabled,"Network boss local AI disabled.");
        player.transform.position=new Vector3(10000,0,10000);
        typeof(RemoteMonsterRegistry).GetMethod("Update",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(registry,null);
        Assert(!view.IsPresentationVisible,"Basic server boss HUD hides outside its room.");
        registry.OnDied(900088);
        Assert(view.DisplayedHp==0,"Basic server death clears displayed boss HP.");
        UnityEngine.Object.DestroyImmediate(fixture);
    }
    private static BossTelegraphData Warning(int sequence,int pattern,int phase,float radius,long prepare,long movement=0)
    {
        long now=ServerClock.NowMs;
        return new BossTelegraphData { monsterId=MonsterId,sequence=sequence,pattern=pattern,phase=phase,radius=radius,startTimeMs=now,resolveTimeMs=now+prepare,endTimeMs=now+prepare+movement,serverTimeMs=now };
    }
    private static BossResolveData Impact(BossTelegraphData warning) => new BossResolveData {monsterId=MonsterId,sequence=warning.sequence,pattern=warning.pattern,phase=warning.phase,radius=warning.radius,serverTimeMs=ServerClock.NowMs};
    private static int WarningCount() => UnityEngine.Object.FindObjectsByType<BossTelegraphView>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Length;
    private static int OwnedEffects()
    {
        int count=0; foreach(var effect in UnityEngine.Object.FindObjectsByType<PooledCombatEffect>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)) if(effect.Owner==MonsterId) count++;
        return count;
    }
    private static void Capture(string name) => CombatContentBuilder.CaptureCamera(camera,Output+"/"+name);
    private static void Assert(bool value,string message) { if(!value) throw new InvalidOperationException(message); checks.Add("PASS: "+message); }
    private static void OnLog(string message,string stack,LogType type)
    {
        if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert) errors.Add(message+"\n"+stack);
    }
    private static void Finish(Exception error)
    {
        Application.logMessageReceived-=OnLog;
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output+"/runtime-validation.txt",(error==null?"PASS":"FAIL")+"\nActual Unity PlayMode; synthetic authoritative event fixtures, not a live server or latency integration test.\n"+string.Join("\n",checks)+(error==null?"":"\n"+error));
        SessionState.SetBool(Key,false); stage=null;
        if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
