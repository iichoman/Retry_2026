using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BossClientCompatibilityValidation
{
    const string Key="Retry.BossClientCompatibility", Report="Artifacts/BossClientCompatibility.txt";
    static BossClientCompatibilityValidation() { EditorApplication.playModeStateChanged+=Mode; }
    public static void Start()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Use Edit Mode");
        SessionState.SetString(Key+".scene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Editor/PlayerCombatValidation.unity");
        SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            File.WriteAllText(Report,"");
            try {Run(); File.AppendAllText(Report,"PASS all checks. Synthetic received packets; no live server or multiplayer latency test.\n");}
            catch(Exception ex) {File.AppendAllText(Report,"FAIL "+ex+"\n"); Debug.LogException(ex);}
            finally {Online(false); EditorApplication.ExitPlaymode();}
        }
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key,false);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".scene",""));
        }
    }
    static void Check(bool ok,string label) {if(!ok) throw new Exception(label); File.AppendAllText(Report,"PASS "+label+"\n");}
    static void Field(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);
    static void Property(object obj,string name,object value)=>obj.GetType().GetProperty(name).GetSetMethod(true).Invoke(obj,new[]{value});
    static void Online(bool value)=>typeof(NetworkRuntime).GetProperty("IsOnline").GetSetMethod(true).Invoke(null,new object[]{value});
    static void Packet<T>(NetworkBootstrap bs,PacketType type,T body) where T:struct
        =>typeof(NetworkBootstrap).GetMethod("OnSessionPacket",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bs,new object[]{type,PacketIO.StructToBytes(body)});
    static void BeginTeleport(NetworkBootstrap bs,RoomType type)
        =>typeof(NetworkBootstrap).GetMethod("BeginDebugTeleport",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bs,new object[]{type});
    static void Run()
    {
        var holder=new GameObject("Inactive bootstrap fixture"); holder.SetActive(false);
        var bs=holder.AddComponent<NetworkBootstrap>(); bs.enabled=false; // Awake/Start never run: no sockets or auto login.
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled=false;
        player.transform.SetParent(null); var attack=player.GetComponent<Player_Attack>();
        Property(bs,"LocalPlayer",player);
        Check(attack.IsSwordEquipped,"Actual player starts with sword profile");
        Check(!bs.HasServerEquipment && bs.TryGetAttackWeaponKind(attack,out int kind) && kind==(int)WeaponKind.SWORD,"Legacy v1/v2 sends starting sword intent without equipment sync");
        Check(!bs.HasServerEquipment,"Compatibility path does not fabricate server equipment ownership");
        Field(bs,"allowLegacyStartingSword",false);
        Check(!bs.TryGetAttackWeaponKind(attack,out _),"Strict equipment-sync mode still blocks unsynchronized attacks");
        Property(bs,"HasServerEquipment",true); Property(bs,"ServerWeaponKind",(int)WeaponKind.GUN);
        Check(bs.TryGetAttackWeaponKind(attack,out kind) && kind==(int)WeaponKind.GUN,"Actual server-approved weapon takes precedence");
        Property(bs,"HasServerEquipment",false); Field(bs,"allowLegacyStartingSword",true);
        attack.EquipWeapon("test_gun");
        Check(!attack.IsSwordEquipped && !bs.TryGetAttackWeaponKind(attack,out _),"Legacy mode does not authorize arbitrary acquired weapons");
        attack.EquipWeapon("test_sword");

        var gen=new GameObject("Seed/layout fixture").AddComponent<DungeonGenerator_ChunkMesh>(); gen.enabled=false; gen.generateOnStart=false; gen.randomizeSeedOnGenerate=false;
        gen.mapSize=750; gen.bossRoomCount=1; gen.centerMapAtOrigin=true;
        string Fingerprint()=>string.Join(";",gen.Rooms.Select(r=>$"{r.id}:{r.type}:{r.bounds}:{r.floorTiles.Count}"));
        gen.seed=1895648185; gen.GenerateDataForValidation(); string first=Fingerprint();
        gen.GenerateDataForValidation(); Check(first==Fingerprint(),"Same server seed regenerates identical room layout");
        gen.seed=1895652410; gen.GenerateDataForValidation(); Check(first!=Fingerprint(),"Different observed session seeds generate different layouts");
        Check(gen.Rooms.Count(r=>r.type==RoomType.Boss)==1,"Exactly one boss room in generated layout");
        var room=gen.Rooms.First(r=>r.type==RoomType.Boss); var tile=room.floorTiles.First();
        var destination=gen.TileToWorldCenter(tile)+Vector3.up;
        Check(DebugCheatKeys.MatchesRoom(gen,RoomType.Boss,destination),"Boss destination resolves centered world offset correctly");
        Check(!DebugCheatKeys.MatchesRoom(gen,RoomType.Boss,new Vector3(float.NaN,1,0)),"Non-finite teleport destination rejected");
        Field(bs,"dungeonGeneratorObject",gen.gameObject); Field(bs,"firstSelfMoveApplied",true);
        var id=player.AddComponent<LocalIdentity>(); id.SetLocalClientId(41); Property(bs,"Identity",id);
        var old=gen.TileToWorldCenter(gen.Rooms.First(r=>r.type!=RoomType.Boss).floorTiles.First())+Vector3.up;
        player.transform.position=old; BeginTeleport(bs,RoomType.Boss);
        Packet(bs,PacketType.PLAYER_MOVE,new PlayerMove{clientId=41,posX=old.x,posY=old.y,posZ=old.z});
        Check(bs.IsAwaitingDebugTeleport && player.transform.position==old,"Old ordinary movement packet cannot consume pending boss teleport");
        Packet(bs,PacketType.PLAYER_MOVE,new PlayerMove{clientId=41,posX=destination.x,posY=destination.y,posZ=destination.z});
        Check(!bs.IsAwaitingDebugTeleport && Vector3.Distance(player.transform.position,destination)<.001f,"Matching server destination applies exact position and completes request");
        BeginTeleport(bs,RoomType.Boss); Field(bs,"teleportExpiresAt",Time.unscaledTime-1);
        player.transform.position=old;
        Packet(bs,PacketType.PLAYER_MOVE,new PlayerMove{clientId=41,posX=destination.x,posY=destination.y,posZ=destination.z});
        Check(player.transform.position==old,"Expired teleport does not snap to an unrelated late packet");

        Online(true);
        var registry=new GameObject("Remote boss fixture").AddComponent<RemoteMonsterRegistry>(); registry.enabled=false;
        registry.BossPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CombatContent/Prefabs/DungeonGuardian.prefab");
        registry.Initialize(bs,gen); Property(bs,"RemoteMonsters",registry);
        registry.OnEnterView(new MonsterEnterView{monsterId=81,monsterKind=(int)MonsterKind.BOSS,hp=900,maxHp=900,posX=destination.x,posY=destination.y,posZ=destination.z});
        var boss=registry.GetComponentInChildren<Monster_State>();
        Check(boss!=null && boss.CurrentHp==900 && boss.GetComponent<LocalBossController>()==null,"Online boss is presentation-only and starts at server HP");
        boss.TakeDamage(20,player); Check(boss.CurrentHp==900,"Local hit cannot decrement online boss HP");
        Packet(bs,PacketType.COMBAT_EVENT,new CombatEvent{attackerId=41,targetId=-81,damage=20,targetHpAfter=880,weaponKind=(int)WeaponKind.SWORD,comboIndex=1});
        Check(boss.CurrentHp==880,"Existing server COMBAT_EVENT decrements displayed boss HP");
        var roaming=destination+Vector3.right*150;
        registry.OnMove(new MonsterMove{monsterId=81,posX=roaming.x,posY=roaming.y,posZ=roaming.z});
        BeginTeleport(bs,RoomType.Boss);
        var beside=roaming+Vector3.right*3;
        Packet(bs,PacketType.PLAYER_MOVE,new PlayerMove{clientId=41,posX=beside.x,posY=beside.y,posZ=beside.z});
        Check(!bs.IsAwaitingDebugTeleport && Vector3.Distance(player.transform.position,beside)<.001f,"Server relocation beside a known roaming boss is accepted outside its original room");
        Check(System.Runtime.InteropServices.Marshal.SizeOf<PlayerAttackRequest>()==40,"Existing attack packet remains 40 bytes");
        Check(bs.Session==null && bs.Lobby==null,"No server connection or server file changes in fixture");
    }
}
