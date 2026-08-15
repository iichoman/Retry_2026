using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image selectedImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private Text countText;
    [SerializeField] private Text nameText;

    public event Action<InventorySlotUI, int, PointerEventData.InputButton> Clicked;

    public int SlotIndex { get; private set; } = -1;

    private void Awake()
    {
        GameUIVisualPolish.ApplyToSlot(transform);
    }

    public void BindIndex(int slotIndex)
    {
        SlotIndex = slotIndex;
    }

    public void Refresh(InventorySlot slot, UITextureSet textureSet, bool selected)
    {
        ItemData item = slot != null ? slot.item : null;

        ApplyBackground(item, textureSet);
        ApplySelection(textureSet, selected);
        ApplyIcon(item, textureSet);

        if (countText != null)
        {
            countText.text = item != null && slot.count > 1 ? slot.count.ToString() : string.Empty;
        }

        if (nameText != null)
        {
            nameText.text = item != null ? item.DisplayName : string.Empty;
        }

        GameUIVisualPolish.ApplyToSlot(transform);
    }

    private void ApplyBackground(ItemData item, UITextureSet textureSet)
    {
        if (backgroundImage == null)
        {
            return;
        }

        Sprite sprite = item != null && item.SlotBackground != null
            ? item.SlotBackground
            : textureSet != null ? textureSet.slotSprite : null;

        Color color = textureSet != null ? textureSet.slotFallbackColor : Color.gray;
        ApplyImage(backgroundImage, sprite, color, true);
    }

    private void ApplySelection(UITextureSet textureSet, bool selected)
    {
        if (selectedImage == null)
        {
            return;
        }

        selectedImage.enabled = selected;

        Sprite sprite = textureSet != null ? textureSet.selectedSlotSprite : null;
        Color color = textureSet != null ? textureSet.selectedSlotFallbackColor : Color.yellow;
        ApplyImage(selectedImage, sprite, color, true);
    }

    private void ApplyIcon(ItemData item, UITextureSet textureSet)
    {
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
        Sprite sprite = item.Icon != null ? item.Icon : textureSet != null ? textureSet.missingIconSprite : null;
        Color color = item.Icon != null ? Color.white : item.FallbackIconColor;

        if (item.Icon == null && textureSet != null && textureSet.missingIconSprite != null)
        {
            color = textureSet.missingIconFallbackColor;
        }

        ApplyImage(iconImage, sprite, color, false);
    }

    private static void ApplyImage(Image image, Sprite sprite, Color color, bool slicedWhenPossible)
    {
        image.sprite = sprite;
        image.color = color;
        image.type = sprite != null && slicedWhenPossible ? Image.Type.Sliced : Image.Type.Simple;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke(this, SlotIndex, eventData.button);
    }
}
