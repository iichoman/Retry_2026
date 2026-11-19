using System.Collections.Generic;
using UnityEngine;

public class Corridor
{
    public int id;
    public HashSet<Vector3Int> floorTiles = new();
    public List<int> connectedRoomIds = new();
}
