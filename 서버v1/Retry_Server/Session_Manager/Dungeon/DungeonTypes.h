#pragma once
#include "../../Common/MathTypes.h"
#include <unordered_set>
#include <vector>
#include <memory>

// ============================================================================
//  DungeonTypes
//
//  C# 측 BSPNode/Room/Corridor 와 1:1 대응되는 C++ 자료구조.
//  서버는 시각 메시 생성을 안 하므로 메시 관련 필드는 빠져 있다.
//
//  좌표계: Vector3Int → IntVec3 (격자 좌표).
//          BoundsInt → IntBounds (격자 영역).
//          서버는 Y를 거의 안 쓰지만 (mapHeight=10 고정), 클라와 데이터
//          정합성을 위해 그대로 보관.
// ============================================================================

// ----------------------------------------------------------------------------
//  IntBounds = Unity의 BoundsInt 대응
//  position(min) + size 형식. Unity 와 동일.
// ----------------------------------------------------------------------------
struct IntBounds
{
    IntVec3 position;     // min corner
    IntVec3 size;

    IntBounds() = default;
    IntBounds(IntVec3 pos, IntVec3 sz) : position(pos), size(sz) {}

    int xMin() const { return position.x; }
    int yMin() const { return position.y; }
    int zMin() const { return position.z; }
    int xMax() const { return position.x + size.x; }
    int yMax() const { return position.y + size.y; }
    int zMax() const { return position.z + size.z; }

    // Unity BoundsInt.center 와 동일 (float)
    Vec3 center() const
    {
        return Vec3(
            position.x + size.x * 0.5f,
            position.y + size.y * 0.5f,
            position.z + size.z * 0.5f
        );
    }
};

// ----------------------------------------------------------------------------
//  Room - C#의 Room.cs 1:1 대응
// ----------------------------------------------------------------------------
enum RoomType : int {
    ROOM_TYPE_NORMAL  = 0,
    ROOM_TYPE_BOSS    = 1,
    ROOM_TYPE_REWARD  = 2,
    ROOM_TYPE_EXIT    = 3,
    ROOM_TYPE_START   = 4,
};

enum RoomShape : int {
    ROOM_SHAPE_SMALL     = 0,
    ROOM_SHAPE_NORMAL    = 1,
    ROOM_SHAPE_LARGE     = 2,
    ROOM_SHAPE_LONG_WIDE = 3,
    ROOM_SHAPE_LONG_TALL = 4,
};

enum RoomLayoutType : int {
    LAYOUT_OPEN          = 0,
    LAYOUT_FOUR_PILLARS  = 1,
    LAYOUT_CENTER_BLOCK  = 2,
};

struct Room
{
    int                          id;
    RoomType                     type;
    RoomShape                    shape;
    RoomLayoutType               layoutType;
    IntBounds                    bounds;
    std::vector<int>             neighbors;             // 인접 room id
    std::unordered_set<IntVec3>  floorTiles;            // 실내 바닥 타일
    std::unordered_set<IntVec3>  blockedTiles;          // 기둥/장애물 (실내 막힘)
    std::unordered_set<IntVec3>  doorwayFloorTiles;     // 출입구 타일

    Room() : id(0), type(ROOM_TYPE_NORMAL), shape(ROOM_SHAPE_NORMAL),
             layoutType(LAYOUT_OPEN) {}
};

// ----------------------------------------------------------------------------
//  Corridor - C#의 Corridor.cs 1:1 대응
// ----------------------------------------------------------------------------
struct Corridor
{
    int                          id;
    std::unordered_set<IntVec3>  floorTiles;
    std::vector<int>             connectedRoomIds;

    Corridor() : id(0) {}
};

// ----------------------------------------------------------------------------
//  StartRoom - C#의 StartRoom.cs / StartRoomManager.cs와 1:1 대응.
//  PlayersPerTeam=3 기준 3개의 spawn 위치를 미리 계산해 보관.
// ----------------------------------------------------------------------------
struct StartRoom
{
    int                slotIndex;            // 0~15 (4면 × 4슬롯)
    int                teamId;               // -1 = 미배정
    bool               occupied;
    IntBounds          bounds;
    Vec3               teamAnchorPosition;   // 방 중심 + spawn 높이
    float              spawnYawDegrees;      // Unity Quaternion.Euler(0, yaw, 0) 의 yaw
    std::vector<Vec3>  playerSpawnPositions; // 팀원 3명의 정확한 스폰 좌표

    StartRoom() : slotIndex(-1), teamId(-1), occupied(false), spawnYawDegrees(0.f) {}
};

static constexpr int PLAYERS_PER_TEAM = 3;

// ----------------------------------------------------------------------------
//  BSPNode - C#의 BSPNode.cs 1:1 대응.
//  unique_ptr로 자식 노드 소유.
// ----------------------------------------------------------------------------
class CSharpRandom;     // forward

class BSPNode
{
public:
    IntBounds                  bounds;
    std::unique_ptr<BSPNode>   left;
    std::unique_ptr<BSPNode>   right;
    IntBounds                  roomBounds;
    bool                       hasRoom;
    Room*                      roomData;       // 약한 참조 (DungeonGenerator의 rooms vector 소유)

    explicit BSPNode(const IntBounds& b)
        : bounds(b), hasRoom(false), roomData(nullptr) {}

    bool IsLeaf() const { return !left && !right; }

    // C#의 TrySplit과 동일 동작.
    // aspectBias = 1.25f가 디폴트.
    bool TrySplit(CSharpRandom& random, int minLeafSize, float aspectBias = 1.25f);

    // 모든 leaf 노드를 leaves에 수집
    void CollectLeaves(std::vector<BSPNode*>& leaves);

    // 가장 안쪽 방의 중심 격자 좌표 반환
    IntVec3 GetRoomCenter() const;

    // 가장 안쪽 방의 Room* 반환 (재귀)
    Room* GetRoomData() const;
};
