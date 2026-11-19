using System;
using UnityEngine;

[Serializable]
public class StartingInventoryEntry
{
    [SerializeField] private ItemData item;
    [SerializeField, Min(1)] private int count = 1;

    public ItemData Item => item;
    public int Count => Mathf.Max(1, count);
}
