using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LootUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Text titleText;
    [SerializeField] private Transform lootSlotRoot;
    [SerializeField] private InventorySlotUI slotPrefab;
    [SerializeField] private UITextureSet textureSet;
    [SerializeField] private InventoryUI playerInventoryUI;
    [SerializeField] private Button takeAllButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private string defaultTitle = "전리품";

    private readonly List<InventorySlotUI> lootSlots = new List<InventorySlotUI>();
    private LootContainer lootContainer;
    private PlayerInventory targetInventory;
    private bool visibilityRequested;
    private bool isOpen;

    public event Action Closed;

    private void Awake()
    {
        if (root == null)
        {
            root = gameObject;
        }

        if (takeAllButton != null)
        {
            takeAllButton.onClick.AddListener(TakeAll);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
        }

        if (!visibilityRequested)
        {
            SetVisible(false);
        }

        GameUIVisualPolish.ApplyTo(transform);
    }

    private void OnDestroy()
    {
        if (takeAllButton != null)
        {
            takeAllButton.onClick.RemoveListener(TakeAll);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
        }
    }

    public bool Open(LootContainer container, PlayerInventory playerInventory)
    {
        if (container == null || playerInventory == null || !container.IsAvailable)
        {
            return false;
        }

        UnbindContainer();
        lootContainer = container;
        targetInventory = playerInventory;
        lootContainer.ContentsChanged += HandleContentsChanged;

        if (titleText != null)
        {
            titleText.text = defaultTitle;
        }

        EnsureLootSlotViews();
        Refresh();

        if (playerInventoryUI != null)
        {
            playerInventoryUI.SetVisible(true);
            playerInventoryUI.Refresh(playerInventory);
        }

        SetVisible(true);
        isOpen = true;
        return true;
    }

    public void Close()
    {
        if (!isOpen && lootContainer == null && targetInventory == null)
        {
            SetVisible(false);
            return;
        }

        isOpen = false;
        SetVisible(false);
        UnbindContainer();
        Closed?.Invoke();
    }

    public void TakeAll()
    {
        if (lootContainer == null || targetInventory == null)
        {
            return;
        }

        lootContainer.TryTakeAll(targetInventory);
        Refresh();
    }

    private void HandleSlotClicked(InventorySlotUI slotUI, int slotIndex, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left && button != PointerEventData.InputButton.Right)
        {
            return;
        }

        if (lootContainer == null || targetInventory == null)
        {
            return;
        }

        lootContainer.TryTakeSlot(slotIndex, targetInventory);
        Refresh();
    }

    private void HandleContentsChanged(LootContainer container)
    {
        Refresh();

        if (container != null && !container.IsAvailable)
        {
            Close();
        }
    }

    private void Refresh()
    {
        if (lootContainer == null)
        {
            return;
        }

        EnsureLootSlotViews();

        for (int i = 0; i < lootSlots.Count; i++)
        {
            InventorySlot slot = i < lootContainer.Slots.Count ? lootContainer.Slots[i] : null;
            lootSlots[i].Refresh(slot, textureSet, false);
        }

        if (playerInventoryUI != null && targetInventory != null)
        {
            playerInventoryUI.Refresh(targetInventory);
        }
    }

    private void EnsureLootSlotViews()
    {
        if (lootContainer == null || slotPrefab == null || lootSlotRoot == null)
        {
            return;
        }

        while (lootSlots.Count < lootContainer.Capacity)
        {
            InventorySlotUI slot = Instantiate(slotPrefab, lootSlotRoot);
            lootSlots.Add(slot);
        }

        for (int i = 0; i < lootSlots.Count; i++)
        {
            lootSlots[i].BindIndex(i);
            lootSlots[i].Clicked -= HandleSlotClicked;
            lootSlots[i].Clicked += HandleSlotClicked;
        }

        GameUIVisualPolish.ApplyTo(transform);
    }

    private void SetVisible(bool visible)
    {
        visibilityRequested = true;

        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    private void UnbindContainer()
    {
        if (lootContainer != null)
        {
            lootContainer.ContentsChanged -= HandleContentsChanged;
        }

        lootContainer = null;
        targetInventory = null;
    }
}
