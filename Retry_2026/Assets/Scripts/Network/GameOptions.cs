using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public sealed class GameOptions:MonoBehaviour {
    static GameOptions instance;
    public static float BlurStrength=>Mathf.Clamp(PlayerPrefs.GetFloat("options.blurStrength",.35f),.05f,1f);
    public static float Sensitivity=>PlayerPrefs.GetFloat("options.sensitivity",1f);
    public static bool IsOpen=>instance!=null&&instance.root!=null&&instance.root.activeSelf;
    public static int LastCloseFrame {get;private set;}=-1;
    GameObject root;Text sensitivityText,resolutionText,modeText,blurText,strengthText,confirmation;
    Vector2Int[] resolutions;int resolutionIndex;bool fullscreen,blur;float sensitivity;
    float confirmDeadline;int oldWidth,oldHeight;FullScreenMode oldMode;
    Volume volume;VolumeProfile profile;MotionBlur motion;
    readonly Dictionary<UniversalAdditionalCameraData,bool> cameraFlags=new();
    void Awake(){instance=this;ApplyBlur(PlayerPrefs.GetInt("options.blur",1)==1);
#if !UNITY_EDITOR
        int w=PlayerPrefs.GetInt("options.width",0),h=PlayerPrefs.GetInt("options.height",0);
        if(w>=800&&h>=600)Screen.SetResolution(w,h,PlayerPrefs.GetInt("options.fullscreen",1)==1?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
#endif
    }
    public static void Open(){if(instance==null)return;instance.Build();instance.root.SetActive(true);instance.Load();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
    void Load(){sensitivity=Sensitivity;blur=PlayerPrefs.GetInt("options.blur",1)==1;fullscreen=Screen.fullScreen;resolutionIndex=Array.FindIndex(resolutions,r=>r.x==Screen.width&&r.y==Screen.height);if(resolutionIndex<0)resolutionIndex=resolutions.Length-1;Refresh();}
    void Build(){if(root!=null)return;
        resolutions=Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height)).Append(new Vector2Int(Screen.width,Screen.height)).Where(r=>r.x>=800&&r.y>=600).Distinct().OrderBy(r=>r.x).ThenBy(r=>r.y).ToArray();
        if(resolutions.Length==0)resolutions=new[]{new Vector2Int(1280,720)};
        root=new GameObject("Options",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
        root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;root.GetComponent<Canvas>().sortingOrder=450;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=.5f;
        var shade=ExpeditionTheme.Box("Shade",root.transform,0,0,4000,4000,new Color(0,0,0,.8f),true);shade.sprite=null;shade.type=Image.Type.Simple;
        var panel=ExpeditionTheme.Box("Panel",root.transform,0,0,740,660,ExpeditionTheme.Ink,true).transform;
        ExpeditionTheme.Label("Title",panel,-220,275,220,50,30,ExpeditionTheme.White,"설정");
        ExpeditionTheme.Button("Close",panel,305,275,44,44,"×").onClick.AddListener(Close);
        sensitivityText=Row(panel,"마우스 감도",155,()=>{sensitivity=Mathf.Max(.2f,sensitivity-.1f);SaveSensitivity();},()=>{sensitivity=Mathf.Min(3,sensitivity+.1f);SaveSensitivity();});
        resolutionText=Row(panel,"해상도",75,()=>{resolutionIndex=(resolutionIndex+resolutions.Length-1)%resolutions.Length;Refresh();},()=>{resolutionIndex=(resolutionIndex+1)%resolutions.Length;Refresh();});
        modeText=Row(panel,"화면 모드",-5,ToggleMode,ToggleMode);
        blurText=Row(panel,"카메라 모션 블러",-85,ToggleBlur,ToggleBlur);
        strengthText=Row(panel,"블러 강도",-155,()=>ChangeStrength(-.05f),()=>ChangeStrength(.05f));
        confirmation=ExpeditionTheme.Label("Confirmation",panel,0,-210,640,42,17,ExpeditionTheme.Gold,"");
        ExpeditionTheme.Button("Apply",panel,-140,-275,245,46,"화면 설정 적용").onClick.AddListener(ApplyDisplay);
        ExpeditionTheme.Button("Keep",panel,140,-275,245,46,"확인").onClick.AddListener(()=>{if(confirmDeadline>0)KeepDisplay();else Close();});
    }
    Text Row(Transform parent,string title,float y,Action prev,Action next){
        ExpeditionTheme.Label(title,parent,-175,y,290,44,21,ExpeditionTheme.White,title);
        var value=ExpeditionTheme.Label(title+" value",parent,160,y,190,44,20,ExpeditionTheme.Gold,"");
        ExpeditionTheme.Button(title+" previous",parent,40,y,40,40,"‹").onClick.AddListener(()=>prev());
        ExpeditionTheme.Button(title+" next",parent,290,y,40,40,"›").onClick.AddListener(()=>next());return value;
    }
    void SaveSensitivity(){PlayerPrefs.SetFloat("options.sensitivity",sensitivity);PlayerPrefs.Save();Refresh();}
    void ChangeStrength(float change){PlayerPrefs.SetFloat("options.blurStrength",Mathf.Clamp(BlurStrength+change,.05f,1f));PlayerPrefs.Save();ApplyBlur(blur);Refresh();}
    void ToggleMode(){fullscreen=!fullscreen;Refresh();}
    void ToggleBlur(){blur=!blur;PlayerPrefs.SetInt("options.blur",blur?1:0);PlayerPrefs.Save();ApplyBlur(blur);Refresh();}
    void Refresh(){sensitivityText.text=sensitivity.ToString("0.0")+"×";var r=resolutions[resolutionIndex];resolutionText.text=$"{r.x} × {r.y}";modeText.text=fullscreen?"전체 화면":"창 모드";blurText.text=blur?"켜짐":"꺼짐";strengthText.text=$"{BlurStrength*100:0}%";}
    void ApplyDisplay(){
#if UNITY_EDITOR
        confirmation.text="화면 크기는 빌드에서 적용됩니다.";
#else
        if(confirmDeadline>0)RevertDisplay();oldWidth=Screen.width;oldHeight=Screen.height;oldMode=Screen.fullScreenMode;
        var r=resolutions[resolutionIndex];Screen.SetResolution(r.x,r.y,fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);confirmDeadline=Time.unscaledTime+15;
#endif
    }
    void KeepDisplay(){confirmDeadline=0;var r=resolutions[resolutionIndex];PlayerPrefs.SetInt("options.width",r.x);PlayerPrefs.SetInt("options.height",r.y);PlayerPrefs.SetInt("options.fullscreen",fullscreen?1:0);PlayerPrefs.Save();confirmation.text="";}
    void RevertDisplay(){if(confirmDeadline<=0)return;confirmDeadline=0;Screen.SetResolution(oldWidth,oldHeight,oldMode);if(confirmation!=null)confirmation.text="이전 화면 설정으로 복원했습니다.";}
    void Close(){RevertDisplay();root.SetActive(false);LastCloseFrame=Time.frameCount;}
    void Update(){if(!IsOpen)return;if(confirmDeadline>0){confirmation.text=$"화면 설정 유지?  {Mathf.CeilToInt(confirmDeadline-Time.unscaledTime)}초";if(Time.unscaledTime>=confirmDeadline)RevertDisplay();}
        if(UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)Close();}
    void ApplyBlur(bool enabled){
        if(volume==null){var go=new GameObject("Camera motion blur");go.transform.SetParent(transform,false);volume=go.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;
            var asset=Resources.Load<VolumeProfile>("Presentation/CameraOptions");profile=ScriptableObject.CreateInstance<VolumeProfile>();
            if(asset!=null)foreach(var component in asset.components)if(component!=null)profile.components.Add(Instantiate(component));
            if(!profile.TryGet(out motion))motion=profile.Add<MotionBlur>(true);volume.sharedProfile=profile;RenderPipelineManager.beginCameraRendering+=CameraRendering;}
        if(!enabled)RestoreCameras();
        motion.active=true;motion.intensity.Override(enabled ? BlurStrength : 0f);motion.clamp.Override(.035f);motion.mode.Override(MotionBlurMode.CameraOnly);
    }
    void CameraRendering(ScriptableRenderContext context,Camera camera){if(motion.intensity.value>0&&camera.cameraType==CameraType.Game&&camera.CompareTag("MainCamera")){var data=camera.GetUniversalAdditionalCameraData();if(!cameraFlags.ContainsKey(data))cameraFlags[data]=data.renderPostProcessing;data.renderPostProcessing=true;}}
    void RestoreCameras(){foreach(var entry in cameraFlags)if(entry.Key!=null)entry.Key.renderPostProcessing=entry.Value;cameraFlags.Clear();}
    void OnDestroy(){RestoreCameras();RevertDisplay();RenderPipelineManager.beginCameraRendering-=CameraRendering;if(profile!=null){foreach(var component in profile.components)if(component!=null)Destroy(component);Destroy(profile);}if(instance==this)instance=null;}
}
