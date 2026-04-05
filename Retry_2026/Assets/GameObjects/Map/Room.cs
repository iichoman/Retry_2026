using System.Collections.Generic;
using UnityEngine;

public class Room
{
    public int id;
    public RoomType type;
    public RoomShape shape;
    public RoomLayoutType layoutType;
    public BoundsInt bounds;
    public List<int> neighbors = new();
    public HashSet<Vector3Int> floorTiles = new();
    public HashSet<Vector3Int> blockedTiles = new();
}
public enum RoomType
{
    Normal,
    Boss,
    Reward,
    Exit,
    Start
}


public enum RoomShape
{
    Small,
    Normal,
    Large,
    LongWide,
    LongTall
}

public enum RoomLayoutType
{
    Open,
    FourPillars,
    CenterBlock
}
