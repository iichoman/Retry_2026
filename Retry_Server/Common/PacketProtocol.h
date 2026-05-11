#pragma once
#include <cstdint>

// ============================================================================
//  Retry 패킷 프로토콜
//
//  모든 통신: [PacketHeader 8바이트] + [본문 N바이트]
//
//  서버 권위 모델 (Authoritative) -> 그냥 C/S 방식이라는 말:
//   - 클라는 입력/의도만 보내고, 결과(위치, 데미지, HP)는 서버가 결정해 통보
//   - 몬스터 AI 전체는 서버가 시뮬레이션. 클라는 받은 상태로 그리기만 함
//   - 던전은 시드 동기화 (서버/클라가 동일 알고리즘으로 각자 생성)
//
//  채널:
//   - 현재: 모두 TCP
//   - 추가: 위치/시야 갱신만 UDP로 분리 예정
//
//  시야 처리 (Interest Management):
//   - 각 클라는 자기 시야(VIEW_RANGE 반경) 안의 객체만 정보 받음
//   - ENTER/LEAVE 이벤트로 시야 변화 알림, MOVE로 갱신
//   - 현재: ViewList 방식 (모든 객체 순회 거리 체크, O(N²))
//   - 추가: + Sector 방식 (격자 기반 공간 분할, O(N))
// ============================================================================

#pragma pack(push, 1)

constexpr int MAX_SESSION_PLAYERS  = 30;
constexpr int MAX_PLAYER_NAME      = 32;
constexpr int MAX_ROOM_NAME        = 32;
constexpr int MAX_ROOM_LIST        = 50;
constexpr int MAX_FAIL_REASON      = 64;
constexpr int MAX_IP_STRING        = 16;

// ----------------------------------------------------------------------------
//  패킷 종류
// ----------------------------------------------------------------------------
enum class PacketType : int {
    // ── 로비 (Retry_Server, 포트 9000) ─────────────────────────
    LOGIN_REQUEST          = 1,
    LOGIN_RESULT           = 2,
    ROOM_CREATE_REQUEST    = 3,
    ROOM_CREATE_RESULT     = 4,
    ROOM_JOIN_REQUEST      = 5,
    ROOM_JOIN_RESULT       = 6,
    ROOM_LIST_REQUEST      = 7,
    ROOM_LIST_RESULT       = 8,
    GAME_START_REQUEST     = 9,
    SESSION_ASSIGN         = 10,

    // ── 인게임: 입력 / 위치 (Session_Manager, 포트 9001) ───────
    PLAYER_INPUT           = 20,   // C→S: 입력 + 자칭 위치 (50ms)
    PLAYER_ENTER_VIEW      = 21,   // S→C: 시야에 새 플레이어 등장
    PLAYER_LEAVE_VIEW      = 22,   // S→C: 시야에서 사라짐
    PLAYER_MOVE            = 23,   // S→C: 시야 안 플레이어 이동/애니

    // ── 인게임: 전투 ───────────────────────────────────────────
    PLAYER_ATTACK_REQUEST  = 30,   // C→S: 공격 의도
    COMBAT_EVENT           = 31,   // S→C: 공격 발생 + 결과
    PLAYER_DIED            = 32,   // S→C: 사망
    HP_CHANGED             = 33,   // S→C: HP 변경 (회복 등)

    // ── 인게임: 몬스터 (서버 권위 AI) ──────────────────────────
    MONSTER_ENTER_VIEW     = 40,
    MONSTER_LEAVE_VIEW     = 41,
    MONSTER_MOVE           = 42,
    MONSTER_ATTACK_EVENT   = 43,   // S→C: 몬스터가 플레이어 공격함
    MONSTER_DIED           = 44,

    // ── 탈출 / 세션 종료 ───────────────────────────────────────
    EXTRACTION_REQUEST     = 50,
    EXTRACTION_RESULT      = 51,
    SESSION_ENDED          = 52,

    // ── IPC (Retry_Server ↔ Session_Manager, 포트 9002) ───────
    IPC_CREATE_SESSION     = 100,
    IPC_SESSION_ENDED      = 101,
};

struct PacketHeader {
    PacketType type;
    int        size;     // 본문 크기 (헤더 제외)
};

// ----------------------------------------------------------------------------
//  로비 패킷 본문
// ----------------------------------------------------------------------------

// C→S: 로그인 요청 (DB 없이 이름만으로 식별)
struct LoginRequest {
    char playerName[MAX_PLAYER_NAME];
};

// S→C: 로그인 결과
struct LoginResult {
    int  success;                      // 1=성공, 0=실패
    int  clientId;                     // 서버가 할당한 본인 ID
    char failReason[MAX_FAIL_REASON];
};

// C→S: 방 만들기
struct RoomCreateRequest {
    char roomName[MAX_ROOM_NAME];
};

struct RoomCreateResult {
    int success;
    int roomId;
};

// C→S: 방 참가
struct RoomJoinRequest {
    int roomId;
};

struct RoomJoinResult {
    int  success;
    int  roomId;
    int  hostClientId;
    int  currentPlayers;
    int  maxPlayers;
    char failReason[MAX_FAIL_REASON];
};

// 방 목록 응답에 들어가는 1개 항목
struct RoomListEntry {
    int  roomId;
    int  hostClientId;
    int  currentPlayers;
    int  maxPlayers;
    char roomName[MAX_ROOM_NAME];
};

struct RoomListResult {
    int           count;
    RoomListEntry rooms[MAX_ROOM_LIST];
};

// 세션 할당 (방장의 GAME_START_REQUEST 응답으로 모든 멤버에게 송신)
struct SessionAssignData {
    int  sessionId;
    int  mapSeed;          // 클라가 동일 알고리즘으로 던전 생성에 사용
    char sessionServerIP[MAX_IP_STRING];
    int  sessionServerPort;
};

// ----------------------------------------------------------------------------
//  인게임: 입력 / 위치
// ----------------------------------------------------------------------------

// C→S: 본인의 입력 + 자칭 위치 (50ms 주기).
// Phase 1: 서버가 위치를 그대로 신뢰.
// Phase 2 이후: sanity check (속도 초과, 벽 통과 검증) 추가 예정.
struct PlayerInput {
    int       clientId;
    float     moveX, moveY;
    int       jump;
    int       sprint;
    float     cameraYaw;
    long long timestamp;
    float     posX, posY, posZ;
    float     rotY;
};

// S→C: 시야에 새 플레이어 등장
struct PlayerEnterView {
    int   clientId;
    char  playerName[MAX_PLAYER_NAME];
    float posX, posY, posZ;
    float rotY;
    int   hp;
    int   maxHp;
};

// S→C: 시야에서 사라짐 (먼 거리로 이동, 사망, 접속 종료 등)
struct PlayerLeaveView {
    int clientId;
};

// S→C: 시야 안 플레이어 이동/애니 갱신 (각 객체당 1패킷)
struct PlayerMove {
    int       clientId;
    float     posX, posY, posZ;
    float     rotY;
    float     speed;            // 애니 블렌드용 (0=정지, 양수=이동 속도)
    int       animState;        // 0=Idle, 1=Walk, 2=Run, 3=Attack...
    long long timestamp;
};

// ----------------------------------------------------------------------------
//  인게임: 전투
// ----------------------------------------------------------------------------

enum WeaponKind : int {
    WEAPON_SWORD     = 0,    // 한손검
    WEAPON_BIG_SWORD = 1,    // 양손검
    WEAPON_BOW       = 2,    // 활
    WEAPON_GUN       = 3,    // 총
};

// C→S: 공격 의도. 서버가 충돌 검사로 대상/데미지를 결정.
struct PlayerAttackRequest {
    int       weaponKind;
    int       comboIndex;
    float     originX, originY, originZ;   // 공격 시작 위치
    float     dirX, dirY, dirZ;            // 공격 방향(정규화 권장)
    long long timestamp;
};

// S→C: 공격 발생 (시야 안 모든 클라에게).
//      attackerId, targetId의 부호로 플레이어/몬스터 구분:
//      양수 = 플레이어 ID, 음수 = 몬스터 ID(부호 반전)
struct CombatEvent {
    int   attackerId;
    int   targetId;
    int   damage;
    int   weaponKind;
    int   comboIndex;
    int   targetHpAfter;
    int   isCritical;
};

struct PlayerDied {
    int victimId;
    int killerId;          // 양수=플레이어, 음수=몬스터, 0=환경/낙사
};

// HP 변화 (회복 아이템, 디버프 등 데미지 외 사유)
struct HpChanged {
    int targetId;
    int hp;
    int maxHp;
};

// ----------------------------------------------------------------------------
//  인게임: 몬스터 (서버 권위 AI)
// ----------------------------------------------------------------------------

enum MonsterKind : int {
    MONSTER_NORMAL = 0,
    MONSTER_ELITE  = 1,
    MONSTER_BOSS   = 2,
};

enum MonsterAiState : int {
    AI_IDLE   = 0,
    AI_PATROL = 1,
    AI_CHASE  = 2,
    AI_ATTACK = 3,
    AI_DEAD   = 4,
};

struct MonsterEnterView {
    int   monsterId;
    int   monsterKind;
    float posX, posY, posZ;
    float rotY;
    int   hp;
    int   maxHp;
};

struct MonsterLeaveView {
    int monsterId;
};

struct MonsterMove {
    int       monsterId;
    float     posX, posY, posZ;
    float     rotY;
    int       aiState;          // MonsterAiState
    int       targetClientId;   // 추격/공격 중인 플레이어 (없으면 0)
    long long timestamp;
};

// 몬스터 → 플레이어 공격 이벤트 (서버가 판정하고 통보)
struct MonsterAttackEvent {
    int monsterId;
    int victimClientId;
    int damage;
    int victimHpAfter;
};

struct MonsterDied {
    int monsterId;
    int killerId;          // 잡은 플레이어 ID (양수)
};

// ----------------------------------------------------------------------------
//  탈출 / 세션 종료
// ----------------------------------------------------------------------------

struct ExtractionRequest {
    int extractionPointId;     // 탈출 지점 ID (시작 방 N개 중 하나)
};

struct ExtractionResult {
    int success;
    int itemCount;             // 가지고 나간 아이템 수
};

struct SessionEnded {
    int reason;       // 0=정상 종료, 1=호스트 이탈, 2=오류, 3=타임아웃
};

// ----------------------------------------------------------------------------
//  IPC (Retry_Server → Session_Manager)
//
//  메인 서버가 GAME_START 요청을 받으면 세션 매니저에게 세션 생성 명령을 보냄.
//  세션 매니저는 세션을 만들고 정상 시작했음을 확인.
// ----------------------------------------------------------------------------

struct IpcCreateSession {
    int sessionId;
    int hostClientId;
    int mapSeed;
    int playerCount;
    int playerIds[MAX_SESSION_PLAYERS];
};

struct IpcSessionEnded {
    int sessionId;
    int reason;
    int totalPlayers;
    int survivors;
};

#pragma pack(pop)
