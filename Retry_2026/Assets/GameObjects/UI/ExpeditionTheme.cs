using UnityEngine;
using UnityEngine.UI;

public static class ExpeditionTheme
{
    public static readonly Color Ink = new Color(.035f,.043f,.055f,.99f);
    public static readonly Color Card = new Color(.075f,.09f,.11f,1);
    public static readonly Color Gold = new Color(.83f,.69f,.44f,1);
    public static readonly Color White = new Color(.94f,.92f,.86f,1);
    public static readonly Color Muted = new Color(.52f,.58f,.63f,1);
    static Font font;
    public static Font Font => !Application.isPlaying ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") : font != null ? font : font = UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","맑은 고딕","Apple SD Gothic Neo","Noto Sans CJK KR","Arial"},20);
    public static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    {
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);
        r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;
    }
    public static Image Box(string name,Transform parent,float x,float y,float w,float h,Color color,bool raycast=false)
    {
        var image=Rect(name,parent,x,y,w,h).gameObject.AddComponent<Image>();
        image.sprite=Resources.Load<Sprite>("ExpeditionUI/Panel");image.type=Image.Type.Sliced;image.color=color;image.raycastTarget=raycast;return image;
    }
    public static Text Label(string name,Transform parent,float x,float y,float w,float h,int size,Color color,string text="",TextAnchor alignment=TextAnchor.MiddleLeft)
    {
        var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<Text>();t.font=Font;t.fontSize=size;t.color=color;
        t.text=text;t.alignment=alignment;t.raycastTarget=false;t.supportRichText=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
    }
    public static Button Button(string name,Transform parent,float x,float y,float w,float h,string text,bool primary=false)
    {
        var image=Box(name,parent,x,y,w,h,primary?Gold:Card,true);var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;
        var colors=b.colors;colors.highlightedColor=new Color(1.15f,1.15f,1.15f);colors.pressedColor=new Color(.8f,.8f,.8f);colors.disabledColor=new Color(.45f,.45f,.45f,.65f);b.colors=colors;
        Label("Label",b.transform,0,0,w-20,h,18,primary?Ink:White,text,TextAnchor.MiddleCenter);return b;
    }
    public static void Icon(Image image,ItemData item)
    {
        image.sprite=item!=null?item.Icon:null;image.preserveAspect=true;image.type=Image.Type.Simple;
        image.color=item==null?Color.clear:item.Icon!=null?Color.white:item.RarityColor;
    }
}
