using UnityEngine;

public sealed partial class RoomObjectPlacer2
{
    [Header("Required exit portal")]
    [Tooltip("Exactly one gameplay portal in the generator's single exit room. Not a decoration slot.")]
    [SerializeField] private GameObject exitPortalPrefab;

    // Includes the existing prefab's trigger and enough space for a player to approach.
    private const float ExitPortalClearance = 2.5f;

    private void ReserveExitPortalFloor()
    {
        // Shared composition already reserves central approach routes. Legacy offline
        // decoration must also leave the mandatory portal's floor free of centerpieces.
        if (!UsesRoomComposition && TryGetExitPortalFloorPosition(out Vector3 position))
        {
            Vector3Int tile = dungeonGenerator.WorldToTile(position);
            tile.y = 0;
            OccupyFloorArea(tile, Mathf.CeilToInt(ExitPortalClearance));
        }
    }

    private void PlaceExitPortal(Transform root)
    {
        if (dungeonGenerator.ExitRoom == null) return;
        int exitRooms = 0;
        foreach (var room in dungeonGenerator.Rooms)
            if (room.type == RoomType.Exit) exitRooms++;
        if (exitRooms != 1)
        {
            Debug.LogError($"[ExitPortal] Expected one exit room, found {exitRooms}.", this);
            return;
        }

        GameObject prefab = exitPortalPrefab;
        // Old serialized placers can migrate from the former decoration reference.
        if (prefab == null && propLibrary != null)
            foreach (var candidate in propLibrary.탈출의식소품목록)
                if (candidate != null && candidate.GetComponentInChildren<ExitPortal>(true) != null)
                { prefab = candidate; break; }
        if (prefab == null || prefab.GetComponentsInChildren<ExitPortal>(true).Length != 1)
        {
            Debug.LogError("[ExitPortal] Assign a prefab containing exactly one ExitPortal.", this);
            return;
        }
        if (!TryGetExitPortalFloorPosition(out Vector3 position))
        {
            Debug.LogError($"[ExitPortal] Exit room {dungeonGenerator.ExitRoom.id} has no safe portal floor.", this);
            return;
        }

        var portal = Spawn(prefab, position, Quaternion.identity, root);
        portal.name = $"ExitPortal_Room_{dungeonGenerator.ExitRoom.id}";
        // Mandatory floor alignment, independent of optional decoration settings.
        if (TryGetPlacementBounds(portal, out Bounds bounds))
            portal.transform.position += Vector3.up * (position.y - bounds.min.y);
        portal.SetActive(true);
    }

    public bool TryGetExitPortalFloorPosition(out Vector3 position)
    {
        position = default;
        Room room = dungeonGenerator != null ? dungeonGenerator.ExitRoom : null;
        if (room == null || room.type != RoomType.Exit) return false;
        Vector3 center = new Vector3(room.bounds.center.x, floorSurfaceHeight, room.bounds.center.z)
            + (Vector3)dungeonGenerator.WorldOffset;
        if (IsSafeExitPortalFloor(room, center)) { position = center; return true; }

        // CenterBlock is authoritative geometry. Keep it intact and choose the nearest
        // usable floor in the SAME exit room; never change the server's room/seed/layout.
        float nearest = float.PositiveInfinity;
        bool found = false;
        foreach (var tile in room.floorTiles)
        {
            Vector3 candidate = dungeonGenerator.TileToWorldCenter(tile) + Vector3.up * floorSurfaceHeight;
            float distance = (candidate - center).sqrMagnitude;
            if (distance > nearest) continue;
            if (distance == nearest && found &&
                (candidate.x > position.x || candidate.x == position.x && candidate.z >= position.z)) continue;
            if (!IsSafeExitPortalFloor(room, candidate)) continue;
            found = true; nearest = distance; position = candidate;
        }
        return found;
    }

    private bool IsSafeExitPortalFloor(Room room, Vector3 position)
    {
        Vector3Int min = dungeonGenerator.WorldToTile(position - new Vector3(ExitPortalClearance, 0, ExitPortalClearance));
        Vector3Int max = dungeonGenerator.WorldToTile(position + new Vector3(ExitPortalClearance, 0, ExitPortalClearance));
        for (int x = min.x; x <= max.x; x++)
        for (int z = min.z; z <= max.z; z++)
        {
            var tile = new Vector3Int(x, 0, z);
            if (!room.floorTiles.Contains(tile) || room.blockedTiles.Contains(tile) ||
                !dungeonGenerator.IsFloorTile(tile) || dungeonGenerator.IsWallTile(tile)) return false;
        }
        var clearance = new Bounds(position + Vector3.up * 2f,
            new Vector3(ExitPortalClearance * 2, 4f, ExitPortalClearance * 2));
        foreach (var occupied in compositionPlacements)
            if (clearance.Intersects(occupied)) return false;
        return true;
    }
}
