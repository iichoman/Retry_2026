using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EquipmentSlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private EquipmentSlotType slotType;
    [SerializeField] private Image iconImage;
    [SerializeField] private Text slotNameText;
    [SerializeField] private Text itemNameText;

    public event Action<EquipmentSlotType> Clicked;

    public EquipmentSlotType SlotType => slotType;

    private void Awake()
    {
        GameUIVisualPolish.ApplyToSlot(transform);
    }

    public void SetSlotType(EquipmentSlotType value)
    {
        slotType = value;
        Refresh(null);
    }

    public void Refresh(EquipmentItemData item)
    {
        if (slotNameText != null)
        {
            slotNameText.text = FormatSlotName(slotType);
        }

        if (itemNameText != null)
        {
            itemNameText.text = item != null ? item.DisplayName : string.Empty;
        }

        if (iconImage == null)
        {
            return;
        }

        if (item == null)
        {
            iconImage.enabled = false;
            iconImage.sprite = null;
            return;
        }

        iconImage.enabled = true;
        iconImage.sprite = item.Icon;
        iconImage.color = item.Icon != null ? Color.white : item.FallbackIconColor;

        GameUIVisualPolish.ApplyToSlot(transform);
    }

    private static string FormatSlotName(EquipmentSlotType value)
    {
        return value switch
        {
            EquipmentSlotType.Weapon => "무기",
            EquipmentSlotType.Helmet => "투구",
            EquipmentSlotType.Chest => "갑옷",
            EquipmentSlotType.Gloves => "장갑",
            EquipmentSlotType.Boots => "신발",
            EquipmentSlotType.Accessory => "장신구",
            _ => value.ToString()
        };
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Clicked?.Invoke(slotType);
        }
    }
}
