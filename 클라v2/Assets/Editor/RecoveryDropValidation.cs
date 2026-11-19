using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class RecoveryDropValidation
{
    const string Output="Artifacts/RecoveryDrop1110",Request=Output+"/preview-v1.request";
    static RecoveryDropValidation(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;File.Delete(Request);Run();}
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    static void Call(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    static void Property(object target,string name,object value)=>target.GetType().GetProperty(name).GetSetMethod(true).Invoke(target,new[]{value});
    [MenuItem("Retry/Inventory/Validate recovery roll and item drops (preserve open scenes)")]
    public static void Run(){Directory.CreateDirectory(Output);try{Validate();}catch(Exception e){File.WriteAllText(Output+"/validation.txt","FAIL\n"+e);Debug.LogException(e);}}
    static void Validate(){
        if(EditorApplication.isPlayingOrWillChangePlaymode||NetworkRuntime.IsOnline)throw new Exception("Requires idle Edit Mode; existing play/session state is preserved.");
        File.WriteAllText(Output+"/validation.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        var previous=EditorSceneManager.GetSceneManagerSetup();var preview=EditorSceneManager.NewPreviewScene();
        Font font=null;Material floorMaterial=null;string error=null;var oldStamina=PlayerStamina.Local;
        void OnLog(string message,string trace,LogType kind){if(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert)error=message;}
        Application.logMessageReceived+=OnLog;
        try{
            var owner=new GameObject("Inventory fixture");SceneManager.MoveGameObjectToScene(owner,preview);
            var inventory=owner.AddComponent<PlayerInventory>();Call(inventory,"Awake");
            var catalog=ItemCatalog.Load();var potion=catalog.Resolve("health_potion");var gold=catalog.Resolve("gold_coin");
            var first=inventory.TryAddDetailed(potion,28);var second=inventory.TryAddDetailed(gold,10);
            Check(first.AddedCount==28&&second.AddedCount==10&&second.RemainingCount==0&&inventory.UsedSlots==3,"potions use two stacks and gold uses one slot");
            Check(inventory.TryAddDetailed(potion,int.MaxValue).AddedCount==436&&inventory.UsedSlots==30,"full inventory cannot accept another unit");
            inventory.ClearAll();
            string[] ids={"health_potion","flame_sword","frost_sword","chronometer_t1","gold_coin","focus_tonic_t1","runic_focus_t1","vigor_potion_t1"};
            int[] counts={10,1,1,1,10,5,1,1};
            for(int i=0;i<ids.Length;i++)Check(inventory.TryAddDetailed(catalog.Resolve(ids[i]),counts[i]).FullyAdded,"preview item fits: "+ids[i]);
            Check(inventory.TotalCount==30&&inventory.Capacity==30,"30 units displayed in 30 available cells");
            var camera=new GameObject("Inventory preview camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,preview);
            camera.scene=preview;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(preview);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.06f);
            camera.transform.position=new Vector3(5,5,-7);camera.transform.LookAt(Vector3.up);camera.nearClipPlane=.1f;
            var uiObject=Object.Instantiate(Resources.Load<GameObject>("ExpeditionUI/View"));SceneManager.MoveGameObjectToScene(uiObject,preview);
            var ui=uiObject.GetComponent<ExpeditionUI>();ui.enabled=false;ui.StyleInventory();ui.StyleQuickAccess();ui.StyleUpgradeCards();ui.Bind(inventory,null);ui.SetInventoryVisible(true);ui.Select(0);ui.ChangeDropCount(2);
            var canvas=uiObject.GetComponent<Canvas>();uiObject.GetComponent<CanvasScaler>().enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","맑은 고딕","Arial"},20);foreach(var text in uiObject.GetComponentsInChildren<Text>(true))text.font=font;
            Check(Field<Text>(ui,"capacity").text.Contains("30 / 30")&&Field<Text>(ui,"dropQuantity").text=="수량 3","quantity counter and drop selector");
            Check(Field<ExpeditionUI.Cell[]>(ui,"cells").Count(c=>c.bg.gameObject.activeSelf)==30,"serialized prefab exposes exactly 30 cells");
            Capture(camera,canvas,1920,1080,"inventory-full");Capture(camera,canvas,1280,720,"inventory-720p");
            foreach(var cell in Field<ExpeditionUI.Cell[]>(ui,"cells").Take(30))Check(cell.label.preferredHeight<=cell.label.rectTransform.rect.height+1,"item name and tier fit the slot");
            ui.DropSelected();Check(inventory.TotalCount==27&&inventory.Count(potion)==7,"drop button removes exactly the selected offline quantity");
            var drop=preview.GetRootGameObjects().Select(g=>g.GetComponent<LootContainer>()).FirstOrDefault(l=>l!=null);
            Check(drop!=null&&drop.IsAvailable&&drop.Slots.Sum(s=>s.count)==3,"dropped units are available in a world container");
            Capture(camera,canvas,1920,1080,"inventory-after-drop");
            Check(drop.TryTakeAll(inventory)&&inventory.TotalCount==30&&inventory.Count(potion)==10,"dropped stack can be picked up exactly once");
            Check(!drop.TryTakeAll(inventory)&&inventory.TotalCount==30,"empty drop cannot duplicate items");
            ui.ChangeDropCount(0,true);Check(Field<Text>(ui,"dropQuantity").text=="수량 10","all selects the current stack only");
            uiObject.SetActive(false);
            // Validate the authoritative snapshot boundary without modifying an accepted inventory on failure.
            inventory.SetServerOwned();var snapshot=new InventorySyncData{entryCount=1,totalCount=30,entries=new ItemStack[32]};snapshot.entries[0]=new ItemStack{itemHash=ItemHash.Of("health_potion"),count=30};
            Check(inventory.ApplyServerSnapshot(snapshot,catalog)&&inventory.TotalCount==30,"server snapshot accepts two potion stacks totaling 30");
            snapshot.totalCount=snapshot.entries[0].count=481;
            Check(!inventory.ApplyServerSnapshot(snapshot,catalog)&&inventory.TotalCount==30,"31-stack snapshot rejected atomically");
            // Actual player prefab + native controller. Move only this fixture; never simulate user scenes.
            var stage=new GameObject("Recovery actor");SceneManager.MoveGameObjectToScene(stage,preview);stage.SetActive(false);
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),stage.transform);
            foreach(var script in actor.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            foreach(var cam in actor.GetComponentsInChildren<Camera>(true))cam.enabled=false;
            stage.SetActive(true);actor.transform.position=new Vector3(0,.1f,0);
            var state=actor.GetComponent<Player_State>();Call(state,"Awake");
            var input=actor.GetComponent<Defalult_Input>();input.enabled=true;
            var movement=actor.GetComponent<Player_Movement>();movement.enabled=true;Call(movement,"Awake");
            var attack=actor.GetComponent<Player_Attack>();Call(attack,"Awake");
            var animation=actor.GetComponent<Player_Animation>();Call(animation,"Awake");
            var defense=actor.GetComponent<PlayerDefenseController>();defense.enabled=true;Call(defense,"Awake");Call(defense,"OnEnable");
            var stamina=actor.AddComponent<PlayerStamina>();Call(stamina,"Awake");stamina.enabled=true;stamina.BindPlayer(input);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,preview);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(20,1,20);
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.12f,.14f,.17f));floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            var sun=new GameObject("Recovery preview light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(sun.gameObject,preview);sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(45,-25,0);sun.intensity=1.5f;
            var capsule=actor.GetComponent<CharacterController>();capsule.enabled=true;Physics.SyncTransforms();capsule.Move(Vector3.down*.3f);Check(capsule.isGrounded,"native controller grounds on the isolated preview floor");
            var animator=actor.GetComponent<Animator>();animator.Rebind();animator.applyRootMotion=false;
            state.TakeDamage(5,null);animator.Play("Hit",0,.2f);animator.Update(0);Property(stamina,"Current",29);
            Check(state.IsHit&&!defense.TryStartDash(Vector3.right)&&stamina.Current==29,"insufficient recovery stamina preserves hit state");
            Capture(camera,null,960,720,"recovery-before");Property(stamina,"Current",30);
            Check(defense.TryStartDash(Vector3.right)&&!state.IsHit&&defense.IsDashing&&stamina.Current==0,"paid recovery exits hit and rolls for 30 stamina");
            Check(!state.TryConsumeHitAnimationRequest(out _),"cancelled hit request cannot restart after recovery");
            animator.Play(PlayerDefenseAnimation.RollState,0,.35f);animator.Update(0);Capture(camera,null,960,720,"recovery-roll");
            defense.CancelDefense();state.TakeDamage(1,null);Set(defense,"nextRecoveryAt",0f);Set(defense,"pendingSequence",99);Set(defense,"pendingRecovery",true);
            defense.ReconcileDefense(99,1);Check(state.IsHit&&!defense.IsDashing,"rejected online recovery does not clear hit state");
            Set(defense,"pendingSequence",100);Set(defense,"pendingRecovery",true);Set(defense,"pendingRecoveryDirection",Vector3.forward);
            defense.ReconcileDefense(100,0);Check(!state.IsHit&&defense.IsDashing&&defense.DashDirection==Vector3.forward,"accepted online recovery preserves requested direction and clears hit");
            defense.CancelDefense();state.CancelHitRecovery();animator.Play("Blend Tree",0,0);animator.Update(0);Set(defense,"nextDashAt",0f);Property(stamina,"Current",100);
            Check(defense.TryStartDash(Vector3.forward)&&stamina.Current==80,"ordinary roll still costs 20");
            defense.CancelDefense();Set(defense,"nextRecoveryAt",0f);state.ApplyOfflineParryStun();Check(!defense.TryStartDash(Vector3.forward),"parry stun remains distinct from escapable damage reaction");
            attack.UnequipWeapon();Check(!attack.IsSwordEquipped&&string.IsNullOrEmpty(attack.EquippedWeaponId),"empty weapon state clears visuals and attack profile");
            Call(defense,"OnDisable");
        }finally{
            EditorSceneManager.ClosePreviewScene(preview);if(font!=null)Object.DestroyImmediate(font);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);
            typeof(PlayerStamina).GetProperty("Local").GetSetMethod(true).Invoke(null,new object[]{oldStamina});Application.logMessageReceived-=OnLog;
        }
        Check(error==null,"editor fixture logged an error: "+error);
        var after=EditorSceneManager.GetSceneManagerSetup();Check(previous.Length==after.Length&&previous.Zip(after,(a,b)=>a.path==b.path&&a.isLoaded==b.isLoaded&&a.isActive==b.isActive).All(v=>v),"user scene setup preserved");
        File.WriteAllText(Output+"/validation.txt",$"PASS {checks}: actual inventory/drop UI at 1080p/720p, total quantity limit, partial pickups, rejected snapshots, offline drop/re-pickup, real player hit-to-roll with 20/30 stamina, approval/rejection, parry stun and unarmed state. Editor preview only; no live multiplayer session. Open scenes and Play Mode preserved.");
    }
    static void Capture(Camera camera,Canvas canvas,int width,int height,string name){
        var rt=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{camera.targetTexture=rt;if(canvas!=null)canvas.scaleFactor=Mathf.Sqrt(width/1920f*height/1080f);Canvas.ForceUpdateCanvases();
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);}
    }
}
