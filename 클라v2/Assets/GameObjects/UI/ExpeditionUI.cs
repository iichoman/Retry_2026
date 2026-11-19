using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using T=ExpeditionTheme;

/// <summary>Inventory and ability presentation. All online mutations remain AB requests.</summary>
public sealed class ExpeditionUI:MonoBehaviour
{
    public static ExpeditionUI Active {get;private set;}
    public static int LastCloseFrame {get;private set;}=-1;
    public static ExpeditionUI Ensure()
    {
        if(Active!=null)return Active;
        var prefab=Resources.Load<GameObject>("ExpeditionUI/View");
        if(prefab!=null)return Instantiate(prefab).GetComponent<ExpeditionUI>();
        return new GameObject("Expedition UI").AddComponent<ExpeditionUI>();
    }
    [Serializable] public class Cell { public Image bg,icon,accent;public Text count,label,tag; }
    [SerializeField] GameObject inventoryPanel,upgradePanel,veil,hud;
    [SerializeField] Text capacity,value,equipped,passive,detailName,detailMeta,detailBody,detailValue,feedback,hudLevel,hudHint,upgradeSubtitle;
    [SerializeField] Image detailIcon,xpFill;
    [SerializeField] Button action,closeInventory,closeUpgrade;
    [SerializeField] Button[] assign=new Button[4],upgradeButtons=new Button[3];
    [SerializeField] Text[] upgradeTitles=new Text[3],upgradeDescriptions=new Text[3];
    [SerializeField] Image[] upgradeIcons=new Image[3];
    [SerializeField] Cell[] cells=new Cell[ItemCatalog.InventoryCapacity],quickCells=new Cell[4];
    [SerializeField] Text assignmentHint;
    Image equippedWeaponIcon, equippedItemIcon;
    int weaponHash, weaponOccurrence, passiveHash, passiveOccurrence;
    int pendingEquipHash, pendingEquipOccurrence, pendingDroppedHash, pendingDroppedOccurrence;
    PlayerInventory inventory;
    PlayerEquipment equipment;
    GameUIController controller;
    Defalult_Input input;
    ABClient client;
    int selected=-1,pendingOffer,pendingSequence,pendingItemSequence;
    readonly int[] quickHashes=new int[4];
    int lastHashInventory;
    float refreshAt,feedbackUntil,pendingUntil;
    bool wasSupported,wired,quickStyled;
    Button dropButton,dropLess,dropMore,dropAll;Text dropQuantity;
    int dropCount=1,dropHash,pendingDropSequence;float dropPendingUntil;
    EconomyClient outfitter;
    readonly List<InventorySlot> storedSlots = new();
    int stashPage;
    Text statsTitle;
    readonly Text[] statValues = new Text[6];
    Button stashPrevious,stashNext,defaultWeapon,defaultItem;
    public bool LoadoutMode => outfitter != null;
    public bool InventoryOpen=>inventoryPanel!=null&&inventoryPanel.activeSelf;
    public bool UpgradeOpen=>upgradePanel!=null&&upgradePanel.activeSelf;
    public int QuickHash(int index)=>index>=0&&index<4?quickHashes[index]:0;
    static readonly string[] SkillNames={"생명력","지구력","강인한 일격","전투의 리듬","거인의 검","두 번째 숨결","깊은 상처","배움의 본능",
        "시간 감각","비전 검술","파동 확장","속성 공명","사냥의 순환","집중 시전","대검 숙련","진격의 회전","심판의 무게","회전 가속","신속한 심판","칼날 폭풍","거인의 심판"};
    static readonly string[] SkillStats={"최대 체력 +10","최대 스태미나 +10","공격력 +8%","공격 속도 +5%","무기 크기 · 공격 범위 +5%","스태미나 회복 +2 / 초","화상·독·냉각 지속 시간 +10%","획득 경험치 +10%",
        "스킬 쿨타임 -5%\n최대 6회 · 합산 한도 50%","E/Q 위력 +10%\n최대 6회",
        "E 폭·사거리 / Q 반경 +6%\n최대 5회 · 합산 한도 60%","상태이상 대상에 스킬 피해 +8%\n최대 5회",
        "스킬 직접 처치 시 E/Q 대기 -0.35초\n최대 5회","E/Q 선딜레이 -5%\n최대 5회 · 시전 속도 최대 2배",
        "대검 공격력 +8%\n최대 5회","대검 E 회전 중 이동 가능\n1회 선택","대검 Q 피해 +15%\n최대 5회","대검 E 쿨타임 -5%\n합산 한도 50%","대검 Q 쿨타임 -5%\n합산 한도 50%","대검 E 반경 +8%\n최대 5회 · 합산 한도 100%","대검 Q 반경 +8%\n최대 5회 · 합산 한도 100%"};
    static readonly string[] SkillItems={"life_amulet_t1","vigor_ring_t1","sword_001","duelist_knot_t1","sword_001","vigor_potion_t1","poison_thorn_t1","rare_gem",
        "chronometer_t1","runic_focus_t1","arc_prism_t1","ember_charm_t1","echo_orb_t1","focus_tonic_t1","greatsword_001","greatsword_001","greatsword_001","chronometer_t1","chronometer_t1","arc_prism_t1","arc_prism_t1"};

    void Awake(){if(Active!=null&&Active!=this){Destroy(gameObject);return;}Active=this;Build();StyleQuickAccess();StyleUpgradeCards();StyleInventory();foreach(var label in GetComponentsInChildren<Text>(true))label.font=T.Font;Wire();}
    void Start(){controller=FindFirstObjectByType<GameUIController>();}
    void OnDestroy(){if(outfitter!=null)outfitter.Changed-=RefreshStoredItems;if(inventory!=null)inventory.InventoryChanged-=Changed;if(client!=null)client.Changed-=ServerChanged;if(Active==this)Active=null;}
    public void Bind(PlayerInventory owner,PlayerEquipment gear)
    {
        if (LoadoutMode) return;
        if(inventory!=owner){if(inventory!=null)inventory.InventoryChanged-=Changed;inventory=owner;if(owner!=null)owner.InventoryChanged+=Changed;selected=-1;weaponHash=passiveHash=weaponOccurrence=passiveOccurrence=0;}
        equipment=gear;input=owner!=null?owner.GetComponent<Defalult_Input>():null;Refresh();
    }
    void Changed(PlayerInventory _)=>Refresh();
    public void OpenLoadout(EconomyClient shop)
    {
        if (outfitter != null) outfitter.Changed -= RefreshStoredItems;
        outfitter = shop; stashPage = 0; selected = -1;
        outfitter.Changed += RefreshStoredItems;
        GetComponent<Canvas>().sortingOrder = 380;
        inventoryPanel.SetActive(true); CloseUpgrade(); RefreshVeil(); RefreshStoredItems();
    }
    public void CloseLoadout()
    {
        if (outfitter != null) outfitter.Changed -= RefreshStoredItems;
        outfitter = null; storedSlots.Clear(); selected = -1;
        GetComponent<Canvas>().sortingOrder = 160; inventoryPanel.SetActive(false); RefreshVeil();
        EconomyClient.LastInventoryCloseFrame = Time.frameCount;
    }
    void RefreshStoredItems()
    {
        if (!LoadoutMode) return;
        storedSlots.Clear();
        foreach (var row in (outfitter.State.entries ?? Array.Empty<EconomyEntry>()).Take(outfitter.State.count)
            .Where(e => e.count > 0).OrderBy(e => ABItemDefinitions.Find(e.hash)?.kind == 2 ? 0 : ABItemDefinitions.Find(e.hash)?.kind == 1 ? 1 : 2).ThenBy(e => ItemCatalog.Load()?.Resolve(e.hash)?.DisplayName))
        {
            var item = ItemCatalog.Load()?.Resolve(row.hash);
            if (item != null) storedSlots.Add(new InventorySlot { item = item, count = row.count });
        }
        stashPage = Mathf.Clamp(stashPage, 0, Mathf.Max(0, (storedSlots.Count - 1) / cells.Length));
        Refresh();
    }
    InventorySlot SlotAt(int index) => LoadoutMode
        ? index >= 0 && stashPage * cells.Length + index < storedSlots.Count ? storedSlots[stashPage * cells.Length + index] : null
        : inventory?.GetSlot(index);
    int Owned(ItemData item) => LoadoutMode ? storedSlots.FirstOrDefault(s => s.item == item)?.count ?? 0 : inventory?.Count(item) ?? 0;
    void ServerChanged()
    {
        if(pendingDropSequence!=0&&client.State.sequence>=pendingDropSequence){
            if (client.State.result == 0)
            {
                if (pendingDroppedHash == weaponHash && pendingDroppedOccurrence <= weaponOccurrence) weaponOccurrence = Mathf.Max(0, weaponOccurrence - 1);
                if (pendingDroppedHash == passiveHash && pendingDroppedOccurrence <= passiveOccurrence) passiveOccurrence = Mathf.Max(0, passiveOccurrence - 1);
            }
            Notify(client.State.result==0?"아이템을 바닥에 버렸습니다.":"버릴 수 없습니다. 수량과 상태를 확인하세요.");pendingDropSequence=0;
        }
        if(pendingSequence!=0 && client.State.sequence>=pendingSequence){
            bool accepted=client.State.result==0 && client.State.offer!=pendingOffer;
            pendingSequence=0;
            if(accepted){Notify("강화 완료");if(client.State.offer==0)CloseUpgrade();}
            else Notify("선택 실패");
        }
        if(pendingItemSequence!=0 && client.State.sequence==pendingItemSequence){
            if (client.State.result == 0 && pendingEquipHash != 0)
            {
                if (client.State.weapon == pendingEquipHash) { weaponHash = pendingEquipHash; weaponOccurrence = pendingEquipOccurrence; }
                if (client.State.passive == pendingEquipHash) { passiveHash = pendingEquipHash; passiveOccurrence = pendingEquipOccurrence; }
            }
            if(client.State.result!=0)Notify("사용·장착 불가");pendingItemSequence=0;pendingEquipHash=0;
        }
        Refresh();
    }
    void Update()
    {
        bool ready=client!=null&&client.Supported&&client.State.offer!=0;
        if(hudHint!=null){float pulse=ready?.5f+.5f*Mathf.Sin(Time.unscaledTime*4):0;hudHint.color=ready?Color.Lerp(T.Gold,T.White,pulse):T.Muted;hudHint.rectTransform.localScale=Vector3.one*(ready?1f+.07f*pulse:1f);}
        if(hudLevel!=null)hudLevel.color=ready?T.Gold:T.White;
        if(client!=ABClient.Active){if(client!=null)client.Changed-=ServerChanged;client=ABClient.Active;if(client!=null)client.Changed+=ServerChanged;}
        bool supported=client!=null&&client.Supported;
        if(wasSupported&&!supported){Array.Clear(quickHashes,0,4);pendingSequence=pendingItemSequence=pendingDropSequence=0;CloseUpgrade();CloseInventory();}
        wasSupported=supported;
        if(supported&&client.Bootstrap.LocalPlayer!=null&&inventory==null){var p=client.Bootstrap.LocalPlayer;Bind(p.GetComponent<PlayerInventory>(),p.GetComponent<PlayerEquipment>());}
        if(supported&&client.State.hp<=0){if(InventoryOpen)CloseInventory();CloseUpgrade();}
        if(pendingSequence!=0&&Time.unscaledTime>pendingUntil){pendingSequence=0;Notify("응답 지연");Refresh();}
        if(pendingDropSequence!=0&&Time.unscaledTime>dropPendingUntil){pendingDropSequence=0;Notify("버리기 응답 지연 · 소지 수량을 확인하세요.");Refresh();}
        var keyboard=Keyboard.current;
        if(keyboard!=null&&!ChatUI.IsTyping){
            if(keyboard.tabKey.wasPressedThisFrame && supported && client.State.hp>0 && (UpgradeOpen||InventoryOpen||input!=null&&input.IsGameplayInputEnabled))ToggleUpgrade();
            if(keyboard.escapeKey.wasPressedThisFrame){if(UpgradeOpen)CloseUpgrade();else if(InventoryOpen)CloseInventory();}
            for(int i=0;i<4;i++)if(keyboard[(Key)((int)Key.Digit1+i)].wasPressedThisFrame){
                if(InventoryOpen)AssignQuick(i);else if(!UpgradeOpen&&input!=null&&input.IsGameplayInputEnabled)UseQuick(i);break;
            }
        }
        if(Time.unscaledTime>=refreshAt){refreshAt=Time.unscaledTime+.2f;RefreshHud();if(InventoryOpen)RefreshStats();}
        if(feedback!=null&&Time.unscaledTime>feedbackUntil)feedback.text="";
    }
    public void SetInventoryVisible(bool show)
    {
        if (LoadoutMode) return;
        if (!show && InventoryOpen) LastCloseFrame = Time.frameCount;
        if(show)CloseUpgrade();inventoryPanel.SetActive(show);RefreshVeil();if(show)Refresh();
    }
    public void CloseInventory(){if(LoadoutMode){CloseLoadout();return;}if(controller!=null)controller.SetInventoryOpen(false);else{SetInventoryVisible(false);RefreshInput();}}
    public void ToggleUpgrade()
    {
        if(UpgradeOpen){CloseUpgrade();return;}
        if(client==null||!client.Supported||client.State.offer==0){Notify("선택 가능한 강화 없음");return;}
        CloseInventory();upgradePanel.SetActive(true);RefreshVeil();RefreshInput();RefreshUpgrades();
    }
    public void CloseUpgrade(){if(upgradePanel==null||!UpgradeOpen)return;LastCloseFrame=Time.frameCount;upgradePanel.SetActive(false);RefreshVeil();RefreshInput();}
    void RefreshInput(){if(controller!=null)controller.RefreshCurrentInputMode();else input?.SetInputMode(InventoryOpen||UpgradeOpen?PlayerInputMode.UI:PlayerInputMode.Gameplay);}
    void RefreshVeil(){veil.SetActive(InventoryOpen||UpgradeOpen);if(hud!=null)RefreshHud();if(feedback!=null)feedback.rectTransform.anchoredPosition=new Vector2(0,InventoryOpen||UpgradeOpen?450:-280);}
    public void Select(int index){selected=index;dropCount=1;dropHash=0;Refresh();}
    public void ChangeDropCount(int change,bool all=false)
    {
        var slot=inventory?.GetSlot(selected);if(slot==null||slot.IsEmpty||pendingDropSequence!=0)return;
        dropCount=all?slot.count:Mathf.Clamp(dropCount+change,1,slot.count);RefreshDropControls();
    }
    public void DropSelected()
    {
        if (LoadoutMode) return;
        var slot=inventory?.GetSlot(selected);if(slot==null||slot.IsEmpty||pendingDropSequence!=0)return;
        int count=Mathf.Clamp(dropCount,1,slot.count);
        if(inventory.IsServerOwned){
            if(client==null||!client.Supported||client.State.hp<=0)return;
            pendingDroppedHash = ItemHash.Of(slot.item.ItemId); pendingDroppedOccurrence = OccurrenceAt(selected);
            client.Request(7,ItemHash.Of(slot.item.ItemId),count);pendingDropSequence=client.LastRequestSequence;dropPendingUntil=Time.unscaledTime+3;
        }else if(DroppedItemSpawner.Drop(inventory,selected,count))Notify("아이템을 바닥에 버렸습니다.");
        Refresh();
    }
    public void Hover(int index,bool hovered){Refresh();if(hovered&&index>=0&&index<cells.Length)cells[index].bg.color=Color.Lerp(cells[index].bg.color,Color.white,.12f);}
    ItemData Selected=>SlotAt(selected)?.item;
    public void UseSelected()
    {
        var item=Selected;if(item==null||pendingDropSequence!=0)return;
        if(LoadoutMode){var definition=ABItemDefinitions.Find(ItemHash.Of(item.ItemId));if(definition!=null&&outfitter.Ready)outfitter.Request(3,ItemHash.Of(item.ItemId),1,definition.kind==2?0:1);return;}
        if(client!=null&&client.Supported){if(client.TryUse(item)){pendingItemSequence=client.LastRequestSequence;pendingEquipHash=ItemHash.Of(item.ItemId);pendingEquipOccurrence=OccurrenceAt(selected);}else Notify("사용·장착 불가");}
        else if(item is EquipmentItemData&&equipment!=null)equipment.TryEquipFromInventory(selected);
        else Notify("사용 불가");
    }
    static bool Quickable(ItemData item){var d=item!=null?ABItemDefinitions.Find(ItemHash.Of(item.ItemId)):null;return d!=null&&(d.kind==0||d.kind==1&&d.effect==15);}
    public void AssignQuick(int index)
    {
        if (LoadoutMode) return;
        var item=Selected;if(index<0||index>3)return;
        if(!Quickable(item)){Notify("퀵슬롯 등록 불가");return;}
        int hash=ItemHash.Of(item.ItemId);for(int i=0;i<4;i++)if(quickHashes[i]==hash)quickHashes[i]=0;
        quickHashes[index]=hash;Notify($"퀵슬롯 {index+1} 등록");Refresh();
    }
    public void UseQuick(int index)
    {
        if(index<0||index>3||client==null||!client.Supported||client.State.hp<=0)return;
        var item=ItemCatalog.Load()?.Resolve(quickHashes[index]);
        if(item==null||inventory==null||inventory.Count(item)<=0){Notify("아이템 없음");return;}
        var definition=ABItemDefinitions.Find(quickHashes[index]);
        if(definition==null)return;
        if(definition.kind!=0&&client.State.passive!=quickHashes[index]){Notify("장착 필요");return;}
        client.Request(2,quickHashes[index]);pendingItemSequence=client.LastRequestSequence;pendingEquipHash=0;
    }
    public void Choose(int card)
    {
        if(client==null||!client.Supported||pendingSequence!=0)return;
        var s=client.State;int choice=card==0?s.choice0:card==1?s.choice1:s.choice2;
        if(s.offer==0||choice<0||choice>=SkillNames.Length)return;
        pendingOffer=s.offer;client.Request(3,choice,s.offer);pendingSequence=client.LastRequestSequence;pendingUntil=Time.unscaledTime+3;RefreshUpgrades();
    }
    public void Notify(string message){if(feedback==null)return;feedback.text=message;feedbackUntil=Time.unscaledTime+2;}
    void Refresh()
    {
        if(cells[0]==null)return;
        int equippedWeaponSlot = -1, equippedItemSlot = -1;
        if (LoadoutMode)
        {
            weaponHash = outfitter.State.weapon; passiveHash = outfitter.State.item;
            equippedWeaponSlot = FindEquippedSlot(weaponHash, 0); equippedItemSlot = FindEquippedSlot(passiveHash, 0);
        }
        else if (client != null && client.Supported)
        {
            if (weaponHash != client.State.weapon) { weaponHash = client.State.weapon; weaponOccurrence = 0; }
            if (passiveHash != client.State.passive) { passiveHash = client.State.passive; passiveOccurrence = 0; }
            equippedWeaponSlot = FindEquippedSlot(weaponHash, weaponOccurrence);
            equippedItemSlot = FindEquippedSlot(passiveHash, passiveOccurrence);
        }
        long total=0;
        for(int i=0;i<cells.Length;i++){
            if(i>=ItemCatalog.InventoryCapacity){cells[i].bg.gameObject.SetActive(false);continue;}
            var slot=SlotAt(i);var item=slot?.item;
            if(item!=null&&slot.count>0)total+=(long)item.ExtractionValue*slot.count;
            var c=cells[i];T.Icon(c.icon,item);c.count.text=item!=null&&slot.count>1?slot.count.ToString():"";
            c.label.text=item!=null?item.DisplayName:"";c.accent.color=item!=null?EquipmentPresentation.Accent(item):new Color(1,1,1,.05f);
            c.bg.color=i==selected?new Color(.18f,.20f,.22f):T.Card;c.tag.text="";
            if(item!=null&&(LoadoutMode||client!=null&&client.Supported)){if(i==equippedWeaponSlot||i==equippedItemSlot){c.tag.text="장착 중";c.tag.fontSize=11;c.tag.fontStyle=FontStyle.Bold;c.tag.color=new Color(1,.85f,.35f);c.bg.color=i==selected?new Color(.32f,.27f,.12f):new Color(.21f,.17f,.075f);c.accent.rectTransform.sizeDelta=new Vector2(c.accent.rectTransform.sizeDelta.x,4);}
                else c.accent.rectTransform.sizeDelta=new Vector2(c.accent.rectTransform.sizeDelta.x,2);}
        }
        capacity.text=LoadoutMode?$"보관 {storedSlots.Count}종 · {stashPage+1} / {Mathf.Max(1,Mathf.CeilToInt(storedSlots.Count/(float)cells.Length))} 페이지":$"사용 슬롯  {inventory?.UsedSlots??0} / {ItemCatalog.InventoryCapacity}";
        value.text=LoadoutMode?$"{outfitter.State.credits:N0} G":$"총 가치   {total:N0}";
        inventoryPanel.transform.Find("Title").GetComponent<Text>().text=LoadoutMode?"출격 장비":"소지품";
        closeInventory.GetComponentInChildren<Text>().text=LoadoutMode?"닫기 [Esc]":"닫기 [I]";
        var weaponItem = LoadoutMode ? ItemCatalog.Load()?.Resolve(weaponHash==0?ItemHash.Of("sword_001"):weaponHash) : client != null && client.Supported ? ItemCatalog.Load()?.Resolve(client.State.weapon) : equipment?.GetEquippedItem(EquipmentSlotType.Weapon);
        var passiveItem = LoadoutMode ? ItemCatalog.Load()?.Resolve(passiveHash==0?ItemHash.Of("health_potion"):passiveHash) : client != null && client.Supported ? ItemCatalog.Load()?.Resolve(client.State.passive) : null;
        equipped.text = weaponItem != null ? weaponItem.DisplayName : "없음";
        passive.text = passiveItem != null ? passiveItem.DisplayName + (LoadoutMode&&passiveHash==0?" ×3":"") : "없음";
        if (equippedWeaponIcon != null) T.Icon(equippedWeaponIcon, weaponItem);
        if (equippedItemIcon != null) T.Icon(equippedItemIcon, passiveItem);
        var selectedItem=Selected;T.Icon(detailIcon,selectedItem);
        detailName.text=selectedItem!=null?selectedItem.DisplayName:"";
        detailMeta.text=selectedItem!=null?selectedItem.TypeName+"  /  "+selectedItem.RarityName:"";
        detailMeta.color=selectedItem!=null?EquipmentPresentation.Accent(selectedItem):T.Gold;
        detailBody.text=selectedItem!=null?CompactDescription(selectedItem.Description):"";
        detailValue.text=selectedItem!=null?$"개당 가치  {selectedItem.ExtractionValue:N0}   ·   보유 {Owned(selectedItem)}개":"";
        var definition=selectedItem!=null?ABItemDefinitions.Find(ItemHash.Of(selectedItem.ItemId)):null;
        action.interactable=selectedItem!=null&&(definition!=null||selectedItem is EquipmentItemData);
        action.gameObject.SetActive(action.interactable);
        bool worn=selected >= 0 && (selected == equippedWeaponSlot || selected == equippedItemSlot);
        action.GetComponentInChildren<Text>(true).text=worn?"장착 중":LoadoutMode?"출격 장비로 장착":definition!=null&&definition.kind==0?"사용":"장착";
        if(worn)action.interactable=false;
        if(pendingDropSequence!=0)action.interactable=false;
        if(LoadoutMode&&!outfitter.Ready)action.interactable=false;
        for(int i=0;i<4;i++){assign[i].gameObject.SetActive(!LoadoutMode&&Quickable(selectedItem));assign[i].interactable=Quickable(selectedItem);}
        assignmentHint.text=LoadoutMode?"아이템 우클릭으로 장착 · 선택한 장비로 출격합니다":Quickable(selectedItem)?"퀵슬롯 등록":"";
        RefreshStats();
        foreach(var button in new[]{stashPrevious,stashNext,defaultWeapon,defaultItem})if(button!=null)button.gameObject.SetActive(LoadoutMode);
        if(LoadoutMode){stashPrevious.interactable=stashPage>0;stashNext.interactable=(stashPage+1)*cells.Length<storedSlots.Count;defaultWeapon.interactable=defaultItem.interactable=outfitter.Ready;}
        // The initial potion is assigned once. Empty stacks retain their assignment.
        if(lastHashInventory!=(inventory!=null?inventory.GetInstanceID():0)){lastHashInventory=inventory!=null?inventory.GetInstanceID():0;Array.Clear(quickHashes,0,4);quickHashes[0]=ItemHash.Of("health_potion");}
        RefreshDropControls();RefreshHud();if(UpgradeOpen)RefreshUpgrades();
    }
    static string CompactDescription(string description)=>string.IsNullOrEmpty(description)?"":description.Replace("장착 시 ","").Replace("기본 공격력","공격력").Replace("중첩 대신 지속 시간 갱신.","중첩 불가 · 지속 시간 갱신").Replace(". ","\n").TrimEnd('.');
    static string Name(int hash)=>ItemCatalog.Load()?.Resolve(hash)?.DisplayName??"없음";
    void RefreshHud()
    {
        bool active=client!=null&&client.Supported&&client.Bootstrap.Identity.IsConnectedToSession;hud.SetActive(active&&!InventoryOpen&&!UpgradeOpen);
        if(!active)return;
        var s=client.State;hudLevel.text=$"LV. {s.level:D2}    {s.xp} / {s.required}";xpFill.fillAmount=s.required>0?(float)s.xp/s.required:0;
        hudHint.text=s.offer!=0?"스킬 선택 가능  [Tab]":"";
        for(int i=0;i<4;i++){var item=ItemCatalog.Load()?.Resolve(quickHashes[i]);var c=quickCells[i];T.Icon(c.icon,item);int count=item!=null&&inventory!=null?inventory.Count(item):0;
            c.count.text=item!=null?count.ToString():"";c.label.text=item!=null?item.DisplayName:"";c.bg.color=count>0?T.Card:new Color(.055f,.062f,.07f,.9f);c.icon.color=item==null?Color.clear:count>0?Color.white:new Color(.4f,.4f,.4f,.5f);
        }
    }
    void RefreshUpgrades()
    {
        if(client==null||!client.Supported)return;var s=client.State;upgradeSubtitle.text=$"Lv. {s.level}";
        for(int i=0;i<3;i++){int choice=i==0?s.choice0:i==1?s.choice1:s.choice2;bool valid=s.offer!=0&&choice>=0&&choice<SkillNames.Length;
            upgradeButtons[i].interactable=valid&&pendingSequence==0;upgradeTitles[i].text=valid?SkillNames[choice]:"선택 완료";
            upgradeDescriptions[i].text=valid?SkillStats[choice]:"";
            T.Icon(upgradeIcons[i],valid?ItemCatalog.Load()?.Resolve(SkillItems[choice]):null);
        }
    }
    // Also restyle the serialized prefab at runtime; rebuilding the inventory prefab is unnecessary.
    public void StyleInventory()
    {
        if (statsTitle == null)
        {
            var stats = T.Box("Player Stats", inventoryPanel.transform, -236, -260, 732, 112, T.Card).transform;
            statsTitle = T.Label("Title", stats, -176, 35, 330, 24, 16, T.Gold, "플레이어 스탯");
            T.Label("Source", stats, 169, 35, 340, 22, 11, T.Muted, "장비 · 강화 · 현재 효과 반영", TextAnchor.MiddleRight);
            for (int i=0;i<6;i++) statValues[i]=T.Label("Stat "+i,stats,-238+(i%3)*238,5-(i/3)*31,224,27,16,T.White);
            stashPrevious=T.Button("Stash Previous",inventoryPanel.transform,-555,-367,78,36,"‹");
            stashNext=T.Button("Stash Next",inventoryPanel.transform,-465,-367,78,36,"›");
            defaultWeapon=T.Button("Default Weapon",inventoryPanel.transform,-275,-367,230,36,"기본 검 장착");
            defaultItem=T.Button("Default Item",inventoryPanel.transform,-29,-367,230,36,"기본 물약 선택");
            stashPrevious.onClick.AddListener(()=>{stashPage=Mathf.Max(0,stashPage-1);selected=-1;Refresh();});
            stashNext.onClick.AddListener(()=>{stashPage++;selected=-1;RefreshStoredItems();});
            defaultWeapon.onClick.AddListener(()=>outfitter?.Request(3,0,1,0));
            defaultItem.onClick.AddListener(()=>outfitter?.Request(3,0,1,1));
            foreach(var button in new[]{stashPrevious,stashNext,defaultWeapon,defaultItem})button.gameObject.SetActive(false);
        }
        assignmentHint.rectTransform.anchoredPosition=new Vector2(-236,-332);
        for(int i=0;i<assign.Length;i++){((RectTransform)assign[i].transform).anchoredPosition=new Vector2(-510+i*175,-367);((RectTransform)assign[i].transform).sizeDelta=new Vector2(160,36);}
        if (equippedWeaponIcon == null)
        {
            equippedWeaponIcon = CreateEquippedSlot("Weapon equipment", "장착 무기", -422, equipped, true);
            equippedItemIcon = CreateEquippedSlot("Passive equipment", "장착 아이템", -50, passive, false);
        }
        for(int i=0;i<cells.Length;i++){
            var c=cells[i];if(c?.bg==null)continue;
            c.bg.gameObject.SetActive(i<ItemCatalog.InventoryCapacity);if(i>=ItemCatalog.InventoryCapacity)continue;
            c.bg.rectTransform.anchoredPosition=new Vector2(-546+(i%6)*124,151-(i/6)*78);c.bg.rectTransform.sizeDelta=new Vector2(112,72);
            c.icon.rectTransform.anchoredPosition=new Vector2(-27,5);c.icon.rectTransform.sizeDelta=new Vector2(48,48);
            c.label.rectTransform.anchoredPosition=new Vector2(26,-5);c.label.rectTransform.sizeDelta=new Vector2(55,42);c.label.fontSize=10;
            c.count.rectTransform.anchoredPosition=new Vector2(35,24);c.accent.rectTransform.anchoredPosition=new Vector2(0,-34);c.accent.rectTransform.sizeDelta=new Vector2(98,2);
            c.tag.rectTransform.anchoredPosition=new Vector2(-21,28);c.tag.rectTransform.sizeDelta=new Vector2(68,16);
        }
        if(action==null)return;
        var detail=(RectTransform)action.transform.parent;detail.anchoredPosition=new Vector2(416,-90);detail.sizeDelta=new Vector2(370,580);
        var rule=detail.Find("Detail Accent") as RectTransform;if(rule!=null)rule.anchoredPosition=new Vector2(0,288);
        detailIcon.rectTransform.anchoredPosition=new Vector2(0,208);detailMeta.rectTransform.anchoredPosition=new Vector2(0,130);
        detailName.rectTransform.anchoredPosition=new Vector2(0,83);detailBody.rectTransform.anchoredPosition=new Vector2(0,-31);detailBody.rectTransform.sizeDelta=new Vector2(310,122);
        detailBody.resizeTextForBestFit=true;detailBody.resizeTextMinSize=16;detailBody.resizeTextMaxSize=19;
        detailValue.rectTransform.anchoredPosition=new Vector2(0,-114);detailValue.rectTransform.sizeDelta=new Vector2(310,26);
        ((RectTransform)action.transform).anchoredPosition=new Vector2(0,-158);((RectTransform)action.transform).sizeDelta=new Vector2(310,42);
        if(dropButton!=null)return;
        dropLess=T.Button("Drop Less",detail,-132,-207,46,32,"−");dropMore=T.Button("Drop More",detail,40,-207,46,32,"+");
        dropQuantity=T.Label("Drop Quantity",detail,-47,-207,115,32,16,T.White,"수량 1",TextAnchor.MiddleCenter);
        dropAll=T.Button("Drop All",detail,116,-207,78,32,"전체");
        dropButton=T.Button("Drop Item",detail,0,-257,310,42,"바닥에 버리기");
        dropButton.GetComponent<Image>().color=new Color(.24f,.12f,.12f);
        dropLess.onClick.AddListener(()=>ChangeDropCount(-1));dropMore.onClick.AddListener(()=>ChangeDropCount(1));dropAll.onClick.AddListener(()=>ChangeDropCount(0,true));dropButton.onClick.AddListener(DropSelected);
    }
    void RefreshStats()
    {
        if (statsTitle == null) return;
        var state = LoadoutMode ? EquipmentPresentation.Preview(weaponHash,passiveHash)
            : client != null && client.Supported ? client.State : EquipmentPresentation.Preview(weaponHash,passiveHash);
        if (!LoadoutMode && (client == null || !client.Supported) && inventory != null)
        {
            var health=inventory.GetComponent<Player_State>();var attack=inventory.GetComponent<Player_Attack>();var stamina=inventory.GetComponent<PlayerStamina>();
            if(health!=null){state.maxHp=health.MaxHp;state.hp=health.CurrentHp;}
            if(attack!=null)state.damage=attack.CurrentAttackDamage;
            if(stamina!=null){state.maxStamina=stamina.Max;state.stamina=stamina.Current;}
        }
        statsTitle.text = LoadoutMode ? "출격 스탯" : $"플레이어 스탯 · Lv. {Mathf.Max(1,state.level)}";
        var values = EquipmentPresentation.Stats(state);
        for(int i=0;i<values.Length;i++)statValues[i].text=values[i];
    }
    Image CreateEquippedSlot(string name, string title, float x, Text label, bool weapon)
    {
        var box = T.Box(name, inventoryPanel.transform, x, 228, 350, 62, T.Card, true);
        var button = box.gameObject.AddComponent<Button>(); button.targetGraphic = box;
        button.onClick.AddListener(() => {
            int hash=weapon?weaponHash:passiveHash;
            if(LoadoutMode){int index=storedSlots.FindIndex(s=>ItemHash.Of(s.item.ItemId)==hash);if(index>=0){stashPage=index/cells.Length;Select(index%cells.Length);return;}}
            Select(FindEquippedSlot(hash,weapon?weaponOccurrence:passiveOccurrence));
        });
        var icon = T.Box("Icon", box.transform, -141, 0, 42, 42, Color.clear);
        T.Label("Slot name", box.transform, 31, 17, 264, 18, 12, T.Gold, title);
        label.transform.SetParent(box.transform, false);
        label.rectTransform.anchoredPosition = new Vector2(31, -10);
        label.rectTransform.sizeDelta = new Vector2(264, 26); label.fontSize = 16;
        return icon;
    }
    int OccurrenceAt(int index)
    {
        var item = inventory?.GetSlot(index)?.item;
        int occurrence = 0;
        for (int i = 0; i < index; i++) if (inventory.GetSlot(i)?.item == item) occurrence++;
        return occurrence;
    }
    int FindEquippedSlot(int hash, int occurrence)
    {
        if (hash == 0 || !LoadoutMode && inventory == null) return -1;
        int last = -1;
        for (int i = 0; i < cells.Length; i++)
        {
            var slot = SlotAt(i);
            if (slot == null || slot.IsEmpty || ItemHash.Of(slot.item.ItemId) != hash) continue;
            last = i;
            if (occurrence-- == 0) return i;
        }
        return last;
    }
    void RefreshDropControls()
    {
        if(dropButton==null)return;
        foreach(var control in new Component[]{dropButton,dropLess,dropMore,dropAll,dropQuantity})control.gameObject.SetActive(!LoadoutMode);
        if(LoadoutMode)return;
        var slot=inventory?.GetSlot(selected);bool hasItem=slot!=null&&!slot.IsEmpty;
        int hash=hasItem?ItemHash.Of(slot.item.ItemId):0;
        if(hash!=dropHash){dropHash=hash;dropCount=1;}
        dropCount=hasItem?Mathf.Clamp(dropCount,1,slot.count):1;
        bool available=hasItem&&pendingDropSequence==0&&(!inventory.IsServerOwned||client!=null&&client.Supported&&client.State.hp>0);
        dropButton.interactable=available;dropLess.interactable=available&&dropCount>1;dropMore.interactable=dropAll.interactable=available&&dropCount<slot.count;
        dropQuantity.text=hasItem?$"수량 {dropCount}":"수량 —";
        dropButton.GetComponentInChildren<Text>().text=pendingDropSequence!=0?"버리는 중…":hasItem?$"{dropCount}개 바닥에 버리기":"바닥에 버리기";
    }
    public void StyleUpgradeCards()
    {
        foreach(var description in upgradeDescriptions){
            if(description==null)continue;
            description.fontSize=18;description.rectTransform.sizeDelta=new Vector2(294,78);
            description.rectTransform.anchoredPosition=new Vector2(0,-53);
        }
    }
    public void StyleQuickAccess()
    {
        if(quickStyled||hud==null)return;quickStyled=true;
        var rect=(RectTransform)hud.transform;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,0);
        rect.anchoredPosition=new Vector2(0,32);rect.sizeDelta=new Vector2(552,124);
        hudLevel.rectTransform.anchoredPosition=new Vector2(-151,53);hudLevel.rectTransform.sizeDelta=new Vector2(230,22);hudLevel.fontSize=12;
        hudHint.rectTransform.anchoredPosition=new Vector2(151,53);hudHint.rectTransform.sizeDelta=new Vector2(230,22);hudHint.fontSize=12;
        var track=hud.transform.Find("XP Track") as RectTransform;
        if(track!=null){track.anchoredPosition=new Vector2(0,36);track.sizeDelta=new Vector2(528,2);}
        xpFill.rectTransform.anchoredPosition=new Vector2(0,36);xpFill.rectTransform.sizeDelta=new Vector2(528,2);
        for(int i=0;i<4;i++){
            var cell=quickCells[i];var r=cell.bg.rectTransform;r.anchoredPosition=new Vector2(-201+i*134,-14);r.sizeDelta=new Vector2(126,84);
            cell.bg.color=T.Ink;
            cell.icon.rectTransform.anchoredPosition=new Vector2(0,8);cell.icon.rectTransform.sizeDelta=new Vector2(40,40);
            cell.count.rectTransform.anchoredPosition=new Vector2(44,23);cell.count.rectTransform.sizeDelta=new Vector2(24,22);cell.count.fontSize=12;
            cell.label.rectTransform.anchoredPosition=new Vector2(0,-26);cell.label.rectTransform.sizeDelta=new Vector2(114,27);cell.label.fontSize=11;
            var cap=T.Box("Keycap",r,-46,25,20,20,new Color(.14f,.17f,.20f));
            cell.tag.transform.SetParent(cap.transform,false);cell.tag.rectTransform.anchoredPosition=Vector2.zero;cell.tag.rectTransform.sizeDelta=new Vector2(20,20);
            cell.tag.alignment=TextAnchor.MiddleCenter;cell.tag.fontSize=11;cell.tag.color=T.White;
            T.Box("Slot Rule",r,0,-41,102,1,new Color(T.Gold.r,T.Gold.g,T.Gold.b,.28f));
        }
    }
    void Wire()
    {
        if(wired)return;wired=true;closeInventory.onClick.AddListener(CloseInventory);closeUpgrade.onClick.AddListener(CloseUpgrade);action.onClick.AddListener(UseSelected);
        for(int i=0;i<4;i++){int index=i;assign[i].onClick.AddListener(()=>AssignQuick(index));}
        for(int i=0;i<3;i++){int index=i;upgradeButtons[i].onClick.AddListener(()=>Choose(index));}
    }
    public void Build()
    {
        if(inventoryPanel!=null)return;
        var canvas=GetComponent<Canvas>();if(canvas==null)canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=160;
        var scaler=GetComponent<CanvasScaler>();if(scaler==null)scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        if(GetComponent<GraphicRaycaster>()==null)gameObject.AddComponent<GraphicRaycaster>();
        veil=T.Box("Backdrop",transform,0,0,4000,2400,new Color(.005f,.01f,.02f,.76f),true).gameObject;
        inventoryPanel=T.Box("Inventory",transform,0,0,1280,820,T.Ink,true).gameObject;
        var p=inventoryPanel.transform;
        T.Label("Title",p,-388,330,420,62,36,T.White,"소지품");
        capacity=T.Label("Capacity",p,194,350,260,30,16,T.Muted,"",TextAnchor.MiddleRight);
        value=T.Label("Value",p,194,310,260,36,23,T.Gold,"",TextAnchor.MiddleRight);
        closeInventory=T.Button("Close",p,545,350,110,40,"닫기  [I]");
        T.Box("Rule",p,0,265,1190,1,new Color(.83f,.69f,.44f,.28f));
        equipped=T.Label("Weapon",p,-383,232,430,36,19,T.White);passive=T.Label("Passive",p,99,232,430,36,19,T.White);
        for(int i=0;i<ItemCatalog.InventoryCapacity;i++){
            float x=-550+(i%8)*96,y=139-(i/8)*122;
            var bg=T.Box("Slot "+i,p,x,y,86,108,T.Card,true);bg.gameObject.AddComponent<ExpeditionSlot>().Bind(this,i);
            cells[i]=new Cell{bg=bg,icon=T.Box("Icon",bg.transform,0,13,58,58,Color.clear),
                accent=T.Box("Rarity",bg.transform,0,-52,66,2,T.Muted),count=T.Label("Count",bg.transform,22,40,34,20,14,T.White,"",TextAnchor.MiddleRight),
                label=T.Label("Name",bg.transform,0,-32,76,36,12,T.White,"",TextAnchor.MiddleCenter),tag=T.Label("Equipped",bg.transform,-10,41,60,18,11,T.Gold)};
        }
        var detail=T.Box("Item Detail",p,416,-35,370,530,new Color(.06f,.073f,.09f),true).transform;
        T.Box("Detail Accent",detail,0,263,310,2,T.Gold);
        detailIcon=T.Box("Large Icon",detail,0,166,110,110,Color.clear);
        detailMeta=T.Label("Meta",detail,0,83,320,26,15,T.Gold,"",TextAnchor.MiddleCenter);
        detailName=T.Label("Name",detail,0,36,320,64,28,T.White,"",TextAnchor.MiddleCenter);
        detailBody=T.Label("Description",detail,0,-78,310,132,19,T.Muted);
        detailValue=T.Label("Value",detail,0,-165,310,34,16,T.Gold,"",TextAnchor.MiddleCenter);
        action=T.Button("Action",detail,0,-221,310,48,"장착",true);
        assignmentHint=T.Label("Assignment Hint",p,-203,-294,750,26,15,T.Muted);
        for(int i=0;i<4;i++)assign[i]=T.Button("Assign "+i,p,-510+i*175,-346,160,48,$"[{i+1}] 등록");
        upgradePanel=T.Box("Upgrade",transform,0,0,1260,620,T.Ink,true).gameObject;p=upgradePanel.transform;
        T.Label("Title",p,0,240,900,56,36,T.White,"강화 선택",TextAnchor.MiddleCenter);
        upgradeSubtitle=T.Label("Subtitle",p,0,190,900,30,18,T.Muted,"",TextAnchor.MiddleCenter);
        for(int i=0;i<3;i++){
            var card=T.Box("Choice "+i,p,(i-1)*382,-15,350,330,T.Card,true).transform;
            T.Box("Card Rule",card,0,164,292,2,T.Gold);
            upgradeIcons[i]=T.Box("Icon",card,0,90,72,72,Color.clear);
            upgradeTitles[i]=T.Label("Name",card,0,17,294,50,26,T.White,"",TextAnchor.MiddleCenter);
            upgradeDescriptions[i]=T.Label("Description",card,0,-49,294,66,21,T.White,"",TextAnchor.MiddleCenter);
            upgradeButtons[i]=T.Button("Choose",card,0,-125,292,44,"선택",true);
        }
        closeUpgrade=T.Button("Later",p,446,-260,240,44,"닫기  [Tab]");
        hud=T.Rect("Quick Access",transform,0,-425,680,180).gameObject;p=hud.transform;
        hudLevel=T.Label("Level",p,-228,78,180,24,14,T.Gold);hudHint=T.Label("Hint",p,170,78,360,24,15,T.Muted,"",TextAnchor.MiddleRight);
        T.Box("XP Track",p,0,58,640,3,T.Card);xpFill=T.Box("XP Fill",p,0,58,640,3,T.Gold);xpFill.type=Image.Type.Filled;xpFill.fillMethod=Image.FillMethod.Horizontal;
        for(int i=0;i<4;i++){
            var bg=T.Box("Quick "+i,p,-240+i*160,-6,150,100,T.Card);
            quickCells[i]=new Cell{bg=bg,icon=T.Box("Icon",bg.transform,0,12,48,48,Color.clear),count=T.Label("Count",bg.transform,52,28,36,26,16,T.White,"",TextAnchor.MiddleRight),
                label=T.Label("Name",bg.transform,0,-30,138,30,13,T.White,"",TextAnchor.MiddleCenter),tag=T.Label("Key",bg.transform,-51,29,28,26,18,T.Gold,(i+1).ToString())};
        }
        feedback=T.Label("Toast",transform,0,-280,1100,48,20,T.Gold,"",TextAnchor.MiddleCenter);
        inventoryPanel.SetActive(false);upgradePanel.SetActive(false);veil.SetActive(false);hud.SetActive(false);
    }
}
