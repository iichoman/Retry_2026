using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Can run from an already open editor or in a separate batch run when the project is free.
// All objects live in a disposable Preview Scene. User scenes and Play Mode are untouched.
[InitializeOnLoad]
public static class HudPreviewValidation
{
    const string Output = "Artifacts/HUD1110", Request = Output + "/preview-v2.request";
    static HudPreviewValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request); Run();
    }
    [MenuItem("Retry/UI/Preview player HUD (preserve open scenes)")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/validation.txt", "RUNNING " + DateTime.UtcNow.ToString("O"));
        try { Render(); if (Application.isBatchMode) EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Output + "/validation.txt", "FAIL\n" + e); Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); }
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void Render()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Preview requires idle Edit Mode.");
        var previous = EditorSceneManager.GetSceneManagerSetup();
        var scene = EditorSceneManager.NewPreviewScene();
        Font font = null; Material groundMaterial = null; RenderTexture portraitTexture = null;
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        try
        {
            var stage = new GameObject("HUD Preview"); SceneManager.MoveGameObjectToScene(stage, scene); stage.SetActive(false);
            var actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"), stage.transform);
            foreach (var script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var c in actor.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            stage.SetActive(true);
            var animator = actor.GetComponent<Animator>(); animator.applyRootMotion = false; animator.Rebind(); animator.Update(0);
            var camera = new GameObject("Preview Camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(9, 12, -12); camera.transform.LookAt(Vector3.up * .5f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.065f,.075f,.085f); camera.nearClipPlane = .1f;
            var sun = new GameObject("Preview Light").AddComponent<Light>(); SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
            sun.type = LightType.Directional; sun.intensity = 1.4f; sun.transform.rotation = Quaternion.Euler(45,160,0);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = new Vector3(0,-.12f,0); ground.transform.localScale = new Vector3(60,.2f,60);
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); groundMaterial.SetColor("_BaseColor",new Color(.13f,.15f,.17f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            var canvas = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            SceneManager.MoveGameObjectToScene(canvas.gameObject,scene);
            var hudObject = new GameObject("Player HUD",typeof(RectTransform)); hudObject.transform.SetParent(canvas.transform,false);
            var hudRect = (RectTransform)hudObject.transform; hudRect.anchorMin=Vector2.zero;hudRect.anchorMax=Vector2.one;hudRect.offsetMin=hudRect.offsetMax=Vector2.zero;
            var hud = hudObject.AddComponent<GameHUDOverlay>(); hud.enabled=false; hud.Build();
            Field<Text>(hud,"playerNameText").text="서리달";Field<Text>(hud,"playerNameText").color=TeamPalette.Get(0);
            Field<Image>(hud,"teamMark").color=TeamPalette.Get(0); Field<Text>(hud,"levelText").text="LV. 07";
            typeof(GameHUDOverlay).GetMethod("RefreshHp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,new object[]{76,120});
            Field<Image>(hud,"hpTrail").fillAmount=.78f;Field<Image>(hud,"staminaFill").fillAmount=.68f;
            Field<Text>(hud,"staminaText").text="스태미나  68 / 100";Field<Text>(hud,"timerText").text="07:42";
            var e = Field<SkillHudCard>(hud,"eCard");var q = Field<SkillHudCard>(hud,"qCard");
            e.Refresh(0,8,true,true);q.Refresh(12,20,true,true);
            Field<Text>(hud,"stormLabel").text="2/4 단계 · 안전 구역 축소 중";Field<Text>(hud,"stormValue").text="00:42";
            var statuses=Field<GameObject[]>(hud,"debuffBadges");statuses[0].SetActive(true);statuses[3].SetActive(true);
            Check(Field<Text>(e,"status").text=="준비"&&Field<Text>(q,"countdown").text=="12","ready and cooldown labels");
            Check(Mathf.Abs(Field<Image>(q,"cooldownMask").fillAmount-.60f)<.001f,"cooldown sweep tracks server duration");
            Check(!hud.GetComponentsInChildren<Graphic>(true).Any(g=>g.raycastTarget),"HUD never consumes gameplay clicks");

            var portraitCamera = new GameObject("Portrait Preview").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(portraitCamera.gameObject,scene);
            portraitCamera.scene=scene;portraitCamera.overrideSceneCullingMask=camera.overrideSceneCullingMask;
            var focus=animator.GetBoneTransform(HumanBodyBones.Head).position-Vector3.up*.035f;
            portraitCamera.transform.position=focus+actor.transform.forward*.65f;portraitCamera.transform.LookAt(focus);
            portraitCamera.fieldOfView=34;portraitCamera.nearClipPlane=.02f;portraitCamera.farClipPlane=1.1f;
            portraitCamera.clearFlags=CameraClearFlags.SolidColor;portraitCamera.backgroundColor=new Color(.04f,.05f,.07f);
            portraitTexture=new RenderTexture(256,256,24);portraitCamera.targetTexture=portraitTexture;
            RenderPipeline.SubmitRenderRequest(portraitCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=portraitTexture});portraitCamera.enabled=false;
            Field<RawImage>(hud,"portraitRaw").texture=portraitTexture;Field<RawImage>(hud,"portraitRaw").color=Color.white;

            var expedition = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("ExpeditionUI/View"));SceneManager.MoveGameObjectToScene(expedition,scene);
            foreach(var script in expedition.GetComponentsInChildren<MonoBehaviour>(true))
                if(script is ExpeditionUI || script is ExpeditionSlot || script is GraphicRaycaster || script is CanvasScaler)script.enabled=false;
            var ui=expedition.GetComponent<ExpeditionUI>();ui.StyleQuickAccess();
            var quick=Field<GameObject>(ui,"hud");quick.SetActive(true);
            Field<Text>(ui,"hudLevel").text="LV. 07    180 / 300";Field<Text>(ui,"hudHint").text="강화 선택  [Tab]";Field<Text>(ui,"hudHint").color=ExpeditionTheme.Gold;Field<Image>(ui,"xpFill").fillAmount=.6f;
            var quickCells=Field<ExpeditionUI.Cell[]>(ui,"quickCells");
            for(int i=0;i<2;i++){
                var item=ItemCatalog.Load().Resolve(i==0?"health_potion":"vigor_potion_t1");
                ExpeditionTheme.Icon(quickCells[i].icon,item);quickCells[i].label.text=item.DisplayName;quickCells[i].count.text=i==0?"3":"2";
                quickCells[i].bg.color=ExpeditionTheme.Card;
            }
            font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","맑은 고딕","Arial"},20);
            foreach(var text in hud.GetComponentsInChildren<Text>(true).Concat(expedition.GetComponentsInChildren<Text>(true)))text.font=font;
            var canvases=new[]{canvas,expedition.GetComponent<Canvas>()};
            foreach(var dimensions in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)})
            {
                Capture(camera,canvases,hud,dimensions.x,dimensions.y,$"hud-{dimensions.x}x{dimensions.y}");
                foreach(var glyph in hud.GetComponentsInChildren<SkillHudGlyph>()) {
                    var mesh=glyph.canvasRenderer.GetMesh();Check(mesh!=null&&mesh.vertexCount>12,"skill glyph has rendered geometry");
                }
                Check(quickCells.All(c=>c.bg.isActiveAndEnabled&&c.label.isActiveAndEnabled),"quick item graphics remain visible");
                var profile=RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform,Field<RectTransform>(hud,"playerPanel"));
                var quickBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(expedition.transform,quick.transform);
                Check(profile.max.x+8<quickBounds.min.x,"profile and quick slots remain separate at "+dimensions);
                Check(profile.min.x>=-((RectTransform)canvas.transform).rect.width*.5f,"profile remains inside screen");
            }
            e.Refresh(.4f,8,true,true);q.Refresh(0,20,true,false);
            Check(Field<Text>(e,"countdown").text=="0.4"&&Field<Text>(q,"status").text=="사용 불가","final second and unavailable states");
            typeof(GameHUDOverlay).GetMethod("RefreshHp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,new object[]{0,120});
            Check(Field<Image>(hud,"hpFill").fillAmount==0,"empty health bar has no residual fill");
            Capture(camera,canvases,hud,1920,1080,"hud-unavailable");
            e.Refresh(0,8,false,false);Check(Field<Text>(e,"status").text=="대기 중","disconnected skills are not shown as ready");
            // Detailed render uses the exact same UI geometry at twice the canvas scale.
            quick.SetActive(false);Field<RectTransform>(hud,"timerPanel").gameObject.SetActive(false);
            typeof(GameHUDOverlay).GetMethod("RefreshHp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hud,new object[]{76,120});
            e.Refresh(0,8,true,true);q.Refresh(12,20,true,true);
            Capture(camera,new[]{canvas},hud,960,600,"hud-detail",2);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if(font!=null)UnityEngine.Object.DestroyImmediate(font);
            if(groundMaterial!=null)UnityEngine.Object.DestroyImmediate(groundMaterial);
            if(portraitTexture!=null){portraitTexture.Release();UnityEngine.Object.DestroyImmediate(portraitTexture);}
        }
        var after=EditorSceneManager.GetSceneManagerSetup();
        Check(previous.Length==after.Length&&previous.Zip(after,(a,b)=>a.path==b.path&&a.isActive==b.isActive&&a.isLoaded==b.isLoaded).All(x=>x),"open scenes preserved");
        File.WriteAllText(Output+"/validation.txt",$"PASS {checks}: actual HUD geometry and portrait rendered at 1080p/720p/4:3/ultrawide; cooldown/disabled/disconnected/empty HP states; no click blocking; open scenes preserved. No live multiplayer session used.");
    }
    static void Capture(Camera camera,Canvas[] canvases,GameHUDOverlay hud,int width,int height,string name,float forcedScale=0)
    {
        var rt=new RenderTexture(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=rt;
            foreach(var canvas in canvases){
                var scaler=canvas.GetComponent<CanvasScaler>();if(scaler!=null)scaler.enabled=false;
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;
                canvas.scaleFactor=forcedScale>0?forcedScale:Mathf.Sqrt(width/1920f*height/1080f);
            }
            Canvas.ForceUpdateCanvases();hud.ApplyLayout(new Rect(0,0,width,height),width,height);
            if(forcedScale>0)Field<RectTransform>(hud,"playerPanel").localScale=Vector3.one;
            Canvas.ForceUpdateCanvases();
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);}
    }
}
