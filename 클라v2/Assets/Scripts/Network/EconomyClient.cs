using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct EconomyCommand { public int version,sequence,action,itemHash,count,slot; }
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct EconomyEntry { public int hash,count,sell,buy; }
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct EconomySnapshot {
    public int version,sequence,result;public long credits;public int weapon,item,inRun,count;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=96)] public EconomyEntry[] entries;
}

public sealed class EconomyClient:MonoBehaviour {
    public const int CommandPacket=210,StatePacket=211;
    public static EconomyClient Active {get;private set;}
    public EconomySnapshot State {get;private set;}
    public bool Supported {get;private set;}
    public static int LastCloseFrame {get;private set;}=-1;
    public static int LastInventoryCloseFrame {get;set;}=-1;
    public event Action Changed;
    public bool Ready=>Supported&&pendingUntil==0&&State.inRun==0;
    public bool IsOpen=>root!=null&&root.activeSelf;
    NetworkBootstrap bootstrap;GameObject root;RectTransform panel;Text balance,message,detail,loadout;
    Image detailIcon;Button buy,sell,equip,quantityButton,abandon;readonly Button[] cells=new Button[8];
    int sequence,page,selected,quantity=1;bool shop,requested,confirmAbandon;float pendingUntil;
    void Awake(){Active=this;bootstrap=GetComponent<NetworkBootstrap>();bootstrap.LobbyPacket+=Packet;}
    void OnDestroy(){if(bootstrap!=null)bootstrap.LobbyPacket-=Packet;if(Active==this)Active=null;}
    void Packet(PacketType type,byte[] bytes){if((int)type==StatePacket)Receive(bytes);}
    public static bool TryDecode(byte[] bytes,out EconomySnapshot data){
        data=default;if(bytes==null||(bytes.Length!=1060&&bytes.Length!=1572))return false;
        int capacity=bytes.Length==1060?64:96;
        int version=BitConverter.ToInt32(bytes,0);
        if(version!=(capacity==64?1:2))return false;
        if(capacity==64){var padded=new byte[1572];Array.Copy(bytes,padded,1060);bytes=padded;}
        data=PacketIO.BytesToStruct<EconomySnapshot>(bytes,0);
        return data.count>=0&&data.count<=capacity&&data.credits>=0;
    }
    public void Receive(byte[] bytes){
        if(!TryDecode(bytes,out var data)||data.sequence<State.sequence)return;
        pendingUntil=0;sequence=Math.Max(sequence,data.sequence);
        if(data.result!=5){State=data;Supported=true;}
        string feedback=data.result switch {0=>"",1=>"처리할 수 없는 요청입니다.",2=>"재화가 부족합니다.",3=>"보관 중인 아이템이 부족합니다.",4=>"진행 중인 출격이 있습니다.",_=>"저장소에 연결할 수 없습니다."};
        if(message!=null)message.text=feedback;
        if(data.result!=0&&ExpeditionUI.Active?.LoadoutMode==true)ExpeditionUI.Active.Notify(feedback);
        Refresh();
    }
    public void Request(int action,int hash=0,int count=1,int slot=0){
        if(bootstrap.Lobby==null||!bootstrap.Lobby.IsConnected||bootstrap.Identity==null||!bootstrap.Identity.IsAuthenticated)return;
        pendingUntil=Time.unscaledTime+4;
        bootstrap.Lobby.SendEconomy(new EconomyCommand{version=1,sequence=++sequence,action=action,itemHash=hash,count=count,slot=slot});Refresh();
    }
    void Update(){
        bool online=bootstrap.Lobby!=null&&bootstrap.Lobby.IsConnected&&bootstrap.Identity!=null&&bootstrap.Identity.IsAuthenticated;
        if(online&&!requested){requested=true;Request(0);}
        if(!online){requested=false;Supported=false;if(IsOpen)Close();}
        if(bootstrap.Session!=null&&bootstrap.Session.IsConnected&&IsOpen)Close();
        if(pendingUntil>0&&Time.unscaledTime>pendingUntil){pendingUntil=0;if(message!=null)message.text="응답 지연 · 새로고침해 주세요.";if(ExpeditionUI.Active?.LoadoutMode==true)ExpeditionUI.Active.Notify("응답 지연 · 상점에서 새로고침해 주세요.");Refresh();}
        if(IsOpen&&ExpeditionUI.Active?.LoadoutMode!=true&&LastInventoryCloseFrame!=Time.frameCount&&UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)Close();
    }
    public static void Open(){if(Active!=null){Active.Build();Active.root.SetActive(true);Active.confirmAbandon=false;Active.Request(0);Active.Refresh();}}
    public void Close(){if(ExpeditionUI.Active?.LoadoutMode==true)ExpeditionUI.Active.CloseLoadout();if(root!=null)root.SetActive(false);LastCloseFrame=Time.frameCount;}
    void Build(){
        if(root!=null)return;
        root=new GameObject("Stash and outfitter",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
        root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;root.GetComponent<Canvas>().sortingOrder=350;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=.5f;
        var dim=ExpeditionTheme.Box("Shade",root.transform,0,0,4000,4000,new Color(0,0,0,.8f),true);dim.sprite=null;dim.type=Image.Type.Simple;
        panel=ExpeditionTheme.Box("Panel",root.transform,0,0,1080,730,ExpeditionTheme.Ink,true).rectTransform;
        ExpeditionTheme.Label("Title",panel,-342,310,320,48,30,ExpeditionTheme.White,"보관함 / 상점");
        balance=ExpeditionTheme.Label("Credits",panel,290,310,340,48,24,ExpeditionTheme.Gold,"—",TextAnchor.MiddleRight);
        ExpeditionTheme.Button("Close",panel,493,310,46,44,"×").onClick.AddListener(Close);
        ExpeditionTheme.Button("Stash",panel,-420,250,170,40,"보관함").onClick.AddListener(()=>{shop=false;page=0;Refresh();});
        ExpeditionTheme.Button("Shop",panel,-235,250,170,40,"상점").onClick.AddListener(()=>{shop=true;page=0;Refresh();});
        ExpeditionTheme.Button("Refresh",panel,-50,250,160,40,"새로고침").onClick.AddListener(()=>Request(0));
        for(int i=0;i<cells.Length;i++){
            int slot=i;float x=-350+(i%2)*330,y=165-(i/2)*112;
            var image=ExpeditionTheme.Box("Item "+i,panel,x,y,314,102,ExpeditionTheme.Card,true);var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;cells[i]=b;
            ExpeditionTheme.Rect("Icon",b.transform,-112,0,70,70).gameObject.AddComponent<Image>().raycastTarget=false;
            ExpeditionTheme.Label("Name",b.transform,32,18,205,38,18,ExpeditionTheme.White);
            ExpeditionTheme.Label("Count",b.transform,32,-23,205,28,14,ExpeditionTheme.Muted);
            b.onClick.AddListener(()=>{var entries=Entries();int index=page*8+slot;if(index<entries.Length){selected=entries[index].hash;quantity=1;Refresh();}});
        }
        ExpeditionTheme.Button("Previous",panel,-420,-240,100,36,"‹").onClick.AddListener(()=>{page=Mathf.Max(0,page-1);Refresh();});
        ExpeditionTheme.Button("Next",panel,-80,-240,100,36,"›").onClick.AddListener(()=>{if((page+1)*8<Entries().Length)page++;Refresh();});
        var right=ExpeditionTheme.Box("Details",panel,330,7,340,525,ExpeditionTheme.Card).rectTransform;
        detailIcon=ExpeditionTheme.Rect("Icon",right,0,160,106,106).gameObject.AddComponent<Image>();detailIcon.raycastTarget=false;
        detail=ExpeditionTheme.Label("Details",right,0,12,296,185,18,ExpeditionTheme.White,"아이템을 선택하세요.",TextAnchor.UpperLeft);
        detail.resizeTextForBestFit=true;detail.resizeTextMinSize=15;detail.resizeTextMaxSize=18;
        quantityButton=ExpeditionTheme.Button("Quantity",right,0,-109,290,34,"수량 1");quantityButton.onClick.AddListener(()=>{quantity=quantity==1?3:quantity==3?5:1;Refresh();});
        buy=ExpeditionTheme.Button("Buy",right,-76,-158,140,44,"구매",true);buy.onClick.AddListener(()=>Request(1,selected,quantity));
        sell=ExpeditionTheme.Button("Sell",right,76,-158,140,44,"판매");sell.onClick.AddListener(()=>Request(2,selected,quantity));
        equip=ExpeditionTheme.Button("Equip",right,0,-214,292,44,"인벤토리에서 장착");equip.onClick.AddListener(()=>ExpeditionUI.Ensure().OpenLoadout(this));
        loadout=ExpeditionTheme.Label("Loadout",panel,-186,-300,640,62,17,ExpeditionTheme.White);
        ExpeditionTheme.Button("Inventory",panel,280,-298,310,44,"출격 인벤토리 열기").onClick.AddListener(()=>ExpeditionUI.Ensure().OpenLoadout(this));
        message=ExpeditionTheme.Label("Feedback",panel,0,-342,1000,26,15,ExpeditionTheme.Gold,"",TextAnchor.MiddleCenter);
        abandon=ExpeditionTheme.Button("Abandon",panel,260,250,480,40,"이전 출격 포기");abandon.onClick.AddListener(()=>{if(!confirmAbandon){confirmAbandon=true;abandon.GetComponentInChildren<Text>().text="장비를 잃고 출격을 포기합니다 · 확인";}else{confirmAbandon=false;Request(4);}});
    }
    EconomyEntry[] Entries(){if(State.entries==null)return Array.Empty<EconomyEntry>();return State.entries.Take(State.count).Where(e=>shop?e.buy>0:e.count>0).OrderBy(e=>ItemCatalog.Load()?.Resolve(e.hash)?.DisplayName).ToArray();}
    string Name(int hash,string fallback)=>hash==0?fallback:ItemCatalog.Load()?.Resolve(hash)?.DisplayName??"아이템";
    void Refresh(){
        Changed?.Invoke();
        if(root==null)return;balance.text=Supported?$"{State.credits:N0} G":"—";
        loadout.text=$"출격 무기  {Name(State.weapon,"일반 검")}\n출격 아이템  {Name(State.item,"체력 물약 ×3")}";
        var entries=Entries();page=Mathf.Clamp(page,0,Mathf.Max(0,(entries.Length-1)/8));
        for(int i=0;i<cells.Length;i++){int index=page*8+i;cells[i].gameObject.SetActive(index<entries.Length);if(index>=entries.Length)continue;var e=entries[index];var item=ItemCatalog.Load()?.Resolve(e.hash);
            ExpeditionTheme.Icon(cells[i].transform.Find("Icon").GetComponent<Image>(),item);cells[i].transform.Find("Name").GetComponent<Text>().text=item!=null?item.DisplayName:"아이템";
            cells[i].transform.Find("Count").GetComponent<Text>().text=shop?$"{e.buy:N0} G  ·  보관 {e.count}":$"×{e.count}  ·  판매 {e.sell:N0} G";
            bool assigned=e.hash==State.weapon||e.hash==State.item;
            if(assigned)cells[i].transform.Find("Count").GetComponent<Text>().text="출격 장비  ·  "+cells[i].transform.Find("Count").GetComponent<Text>().text;
            cells[i].GetComponent<Image>().color=assigned?new Color(.26f,.21f,.08f):e.hash==selected?new Color(.19f,.19f,.15f):ExpeditionTheme.Card;
        }
        var selectedItem=ItemCatalog.Load()?.Resolve(selected);var selectedEntry=State.entries?.Take(State.count).FirstOrDefault(e=>e.hash==selected)??default;
        ExpeditionTheme.Icon(detailIcon,selectedItem);detail.text=selectedItem!=null?$"{selectedItem.DisplayName}\n{selectedItem.RarityName}\n\n{selectedItem.Description}\n\n판매 {selectedEntry.sell:N0} G / 개":"아이템을 선택하세요.";
        bool ready=Supported&&pendingUntil==0&&State.inRun==0;buy.interactable=ready&&selectedEntry.buy>0&&State.credits>=(long)selectedEntry.buy*quantity;sell.interactable=ready&&selectedEntry.count>=quantity;
        equip.interactable=Supported;quantityButton.GetComponentInChildren<Text>().text=$"수량 {quantity}";
        abandon.gameObject.SetActive(Supported&&State.inRun!=0);abandon.interactable=pendingUntil==0;
    }
}
