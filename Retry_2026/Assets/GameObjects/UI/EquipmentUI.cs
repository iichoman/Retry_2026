using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EquipmentUI : MonoBehaviour
{
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private GameObject root;
    [SerializeField] private List<EquipmentSlotUI> slotViews = new List<EquipmentSlotUI>();
    [SerializeField] private bool visibleOnStart;

    private void Awake()
    {
        if (root == null)
        {
            root = gameObject;
        }

        SetVisible(visibleOnStart);
    }

    private void OnEnable()
    {
        if (playerEquipment == null)
        {
            playerEquipment = FindFirstObjectByType<PlayerEquipment>();
        }

        if (playerEquipment != null)
        {
            playerEquipment.EquipmentChanged += Refresh;
        }

        BindSlotViews();
        Refresh(playerEquipment);
    }

    private void OnDisable()
    {
        if (playerEquipment != null)
        {
            playerEquipment.EquipmentChanged -= Refresh;
        }

        for (int i = 0; i < slotViews.Count; i++)
        {
            if (slotViews[i] != null)
            {
                slotViews[i].Clicked -= HandleSlotClicked;
            }
        }
    }

    public void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    public void ToggleVisible()
    {
        if (root != null)
        {
            SetVisible(!root.activeSelf);
        }
    }

    public void Refresh(PlayerEquipment equipment)
    {
        for (int i = 0; i < slotViews.Count; i++)
        {
            EquipmentSlotUI slotView = slotViews[i];
            if (slotView == null)
            {
                continue;
            }

            EquipmentItemData item = equipment != null ? equipment.GetEquippedItem(slotView.SlotType) : null;
            slotView.Refresh(item);
        }
    }

    private void BindSlotViews()
    {
        for (int i = 0; i < slotViews.Count; i++)
        {
            if (slotViews[i] == null)
            {
                continue;
            }

            slotViews[i].Clicked -= HandleSlotClicked;
            slotViews[i].Clicked += HandleSlotClicked;
        }
    }

    private void HandleSlotClicked(EquipmentSlotType slotType)
    {
        if (playerEquipment != null && playerEquipment.TryUnequip(slotType))
        {
            Refresh(playerEquipment);
        }
    }
}
