using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private UITextureSet textureSet;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Image panelBackground;
    [SerializeField] private Text itemDetailText;
    [SerializeField] private Transform slotRoot;
    [SerializeField] private InventorySlotUI slotPrefab;
    [SerializeField] private List<InventorySlotUI> slots = new List<InventorySlotUI>();
    [SerializeField] private bool visibleOnStart;

    private int selectedIndex;
    private bool visibilityRequested;
    private string equipmentFeedback;

    private void Awake()
    {
        ExpeditionUI.Ensure();
        ApplyPanelTexture();

        if (!visibilityRequested)
        {
            SetVisible(visibleOnStart);
        }
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (inventory != null)
        {
            inventory.InventoryChanged += Refresh;
            EnsureSlotViews();
            Refresh(inventory);
        }
        BindEquipmentFeedback();
    }

    private void OnDisable()
    {
        if (playerEquipment != null)
        {
            playerEquipment.EquipmentFeedback -= OnEquipmentFeedback;
            playerEquipment.EquipmentChanged -= OnEquipmentChanged;
        }
        if (inventory != null)
        {
            inventory.InventoryChanged -= Refresh;
        }
    }

    /// <summary>표시 대상 인벤토리/장비를 지금 살아 있는 것으로 다시 잡는다.</summary>
    public void RebindOwner(PlayerInventory inv, PlayerEquipment equip)
    {
        if (playerEquipment != null)
        {
            playerEquipment.EquipmentFeedback -= OnEquipmentFeedback;
            playerEquipment.EquipmentChanged -= OnEquipmentChanged;
        }
        if (inventory != null) inventory.InventoryChanged -= Refresh;
        inventory = inv;
        if (inventory != null) inventory.InventoryChanged += Refresh;

        playerEquipment = equip;
        ExpeditionUI.Ensure().Bind(inv,equip);
        BindEquipmentFeedback();

        EnsureSlotViews();
        Refresh(inventory);
    }

    private void BindEquipmentFeedback()
    {
        if (playerEquipment == null) return;
        playerEquipment.EquipmentFeedback -= OnEquipmentFeedback;
        playerEquipment.EquipmentFeedback += OnEquipmentFeedback;
        playerEquipment.EquipmentChanged -= OnEquipmentChanged;
        playerEquipment.EquipmentChanged += OnEquipmentChanged;
    }
    private void OnEquipmentFeedback(string message, bool success) { equipmentFeedback = message; RefreshItemDetail(); }
    private void OnEquipmentChanged(PlayerEquipment equipment) => RefreshItemDetail();

    public void SetVisible(bool visible)
    {
        visibilityRequested = true;
        if(ExpeditionUI.Active!=null){
            ResolveReferences();ExpeditionUI.Active.Bind(inventory,playerEquipment);
            if(panelRoot!=null)panelRoot.SetActive(false);
            ExpeditionUI.Active.SetInventoryVisible(visible);return;
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }

        if (visible)
        {
            ResolveReferences();
            Refresh(inventory);
        }
    }

    public void ToggleVisible()
    {
        if(ExpeditionUI.Active!=null){SetVisible(!ExpeditionUI.Active.InventoryOpen);return;}
        if (panelRoot != null)
        {
            SetVisible(!panelRoot.activeSelf);
        }
    }

    public void SelectSlot(int index)
    {
        selectedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, slots.Count - 1));

        if (inventory != null)
        {
            Refresh(inventory);
        }

        RefreshItemDetail();
    }

    public void UseSelectedSlot()
    {
        TryUseSlot(selectedIndex);
    }

    public void Refresh(PlayerInventory source)
    {
        if (source == null)
        {
            return;
        }

        EnsureSlotViews();

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = i < source.Slots.Count ? source.Slots[i] : null;
            slots[i].Refresh(slot, textureSet, i == selectedIndex);
        }

        RefreshItemDetail();
    }

    private void EnsureSlotViews()
    {
        if (inventory == null || slotPrefab == null || slotRoot == null)
        {
            return;
        }

        while (slots.Count < inventory.Capacity)
        {
            InventorySlotUI slot = Instantiate(slotPrefab, slotRoot);
            slots.Add(slot);
        }

        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].BindIndex(i);
            slots[i].Clicked -= HandleSlotClicked;
            slots[i].Clicked += HandleSlotClicked;
        }
    }

    private void HandleSlotClicked(InventorySlotUI slotUI, int slotIndex, PointerEventData.InputButton button)
    {
        SelectSlot(slotIndex);

        if (button == PointerEventData.InputButton.Right)
        {
            TryUseSlot(slotIndex);
        }
    }

    private bool TryUseSlot(int slotIndex)
    {
        if (inventory == null || !inventory.IsValidSlotIndex(slotIndex))
        {
            return false;
        }

        InventorySlot slot = inventory.GetSlot(slotIndex);
        if (slot == null || slot.IsEmpty)
        {
            return false;
        }

        if (ABClient.Active != null && ABClient.Active.Supported)
            return ABClient.Active.TryUse(slot.item);

        if (slot.item is EquipmentItemData && playerEquipment != null)
        {
            return playerEquipment.TryEquipFromInventory(slotIndex);
        }

        return false;
    }

    private void RefreshItemDetail()
    {
        if (itemDetailText == null || inventory == null || !inventory.IsValidSlotIndex(selectedIndex))
        {
            return;
        }

        InventorySlot slot = inventory.GetSlot(selectedIndex);
        if (slot == null || slot.IsEmpty)
        {
            itemDetailText.text = string.Empty;
            return;
        }

        itemDetailText.text = FormatItemDetail(slot);
        if (playerEquipment != null && slot.item is EquipmentItemData equipment &&
            playerEquipment.GetEquippedItem(equipment.EquipmentSlot) == equipment)
            itemDetailText.text += "\n[장착 중]";
        if (!string.IsNullOrEmpty(equipmentFeedback)) itemDetailText.text += "\n" + equipmentFeedback;
    }

    private static string FormatItemDetail(InventorySlot slot)
    {
        ItemData item = slot.item;
        string summary = $"{item.DisplayName}\n{item.TypeName} · {item.RarityName} · 수량 {slot.count}/{item.MaxStack}\n개당 가치 {item.ExtractionValue:N0} · 총 가치 {(long)item.ExtractionValue * slot.count:N0}\n{item.Description}";
        if (ABClient.Active != null && ABClient.Active.Supported && ABItemDefinitions.Find(ItemHash.Of(item.ItemId)) is var definition && definition != null)
            return summary + (definition.kind==0 ? "\n우클릭: 사용" : "\n우클릭: 장착 (장비 아이템은 1개만 적용)");
        if (item is EquipmentItemData equipmentItem)
        {
            return summary + $"\n장비 슬롯: {FormatEquipmentSlot(equipmentItem.EquipmentSlot)} · 우클릭하여 장착";
        }

        return summary;
    }

    private static string FormatItemType(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.Consumable => "소모품",
            ItemType.Weapon => "무기",
            ItemType.Armor => "방어구",
            ItemType.Material => "재료",
            ItemType.Quest => "퀘스트",
            ItemType.Currency => "재화",
            _ => itemType.ToString()
        };
    }

    private static string FormatEquipmentSlot(EquipmentSlotType slotType)
    {
        return slotType switch
        {
            EquipmentSlotType.Weapon => "무기",
            EquipmentSlotType.Helmet => "투구",
            EquipmentSlotType.Chest => "갑옷",
            EquipmentSlotType.Gloves => "장갑",
            EquipmentSlotType.Boots => "신발",
            EquipmentSlotType.Accessory => "장신구",
            _ => slotType.ToString()
        };
    }

    private void ApplyPanelTexture()
    {
        if (panelBackground == null || textureSet == null)
        {
            return;
        }

        panelBackground.sprite = textureSet.panelSprite;
        panelBackground.color = textureSet.panelFallbackColor;
        panelBackground.type = textureSet.panelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
    }

    private void ResolveReferences()
    {
        if (inventory == null)
        {
            inventory = FindFirstObjectByType<PlayerInventory>();
        }

        if (playerEquipment == null)
        {
            playerEquipment = FindFirstObjectByType<PlayerEquipment>();
        }
    }
}
