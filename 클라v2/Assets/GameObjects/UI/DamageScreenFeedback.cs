using UnityEngine;
using UnityEngine.UI;

/// <summary>Local HP deltas only. Repeated snapshots, healing and maximum-HP clamps do not flash.</summary>
public sealed class DamageFeedbackPulse
{
    int previousHp; bool initialized; float hold;
    public float Strength { get; private set; }
    public void Reset() { initialized=false; Strength=hold=0; }
    public void Observe(int hp, int maximum)
    {
        maximum=Mathf.Max(1,maximum);hp=Mathf.Clamp(hp,0,maximum);
        int damage=initialized?Mathf.Min(previousHp,maximum)-hp:0;
        previousHp=hp;initialized=true;
        if(damage<=0)return;
        Strength=Mathf.Min(.72f,Mathf.Max(Strength,.28f+Mathf.Sqrt(damage/(float)maximum)*.55f)+Strength*.12f);
        hold=.07f;
    }
    public void Step(float dt)
    {
        float fade=Mathf.Max(0,dt-hold);hold=Mathf.Max(0,hold-dt);
        Strength=Mathf.MoveTowards(Strength,0,fade*.95f);
    }
}

public sealed class DamageScreenFeedback : MonoBehaviour
{
    NetworkBootstrap bootstrap;Player_State owner;CanvasGroup opacity;GameObject surface;
    readonly DamageFeedbackPulse pulse=new();
    void Awake(){bootstrap=GetComponent<NetworkBootstrap>();}
    void Update()
    {
        var next=bootstrap!=null&&bootstrap.LocalPlayer!=null?bootstrap.LocalPlayer.GetComponent<Player_State>():null;
        if(next!=owner){Unbind();owner=next;if(owner!=null){pulse.Observe(owner.CurrentHp,owner.MaxHp);owner.HpChanged+=OnHp;}}
        if(owner==null)return;
        if(surface==null)Build();pulse.Step(Time.unscaledDeltaTime);opacity.alpha=pulse.Strength;
    }
    void OnHp(int hp,int maximum)=>pulse.Observe(hp,maximum);
    void Build()
    {
        surface=new GameObject("Damage · screen edge",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup));
        surface.transform.SetParent(transform,false);
        var canvas=surface.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=90;
        opacity=surface.GetComponent<CanvasGroup>();opacity.blocksRaycasts=false;opacity.interactable=false;opacity.alpha=0;
        var graphic=new GameObject("Soft red edge",typeof(RectTransform),typeof(CanvasRenderer),typeof(DamageVignetteGraphic)).GetComponent<DamageVignetteGraphic>();
        graphic.transform.SetParent(surface.transform,false);graphic.rectTransform.anchorMin=Vector2.zero;graphic.rectTransform.anchorMax=Vector2.one;
        graphic.rectTransform.offsetMin=graphic.rectTransform.offsetMax=Vector2.zero;graphic.raycastTarget=false;
    }
    void Unbind(){if(owner!=null)owner.HpChanged-=OnHp;owner=null;pulse.Reset();if(opacity!=null)opacity.alpha=0;}
    void OnDisable()=>Unbind();
    void OnDestroy(){Unbind();if(surface!=null)Destroy(surface);}
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class DamageVignetteGraphic : MaskableGraphic
{
    public DamageVignetteGraphic(){useLegacyMeshGeneration=false;}
    protected override void Awake(){base.Awake();raycastTarget=false;}
    public static float EdgeAlpha(float x,float y,float aspect)
    {
        float horizontal=Mathf.Clamp01((.23f-Mathf.Min(x,1-x)*aspect)/.23f);
        float vertical=Mathf.Clamp01((.23f-Mathf.Min(y,1-y))/.23f);
        float edge=1-(1-horizontal*horizontal)*(1-vertical*vertical);
        return edge;
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();var rect=rectTransform.rect;const int columns=48,rows=30;
        for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++){
            float u=x/(float)columns,v=y/(float)rows;
            var tint=new Color(.72f,.012f,.025f,EdgeAlpha(u,v,rect.width/Mathf.Max(1,rect.height))*color.a);
            vh.AddVert(new Vector3(rect.xMin+u*rect.width,rect.yMin+v*rect.height),tint,new Vector2(u,v));
            if(x==columns||y==rows)continue;int n=y*(columns+1)+x;
            vh.AddTriangle(n,n+1,n+columns+1);vh.AddTriangle(n+1,n+columns+2,n+columns+1);
        }
    }
}
