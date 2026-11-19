using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class EquipmentPolishValidation
{
    const string Output = "Artifacts/EquipmentPolish";
    static bool running;
    static EquipmentPolishValidation() { EditorApplication.update += Poll; }
    static async void Poll()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Output + "/build.request")) return;
        try { File.Delete(Output + "/build.request"); }
        catch (IOException) { return; } // The requesting process may still be closing the file.
        running = true;
        try { await Run(); File.WriteAllText(Output + "/result.txt", "PASS actual weapon icons, local/remote back carry and grip restoration, inventory stats, stash selection and pagination\n"); }
        catch (Exception e) { File.WriteAllText(Output + "/result.txt", "FAIL\n" + e); Debug.LogException(e); }
        finally { running = false; }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("PASS " + message); }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    static async Task Run()
    {
        await WeaponIconBuilder.Build();
        foreach (var definition in ABItemDefinitions.All.Where(i=>i.kind==2&&i.effect!=4))
        {
            var item=ItemCatalog.Load().Resolve(definition.id);
            Check(item.Icon==WeaponIconBuilder.Get(definition.effect), "model icon bound: "+definition.id);
            Check(!item.Description.Contains("3.5m")&&!item.Description.Contains("1.6초"),"concise description: "+definition.id);
        }
        ElementalInventoryValidation.Validate();
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            await Inventory(scene);
            foreach(string prefab in new[]{"LocalPlayerPrefab","RemotePlayerPrefab"})await Carry(scene,prefab);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static async Task Inventory(Scene scene)
    {
        var owner=new GameObject("Equipment fixture");SceneManager.MoveGameObjectToScene(owner,scene);
        var inventory=owner.AddComponent<PlayerInventory>();Call(inventory,"Awake");inventory.ClearAll();
        foreach(string id in new[]{"flame_greatsword","frost_greatsword","greatsword_001","flame_sword","frost_sword","beam_sword","sword_001","life_amulet_t3"})inventory.TryAdd(ItemCatalog.Load().Resolve(id));
        inventory.TryAddDetailed(ItemCatalog.Load().Resolve("health_potion"),30);
        var bootstrap=owner.AddComponent<NetworkBootstrap>();Set(bootstrap,"<Identity>k__BackingField",owner.AddComponent<LocalIdentity>());
        var client=owner.AddComponent<ABClient>();Set(client,"bootstrap",bootstrap);Set(client,"<Supported>k__BackingField",true);
        var state=EquipmentPresentation.Preview(ItemHash.Of("flame_greatsword"),ItemHash.Of("life_amulet_t3"));
        Check(state.maxHp==120&&state.damage==45,"loadout preview matches greatsword and tier-three amulet");
        state.weapon=ItemHash.Of("flame_greatsword");state.passive=ItemHash.Of("life_amulet_t3");
        state.level=5;state.maxHp=140;state.damage=52.2f;state.attackSpeed=1.1f;state.range=1.05f;
        Set(client,"<State>k__BackingField",state);
        var view=Object.Instantiate(Resources.Load<GameObject>("ExpeditionUI/View"));SceneManager.MoveGameObjectToScene(view,scene);
        var ui=view.GetComponent<ExpeditionUI>();ui.enabled=false;ui.StyleInventory();ui.StyleQuickAccess();ui.StyleUpgradeCards();ui.Bind(inventory,null);Set(ui,"client",client);Call(ui,"Refresh");ui.SetInventoryVisible(true);ui.Select(0);
        var stats=Field<Text[]>(ui,"statValues");
        Check(stats[0].text.Contains("140")&&stats[1].text.Contains("52.2")&&stats[2].text.Contains("110"),"inventory displays server stats including upgrades");
        await Capture(view,scene,"inventory");
        var shop=owner.AddComponent<EconomyClient>();Set(shop,"bootstrap",bootstrap);Set(shop,"<Supported>k__BackingField",true);
        var entries=ABItemDefinitions.All.Where(i=>i.effect!=4||i.kind!=2).Select(i=>new EconomyEntry{hash=ItemHash.Of(i.id),count=2}).ToArray();
        var economy=new EconomySnapshot{version=2,credits=1500,weapon=ItemHash.Of("frost_greatsword"),item=ItemHash.Of("life_amulet_t3"),count=entries.Length,entries=entries};
        Set(shop,"<State>k__BackingField",economy);ui.OpenLoadout(shop);
        Check(ui.LoadoutMode&&ui.InventoryOpen&&!Field<Button>(ui,"dropButton").gameObject.activeSelf,"shop uses inventory and hides expedition-only controls");
        Check(stats[0].text.Contains("120")&&stats[1].text.Contains("45"),"shop previews chosen loadout without previous run upgrades");
        Field<Button>(ui,"stashNext").onClick.Invoke();Check(Field<int>(ui,"stashPage")==1,"stash page navigation");
        Field<Button>(ui,"stashPrevious").onClick.Invoke();
        var cells=Field<ExpeditionUI.Cell[]>(ui,"cells");int selected=Array.FindIndex(cells,c=>c.label.text==ItemCatalog.Load().Resolve("frost_greatsword").DisplayName);
        ui.Select(selected>=0?selected:0);await Capture(view,scene,"shop-inventory");
        ui.CloseLoadout();Check(!ui.InventoryOpen&&!ui.LoadoutMode,"close returns to shop without leaving overlay active");
        Set(ui,"client",null);Object.DestroyImmediate(view);Object.DestroyImmediate(owner);
    }
    static async Task Carry(Scene scene,string prefab)
    {
        var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+prefab+".prefab"));SceneManager.MoveGameObjectToScene(player,scene);
        player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        foreach(var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
        foreach(var camera in player.GetComponentsInChildren<Camera>(true))camera.enabled=false;
        foreach(var canvas in player.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
        var animator=player.GetComponentInChildren<Animator>();animator.applyRootMotion=false;animator.Rebind();animator.Update(0);
        var attack=player.GetComponent<Player_Attack>();Call(attack,"CollectWeapons");
        var carry=player.GetComponent<WeaponBackCarry>()??player.AddComponent<WeaponBackCarry>();carry.enabled=false;carry.Bind();
        foreach(string id in new[]{"test_sword",GreatswordWeapon.Id})
        {
            Check(attack.EquipWeapon(id),prefab+" equips "+id);
            var weapon=player.GetComponentsInChildren<Weapon_Sword>(true).First(w=>w.WeaponId==id);
            var gripPosition=weapon.transform.localPosition;var gripRotation=weapon.transform.localRotation;
            carry.ApplyPose(false);Check(carry.IsCarried&&weapon.gameObject.activeInHierarchy,prefab+" carries visible "+id);
            var blade=weapon.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Blade Trail Base");
            Check(Vector3.Dot(blade.position-player.transform.position,player.transform.forward)<-.15f,"weapon lies behind torso");
            var backRotation=weapon.transform.rotation;var hand=weapon.transform.parent;var handRotation=hand.localRotation;
            hand.localRotation*=Quaternion.Euler(35,60,15);carry.ApplyPose(false);
            Check(Quaternion.Angle(weapon.transform.rotation,backRotation)<.01f,"arm animation does not roll the carried blade");hand.localRotation=handRotation;carry.ApplyPose(false);
            if(prefab=="LocalPlayerPrefab")await CapturePlayer(player,scene,id+"-back");
            carry.ApplyPose(true);Check(!carry.IsCarried&&Vector3.Distance(weapon.transform.localPosition,gripPosition)<.0001f&&Quaternion.Angle(weapon.transform.localRotation,gripRotation)<.01f,"combat restores authored grip without drift");
            for(int i=0;i<20;i++){carry.ApplyPose(false);carry.ApplyPose(true);}
            Check(Vector3.Distance(weapon.transform.localPosition,gripPosition)<.0001f,"repeated carry transitions preserve grip");
        }
        Object.DestroyImmediate(player);
    }
    static Camera Camera(Scene scene)
    {
        var camera=new GameObject("Equipment camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
        camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.065f);return camera;
    }
    static async Task Capture(GameObject view,Scene scene,string name)
    {
        var camera=Camera(scene);var canvas=view.GetComponent<Canvas>();view.GetComponent<CanvasScaler>().enabled=false;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        Canvas.ForceUpdateCanvases();await Save(camera,name,1920,1080);Object.DestroyImmediate(camera.gameObject);
    }
    static async Task CapturePlayer(GameObject player,Scene scene,string name)
    {
        var camera=Camera(scene);camera.transform.position=new Vector3(3,2.8f,-5);camera.transform.LookAt(new Vector3(0,1,0));camera.orthographic=true;camera.orthographicSize=1.6f;
        var light=new GameObject("Equipment key").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=2.3f;light.transform.rotation=Quaternion.Euler(35,-25,0);
        await Save(camera,name,900,900);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);
    }
    static async Task Save(Camera camera,string name,int width,int height)
    {
        var target=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            await WeaponIconBuilder.NextFrame();await WeaponIconBuilder.NextFrame();camera.targetTexture=target;
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
    }
}
