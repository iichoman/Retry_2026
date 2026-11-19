using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation metadata. Effects and inventory changes are owned by v3.</summary>
public static class ABItemDefinitions
{
    [Serializable]
    public class Item
    {
        public string id, label, description;
        public int effect, tier, value, kind;
        public bool IsGreatsword => kind == 2 && effect >= 5 && effect <= 7;
        public int WeaponElement => IsGreatsword ? effect - 5 : effect;
    }
    [Serializable]
    public class Document
    {
        public Item[] items;
    }
    private static Dictionary<int, Item> byHash;
    public static Item[] All
    {
        get {
            EnsureLoaded();
            return all;
        }
    }
    private static Item[] all;

    private static void EnsureLoaded()
    {
        if (byHash != null)
            return;
        byHash = new Dictionary<int, Item>();
        var source = Resources.Load<TextAsset>("ABItems");
        all = source != null ? JsonUtility.FromJson<Document>(source.text).items : Array.Empty<Item>();
        foreach (var item in all)
        {
            int hash = ItemHash.Of(item.id);
            if (!byHash.TryAdd(hash, item))
                Debug.LogError("Duplicate AB item/hash: " + item.id);
        }
    }
    public static Item Find(int hash)
    {
        EnsureLoaded();
        return byHash.TryGetValue(hash, out var item) ? item : null;
    }
}
