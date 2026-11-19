using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct StormState { public int version,stage;public float x,z,radius,target;public long time,end,next; }
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct StormDamage { public int version,victimId,hp; }

public sealed class StormClient : MonoBehaviour
{
    public const int Packet=205;
    public const int DamagePacket=243;
    public StormState State {get;private set;}
    public float CurrentRadius {get;private set;}
    private NetworkBootstrap bootstrap;
    private Mesh mesh;
    private MeshRenderer boundary;
    private Material material;
    private GameObject hud;
    private Image outsideOverlay;
    public bool HasState=>received;
    public bool IsOutside {get;private set;}
    public string StatusText {get;private set;}="자기장 정보 대기 중";
    public string CountdownText {get;private set;}="—";
    public float Progress {get;private set;}
    private bool received;
    private long phaseStart;
    private bool wasShrinking;
    private void Awake(){bootstrap=GetComponent<NetworkBootstrap>();}
    public void Receive(byte[] bytes)
    {
        if(bytes==null||bytes.Length!=48)return;
        var s=PacketIO.BytesToStruct<StormState>(bytes,0);
        if(s.version!=1||s.stage<0||s.stage>4||s.time<State.time||!float.IsFinite(s.radius)||s.radius<12||!float.IsFinite(s.x)||!float.IsFinite(s.z)||!float.IsFinite(s.target))return;
        bool shrinking=s.end>s.time;
        if(!received || s.stage!=State.stage || shrinking!=wasShrinking)phaseStart=s.time;
        wasShrinking=shrinking;State=s;received=true;
        if(boundary==null)Build();
    }
    private void Build()
    {
        var go=new GameObject("Storm energy wall",typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(transform,false);
        const int segments=192;
        var vertices=new Vector3[(segments+1)*2];var uv=new Vector2[vertices.Length];var triangles=new int[segments*6];
        for(int i=0;i<=segments;i++){
            float a=i*Mathf.PI*2/segments;
            vertices[i*2]=new Vector3(Mathf.Cos(a),-20,Mathf.Sin(a));
            vertices[i*2+1]=new Vector3(Mathf.Cos(a),80,Mathf.Sin(a));
            uv[i*2]=new Vector2(i/(float)segments,0);uv[i*2+1]=new Vector2(i/(float)segments,1);
            if(i==segments)continue;int v=i*2,t=i*6;
            triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;
            triangles[t+3]=v+2;triangles[t+4]=v+1;triangles[t+5]=v+3;
        }
        mesh=new Mesh{name="Storm cylinder"};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        material=new Material(Resources.Load<Shader>("StormBoundary"));
        boundary=go.GetComponent<MeshRenderer>();boundary.sharedMaterial=material;
        boundary.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;boundary.receiveShadows=false;
        hud=new GameObject("Storm HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));hud.transform.SetParent(transform,false);
        hud.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;hud.GetComponent<Canvas>().sortingOrder=80;
        var scaler=hud.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);
        var overlay=new GameObject("Outside zone screen effect",typeof(RectTransform),typeof(Image));overlay.transform.SetParent(hud.transform,false);
        outsideOverlay=overlay.GetComponent<Image>();outsideOverlay.raycastTarget=false;
        var rect=outsideOverlay.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
    }
    private void Update()
    {
        bool connected=bootstrap.Session!=null&&bootstrap.Session.IsConnected;
        bool active=received&&connected&&bootstrap.LocalPlayer!=null;
        if(boundary!=null)boundary.enabled=active;if(hud!=null)hud.SetActive(active);
        if(!connected){received=false;State=default;CurrentRadius=0;}
        if(!active)return;
        long now=ServerClock.NowMs;
        float t=State.end<=State.time?1:Mathf.Clamp01((float)(now-State.time)/(State.end-State.time));
        CurrentRadius=Mathf.Lerp(State.radius,State.target,t);
        boundary.transform.position=new Vector3(State.x,0,State.z);boundary.transform.localScale=new Vector3(CurrentRadius,1,CurrentRadius);
        var p=bootstrap.LocalPlayer.transform.position;
        float distance=new Vector2(p.x-State.x,p.z-State.z).magnitude;
        bool outside=distance>CurrentRadius;
        var health=bootstrap.LocalPlayer.GetComponent<Player_State>();
        outsideOverlay.color=new Color(.12f,.32f,.85f,outside && (health==null||!health.IsDead)? .18f+.035f*Mathf.Sin(Time.unscaledTime*3):0);
        bool shrinking=now<State.end;long deadline=shrinking?State.end:State.next;
        IsOutside=outside;
        StatusText=outside?"자기장 밖 · 안전 구역으로 이동":$"{State.stage}/4 단계 · "+(shrinking?"안전 구역 축소 중":State.next>0?"다음 축소까지":"최종 안전 구역");
        CountdownText=outside?$"{Mathf.CeilToInt(distance-CurrentRadius)} m → 안전 구역":deadline>0?TimeSpan.FromMilliseconds(Math.Max(0,deadline-now)).ToString(@"mm\:ss"):$"반경 {CurrentRadius:0} m";
        Progress=deadline>0?Mathf.Clamp01((float)(deadline-now)/Math.Max(1,deadline-phaseStart)):1;
    }
    private void OnDestroy(){if(material!=null)Destroy(material);if(mesh!=null)Destroy(mesh);}
}
