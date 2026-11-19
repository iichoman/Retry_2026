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
public static class ABRuntimeValidation
{
    const string Key = "Retry.ABTest", Output = "Artifacts/AB";
    static IEnumerator routine;
    static double began;
    static float resume;
    static int frame;
    static string error;
    static NetworkBootstrap bootstrap;
    static bool background;
    static ABRuntimeValidation()
    {
        EditorApplication.playModeStateChanged += Mode;
        EditorApplication.update += Tick;
    }
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Use Edit Mode");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/runtime.txt", "RUNNING\n");
        SessionState.SetString(Key + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene =
            AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Editor/PlayerCombatValidation.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Key, false))
            return;
        if (mode == PlayModeStateChange.EnteredPlayMode)
        {
            error = null;
            frame = -1;
            resume = 0;
            began = EditorApplication.timeSinceStartup;
            background = Application.runInBackground;
            Application.runInBackground = true;
            Application.logMessageReceived += Log;
            routine = Run();
        }
        if (mode == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".scene", ""));
        }
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            error = message + "\n" + stack;
    }
    static void Tick()
    {
        if (routine == null || !EditorApplication.isPlaying)
            return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 60)
                throw new TimeoutException();
            if (error != null)
                throw new Exception(error);
            if (frame == Time.frameCount || Time.time < resume)
                return;
            frame = Time.frameCount;
            if (!routine.MoveNext())
            {
                Finish(null);
                return;
            }
            resume = Time.time + (routine.Current is float delay ? delay : 0);
        }
        catch (Exception ex)
        {
            Finish(ex);
        }
    }
    static void Finish(Exception ex)
    {
        routine = null;
        Application.logMessageReceived -= Log;
        bootstrap?.Session?.Disconnect();
        Application.runInBackground = background;
        File.AppendAllText(Output + "/runtime.txt",
                           ex == null ? "PASS runtime + isolated live v3 session; VFX captures use cosmetic fixtures.\n"
                                      : "FAIL " + ex + "\n");
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        File.AppendAllText(Output + "/runtime.txt", "PASS " + message + "\n");
    }
    static IEnumerator Run()
    {
        var torch=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Map/torch.prefab"));
        var flame=torch.GetComponentInChildren<StylizedTorchFlame>();
        Check(flame!=null,"original authored torch prefab retains flame controller");
        var flameSettings=new SerializedObject(flame);
        var fireRoot=(Transform)flameSettings.FindProperty("flameRoot").objectReferenceValue;
        var torchCamera=new GameObject("Torch visible-camera fixture").AddComponent<Camera>();
        torchCamera.transform.position=fireRoot.position+new Vector3(0,.3f,-4);
        torchCamera.transform.LookAt(fireRoot.position+Vector3.up*.3f);
        torchCamera.clearFlags=CameraClearFlags.SolidColor;torchCamera.backgroundColor=new Color(.025f,.03f,.035f);
        yield return 1.2f;
        foreach(var field in new[]{"flameCore","flameGlow","sparks","smoke"}){
            var ps=flameSettings.FindProperty(field).objectReferenceValue as ParticleSystem;
            Check(ps!=null&&ps.isPlaying,"original torch particle playing: "+field);
            if(field=="flameCore"||field=="flameGlow")Check(ps.particleCount>0,"visible original flame particles: "+field);
        }
        Directory.CreateDirectory("Artifacts/E");CombatContentBuilder.CaptureCamera(torchCamera,"Artifacts/E/OriginalTorchPlayMode.png");
        UnityEngine.Object.Destroy(torchCamera.gameObject);UnityEngine.Object.Destroy(torch);
        Check(ItemCatalog.Load().ValidateCatalog(), "catalog IDs/hashes unique");
        foreach (var definition in ABItemDefinitions.All)
        {
            var item = ItemCatalog.Load().Resolve(definition.id);
            Check(item != null && item.ExtractionValue == definition.value &&
                      item.Description == definition.description,
                  "catalog " + definition.id);
        }
        var initialCamera = new GameObject("Fixture Main Camera").AddComponent<Camera>();
        initialCamera.tag = "MainCamera";
        var player = UnityEngine.Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
        player.GetComponent<Player_Movement>().enabled = false;
        player.GetComponent<Player_Camera_Controller>().enabled = false;
        var input = player.GetComponent<Defalult_Input>();
        input.SetInputMode(PlayerInputMode.UI);
        bootstrap = new GameObject("AB Test Bootstrap").AddComponent<NetworkBootstrap>();
        bootstrap.RemotePlayers.RemotePlayerPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RemotePlayerPrefab.prefab");
        bootstrap.RemoteMonsters.MonsterPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Monster/Monster1.prefab");
        bootstrap.Identity.SetLocalClientId(11);
        bootstrap.DebugUI.enabled=false;
        bootstrap.Identity.IsConnectedToSession=true;
        typeof(NetworkBootstrap)
            .GetProperty("LocalPlayer")
            .GetSetMethod(true)
            .Invoke(bootstrap, new object[] { player });
        bootstrap.Inventory.Initialize(bootstrap, player.GetComponent<PlayerInventory>());
        Check(bootstrap.Session.ConnectAndAuth("127.0.0.1", 39101, 923, 11), "live C# SessionClient connection");
        var ab = bootstrap.GetComponent<ABClient>();
        for (int i = 0; i < 30 && !ab.Supported; i++)
            yield return .1f;
        Check(ab.Supported && ab.State.weapon == ItemHash.Of("sword_001"), "84-byte snapshot + starter equipped");
        Check(System.Runtime.InteropServices.Marshal.SizeOf<EnvironmentState>()==56 && System.Runtime.InteropServices.Marshal.OffsetOf<EnvironmentState>("deadline").ToInt32()==48,"E packet layout 56 bytes / deadline offset 48");
        Check(bootstrap.GetComponentsInChildren<EnvironmentTrapView>(true).Length>0,"live E trap snapshots instantiate views");
        Check(bootstrap.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Chest_")),"live E loot broadcasts instantiate chest assets");
        Check(bootstrap.Inventory.TotalItemCount == 4, "inventory sync starter sword and 3 potions");
        Check(player.GetComponent<PlayerEquipment>().GetEquippedItem(EquipmentSlotType.Weapon) != null,
              "approved equipment bound to player");
        Check(Marshal.SizeOf<PositionSyncState>()==32&&Marshal.OffsetOf<PositionSyncState>("inputTimestamp").ToInt32()==8&&Marshal.OffsetOf<PositionSyncState>("teleport").ToInt32()==28,"v3 position packet 204 layout");
        player.GetComponent<Player_Movement>().enabled=false;
        var positionSender=player.GetComponent<LocalPlayerInputSender>();if(positionSender==null)positionSender=player.AddComponent<LocalPlayerInputSender>();
        positionSender.Initialize(bootstrap);positionSender.enabled=false;
        positionSender.SendNow();yield return .15f;
        var acceptedOrigin=player.transform.position;
        var capsuleFixture=player.GetComponent<CharacterController>();capsuleFixture.enabled=false;
        player.transform.position+=Vector3.right*12;positionSender.SendNow();yield return .3f;
        Check(Vector2.Distance(new Vector2(player.transform.position.x,player.transform.position.z),new Vector2(acceptedOrigin.x,acceptedOrigin.z))<1.1f,"live server movement rejection reconciles local position");
        bootstrap.RequestDebugTeleportExit();yield return .35f;
        Check(!bootstrap.IsAwaitingDebugTeleport&&(player.transform.position-acceptedOrigin).sqrMagnitude>100,"live F9 server teleport applies dedicated confirmation");
        var teleported=player.transform.position;positionSender.SendNow();yield return .2f;
        Check((player.transform.position-teleported).sqrMagnitude<.01f,"post-teleport input preserves server destination");
        var stale=new PositionSyncState{version=1,serial=1,teleport=1,x=0,y=0,z=0};
        positionSender.ReceivePosition(PacketIO.StructToBytes(stale));Check((player.transform.position-teleported).sqrMagnitude<.01f,"stale position serial cannot undo teleport");
        capsuleFixture.enabled=true;
        ab.Request(1, ItemHash.Of("flame_sword"));
        yield return .35f;
        Check(ab.State.result != 0 && ab.State.weapon == ItemHash.Of("sword_001"),
              "unowned weapon rejected without prediction");
        ab.Request(2, ItemHash.Of("health_potion"));
        yield return .35f;
        Check(ab.State.result == 0 && bootstrap.Inventory.TotalItemCount == 3,
              "consumable removes exactly one on authoritative inventory sync");
        ab.Request(3, 0, 99999);
        yield return .3f;
        Check(ab.State.result != 0, "forged level offer rejected over C# socket");
        bootstrap.Session.OnPacketReceived += (type,bytes)=>{if(type==PacketType.TIME_SYNC_RESULT){var sync=PacketIO.BytesToStruct<TimeSyncResult>(bytes,0);File.AppendAllText(Output+"/runtime.txt",$"CLOCK local={ServerClock.LocalMs} echo={sync.clientTimeMs} server={sync.serverTimeMs}\n");}};
        bootstrap.Session.SendTimeSync();
        for(int i=0;i<20&&!(bool)typeof(ServerClock).GetField("initialized",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);i++)yield return .1f;
        Check((bool)typeof(ServerClock).GetField("initialized",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null),"v3 clock response synchronized telegraph clock");
        // Stop polling for controlled visual fixtures; they never send gameplay grants.
        bootstrap.enabled = false;
        var actor = player.GetComponent<ABActorView>();
        Check(actor != null, "28-byte actor snapshot creates view");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = player.transform.position - Vector3.up * .13f;
        floor.transform.localScale = new Vector3(12, .2f, 12);
        var light = new GameObject("AB Key Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var camera = new GameObject("AB Camera").AddComponent<Camera>();
        camera.fieldOfView = 34;
        camera.transform.position = player.transform.position + new Vector3(2.6f, 1.8f, 3.7f);
        camera.transform.LookAt(player.transform.position + Vector3.up);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .035f, .05f);
        Directory.CreateDirectory("Artifacts/E");
        var trapView=new GameObject("E visual fixture").AddComponent<EnvironmentTrapView>();
        var treasure=bootstrap.GetComponentsInChildren<LootVfxController>().FirstOrDefault(v=>v.RewardChest);
        Check(treasure!=null&&treasure.Beam!=null&&treasure.Beam.GetPosition(1).y==2.8f&&treasure.Aura!=null&&treasure.Aura.main.maxParticles==18,"live reward chest keeps gold beacon and bounded motes regardless of item rarity");
        var treasureCameraPosition=camera.transform.position;var treasureCameraRotation=camera.transform.rotation;
        camera.transform.position=treasure.transform.position+new Vector3(3,2.6f,4);camera.transform.LookAt(treasure.transform.position+Vector3.up);yield return .8f;
        CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/TreasureChestPlayMode.png");
        camera.transform.SetPositionAndRotation(treasureCameraPosition,treasureCameraRotation);
        var pos=player.transform.position+Vector3.right*3;
        var trapState=new EnvironmentState{version=1,id=9999,kind=1,state=1,serial=1,x=pos.x,y=pos.y,z=pos.z,dz=1,length=2,width=2,deadline=ServerClock.NowMs+1000};
        trapView.Apply(trapState);yield return .1f;
        var area=trapView.transform.Find("Visuals/Danger area").GetComponent<Renderer>();
        Check(!area.enabled&&area.transform.localScale.x==2&&area.transform.localScale.z==2,"hidden trap warning preserves server footprint");
        var old=trapState;old.serial=0;old.state=0;trapView.Apply(old);yield return .05f;Check(!area.enabled,"old trap serial ignored");
        var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;
        camera.transform.position=pos+new Vector3(3,4,4);camera.transform.LookAt(pos);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/SpikesWarning.png");
        trapState.serial=2;trapState.state=2;trapState.deadline=ServerClock.NowMs+500;trapView.Apply(trapState);yield return .05f;
        CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/SpikesActive.png");
        trapState.serial=3;trapState.deadline=ServerClock.NowMs-1;trapView.Apply(trapState);yield return .05f;
        Check(!area.enabled&&trapView.transform.Find("Visuals/Spikes(Clone)").localScale.y<.1f,"expired attack never replays");
        UnityEngine.Object.Destroy(trapView.gameObject);
        trapView=new GameObject("E arrow visual fixture").AddComponent<EnvironmentTrapView>();
        trapState.kind=2;trapState.state=1;trapState.length=6;trapState.width=.8f;trapState.deadline=ServerClock.NowMs+1200;trapView.Apply(trapState);yield return .05f;
        Check(Mathf.Abs(trapView.transform.Find("Visuals/Danger area").localScale.z-7.4f)<.001f,"arrow warning includes wall emitter to path end");
        camera.transform.position=pos+new Vector3(6,7,9);camera.transform.LookAt(pos+Vector3.forward*3);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/ArrowWarning.png");
        trapState.serial++;trapState.state=2;trapState.deadline=ServerClock.NowMs+400;trapView.Apply(trapState);yield return .05f;
        var arrowVisual=trapView.transform.Find("Visuals/Arrow");Check(arrowVisual.gameObject.activeSelf&&arrowVisual.localPosition.z>0,"arrow projectile visible during server active state");CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/ArrowActive.png");
        UnityEngine.Object.Destroy(trapView.gameObject);
        bootstrap.RemoteLoot.OnSpawn(new LootSpawnData{lootId=900000,sourceMonsterId=EnvironmentClient.RewardChestSource-1,posX=pos.x,posY=pos.y,posZ=pos.z,entryCount=1,entries=new[]{new ItemStack{itemHash=ItemHash.Of("rare_gem"),count=2}}});
        var chest=bootstrap.RemoteLoot.transform.Find("Chest_900000");Check(chest!=null&&chest.GetComponent<BoxCollider>().enabled,"gold chest prefab has active collision");
        var rewardVfx=chest.GetComponent<LootVfxController>();Check(rewardVfx.RewardChest&&Mathf.Abs(rewardVfx.Beam.GetPosition(1).y-2.8f)<.01f&&rewardVfx.Aura.main.maxParticles==18,"reward chest gold beam and bounded particle density");
        camera.transform.position=pos+new Vector3(3,2,4);camera.transform.LookAt(pos+Vector3.up*.5f);yield return .1f;CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/RewardChest.png");
        bootstrap.RemoteLoot.OnRemoved(new LootRemovedData{lootId=900000});Check(!chest.GetComponent<BoxCollider>().enabled,"empty chest collider disabled immediately");yield return .8f;Check(chest==null,"empty chest visual cleaned up");
        camera.transform.SetPositionAndRotation(savedPosition,savedRotation);
        foreach (string id in new[] { "flame_sword", "frost_sword", "beam_sword" })
        {
            actor.Apply(new ABActorState { version = 1, entityId = 11, weapon = ItemHash.Of(id), range = 1.3f,
                                           attackSpeed = 1,
                                           statusMask = id == "flame_sword"   ? 1
                                                        : id == "frost_sword" ? 2
                                                                              : 8 });
            var probe=new GameObject("Weapon scale probe");actor.ScaleWeapon(probe.transform);actor.ScaleWeapon(probe.transform);
            Check(Vector3.Distance(probe.transform.localScale,Vector3.one*1.3f)<.001f,"shared weapon scale does not compound");UnityEngine.Object.Destroy(probe);
            yield return .45f;
            var lines = player.GetComponentsInChildren<LineRenderer>();
            Check(lines.Length > 0, "element weapon instantiated " + id);
            foreach (var renderer in player.GetComponentsInChildren<Renderer>())
                foreach (var mat in renderer.sharedMaterials)
                    Check(mat != null && mat.shader != null && mat.shader.isSupported, "material " + renderer.name);
            CombatContentBuilder.CaptureCamera(camera, Output + "/" + id + ".png");
        }
        actor.Apply(new ABActorState { version = 1, entityId = 11, weapon = ItemHash.Of("sword_001"), range = 1,
                                       attackSpeed = 1 });
        yield return .25f;
        foreach (string name in new[] { "Burn(Clone)", "Freeze(Clone)", "Poison(Clone)" })
            Check(actor.transform.Find(name) != null && !actor.transform.Find(name).gameObject.activeSelf,
                  "cleared status " + name);
        ExpeditionUI.Ensure().Bind(player.GetComponent<PlayerInventory>(),player.GetComponent<PlayerEquipment>());
        ExpeditionUI.Active.SetInventoryVisible(true);
        yield return .2f;
        ScreenCapture.CaptureScreenshot(Output + "/inventory.png");
        yield return .3f;
        ExtractionContentBuild.Validate();
        Check(bootstrap.GetComponent<StormClient>().State.version==1,"live storm state 205 received");
        ExpeditionUI.Active.SetInventoryVisible(false);yield return .1f;
        ScreenCapture.CaptureScreenshot("Artifacts/C/StormHud.png");yield return .2f;
        bootstrap.Session.Disconnect();yield return .1f;
        ExpeditionUI.Active.SetInventoryVisible(false);
        var economy=bootstrap.GetComponent<EconomyClient>();economy.enabled=false;
        var entries=new EconomyEntry[96];entries[0]=new EconomyEntry{hash=ItemHash.Of("flame_sword"),count=1,sell=500,buy=0};entries[1]=new EconomyEntry{hash=ItemHash.Of("health_potion"),count=5,sell=30,buy=60};entries[2]=new EconomyEntry{hash=ItemHash.Of("rare_gem"),count=3,sell=150};
        economy.Receive(PacketIO.StructToBytes(new EconomySnapshot{version=2,sequence=99,credits=1240,count=3,weapon=ItemHash.Of("flame_sword"),entries=entries}));
        EconomyClient.Open();yield return .2f;ScreenCapture.CaptureScreenshot("Artifacts/C/Stash.png");yield return .25f;
        Check(economy.IsOpen&&economy.State.credits==1240,"stash UI renders server snapshot with icons and prices");economy.Close();
        GameOptions.Open();yield return .2f;ScreenCapture.CaptureScreenshot("Artifacts/C/Options.png");yield return .25f;
        var options=bootstrap.GetComponent<GameOptions>();var apply=typeof(GameOptions).GetMethod("ApplyBlur",BindingFlags.NonPublic|BindingFlags.Instance);
        apply.Invoke(options,new object[]{true});yield return .1f;
        var motion=(UnityEngine.Rendering.Universal.MotionBlur)typeof(GameOptions).GetField("motion",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(options);
        Resources.Load<UnityEngine.Rendering.VolumeProfile>("Presentation/CameraOptions").TryGet<UnityEngine.Rendering.Universal.MotionBlur>(out var sourceBlur);
        Check(sourceBlur!=motion&&sourceBlur.intensity.value==0,"motion blur edits runtime clone only");
        Check(Mathf.Approximately(motion.intensity.value,GameOptions.BlurStrength)&&motion.mode.value==UnityEngine.Rendering.Universal.MotionBlurMode.CameraOnly,"URP camera-only blur applies and has bounded intensity");apply.Invoke(options,new object[]{PlayerPrefs.GetInt("options.blur",1)==1});
        typeof(GameOptions).GetMethod("Close",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(options,null);Check(!GameOptions.IsOpen,"options close restores overlay state");

        Check(!bootstrap.RemoteLoot.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Chest_")),"disconnect removes authoritative chest colliders and views");
    }
}
