using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LocalBossValidation
{
    const string Key="Retry.LocalBoss.Validation";
    [Serializable] class Saved {public SceneSetup[] scenes;}
    static LocalBossController boss;
    static Player_State player;
    static double began;
    static int step, hp;
    static Camera camera;
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static LocalBossValidation() {EditorApplication.playModeStateChanged+=Mode; EditorApplication.update+=Tick;}
    public static void Start()
    {
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++) if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new Exception("Save scene before tests");
        SessionState.SetString(Key+".scenes",JsonUtility.ToJson(new Saved{scenes=EditorSceneManager.GetSceneManagerSetup()}));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Directory.CreateDirectory("Artifacts/LocalBoss"); File.WriteAllText("Artifacts/LocalBoss/runtime.txt","RUNNING");
        SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                step=0; began=EditorApplication.timeSinceStartup;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=new Vector3(0,-.5f,0); floor.transform.localScale=new Vector3(30,1,30);
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(4,2,0); wall.transform.localScale=new Vector3(.5f,4,10);
                player=new GameObject("Test Player").AddComponent<Player_State>(); player.transform.position=new Vector3(2,0,0);
                boss=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(LocalBossBuilder.PrefabPath)).GetComponent<LocalBossController>();
                camera=new GameObject("Camera").AddComponent<Camera>(); camera.transform.position=new Vector3(-10,12,-12); camera.transform.LookAt(Vector3.zero);
                var light=new GameObject("Light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(50,-30,0);
            }
            catch(Exception ex) {Finish(ex);}
        }
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key,false);
            var previous=JsonUtility.FromJson<Saved>(SessionState.GetString(Key+".scenes","{}"));
            if(previous.scenes!=null) EditorSceneManager.RestoreSceneManagerSetup(previous.scenes);
        }
    }
    static void Set(string name,object value)=>typeof(LocalBossController).GetField(name,Flags).SetValue(boss,value);
    static void Begin(int pattern)
    {
        Set("target",player); Set("players",new[]{player});
        typeof(LocalBossController).GetMethod("BeginPattern",Flags).Invoke(boss,new object[]{pattern});
        hp=player.CurrentHp; began=EditorApplication.timeSinceStartup;
    }
    static void Check(bool condition,string message) {if(!condition) throw new Exception(message); File.AppendAllText("Artifacts/LocalBoss/checks.txt","PASS "+message+"\n");}
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || boss==null) return;
        try
        {
            double elapsed=EditorApplication.timeSinceStartup-began;
            if(elapsed>12) throw new Exception("Local boss test timeout");
            if(step==0 && elapsed>.3)
            {
                File.WriteAllText("Artifacts/LocalBoss/checks.txt","");
                Check(boss.GetComponent<Monster_State>().Type==MonsterType.Boss,"Boss prefab stats configured");
                Begin(1); step=1;
            }
            else if(step==1 && elapsed>.6)
            {
                Check(player.CurrentHp==hp && boss.ActivePattern==1,"Slam warning precedes damage");
                CombatContentBuilder.CaptureCamera(camera,"Artifacts/LocalBoss/slam.png"); step=2;
            }
            else if(step==2 && elapsed>1.6)
            {
                Check(player.CurrentHp<hp,"Slam actually damages offline player");
                Begin(2); step=3;
            }
            else if(step==3 && elapsed>2.1)
            {
                Check(boss.transform.position.x<3 && boss.transform.position.x>0,"Charge moves and stops before wall");
                Check(player.CurrentHp<hp,"Charge path damages player");
                boss.GetComponent<Monster_State>().TakeDamage(2100,player.gameObject);
                Check(boss.Phase==2,"HP threshold enters phase 2");
                player.Heal(100); player.transform.position=boss.transform.position+Vector3.left*2;
                Set("nextPattern",3); Begin(3); Set("nextPattern",1); step=4;
            }
            else if(step==4 && elapsed>.5)
            {
                Check(player.CurrentHp==hp,"Phase 2 wave warns before damage");
                CombatContentBuilder.CaptureCamera(camera,"Artifacts/LocalBoss/phase2.png"); step=5;
            }
            else if(step==5 && elapsed>1.5)
            {
                Check(player.CurrentHp<hp,"Wave actually damages player");
                hp=player.CurrentHp; step=55;
            }
            else if(step==55 && elapsed>3.3)
            {
                Check(player.CurrentHp<hp,"Phase 2 repeats wave after a second warning");
                Begin(1); boss.GetComponent<Monster_State>().TakeDamage(10000,player.gameObject);
                Check(boss.Defeated && boss.ActivePattern==0,"Death cancels scheduled pattern");
                var loot=boss.GetComponent<LootContainer>();
                Check(loot.IsAvailable && loot.Slots.Any(s=>s.item!=null && s.item.ItemId=="boss_core" && s.count>=1),"Death guarantees lootable boss core");
                Check(loot.Slots.Any(s=>s.item!=null && s.item.ItemId=="sword_001") && !loot.Slots.Any(s=>s.item!=null && s.item.ItemId=="gun_001"),"Weapon rewards present");
                hp=player.CurrentHp; began=EditorApplication.timeSinceStartup; step=6;
            }
            else if(step==6 && elapsed>1.6)
            {
                Check(player.CurrentHp==hp,"No delayed damage after death");
                typeof(NetworkRuntime).GetProperty("IsOnline").SetValue(null,true);
                typeof(LocalBossController).GetMethod("Update",Flags).Invoke(boss,null);
                Check(!boss.gameObject.activeSelf,"Switching online disables local boss");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CombatContent/Prefabs/DungeonGuardian.prefab").GetComponent<LocalBossController>()==null,"Online prefab has no local authority");
                typeof(NetworkRuntime).GetProperty("IsOnline").SetValue(null,false);
                var inventory=player.gameObject.AddComponent<PlayerInventory>();
                var loot=boss.GetComponent<LootContainer>();
                Check(loot.TryTakeAll(inventory) && loot.IsEmpty(),"Offline rewards transfer into player inventory");
                Check(!loot.TryTakeAll(inventory),"Empty reward cannot be taken twice");
                Finish(null);
            }
        }
        catch(Exception ex) {Finish(ex);}
    }
    static void Finish(Exception ex)
    {
        File.WriteAllText("Artifacts/LocalBoss/runtime.txt",ex==null?"PASS: actual offline PlayMode AI, 3 patterns, phase 2, wall collision, damage, death rewards and online isolation. No live network test.":"FAIL\n"+ex);
        boss=null; EditorApplication.ExitPlaymode();
    }
}
