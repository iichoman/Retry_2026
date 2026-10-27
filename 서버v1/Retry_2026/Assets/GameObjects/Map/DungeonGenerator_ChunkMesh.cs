using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class DungeonGenerator_ChunkMesh : MonoBehaviour
{
    public event Action<DungeonGenerator_ChunkMesh> DungeonGenerated;

    //-------------------------------------------------------------------
    // Inspector
    //-------------------------------------------------------------------

    [Header("Map")]
    public int mapSize = 750;
    public int mapHeight = 10;

    [Header("Rooms")]
    public int baseRoomSize = 50;
    public int targetRoomCount = 20;
    public int minLeafSize = 100;
    [Range(0f, 1f)] public float specialRoomLayoutChance = 0.45f;
    public int minimumLayoutInset = 2;

    [Header("Boss Rooms")]
    [Range(0, 2)] public int bossRoomCount = 1;
    public Vector2Int bossRoomSize = new Vector2Int(90, 90);
    public int bossRoomConnectionCount = 1;
    public int bossRoomOverlapPadding = 4;

    [Header("Exit Room")]
    public bool createExitRoom = true;

    [Header("Corridors")]
    public int corridorWidth = 10;

    [Header("Start Rooms")]
    public int startRoomEdgeMargin = 10;
    public int teamCount = 16;   // 서버 DungeonGenerator.h와 일치 필수 (시드 동기화)

    [Header("Generation")]
    public int seed = 12345;
    public bool randomizeSeedOnGenerate = true;
    public bool generateOnStart = true;
    public bool centerMapAtOrigin = true;

    [Header("Chunk Mesh")]
    public Material floorMaterial;
    public Material wallMaterial;
    public List<Material> wallMaterials = new List<Material>();
    public Material ceilingMaterial;
    public bool createFloorCollider = true;
    public bool createWallCollider = true;
    public bool createCeilingCollider = true;
    public bool splitWallMeshesBySegment = false;
    public bool createWallSegmentColliders = false;
    public bool randomizeWallMaterialByTile = true;
    public int wallMaterialSeedOffset = 9137;

    [Header("Layers")]
    [SerializeField] private LayerMask dungeonRootLayer = 0;
    [SerializeField] private LayerMask floorLayer = 0;
    [SerializeField] private LayerMask wallLayer = 0;
    [SerializeField] private LayerMask ceilingLayer = 0;

    [Header("Debug")]
    public bool drawBspGizmos = false;
    public bool drawRoomGizmos = false;

    private BSPNode root;
    private readonly List<Room> rooms = new();
    private readonly List<Corridor> corridors = new();
    private List<StartRoom> startRoomCandidates = new();
    private List<StartRoom> assignedStartRooms = new();
    private Room exitRoom;
    private readonly HashSet<Vector3Int> floorTiles = new();
    private readonly HashSet<Vector3Int> wallTiles = new();
    private readonly HashSet<Vector3Int> ceilingTiles = new();
    private readonly HashSet<Vector3Int> solidTiles = new();
    private readonly List<WallSegment> wallSegments = new();
    private Vector3Int worldOffset = Vector3Int.zero;

    private const string RootName = "DungeonRoot_ChunkMesh";
    private const string FloorRootName = "FloorChunks";
    private const string WallRootName = "WallChunks";
    private const string CeilingRootName = "CeilingChunks";
    private static readonly Vector3Int[] HorizontalDirections =
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.forward,
        Vector3Int.back
    };
    private static readonly Vector3Int[] NeighborDirs =
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.up,
        Vector3Int.down,
        Vector3Int.forward,
        Vector3Int.back
    };

    //-------------------------------------------------------------------
    // Generate ENTER
    //-------------------------------------------------------------------

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateDungeon();
        }
    }

    public void GenerateDungeon()
    {
        if (randomizeSeedOnGenerate)
        {
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        var random = new System.Random(seed);
        worldOffset = centerMapAtOrigin
            ? new Vector3Int(-mapSize / 2, 0, -mapSize / 2)
            : Vector3Int.zero;

        var startRoomManager = new StartRoomManager();
        startRoomCandidates = startRoomManager.BuildStartRoomCandidates(
            mapSize,
            baseRoomSize,
            startRoomEdgeMargin,
            mapHeight
        );
        assignedStartRooms = startRoomManager.AssignTeams(startRoomCandidates, teamCount, seed);
        ApplyWorldOffsetToStartRooms(assignedStartRooms);

        int inset = startRoomEdgeMargin + baseRoomSize;
        int innerSize = Mathf.Max(1, mapSize - inset * 2);
        var innerBounds = new BoundsInt(
            new Vector3Int(inset, 0, inset),
            new Vector3Int(innerSize, mapHeight, innerSize)
        );

        root = new BSPNode(innerBounds);
        SplitToTarget(root, targetRoomCount, minLeafSize, random);

        BuildRooms(root, random);
        BuildBossRooms(random);
        AssignExitRoom(random);

        floorTiles.Clear();
        wallTiles.Clear();
        ceilingTiles.Clear();
        solidTiles.Clear();
        wallSegments.Clear();

        BuildCorridors(root, random);
        ConnectBossRoomsToDungeon(random);
        ConnectStartRoomsToDungeon(random);
        AddRoomTiles();
        AddCorridorTiles();
        AddStartRoomTiles();
        BuildWallsAndCeiling();
        BuildWallSegments();

        BuildChunkMeshes();
        DungeonGenerated?.Invoke(this);
    }

    //-------------------------------------------------------------------
    // Start Room
    //-------------------------------------------------------------------

    public IReadOnlyList<StartRoom> GetAssignedStartRooms()
    {
        return assignedStartRooms;
    }

    public bool TryGetAssignedStartRoom(int teamId, out StartRoom startRoom)
    {
        foreach (StartRoom room in assignedStartRooms)
        {
            if (room.teamId != teamId)
            {
                continue;
            }

            startRoom = room;
            return true;
        }

        startRoom = null;
        return false;
    }

    public bool TryGetTeamSpawnPosition(int teamId, int memberIndex, out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        if (TryGetAssignedStartRoom(teamId, out StartRoom startRoom))
        {
            spawnPosition = startRoom.GetSpawnPositionForMember(memberIndex);
            spawnRotation = startRoom.spawnRotation;
            return true;
        }

        spawnPosition = Vector3.zero;
        spawnRotation = Quaternion.identity;
        return false;
    }

    public IReadOnlyList<StartRoom> GetAllStartRooms()
    {
        return startRoomCandidates;
    }

    //-------------------------------------------------------------------
    // BSP Split
    //-------------------------------------------------------------------

    private void SplitToTarget(BSPNode node, int targetCount, int minSize, System.Random random)
    {
        var leaves = new List<BSPNode> { node };
        var splittable = new List<BSPNode> { node };
        int safety = 0;

        while (leaves.Count < targetCount && splittable.Count > 0 && safety < 1000)
        {
            safety++;
            int index = random.Next(0, splittable.Count);
            BSPNode leaf = splittable[index];
            if (leaf.TrySplit(random, minSize))
            {
                leaves.Remove(leaf);
                leaves.Add(leaf.left);
                leaves.Add(leaf.right);
                splittable.RemoveAt(index);
                splittable.Add(leaf.left);
                splittable.Add(leaf.right);
            }
            else
            {
                splittable.RemoveAt(index);
            }
        }
    }

    //-------------------------------------------------------------------
    // Room Generation
    //-------------------------------------------------------------------

    private void BuildRooms(BSPNode node, System.Random random)
    {
        rooms.Clear();
        corridors.Clear();
        var leaves = new List<BSPNode>();
        node.CollectLeaves(leaves);

        int roomId = 0;
        foreach (BSPNode leaf in leaves)
        {
            RoomShape shape = PickRoomShape(random);
            BoundsInt roomBounds = CreateRoomBounds(leaf.bounds, shape, random);

            var room = new Room
            {
                id = roomId++,
                type = RoomType.Normal,
                shape = shape,
                bounds = roomBounds
            };
            room.layoutType = PickRoomLayout(roomBounds, random);
            GenerateRoomLayout(room, random);

            leaf.roomBounds = roomBounds;
            leaf.hasRoom = true;
            leaf.roomData = room;
            rooms.Add(room);
        }
    }

    //-------------------------------------------------------------------
    // Boss Room Generation
    //-------------------------------------------------------------------

    private void BuildBossRooms(System.Random random)
    {
        if (bossRoomCount <= 0)
        {
            return;
        }

        int clampedCount = Mathf.Clamp(bossRoomCount, 0, 2);
        List<BoundsInt> bossBounds = CreateBossRoomBounds(clampedCount);

        foreach (BoundsInt bounds in bossBounds)
        {
            RemoveRoomsOverlapping(bounds, bossRoomOverlapPadding);

            var room = new Room
            {
                id = GetNextRoomId(),
                type = RoomType.Boss,
                shape = RoomShape.Large,
                layoutType = RoomLayoutType.Open,
                bounds = bounds
            };

            GenerateRoomLayout(room, random);
            rooms.Add(room);
        }
    }

    private void AssignExitRoom(System.Random random)
    {
        exitRoom = null;

        if (!createExitRoom || rooms.Count == 0)
        {
            return;
        }

        var candidates = new List<Room>();
        foreach (Room room in rooms)
        {
            if (room.type != RoomType.Normal)
            {
                continue;
            }

            candidates.Add(room);
        }

        if (candidates.Count == 0)
        {
            return;
        }

        exitRoom = candidates[random.Next(0, candidates.Count)];
        exitRoom.type = RoomType.Exit;
    }

    private List<BoundsInt> CreateBossRoomBounds(int count)
    {
        var result = new List<BoundsInt>();
        int width = Mathf.Max(6, bossRoomSize.x);
        int depth = Mathf.Max(6, bossRoomSize.y);
        int centerX = mapSize / 2;
        int centerZ = mapSize / 2;

        if (count == 1)
        {
            result.Add(CreateCenteredBounds(centerX, centerZ, width, depth));
            return result;
        }

        int spacing = width + Mathf.Max(corridorWidth * 2, 8);
        int leftCenterX = centerX - spacing / 2;
        int rightCenterX = centerX + spacing / 2;
        result.Add(CreateCenteredBounds(leftCenterX, centerZ, width, depth));
        result.Add(CreateCenteredBounds(rightCenterX, centerZ, width, depth));
        return result;
    }

    private BoundsInt CreateCenteredBounds(int centerX, int centerZ, int width, int depth)
    {
        int startRoomInset = startRoomEdgeMargin + baseRoomSize;
        int minX = startRoomInset;
        int minZ = startRoomInset;
        int maxX = Mathf.Max(minX, mapSize - startRoomInset - width);
        int maxZ = Mathf.Max(minZ, mapSize - startRoomInset - depth);
        int startX = Mathf.Clamp(centerX - width / 2, minX, maxX);
        int startZ = Mathf.Clamp(centerZ - depth / 2, minZ, maxZ);

        return new BoundsInt(
            new Vector3Int(startX, 0, startZ),
            new Vector3Int(width, mapHeight, depth)
        );
    }

    private int GetNextRoomId()
    {
        int nextId = 0;
        foreach (Room room in rooms)
        {
            nextId = Mathf.Max(nextId, room.id + 1);
        }

        return nextId;
    }

    private void RemoveRoomsOverlapping(BoundsInt bounds, int padding)
    {
        BoundsInt paddedBounds = ExpandBoundsXZ(bounds, Mathf.Max(0, padding));

        for (int i = rooms.Count - 1; i >= 0; i--)
        {
            if (rooms[i].type == RoomType.Boss)
            {
                continue;
            }

            if (!OverlapsXZ(rooms[i].bounds, paddedBounds))
            {
                continue;
            }

            ClearRoomFromBsp(root, rooms[i]);
            rooms.RemoveAt(i);
        }
    }

    private void ClearRoomFromBsp(BSPNode node, Room room)
    {
        if (node == null)
        {
            return;
        }

        if (node.roomData == room)
        {
            node.roomData = null;
            node.hasRoom = false;
        }

        ClearRoomFromBsp(node.left, room);
        ClearRoomFromBsp(node.right, room);
    }

    private BoundsInt ExpandBoundsXZ(BoundsInt bounds, int padding)
    {
        return new BoundsInt(
            new Vector3Int(bounds.xMin - padding, bounds.yMin, bounds.zMin - padding),
            new Vector3Int(bounds.size.x + padding * 2, bounds.size.y, bounds.size.z + padding * 2)
        );
    }

    private bool OverlapsXZ(BoundsInt a, BoundsInt b)
    {
        return a.xMin < b.xMax &&
               a.xMax > b.xMin &&
               a.zMin < b.zMax &&
               a.zMax > b.zMin;
    }

    //-------------------------------------------------------------------
    // Corridor Connection
    //-------------------------------------------------------------------

    private void BuildCorridors(BSPNode node, System.Random random)
    {
        if (node == null || node.left == null || node.right == null)
        {
            return;
        }

        Room leftRoom = node.left.GetRoomData();
        Room rightRoom = node.right.GetRoomData();
        if (leftRoom != null && rightRoom != null)
        {
            Vector3Int leftCenter = GetRoomConnectionPoint(leftRoom, Vector3Int.FloorToInt(rightRoom.bounds.center));
            Vector3Int rightCenter = GetRoomConnectionPoint(rightRoom, Vector3Int.FloorToInt(leftRoom.bounds.center));
            Corridor corridor = CreateCorridor(leftCenter, rightCenter, random);
            leftRoom.doorwayFloorTiles.Add(leftCenter);
            rightRoom.doorwayFloorTiles.Add(rightCenter);
            corridor.connectedRoomIds.Add(leftRoom.id);
            corridor.connectedRoomIds.Add(rightRoom.id);
            corridors.Add(corridor);
            AddNeighbor(leftRoom, rightRoom);
        }

        BuildCorridors(node.left, random);
        BuildCorridors(node.right, random);
    }

    private void AddNeighbor(Room a, Room b)
    {
        if (!a.neighbors.Contains(b.id))
        {
            a.neighbors.Add(b.id);
        }

        if (!b.neighbors.Contains(a.id))
        {
            b.neighbors.Add(a.id);
        }
    }

    private Corridor CreateCorridor(Vector3Int start, Vector3Int end, System.Random random)
    {
        var corridor = new Corridor
        {
            id = corridors.Count
        };

        bool xFirst = random.NextDouble() > 0.5;
        if (xFirst)
        {
            AddHorizontalCorridor(corridor.floorTiles, start.x, end.x, start.z);
            AddVerticalCorridor(corridor.floorTiles, start.z, end.z, end.x);
        }
        else
        {
            AddVerticalCorridor(corridor.floorTiles, start.z, end.z, start.x);
            AddHorizontalCorridor(corridor.floorTiles, start.x, end.x, end.z);
        }

        return corridor;
    }

    private void ConnectStartRoomsToDungeon(System.Random random)
    {
        if (rooms.Count == 0 || assignedStartRooms.Count == 0)
        {
            return;
        }

        foreach (StartRoom startRoom in assignedStartRooms)
        {
            Room nearest = FindNearestRoom(startRoom.bounds.center);
            if (nearest == null)
            {
                continue;
            }

            Vector3Int startCenter = Vector3Int.FloorToInt(startRoom.bounds.center);
            Vector3Int roomCenter = GetRoomConnectionPoint(nearest, startCenter);
            Corridor corridor = CreateCorridor(startCenter, roomCenter, random);
            nearest.doorwayFloorTiles.Add(roomCenter);
            corridor.connectedRoomIds.Add(nearest.id);
            corridors.Add(corridor);
        }
    }

    private void ConnectBossRoomsToDungeon(System.Random random)
    {
        int connectionCount = Mathf.Max(1, bossRoomConnectionCount);

        foreach (Room bossRoom in rooms)
        {
            if (bossRoom.type != RoomType.Boss)
            {
                continue;
            }

            List<Room> nearestRooms = FindNearestRooms(
                bossRoom.bounds.center,
                connectionCount,
                bossRoom
            );

            foreach (Room targetRoom in nearestRooms)
            {
                Vector3Int bossConnection = GetRoomConnectionPoint(
                    bossRoom,
                    Vector3Int.FloorToInt(targetRoom.bounds.center)
                );
                Vector3Int targetConnection = GetRoomConnectionPoint(
                    targetRoom,
                    Vector3Int.FloorToInt(bossRoom.bounds.center)
                );

                Corridor corridor = CreateCorridor(bossConnection, targetConnection, random);
                bossRoom.doorwayFloorTiles.Add(bossConnection);
                targetRoom.doorwayFloorTiles.Add(targetConnection);
                corridor.connectedRoomIds.Add(bossRoom.id);
                corridor.connectedRoomIds.Add(targetRoom.id);
                corridors.Add(corridor);
                AddNeighbor(bossRoom, targetRoom);
            }
        }
    }

    private Room FindNearestRoom(Vector3 startCenter)
    {
        Room nearest = null;
        float bestDistance = float.MaxValue;

        foreach (Room room in rooms)
        {
            float distance = Vector3.Distance(startCenter, room.bounds.center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = room;
            }
        }

        return nearest;
    }

    private List<Room> FindNearestRooms(Vector3 startCenter, int count, Room excludedRoom)
    {
        var candidates = new List<Room>();
        foreach (Room room in rooms)
        {
            if (room == excludedRoom)
            {
                continue;
            }

            candidates.Add(room);
        }

        candidates.Sort((a, b) =>
        {
            float distanceA = Vector3.SqrMagnitude(a.bounds.center - startCenter);
            float distanceB = Vector3.SqrMagnitude(b.bounds.center - startCenter);
            return distanceA.CompareTo(distanceB);
        });

        if (candidates.Count > count)
        {
            candidates.RemoveRange(count, candidates.Count - count);
        }

        return candidates;
    }

    private void AddHorizontalCorridor(HashSet<Vector3Int> destination, int x1, int x2, int z)
    {
        int min = Mathf.Min(x1, x2);
        int max = Mathf.Max(x1, x2);
        int half = Mathf.Max(0, corridorWidth / 2);

        for (int x = min; x <= max; x++)
        {
            for (int offset = -half; offset <= half; offset++)
            {
                destination.Add(new Vector3Int(x, 0, z + offset));
            }
        }
    }

    private void AddVerticalCorridor(HashSet<Vector3Int> destination, int z1, int z2, int x)
    {
        int min = Mathf.Min(z1, z2);
        int max = Mathf.Max(z1, z2);
        int half = Mathf.Max(0, corridorWidth / 2);

        for (int z = min; z <= max; z++)
        {
            for (int offset = -half; offset <= half; offset++)
            {
                destination.Add(new Vector3Int(x + offset, 0, z));
            }
        }
    }

    //-------------------------------------------------------------------
    // Tile Collection
    //-------------------------------------------------------------------

    private void AddRoomTiles()
    {
        foreach (Room room in rooms)
        {
            AddRoomFloorTiles(room);
        }
    }

    private void AddStartRoomTiles()
    {
        foreach (StartRoom startRoom in assignedStartRooms)
        {
            AddBoundsTiles(startRoom.bounds);
        }
    }

    private void AddCorridorTiles()
    {
        foreach (Corridor corridor in corridors)
        {
            foreach (Vector3Int tile in corridor.floorTiles)
            {
                floorTiles.Add(tile);
            }
        }
    }

    private void AddBoundsTiles(BoundsInt bounds)
    {
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int z = bounds.zMin; z < bounds.zMax; z++)
            {
                floorTiles.Add(new Vector3Int(x, 0, z));
            }
        }
    }

    private void AddRoomFloorTiles(Room room)
    {
        foreach (Vector3Int tile in room.floorTiles)
        {
            floorTiles.Add(tile);
        }
    }

    //-------------------------------------------------------------------
    // Room Layout Rules
    //-------------------------------------------------------------------

    private RoomLayoutType PickRoomLayout(BoundsInt roomBounds, System.Random random)
    {
        Vector2Int interiorSize = GetInteriorSize(roomBounds);
        bool canPlacePillars = interiorSize.x >= 8 && interiorSize.y >= 8;
        bool canPlaceCenterBlock = interiorSize.x >= 12 && interiorSize.y >= 12;

        var candidates = new List<RoomLayoutType> { RoomLayoutType.Open };
        if (canPlacePillars)
        {
            candidates.Add(RoomLayoutType.FourPillars);
        }

        if (canPlaceCenterBlock)
        {
            candidates.Add(RoomLayoutType.CenterBlock);
        }

        if (candidates.Count == 1 || random.NextDouble() > specialRoomLayoutChance)
        {
            return RoomLayoutType.Open;
        }

        return candidates[random.Next(1, candidates.Count)];
    }

    private void GenerateRoomLayout(Room room, System.Random random)
    {
        room.floorTiles.Clear();
        room.blockedTiles.Clear();
        FillRoomInterior(room.floorTiles, room.bounds);

        switch (room.layoutType)
        {
            case RoomLayoutType.FourPillars:
                ApplyFourPillarsLayout(room, random);
                break;
            case RoomLayoutType.CenterBlock:
                ApplyCenterBlockLayout(room, random);
                break;
        }
    }

    private void FillRoomInterior(HashSet<Vector3Int> destination, BoundsInt bounds)
    {
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int z = bounds.zMin; z < bounds.zMax; z++)
            {
                destination.Add(new Vector3Int(x, 0, z));
            }
        }
    }

    private void ApplyFourPillarsLayout(Room room, System.Random random)
    {
        Vector2Int interiorSize = GetInteriorSize(room.bounds);
        Vector2Int pillarSize = CalculateFourPillarSize(interiorSize);
        Vector2Int xCenters = CalculateQuarterCenters(interiorSize.x);
        Vector2Int zCenters = CalculateQuarterCenters(interiorSize.y);

        var pillarOrigins = new List<Vector3Int>
        {
            CreateInteriorOriginFromCenter(room.bounds, xCenters.x, zCenters.x, pillarSize),
            CreateInteriorOriginFromCenter(room.bounds, xCenters.y, zCenters.x, pillarSize),
            CreateInteriorOriginFromCenter(room.bounds, xCenters.x, zCenters.y, pillarSize),
            CreateInteriorOriginFromCenter(room.bounds, xCenters.y, zCenters.y, pillarSize)
        };

        foreach (Vector3Int origin in pillarOrigins)
        {
            BlockArea(room, new BoundsInt(origin, new Vector3Int(pillarSize.x, mapHeight, pillarSize.y)));
        }
    }

    private void ApplyCenterBlockLayout(Room room, System.Random random)
    {
        Vector2Int interiorSize = GetInteriorSize(room.bounds);
        int maxWidth = interiorSize.x - minimumLayoutInset * 2;
        int maxDepth = interiorSize.y - minimumLayoutInset * 2;
        int blockWidth = Mathf.Clamp(NextInclusive(random, interiorSize.x / 3, interiorSize.x / 2), 4, maxWidth);
        int blockDepth = Mathf.Clamp(NextInclusive(random, interiorSize.y / 3, interiorSize.y / 2), 4, maxDepth);
        int startX = room.bounds.xMin + (room.bounds.size.x - blockWidth) / 2;
        int startZ = room.bounds.zMin + (room.bounds.size.z - blockDepth) / 2;

        BlockArea(room, new BoundsInt(
            new Vector3Int(startX, 0, startZ),
            new Vector3Int(blockWidth, mapHeight, blockDepth))
        );
    }

    private void BlockArea(Room room, BoundsInt area)
    {
        for (int x = area.xMin; x < area.xMax; x++)
        {
            for (int z = area.zMin; z < area.zMax; z++)
            {
                var tile = new Vector3Int(x, 0, z);
                room.floorTiles.Remove(tile);
                room.blockedTiles.Add(tile);
            }
        }
    }

    private Vector3Int GetRoomConnectionPoint(Room room, Vector3Int target)
    {
        if (room.floorTiles.Count == 0)
        {
            return Vector3Int.FloorToInt(room.bounds.center);
        }

        Vector3Int bestTile = Vector3Int.FloorToInt(room.bounds.center);
        float bestDistance = float.MaxValue;

        foreach (Vector3Int tile in room.floorTiles)
        {
            float distance = (tile - target).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTile = tile;
            }
        }

        return bestTile;
    }

    private Vector2Int GetInteriorSize(BoundsInt bounds)
    {
        return new Vector2Int(bounds.size.x, bounds.size.z);
    }

    private Vector3Int CreateInteriorOrigin(BoundsInt bounds, int localX, int localZ, Vector2Int size)
    {
        int clampedX = Mathf.Clamp(localX, minimumLayoutInset, Mathf.Max(minimumLayoutInset, bounds.size.x - minimumLayoutInset - size.x));
        int clampedZ = Mathf.Clamp(localZ, minimumLayoutInset, Mathf.Max(minimumLayoutInset, bounds.size.z - minimumLayoutInset - size.y));
        return new Vector3Int(bounds.xMin + clampedX, 0, bounds.zMin + clampedZ);
    }

    private Vector3Int CreateInteriorOriginFromCenter(BoundsInt bounds, int centerX, int centerZ, Vector2Int size)
    {
        int startX = centerX - size.x / 2;
        int startZ = centerZ - size.y / 2;
        return CreateInteriorOrigin(bounds, startX, startZ, size);
    }

    private Vector2Int CalculateFourPillarSize(Vector2Int interiorSize)
    {
        int desiredWidth = Mathf.Max(2, interiorSize.x / 4);
        int desiredDepth = Mathf.Max(2, interiorSize.y / 4);
        int maxAllowedWidth = Mathf.Max(2, (interiorSize.x - minimumLayoutInset * 2) / 2);
        int maxAllowedDepth = Mathf.Max(2, (interiorSize.y - minimumLayoutInset * 2) / 2);

        return new Vector2Int(
            Mathf.Clamp(desiredWidth, 2, maxAllowedWidth),
            Mathf.Clamp(desiredDepth, 2, maxAllowedDepth)
        );
    }

    private Vector2Int CalculateQuarterCenters(int axisSize)
    {
        int firstCenter = Mathf.RoundToInt(axisSize * 0.25f);
        int secondCenter = Mathf.RoundToInt(axisSize * 0.75f);
        int minCenter = minimumLayoutInset;
        int maxCenter = Mathf.Max(minCenter, axisSize - minimumLayoutInset);

        return new Vector2Int(
            Mathf.Clamp(firstCenter, minCenter, maxCenter),
            Mathf.Clamp(secondCenter, minCenter, maxCenter)
        );
    }

    private static int NextInclusive(System.Random random, int minInclusive, int maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        return random.Next(minInclusive, maxInclusive + 1);
    }

    //-------------------------------------------------------------------
    // Wall And Ceiling Generation
    //-------------------------------------------------------------------

    private void BuildWallsAndCeiling()
    {
        int ceilingY = Mathf.Max(1, mapHeight);

        foreach (Vector3Int floorTile in floorTiles)
        {
            ceilingTiles.Add(new Vector3Int(floorTile.x, ceilingY, floorTile.z));

            foreach (Vector3Int direction in HorizontalDirections)
            {
                Vector3Int neighborFloor = floorTile + direction;
                if (floorTiles.Contains(neighborFloor))
                {
                    continue;
                }

                for (int y = 0; y < ceilingY + 1; y++)
                {
                    wallTiles.Add(new Vector3Int(neighborFloor.x, y, neighborFloor.z));
                }
            }
        }

        foreach (Room room in rooms)
        {
            foreach (Vector3Int blockedTile in room.blockedTiles)
            {
                for (int y = 0; y < ceilingY + 1; y++)
                {
                    wallTiles.Add(new Vector3Int(blockedTile.x, y, blockedTile.z));
                }
            }
        }

        foreach (Vector3Int tile in floorTiles)
        {
            solidTiles.Add(tile);
        }

        foreach (Vector3Int tile in wallTiles)
        {
            solidTiles.Add(tile);
        }

        foreach (Vector3Int tile in ceilingTiles)
        {
            solidTiles.Add(tile);
        }
    }

    private void BuildWallSegments()
    {
        wallSegments.Clear();

        foreach (Room room in rooms)
        {
            AddBoundsWallSegments(
                WallSegmentOwnerType.Room,
                room.id,
                room.bounds,
                room.floorTiles
            );
        }

        foreach (StartRoom startRoom in assignedStartRooms)
        {
            AddBoundsWallSegments(
                WallSegmentOwnerType.StartRoom,
                startRoom.slotIndex,
                startRoom.bounds,
                BuildBoundsFloorSet(startRoom.bounds)
            );
        }

        foreach (Corridor corridor in corridors)
        {
            AddCorridorWallSegments(corridor);
        }
    }

    private void AddBoundsWallSegments(
        WallSegmentOwnerType ownerType,
        int ownerId,
        BoundsInt bounds,
        HashSet<Vector3Int> ownerFloorTiles
    )
    {
        var candidates = new List<WallSegmentCandidate>();

        candidates.Clear();
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            AddWallSegmentCandidate(
                candidates,
                ownerFloorTiles,
                new Vector3Int(x, bounds.yMin, bounds.zMax - 1),
                new Vector3Int(x, bounds.yMin, bounds.zMax)
            );
        }
        AddOrderedSegments(ownerType, ownerId, WallSide.North, Vector3.back, candidates, true);

        candidates.Clear();
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            AddWallSegmentCandidate(
                candidates,
                ownerFloorTiles,
                new Vector3Int(x, bounds.yMin, bounds.zMin),
                new Vector3Int(x, bounds.yMin, bounds.zMin - 1)
            );
        }
        AddOrderedSegments(ownerType, ownerId, WallSide.South, Vector3.forward, candidates, true);

        candidates.Clear();
        for (int z = bounds.zMin; z < bounds.zMax; z++)
        {
            AddWallSegmentCandidate(
                candidates,
                ownerFloorTiles,
                new Vector3Int(bounds.xMax - 1, bounds.yMin, z),
                new Vector3Int(bounds.xMax, bounds.yMin, z)
            );
        }
        AddOrderedSegments(ownerType, ownerId, WallSide.East, Vector3.left, candidates, false);

        candidates.Clear();
        for (int z = bounds.zMin; z < bounds.zMax; z++)
        {
            AddWallSegmentCandidate(
                candidates,
                ownerFloorTiles,
                new Vector3Int(bounds.xMin, bounds.yMin, z),
                new Vector3Int(bounds.xMin - 1, bounds.yMin, z)
            );
        }
        AddOrderedSegments(ownerType, ownerId, WallSide.West, Vector3.right, candidates, false);
    }

    private void AddCorridorWallSegments(Corridor corridor)
    {
        var groups = new Dictionary<WallSegmentLineKey, List<WallSegmentCandidate>>();

        foreach (Vector3Int floorTile in corridor.floorTiles)
        {
            foreach (Vector3Int wallDirection in HorizontalDirections)
            {
                Vector3Int wallTile = floorTile + wallDirection;
                if (!IsWallTileAtBase(wallTile))
                {
                    continue;
                }

                WallSide side = ToWallSide(wallDirection);
                int lineCoordinate = wallDirection.x != 0 ? wallTile.x : wallTile.z;
                var key = new WallSegmentLineKey(side, lineCoordinate);

                if (!groups.TryGetValue(key, out List<WallSegmentCandidate> candidates))
                {
                    candidates = new List<WallSegmentCandidate>();
                    groups.Add(key, candidates);
                }

                candidates.Add(new WallSegmentCandidate(floorTile, wallTile));
            }
        }

        foreach (KeyValuePair<WallSegmentLineKey, List<WallSegmentCandidate>> pair in groups)
        {
            bool sortByX = pair.Key.side == WallSide.North || pair.Key.side == WallSide.South;
            Vector3 roomFacingDirection = ToRoomFacingDirection(pair.Key.side);
            AddOrderedSegments(
                WallSegmentOwnerType.Corridor,
                corridor.id,
                pair.Key.side,
                roomFacingDirection,
                pair.Value,
                sortByX
            );
        }
    }

    private void AddWallSegmentCandidate(
        List<WallSegmentCandidate> candidates,
        HashSet<Vector3Int> ownerFloorTiles,
        Vector3Int floorTile,
        Vector3Int wallTile
    )
    {
        if (!ownerFloorTiles.Contains(floorTile) || !IsWallTileAtBase(wallTile))
        {
            return;
        }

        candidates.Add(new WallSegmentCandidate(floorTile, wallTile));
    }

    private void AddOrderedSegments(
        WallSegmentOwnerType ownerType,
        int ownerId,
        WallSide side,
        Vector3 roomFacingDirection,
        List<WallSegmentCandidate> candidates,
        bool sortByX
    )
    {
        if (candidates.Count == 0)
        {
            return;
        }

        candidates.Sort((a, b) =>
        {
            int aValue = sortByX ? a.wallTile.x : a.wallTile.z;
            int bValue = sortByX ? b.wallTile.x : b.wallTile.z;
            return aValue.CompareTo(bValue);
        });

        var wallRun = new List<Vector3Int>();
        var floorRun = new List<Vector3Int>();
        int previousCoordinate = int.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            WallSegmentCandidate candidate = candidates[i];
            int coordinate = sortByX ? candidate.wallTile.x : candidate.wallTile.z;
            bool startsNewRun = wallRun.Count > 0 && coordinate != previousCoordinate + 1;

            if (startsNewRun)
            {
                AddWallSegment(ownerType, ownerId, side, roomFacingDirection, wallRun, floorRun);
                wallRun.Clear();
                floorRun.Clear();
            }

            wallRun.Add(candidate.wallTile);
            floorRun.Add(candidate.floorTile);
            previousCoordinate = coordinate;
        }

        if (wallRun.Count > 0)
        {
            AddWallSegment(ownerType, ownerId, side, roomFacingDirection, wallRun, floorRun);
        }
    }

    private void AddWallSegment(
        WallSegmentOwnerType ownerType,
        int ownerId,
        WallSide side,
        Vector3 roomFacingDirection,
        List<Vector3Int> wallRun,
        List<Vector3Int> floorRun
    )
    {
        wallSegments.Add(new WallSegment(
            wallSegments.Count,
            ownerType,
            ownerId,
            side,
            roomFacingDirection,
            wallRun,
            floorRun
        ));
    }

    private HashSet<Vector3Int> BuildBoundsFloorSet(BoundsInt bounds)
    {
        var tiles = new HashSet<Vector3Int>();
        foreach (Vector3Int tile in EnumerateBoundsTiles(bounds))
        {
            tiles.Add(tile);
        }

        return tiles;
    }

    private bool IsWallTileAtBase(Vector3Int tile)
    {
        return wallTiles.Contains(new Vector3Int(tile.x, 0, tile.z));
    }

    private static WallSide ToWallSide(Vector3Int wallDirection)
    {
        if (wallDirection == Vector3Int.forward)
        {
            return WallSide.North;
        }

        if (wallDirection == Vector3Int.back)
        {
            return WallSide.South;
        }

        if (wallDirection == Vector3Int.right)
        {
            return WallSide.East;
        }

        return WallSide.West;
    }

    private static Vector3 ToRoomFacingDirection(WallSide side)
    {
        return side switch
        {
            WallSide.North => Vector3.back,
            WallSide.South => Vector3.forward,
            WallSide.East => Vector3.left,
            WallSide.West => Vector3.right,
            _ => Vector3.zero
        };
    }

    //-------------------------------------------------------------------
    // Chunk Mesh Build *******
    //-------------------------------------------------------------------

    private void BuildChunkMeshes()
    {
        Transform rootTransform = CreateOrClearRoot();
        SetLayerIfValid(rootTransform.gameObject, dungeonRootLayer);

        Transform floorRoot = GetOrCreateChild(rootTransform, FloorRootName);
        Transform wallRoot = GetOrCreateChild(rootTransform, WallRootName);
        Transform ceilingRoot = GetOrCreateChild(rootTransform, CeilingRootName);
        SetLayerIfValid(floorRoot.gameObject, floorLayer);
        SetLayerIfValid(wallRoot.gameObject, wallLayer);
        SetLayerIfValid(ceilingRoot.gameObject, ceilingLayer);
        Dictionary<string, MeshSectionData> sections = BuildMeshSections();

        CreateSectionObjects(sections, floorRoot, floorMaterial, createFloorCollider, SectionSurfaceType.Floor, floorLayer);
        CreateSectionObjects(sections, wallRoot, wallMaterial, createWallCollider, SectionSurfaceType.Wall, wallLayer);
        CreateSectionObjects(sections, ceilingRoot, ceilingMaterial, createCeilingCollider, SectionSurfaceType.Ceiling, ceilingLayer);
    }

    private Dictionary<string, MeshSectionData> BuildMeshSections()
    {
        var sections = new Dictionary<string, MeshSectionData>();

        foreach (Room room in rooms)
        {
            var section = new MeshSectionData($"Room_{room.id}", MeshSectionKind.Room, WallSegmentOwnerType.Room, room.id);
            AddSectionFloorTiles(section, room.floorTiles);
            AddRoomBlockedWalls(section, room);
            PopulateSectionBoundarySurfaces(section);
            sections[section.name] = section;
        }

        foreach (StartRoom startRoom in assignedStartRooms)
        {
            var section = new MeshSectionData($"StartRoom_{startRoom.slotIndex}", MeshSectionKind.StartRoom, WallSegmentOwnerType.StartRoom, startRoom.slotIndex);
            AddSectionFloorTiles(section, EnumerateBoundsTiles(startRoom.bounds));
            PopulateSectionBoundarySurfaces(section);
            sections[section.name] = section;
        }

        foreach (Corridor corridor in corridors)
        {
            var section = new MeshSectionData($"Corridor_{corridor.id}", MeshSectionKind.Corridor, WallSegmentOwnerType.Corridor, corridor.id);
            AddSectionFloorTiles(section, corridor.floorTiles);
            PopulateSectionBoundarySurfaces(section);
            sections[section.name] = section;
        }

        return sections;
    }

    private void AddSectionFloorTiles(
        MeshSectionData section,
        IEnumerable<Vector3Int> sourceTiles
    )
    {
        foreach (Vector3Int tile in sourceTiles)
        {
            section.floorTiles.Add(tile);
        }
    }

    private void AddRoomBlockedWalls(MeshSectionData section, Room room)
    {
        int ceilingY = Mathf.Max(1, mapHeight);

        foreach (Vector3Int blockedTile in room.blockedTiles)
        {
            for (int y = 0; y < ceilingY + 1; y++)
            {
                section.wallTiles.Add(new Vector3Int(blockedTile.x, y, blockedTile.z));
            }
        }
    }

    private void PopulateSectionBoundarySurfaces(MeshSectionData section)
    {
        int ceilingY = Mathf.Max(1, mapHeight);

        foreach (Vector3Int floorTile in section.floorTiles)
        {
            section.ceilingTiles.Add(new Vector3Int(floorTile.x, ceilingY, floorTile.z));

            foreach (Vector3Int direction in HorizontalDirections)
            {
                Vector3Int neighborFloor = floorTile + direction;
                if (floorTiles.Contains(neighborFloor))
                {
                    continue;
                }

                for (int y = 0; y < ceilingY + 1; y++)
                {
                    section.wallTiles.Add(new Vector3Int(neighborFloor.x, y, neighborFloor.z));
                }
            }
        }
    }

    private IEnumerable<Vector3Int> EnumerateBoundsTiles(BoundsInt bounds)
    {
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int z = bounds.zMin; z < bounds.zMax; z++)
            {
                yield return new Vector3Int(x, 0, z);
            }
        }
    }

    private void CreateSectionObjects(
        Dictionary<string, MeshSectionData> sections,
        Transform parent,
        Material material,
        bool addCollider,
        SectionSurfaceType surfaceType,
        LayerMask layerMask
    )
    {
        foreach (MeshSectionData section in sections.Values)
        {
            if (surfaceType == SectionSurfaceType.Wall && splitWallMeshesBySegment)
            {
                CreateSplitWallSectionObject(section, parent, material, addCollider, layerMask);
                continue;
            }

            List<Vector3Int> tiles = GetSectionSurfaceTiles(section, surfaceType);
            Material[] resolvedMaterials = surfaceType == SectionSurfaceType.Wall
                ? ResolveWallMaterials(material)
                : ResolveSingleMaterial(material);
            Mesh mesh = surfaceType == SectionSurfaceType.Wall
                ? BuildWallMeshForTiles(tiles, resolvedMaterials.Length)
                : BuildMeshForTiles(tiles);
            if (mesh == null || mesh.vertexCount == 0)
            {
                continue;
            }

            GameObject sectionObject = new GameObject($"{section.name}_{surfaceType}");
            sectionObject.transform.SetParent(parent, false);
            SetLayerIfValid(sectionObject, layerMask);

            MeshFilter filter = sectionObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
            AssignRendererMaterials(renderer, resolvedMaterials);

            if (addCollider)
            {
                MeshCollider collider = sectionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = surfaceType == SectionSurfaceType.Floor
                    ? BuildTopSurfaceColliderMesh(tiles)
                    : mesh;
                // collider.sharedMesh = mesh;
            }
        }
    }

    private void CreateSplitWallSectionObject(
        MeshSectionData section,
        Transform parent,
        Material material,
        bool addCollider,
        LayerMask layerMask
    )
    {
        List<Vector3Int> sectionTiles = GetSectionSurfaceTiles(section, SectionSurfaceType.Wall);
        if (sectionTiles.Count == 0)
        {
            return;
        }

        GameObject sectionObject = new GameObject($"{section.name}_Wall");
        sectionObject.transform.SetParent(parent, false);
        SetLayerIfValid(sectionObject, layerMask);

        if (addCollider && !createWallSegmentColliders)
        {
            Mesh sectionMesh = BuildMeshForTiles(sectionTiles);
            if (sectionMesh != null && sectionMesh.vertexCount > 0)
            {
                MeshCollider collider = sectionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = sectionMesh;
            }
        }

        int createdCount = 0;
        foreach (WallSegment segment in wallSegments)
        {
            if (segment.ownerType != section.ownerType || segment.ownerId != section.ownerId)
            {
                continue;
            }

            List<Vector3Int> segmentTiles = BuildWallSegmentMeshTiles(segment);
            Material[] resolvedWallMaterials = ResolveWallMaterials(material);
            Mesh segmentMesh = BuildWallMeshForTiles(segmentTiles, resolvedWallMaterials.Length);
            if (segmentMesh == null || segmentMesh.vertexCount == 0)
            {
                continue;
            }

            GameObject segmentObject = new GameObject(CreateWallSegmentObjectName(segment));
            segmentObject.transform.SetParent(sectionObject.transform, false);
            SetLayerIfValid(segmentObject, layerMask);

            MeshFilter filter = segmentObject.AddComponent<MeshFilter>();
            filter.sharedMesh = segmentMesh;

            MeshRenderer renderer = segmentObject.AddComponent<MeshRenderer>();
            AssignRendererMaterials(renderer, resolvedWallMaterials);

            if (addCollider && createWallSegmentColliders)
            {
                MeshCollider collider = segmentObject.AddComponent<MeshCollider>();
                collider.sharedMesh = segmentMesh;
            }

            createdCount++;
        }

        List<Vector3Int> remainderTiles = GetWallTilesNotInSegments(sectionTiles, section);
        Material[] resolvedRemainderMaterials = ResolveWallMaterials(material);
        Mesh remainderMesh = BuildWallMeshForTiles(remainderTiles, resolvedRemainderMaterials.Length);
        if (remainderMesh != null && remainderMesh.vertexCount > 0)
        {
            GameObject remainderObject = new GameObject("Unsegmented_Walls");
            remainderObject.transform.SetParent(sectionObject.transform, false);
            SetLayerIfValid(remainderObject, layerMask);

            MeshFilter filter = remainderObject.AddComponent<MeshFilter>();
            filter.sharedMesh = remainderMesh;

            MeshRenderer renderer = remainderObject.AddComponent<MeshRenderer>();
            AssignRendererMaterials(renderer, resolvedRemainderMaterials);

            if (addCollider && createWallSegmentColliders)
            {
                MeshCollider collider = remainderObject.AddComponent<MeshCollider>();
                collider.sharedMesh = remainderMesh;
            }

            createdCount++;
        }

        if (createdCount == 0)
        {
            DestroyGeneratedObject(sectionObject);
        }
    }

    private List<Vector3Int> BuildWallSegmentMeshTiles(WallSegment segment)
    {
        int ceilingY = Mathf.Max(1, mapHeight);
        var tiles = new List<Vector3Int>(segment.wallTiles.Count * (ceilingY + 1));

        for (int i = 0; i < segment.wallTiles.Count; i++)
        {
            Vector3Int baseTile = segment.wallTiles[i];
            for (int y = 0; y < ceilingY + 1; y++)
            {
                tiles.Add(new Vector3Int(baseTile.x, y, baseTile.z));
            }
        }

        return tiles;
    }

    private List<Vector3Int> GetWallTilesNotInSegments(List<Vector3Int> sectionTiles, MeshSectionData section)
    {
        var segmentedBaseTiles = new HashSet<Vector3Int>();
        foreach (WallSegment segment in wallSegments)
        {
            if (segment.ownerType != section.ownerType || segment.ownerId != section.ownerId)
            {
                continue;
            }

            for (int i = 0; i < segment.wallTiles.Count; i++)
            {
                Vector3Int tile = segment.wallTiles[i];
                segmentedBaseTiles.Add(new Vector3Int(tile.x, 0, tile.z));
            }
        }

        var remainderTiles = new List<Vector3Int>();
        foreach (Vector3Int tile in sectionTiles)
        {
            if (segmentedBaseTiles.Contains(new Vector3Int(tile.x, 0, tile.z)))
            {
                continue;
            }

            remainderTiles.Add(tile);
        }

        return remainderTiles;
    }

    private static string CreateWallSegmentObjectName(WallSegment segment)
    {
        return $"{segment.side}_Segment_{segment.id}_Length_{segment.LengthTiles}";
    }

    private Material[] ResolveWallMaterials(Material fallbackMaterial)
    {
        var resolved = new List<Material>();

        if (wallMaterials != null)
        {
            for (int i = 0; i < wallMaterials.Count; i++)
            {
                if (wallMaterials[i] != null)
                {
                    resolved.Add(wallMaterials[i]);
                }
            }
        }

        if (resolved.Count == 0 && fallbackMaterial != null)
        {
            resolved.Add(fallbackMaterial);
        }

        if (resolved.Count == 0)
        {
            resolved.Add(null);
        }

        return resolved.ToArray();
    }

    private static Material[] ResolveSingleMaterial(Material material)
    {
        return material != null
            ? new[] { material }
            : Array.Empty<Material>();
    }

    private static void AssignRendererMaterials(MeshRenderer renderer, Material[] materials)
    {
        if (renderer == null || materials == null || materials.Length == 0)
        {
            return;
        }

        renderer.sharedMaterials = materials;
    }

    private static void DestroyGeneratedObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void SetLayerIfValid(GameObject target, LayerMask layerMask)
    {
        if (target == null || layerMask.value == 0)
        {
            return;
        }

        int layer = GetFirstLayerFromMask(layerMask);
        if (layer < 0)
        {
            return;
        }

        target.layer = layer;
    }

    private int GetFirstLayerFromMask(LayerMask layerMask)
    {
        int mask = layerMask.value;
        int selectedLayer = -1;

        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) == 0)
            {
                continue;
            }

            if (selectedLayer >= 0)
            {
                Debug.LogWarning(
                    "Dungeon layer masks should contain only one layer. The first selected layer will be used.",
                    this
                );
                return selectedLayer;
            }

            selectedLayer = layer;
        }

        return selectedLayer;
    }

    //-------------------------------------------------------------------
    // Tile To Mesh
    //-------------------------------------------------------------------

    private List<Vector3Int> GetSectionSurfaceTiles(MeshSectionData section, SectionSurfaceType surfaceType)
    {
        return surfaceType switch
        {
            SectionSurfaceType.Floor => new List<Vector3Int>(section.floorTiles),
            SectionSurfaceType.Wall => new List<Vector3Int>(section.wallTiles),
            SectionSurfaceType.Ceiling => new List<Vector3Int>(section.ceilingTiles),
            _ => new List<Vector3Int>()
        };
    }

    private Mesh BuildMeshForTiles(List<Vector3Int> tiles)
    {
        var vertices = new List<Vector3>(tiles.Count * 8);
        var triangles = new List<int>(tiles.Count * 12);
        var uvs = new List<Vector2>(tiles.Count * 8);

        for (int i = 0; i < tiles.Count; i++)
        {
            Vector3Int tile = tiles[i];
            Vector3 basePos = new Vector3(
                tile.x + worldOffset.x,
                tile.y + worldOffset.y,
                tile.z + worldOffset.z
            );

            for (int d = 0; d < NeighborDirs.Length; d++)
            {
                AddFace(d, basePos, vertices, triangles, uvs);
            }
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        var mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildWallMeshForTiles(List<Vector3Int> tiles, int materialCount)
    {
        int safeMaterialCount = Mathf.Max(1, materialCount);
        var vertices = new List<Vector3>(tiles.Count * 8);
        var uvs = new List<Vector2>(tiles.Count * 8);
        var subMeshTriangles = new List<int>[safeMaterialCount];

        for (int i = 0; i < subMeshTriangles.Length; i++)
        {
            subMeshTriangles[i] = new List<int>(tiles.Count * 12 / safeMaterialCount + 12);
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            Vector3Int tile = tiles[i];
            Vector3 basePos = new Vector3(
                tile.x + worldOffset.x,
                tile.y + worldOffset.y,
                tile.z + worldOffset.z
            );

            for (int d = 0; d < NeighborDirs.Length; d++)
            {
                int materialIndex = randomizeWallMaterialByTile
                    ? GetWallMaterialIndex(tile, d, safeMaterialCount)
                    : 0;

                AddFace(d, basePos, vertices, subMeshTriangles[materialIndex], uvs);
            }
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        var mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32,
            subMeshCount = safeMaterialCount
        };

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);

        for (int i = 0; i < subMeshTriangles.Length; i++)
        {
            mesh.SetTriangles(subMeshTriangles[i], i);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private int GetWallMaterialIndex(Vector3Int tile, int directionIndex, int materialCount)
    {
        if (materialCount <= 1)
        {
            return 0;
        }

        unchecked
        {
            int hash = seed + wallMaterialSeedOffset;
            hash = hash * 397 ^ tile.x;
            hash = hash * 397 ^ tile.y;
            hash = hash * 397 ^ tile.z;
            hash = hash * 397 ^ directionIndex;
            return (hash & int.MaxValue) % materialCount;
        }
    }


    private Mesh BuildTopSurfaceColliderMesh(List<Vector3Int> tiles)
    {
        var vertices = new List<Vector3>(tiles.Count * 4);
        var triangles = new List<int>(tiles.Count * 12);

        for (int i = 0; i < tiles.Count; i++)
        {
            Vector3Int tile = tiles[i];
            Vector3 basePos = new Vector3(
                tile.x + worldOffset.x,
                tile.y + worldOffset.y,
                tile.z + worldOffset.z
            );

            int start = vertices.Count;
            vertices.Add(basePos + new Vector3(0f, 1f, 0f));
            vertices.Add(basePos + new Vector3(1f, 1f, 0f));
            vertices.Add(basePos + new Vector3(1f, 1f, 1f));
            vertices.Add(basePos + new Vector3(0f, 1f, 1f));

            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 0);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        var mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }


    private void AddFace(
        int directionIndex,
        Vector3 basePos,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs
    )
    {
        Vector3[] face = GetFaceVertices(directionIndex);
        Vector2[] faceUvs = GetFaceUvs(directionIndex);

        int start = vertices.Count;
        vertices.Add(basePos + face[0]);
        vertices.Add(basePos + face[1]);
        vertices.Add(basePos + face[2]);
        vertices.Add(basePos + face[3]);

        uvs.Add(faceUvs[0]);
        uvs.Add(faceUvs[1]);
        uvs.Add(faceUvs[2]);
        uvs.Add(faceUvs[3]);

        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start + 0);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
    }

    private Vector2[] GetFaceUvs(int directionIndex)
    {
        return directionIndex switch
        {
            // +X and -Z faces use a different vertex winding, so their UVs
            // keep texture U horizontal and V vertical instead of rotating.
            0 or 5 => new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            // Ceiling face maps X/Z directly so top materials are not rotated.
            2 => new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            _ => new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            }
        };
    }

    private Vector3[] GetFaceVertices(int directionIndex)
    {
        return directionIndex switch
        {
            0 => new[]
            {
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 1f, 1f),
                new Vector3(1f, 1f, 0f)
            },
            1 => new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 1f, 1f),
                new Vector3(0f, 0f, 1f)
            },
            2 => new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(1f, 1f, 1f),
                new Vector3(0f, 1f, 1f)
            },
            3 => new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, 0f)
            },
            4 => new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 1f, 1f),
                new Vector3(1f, 1f, 1f),
                new Vector3(1f, 0f, 1f)
            },
            5 => new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(0f, 1f, 0f)
            },
            _ => new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(1f, 1f, 1f),
                new Vector3(0f, 1f, 1f)
            }
        };
    }

    //-------------------------------------------------------------------
    // Chunk Object Management
    //-------------------------------------------------------------------

    private Transform CreateOrClearRoot()
    {
        Transform existing = transform.Find(RootName);
        if (existing != null)
        {
            ClearChildren(existing);
            return existing;
        }

        var rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(transform, false);
        return rootObject.transform;
    }

    private Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    private void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    //-------------------------------------------------------------------
    // Room Bounds Rules
    //-------------------------------------------------------------------

    private RoomShape PickRoomShape(System.Random random)
    {
        Array values = Enum.GetValues(typeof(RoomShape));
        return (RoomShape)values.GetValue(random.Next(values.Length));
    }

    private BoundsInt CreateRoomBounds(BoundsInt leafBounds, RoomShape shape, System.Random random)
    {
        int margin = random.Next(4, 11);
        int maxWidth = leafBounds.size.x - margin * 2;
        int maxDepth = leafBounds.size.z - margin * 2;
        if (maxWidth < 6 || maxDepth < 6)
        {
            margin = 1;
            maxWidth = leafBounds.size.x - margin * 2;
            maxDepth = leafBounds.size.z - margin * 2;
        }

        Vector2 scale = GetShapeScale(shape);
        int targetWidth = Mathf.RoundToInt(baseRoomSize * scale.x);
        int targetDepth = Mathf.RoundToInt(baseRoomSize * scale.y);

        int width = Mathf.Clamp(targetWidth, 6, Mathf.Max(6, maxWidth));
        int depth = Mathf.Clamp(targetDepth, 6, Mathf.Max(6, maxDepth));

        int minX = leafBounds.xMin + margin;
        int minZ = leafBounds.zMin + margin;
        int maxX = leafBounds.xMax - margin - width;
        int maxZ = leafBounds.zMax - margin - depth;

        int startX = maxX >= minX ? random.Next(minX, maxX + 1) : minX;
        int startZ = maxZ >= minZ ? random.Next(minZ, maxZ + 1) : minZ;

        return new BoundsInt(
            new Vector3Int(startX, 0, startZ),
            new Vector3Int(width, mapHeight, depth)
        );
    }

    private static Vector2 GetShapeScale(RoomShape shape)
    {
        return shape switch
        {
            RoomShape.Small => new Vector2(0.6f, 0.6f),
            RoomShape.Normal => new Vector2(1.0f, 1.0f),
            RoomShape.Large => new Vector2(1.4f, 1.4f),
            RoomShape.LongWide => new Vector2(1.6f, 0.8f),
            RoomShape.LongTall => new Vector2(0.8f, 1.6f),
            _ => new Vector2(1.0f, 1.0f)
        };
    }

    //-------------------------------------------------------------------
    // Gizmos
    //-------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (root == null)
        {
            return;
        }

        if (drawBspGizmos)
        {
            Gizmos.color = Color.yellow;
            DrawBspGizmos(root);
        }

        if (drawRoomGizmos)
        {
            Gizmos.color = Color.green;
            foreach (Room room in rooms)
            {
                Vector3 center = room.bounds.center + (Vector3)worldOffset;
                Gizmos.DrawWireCube(center, room.bounds.size);
            }
        }
    }

    private void DrawBspGizmos(BSPNode node)
    {
        if (node == null)
        {
            return;
        }

        Vector3 center = node.bounds.center + (Vector3)worldOffset;
        Gizmos.DrawWireCube(center, node.bounds.size);
        DrawBspGizmos(node.left);
        DrawBspGizmos(node.right);
    }

    //-------------------------------------------------------------------
    // World Offset
    //-------------------------------------------------------------------

    private void ApplyWorldOffsetToStartRooms(List<StartRoom> startRooms)
    {
        if (worldOffset == Vector3Int.zero)
        {
            return;
        }

        foreach (StartRoom room in startRooms)
        {
            room.teamAnchorPosition += (Vector3)worldOffset;

            for (int i = 0; i < room.playerSpawnPositions.Count; i++)
            {
                room.playerSpawnPositions[i] += (Vector3)worldOffset;
            }
        }
    }

    //-------------------------------------------------------------------
    // Map Infos
    //-------------------------------------------------------------------

    public IReadOnlyList<Room> Rooms => rooms;
    public IReadOnlyList<Corridor> Corridors => corridors;
    public Room ExitRoom => exitRoom;
    public IReadOnlyCollection<Vector3Int> FloorTiles => floorTiles;
    public IReadOnlyCollection<Vector3Int> WallTiles => wallTiles;
    public IReadOnlyCollection<Vector3Int> CeilingTiles => ceilingTiles;
    public IReadOnlyList<WallSegment> WallSegments => wallSegments;
    public Vector3Int WorldOffset => worldOffset;

    public bool IsFloorTile(Vector3Int tile)
    {
        return floorTiles.Contains(tile);
    }

    public bool IsWallTile(Vector3Int tile)
    {
        return wallTiles.Contains(tile);
    }

    public bool IsCeilingTile(Vector3Int tile)
    {
        return ceilingTiles.Contains(tile);
    }

    public Vector3 TileToWorld(Vector3Int tile)
    {
        return new Vector3(
            tile.x + worldOffset.x,
            tile.y + worldOffset.y,
            tile.z + worldOffset.z
        );
    }

    public Vector3 TileToWorldCenter(Vector3Int tile)
    {
        return TileToWorld(tile) + new Vector3(0.5f, 0f, 0.5f);
    }

    public Vector3Int WorldToTile(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - (Vector3)worldOffset;
        return new Vector3Int(
            Mathf.FloorToInt(local.x),
            Mathf.FloorToInt(local.y),
            Mathf.FloorToInt(local.z)
        );
    }

    //-------------------------------------------------------------------
    // Mesh Types
    //-------------------------------------------------------------------

    private enum MeshSectionKind
    {
        Room,
        Corridor,
        StartRoom
    }

    private enum SectionSurfaceType
    {
        Floor,
        Wall,
        Ceiling
    }

    private sealed class MeshSectionData
    {
        public readonly string name;
        public readonly MeshSectionKind kind;
        public readonly WallSegmentOwnerType ownerType;
        public readonly int ownerId;
        public readonly HashSet<Vector3Int> floorTiles = new();
        public readonly HashSet<Vector3Int> wallTiles = new();
        public readonly HashSet<Vector3Int> ceilingTiles = new();

        public MeshSectionData(string name, MeshSectionKind kind, WallSegmentOwnerType ownerType, int ownerId)
        {
            this.name = name;
            this.kind = kind;
            this.ownerType = ownerType;
            this.ownerId = ownerId;
        }
    }

    private readonly struct WallSegmentCandidate
    {
        public readonly Vector3Int floorTile;
        public readonly Vector3Int wallTile;

        public WallSegmentCandidate(Vector3Int floorTile, Vector3Int wallTile)
        {
            this.floorTile = floorTile;
            this.wallTile = wallTile;
        }
    }

    private readonly struct WallSegmentLineKey : IEquatable<WallSegmentLineKey>
    {
        public readonly WallSide side;
        public readonly int lineCoordinate;

        public WallSegmentLineKey(WallSide side, int lineCoordinate)
        {
            this.side = side;
            this.lineCoordinate = lineCoordinate;
        }

        public bool Equals(WallSegmentLineKey other)
        {
            return side == other.side && lineCoordinate == other.lineCoordinate;
        }

        public override bool Equals(object obj)
        {
            return obj is WallSegmentLineKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)side, lineCoordinate);
        }
    }

}

public enum WallSegmentOwnerType
{
    Room,
    StartRoom,
    Corridor
}

public enum WallSide
{
    North,
    South,
    East,
    West
}

public sealed class WallSegment
{
    public readonly int id;
    public readonly WallSegmentOwnerType ownerType;
    public readonly int ownerId;
    public readonly WallSide side;
    public readonly Vector3 roomFacingDirection;
    public readonly Vector3Int startWallTile;
    public readonly Vector3Int endWallTile;
    public readonly List<Vector3Int> wallTiles;
    public readonly List<Vector3Int> floorTiles;

    public int LengthTiles => wallTiles.Count;

    public WallSegment(
        int id,
        WallSegmentOwnerType ownerType,
        int ownerId,
        WallSide side,
        Vector3 roomFacingDirection,
        List<Vector3Int> wallTiles,
        List<Vector3Int> floorTiles
    )
    {
        this.id = id;
        this.ownerType = ownerType;
        this.ownerId = ownerId;
        this.side = side;
        this.roomFacingDirection = roomFacingDirection;
        this.wallTiles = new List<Vector3Int>(wallTiles);
        this.floorTiles = new List<Vector3Int>(floorTiles);
        startWallTile = this.wallTiles.Count > 0 ? this.wallTiles[0] : default;
        endWallTile = this.wallTiles.Count > 0 ? this.wallTiles[^1] : default;
    }
}