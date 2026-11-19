using System;
using UnityEngine;

public enum DungeonRoomStyle { Storehouse, Archive, Crypt, Guardian, Extraction, Treasure }

[CreateAssetMenu(menuName = "Retry/Dungeon Presentation Palette")]
public sealed class DungeonPresentationPalette : ScriptableObject
{
    [Serializable]
    public sealed class Theme
    {
        public DungeonRoomStyle style;
        public Material stone;
        public Material border;
        public Material accent;
    }

    public Theme[] themes = Array.Empty<Theme>();
    public Theme Resolve(DungeonRoomStyle style)
    {
        if (themes != null) foreach (var theme in themes)
            if (theme != null && theme.style == style) return theme;
        return null;
    }
    public static DungeonPresentationPalette Load() => Resources.Load<DungeonPresentationPalette>("DungeonPresentationPalette");
}
