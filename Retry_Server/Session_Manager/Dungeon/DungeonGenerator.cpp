#include "DungeonGenerator.h"
#include "../../Common/Logger.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <climits>

// ============================================================================
//  유틸 (Mathf 등 Unity 함수 대체)
// ============================================================================

static inline int   Mathf_Max(int a, int b) { return a > b ? a : b; }
static inline int   Mathf_Min(int a, int b) { return a < b ? a : b; }
static inline int   Mathf_Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
static inline int   Mathf_RoundToInt(float f)
{
    // Unity의 Mathf.RoundToInt는 bankers' rounding을 안 쓰고 일반 반올림.
    // C++의 std::round 동작과 동일.
    return (int)std::lround(f);
}
static inline int   Mathf_FloorToInt(float f) { return (int)std::floor(f); }

// 4방향 (XZ)
static const IntVec3 HorizontalDirections[4] = {
    IntVec3(1, 0,  0),
    IntVec3(-1, 0,  0),
    IntVec3(0, 0,  1),
    IntVec3(0, 0, -1),
};

// ============================================================================
//  Generate - 메인 진입점
// ============================================================================

void DungeonGenerator::Generate(int seed)
{
    Log::Info("[Dungeon] Generate seed=%d", seed);

    CSharpRandom random(seed);
    worldOffset = centerMapAtOrigin
        ? IntVec3(-mapSize / 2, 0, -mapSize / 2)
        : IntVec3(0, 0, 0);

    // 시작 방 후보 생성 + 팀 배정 (StartRoomManager.cs 1:1 포팅)
    {
        auto candidates = BuildStartRoomCandidates(mapSize, baseRoomSize,
            startRoomEdgeMargin, startRoomThickness);
        // C#의 AssignTeams는 candidates를 입력받아 results를 반환.
        // 여기선 in-place로 occupied/teamId 설정 + assignedStartRooms에 직접 push.
        AssignTeams(candidates, teamCount, seed);
    }

    // 내부 영역 (시작 방 영역 제외하고 안쪽만 BSP)
    int inset = startRoomEdgeMargin + baseRoomSize;
    int innerSize = Mathf_Max(1, mapSize - inset * 2);
    IntBounds innerBounds(
        IntVec3(inset, 0, inset),
        IntVec3(innerSize, mapHeight, innerSize)
    );

    root = std::make_unique<BSPNode>(innerBounds);
    SplitToTarget(root.get(), targetRoomCount, minLeafSize, random);

    BuildRooms(root.get(), random);
    BuildBossRooms(random);

    floorTiles.clear();
    wallTiles.clear();
    ceilingTiles.clear();
    solidTiles.clear();

    BuildCorridors(root.get(), random);
    ConnectBossRoomsToDungeon(random);
    ConnectStartRoomsToDungeon(random);
    AddRoomTiles();
    AddCorridorTiles();
    AddStartRoomTiles();
    BuildWallsAndCeiling();

    // 포탈 그래프 구성 (방+복도 노드 + 인접 관계)
    BuildNodeGraph();

    Log::Info("[Dungeon] 생성 완료: rooms=%d corridors=%d floor=%d wall=%d solid=%d",
        (int)rooms.size(), (int)corridors.size(),
        (int)floorTiles.size(), (int)wallTiles.size(), (int)solidTiles.size());
}

// ============================================================================
//  StartRoom (StartRoomManager.cs 1:1 정밀 포팅)
//
//  16개 후보 (4면 × 4슬롯) 중 시드 기반 Fisher-Yates shuffle로 teamCount개 선택.
//  주의: PickStartSlots는 별도의 System.Random(seed)을 만들어 사용 → 던전 RNG와 분리.
//        클라와 시드 동일 → 결과 동일.
// ============================================================================

std::vector<StartRoom> DungeonGenerator::BuildStartRoomCandidates(
    int mapSizeArg, int roomSize, int edgeMargin, int thickness)
{
    std::vector<StartRoom> candidates;
    candidates.reserve(16);

    int usable = mapSizeArg - 2 * edgeMargin;
    int totalRooms = 4 * roomSize;
    int gap = (usable - totalRooms) / 5;

    int slots[4];
    for (int i = 0; i < 4; i++)
    {
        slots[i] = edgeMargin + gap * (i + 1) + roomSize * i;
    }

    // South (z = edgeMargin), yaw=0
    for (int i = 0; i < 4; i++)
    {
        candidates.push_back(CreateStartRoom(
            i, IntVec3(slots[i], 0, edgeMargin), roomSize, thickness, 0.f));
    }

    // North (z = mapSize - edgeMargin - roomSize), yaw=180
    int northZ = mapSizeArg - edgeMargin - roomSize;
    for (int i = 0; i < 4; i++)
    {
        candidates.push_back(CreateStartRoom(
            4 + i, IntVec3(slots[i], 0, northZ), roomSize, thickness, 180.f));
    }

    // West (x = edgeMargin), yaw=90
    for (int i = 0; i < 4; i++)
    {
        candidates.push_back(CreateStartRoom(
            8 + i, IntVec3(edgeMargin, 0, slots[i]), roomSize, thickness, 90.f));
    }

    // East (x = mapSize - edgeMargin - roomSize), yaw=-90
    int eastX = mapSizeArg - edgeMargin - roomSize;
    for (int i = 0; i < 4; i++)
    {
        candidates.push_back(CreateStartRoom(
            12 + i, IntVec3(eastX, 0, slots[i]), roomSize, thickness, -90.f));
    }

    return candidates;
}

std::vector<int> DungeonGenerator::PickStartSlots(int teamCountArg, int seed)
{
    int count = Mathf_Clamp(teamCountArg, 1, 16);
    // C#의 new System.Random(seed)와 동일한 새 RNG 사용 (던전 RNG와 분리)
    CSharpRandom random(seed);

    std::vector<int> indices;
    indices.reserve(16);
    for (int i = 0; i < 16; i++) indices.push_back(i);

    // Fisher-Yates shuffle (C#과 동일한 방식)
    for (int i = (int)indices.size() - 1; i > 0; i--)
    {
        int j = random.Next(0, i + 1);
        int temp = indices[i];
        indices[i] = indices[j];
        indices[j] = temp;
    }

    indices.resize(count);
    return indices;
}

void DungeonGenerator::AssignTeams(std::vector<StartRoom>& candidates,
    int teamCountArg, int seed)
{
    auto picked = PickStartSlots(teamCountArg, seed);
    assignedStartRooms.clear();
    assignedStartRooms.reserve(picked.size());

    for (int i = 0; i < (int)picked.size(); ++i)
    {
        int idx = picked[i];
        StartRoom room = candidates[idx];     // 복사
        room.occupied = true;
        room.teamId = i;
        assignedStartRooms.push_back(std::move(room));
    }
}

StartRoom DungeonGenerator::CreateStartRoom(int slotIndex, IntVec3 position,
    int roomSize, int thickness,
    float yawDegrees)
{
    StartRoom sr;
    sr.slotIndex = slotIndex;
    sr.bounds = IntBounds(position, IntVec3(roomSize, thickness, roomSize));

    float spawnY = (float)sr.bounds.yMin() + 1.5f;
    Vec3 boundsCenter = sr.bounds.center();
    sr.teamAnchorPosition = Vec3(boundsCenter.x, spawnY, boundsCenter.z);
    sr.spawnYawDegrees = yawDegrees;
    sr.playerSpawnPositions = BuildPlayerSpawnPositions(
        sr.teamAnchorPosition, yawDegrees, roomSize);

    return sr;
}

std::vector<Vec3> DungeonGenerator::BuildPlayerSpawnPositions(
    const Vec3& anchor, float yawDegrees, int roomSize)
{
    // C#: spacing = Mathf.Clamp(roomSize * 0.18f, 1.5f, 3.5f)
    float spacing = (float)roomSize * 0.18f;
    if (spacing < 1.5f) spacing = 1.5f;
    if (spacing > 3.5f) spacing = 3.5f;

    // C#: rotation * Vector3.right
    // Quaternion.Euler(0, yaw, 0)이 Vector3.right(=(1,0,0))를 회전시킨 결과:
    //   yaw=0    → (1, 0, 0)
    //   yaw=90   → (0, 0, -1)
    //   yaw=180  → (-1, 0, 0)
    //   yaw=-90  → (0, 0, 1)
    // 일반: right = (cos(yaw), 0, -sin(yaw))
    float yawRad = yawDegrees * 3.14159265358979323846f / 180.f;
    Vec3 right(std::cos(yawRad), 0.f, -std::sin(yawRad));

    std::vector<Vec3> positions;
    positions.reserve(3);
    positions.push_back(Vec3(anchor.x - right.x * spacing,
        anchor.y - right.y * spacing,
        anchor.z - right.z * spacing));
    positions.push_back(anchor);
    positions.push_back(Vec3(anchor.x + right.x * spacing,
        anchor.y + right.y * spacing,
        anchor.z + right.z * spacing));
    return positions;
}

// ============================================================================
//  BSP 분할
// ============================================================================

void DungeonGenerator::SplitToTarget(BSPNode* node, int targetCount, int minSize, CSharpRandom& random)
{
    std::vector<BSPNode*> leaves = { node };
    std::vector<BSPNode*> splittable = { node };
    int safety = 0;

    while ((int)leaves.size() < targetCount && !splittable.empty() && safety < 1000)
    {
        safety++;
        int idx = random.Next(0, (int)splittable.size());
        BSPNode* leaf = splittable[idx];

        if (leaf->TrySplit(random, minSize))
        {
            // leaves에서 leaf 제거
            auto itL = std::find(leaves.begin(), leaves.end(), leaf);
            if (itL != leaves.end()) leaves.erase(itL);
            leaves.push_back(leaf->left.get());
            leaves.push_back(leaf->right.get());

            splittable.erase(splittable.begin() + idx);
            splittable.push_back(leaf->left.get());
            splittable.push_back(leaf->right.get());
        }
        else
        {
            splittable.erase(splittable.begin() + idx);
        }
    }
}

// ============================================================================
//  방 생성
// ============================================================================

void DungeonGenerator::BuildRooms(BSPNode* node, CSharpRandom& random)
{
    rooms.clear();
    corridors.clear();

    std::vector<BSPNode*> leaves;
    node->CollectLeaves(leaves);

    int roomId = 0;
    rooms.reserve(leaves.size() + bossRoomCount);

    for (BSPNode* leaf : leaves)
    {
        RoomShape shape = PickRoomShape(random);
        IntBounds rb = CreateRoomBounds(leaf->bounds, shape, random);

        Room room;
        room.id = roomId++;
        room.type = ROOM_TYPE_NORMAL;
        room.shape = shape;
        room.bounds = rb;
        room.layoutType = PickRoomLayout(rb, random);
        GenerateRoomLayout(room, random);

        leaf->roomBounds = rb;
        leaf->hasRoom = true;
        rooms.push_back(std::move(room));
        leaf->roomData = &rooms.back();
    }
    // 주의: rooms에 push_back 하면 vector가 재할당되며 leaf->roomData 포인터가 깨질 수 있음.
    //       reserve로 충분한 공간 확보했지만, 안전하게 한 번 더 보정.
    //       (위에서 reserve(leaves.size + bossRoomCount) 했으므로 일단 괜찮을 것)
    //       BuildBossRooms에서도 push_back 발생하므로 그쪽에서도 ClearRoomFromBsp 호출 시 주의.
}

// ============================================================================
//  보스방 생성
// ============================================================================

void DungeonGenerator::BuildBossRooms(CSharpRandom& random)
{
    if (bossRoomCount <= 0) return;

    int clampedCount = Mathf_Clamp(bossRoomCount, 0, 2);
    std::vector<IntBounds> bossBounds = CreateBossRoomBounds(clampedCount);

    for (const IntBounds& b : bossBounds)
    {
        RemoveRoomsOverlapping(b, bossRoomOverlapPadding);

        Room room;
        room.id = GetNextRoomId();
        room.type = ROOM_TYPE_BOSS;
        room.shape = ROOM_SHAPE_LARGE;
        room.layoutType = LAYOUT_OPEN;
        room.bounds = b;
        GenerateRoomLayout(room, random);
        rooms.push_back(std::move(room));
    }
}

std::vector<IntBounds> DungeonGenerator::CreateBossRoomBounds(int count)
{
    std::vector<IntBounds> result;
    int width = Mathf_Max(6, bossRoomSizeX);
    int depth = Mathf_Max(6, bossRoomSizeZ);
    int centerX = mapSize / 2;
    int centerZ = mapSize / 2;

    if (count == 1)
    {
        result.push_back(CreateCenteredBounds(centerX, centerZ, width, depth));
        return result;
    }

    int spacing = width + Mathf_Max(corridorWidth * 2, 8);
    int leftCenterX = centerX - spacing / 2;
    int rightCenterX = centerX + spacing / 2;
    result.push_back(CreateCenteredBounds(leftCenterX, centerZ, width, depth));
    result.push_back(CreateCenteredBounds(rightCenterX, centerZ, width, depth));
    return result;
}

IntBounds DungeonGenerator::CreateCenteredBounds(int centerX, int centerZ, int width, int depth)
{
    int startRoomInset = startRoomEdgeMargin + baseRoomSize;
    int minX = startRoomInset;
    int minZ = startRoomInset;
    int maxX = Mathf_Max(minX, mapSize - startRoomInset - width);
    int maxZ = Mathf_Max(minZ, mapSize - startRoomInset - depth);
    int startX = Mathf_Clamp(centerX - width / 2, minX, maxX);
    int startZ = Mathf_Clamp(centerZ - depth / 2, minZ, maxZ);

    return IntBounds(IntVec3(startX, 0, startZ), IntVec3(width, mapHeight, depth));
}

int DungeonGenerator::GetNextRoomId()
{
    int nextId = 0;
    for (const Room& r : rooms)
    {
        nextId = Mathf_Max(nextId, r.id + 1);
    }
    return nextId;
}

void DungeonGenerator::RemoveRoomsOverlapping(const IntBounds& bounds, int padding)
{
    IntBounds padded = ExpandBoundsXZ(bounds, Mathf_Max(0, padding));

    for (int i = (int)rooms.size() - 1; i >= 0; i--)
    {
        if (rooms[i].type == ROOM_TYPE_BOSS) continue;
        if (!OverlapsXZ(rooms[i].bounds, padded)) continue;

        ClearRoomFromBsp(root.get(), &rooms[i]);
        rooms.erase(rooms.begin() + i);
    }

    // rooms vector 변경되었으니 BSP의 모든 roomData 포인터 재정렬 필요.
    // 단순하게 BSP 루트부터 다시 매핑.
    // (TrySplit 후 추가 push_back 안 하면 OK이지만 안전상 재매핑)
    // → ClearRoomFromBsp가 영향받은 노드만 nullptr 처리하므로 다른 노드는 OK.
    //   다만 erase로 인해 뒤쪽 룸들의 메모리 위치가 앞당겨지므로 BSP의 roomData 포인터가 stale.
    //   → 전체 재매핑.
    // 구현 단순화를 위해 모든 BSP 노드의 roomData를 nullptr로 clear한 뒤,
    //   현재 rooms vector 기준으로 다시 BSP 트리 leaf와 매핑.
    //   leaf의 hasRoom과 roomBounds가 일치하는 룸을 찾아 연결.
    std::vector<BSPNode*> leaves;
    if (root) root->CollectLeaves(leaves);
    for (BSPNode* l : leaves) l->roomData = nullptr;
    for (Room& r : rooms)
    {
        for (BSPNode* l : leaves)
        {
            if (!l->hasRoom) continue;
            if (l->roomBounds.position == r.bounds.position &&
                l->roomBounds.size == r.bounds.size)
            {
                l->roomData = &r;
                break;
            }
        }
    }
}

void DungeonGenerator::ClearRoomFromBsp(BSPNode* node, const Room* room)
{
    if (!node) return;
    if (node->roomData == room)
    {
        node->roomData = nullptr;
        node->hasRoom = false;
    }
    ClearRoomFromBsp(node->left.get(), room);
    ClearRoomFromBsp(node->right.get(), room);
}

IntBounds DungeonGenerator::ExpandBoundsXZ(const IntBounds& bounds, int padding)
{
    return IntBounds(
        IntVec3(bounds.xMin() - padding, bounds.yMin(), bounds.zMin() - padding),
        IntVec3(bounds.size.x + padding * 2, bounds.size.y, bounds.size.z + padding * 2)
    );
}

bool DungeonGenerator::OverlapsXZ(const IntBounds& a, const IntBounds& b)
{
    return a.xMin() < b.xMax() && a.xMax() > b.xMin() &&
        a.zMin() < b.zMax() && a.zMax() > b.zMin();
}

// ============================================================================
//  복도 연결
// ============================================================================

void DungeonGenerator::BuildCorridors(BSPNode* node, CSharpRandom& random)
{
    if (!node || !node->left || !node->right) return;

    Room* leftRoom = node->left->GetRoomData();
    Room* rightRoom = node->right->GetRoomData();
    if (leftRoom && rightRoom)
    {
        Vec3 lc = leftRoom->bounds.center();
        Vec3 rc = rightRoom->bounds.center();
        IntVec3 leftConn = GetRoomConnectionPoint(*leftRoom,
            IntVec3(Mathf_FloorToInt(rc.x), Mathf_FloorToInt(rc.y), Mathf_FloorToInt(rc.z)));
        IntVec3 rightConn = GetRoomConnectionPoint(*rightRoom,
            IntVec3(Mathf_FloorToInt(lc.x), Mathf_FloorToInt(lc.y), Mathf_FloorToInt(lc.z)));

        Corridor cor = CreateCorridor(leftConn, rightConn, random);
        leftRoom->doorwayFloorTiles.insert(leftConn);
        rightRoom->doorwayFloorTiles.insert(rightConn);
        cor.connectedRoomIds.push_back(leftRoom->id);
        cor.connectedRoomIds.push_back(rightRoom->id);
        corridors.push_back(std::move(cor));
        AddNeighbor(leftRoom, rightRoom);
    }

    BuildCorridors(node->left.get(), random);
    BuildCorridors(node->right.get(), random);
}

void DungeonGenerator::AddNeighbor(Room* a, Room* b)
{
    if (std::find(a->neighbors.begin(), a->neighbors.end(), b->id) == a->neighbors.end())
        a->neighbors.push_back(b->id);
    if (std::find(b->neighbors.begin(), b->neighbors.end(), a->id) == b->neighbors.end())
        b->neighbors.push_back(a->id);
}

Corridor DungeonGenerator::CreateCorridor(IntVec3 start, IntVec3 end, CSharpRandom& random)
{
    Corridor c;
    c.id = (int)corridors.size();

    bool xFirst = random.NextDouble() > 0.5;
    if (xFirst)
    {
        AddHorizontalCorridor(c.floorTiles, start.x, end.x, start.z);
        AddVerticalCorridor(c.floorTiles, start.z, end.z, end.x);
    }
    else
    {
        AddVerticalCorridor(c.floorTiles, start.z, end.z, start.x);
        AddHorizontalCorridor(c.floorTiles, start.x, end.x, end.z);
    }
    return c;
}

void DungeonGenerator::ConnectStartRoomsToDungeon(CSharpRandom& random)
{
    if (rooms.empty() || assignedStartRooms.empty()) return;

    for (StartRoom& sr : assignedStartRooms)
    {
        Vec3 sc = sr.bounds.center();
        Room* nearest = FindNearestRoom(sc);
        if (!nearest) continue;

        IntVec3 startCenter(Mathf_FloorToInt(sc.x), Mathf_FloorToInt(sc.y), Mathf_FloorToInt(sc.z));
        IntVec3 roomConn = GetRoomConnectionPoint(*nearest, startCenter);
        Corridor cor = CreateCorridor(startCenter, roomConn, random);
        nearest->doorwayFloorTiles.insert(roomConn);
        cor.connectedRoomIds.push_back(nearest->id);
        corridors.push_back(std::move(cor));
    }
}

void DungeonGenerator::ConnectBossRoomsToDungeon(CSharpRandom& random)
{
    int connectionCount = Mathf_Max(1, bossRoomConnectionCount);

    // rooms vector를 순회하면서 뒤에 push_back 하면 iterator 깨짐.
    // 보스 룸 인덱스만 미리 모은 뒤 처리.
    std::vector<int> bossIndices;
    for (int i = 0; i < (int)rooms.size(); ++i)
        if (rooms[i].type == ROOM_TYPE_BOSS) bossIndices.push_back(i);

    for (int bi : bossIndices)
    {
        Room* bossRoom = &rooms[bi];
        std::vector<Room*> targets = FindNearestRooms(bossRoom->bounds.center(), connectionCount, bossRoom);

        for (Room* targetRoom : targets)
        {
            Vec3 tc = targetRoom->bounds.center();
            Vec3 bc = bossRoom->bounds.center();

            IntVec3 bossConn = GetRoomConnectionPoint(*bossRoom,
                IntVec3(Mathf_FloorToInt(tc.x), Mathf_FloorToInt(tc.y), Mathf_FloorToInt(tc.z)));
            IntVec3 targetConn = GetRoomConnectionPoint(*targetRoom,
                IntVec3(Mathf_FloorToInt(bc.x), Mathf_FloorToInt(bc.y), Mathf_FloorToInt(bc.z)));

            Corridor cor = CreateCorridor(bossConn, targetConn, random);
            bossRoom->doorwayFloorTiles.insert(bossConn);
            targetRoom->doorwayFloorTiles.insert(targetConn);
            cor.connectedRoomIds.push_back(bossRoom->id);
            cor.connectedRoomIds.push_back(targetRoom->id);
            corridors.push_back(std::move(cor));
            AddNeighbor(bossRoom, targetRoom);
        }
    }
}

Room* DungeonGenerator::FindNearestRoom(const Vec3& startCenter)
{
    Room* nearest = nullptr;
    float bestDist = 1e30f;

    for (Room& r : rooms)
    {
        Vec3 c = r.bounds.center();
        float dx = c.x - startCenter.x;
        float dy = c.y - startCenter.y;
        float dz = c.z - startCenter.z;
        float d = std::sqrt(dx * dx + dy * dy + dz * dz);
        if (d < bestDist) { bestDist = d; nearest = &r; }
    }
    return nearest;
}

std::vector<Room*> DungeonGenerator::FindNearestRooms(const Vec3& startCenter, int count, const Room* excludedRoom)
{
    std::vector<Room*> candidates;
    for (Room& r : rooms)
    {
        if (&r == excludedRoom) continue;
        candidates.push_back(&r);
    }
    std::sort(candidates.begin(), candidates.end(),
        [&startCenter](Room* a, Room* b) {
            Vec3 ca = a->bounds.center();
            Vec3 cb = b->bounds.center();
            float da = (ca.x - startCenter.x) * (ca.x - startCenter.x)
                + (ca.y - startCenter.y) * (ca.y - startCenter.y)
                + (ca.z - startCenter.z) * (ca.z - startCenter.z);
            float db = (cb.x - startCenter.x) * (cb.x - startCenter.x)
                + (cb.y - startCenter.y) * (cb.y - startCenter.y)
                + (cb.z - startCenter.z) * (cb.z - startCenter.z);
            return da < db;
        });

    if ((int)candidates.size() > count) candidates.resize(count);
    return candidates;
}

void DungeonGenerator::AddHorizontalCorridor(std::unordered_set<IntVec3>& dst, int x1, int x2, int z)
{
    int mn = Mathf_Min(x1, x2);
    int mx = Mathf_Max(x1, x2);
    int half = Mathf_Max(0, corridorWidth / 2);
    for (int x = mn; x <= mx; x++)
        for (int o = -half; o <= half; o++)
            dst.insert(IntVec3(x, 0, z + o));
}

void DungeonGenerator::AddVerticalCorridor(std::unordered_set<IntVec3>& dst, int z1, int z2, int x)
{
    int mn = Mathf_Min(z1, z2);
    int mx = Mathf_Max(z1, z2);
    int half = Mathf_Max(0, corridorWidth / 2);
    for (int z = mn; z <= mx; z++)
        for (int o = -half; o <= half; o++)
            dst.insert(IntVec3(x + o, 0, z));
}

// ============================================================================
//  타일 수집
// ============================================================================

void DungeonGenerator::AddRoomTiles()
{
    for (const Room& r : rooms) AddRoomFloorTiles(r);
}

void DungeonGenerator::AddStartRoomTiles()
{
    for (const StartRoom& sr : assignedStartRooms) AddBoundsTiles(sr.bounds);
}

void DungeonGenerator::AddCorridorTiles()
{
    for (const Corridor& c : corridors)
        for (const IntVec3& t : c.floorTiles)
            floorTiles.insert(t);
}

void DungeonGenerator::AddBoundsTiles(const IntBounds& bounds)
{
    for (int x = bounds.xMin(); x < bounds.xMax(); x++)
        for (int z = bounds.zMin(); z < bounds.zMax(); z++)
            floorTiles.insert(IntVec3(x, 0, z));
}

void DungeonGenerator::AddRoomFloorTiles(const Room& room)
{
    for (const IntVec3& t : room.floorTiles) floorTiles.insert(t);
}

// ============================================================================
//  방 내부 레이아웃
// ============================================================================

RoomLayoutType DungeonGenerator::PickRoomLayout(const IntBounds& roomBounds, CSharpRandom& random)
{
    IntVec2 isz = GetInteriorSize(roomBounds);
    bool canPillars = isz.x >= 8 && isz.z >= 8;
    bool canCenterBlock = isz.x >= 12 && isz.z >= 12;

    std::vector<RoomLayoutType> candidates = { LAYOUT_OPEN };
    if (canPillars)     candidates.push_back(LAYOUT_FOUR_PILLARS);
    if (canCenterBlock) candidates.push_back(LAYOUT_CENTER_BLOCK);

    if (candidates.size() == 1 || random.NextDouble() > specialRoomLayoutChance)
        return LAYOUT_OPEN;

    return candidates[random.Next(1, (int)candidates.size())];
}

void DungeonGenerator::GenerateRoomLayout(Room& room, CSharpRandom& random)
{
    room.floorTiles.clear();
    room.blockedTiles.clear();
    FillRoomInterior(room.floorTiles, room.bounds);

    switch (room.layoutType)
    {
    case LAYOUT_FOUR_PILLARS: ApplyFourPillarsLayout(room, random); break;
    case LAYOUT_CENTER_BLOCK: ApplyCenterBlockLayout(room, random); break;
    default: break;
    }
}

void DungeonGenerator::FillRoomInterior(std::unordered_set<IntVec3>& dst, const IntBounds& bounds)
{
    for (int x = bounds.xMin(); x < bounds.xMax(); x++)
        for (int z = bounds.zMin(); z < bounds.zMax(); z++)
            dst.insert(IntVec3(x, 0, z));
}

void DungeonGenerator::ApplyFourPillarsLayout(Room& room, CSharpRandom& /*random*/)
{
    IntVec2 isz = GetInteriorSize(room.bounds);
    IntVec2 pSize = CalculateFourPillarSize(isz);
    IntVec2 xc = CalculateQuarterCenters(isz.x);
    IntVec2 zc = CalculateQuarterCenters(isz.z);

    IntVec3 origins[4] = {
        CreateInteriorOriginFromCenter(room.bounds, xc.x, zc.x, pSize),
        CreateInteriorOriginFromCenter(room.bounds, xc.z, zc.x, pSize),
        CreateInteriorOriginFromCenter(room.bounds, xc.x, zc.z, pSize),
        CreateInteriorOriginFromCenter(room.bounds, xc.z, zc.z, pSize),
    };

    for (const IntVec3& origin : origins)
    {
        BlockArea(room, IntBounds(origin, IntVec3(pSize.x, mapHeight, pSize.z)));
    }
}

void DungeonGenerator::ApplyCenterBlockLayout(Room& room, CSharpRandom& random)
{
    IntVec2 isz = GetInteriorSize(room.bounds);
    int maxWidth = isz.x - minimumLayoutInset * 2;
    int maxDepth = isz.z - minimumLayoutInset * 2;
    int blockWidth = Mathf_Clamp(NextInclusive(random, isz.x / 3, isz.x / 2), 4, maxWidth);
    int blockDepth = Mathf_Clamp(NextInclusive(random, isz.z / 3, isz.z / 2), 4, maxDepth);
    int startX = room.bounds.xMin() + (room.bounds.size.x - blockWidth) / 2;
    int startZ = room.bounds.zMin() + (room.bounds.size.z - blockDepth) / 2;

    BlockArea(room, IntBounds(
        IntVec3(startX, 0, startZ),
        IntVec3(blockWidth, mapHeight, blockDepth)));
}

void DungeonGenerator::BlockArea(Room& room, const IntBounds& area)
{
    for (int x = area.xMin(); x < area.xMax(); x++)
        for (int z = area.zMin(); z < area.zMax(); z++)
        {
            IntVec3 t(x, 0, z);
            room.floorTiles.erase(t);
            room.blockedTiles.insert(t);
        }
}

IntVec3 DungeonGenerator::GetRoomConnectionPoint(const Room& room, const IntVec3& target)
{
    if (room.floorTiles.empty())
    {
        Vec3 c = room.bounds.center();
        return IntVec3(Mathf_FloorToInt(c.x), Mathf_FloorToInt(c.y), Mathf_FloorToInt(c.z));
    }

    Vec3 cc = room.bounds.center();
    IntVec3 best(Mathf_FloorToInt(cc.x), Mathf_FloorToInt(cc.y), Mathf_FloorToInt(cc.z));
    float bestDist = 1e30f;

    for (const IntVec3& tile : room.floorTiles)
    {
        float dx = (float)(tile.x - target.x);
        float dy = (float)(tile.y - target.y);
        float dz = (float)(tile.z - target.z);
        float d = dx * dx + dy * dy + dz * dz;
        if (d < bestDist) { bestDist = d; best = tile; }
    }
    return best;
}

IntVec2 DungeonGenerator::GetInteriorSize(const IntBounds& bounds)
{
    return IntVec2(bounds.size.x, bounds.size.z);
}

IntVec3 DungeonGenerator::CreateInteriorOrigin(const IntBounds& bounds, int localX, int localZ, IntVec2 size)
{
    int clampedX = Mathf_Clamp(localX, minimumLayoutInset,
        Mathf_Max(minimumLayoutInset, bounds.size.x - minimumLayoutInset - size.x));
    int clampedZ = Mathf_Clamp(localZ, minimumLayoutInset,
        Mathf_Max(minimumLayoutInset, bounds.size.z - minimumLayoutInset - size.z));
    return IntVec3(bounds.xMin() + clampedX, 0, bounds.zMin() + clampedZ);
}

IntVec3 DungeonGenerator::CreateInteriorOriginFromCenter(const IntBounds& bounds, int centerX, int centerZ, IntVec2 size)
{
    int startX = centerX - size.x / 2;
    int startZ = centerZ - size.z / 2;
    return CreateInteriorOrigin(bounds, startX, startZ, size);
}

IntVec2 DungeonGenerator::CalculateFourPillarSize(IntVec2 interiorSize)
{
    int desiredW = Mathf_Max(2, interiorSize.x / 4);
    int desiredD = Mathf_Max(2, interiorSize.z / 4);
    int maxW = Mathf_Max(2, (interiorSize.x - minimumLayoutInset * 2) / 2);
    int maxD = Mathf_Max(2, (interiorSize.z - minimumLayoutInset * 2) / 2);
    return IntVec2(Mathf_Clamp(desiredW, 2, maxW), Mathf_Clamp(desiredD, 2, maxD));
}

IntVec2 DungeonGenerator::CalculateQuarterCenters(int axisSize)
{
    int firstCenter = Mathf_RoundToInt(axisSize * 0.25f);
    int secondCenter = Mathf_RoundToInt(axisSize * 0.75f);
    int minC = minimumLayoutInset;
    int maxC = Mathf_Max(minC, axisSize - minimumLayoutInset);
    return IntVec2(Mathf_Clamp(firstCenter, minC, maxC), Mathf_Clamp(secondCenter, minC, maxC));
}

int DungeonGenerator::NextInclusive(CSharpRandom& random, int minInclusive, int maxInclusive)
{
    if (maxInclusive <= minInclusive) return minInclusive;
    return random.Next(minInclusive, maxInclusive + 1);
}

// ============================================================================
//  방 모양/크기
// ============================================================================

RoomShape DungeonGenerator::PickRoomShape(CSharpRandom& random)
{
    // C#: Enum.GetValues(typeof(RoomShape)) → 5개 값 배열, random.Next(values.Length).
    // 우리도 RoomShape enum이 5개. 0~4.
    int idx = random.Next(5);
    return (RoomShape)idx;
}

IntBounds DungeonGenerator::CreateRoomBounds(const IntBounds& leafBounds, RoomShape shape, CSharpRandom& random)
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

    float sx, sy;
    GetShapeScale(shape, sx, sy);
    int targetWidth = Mathf_RoundToInt(baseRoomSize * sx);
    int targetDepth = Mathf_RoundToInt(baseRoomSize * sy);

    int width = Mathf_Clamp(targetWidth, 6, Mathf_Max(6, maxWidth));
    int depth = Mathf_Clamp(targetDepth, 6, Mathf_Max(6, maxDepth));

    int minX = leafBounds.xMin() + margin;
    int minZ = leafBounds.zMin() + margin;
    int maxX = leafBounds.xMax() - margin - width;
    int maxZ = leafBounds.zMax() - margin - depth;

    int startX = (maxX >= minX) ? random.Next(minX, maxX + 1) : minX;
    int startZ = (maxZ >= minZ) ? random.Next(minZ, maxZ + 1) : minZ;

    return IntBounds(IntVec3(startX, 0, startZ), IntVec3(width, mapHeight, depth));
}

void DungeonGenerator::GetShapeScale(RoomShape shape, float& outX, float& outY)
{
    switch (shape)
    {
    case ROOM_SHAPE_SMALL:     outX = 0.6f; outY = 0.6f; break;
    case ROOM_SHAPE_LARGE:     outX = 1.4f; outY = 1.4f; break;
    case ROOM_SHAPE_LONG_WIDE: outX = 1.6f; outY = 0.8f; break;
    case ROOM_SHAPE_LONG_TALL: outX = 0.8f; outY = 1.6f; break;
    case ROOM_SHAPE_NORMAL:
    default:                   outX = 1.0f; outY = 1.0f; break;
    }
}

// ============================================================================
//  벽/천장 생성
// ============================================================================

void DungeonGenerator::BuildWallsAndCeiling()
{
    int ceilingY = Mathf_Max(1, mapHeight);

    // 바닥 → 천장 + 인접한 floor 아닌 칸에 벽
    for (const IntVec3& floor : floorTiles)
    {
        ceilingTiles.insert(IntVec3(floor.x, ceilingY, floor.z));

        for (const IntVec3& d : HorizontalDirections)
        {
            IntVec3 nf(floor.x + d.x, floor.y + d.y, floor.z + d.z);
            if (floorTiles.count(nf)) continue;

            for (int y = 0; y < ceilingY + 1; y++)
                wallTiles.insert(IntVec3(nf.x, y, nf.z));
        }
    }

    // 방 내부 blocked 타일도 벽으로
    for (const Room& room : rooms)
    {
        for (const IntVec3& bt : room.blockedTiles)
        {
            for (int y = 0; y < ceilingY + 1; y++)
                wallTiles.insert(IntVec3(bt.x, y, bt.z));
        }
    }

    // solid = floor + wall + ceiling (충돌/시야 검증 통합 집합)
    for (const IntVec3& t : floorTiles)   solidTiles.insert(t);
    for (const IntVec3& t : wallTiles)    solidTiles.insert(t);
    for (const IntVec3& t : ceilingTiles) solidTiles.insert(t);
}

// ============================================================================
//  좌표 변환 / 조회 (클라와 결과 일치 필수)
// ============================================================================

Vec3 DungeonGenerator::TileToWorld(const IntVec3& tile) const
{
    return Vec3(
        (float)(tile.x + worldOffset.x),
        (float)(tile.y + worldOffset.y),
        (float)(tile.z + worldOffset.z)
    );
}

Vec3 DungeonGenerator::TileToWorldCenter(const IntVec3& tile) const
{
    Vec3 v = TileToWorld(tile);
    return Vec3(v.x + 0.5f, v.y, v.z + 0.5f);
}

IntVec3 DungeonGenerator::WorldToTile(const Vec3& worldPos) const
{
    Vec3 local(worldPos.x - worldOffset.x,
        worldPos.y - worldOffset.y,
        worldPos.z - worldOffset.z);
    return IntVec3(Mathf_FloorToInt(local.x),
        Mathf_FloorToInt(local.y),
        Mathf_FloorToInt(local.z));
}

bool DungeonGenerator::IsFloorTile(const IntVec3& tile) const
{
    return floorTiles.count(tile) > 0;
}

bool DungeonGenerator::IsWallTile(const IntVec3& tile) const
{
    return wallTiles.count(tile) > 0;
}

bool DungeonGenerator::IsSolidTile(const IntVec3& tile) const
{
    return solidTiles.count(tile) > 0;
}

// ============================================================================
//  방 기반 AOI 지원 (Zone 방식)
// ============================================================================
int DungeonGenerator::RoomIdAt(const Vec3& worldPos) const
{
    IntVec3 t = WorldToTile(worldPos);
    // 모든 방의 bounds(사각형)를 검사. BSP라 방끼리 겹치지 않음.
    for (const Room& r : rooms)
    {
        if (t.x >= r.bounds.xMin() && t.x < r.bounds.xMax() &&
            t.z >= r.bounds.zMin() && t.z < r.bounds.zMax())
        {
            return r.id;
        }
    }
    return -1;      // 복도이거나 방 밖
}

bool DungeonGenerator::AreRoomsAdjacent(int roomA, int roomB) const
{
    if (roomA < 0 || roomB < 0) return false;
    if (roomA == roomB) return true;
    for (const Room& r : rooms)
    {
        if (r.id == roomA)
        {
            for (int n : r.neighbors)
                if (n == roomB) return true;
            return false;
        }
    }
    return false;
}

// ============================================================================
//  포탈 그래프 AOI (방+복도 노드, 1-hop 가시성)
// ============================================================================
void DungeonGenerator::BuildNodeGraph()
{
    tileToNode_.clear();    // 복도 타일만 보관 (방은 NodeIdAt에서 bounds로 판정)
    nodeAdj_.clear();

    // 1) 복도 타일 → 복도 노드. (방 타일은 넣지 않는다. 일부 방은 floorTiles가
    //    비어 있을 수 있어, 방 판정은 NodeIdAt에서 bounds로 처리한다.)
    //    복도끼리 교차(타일 공유)하면 두 복도를 인접 노드로 연결한다.
    for (size_t i = 0; i < corridors.size(); ++i)
    {
        int cnode = CORRIDOR_NODE_BASE + (int)i;
        for (const IntVec3& raw : corridors[i].floorTiles)
        {
            IntVec3 t(raw.x, 0, raw.z);
            auto existing = tileToNode_.find(t);
            if (existing != tileToNode_.end() &&
                existing->second >= CORRIDOR_NODE_BASE &&
                existing->second != cnode)
            {
                // 다른 복도와 교차 → 두 복도를 인접 노드로
                nodeAdj_[cnode].insert(existing->second);
                nodeAdj_[existing->second].insert(cnode);
            }
            tileToNode_[t] = cnode;     // 마지막 복도로 덮어쓰기(교차점 대표)
        }
    }

    // 2) 인접 관계: 복도 ↔ 그 복도가 연결하는 방들.
    //    (방↔방 neighbors는 "복도로 연결된 방"이므로 넣지 않는다.
    //     방에 있을 때 복도 건너 다른 방이 보이면 안 되기 때문.)
    for (size_t i = 0; i < corridors.size(); ++i)
    {
        int cnode = CORRIDOR_NODE_BASE + (int)i;
        for (int rid : corridors[i].connectedRoomIds)
        {
            nodeAdj_[cnode].insert(rid);
            nodeAdj_[rid].insert(cnode);
        }
    }
}

int DungeonGenerator::NodeIdAt(const Vec3& worldPos) const
{
    IntVec3 t = WorldToTile(worldPos);
    t.y = 0;

    // 1) 방 bounds 우선 (방 안이면 항상 그 방 노드).
    //    방-복도 경계의 복도 타일이 방 bounds 안에 있어도 "방"으로 분류된다.
    for (const Room& r : rooms)
    {
        if (t.x >= r.bounds.xMin() && t.x < r.bounds.xMax() &&
            t.z >= r.bounds.zMin() && t.z < r.bounds.zMax())
            return r.id;
    }

    // 2) 방 밖이면 복도 타일 조회
    auto it = tileToNode_.find(t);
    return (it != tileToNode_.end()) ? it->second : -1;
}

bool DungeonGenerator::AreNodesAdjacent(int nodeA, int nodeB) const
{
    if (nodeA < 0 || nodeB < 0) return false;
    if (nodeA == nodeB) return true;
    auto it = nodeAdj_.find(nodeA);
    if (it == nodeAdj_.end()) return false;
    return it->second.count(nodeB) > 0;
}