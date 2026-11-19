using UnityEngine;
using UnityEngine.UI;

public static class TeamPalette
{
    private static readonly Color32[] Colors = {
        new(82,180,255,255), new(255,106,106,255), new(93,222,138,255), new(255,207,78,255),
        new(193,139,255,255), new(255,152,70,255), new(72,224,221,255), new(255,130,201,255),
        new(181,219,92,255), new(180,186,208,255)
    };
    public static Color Get(int team) => team < 0 ? Color.white : Colors[team % Colors.Length];
    public static string Plain(string text) => (text ?? "").Replace("<", "＜").Replace(">", "＞");
}

// World-space text uses the camera's depth test, so enemy names do not reveal actors through walls.
public sealed class PlayerNameplate : MonoBehaviour
{
    public static int LocalTeam = -1;
    private Text text;
    private Transform canvasRoot;
    private int team = -1;
    private string nickname = "";
    private Player_State state;
    private void Awake()
    {
        state=GetComponent<Player_State>();
        var go=new GameObject("Nickname",typeof(RectTransform),typeof(Canvas));
        go.transform.SetParent(transform,false); canvasRoot=go.transform;
        go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        canvasRoot.localPosition=new Vector3(0,2.35f,0); canvasRoot.localScale=Vector3.one*.008f;
        ((RectTransform)canvasRoot).sizeDelta=new Vector2(260,38);
        text=ExpeditionTheme.Label("Name",canvasRoot,0,0,260,38,22,Color.white,"");
        text.alignment=TextAnchor.MiddleCenter;text.supportRichText=false;text.raycastTarget=false;
        var outline=text.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.9f);
        outline.effectDistance=new Vector2(1,-1);
    }
    public void SetName(string value) { nickname=value ?? ""; }
    public void SetTeam(int value) { team=value; }
    private void LateUpdate()
    {
        var camera=Camera.main;
        bool visible=camera!=null && (state==null || !state.IsDead);
        if(camera!=null)visible &= Vector3.Distance(camera.transform.position,transform.position)<65;
        canvasRoot.gameObject.SetActive(visible);
        if(!visible)return;
        canvasRoot.rotation=camera.transform.rotation;
        text.color=TeamPalette.Get(team);
        text.text=nickname;
    }
}
