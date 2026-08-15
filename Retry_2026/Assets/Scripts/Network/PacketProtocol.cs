using System;
using System.Runtime.InteropServices;

// ============================================================================
//  Retry 패킷 프로토콜 (클라 측)
//  서버의 Common/PacketProtocol.h와 1:1 대응. 필드 순서/타입 절대 변경 금지.
//
//  사용:
//    byte[] body = PacketIO.StructToBytes(packetStruct);
//    var packet = PacketIO.BytesToStruct<PlayerInput>(body, 0);
// ============================================================================

public enum PacketType : int
{
    // 로비
    LOGIN_REQUEST = 1,
    LOGIN_RESULT = 2,
    ROOM_CREATE_REQUEST = 3,
    ROOM_CREATE_RESULT = 4,
    ROOM_JOIN_REQUEST = 5,
    ROOM_JOIN_RESULT = 6,
    ROOM_LIST_REQUEST = 7,
    ROOM_LIST_RESULT = 8,
    GAME_START_REQUEST = 9,
    SESSION_ASSIGN = 10,

    // 인게임 - 입력 / 위치
    PLAYER_INPUT = 20,
    PLAYER_ENTER_VIEW = 21,
    PLAYER_LEAVE_VIEW = 22,
    PLAYER_MOVE = 23,

    // 전투
    PLAYER_ATTACK_REQUEST = 30,
    COMBAT_EVENT = 31,
    PLAYER_DIED = 32,
    HP_CHANGED = 33,
    PLAYER_ATTACK_BROADCAST = 34,

    // 몬스터
    MONSTER_ENTER_VIEW = 40,
    MONSTER_LEAVE_VIEW = 41,
    MONSTER_MOVE = 42,
    MONSTER_ATTACK_EVENT = 43,
    MONSTER_DIED = 44,

    // 전투 - 원거리 투사체 (활/총)
    PROJECTILE_SPAWN = 45,
    PROJECTILE_MOVE = 46,
    PROJECTILE_DESPAWN = 47,

    // 탈출
    EXTRACTION_REQUEST = 50,
    EXTRACTION_RESULT = 51,
    SESSION_ENDED = 52,
    PLAYER_EXTRACTED = 53,

    ITEM_PICKUP_REQUEST = 60,
    ITEM_PICKUP_RESULT = 61,
    LOOT_SPAWN = 62,
    LOOT_REMOVED = 63,
    INVENTORY_SYNC = 64,

    DEBUG_TELEPORT_EXIT = 90,   // [치트] 배포 시 제거
}

public enum WeaponKind : int
{
    SWORD = 0,
    BIG_SWORD = 1,
    BOW = 2,
    GUN = 3,
}

public enum MonsterKind : int
{
    NORMAL = 0,
    ELITE = 1,
    BOSS = 2,
}

public enum MonsterAiState : int
{
    IDLE = 0,
    PATROL = 1,
    CHASE = 2,
    ATTACK = 3,
    DEAD = 4,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PacketHeader
{
    public PacketType type;
    public int size;
}

// ── 로비 ──────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LoginRequest
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] playerName;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LoginResult
{
    public int success;
    public int clientId;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string failReason;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomCreateRequest
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] roomName;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomCreateResult
{
    public int success;
    public int roomId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomJoinRequest
{
    public int roomId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomJoinResult
{
    public int success;
    public int roomId;
    public int hostClientId;
    public int currentPlayers;
    public int maxPlayers;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string failReason;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SessionAssignData
{
    public int sessionId;
    public int mapSeed;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
    public string sessionServerIP;
    public int sessionServerPort;
}

// ── 인게임: 입력 / 위치 ──────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerInput
{
    public int clientId;
    public float moveX, moveY;
    public int jump;
    public int sprint;
    public long timestamp;        // C++의 long long = C#의 long (8 bytes)
    public float posX, posY, posZ;
    public float rotY;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerEnterView
{
    public int clientId;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string playerName;
    public float posX, posY, posZ;
    public float rotY;
    public int hp;
    public int maxHp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerLeaveView
{
    public int clientId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerMove
{
    public int clientId;
    public float posX, posY, posZ;
    public float rotY;
    public float speed;
    public int animState;
    public long timestamp;
}

// ── 전투 ──────────────────────────────────────────────────────────────────

// ── 아이템 / 인벤토리 (서버 권위) ────────────────────────────
// 인벤토리 실체는 서버에만 있다. 클라는 INVENTORY_SYNC로 받아 표시만 한다.
public static class LootConst
{
    public const int MAX_LOOT_ENTRIES = 8;
    public const int MAX_INVENTORY_ENTRIES = 32;
    public const float PICKUP_RANGE = 3.0f;   // 서버 PacketProtocol.h와 일치 필수
}

public enum PickupFailReason : int
{
    OK = 0,
    NoLoot = 1,      // 컨테이너 없음 (이미 소멸)
    TooFar = 2,      // 거리 초과
    NoItem = 3,      // 그 아이템/수량 없음
    InvFull = 4,     // 인벤토리 가득 참
    Dead = 5,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ItemStack
{
    public int itemHash;
    public int count;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ItemPickupRequest
{
    public int lootId;
    public int itemHash;
    public int count;      // 0 이하 = 전량 요청
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ItemPickupResult
{
    public int success;
    public int lootId;
    public int itemHash;
    public int grantedCount;
    public int failReason;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LootSpawnData
{
    public int lootId;
    public int sourceMonsterId;
    public float posX, posY, posZ;
    public int entryCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public ItemStack[] entries;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LootRemovedData
{
    public int lootId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InventorySyncData
{
    public int entryCount;
    public int totalCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public ItemStack[] entries;
}

// ── 탈출 (서버 권위 판정) ─────────────────────────────────────
// 탈출 성립 조건: 탈출 방 안에서 HOLD_SEC 연속 체류.
// 서버가 자체 위치 기록으로 검증하므로 클라 홀드 시간은 UI 용도다.
public static class ExtractionConst
{
    public const float HOLD_SEC = 7.0f;   // 서버 PacketProtocol.h와 일치 필수
}

public enum ExtractionFailReason : int
{
    OK = 0,
    NotInZone = 1,      // 탈출 방 안이 아님
    HoldTooShort = 2,   // 체류 시간 부족
    Dead = 3,
    Already = 4,
    NoExitRoom = 5,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ExtractionRequest
{
    public int extractionPointId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ExtractionResult
{
    public int success;
    public int itemCount;
    public int failReason;
    public float heldSec;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerExtracted
{
    public int clientId;
    public int remainingPlayers;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SessionEnded
{
    public int reason;   // 0=정상, 1=호스트 이탈, 2=오류, 3=타임아웃
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerAttackRequest
{
    public int weaponKind;
    public int comboIndex;
    public float originX, originY, originZ;
    public float dirX, dirY, dirZ;
    public long timestamp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerAttackBroadcast
{
    public int attackerId;
    public int weaponKind;
    public int comboIndex;
    public float originX, originY, originZ;
    public float dirX, dirY, dirZ;
    public long timestamp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CombatEvent
{
    public int attackerId;
    public int targetId;
    public int damage;
    public int weaponKind;
    public int comboIndex;
    public int targetHpAfter;
    public int isCritical;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerDied
{
    public int victimId;
    public int killerId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HpChanged
{
    public int targetId;
    public int hp;
    public int maxHp;
}

// ── 몬스터 ────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MonsterEnterView
{
    public int monsterId;
    public int monsterKind;
    public float posX, posY, posZ;
    public float rotY;
    public int hp;
    public int maxHp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MonsterLeaveView
{
    public int monsterId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MonsterMove
{
    public int monsterId;
    public float posX, posY, posZ;
    public float rotY;
    public int aiState;
    public int targetClientId;
    public long timestamp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MonsterAttackEvent
{
    public int monsterId;
    public int victimClientId;
    public int damage;
    public int victimHpAfter;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MonsterDied
{
    public int monsterId;
    public int killerId;
}

// ── 원거리 투사체 (활/총). 미쿠가 던지는 직육면체. ──
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProjectileSpawn
{
    public int projectileId;
    public int ownerId;
    public int weaponKind;
    public float posX, posY, posZ;
    public float dirX, dirY, dirZ;
    public float speed;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProjectileMove
{
    public int projectileId;
    public float posX, posY, posZ;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProjectileDespawn
{
    public int projectileId;
    public int hitType;        // 0=벽,1=몬스터,2=플레이어,3=수명
    public int hitTargetId;    // 몬스터=음수, 플레이어=양수, 벽/수명=0
    public float posX, posY, posZ;
}

// ── 마샬링 헬퍼 ───────────────────────────────────────────────────────────

public static class PacketIO
{
    public static T BytesToStruct<T>(byte[] buf, int offset) where T : struct
    {
        if (buf == null) throw new ArgumentNullException(nameof(buf));
        int size = Marshal.SizeOf<T>();
        if (offset + size > buf.Length)
            throw new ArgumentException($"버퍼 크기 부족: need {offset + size}, got {buf.Length}");

        GCHandle handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            IntPtr ptr = IntPtr.Add(handle.AddrOfPinnedObject(), offset);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally { handle.Free(); }
    }

    public static byte[] StructToBytes<T>(T obj) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buf = new byte[size];
        GCHandle handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(obj, handle.AddrOfPinnedObject(), false);
        }
        finally { handle.Free(); }
        return buf;
    }

    // 헤더+본문을 한 번에 합쳐 송신할 byte[] 만들기
    public static byte[] MakePacket<T>(PacketType type, T body) where T : struct
    {
        byte[] bodyBytes = StructToBytes(body);
        return MakePacket(type, bodyBytes);
    }

    public static byte[] MakePacket(PacketType type, byte[] body)
    {
        int bodySize = body?.Length ?? 0;
        var header = new PacketHeader { type = type, size = bodySize };
        byte[] headerBytes = StructToBytes(header);

        byte[] result = new byte[headerBytes.Length + bodySize];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        if (bodySize > 0)
            Buffer.BlockCopy(body, 0, result, headerBytes.Length, bodySize);
        return result;
    }

    // 본문 없는 빈 요청 패킷 (ROOM_LIST_REQUEST, GAME_START_REQUEST 등)
    public static byte[] MakeEmptyPacket(PacketType type)
    {
        return MakePacket(type, (byte[])null);
    }
}