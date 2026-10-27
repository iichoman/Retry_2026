using UnityEngine;

public class ItemPickup : MonoBehaviour, IPickupable
{
    [SerializeField] private ItemData item;
    [SerializeField, Min(1)] private int amount = 1;

    public bool TryPickup(GameObject picker)
    {
        if (picker == null || item == null)
        {
            return false;
        }

        PlayerInventory inventory = picker.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            inventory = picker.GetComponentInParent<PlayerInventory>();
        }

        if (inventory == null || !inventory.TryAdd(item, amount))
        {
            return false;
        }

        Destroy(gameObject);
        return true;
    }
}
