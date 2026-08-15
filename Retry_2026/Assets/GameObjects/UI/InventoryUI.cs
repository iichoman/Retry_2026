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

    private void Awake()
    {
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
    }

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= Refresh;
        }
    }

    public void SetVisible(bool visible)
    {
        visibilityRequested = true;

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
    }

    private static string FormatItemDetail(InventorySlot slot)
    {
        ItemData item = slot.item;
        if (item is EquipmentItemData equipmentItem)
        {
            return $"{item.DisplayName}\n{FormatItemType(item.ItemType)} x{slot.count}\n장비 슬롯: {FormatEquipmentSlot(equipmentItem.EquipmentSlot)}\n체력 +{equipmentItem.MaxHpBonus}  공격력 +{equipmentItem.AttackBonus}  방어력 +{equipmentItem.DefenseBonus}";
        }

        return $"{item.DisplayName}\n{FormatItemType(item.ItemType)} x{slot.count}";
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
