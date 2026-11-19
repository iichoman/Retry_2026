using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EquipmentUI : MonoBehaviour
{
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private GameObject root;
    [SerializeField] private List<EquipmentSlotUI> slotViews = new List<EquipmentSlotUI>();
    [SerializeField] private bool visibleOnStart;

    // GameUIController.Awake가 먼저 돌면 이 패널이 Awake 전에 SetActive(false) 된다.
    // 그러면 Awake가 "처음 켜지는 순간"으로 밀려서, i를 처음 눌러 켠 직후
    // Awake의 SetVisible(false)가 다시 꺼버린다. (첫 i에 장비창만 안 뜨던 원인)
    // 이미 외부에서 표시 요청을 받았으면 Awake는 건드리지 않는다. InventoryUI와 같은 방식.
    private bool visibilityRequested;

    private void Awake()
    {
        if (root == null)
        {
            root = gameObject;
        }

        if (!visibilityRequested)
        {
            SetVisible(visibleOnStart);
        }
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

    /// <summary>표시 대상 PlayerEquipment를 지금 살아 있는 것으로 다시 잡는다.</summary>
    public void RebindOwner(PlayerEquipment owner)
    {
        if (playerEquipment == owner) return;

        if (playerEquipment != null) playerEquipment.EquipmentChanged -= Refresh;
        playerEquipment = owner;
        if (playerEquipment != null) playerEquipment.EquipmentChanged += Refresh;

        BindSlotViews();
        Refresh(playerEquipment);
    }

    public void SetVisible(bool visible)
    {
        visibilityRequested = true;

        // 씬에 미리 박힌 참조는 네트워크로 새로 스폰된 캐릭터가 아니다.
        // 열 때마다 현재 살아있는 것으로 다시 잡아야 빈 창이 뜨지 않는다.
        if (visible && playerEquipment == null)
        {
            RebindOwner(FindFirstObjectByType<PlayerEquipment>());
        }

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
