using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Retry/Dungeon Prop Palette")]
public sealed class DungeonPropPalette : ScriptableObject
{
    [Serializable] public struct Entry
    {
        public DungeonPropLayout.Kind kind;
        [Tooltip("Model fitted to a unit cube, with a unit BoxCollider on its root.")]
        public GameObject prefab;
    }
    public Entry[] entries = Array.Empty<Entry>();
    public GameObject Resolve(DungeonPropLayout.Kind kind)
    {
        foreach (var entry in entries) if (entry.kind == kind) return entry.prefab;
        return null;
    }
    public static DungeonPropPalette Load() => Resources.Load<DungeonPropPalette>("DungeonPropPalette");
}
