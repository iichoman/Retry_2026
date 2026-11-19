using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoomObjectPlacer2
{
    [Header("Room composition")]
    [SerializeField] private bool useRoomComposition = true;
    [SerializeField] private DungeonPropPalette compositionPalette;
    // Shared DungeonPropLayout now owns spacing, counts and all model dimensions.
    private readonly List<Bounds> compositionPlacements = new();
    private bool reportedPaletteMissing;
    public bool UsesRoomComposition => useRoomComposition || NetworkRuntime.IsOnline;

    private System.Random CreateRoomRandom(Room room) => new System.Random(unchecked(
        dungeonGenerator.seed * 397 ^ placementSeedOffset ^ room.bounds.xMin * 73856093 ^ room.bounds.zMin * 19349663));

    private void PlaceRoomComposition(Room room, System.Random random, Transform root)
    {
        if (compositionPalette == null) compositionPalette = DungeonPropPalette.Load();
        var slots = DungeonPropLayout.GenerateRoom(dungeonGenerator, room);
        Transform group = null;
        int groupId = -1;
        foreach (var slot in slots)
        {
            if (group == null || groupId != slot.GroupId)
            {
                group = new GameObject($"Room_{room.id}_{slot.GroupTheme}_{slot.GroupId}").transform;
                group.SetParent(root, false);
                groupId = slot.GroupId;
            }
            var prefab = compositionPalette != null ? compositionPalette.Resolve(slot.PropKind) : null;
            if(slot.PropKind==DungeonPropLayout.Kind.Book&&!slot.Supported)prefab=Resources.Load<GameObject>("RoomBlueprints/Bookcase");
            GameObject prop;
            if (prefab != null) prop = Instantiate(prefab, slot.Center, Quaternion.Euler(0, slot.Yaw, 0), group);
            else
            {
                // A visible solid placeholder preserves server geometry even when an asset is missing.
                prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prop.transform.SetParent(group, false);
                prop.transform.SetPositionAndRotation(slot.Center, Quaternion.Euler(0, slot.Yaw, 0));
                if (!reportedPaletteMissing) { Debug.LogError("DungeonPropPalette is incomplete. Visible collision placeholders are being used.", this); reportedPaletteMissing = true; }
            }
            bool quarterTurn = Mathf.RoundToInt(slot.Yaw / 90f) % 2 != 0;
            prop.transform.localScale = quarterTurn ? new Vector3(slot.Size.z, slot.Size.y, slot.Size.x) : slot.Size;
            prop.name = $"{slot.PropKind}_{slot.GroupId}";
            var marker = prop.GetComponent<DungeonPropObstacle>();if(marker==null)marker=prop.AddComponent<DungeonPropObstacle>();
            marker.RoomId = room.id; marker.GroupId = slot.GroupId; marker.Kind = slot.PropKind; marker.Supported = slot.Supported;
            int wallLayer = LayerMask.NameToLayer("Wall");
            prop.layer = wallLayer >= 0 ? wallLayer : 0;
            SimplePropCollider.Apply(prop, Vector3.zero, Vector3.one, !slot.Supported);
            spawnedObjects.Add(prop);
            compositionPlacements.Add(slot.Bounds);
            if (!slot.Supported)
            {
                var min = dungeonGenerator.WorldToTile(slot.Bounds.min + new Vector3(.001f, 0, .001f));
                var max = dungeonGenerator.WorldToTile(slot.Bounds.max - new Vector3(.001f, 0, .001f));
                for (int x = min.x; x <= max.x; x++)
                for (int z = min.z; z <= max.z; z++) occupiedFloorTiles.Add(new Vector3Int(x, 0, z));
            }
        }
    }
}
