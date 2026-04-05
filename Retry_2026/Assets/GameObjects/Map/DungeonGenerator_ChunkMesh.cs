using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class DungeonGenerator_ChunkMesh : MonoBehaviour
{
    public event Action<DungeonGenerator_ChunkMesh> DungeonGenerated;

    [Header("Map")]
    public int mapSize = 750;
    public int mapHeight = 10;

    [Header("Rooms")]
    public int baseRoomSize = 50;
    public int targetRoomCount = 20;
    public int minLeafSize = 100;
    [Range(0f, 1f)] public float specialRoomLayoutChance = 0.45f;
    public int minimumLayoutInset = 2;

    [Header("Corridors")]
    public int corridorWidth = 10;

    [Header("Start Rooms")]
    public int startRoomEdgeMargin = 10;
    public int teamCount = 4;

    [Header("Generation")]
    public int seed = 12345;
    public bool randomizeSeedOnGenerate = true;
    public bool generateOnStart = true;
    public bool centerMapAtOrigin = true;

    [Header("Chunk Mesh")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;
    public bool createFloorCollider = true;
    public bool createWallCollider = true;
    public bool createCeilingCollider = true;

    [Header("Debug")]
    public bool drawBspGizmos = false;
    public bool drawRoomGizmos = false;

    private BSPNode root;
    private readonly List<Room> rooms = new();
    private readonly List<Corridor> corridors = new();
    private List<StartRoom> startRoomCandidates = new();
    private List<StartRoom> assignedStartRooms = new();
    private readonly HashSet<Vector3Int> floorTiles = new();
    private readonly HashSet<Vector3Int> wallTiles = new();
    private readonly HashSet<Vector3Int> ceilingTiles = new();
    private readonly HashSet<Vector3Int> solidTiles = new();
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

        floorTiles.Clear();
        wallTiles.Clear();
        ceilingTiles.Clear();
        solidTiles.Clear();

        BuildCorridors(root, random);
        ConnectStartRoomsToDungeon(random);
        AddRoomTiles();
        AddCorridorTiles();
        AddStartRoomTiles();
        BuildWallsAndCeiling();

        BuildChunkMeshes();
        DungeonGenerated?.Invoke(this);
    }

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
            corridor.connectedRoomIds.Add(nearest.id);
            corridors.Add(corridor);
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

                for (int y = 0; y < ceilingY; y++)
                {
                    wallTiles.Add(new Vector3Int(neighborFloor.x, y, neighborFloor.z));
                }
            }
        }

        foreach (Room room in rooms)
        {
            foreach (Vector3Int blockedTile in room.blockedTiles)
            {
                for (int y = 0; y < ceilingY; y++)
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

    private void BuildChunkMeshes()
    {
        Transform rootTransform = CreateOrClearRoot();
        Transform floorRoot = GetOrCreateChild(rootTransform, FloorRootName);
        Transform wallRoot = GetOrCreateChild(rootTransform, WallRootName);
        Transform ceilingRoot = GetOrCreateChild(rootTransform, CeilingRootName);
        Dictionary<string, MeshSectionData> sections = BuildMeshSections();

        CreateSectionObjects(sections, floorRoot, floorMaterial, createFloorCollider, SectionSurfaceType.Floor);
        CreateSectionObjects(sections, wallRoot, wallMaterial, createWallCollider, SectionSurfaceType.Wall);
        CreateSectionObjects(sections, ceilingRoot, ceilingMaterial, createCeilingCollider, SectionSurfaceType.Ceiling);
    }

    private Dictionary<string, MeshSectionData> BuildMeshSections()
    {
        var sections = new Dictionary<string, MeshSectionData>();

        foreach (Room room in rooms)
        {
            var section = new MeshSectionData($"Room_{room.id}", MeshSectionKind.Room);
            AddSectionFloorTiles(section, room.floorTiles);
            AddRoomBlockedWalls(section, room);
            PopulateSectionBoundarySurfaces(section);
            sections[section.name] = section;
        }

        foreach (StartRoom startRoom in assignedStartRooms)
        {
            var section = new MeshSectionData($"StartRoom_{startRoom.slotIndex}", MeshSectionKind.StartRoom);
            AddSectionFloorTiles(section, EnumerateBoundsTiles(startRoom.bounds));
            PopulateSectionBoundarySurfaces(section);
            sections[section.name] = section;
        }

        foreach (Corridor corridor in corridors)
        {
            var section = new MeshSectionData($"Corridor_{corridor.id}", MeshSectionKind.Corridor);
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
            for (int y = 0; y < ceilingY; y++)
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

                for (int y = 0; y < ceilingY; y++)
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
        SectionSurfaceType surfaceType
    )
    {
        foreach (MeshSectionData section in sections.Values)
        {
            List<Vector3Int> tiles = GetSectionSurfaceTiles(section, surfaceType);
            Mesh mesh = BuildMeshForTiles(tiles);
            if (mesh == null || mesh.vertexCount == 0)
            {
                continue;
            }

            GameObject sectionObject = new GameObject($"{section.name}_{surfaceType}");
            sectionObject.transform.SetParent(parent, false);

            MeshFilter filter = sectionObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

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
        mesh.RecalculateBounds();
        return mesh;
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

            // 아래쪽 면
            // triangles.Add(start + 0);
            // triangles.Add(start + 1);
            // triangles.Add(start + 2);
            // triangles.Add(start + 0);
            // triangles.Add(start + 2);
            // triangles.Add(start + 3);

            // 위쪽 ㄴ면
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

        int start = vertices.Count;
        vertices.Add(basePos + face[0]);
        vertices.Add(basePos + face[1]);
        vertices.Add(basePos + face[2]);
        vertices.Add(basePos + face[3]);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));

        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start + 0);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
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
        public readonly HashSet<Vector3Int> floorTiles = new();
        public readonly HashSet<Vector3Int> wallTiles = new();
        public readonly HashSet<Vector3Int> ceilingTiles = new();

        public MeshSectionData(string name, MeshSectionKind kind)
        {
            this.name = name;
            this.kind = kind;
        }
    }

}
