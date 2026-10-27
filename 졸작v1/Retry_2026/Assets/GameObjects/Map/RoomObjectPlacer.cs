using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RoomObjectPlacer : MonoBehaviour
{
    public event Action<RoomObjectPlacer> RoomObjectsPlaced;

    [Header("참조")]
    [InspectorName("던전 생성기")]
    [SerializeField] private DungeonGenerator_ChunkMesh dungeonGenerator;

    [Header("배치 규칙")]
    [InspectorName("바닥 배치 규칙")]
    [SerializeField] private List<FloorPlacementRule> floorRules = new List<FloorPlacementRule>();
    [InspectorName("장식 배치 규칙")]
    [SerializeField] private List<DecorPlacementRule> decorRules = new List<DecorPlacementRule>();
    [InspectorName("벽 배치 규칙")]
    [SerializeField] private List<WallPlacementRule> wallRules = new List<WallPlacementRule>();

    [Header("공통 규칙")]
    [InspectorName("출입구 제외 반경")]
    [SerializeField, Min(0)] private int doorwayExclusionRadius = 3;
    [InspectorName("배치 시드 오프셋")]
    [SerializeField] private int placementSeedOffset = 5000;
    [InspectorName("던전 생성 후 자동 배치")]
    [SerializeField] private bool placeOnDungeonGenerated = true;

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly HashSet<Vector3Int> occupiedTiles = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> occupiedWallTiles = new HashSet<Vector3Int>();

    private const string RootName = "RoomObjects";

    public bool PlaceOnDungeonGenerated => placeOnDungeonGenerated;

    private void OnEnable()
    {
        if (dungeonGenerator == null)
        {
            dungeonGenerator = GetComponent<DungeonGenerator_ChunkMesh>();
        }

        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated += HandleDungeonGenerated;
        }
    }

    private void OnDisable()
    {
        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated -= HandleDungeonGenerated;
        }
    }

    [ContextMenu("방 오브젝트 배치")]
    public void PlaceRoomObjects()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogWarning("RoomObjectPlacer requires a DungeonGenerator_ChunkMesh reference.", this);
            return;
        }

        ClearPlacedObjects();

        var random = new System.Random(dungeonGenerator.seed + placementSeedOffset);
        Transform root = GetOrCreateRoot();

        foreach (Room room in dungeonGenerator.Rooms)
        {
            PlaceFloorObjects(room, random, root);
            PlaceDecorObjects(room, random, root);
            PlaceWallObjects(room, random, root);
        }

        PlaceStartRoomWallObjects(random, root);
        PlaceCorridorWallObjects(random, root);

        RoomObjectsPlaced?.Invoke(this);
    }

    [ContextMenu("방 오브젝트 제거")]
    public void ClearPlacedObjects()
    {
        spawnedObjects.Clear();
        occupiedTiles.Clear();
        occupiedWallTiles.Clear();

        Transform root = transform.Find(RootName);
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyObject(root.GetChild(i).gameObject);
        }
    }

    public bool IsOccupied(Vector3Int tile)
    {
        return occupiedTiles.Contains(tile);
    }

    private void HandleDungeonGenerated(DungeonGenerator_ChunkMesh generator)
    {
        if (!placeOnDungeonGenerated)
        {
            return;
        }

        PlaceRoomObjects();
    }

    private void PlaceFloorObjects(Room room, System.Random random, Transform root)
    {
        foreach (FloorPlacementRule rule in floorRules)
        {
            if (!rule.CanPlaceIn(room))
            {
                continue;
            }

            int count = rule.GetCount(random);
            for (int i = 0; i < count; i++)
            {
                if (!TryPickFloorTile(room, rule, random, i, out Vector3Int tile))
                {
                    break;
                }

                GameObject prefab = rule.PickPrefab(random);
                if (prefab == null)
                {
                    continue;
                }

                Vector3 floorSurfaceCenter = GetFloorSurfaceCenter(tile);
                Vector3 position = floorSurfaceCenter + rule.positionOffset;
                Quaternion rotation = rule.randomYaw
                    ? Quaternion.Euler(0f, (float)(random.NextDouble() * 360f), 0f)
                    : Quaternion.Euler(rule.rotationOffset);

                Spawn(prefab, position, rotation, root);

                OccupyArea(tile, rule.footprintRadius);
            }
        }
    }

    private void PlaceWallObjects(Room room, System.Random random, Transform root)
    {
        foreach (WallPlacementRule rule in wallRules)
        {
            if ((rule.placementTargets & WallPlacementTargetMask.Rooms) == 0)
            {
                continue;
            }

            if (!rule.CanPlaceIn(room))
            {
                continue;
            }

            if (rule.placementMode == WallPlacementMode.EvenlySpacedLines)
            {
                PlaceWallLineObjects(room, rule, random, root);
                continue;
            }

            if (rule.placementMode == WallPlacementMode.SegmentedEven)
            {
                PlaceSegmentedRoomWallObjects(room, rule, random, root);
                continue;
            }

            int count = rule.GetCount(random);
            for (int i = 0; i < count; i++)
            {
                if (!TryPickWallCandidate(room, rule, random, out WallPlacementCandidate candidate))
                {
                    break;
                }

                GameObject prefab = rule.PickPrefab(random);
                if (prefab == null)
                {
                    continue;
                }

                Vector3 position = GetWallFacePosition(candidate) +
                                   candidate.roomFacingDirection * rule.wallSurfaceOffset +
                                   rule.positionOffset;
                Quaternion rotation = Quaternion.LookRotation(candidate.roomFacingDirection, Vector3.up) * Quaternion.Euler(rule.rotationOffset);

                Spawn(prefab, position, rotation, root);
                OccupyArea(candidate.floorTile, rule.floorExclusionRadius);
            }
        }
    }

    private void PlaceDecorObjects(Room room, System.Random random, Transform root)
    {
        foreach (DecorPlacementRule rule in decorRules)
        {
            if (!rule.CanPlaceIn(room))
            {
                continue;
            }

            int count = rule.GetCount(random);
            for (int i = 0; i < count; i++)
            {
                if (!TryPickDecorTile(room, rule, random, out Vector3Int tile))
                {
                    break;
                }

                GameObject prefab = rule.PickPrefab(random);
                if (prefab == null)
                {
                    continue;
                }

                Vector3 position = GetFloorSurfaceCenter(tile) + rule.positionOffset;
                Quaternion rotation = GetDecorRotation(tile, rule, random);

                Spawn(prefab, position, rotation, root);
                OccupyArea(tile, rule.footprintRadius);
            }
        }
    }

    private void PlaceStartRoomWallObjects(System.Random random, Transform root)
    {
        foreach (StartRoom startRoom in dungeonGenerator.GetAssignedStartRooms())
        {
            foreach (WallPlacementRule rule in wallRules)
            {
                if ((rule.placementTargets & WallPlacementTargetMask.StartRooms) == 0)
                {
                    continue;
                }

                if (!rule.CanPlaceStartRoom())
                {
                    continue;
                }

                if (rule.placementMode == WallPlacementMode.SegmentedEven)
                {
                    PlaceSegmentedStartRoomWallObjects(startRoom, rule, random, root);
                    continue;
                }

                PlaceWallCandidates(GetStartRoomWallCandidates(startRoom, rule), rule, random, root);
            }
        }
    }

    private void PlaceCorridorWallObjects(System.Random random, Transform root)
    {
        foreach (Corridor corridor in dungeonGenerator.Corridors)
        {
            foreach (WallPlacementRule rule in wallRules)
            {
                if ((rule.placementTargets & WallPlacementTargetMask.Corridors) == 0)
                {
                    continue;
                }

                if (!rule.CanPlaceCorridor())
                {
                    continue;
                }

                if (rule.placementMode == WallPlacementMode.SegmentedEven)
                {
                    PlaceSegmentedCorridorWallObjects(corridor, rule, random, root);
                    continue;
                }

                PlaceWallCandidates(GetCorridorWallCandidates(corridor, rule), rule, random, root);
            }
        }
    }

    private void PlaceWallLineObjects(Room room, WallPlacementRule rule, System.Random random, Transform root)
    {
        PlaceWallCandidates(GetWallLineCandidates(room, rule), rule, random, root);
    }

    private void PlaceWallCandidates(List<WallPlacementCandidate> candidates, WallPlacementRule rule, System.Random random, Transform root)
    {
        foreach (WallPlacementCandidate candidate in candidates)
        {
            TryPlaceWallCandidate(candidate, rule, random, root, true);
        }
    }

    private void PlaceSegmentedRoomWallObjects(Room room, WallPlacementRule rule, System.Random random, Transform root)
    {
        foreach (WallSegment segment in dungeonGenerator.WallSegments)
        {
            if (segment.ownerType != WallSegmentOwnerType.Room || segment.ownerId != room.id)
            {
                continue;
            }

            if (!MatchesWallSide(rule.wallSides, segment.side))
            {
                continue;
            }

            List<int> coordinates = CalculateEvenCoordinatesForSegment(segment, rule);
            PlaceSegmentedWallCoordinates(segment, coordinates, rule, random, root, room);
        }
    }

    private void PlaceSegmentedStartRoomWallObjects(StartRoom startRoom, WallPlacementRule rule, System.Random random, Transform root)
    {
        foreach (WallSegment segment in dungeonGenerator.WallSegments)
        {
            if (segment.ownerType != WallSegmentOwnerType.StartRoom || segment.ownerId != startRoom.slotIndex)
            {
                continue;
            }

            if (!MatchesWallSide(rule.wallSides, segment.side))
            {
                continue;
            }

            List<int> coordinates = CalculateEvenCoordinatesForSegment(segment, rule);
            PlaceSegmentedWallCoordinates(segment, coordinates, rule, random, root, null);
        }
    }

    private void PlaceSegmentedCorridorWallObjects(Corridor corridor, WallPlacementRule rule, System.Random random, Transform root)
    {
        foreach (WallSegment segment in dungeonGenerator.WallSegments)
        {
            if (segment.ownerType != WallSegmentOwnerType.Corridor || segment.ownerId != corridor.id)
            {
                continue;
            }

            if (!MatchesWallSide(rule.wallSides, segment.side))
            {
                continue;
            }

            if (!rule.placeBothCorridorSides && (segment.side == WallSide.South || segment.side == WallSide.West))
            {
                continue;
            }

            List<int> coordinates = CalculateEvenCoordinatesForSegment(segment, rule);
            PlaceSegmentedWallCoordinates(segment, coordinates, rule, random, root, null);
        }
    }

    private void PlaceSegmentedWallCoordinates(
        WallSegment segment,
        List<int> coordinates,
        WallPlacementRule rule,
        System.Random random,
        Transform root,
        Room roomForDoorwayCheck)
    {
        if (coordinates.Count == 0)
        {
            return;
        }

        for (int i = 0; i < coordinates.Count; i++)
        {
            if (!TryCreateCandidateAtCoordinate(segment, coordinates[i], out WallPlacementCandidate candidate))
            {
                continue;
            }

            if (roomForDoorwayCheck != null &&
                IsNearDoorway(roomForDoorwayCheck, candidate.floorTile, doorwayExclusionRadius + rule.extraDoorwayExclusionRadius + rule.segmentedDoorwayExclusionRadius))
            {
                continue;
            }

            if (segment.ownerType == WallSegmentOwnerType.Corridor &&
                IsNearAnyRoomDoorway(candidate.floorTile, rule.corridorJunctionExclusionRadius))
            {
                continue;
            }

            if (IsNearOccupiedWallTile(candidate.wallTile, rule.wallPlacementExclusionRadius))
            {
                continue;
            }

            TryPlaceWallCandidate(candidate, rule, random, root, true);
        }
    }

    private bool TryCreateCandidateAtCoordinate(WallSegment segment, int coordinate, out WallPlacementCandidate candidate)
    {
        for (int i = 0; i < segment.wallTiles.Count; i++)
        {
            Vector3Int wallTile = segment.wallTiles[i];
            int tileCoordinate = IsHorizontalWall(segment.side) ? wallTile.x : wallTile.z;
            if (tileCoordinate != coordinate)
            {
                continue;
            }

            Vector3Int floorTile = i < segment.floorTiles.Count ? segment.floorTiles[i] : default;
            candidate = new WallPlacementCandidate(floorTile, wallTile, segment.roomFacingDirection);
            return true;
        }

        candidate = default;
        return false;
    }

    private bool TryPlaceWallCandidate(
        WallPlacementCandidate candidate,
        WallPlacementRule rule,
        System.Random random,
        Transform root,
        bool occupyAfterPlacement)
    {
        if (IsAreaOccupied(candidate.floorTile, rule.floorExclusionRadius))
        {
            return false;
        }

        GameObject prefab = rule.PickPrefab(random);
        if (prefab == null)
        {
            return false;
        }

        Vector3 position = GetWallFacePosition(candidate) +
                           candidate.roomFacingDirection * rule.wallSurfaceOffset +
                           rule.positionOffset;
        Quaternion rotation = Quaternion.LookRotation(candidate.roomFacingDirection, Vector3.up) * Quaternion.Euler(rule.rotationOffset);

        Spawn(prefab, position, rotation, root);

        if (occupyAfterPlacement)
        {
            OccupyArea(candidate.floorTile, rule.floorExclusionRadius);
            OccupyWallArea(candidate.wallTile, rule.wallPlacementExclusionRadius);
        }

        return true;
    }

    private List<int> CalculateEvenCoordinatesForSegment(WallSegment segment, WallPlacementRule rule)
    {
        if (segment.wallTiles.Count == 0)
        {
            return new List<int>();
        }

        int start = IsHorizontalWall(segment.side)
            ? Mathf.Min(segment.startWallTile.x, segment.endWallTile.x)
            : Mathf.Min(segment.startWallTile.z, segment.endWallTile.z);
        int endInclusive = IsHorizontalWall(segment.side)
            ? Mathf.Max(segment.startWallTile.x, segment.endWallTile.x)
            : Mathf.Max(segment.startWallTile.z, segment.endWallTile.z);

        return CalculateEvenCoordinates(start, endInclusive + 1, rule);
    }

    private static List<int> CalculateEvenCoordinates(int startInclusive, int endExclusive, WallPlacementRule rule)
    {
        var coordinates = new List<int>();
        int length = Mathf.Max(0, endExclusive - startInclusive);
        int padding = Mathf.Max(0, rule.edgePaddingTiles);
        int usableLength = length - padding * 2;

        if (usableLength < Mathf.Max(1, rule.minSegmentLengthTiles))
        {
            return coordinates;
        }

        int first = startInclusive + padding;
        int last = endExclusive - padding - 1;
        if (last < first)
        {
            return coordinates;
        }

        if (rule.centerAlignSegmentPlacements)
        {
            return CalculateCenteredCoordinates(first, last, usableLength, rule);
        }

        int spacing = Mathf.Max(1, rule.spacingTiles);
        int count = Mathf.Max(1, Mathf.FloorToInt(usableLength / (float)spacing) + 1);

        if (count == 1)
        {
            coordinates.Add(Mathf.RoundToInt((first + last) * 0.5f));
            return coordinates;
        }

        var used = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            int coordinate = Mathf.RoundToInt(Mathf.Lerp(first, last, t));
            if (used.Add(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        return coordinates;
    }

    private static List<int> CalculateCenteredCoordinates(int first, int last, int usableLength, WallPlacementRule rule)
    {
        var coordinates = new List<int>();
        int spacing = Mathf.Max(1, rule.spacingTiles);
        int count = Mathf.Max(1, Mathf.FloorToInt(usableLength / (float)spacing) + 1);

        while (count > 1 && (count - 1) * spacing > usableLength - 1)
        {
            count--;
        }

        float center = (first + last) * 0.5f;
        if (count == 1)
        {
            coordinates.Add(Mathf.RoundToInt(center));
            return coordinates;
        }

        float totalSpan = (count - 1) * spacing;
        float start = center - totalSpan * 0.5f;
        var used = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int coordinate = Mathf.RoundToInt(start + i * spacing);
            coordinate = Mathf.Clamp(coordinate, first, last);
            if (used.Add(coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        return coordinates;
    }

    private bool TryPickFloorTile(
        Room room,
        FloorPlacementRule rule,
        System.Random random,
        int placementIndex,
        out Vector3Int selectedTile)
    {
        selectedTile = default;

        if (rule.placementMode == FloorPlacementMode.Center)
        {
            return TryPickCenterFloorTile(room, rule, placementIndex, out selectedTile);
        }

        var candidates = new List<Vector3Int>();
        foreach (Vector3Int tile in room.floorTiles)
        {
            if (!IsValidFloorTile(room, tile, rule))
            {
                continue;
            }

            candidates.Add(tile);
        }

        return TryPickRandom(candidates, random, out selectedTile);
    }

    private bool TryPickCenterFloorTile(Room room, FloorPlacementRule rule, int placementIndex, out Vector3Int selectedTile)
    {
        selectedTile = default;

        if (placementIndex > 0)
        {
            return false;
        }

        Vector3 center = room.bounds.center;
        var candidates = new List<Vector3Int>();
        foreach (Vector3Int tile in room.floorTiles)
        {
            if (!IsValidFloorTile(room, tile, rule))
            {
                continue;
            }

            candidates.Add(tile);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        candidates.Sort((a, b) =>
        {
            float distanceA = (new Vector2(a.x + 0.5f, a.z + 0.5f) - new Vector2(center.x, center.z)).sqrMagnitude;
            float distanceB = (new Vector2(b.x + 0.5f, b.z + 0.5f) - new Vector2(center.x, center.z)).sqrMagnitude;
            return distanceA.CompareTo(distanceB);
        });

        selectedTile = candidates[0];
        return true;
    }

    private bool IsValidFloorTile(Room room, Vector3Int tile, FloorPlacementRule rule)
    {
        if (IsNearDoorway(room, tile, doorwayExclusionRadius + rule.extraDoorwayExclusionRadius))
        {
            return false;
        }

        if (IsAreaOccupied(tile, rule.footprintRadius))
        {
            return false;
        }

        if (rule.keepAwayFromWalls && IsAdjacentToWall(tile))
        {
            return false;
        }

        return true;
    }

    private bool TryPickDecorTile(Room room, DecorPlacementRule rule, System.Random random, out Vector3Int selectedTile)
    {
        selectedTile = default;

        var candidates = new List<Vector3Int>();
        foreach (Vector3Int tile in room.floorTiles)
        {
            if (!IsValidDecorTile(room, tile, rule))
            {
                continue;
            }

            candidates.Add(tile);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        if (rule.placementMode == DecorPlacementMode.CenterCluster)
        {
            Vector2 center = new Vector2(room.bounds.center.x, room.bounds.center.z);
            candidates.Sort((a, b) =>
            {
                float distanceA = (new Vector2(a.x + 0.5f, a.z + 0.5f) - center).sqrMagnitude;
                float distanceB = (new Vector2(b.x + 0.5f, b.z + 0.5f) - center).sqrMagnitude;
                return distanceA.CompareTo(distanceB);
            });

            int takeFrom = Mathf.Min(candidates.Count, Mathf.Max(1, rule.centerCandidatePoolSize));
            selectedTile = candidates[random.Next(0, takeFrom)];
            return true;
        }

        return TryPickRandom(candidates, random, out selectedTile);
    }

    private bool IsValidDecorTile(Room room, Vector3Int tile, DecorPlacementRule rule)
    {
        if (IsNearDoorway(room, tile, doorwayExclusionRadius + rule.extraDoorwayExclusionRadius))
        {
            return false;
        }

        if (IsAreaOccupied(tile, rule.footprintRadius))
        {
            return false;
        }

        if (rule.wallClearanceTiles > 0 && IsNearWall(tile, rule.wallClearanceTiles))
        {
            return false;
        }

        int adjacentWallCount = CountAdjacentWalls(tile);

        return rule.placementMode switch
        {
            DecorPlacementMode.RandomOpen => true,
            DecorPlacementMode.AwayFromWalls => adjacentWallCount == 0,
            DecorPlacementMode.NearWall => adjacentWallCount > 0,
            DecorPlacementMode.AlongWall => adjacentWallCount == 1,
            DecorPlacementMode.Corner => adjacentWallCount >= 2,
            DecorPlacementMode.CenterCluster => adjacentWallCount == 0,
            _ => true
        };
    }

    private bool IsNearWall(Vector3Int tile, int radius)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) > radius)
                {
                    continue;
                }

                if (dungeonGenerator.IsWallTile(tile + new Vector3Int(x, 0, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Quaternion GetDecorRotation(Vector3Int tile, DecorPlacementRule rule, System.Random random)
    {
        Quaternion baseRotation;
        if (rule.faceRoomFromNearestWall && TryGetNearestWallFacingDirection(tile, out Vector3 facingDirection))
        {
            baseRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
        }
        else if (rule.randomYaw)
        {
            baseRotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360f), 0f);
        }
        else
        {
            baseRotation = Quaternion.identity;
        }

        return baseRotation * Quaternion.Euler(rule.rotationOffset);
    }

    private int CountAdjacentWalls(Vector3Int tile)
    {
        int count = 0;
        foreach (Vector3Int direction in HorizontalDirections)
        {
            if (dungeonGenerator.IsWallTile(tile + direction))
            {
                count++;
            }
        }

        return count;
    }

    private bool TryGetNearestWallFacingDirection(Vector3Int tile, out Vector3 facingDirection)
    {
        foreach (Vector3Int direction in HorizontalDirections)
        {
            if (!dungeonGenerator.IsWallTile(tile + direction))
            {
                continue;
            }

            facingDirection = new Vector3(-direction.x, 0f, -direction.z);
            return true;
        }

        facingDirection = Vector3.forward;
        return false;
    }

    private bool TryPickWallCandidate(Room room, WallPlacementRule rule, System.Random random, out WallPlacementCandidate selectedCandidate)
    {
        selectedCandidate = default;

        var candidates = new List<WallPlacementCandidate>();
        foreach (Vector3Int floorTile in room.floorTiles)
        {
            if (IsNearDoorway(room, floorTile, doorwayExclusionRadius + rule.extraDoorwayExclusionRadius))
            {
                continue;
            }

            if (IsAreaOccupied(floorTile, rule.floorExclusionRadius))
            {
                continue;
            }

            foreach (Vector3Int wallDirection in HorizontalDirections)
            {
                Vector3Int wallTile = floorTile + wallDirection;
                if (!dungeonGenerator.IsWallTile(wallTile))
                {
                    continue;
                }

                candidates.Add(new WallPlacementCandidate(
                    floorTile,
                    wallTile,
                    new Vector3(-wallDirection.x, 0f, -wallDirection.z)
                ));
            }
        }

        return TryPickRandom(candidates, random, out selectedCandidate);
    }

    private List<WallPlacementCandidate> GetWallLineCandidates(Room room, WallPlacementRule rule)
    {
        var candidates = new List<WallPlacementCandidate>();
        int spacing = Mathf.Max(1, rule.spacingTiles);
        int padding = Mathf.Max(0, rule.edgePaddingTiles);

        if ((rule.wallSides & WallSideMask.North) != 0)
        {
            int floorZ = room.bounds.zMax - 1;
            int wallZ = room.bounds.zMax;
            for (int x = room.bounds.xMin + padding; x < room.bounds.xMax - padding; x += spacing)
            {
                AddWallLineCandidate(room, rule, candidates,
                    new Vector3Int(x, room.bounds.yMin, floorZ),
                    new Vector3Int(x, room.bounds.yMin, wallZ),
                    Vector3.back);
            }
        }

        if ((rule.wallSides & WallSideMask.South) != 0)
        {
            int floorZ = room.bounds.zMin;
            int wallZ = room.bounds.zMin - 1;
            for (int x = room.bounds.xMin + padding; x < room.bounds.xMax - padding; x += spacing)
            {
                AddWallLineCandidate(room, rule, candidates,
                    new Vector3Int(x, room.bounds.yMin, floorZ),
                    new Vector3Int(x, room.bounds.yMin, wallZ),
                    Vector3.forward);
            }
        }

        if ((rule.wallSides & WallSideMask.East) != 0)
        {
            int floorX = room.bounds.xMax - 1;
            int wallX = room.bounds.xMax;
            for (int z = room.bounds.zMin + padding; z < room.bounds.zMax - padding; z += spacing)
            {
                AddWallLineCandidate(room, rule, candidates,
                    new Vector3Int(floorX, room.bounds.yMin, z),
                    new Vector3Int(wallX, room.bounds.yMin, z),
                    Vector3.left);
            }
        }

        if ((rule.wallSides & WallSideMask.West) != 0)
        {
            int floorX = room.bounds.xMin;
            int wallX = room.bounds.xMin - 1;
            for (int z = room.bounds.zMin + padding; z < room.bounds.zMax - padding; z += spacing)
            {
                AddWallLineCandidate(room, rule, candidates,
                    new Vector3Int(floorX, room.bounds.yMin, z),
                    new Vector3Int(wallX, room.bounds.yMin, z),
                    Vector3.right);
            }
        }

        return candidates;
    }

    private List<WallPlacementCandidate> GetStartRoomWallCandidates(StartRoom startRoom, WallPlacementRule rule)
    {
        var floorTiles = new HashSet<Vector3Int>();
        BoundsInt bounds = startRoom.bounds;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int z = bounds.zMin; z < bounds.zMax; z++)
            {
                floorTiles.Add(new Vector3Int(x, bounds.yMin, z));
            }
        }

        return GetBoundsWallLineCandidates(bounds, floorTiles, rule, null);
    }

    private List<WallPlacementCandidate> GetBoundsWallLineCandidates(
        BoundsInt bounds,
        HashSet<Vector3Int> floorTiles,
        WallPlacementRule rule,
        Room roomForDoorwayCheck)
    {
        var candidates = new List<WallPlacementCandidate>();
        int spacing = Mathf.Max(1, rule.spacingTiles);
        int padding = Mathf.Max(0, rule.edgePaddingTiles);

        if ((rule.wallSides & WallSideMask.North) != 0)
        {
            int floorZ = bounds.zMax - 1;
            int wallZ = bounds.zMax;
            for (int x = bounds.xMin + padding; x < bounds.xMax - padding; x += spacing)
            {
                AddWallLineCandidate(roomForDoorwayCheck, rule, candidates, floorTiles,
                    new Vector3Int(x, bounds.yMin, floorZ),
                    new Vector3Int(x, bounds.yMin, wallZ),
                    Vector3.back);
            }
        }

        if ((rule.wallSides & WallSideMask.South) != 0)
        {
            int floorZ = bounds.zMin;
            int wallZ = bounds.zMin - 1;
            for (int x = bounds.xMin + padding; x < bounds.xMax - padding; x += spacing)
            {
                AddWallLineCandidate(roomForDoorwayCheck, rule, candidates, floorTiles,
                    new Vector3Int(x, bounds.yMin, floorZ),
                    new Vector3Int(x, bounds.yMin, wallZ),
                    Vector3.forward);
            }
        }

        if ((rule.wallSides & WallSideMask.East) != 0)
        {
            int floorX = bounds.xMax - 1;
            int wallX = bounds.xMax;
            for (int z = bounds.zMin + padding; z < bounds.zMax - padding; z += spacing)
            {
                AddWallLineCandidate(roomForDoorwayCheck, rule, candidates, floorTiles,
                    new Vector3Int(floorX, bounds.yMin, z),
                    new Vector3Int(wallX, bounds.yMin, z),
                    Vector3.left);
            }
        }

        if ((rule.wallSides & WallSideMask.West) != 0)
        {
            int floorX = bounds.xMin;
            int wallX = bounds.xMin - 1;
            for (int z = bounds.zMin + padding; z < bounds.zMax - padding; z += spacing)
            {
                AddWallLineCandidate(roomForDoorwayCheck, rule, candidates, floorTiles,
                    new Vector3Int(floorX, bounds.yMin, z),
                    new Vector3Int(wallX, bounds.yMin, z),
                    Vector3.right);
            }
        }

        return candidates;
    }

    private List<WallPlacementCandidate> GetCorridorWallCandidates(Corridor corridor, WallPlacementRule rule)
    {
        var candidates = new List<WallPlacementCandidate>();
        int spacing = Mathf.Max(1, rule.spacingTiles);

        foreach (Vector3Int floorTile in corridor.floorTiles)
        {
            if (!ShouldKeepCorridorTile(floorTile, spacing))
            {
                continue;
            }

            foreach (Vector3Int wallDirection in HorizontalDirections)
            {
                Vector3Int wallTile = floorTile + wallDirection;
                if (!dungeonGenerator.IsWallTile(wallTile))
                {
                    continue;
                }

                if (!MatchesWallSide(rule.wallSides, wallDirection))
                {
                    continue;
                }

                if (IsAreaOccupied(floorTile, rule.floorExclusionRadius))
                {
                    continue;
                }

                candidates.Add(new WallPlacementCandidate(
                    floorTile,
                    wallTile,
                    new Vector3(-wallDirection.x, 0f, -wallDirection.z)
                ));
            }
        }

        return candidates;
    }

    private bool ShouldKeepCorridorTile(Vector3Int tile, int spacing)
    {
        if (spacing <= 1)
        {
            return true;
        }

        int hash = Mathf.Abs(tile.x * 73856093 ^ tile.z * 19349663);
        return hash % spacing == 0;
    }

    private void AddWallLineCandidate(
        Room room,
        WallPlacementRule rule,
        List<WallPlacementCandidate> candidates,
        Vector3Int floorTile,
        Vector3Int wallTile,
        Vector3 roomFacingDirection)
    {
        if (!room.floorTiles.Contains(floorTile))
        {
            return;
        }

        if (!dungeonGenerator.IsWallTile(wallTile))
        {
            return;
        }

        if (IsNearDoorway(room, floorTile, doorwayExclusionRadius + rule.extraDoorwayExclusionRadius))
        {
            return;
        }

        if (IsAreaOccupied(floorTile, rule.floorExclusionRadius))
        {
            return;
        }

        candidates.Add(new WallPlacementCandidate(floorTile, wallTile, roomFacingDirection));
    }

    private void AddWallLineCandidate(
        Room roomForDoorwayCheck,
        WallPlacementRule rule,
        List<WallPlacementCandidate> candidates,
        HashSet<Vector3Int> floorTiles,
        Vector3Int floorTile,
        Vector3Int wallTile,
        Vector3 roomFacingDirection)
    {
        if (!floorTiles.Contains(floorTile))
        {
            return;
        }

        if (!dungeonGenerator.IsWallTile(wallTile))
        {
            return;
        }

        if (roomForDoorwayCheck != null &&
            IsNearDoorway(roomForDoorwayCheck, floorTile, doorwayExclusionRadius + rule.extraDoorwayExclusionRadius))
        {
            return;
        }

        if (IsAreaOccupied(floorTile, rule.floorExclusionRadius))
        {
            return;
        }

        candidates.Add(new WallPlacementCandidate(floorTile, wallTile, roomFacingDirection));
    }

    private static bool MatchesWallSide(WallSideMask wallSides, Vector3Int wallDirection)
    {
        if (wallDirection == Vector3Int.forward)
        {
            return (wallSides & WallSideMask.North) != 0;
        }

        if (wallDirection == Vector3Int.back)
        {
            return (wallSides & WallSideMask.South) != 0;
        }

        if (wallDirection == Vector3Int.right)
        {
            return (wallSides & WallSideMask.East) != 0;
        }

        if (wallDirection == Vector3Int.left)
        {
            return (wallSides & WallSideMask.West) != 0;
        }

        return false;
    }

    private bool IsNearDoorway(Room room, Vector3Int tile, int radius)
    {
        if (radius <= 0 || room.doorwayFloorTiles == null || room.doorwayFloorTiles.Count == 0)
        {
            return false;
        }

        int sqrRadius = radius * radius;
        foreach (Vector3Int doorwayTile in room.doorwayFloorTiles)
        {
            int dx = tile.x - doorwayTile.x;
            int dz = tile.z - doorwayTile.z;
            if (dx * dx + dz * dz <= sqrRadius)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsNearAnyRoomDoorway(Vector3Int tile, int radius)
    {
        if (radius <= 0)
        {
            return false;
        }

        foreach (Room room in dungeonGenerator.Rooms)
        {
            if (IsNearDoorway(room, tile, radius))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAdjacentToWall(Vector3Int tile)
    {
        foreach (Vector3Int direction in HorizontalDirections)
        {
            if (dungeonGenerator.IsWallTile(tile + direction))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAreaOccupied(Vector3Int center, int radius)
    {
        if (radius <= 0)
        {
            return occupiedTiles.Contains(center);
        }

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                if (occupiedTiles.Contains(new Vector3Int(x, center.y, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OccupyArea(Vector3Int center, int radius)
    {
        if (radius <= 0)
        {
            occupiedTiles.Add(center);
            return;
        }

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                occupiedTiles.Add(new Vector3Int(x, center.y, z));
            }
        }
    }

    private bool IsNearOccupiedWallTile(Vector3Int center, int radius)
    {
        if (radius <= 0)
        {
            return occupiedWallTiles.Contains(new Vector3Int(center.x, 0, center.z));
        }

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                if (occupiedWallTiles.Contains(new Vector3Int(x, 0, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OccupyWallArea(Vector3Int center, int radius)
    {
        if (radius <= 0)
        {
            occupiedWallTiles.Add(new Vector3Int(center.x, 0, center.z));
            return;
        }

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                occupiedWallTiles.Add(new Vector3Int(x, 0, z));
            }
        }
    }

    private Vector3 GetWallFacePosition(WallPlacementCandidate candidate)
    {
        Vector3 wallCenter = dungeonGenerator.TileToWorldCenter(candidate.wallTile);
        return wallCenter + candidate.roomFacingDirection * 0.5f;
    }

    private Vector3 GetFloorSurfaceCenter(Vector3Int tile)
    {
        Vector3 center = dungeonGenerator.TileToWorldCenter(tile);
        center.y += 1.0f;
        return center;
    }

    private GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform root)
    {
        GameObject spawnedObject = Instantiate(prefab, position, rotation, root);
        spawnedObjects.Add(spawnedObject);
        return spawnedObject;
    }

    private Transform GetOrCreateRoot()
    {
        Transform root = transform.Find(RootName);
        if (root != null)
        {
            return root;
        }

        var rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(transform, false);
        return rootObject.transform;
    }

    private void DestroyObject(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static bool TryPickRandom<T>(List<T> candidates, System.Random random, out T selected)
    {
        if (candidates.Count == 0)
        {
            selected = default;
            return false;
        }

        selected = candidates[random.Next(0, candidates.Count)];
        return true;
    }

    private static bool IsHorizontalWall(WallSide side)
    {
        return side == WallSide.North || side == WallSide.South;
    }

    private static bool MatchesWallSide(WallSideMask wallSides, WallSide side)
    {
        return side switch
        {
            WallSide.North => (wallSides & WallSideMask.North) != 0,
            WallSide.South => (wallSides & WallSideMask.South) != 0,
            WallSide.East => (wallSides & WallSideMask.East) != 0,
            WallSide.West => (wallSides & WallSideMask.West) != 0,
            _ => false
        };
    }

    private static readonly Vector3Int[] HorizontalDirections =
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.forward,
        Vector3Int.back
    };

    private readonly struct WallPlacementCandidate
    {
        public readonly Vector3Int floorTile;
        public readonly Vector3Int wallTile;
        public readonly Vector3 roomFacingDirection;

        public WallPlacementCandidate(Vector3Int floorTile, Vector3Int wallTile, Vector3 roomFacingDirection)
        {
            this.floorTile = floorTile;
            this.wallTile = wallTile;
            this.roomFacingDirection = roomFacingDirection;
        }
    }

    [Serializable]
    private abstract class PlacementRule
    {
        [InspectorName("규칙 이름")]
        public string label = string.Empty;
        [InspectorName("사용")]
        public bool enabled = true;
        [InspectorName("프리팹 목록")]
        public List<GameObject> prefabs = new List<GameObject>();
        [InspectorName("방당 최소 개수")]
        [Min(0)] public int minPerRoom = 0;
        [InspectorName("방당 최대 개수")]
        [Min(0)] public int maxPerRoom = 1;
        [InspectorName("대상 방 타입")]
        public RoomTypeMask roomTypes = RoomTypeMask.Normal;
        [InspectorName("대상 방 크기/형태")]
        public RoomShapeMask roomShapes = RoomShapeMask.All;
        [InspectorName("대상 방 레이아웃")]
        public RoomLayoutMask roomLayouts = RoomLayoutMask.All;
        [InspectorName("위치 오프셋")]
        public Vector3 positionOffset = Vector3.zero;
        [InspectorName("회전 오프셋")]
        public Vector3 rotationOffset = Vector3.zero;
        [InspectorName("추가 출입구 제외 반경")]
        [Min(0)] public int extraDoorwayExclusionRadius = 0;

        public bool CanPlaceIn(Room room)
        {
            return enabled &&
                   prefabs.Count > 0 &&
                   MatchesRoomType(room.type) &&
                   MatchesRoomShape(room.shape) &&
                   MatchesRoomLayout(room.layoutType);
        }

        public int GetCount(System.Random random)
        {
            int min = Mathf.Max(0, minPerRoom);
            int max = Mathf.Max(min, maxPerRoom);
            return random.Next(min, max + 1);
        }

        public GameObject PickPrefab(System.Random random)
        {
            if (prefabs.Count == 0)
            {
                return null;
            }

            return prefabs[random.Next(0, prefabs.Count)];
        }

        private bool MatchesRoomType(RoomType roomType)
        {
            return (roomTypes & ToMask(roomType)) != 0;
        }

        private bool MatchesRoomShape(RoomShape roomShape)
        {
            return (roomShapes & ToMask(roomShape)) != 0;
        }

        private bool MatchesRoomLayout(RoomLayoutType roomLayout)
        {
            return (roomLayouts & ToMask(roomLayout)) != 0;
        }

        private static RoomTypeMask ToMask(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Normal => RoomTypeMask.Normal,
                RoomType.Boss => RoomTypeMask.Boss,
                RoomType.Reward => RoomTypeMask.Reward,
                RoomType.Exit => RoomTypeMask.Exit,
                RoomType.Start => RoomTypeMask.Start,
                _ => RoomTypeMask.None
            };
        }

        private static RoomShapeMask ToMask(RoomShape roomShape)
        {
            return roomShape switch
            {
                RoomShape.Small => RoomShapeMask.Small,
                RoomShape.Normal => RoomShapeMask.Normal,
                RoomShape.Large => RoomShapeMask.Large,
                RoomShape.LongWide => RoomShapeMask.LongWide,
                RoomShape.LongTall => RoomShapeMask.LongTall,
                _ => RoomShapeMask.None
            };
        }

        private static RoomLayoutMask ToMask(RoomLayoutType roomLayout)
        {
            return roomLayout switch
            {
                RoomLayoutType.Open => RoomLayoutMask.Open,
                RoomLayoutType.FourPillars => RoomLayoutMask.FourPillars,
                RoomLayoutType.CenterBlock => RoomLayoutMask.CenterBlock,
                _ => RoomLayoutMask.None
            };
        }
    }

    [Serializable]
    private sealed class FloorPlacementRule : PlacementRule
    {
        [InspectorName("배치 방식")]
        public FloorPlacementMode placementMode = FloorPlacementMode.Random;
        [InspectorName("점유 반경")]
        [Min(0)] public int footprintRadius = 0;
        [InspectorName("무작위 Y 회전")]
        public bool randomYaw = true;
        [InspectorName("벽에서 떨어뜨리기")]
        public bool keepAwayFromWalls = false;
    }

    private enum FloorPlacementMode
    {
        [InspectorName("무작위")]
        Random,
        [InspectorName("중앙")]
        Center
    }

    [Serializable]
    private sealed class DecorPlacementRule : PlacementRule
    {
        [InspectorName("장식 분류")]
        public DecorCategory category = DecorCategory.SmallClutter;
        [InspectorName("배치 방식")]
        public DecorPlacementMode placementMode = DecorPlacementMode.RandomOpen;
        [InspectorName("벽 거리 제한")]
        [Min(0)] public int wallClearanceTiles = 0;
        [InspectorName("점유 반경")]
        [Min(0)] public int footprintRadius = 0;
        [InspectorName("중앙 후보 타일 수")]
        [Min(1)] public int centerCandidatePoolSize = 8;
        [InspectorName("무작위 Y 회전")]
        public bool randomYaw = true;
        [InspectorName("가까운 벽 기준 방 안쪽 바라보기")]
        public bool faceRoomFromNearestWall = false;
    }

    private enum DecorCategory
    {
        [InspectorName("작은 잡동사니")]
        SmallClutter,
        [InspectorName("큰 장애물")]
        LargeObstacle,
        [InspectorName("엄폐물")]
        Cover,
        [InspectorName("중앙 장식")]
        Centerpiece,
        [InspectorName("코너 세트")]
        CornerSet,
        [InspectorName("보상 장식")]
        Reward,
        [InspectorName("출구 장식")]
        ExitDressing
    }

    private enum DecorPlacementMode
    {
        [InspectorName("빈 공간 무작위")]
        RandomOpen,
        [InspectorName("벽에서 떨어진 곳")]
        AwayFromWalls,
        [InspectorName("벽 근처")]
        NearWall,
        [InspectorName("벽면 따라 배치")]
        AlongWall,
        [InspectorName("코너")]
        Corner,
        [InspectorName("중앙 묶음")]
        CenterCluster
    }

    [Serializable]
    private sealed class WallPlacementRule : PlacementRule
    {
        [InspectorName("배치 대상")]
        public WallPlacementTargetMask placementTargets = WallPlacementTargetMask.Rooms;
        [InspectorName("배치 방식")]
        public WallPlacementMode placementMode = WallPlacementMode.Random;
        [InspectorName("바닥 제외 반경")]
        [Min(0)] public int floorExclusionRadius = 0;
        [InspectorName("간격 타일 수")]
        [Min(1)] public int spacingTiles = 3;
        [InspectorName("가장자리 여백 타일 수")]
        [Min(0)] public int edgePaddingTiles = 1;
        [InspectorName("대상 벽 방향")]
        public WallSideMask wallSides = WallSideMask.All;
        [InspectorName("벽 표면 오프셋")]
        public float wallSurfaceOffset = 0f;
        [InspectorName("최소 벽 구간 길이")]
        [Min(1)] public int minSegmentLengthTiles = 4;
        [InspectorName("구간 배치 출입구 제외 반경")]
        [Min(0)] public int segmentedDoorwayExclusionRadius = 0;
        [InspectorName("복도 교차점 제외 반경")]
        [Min(0)] public int corridorJunctionExclusionRadius = 5;
        [InspectorName("벽 배치 제외 반경")]
        [Min(0)] public int wallPlacementExclusionRadius = 2;
        [InspectorName("복도 양쪽 배치")]
        public bool placeBothCorridorSides = false;
        [InspectorName("구간 중앙 정렬")]
        public bool centerAlignSegmentPlacements = true;

        public bool CanPlaceStartRoom()
        {
            return enabled &&
                   prefabs.Count > 0 &&
                   (roomTypes & RoomTypeMask.Start) != 0;
        }

        public bool CanPlaceCorridor()
        {
            return enabled && prefabs.Count > 0;
        }
    }

    [Flags]
    private enum RoomTypeMask
    {
        [InspectorName("없음")]
        None = 0,
        [InspectorName("일반방")]
        Normal = 1 << 0,
        [InspectorName("보스방")]
        Boss = 1 << 1,
        [InspectorName("보상방")]
        Reward = 1 << 2,
        [InspectorName("출구방")]
        Exit = 1 << 3,
        [InspectorName("시작방")]
        Start = 1 << 4,
        [InspectorName("전체")]
        All = Normal | Boss | Reward | Exit | Start
    }

    [Flags]
    private enum RoomShapeMask
    {
        [InspectorName("없음")]
        None = 0,
        [InspectorName("작은 방")]
        Small = 1 << 0,
        [InspectorName("보통 방")]
        Normal = 1 << 1,
        [InspectorName("큰 방")]
        Large = 1 << 2,
        [InspectorName("가로로 긴 방")]
        LongWide = 1 << 3,
        [InspectorName("세로로 긴 방")]
        LongTall = 1 << 4,
        [InspectorName("전체")]
        All = Small | Normal | Large | LongWide | LongTall
    }

    [Flags]
    private enum RoomLayoutMask
    {
        [InspectorName("없음")]
        None = 0,
        [InspectorName("개방형")]
        Open = 1 << 0,
        [InspectorName("네 기둥")]
        FourPillars = 1 << 1,
        [InspectorName("중앙 블록")]
        CenterBlock = 1 << 2,
        [InspectorName("전체")]
        All = Open | FourPillars | CenterBlock
    }

    private enum WallPlacementMode
    {
        [InspectorName("무작위")]
        Random,
        [InspectorName("균등 간격 라인")]
        EvenlySpacedLines,
        [InspectorName("벽 구간 균등 배치")]
        SegmentedEven
    }

    [Flags]
    private enum WallPlacementTargetMask
    {
        [InspectorName("없음")]
        None = 0,
        [InspectorName("일반 방")]
        Rooms = 1 << 0,
        [InspectorName("시작 방")]
        StartRooms = 1 << 1,
        [InspectorName("복도")]
        Corridors = 1 << 2,
        [InspectorName("전체")]
        All = Rooms | StartRooms | Corridors
    }

    [Flags]
    private enum WallSideMask
    {
        [InspectorName("없음")]
        None = 0,
        [InspectorName("북쪽")]
        North = 1 << 0,
        [InspectorName("남쪽")]
        South = 1 << 1,
        [InspectorName("동쪽")]
        East = 1 << 2,
        [InspectorName("서쪽")]
        West = 1 << 3,
        [InspectorName("전체")]
        All = North | South | East | West
    }
}
