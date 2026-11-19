using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>Runs in the existing editor, without loading user scenes or entering Play Mode.</summary>
[InitializeOnLoad]
public static class SkillBuildValidation
{
    const string Output="Artifacts/SkillBuilds1110",Request=Output+"/preview-v1.request";
    static SkillBuildValidation(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Request))return;
        File.Delete(Request);Run();
    }
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    [MenuItem("Retry/Skills/Validate skill builds and VFX (preserve open scenes)")]
    public static void Run(){
        Directory.CreateDirectory(Output);
        try{Validate();}catch(Exception e){File.WriteAllText(Output+"/validation.txt","FAIL\n"+e);Debug.LogException(e);}
    }
    static void Validate(){
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Wait for Edit Mode; Play Mode is not changed by this tool.");
        File.WriteAllText(Output+"/validation.txt","RUNNING "+DateTime.UtcNow.ToString("O"));
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        SkillBuildContentBuilder.Build();
        Check(Marshal.SizeOf<SkillState>()==64&&Marshal.SizeOf<SkillEvent>()==56,"server v2 packet sizes");
        Check(Marshal.OffsetOf<SkillState>("eDuration").ToInt32()==36&&Marshal.OffsetOf<SkillEvent>("weapon").ToInt32()==44,"wire append offsets");
        var economyEntries=new EconomyEntry[96];for(int i=0;i<75;i++)economyEntries[i]=new EconomyEntry{hash=i+1,count=1};
        var economyBytes=PacketIO.StructToBytes(new EconomySnapshot{version=2,count=75,entries=economyEntries});
        Check(economyBytes.Length==1572&&EconomyClient.TryDecode(economyBytes,out var fullEconomy)&&fullEconomy.entries[74].hash==75,"expanded stash packet retains every item");
        var legacy=PacketIO.StructToBytes(new EconomySnapshot{version=1,count=3,entries=economyEntries}).Take(1060).ToArray();
        Check(EconomyClient.TryDecode(legacy,out var oldEconomy)&&oldEconomy.count==3,"legacy stash packet remains readable");
        legacy[0]=2;Check(!EconomyClient.TryDecode(legacy,out _),"truncated new stash packet rejected");
        Check(ABItemDefinitions.All.Length==71,"71 item definitions");
        var newItems=ABItemDefinitions.All.Where(r=>r.effect>=16).ToArray();Check(newItems.Length==18,"18 skill items");
        foreach(var row in newItems){var item=ItemCatalog.Load().Resolve(row.id);Check(item!=null&&item.Icon!=null&&!string.IsNullOrEmpty(item.Description),"catalog icon/description: "+row.id);}
        Check(ItemCatalog.Load().ValidateCatalog(),"unique item IDs/hashes");
        var shader=Resources.Load<Shader>("SkillHologram");Check(shader!=null&&!ShaderUtil.ShaderHasError(shader),"hologram shader compiles");
        var pulse=new DamageFeedbackPulse();pulse.Observe(100,100);Check(pulse.Strength==0,"initial HP does not flash");
        pulse.Observe(75,100);float strength=pulse.Strength;Check(strength>.5f,"damage flashes");pulse.Observe(75,100);Check(pulse.Strength==strength,"duplicate snapshot does not retrigger");
        pulse.Step(2);Check(pulse.Strength==0,"pulse fades to zero");pulse.Observe(95,100);Check(pulse.Strength==0,"healing does not flash");
        pulse.Observe(80,80);Check(pulse.Strength==0,"max HP equipment clamp does not flash");pulse.Reset();pulse.Observe(20,100);Check(pulse.Strength==0,"rebinding clears previous HP");
        Check(DamageVignetteGraphic.EdgeAlpha(.5f,.5f,16f/9)==0&&DamageVignetteGraphic.EdgeAlpha(0,0,16f/9)==1,"transparent center/red edge");
        var previous=EditorSceneManager.GetSceneManagerSetup();var scene=EditorSceneManager.NewPreviewScene();Material floorMaterial=null;Font font=null;
        string renderError=null;
        void OnLog(string message,string trace,LogType kind){if(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert)renderError=message;}
        Application.logMessageReceived+=OnLog;
        try{
            var root=new GameObject("Skill preview stage");SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),root.transform);
            foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var c in actor.GetComponentsInChildren<Camera>(true))c.enabled=false;
            root.SetActive(true);var animator=actor.GetComponent<Animator>();animator.Rebind();animator.applyRootMotion=false;animator.Play("SkillQ",0,.43f);animator.Update(0);
            var camera=new GameObject("Skill preview camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
            camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position=new Vector3(8,9,-11);camera.transform.LookAt(new Vector3(0,.6f,2.5f));camera.fieldOfView=42;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.044f,.055f);camera.nearClipPlane=.1f;
            var light=new GameObject("Preview daylight").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);
            light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(45,-35,0);
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.13f,.15f,.17f));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=new Vector3(0,-.16f,2);floor.transform.localScale=new Vector3(25,.3f,25);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","맑은 고딕","Arial"},20);
            var canvas=new GameObject("Preview labels",typeof(RectTransform),typeof(Canvas)).GetComponent<Canvas>();SceneManager.MoveGameObjectToScene(canvas.gameObject,scene);
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;
            var title=ExpeditionTheme.Label("Title",canvas.transform,0,345,1000,44,30,ExpeditionTheme.White,"",TextAnchor.MiddleCenter);title.font=font;
            var subtitle=ExpeditionTheme.Label("Subtitle",canvas.transform,0,300,1100,30,17,ExpeditionTheme.Muted,"E 검기 · Q 전방 검격 / 반투명 홀로그램",TextAnchor.MiddleCenter);subtitle.font=font;
            var eCard=SkillHudCard.Create(canvas.transform,"E","검기",1,new Vector2(-105,-322));var qCard=SkillHudCard.Create(canvas.transform,"Q","전방 검격",2,new Vector2(105,-322));
            foreach(var text in canvas.GetComponentsInChildren<Text>())text.font=font;
            string[] ids={"sword_001","flame_sword","frost_sword","beam_sword"};string[] names={"일반검 · 금빛","화염검 · 붉은빛","얼음검 · 하늘빛","광선검 · 민트빛"};
            Color? last=null;
            for(int i=0;i<ids.Length;i++){
                int hash=ItemHash.Of(ids[i]);Color tint=SkillPalette.ForWeapon(hash);Check(!last.HasValue||tint!=last.Value,"weapon palette is distinct");last=tint;
                title.text=names[i];title.color=tint;eCard.SetTint(tint);qCard.SetTint(tint);eCard.Refresh(1.5f,4,true,true);qCard.Refresh(0,12.5f,true,true);
                Check(Mathf.Abs(Field<Image>(eCard,"cooldownMask").fillAmount-.375f)<.001f,"HUD uses modified cooldown duration");
                var warning=SkillEffect.Charge(Vector3.zero,Vector3.forward,hash,1);SceneManager.MoveGameObjectToScene(warning.gameObject,scene);warning.enabled=false;warning.SampleVisual(.8f);
                var slash=SkillEffect.Slash(Vector3.zero,Vector3.forward,hash,1);SceneManager.MoveGameObjectToScene(slash.gameObject,scene);slash.enabled=false;slash.SampleVisual(.12f);
                var wave=SkillEffect.CreateWave();SceneManager.MoveGameObjectToScene(wave.gameObject,scene);wave.enabled=false;
                wave.Flight(new SkillEvent{weapon=hash,area=1,x=-2.5f,y=0,z=2.4f,dz=1,remaining=1});
                Check(wave.GetComponent<MeshFilter>().sharedMesh.vertexCount>300,"E layered ribbons");
                Check(wave.GetComponent<Renderer>().shadowCastingMode==ShadowCastingMode.Off&&wave.GetComponentInChildren<Light>()==null,"VFX has no shadows/lights");
                Capture(camera,canvas,1280,800,"skill-"+ids[i]);
                if(i==1){
                    var edge=new GameObject("Damage gradient",typeof(RectTransform),typeof(CanvasRenderer),typeof(DamageVignetteGraphic)).GetComponent<DamageVignetteGraphic>();edge.transform.SetParent(canvas.transform,false);
                    edge.rectTransform.anchorMin=Vector2.zero;edge.rectTransform.anchorMax=Vector2.one;edge.rectTransform.offsetMin=edge.rectTransform.offsetMax=Vector2.zero;
                    edge.raycastTarget=false;edge.color=new Color(1,1,1,.68f);edge.transform.SetAsFirstSibling();Check(!edge.raycastTarget,"damage overlay passes pointer input");
                    Capture(camera,canvas,1280,800,"damage-feedback");Object.DestroyImmediate(edge.gameObject);
                }
                Object.DestroyImmediate(warning.gameObject);Object.DestroyImmediate(slash.gameObject);Object.DestroyImmediate(wave.gameObject);
            }
            root.SetActive(false);floor.SetActive(false);title.text="스킬 빌드 · 장비와 소모품";title.color=ExpeditionTheme.Gold;subtitle.text="장비 4종 · 물약 2종 / 각 1~3등급";
            eCard.gameObject.SetActive(false);qCard.gameObject.SetActive(false);
            for(int i=0;i<6;i++){
                var row=newItems[i*3];var card=ExpeditionTheme.Box("Item family",canvas.transform,(i%3-1)*380,125-(i/3)*240,350,205,ExpeditionTheme.Ink);
                var icon=ExpeditionTheme.Box("Icon",card.transform,0,53,54,54,Color.clear);ExpeditionTheme.Icon(icon,ItemCatalog.Load().Resolve(row.id));
                var name=ExpeditionTheme.Label("Name",card.transform,0,6,320,35,22,ExpeditionTheme.White,row.label.Replace(" 1등급",""),TextAnchor.MiddleCenter);name.font=font;
                string[] captions={"쿨타임 감소\n8 / 14 / 20%","스킬 위력 증가\n12 / 22 / 32%","스킬 처치 시 E 초기화\n발동 간격 20 / 16 / 12초","폭 · 사거리 · 반경 증가\n10 / 18 / 26%","남은 쿨타임 회복\n50 / 75 / 100%","10초간 스킬 위력 증가\n20 / 30 / 40%"};
                var desc=ExpeditionTheme.Label("Description",card.transform,0,-49,320,70,18,ExpeditionTheme.Muted,captions[i],TextAnchor.MiddleCenter);desc.font=font;
            }
            Capture(camera,canvas,1280,800,"skill-items");
            var upgradeNames=(string[])typeof(ExpeditionUI).GetField("SkillNames",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var upgradeStats=(string[])typeof(ExpeditionUI).GetField("SkillStats",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var upgradeItems=(string[])typeof(ExpeditionUI).GetField("SkillItems",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            Check(upgradeNames.Length==14&&upgradeStats.Length==14&&upgradeItems.Length==14,"all 14 upgrade choices are presented");
            canvas.gameObject.SetActive(false);
            var expedition=Object.Instantiate(Resources.Load<GameObject>("ExpeditionUI/View"));SceneManager.MoveGameObjectToScene(expedition,scene);
            var ui=expedition.GetComponent<ExpeditionUI>();ui.enabled=false;ui.StyleUpgradeCards();
            Field<GameObject>(ui,"hud").SetActive(false);Field<GameObject>(ui,"inventoryPanel").SetActive(false);Field<GameObject>(ui,"upgradePanel").SetActive(true);
            Field<GameObject>(ui,"veil").SetActive(true);
            var upgradeCanvas=expedition.GetComponent<Canvas>();upgradeCanvas.renderMode=RenderMode.ScreenSpaceCamera;upgradeCanvas.worldCamera=camera;upgradeCanvas.planeDistance=.5f;
            var scaler=expedition.GetComponent<CanvasScaler>();if(scaler!=null)scaler.enabled=false;
            foreach(var text in expedition.GetComponentsInChildren<Text>(true))text.font=font;
            for(int page=0;page<2;page++){
                for(int i=0;i<3;i++){
                    int choice=8+page*3+i;Field<Text[]>(ui,"upgradeTitles")[i].text=upgradeNames[choice];
                    Field<Text[]>(ui,"upgradeDescriptions")[i].text=upgradeStats[choice];
                    ExpeditionTheme.Icon(Field<Image[]>(ui,"upgradeIcons")[i],ItemCatalog.Load().Resolve(upgradeItems[choice]));
                }
                Field<Text>(ui,"upgradeSubtitle").text="스킬 강화 선택";Capture(camera,upgradeCanvas,1440,900,"skill-upgrades-"+(page+1));
                foreach(var description in Field<Text[]>(ui,"upgradeDescriptions"))Check(description.preferredHeight<=description.rectTransform.rect.height+1,"upgrade description fits card");
            }
            // Render the real paged shop, including a long skill potion description.
            expedition.SetActive(false);
            var economyHost=new GameObject("Inactive economy fixture");SceneManager.MoveGameObjectToScene(economyHost,scene);economyHost.SetActive(false);
            var economy=economyHost.AddComponent<EconomyClient>();
            typeof(EconomyClient).GetMethod("Build",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(economy,null);
            var economyRoot=Field<GameObject>(economy,"root");economyRoot.transform.SetParent(null,false);SceneManager.MoveGameObjectToScene(economyRoot,scene);
            var economyCanvas=economyRoot.GetComponent<Canvas>();economyCanvas.renderMode=RenderMode.ScreenSpaceCamera;economyCanvas.worldCamera=camera;economyCanvas.planeDistance=.5f;
            economyRoot.GetComponent<CanvasScaler>().enabled=false;
            foreach(var text in economyRoot.GetComponentsInChildren<Text>(true))text.font=font;
            var shopEntries=new EconomyEntry[96];int count=0;
            foreach(var row in ABItemDefinitions.All)shopEntries[count++]=new EconomyEntry{hash=ItemHash.Of(row.id),count=1,sell=row.value,buy=row.tier==1?row.value*2:0};
            typeof(EconomyClient).GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(economy,ItemHash.Of("reset_tonic_t1"));
            typeof(EconomyClient).GetField("shop",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(economy,true);
            economy.Receive(PacketIO.StructToBytes(new EconomySnapshot{version=2,credits=1200,count=count,entries=shopEntries}));
            Check(economy.State.count==71&&economy.Supported,"real shop accepts all item definitions");
            Capture(camera,economyCanvas,1440,900,"skill-shop");
        }finally{EditorSceneManager.ClosePreviewScene(scene);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);if(font!=null)Object.DestroyImmediate(font);Application.logMessageReceived-=OnLog;}
        Check(renderError==null,"render logged an error: "+renderError);
        var after=EditorSceneManager.GetSceneManagerSetup();Check(previous.Length==after.Length&&previous.Zip(after,(a,b)=>a.path==b.path&&a.isActive==b.isActive&&a.isLoaded==b.isLoaded).All(x=>x),"user scenes preserved");
        File.WriteAllText(Output+"/validation.txt",$"PASS {checks}: packet layouts, 18 item assets/icons, palette, real effect meshes/shader, cooldown UI, damage pulse filtering/fade/gradient and 14 upgrade entries. Preview scene only; no live multiplayer session. User scenes and Play Mode preserved.");
    }
    static void Capture(Camera camera,Canvas canvas,int width,int height,string name){
        var rt=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var prior=RenderTexture.active;
        try{camera.targetTexture=rt;canvas.scaleFactor=1;Canvas.ForceUpdateCanvases();
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }finally{camera.targetTexture=null;RenderTexture.active=prior;Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);}
    }
}
