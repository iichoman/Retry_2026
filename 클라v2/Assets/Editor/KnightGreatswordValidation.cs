using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class KnightGreatswordValidation
{
    const string Output="Artifacts/KnightGreatsword", Key="Retry.KnightGreatswordValidation";
    static IEnumerator routine; static double began; static string failure; static int frame=-1, checks;
    static readonly List<string> report=new List<string>();
    static KnightGreatswordValidation(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Mode;}
    public static void BuildAndValidate()
    {
        KnightGreatswordBuilder.Build();
        Render();
        Start();
    }
    public static void Start()
    {
        if(!Application.isBatchMode)throw new Exception("Isolated fixture requires batch mode");
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;routine=Run();Application.logMessageReceived+=Log;}
        if(mode==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(failure==null?0:1);}
    }
    static void Log(string message,string stack,LogType kind)
    {
        if(kind==LogType.Error || kind==LogType.Exception || kind==LogType.Assert)failure=message+"\n"+stack;
    }
    static void Tick()
    {
        if(routine==null || !EditorApplication.isPlaying || frame==Time.frameCount)return;
        frame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup-began>60)throw new Exception("Fixture timeout");if(failure!=null)throw new Exception(failure);if(routine.MoveNext())return;}
        catch(Exception e){failure=e.ToString();}
        routine=null;Application.logMessageReceived-=Log;
        File.WriteAllText(Output+"/runtime.txt",failure==null?$"PASS {checks} checks\n"+string.Join("\n",report):"FAIL\n"+failure+"\n"+string.Join("\n",report));
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);++checks;report.Add("PASS "+label);}
    static IEnumerator Run()
    {
        var item=ItemCatalog.Load().Resolve("greatsword_001") as EquipmentItemData;
        Check(item!=null && item.WeaponId==GreatswordWeapon.Id,"greatsword catalog/equipment link");
        Check(ABItemDefinitions.Find(ItemHash.Of("greatsword_001"))?.effect==5,"AB item mapping");
        foreach(RoomType type in Enum.GetValues(typeof(RoomType)))Check(KnightEncounter.IsKnightRoom(new Room{type=type})==(type==RoomType.Reward),"knight room rule "+type);
        var holder=new GameObject("Fixture");holder.SetActive(false);
        var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        foreach(var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled=behaviour is Player_Attack || behaviour is Player_Animation || behaviour is Defalult_Input || behaviour is Player_State || behaviour is Weapon_Sword || behaviour is WeaponHitbox || behaviour is WeaponSwingTrail;
        var hardware=player.GetComponent<UnityEngine.InputSystem.PlayerInput>();if(hardware!=null)hardware.enabled=false;
        holder.SetActive(true);
        var input=player.GetComponent<Defalult_Input>();input.SetInputMode(PlayerInputMode.Gameplay);
        var attack=player.GetComponent<Player_Attack>();var animation=player.GetComponent<Player_Animation>();var animator=player.GetComponent<Animator>();
        Check(attack.EquipWeapon(GreatswordWeapon.Id),"real local prefab can equip greatsword");
        Check(attack.IsGreatswordEquipped && attack.CurrentAttackDamage==45,"heavy weapon damage/profile");
        var great=player.GetComponentInChildren<GreatswordWeapon>(true);
        var ordinary=player.GetComponentInChildren<test_sword>(true);
        Check(great.gameObject.activeSelf && !ordinary.gameObject.activeSelf,"only equipped model visible");
        var targets=new List<Monster_State>();
        for(int i=0;i<2;i++)
        {
            var target=new GameObject("Cleave target "+i);target.layer=LayerMask.NameToLayer("Monster");target.transform.position=new Vector3(i==0?0:1.25f,0,2.7f);
            var collider=target.AddComponent<CapsuleCollider>();collider.center=Vector3.up;collider.height=2;collider.radius=.15f;
            var health=target.AddComponent<Monster_State>();health.NetworkSetMax(300,300);targets.Add(health);
        }
        Physics.SyncTransforms(); int starts=0;attack.AttackAnimationStarted+=_=>starts++;
        great.RequestAttack();float start=Time.time;
        while(Time.time-start<.35f){great.RequestAttack();yield return null;}
        Check(starts==1 && animation.IsAttacking,"one committed heavy attack despite repeated input");
        Check(targets.All(t=>t.CurrentHp==300),"cleave has a real wind-up before damage");
        while(Time.time-start<1.25f){great.RequestAttack();yield return null;}
        Check(targets.All(t=>t.CurrentHp==255),"wide cleave hits multiple targets once for 45");
        while(Time.time-start<1.9f)yield return null;
        Check(!animation.IsAttacking && attack.ActiveAnimationComboIndex==0,"heavy attack returns to locomotion");
        Check(player.transform.position.sqrMagnitude<.0001f && Quaternion.Angle(player.transform.rotation,Quaternion.identity)<.01f,"heavy motion preserves gameplay root");
        Check(attack.EquipWeapon("test_sword") && !great.gameObject.activeSelf && ordinary.gameObject.activeSelf,"switching back restores ordinary sword");
        Check(attack.CurrentAttackDamage<45,"ordinary and heavy damage differ");
        int normalStarts=starts;
        ordinary.RequestAttack();
        for(int combo=1;combo<=4;combo++)
        {
            while(attack.ActiveAnimationComboIndex!=combo)yield return null;
            float stepAt=Time.time;while(Time.time-stepAt<.4f)yield return null;
            if(combo<4)ordinary.RequestAttack();
        }
        float finalAt=Time.time;while(Time.time-finalAt<1.1f)yield return null;
        Check(starts-normalStarts==4 && !animation.IsAttacking,"ordinary sword still completes all four queued attacks");
        attack.EquipWeapon(GreatswordWeapon.Id);great.RequestAttack();yield return null;yield return null;
        attack.EquipWeapon("test_sword");yield return null;yield return null;
        Check(attack.ActiveAnimationComboIndex==0 && !great.GetComponentInChildren<Collider>(true).enabled,"switch during wind-up cancels hitbox/request");
        Check(animator.HasState(0,Animator.StringToHash("SkillE")) && animator.HasState(0,Animator.StringToHash("SkillQ")),"Q/E animator states retained");
        var registry=new GameObject("Remote monsters").AddComponent<RemoteMonsterRegistry>();
        registry.MonsterPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monster1.prefab");
        registry.OnEnterView(new MonsterEnterView{monsterId=712,monsterKind=(int)MonsterKind.ELITE,hp=300,maxHp=300,posX=10,posZ=3});
        var knight=registry.GetActor(712);var knightHealth=knight.GetComponent<Monster_State>();var knightAnimator=knight.GetComponentInChildren<Animator>();
        Check(knightHealth.Type==MonsterType.Elite && knight.GetComponent<Monster>().MonsterId=="knight_elite","network ELITE resolves to knight model and elite stats");
        Check(knight.transform.localScale==Vector3.one && knightAnimator.runtimeAnimatorController.name=="KnightElite","network keeps authored knight scale/controller");
        Check(!knight.GetComponent<Monster>().enabled && !knight.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled,"network knight cannot run local AI/navigation");
        registry.OnMonsterAttack(new MonsterAttackEvent{monsterId=712});yield return null;yield return null;
        Check(knightAnimator.GetCurrentAnimatorStateInfo(0).IsTag("Attack"),"server attack event plays authored knight attack");
        Check(knight.GetComponentsInChildren<MonsterHitbox>(true).All(h=>!h.enabled),"remote knight cannot apply local damage");
        registry.OnDied(712);yield return null;yield return null;
        Check(knightHealth.IsDead && knightAnimator.GetNextAnimatorStateInfo(0).IsTag("Death") || knightHealth.IsDead && knightAnimator.GetCurrentAnimatorStateInfo(0).IsTag("Death"),"knight death uses authored clip");
        var remote=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RemotePlayerPrefab.prefab"),holder.transform);
        Check(remote.GetComponent<Player_Attack>().EquipWeapon(GreatswordWeapon.Id),"remote player has greatsword model/profile");
        Object.Destroy(remote);Object.Destroy(registry.gameObject);Object.Destroy(holder);foreach(var t in targets)Object.Destroy(t.gameObject);
        yield return null;
        var loading=EditorSceneManager.LoadSceneAsyncInPlayMode(KnightGreatswordBuilder.PracticeScene,new LoadSceneParameters(LoadSceneMode.Single));
        while(!loading.isDone)yield return null;
        yield return null;yield return null;
        var practice=Object.FindFirstObjectByType<KnightGreatswordPractice>();
        Check(practice!=null && practice.Player.GetComponent<Player_Attack>().IsGreatswordEquipped,"playable scene starts with greatsword equipped");
        Check(practice.Knight.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh,"practice knight is on generated NavMesh");
        Vector3 before=practice.Knight.transform.position;float beganAt=Time.time;
        while(Time.time-beganAt<1)yield return null;
        Check(Vector3.Distance(before,practice.Knight.transform.position)>.5f,"actual knight AI chases player");
        while(Time.time-beganAt<5)yield return null;
        Check(practice.Player.GetComponent<Player_State>().CurrentHp<100,"actual knight swing damages player");
        CapturePractice();
        var equipment=practice.Player.GetComponent<PlayerEquipment>();
        Check(equipment.TryUnequip(EquipmentSlotType.Weapon) && practice.Player.GetComponent<Player_Attack>().EquippedWeaponId=="","unequip removes active weapon/profile");
        // Two small reward-room islands at the same one-metre floor height as the dungeon.
        var spawnFixture=new GameObject("Reward spawn fixture");
        var dungeon=spawnFixture.AddComponent<DungeonGenerator_ChunkMesh>();dungeon.generateOnStart=false;dungeon.enabled=false;
        var rooms=(List<Room>)dungeon.Rooms;
        for(int index=0;index<2;index++)
        {
            int x=30+index*20;
            var room=new Room{id=700+index,type=RoomType.Reward,bounds=new BoundsInt(x,0,30,12,1,12)};
            for(int tx=x;tx<x+12;tx++)for(int tz=30;tz<42;tz++)room.floorTiles.Add(new Vector3Int(tx,0,tz));
            room.blockedTiles.Add(new Vector3Int(x+6,0,36));rooms.Add(room);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.layer=LayerMask.NameToLayer("Wall");floor.transform.position=new Vector3(x+6,.5f,36);floor.transform.localScale=new Vector3(12,1,12);
        }
        Physics.SyncTransforms();var surface=spawnFixture.AddComponent<Unity.AI.Navigation.NavMeshSurface>();surface.useGeometry=UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=LayerMask.GetMask("Wall");surface.BuildNavMesh();
        var spawner=spawnFixture.AddComponent<MonsterSpawner>();var serialized=new SerializedObject(spawner);var prefabs=serialized.FindProperty("monsterPrefabs");prefabs.arraySize=1;prefabs.GetArrayElementAtIndex(0).objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monster1.prefab");serialized.ApplyModifiedPropertiesWithoutUndo();
        spawner.SpawnMonsters();yield return null;
        var spawned=(List<GameObject>)typeof(MonsterSpawner).GetField("spawnedMonsters",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(spawner);
        int expected=rooms.Sum(r=>RoomBlueprint.GuardCount(dungeon.seed,r));
        Check(spawned.Count==expected && spawned.All(g=>g.GetComponent<Monster_State>().Type==MonsterType.Elite),"offline spawner creates one or two knights per reward room");
        for(int i=0;i<spawned.Count;i++)
        {
            var tile=dungeon.WorldToTile(spawned[i].transform.position);tile.y=0;
            Check(rooms.Any(r=>r.floorTiles.Contains(tile) && !r.blockedTiles.Contains(tile)),"offline knight stays on an unblocked reward room floor "+i);
        }
        spawner.SpawnMonsters();yield return null;
        Check(spawned.Count==expected,"respawning replaces previous knights without duplicating the tracked population");
    }

    public static void Render()
    {
        Directory.CreateDirectory(Output);
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
            SceneManager.MoveGameObjectToScene(player,scene);Prepare(player);
            player.GetComponentInChildren<GreatswordWeapon>(true).CorrectBladeFacing();
            foreach(var weapon in player.GetComponentsInChildren<Weapon_Sword>(true))weapon.gameObject.SetActive(weapon is GreatswordWeapon);
            foreach(var gun in player.GetComponentsInChildren<Weapon_Gun>(true))gun.gameObject.SetActive(false);
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(KnightGreatswordBuilder.Folder+"/GreatswordCleave.anim");
            SampleSheet(player,player.GetComponent<Animator>(),clip,scene,"greatsword-poses");Object.DestroyImmediate(player);
            var knight=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KnightGreatswordBuilder.KnightPath));
            SceneManager.MoveGameObjectToScene(knight,scene);Prepare(knight);
            foreach(string name in new[]{"KnightIdle","KnightAttack","KnightDeath"})
            {
                var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(KnightGreatswordBuilder.Folder+"/"+name+".anim");
                SampleSheet(knight,knight.GetComponentInChildren<Animator>(),source,scene,name);
            }
            Object.DestroyImmediate(knight);
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void CapturePractice()
    {
        var camera=Camera.main;var rt=new RenderTexture(1280,720,24);var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Output+"/practice.png",texture.EncodeToPNG());
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.Destroy(rt);Object.Destroy(texture);}
    }
    static void Prepare(GameObject root)
    {
        root.transform.position=Vector3.zero;
        foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
        foreach(var c in root.GetComponentsInChildren<Camera>(true))c.enabled=false;
        foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
        foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))ps.gameObject.SetActive(false);
        foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.updateWhenOffscreen=true;
    }
    static void SampleSheet(GameObject root,Animator animator,AnimationClip clip,Scene scene,string name)
    {
        animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.fireEvents=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var graph=PlayableGraph.Create("KnightGreatsword preview");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable=AnimationClipPlayable.Create(graph,clip);AnimationPlayableOutput.Create(graph,"Preview",animator).SetSourcePlayable(playable);graph.Play();
        var sheet=new Texture2D(1440,960,TextureFormat.RGB24,false);
        var camera=new GameObject("Camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position=new Vector3(4,3.5f,5);camera.transform.LookAt(new Vector3(0,1,.3f));camera.orthographic=true;camera.orthographicSize=1.9f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.2f);
        var light=new GameObject("Key").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var rt=new RenderTexture(480,480,24);var previous=RenderTexture.active;
        try
        {
            for(int i=0;i<6;i++)
            {
                playable.SetTime(clip.length * i / 5d);graph.Evaluate(0);
                // Multiple editor renders happen in one frame; bake current bones so GPU skinning
                // cannot reuse the first pose's matrices for the remaining contact-sheet cells.
                var baked=new List<(SkinnedMeshRenderer skin,GameObject go,Mesh mesh)>();
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(!skin.enabled)continue;
                    var mesh=new Mesh();skin.BakeMesh(mesh,true);
                    var go=new GameObject("Baked pose");go.transform.SetParent(skin.transform,false);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                    skin.enabled=false;baked.Add((skin,go,mesh));
                }
                camera.targetTexture=rt;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
                sheet.ReadPixels(new Rect(0,0,480,480),(i%3)*480,(1-i/3)*480);
                foreach(var sample in baked){sample.skin.enabled=true;Object.DestroyImmediate(sample.go);Object.DestroyImmediate(sample.mesh);}
            }
            sheet.Apply();File.WriteAllBytes(Output+"/"+name+".png",sheet.EncodeToPNG());
        }
        finally{graph.Destroy();camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(sheet);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);}
    }
}
