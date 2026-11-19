using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using InputKey=UnityEngine.InputSystem.Key;

[InitializeOnLoad]
public static class ExpeditionUIValidation
{
    const string Key = "Retry.ExpeditionUITest", Output = "Artifacts/ExpeditionUI";
    static IEnumerator routine;
    static double began;
    static float resume;
    static int frame;
    static string error;
    static NetworkBootstrap bootstrap;
    static bool background;
    static System.Net.Sockets.TcpListener listener;
    static System.Net.Sockets.TcpClient peer;
    static UnityEngine.InputSystem.Keyboard keyboard;
    static ExpeditionUIValidation()
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
        peer?.Close();listener?.Stop();
        if(keyboard!=null)UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
        Application.runInBackground = background;
        File.AppendAllText(Output + "/runtime.txt",
                           ex == null ? "PASS UI fixture + real SessionClient requests; snapshots simulated, not full v3 gameplay.\n"
                                      : "FAIL " + ex + "\n");
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        File.AppendAllText(Output + "/runtime.txt", "PASS " + message + "\n");
    }
    static void Press(params InputKey[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    static ABCommand Command()
    {
        var stream=peer.GetStream();stream.ReadTimeout=1000;
        var bytes=new byte[28];int got=0;while(got<bytes.Length)got+=stream.Read(bytes,got,bytes.Length-got);
        Check(BitConverter.ToInt32(bytes,0)==ABWire.Command&&BitConverter.ToInt32(bytes,4)==20,"real 200/20 byte command envelope");
        return PacketIO.BytesToStruct<ABCommand>(bytes,8);
    }
    static IEnumerator Run()
    {
        var camera=new GameObject("UI Preview Camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.018f,.023f,.032f);
        var player=new GameObject("UI Test Owner");var inventory=player.AddComponent<PlayerInventory>();var input=player.AddComponent<Defalult_Input>();
        bootstrap=new GameObject("UI Test Bootstrap").AddComponent<NetworkBootstrap>();bootstrap.enabled=false;bootstrap.DebugUI.enabled=false;
        bootstrap.Identity.SetLocalClientId(11);bootstrap.Identity.IsConnectedToSession=true;
        typeof(NetworkBootstrap).GetProperty("LocalPlayer").GetSetMethod(true).Invoke(bootstrap,new object[]{player});
        var legacy=new GameObject("Inventory Adapter").AddComponent<InventoryUI>();legacy.RebindOwner(inventory,null);
        var controller=new GameObject("UI Controller").AddComponent<GameUIController>();
        Set(controller,"inventoryUI",legacy);Set(controller,"playerInput",input);Set(controller,"bootstrap",bootstrap);
        listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Check(bootstrap.Session.ConnectAndAuth("127.0.0.1",port,923,11),"fixture TCP connection (no existing server touched)");
        peer=listener.AcceptTcpClient();var auth=new byte[8];peer.GetStream().Read(auth,0,8);
        var ab=bootstrap.GetComponent<ABClient>();
        var snapshot=new ABSnapshot{version=1,hp=80,maxHp=100,stamina=100,maxStamina=100,weapon=ItemHash.Of("sword_001"),level=2,xp=25,required=120,damage=1,range=1,attackSpeed=1,movement=1};
        ab.Receive(PacketIO.StructToBytes(snapshot));
        string[] ids={"sword_001","flame_sword","frost_sword","beam_sword","health_potion","vigor_potion_t1","gold_coin","rare_gem","boss_core","life_amulet_t1","vigor_ring_t1","guardian_sigil_t1"};
        var sync=new InventorySyncData{entryCount=ids.Length,entries=new ItemStack[32]};
        for(int i=0;i<ids.Length;i++){int n=i==4?3:1;sync.entries[i]=new ItemStack{itemHash=ItemHash.Of(ids[i]),count=n};sync.totalCount+=n;}
        inventory.SetServerOwned();Check(inventory.ApplyServerSnapshot(sync,ItemCatalog.Load()),"controlled inventory snapshot binds real catalog");
        var ui=ExpeditionUI.Active;ui.Bind(inventory,null);keyboard=InputSystem.AddDevice<Keyboard>();yield return .2f;
        Press(InputKey.I);yield return .1f;Press();yield return .1f;
        Check(ui.InventoryOpen&&!input.IsGameplayInputEnabled,"I opens inventory through existing input + UI controller");
        foreach(var item in ItemCatalog.Load().Items)Check(item.Icon!=null,"registered icon "+item.ItemId);
        foreach(var text in ui.GetComponentsInChildren<Text>(true))Check(text.font!=null&&text.font.material!=null,"runtime font "+text.name);
        ui.Select(1);ui.UseSelected();yield return .1f;var command=Command();
        Check(command.action==1&&command.itemHash==ItemHash.Of("flame_sword"),"inventory equip sends selected item hash");
        Check(ab.State.weapon==ItemHash.Of("sword_001"),"equipment does not predict approval");
        snapshot.sequence=command.sequence;snapshot.weapon=command.itemHash;ab.Receive(PacketIO.StructToBytes(snapshot));
        ui.Select(4);Press(InputKey.Digit2);yield return .1f;Press();yield return .1f;
        Check(ui.QuickHash(1)==ItemHash.Of("health_potion")&&ui.QuickHash(0)==0,"digit 2 assigns selected consumable and removes duplicate binding");
        ui.Select(5);ui.AssignQuick(0);ui.Select(11);ui.AssignQuick(3);ui.Select(1);
        Capture(ui.GetComponent<Canvas>(),camera,"inventory",1920,1080);Capture(ui.GetComponent<Canvas>(),camera,"inventory-720p",1280,720);
        Press(InputKey.I);yield return .1f;Press();yield return .1f;
        Check(!ui.InventoryOpen&&input.IsGameplayInputEnabled,"I closes inventory and restores gameplay");
        Press(InputKey.Digit2);yield return .1f;Press();yield return .1f;command=Command();
        Check(command.action==2&&command.itemHash==ItemHash.Of("health_potion"),"digit 2 sends use request");
        Check(inventory.Count(ItemCatalog.Load().Resolve("health_potion"))==3,"use does not decrement before inventory sync");
        sync.entries[4].count=2;sync.totalCount--;inventory.ApplyServerSnapshot(sync,ItemCatalog.Load());
        Check(inventory.Count(ItemCatalog.Load().Resolve("health_potion"))==2,"authoritative inventory sync decrements once");
        int before=ab.LastRequestSequence;input.SetInputMode(PlayerInputMode.UI);Press(InputKey.Digit2);yield return .1f;Press();yield return .1f;
        Check(ab.LastRequestSequence==before,"other UI modes block quick item use");input.SetInputMode(PlayerInputMode.Gameplay);
        Press(InputKey.Digit4);yield return .1f;Press();yield return .1f;Check(ab.LastRequestSequence==before,"unequipped active item cannot be used");
        snapshot.sequence=command.sequence;snapshot.offer=42;snapshot.choice0=0;snapshot.choice1=2;snapshot.choice2=4;ab.Receive(PacketIO.StructToBytes(snapshot));
        Press(InputKey.Tab);yield return .1f;Press();yield return .1f;
        Check(ui.UpgradeOpen&&!input.IsGameplayInputEnabled,"Tab opens server-offered upgrades and blocks combat");
        Capture(ui.GetComponent<Canvas>(),camera,"upgrade",1920,1080);
        var choose=ui.transform.Find("Upgrade/Choice 1/Choose").GetComponent<Button>();choose.onClick.Invoke();yield return .1f;command=Command();
        Check(command.action==3&&command.itemHash==2&&command.offer==42,"upgrade card sends server choice and offer serial");
        Check(ui.UpgradeOpen&&!choose.interactable&&ab.State.damage==1,"pending selection locks buttons without predicting stats");
        int lockedSequence=ab.LastRequestSequence;ui.Choose(0);Check(ab.LastRequestSequence==lockedSequence,"duplicate upgrade click blocked while pending");
        snapshot.sequence=command.sequence;snapshot.result=1;ab.Receive(PacketIO.StructToBytes(snapshot));
        Check(ui.UpgradeOpen&&choose.interactable,"denied upgrade keeps offer and re-enables selection");
        choose.onClick.Invoke();yield return .1f;command=Command();snapshot.result=0;
        snapshot.sequence=command.sequence;snapshot.offer=0;snapshot.damage=1.08f;ab.Receive(PacketIO.StructToBytes(snapshot));yield return .1f;
        Check(!ui.UpgradeOpen&&input.IsGameplayInputEnabled,"accepted snapshot closes upgrade and restores input");
        Press(InputKey.F8);yield return .1f;Press();yield return .1f;Check(!ui.InventoryOpen&&!ui.UpgradeOpen,"F8 no longer opens old panel");
        Capture(ui.GetComponent<Canvas>(),camera,"quickslots",1920,1080);
        var loot=new GameObject("Loot Preview").AddComponent<ServerLootUI>();int requests=0;loot.Open(_=>requests++,()=>requests++,()=>{});
        loot.Refresh(new LootSpawnData{entryCount=3,entries=new[]{sync.entries[1],sync.entries[7],sync.entries[8]}},ItemCatalog.Load(),true,_=>false);
        Capture(loot.GetComponent<Canvas>(),camera,"loot",1920,1080);Check(requests==0,"rendering loot does not grant items");loot.Close();
        for(int i=0;i<4;i++){
            controller.SetInventoryOpen(true);ui.Select(4);Press((InputKey)((int)InputKey.Digit1+i));yield return .08f;Press();yield return .08f;
            Check(ui.QuickHash(i)==ItemHash.Of("health_potion"),"assign key "+(i+1));
            controller.SetInventoryOpen(false);Press((InputKey)((int)InputKey.Digit1+i));yield return .08f;Press();yield return .08f;command=Command();
            Check(command.action==2&&command.itemHash==ItemHash.Of("health_potion"),"use key "+(i+1));
        }
        bootstrap.Session.Disconnect();yield return .2f;Check(!ab.Supported&&!ui.UpgradeOpen&&!ui.InventoryOpen,"disconnect clears authoritative UI");
    }
    static void Capture(Canvas canvas,Camera camera,string name,int width,int height)
    {
        var rt=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        camera.targetTexture=rt;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.enabled=false;
        canvas.scaleFactor=Mathf.Min(width/1920f,height/1080f);Canvas.ForceUpdateCanvases();
        for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
        RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;scaler.enabled=true;camera.targetTexture=null;RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);
    }
}
