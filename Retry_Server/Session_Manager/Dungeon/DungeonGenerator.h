#pragma once
#include "DungeonTypes.h"
#include "CSharpRandom.h"

#include <vector>
#include <memory>
#include <unordered_set>
#include <unordered_map>

// ============================================================================
//  DungeonGenerator (서버 측)
//
//  C# DungeonGenerator_ChunkMesh.cs의 데이터 생성 부분을 그대로 포팅.
//  (메시/Material/GameObject 부분은 클라 전용이므로 생략)
//
//  사용:
//    DungeonGenerator gen;
//    gen.Generate(seed);
//    // gen.floorTiles, gen.wallTiles, gen.solidTiles 등 사용 가능
//    // gen.rooms, gen.corridors 사용 가능
//
//  좌표계:
//   - 격자 좌표 (IntVec3): 던전 알고리즘 내부에서 사용
//   - 월드 좌표 (Vec3): 클라의 위치값과 동일. centerMapAtOrigin 적용된 것.
//   - TileToWorld / WorldToTile 로 변환
//
//  주의: C# System.Random과 비트-호환 결과를 내야 시드 동기화 성립.
//        CSharpRandom 사용. std::mt19937 등 절대 사용 금지.
// ============================================================================

class DungeonGenerator
{
public:
    // 인스펙터 디폴트 값들과 동일 (DungeonGenerator_ChunkMesh.cs)
    int   mapSize = 750;
    int   mapHeight = 10;
    int   baseRoomSize = 50;
    int   targetRoomCount = 20;
    int   minLeafSize = 100;
    float specialRoomLayoutChance = 0.45f;
    int   minimumLayoutInset = 2;

    int   bossRoomCount = 1;
    int   bossRoomSizeX = 90;
    int   bossRoomSizeZ = 90;
    int   bossRoomConnectionCount = 1;
    int   bossRoomOverlapPadding = 4;

    int   corridorWidth = 10;

    int   startRoomEdgeMargin = 10;
    // 시작 방 thickness는 클라가 mapHeight를 넘기므로 별도 필드 없음 (Generate 참고)
    int   teamCount = 16;       // 클라 인스펙터와 일치 (1~16 범위)

    bool  createExitRoom = true;           // 클라 인스펙터와 반드시 일치! (RNG 소비 영향)
    bool  centerMapAtOrigin = true;        // 클라 인스펙터와 반드시 일치!

    // 결과 데이터 (Generate 호출 후 채워짐)
    std::vector<Room>                     rooms;
    std::vector<Corridor>                 corridors;
    std::vector<StartRoom>                assignedStartRooms;
    int                                   exitRoomId = -1;   // Exit로 지정된 방 id (-1=없음)
    std::unordered_set<IntVec3>           floorTiles;
    std::unordered_set<IntVec3>           wallTiles;
    std::unordered_set<IntVec3>           ceilingTiles;
    std::unordered_set<IntVec3>           solidTiles;
    IntVec3                               worldOffset;

    // 메인 진입점. 시드를 받아 던전 데이터 생성.
    void Generate(int seed);

    // 좌표 변환 (클라와 동일 결과여야 함).
    Vec3    TileToWorld(const IntVec3& tile) const;
    Vec3    TileToWorldCenter(const IntVec3& tile) const;
    IntVec3 WorldToTile(const Vec3& worldPos) const;

    bool IsFloorTile(const IntVec3& tile) const;
    bool IsWallTile(const IntVec3& tile)  const;
    bool IsSolidTile(const IntVec3& tile) const;

    // 방 기반 AOI 지원 (Zone 방식)
    // 월드 좌표가 속한 방의 id 반환. 복도/방 밖이면 -1.
    int  RoomIdAt(const Vec3& worldPos) const;
    // 두 방이 인접(neighbors)한지. 둘 중 하나라도 -1이면 false.
    bool AreRoomsAdjacent(int roomA, int roomB) const;

    // ── 포탈 그래프 AOI (방+복도 노드, 1-hop 가시성) ──
    // 노드 id 체계: 방 = roomId(0..N-1), 복도 = CORRIDOR_NODE_BASE + corridorIndex.
    static constexpr int CORRIDOR_NODE_BASE = 100000;

    // 월드 좌표가 속한 노드 id. 방이면 roomId, 복도면 CORRIDOR_NODE_BASE+idx, 미분류 -1.
    int  NodeIdAt(const Vec3& worldPos) const;
    // 두 노드가 같거나 포탈로 직접 연결(1-hop)되어 있는지.
    bool AreNodesAdjacent(int nodeA, int nodeB) const;
    // 복도 노드 여부 (디버그/로깅용).
    bool IsCorridorNode(int nodeId) const { return nodeId >= CORRIDOR_NODE_BASE; }

private:
    std::unique_ptr<BSPNode>  root;

    // 포탈 그래프 AOI 데이터 (Generate 끝에서 BuildNodeGraph가 채움)
    std::unordered_map<IntVec3, int>                 tileToNode_;   // 타일 → 노드 id
    std::unordered_map<int, std::unordered_set<int>> nodeAdj_;      // 노드 → 인접 노드들
    void  BuildNodeGraph();

    // BSP / 방 생성
    void  SplitToTarget(BSPNode* node, int targetCount, int minSize, CSharpRandom& random);
    void  BuildRooms(BSPNode* node, CSharpRandom& random);
    void  BuildBossRooms(CSharpRandom& random);
    void  AssignExitRoom(CSharpRandom& random);

    std::vector<IntBounds> CreateBossRoomBounds(int count);
    IntBounds              CreateCenteredBounds(int centerX, int centerZ, int width, int depth);
    int                    GetNextRoomId();
    void                   RemoveRoomsOverlapping(const IntBounds& bounds, int padding);
    void                   ClearRoomFromBsp(BSPNode* node, const Room* room);
    static IntBounds       ExpandBoundsXZ(const IntBounds& bounds, int padding);
    static bool            OverlapsXZ(const IntBounds& a, const IntBounds& b);

    // 복도 연결
    void  BuildCorridors(BSPNode* node, CSharpRandom& random);
    void  AddNeighbor(Room* a, Room* b);
    Corridor CreateCorridor(IntVec3 start, IntVec3 end, CSharpRandom& random);
    void  ConnectStartRoomsToDungeon(CSharpRandom& random);
    void  ConnectBossRoomsToDungeon(CSharpRandom& random);
    Room* FindNearestRoom(const Vec3& startCenter);
    std::vector<Room*> FindNearestRooms(const Vec3& startCenter, int count, const Room* excludedRoom);
    void  AddHorizontalCorridor(std::unordered_set<IntVec3>& dst, int x1, int x2, int z);
    void  AddVerticalCorridor(std::unordered_set<IntVec3>& dst, int z1, int z2, int x);

    // 타일 수집
    void  AddRoomTiles();
    void  AddStartRoomTiles();
    void  AddCorridorTiles();
    void  AddBoundsTiles(const IntBounds& bounds);
    void  AddRoomFloorTiles(const Room& room);

    // 방 내부 레이아웃
    RoomLayoutType PickRoomLayout(const IntBounds& roomBounds, CSharpRandom& random);
    void  GenerateRoomLayout(Room& room, CSharpRandom& random);
    void  FillRoomInterior(std::unordered_set<IntVec3>& dst, const IntBounds& bounds);
    void  ApplyFourPillarsLayout(Room& room, CSharpRandom& random);
    void  ApplyCenterBlockLayout(Room& room, CSharpRandom& random);
    void  BlockArea(Room& room, const IntBounds& area);
    IntVec3 GetRoomConnectionPoint(const Room& room, const IntVec3& target);
    static IntVec2 GetInteriorSize(const IntBounds& bounds);
    IntVec3 CreateInteriorOrigin(const IntBounds& bounds, int localX, int localZ, IntVec2 size);
    IntVec3 CreateInteriorOriginFromCenter(const IntBounds& bounds, int centerX, int centerZ, IntVec2 size);
    IntVec2 CalculateFourPillarSize(IntVec2 interiorSize);
    IntVec2 CalculateQuarterCenters(int axisSize);
    static int NextInclusive(CSharpRandom& random, int minInclusive, int maxInclusive);

    // 방 모양/크기
    RoomShape  PickRoomShape(CSharpRandom& random);
    IntBounds  CreateRoomBounds(const IntBounds& leafBounds, RoomShape shape, CSharpRandom& random);
    static void GetShapeScale(RoomShape shape, float& outX, float& outY);

    // 벽/천장
    void  BuildWallsAndCeiling();

    // 시작 방 (StartRoomManager.cs 1:1 포팅)
    std::vector<StartRoom> BuildStartRoomCandidates(int mapSizeArg, int roomSize,
        int edgeMargin, int thickness);
    std::vector<int>       PickStartSlots(int teamCountArg, int seed);
    void                   AssignTeams(std::vector<StartRoom>& candidates,
        int teamCountArg, int seed);
    static StartRoom       CreateStartRoom(int slotIndex, IntVec3 position,
        int roomSize, int thickness, float yawDegrees);
    static std::vector<Vec3> BuildPlayerSpawnPositions(const Vec3& anchor,
        float yawDegrees, int roomSize);
};