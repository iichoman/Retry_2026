using System;

[Serializable]
public class InventorySlot
{
    public ItemData item;
    public int count;

    public bool IsEmpty => item == null || count <= 0;
    public int RemainingStackSpace => item == null ? 0 : item.MaxStack - count;

    public void Clear()
    {
        item = null;
        count = 0;
    }
}
