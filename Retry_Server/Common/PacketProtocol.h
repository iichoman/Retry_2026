#pragma once
#include <cstdint>

// ============================================================================
//  Retry 패킷 프로토콜
//
//  모든 통신: [PacketHeader 8바이트] + [본문 N바이트]
//
//  서버 권위 모델 (Authoritative):
//   - 클라는 입력/의도만 보내고, 결과(위치, 데미지, HP)는 서버가 결정해 통보
//   - 몬스터 AI 전체는 서버가 시뮬레이션. 클라는 받은 상태로 그리기만 함
//   - 던전은 시드 동기화 (서버/클라가 동일 알고리즘으로 각자 생성)
//
//  채널:
//   - Phase 1: 모두 TCP
//   - Phase 2: 위치/시야 갱신만 UDP로 분리 예정
//
//  시야 처리 (Interest Management):
//   - 각 클라는 자기 시야(VIEW_RANGE 반경) 안의 객체만 정보 받음
//   - ENTER/LEAVE 이벤트로 시야 변화 알림, MOVE로 갱신
//   - Phase 1: ViewList 방식 (모든 객체 순회 거리 체크, O(N²))
//   - Phase 2: + Sector 방식 (격자 기반 공간 분할, O(N))
// ============================================================================

#pragma pack(push, 1)

constexpr int MAX_SESSION_PLAYERS = 30;
constexpr int MAX_TEAMS = 10;   // 로비 팀 슬롯 (5x2 그리드)
constexpr int TEAM_CAPACITY = 3;    // 한 팀 최대 인원 (MAX_TEAMS * TEAM_CAPACITY = MAX_SESSION_PLAYERS)
constexpr int TEAM_UNASSIGNED = -1;  // 미배정
constexpr int MAX_PLAYER_NAME = 32;
constexpr int MAX_ROOM_NAME = 32;
constexpr int MAX_ROOM_LIST = 50;
constexpr int MAX_FAIL_REASON = 64;
constexpr int MAX_PASSWORD = 64;    // 비밀번호(UTF-8 바이트)
constexpr int MAX_IP_STRING = 16;
constexpr int MAX_CHAT_TEXT = 128;   // 채팅 1줄 최대 바이트 (UTF-8, 널 포함)

// ----------------------------------------------------------------------------
//  채팅 채널
//
//  로비  : ALL(전체 접속자) / PARTY(같은 방)
//  세션  : PARTY(같은 세션 전원) / TEAM(같은 팀)
//  로비에 TEAM은 없고, 세션에 ALL은 없다. 서버가 문맥에 맞게 검사한다.
// ----------------------------------------------------------------------------
enum ChatChannel : int {
    CHAT_ALL    = 0,   // 로비 전용
    CHAT_PARTY  = 1,   // 로비=방, 세션=세션 전원
    CHAT_TEAM   = 2,   // 세션 전용
    CHAT_SYSTEM = 3,   // S→C 전용. 입퇴장/경고 등 서버가 만든 알림
};

// ----------------------------------------------------------------------------
//  패킷 종류
// ----------------------------------------------------------------------------
enum class PacketType : int {
    // ── 로비 (Retry_Server, 포트 9000) ─────────────────────────
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
    ROOM_LEAVE_REQUEST = 11,        // C→S: 현재 방(파티) 나가기
    ROOM_LEAVE_RESULT = 12,         // S→C: 나가기 결과 (나간 본인에게)
    ROOM_SELECT_TEAM_REQUEST = 13,  // C→S: 팀 슬롯 선택 (0..MAX_TEAMS-1, -1=미배정)
    ROOM_STATE = 14,                // S→C: 방 멤버/팀 현황 (변경 시 전원에게 push)

    // ── 채팅 (로비/세션 양쪽에서 동일 패킷 사용) ───────────────
    CHAT_SEND = 15,                 // C→S: 채팅 전송 (채널 + 본문)
    CHAT_MESSAGE = 16,              // S→C: 채팅 수신 (발신자 정보 포함)
    ROOM_READY_REQUEST = 17,        // C→S: 준비 상태 토글 (방장 제외)
    GAME_START_NOTICE = 18,         // S→C: 방 전원에게 "던전 생성 시작" 공지(본문 없음)
    SESSION_START_GAME = 19,        // S→C: 전원 입장 완료 → 동시 시작(본문 없음)

    // ── 계정 (로비, 100번대) ───────────────────────────────────
    REGISTER_REQUEST = 100,         // C→S: 회원가입
    REGISTER_RESULT = 101,
    RESET_PW_REQUEST = 102,         // C→S: 비밀번호 재설정
    RESET_PW_RESULT = 103,
    PLAYER_STATS_REQUEST = 104,     // C→S: 내 전적 조회
    PLAYER_STATS_RESULT = 105,

    // ── 인게임: 입력 / 위치 (Session_Manager, 포트 9001) ───────
    PLAYER_INPUT = 20,   // C→S: 입력 + 자칭 위치 (50ms)
    PLAYER_ENTER_VIEW = 21,   // S→C: 시야에 새 플레이어 등장
    PLAYER_LEAVE_VIEW = 22,   // S→C: 시야에서 사라짐
    PLAYER_MOVE = 23,   // S→C: 시야 안 플레이어 이동/애니

    // ── 인게임: 전투 ───────────────────────────────────────────
    PLAYER_ATTACK_REQUEST = 30,   // C→S: 공격 의도
    COMBAT_EVENT = 31,   // S→C: 공격 명중 + 데미지 결과
    PLAYER_DIED = 32,   // S→C: 사망
    HP_CHANGED = 33,   // S→C: HP 변경 (회복 등)
    PLAYER_ATTACK_BROADCAST = 34,  // S→C: 다른 플레이어 공격 액션 (애니용, 빗나가도 송신)

    // ── 인게임: 몬스터 (서버 권위 AI) ──────────────────────────
    MONSTER_ENTER_VIEW = 40,
    MONSTER_LEAVE_VIEW = 41,
    MONSTER_MOVE = 42,
    MONSTER_ATTACK_EVENT = 43,   // S→C: 몬스터가 플레이어 공격함
    MONSTER_DIED = 44,

    // 전투 - 원거리 투사체 (활/총)
    PROJECTILE_SPAWN = 45,   // S→C: 투사체 생성 (직육면체 발사)
    PROJECTILE_MOVE = 46,   // S→C: 투사체 위치 갱신 (매 틱)
    PROJECTILE_DESPAWN = 47,   // S→C: 투사체 소멸 (벽/대상 명중 또는 수명)

    // ── 탈출 / 세션 종료 ───────────────────────────────────────
    EXTRACTION_REQUEST = 50,
    EXTRACTION_RESULT = 51,
    SESSION_ENDED = 52,
    PLAYER_EXTRACTED = 53,   // S→C: 다른 플레이어 탈출 알림 (결과창/킬로그용)

    // ── 아이템 / 인벤토리 (서버 권위) ──────────────────────────
    ITEM_PICKUP_REQUEST = 60,   // C→S: 루팅 의도 (아이템/수량 지정)
    ITEM_PICKUP_RESULT = 61,   // S→C: 루팅 판정 결과 (본인에게만)
    LOOT_SPAWN = 62,   // S→C: 전리품 컨테이너 생성 (몬스터 사망 등)
    LOOT_REMOVED = 63,   // S→C: 컨테이너 소멸 (내용물 소진)
    INVENTORY_SYNC = 64,   // S→C: 인벤토리 전체 동기화 (권위 상태)

    // ── [디버그 치트] 배포 시 제거 ─────────────────────────────
    DEBUG_TELEPORT_EXIT = 90,   // C→S: 탈출 방으로 이동 요청
    DEBUG_TELEPORT_BOSS = 91,   // C→S: 보스 방으로 이동 요청

    // ── IPC (Retry_Server ↔ Session_Manager, 포트 9002) ───────
    IPC_CREATE_SESSION = 100,
    IPC_SESSION_ENDED = 101,
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
    char password[MAX_PASSWORD];
    // 인게임에 들어가면 클라가 로비 연결을 끊는다. 그 상태에서 같은 닉네임이
    // 비어 보이면 남이 가로챌 수 있으므로 서버는 그 이름을 잠가 둔다.
    // 잠긴 이름의 주인이 로비로 돌아올 때는 자기 clientId를 같이 보내 증명한다.
    // 0 = 새 로그인.
    int  reconnectClientId;
};

// S→C: 로그인 결과
struct LoginResult {
    int  success;                      // 1=성공, 0=실패
    int  clientId;                     // 서버가 할당한 본인 ID
    char failReason[MAX_FAIL_REASON];
};

// C→S: 회원가입 / 비밀번호 재설정 (같은 모양)
struct AccountRequest {
    char playerName[MAX_PLAYER_NAME];
    char password[MAX_PASSWORD];
};

// S→C: 회원가입 / 비밀번호 재설정 결과
struct AccountResult {
    int  success;                      // 1=성공
    char failReason[MAX_FAIL_REASON];
};

// S→C: 내 전적
struct PlayerStatsResult {
    int success;
    int totalRuns;
    int totalExtractions;
    int monsterKills;
    int playerKills;
    int bestSeconds;
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

// C→S: 팀 슬롯 선택
struct RoomSelectTeamRequest {
    int teamId;            // 0 .. MAX_TEAMS-1, 또는 TEAM_UNASSIGNED(-1)
};

// S→C: 나가기 결과 (나간 본인에게만)
struct RoomLeaveResult {
    int success;
};

// 방 멤버 1명 (ROOM_STATE 항목)
struct RoomMemberEntry {
    int  clientId;
    int  teamId;           // -1=미배정, 0..MAX_TEAMS-1
    int  isHost;           // 1=방장
    int  isReady;          // 1=준비 완료. 방장은 항상 1로 취급한다.
    char playerName[MAX_PLAYER_NAME];
};

// S→C: 방(파티) 전체 현황. 입장/퇴장/팀변경/방장변경 시 전원에게 push.
struct RoomStateData {
    int             roomId;
    int             hostClientId;
    int             memberCount;
    RoomMemberEntry members[MAX_SESSION_PLAYERS];
};

// ----------------------------------------------------------------------------
//  채팅
// ----------------------------------------------------------------------------

// C→S: 채팅 전송. 서버가 채널 유효성과 대상 범위를 결정한다.
struct ChatSendRequest {
    int  channel;                  // ChatChannel
    char text[MAX_CHAT_TEXT];
};

// S→C: 채팅 수신. 이름/팀은 서버가 채워 넣는다(클라 위조 방지).
struct ChatMessageData {
    int  channel;                  // ChatChannel
    int  senderId;
    int  senderTeam;               // -1 = 미배정/해당 없음
    char senderName[MAX_PLAYER_NAME];
    char text[MAX_CHAT_TEXT];
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
    WEAPON_SWORD = 0,    // 한손검
    WEAPON_BIG_SWORD = 1,    // 양손검
    WEAPON_BOW = 2,    // 활
    WEAPON_GUN = 3,    // 총
};

// C→S: 공격 의도. 서버가 충돌 검사로 대상/데미지를 결정.
struct PlayerAttackRequest {
    int       weaponKind;
    int       comboIndex;
    float     originX, originY, originZ;   // 공격 시작 위치
    float     dirX, dirY, dirZ;            // 공격 방향(정규화 권장)
    long long timestamp;
};

// S→C: 공격 액션 알림. 빗나가도 항상 송신. 다른 클라들의 액션 애니용.
struct PlayerAttackBroadcast {
    int       attackerId;
    int       weaponKind;
    int       comboIndex;
    float     originX, originY, originZ;
    float     dirX, dirY, dirZ;
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

// ── 원거리 투사체 (활/총). 미쿠가 던지는 직육면체. ──
// S→C: 투사체 생성. 클라는 이 패킷으로 직육면체를 월드에 띄운다.
struct ProjectileSpawn {
    int   projectileId;
    int   ownerId;        // 발사한 플레이어
    int   weaponKind;     // WEAPON_BOW / WEAPON_GUN
    float posX, posY, posZ;   // 시작 위치
    float dirX, dirY, dirZ;   // 진행 방향(정규화)
    float speed;              // m/s
};

// S→C: 투사체 위치 갱신 (매 틱). 클라는 직육면체를 이 위치로 이동.
struct ProjectileMove {
    int   projectileId;
    float posX, posY, posZ;
};

// S→C: 투사체 소멸. 클라는 직육면체를 제거하고 미쿠 손에 초기화.
struct ProjectileDespawn {
    int   projectileId;
    int   hitType;        // 0=벽, 1=몬스터, 2=플레이어, 3=수명(사거리 초과)
    int   hitTargetId;    // 몬스터=음수, 플레이어=양수, 벽/수명=0
    float posX, posY, posZ;   // 소멸(명중) 위치
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
    MONSTER_ELITE = 1,
    MONSTER_BOSS = 2,
};

enum MonsterAiState : int {
    AI_IDLE = 0,
    AI_PATROL = 1,
    AI_CHASE = 2,
    AI_ATTACK = 3,
    AI_DEAD = 4,
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

// 탈출 성립에 필요한 연속 체류 시간(초). 클라 ExitPortal.holdDuration과 일치해야 함.
constexpr float EXTRACTION_HOLD_SEC = 3.0f;   // 클라 ExitPotal.prefab의 holdDuration과 같아야 한다
// 서버 판정 관용치. 네트워크 지연으로 클라가 살짝 먼저 요청해도 통과시킴.
constexpr float EXTRACTION_HOLD_TOLERANCE = 0.9f;

// 탈출 실패 사유 (ExtractionResult.failReason)
enum ExtractionFailReason : int {
    EXTRACT_OK = 0,
    EXTRACT_FAIL_NOT_IN_ZONE = 1,   // 탈출 방 안이 아님
    EXTRACT_FAIL_HOLD_TOO_SHORT = 2,   // 체류 시간 부족
    EXTRACT_FAIL_DEAD = 3,   // 사망 상태
    EXTRACT_FAIL_ALREADY = 4,   // 이미 탈출함
    EXTRACT_FAIL_NO_EXIT_ROOM = 5,   // 이 던전에 탈출 방 없음
};

struct ExtractionRequest {
    int extractionPointId;     // 탈출 지점 ID (현재 미사용, 서버가 위치로 판정)
};

struct ExtractionResult {
    int success;
    int itemCount;             // 가지고 나간 아이템 수 (인벤토리 미구현 → 0)
    int failReason;            // ExtractionFailReason
    float heldSec;             // 서버가 인정한 체류 시간 (디버그/UI용)
};

// S→C: 다른 플레이어가 탈출함. 시야와 무관하게 전원에게 broadcast.
struct PlayerExtracted {
    int clientId;
    int remainingPlayers;      // 아직 월드에 남은 인원 (생존 + 미탈출)
};

struct SessionEnded {
    int reason;       // 0=정상 종료, 1=호스트 이탈, 2=오류, 3=타임아웃
};

// ----------------------------------------------------------------------------
//  아이템 / 인벤토리 (서버 권위)
//
//  아이템 식별: 클라의 ItemData.itemId 문자열을 FNV-1a 32bit로 해시한 값.
//  양쪽이 같은 함수로 계산하므로 별도 ID 테이블 동기화가 필요 없다.
//  (ItemHash 참조 — 서버/클라 구현이 반드시 일치해야 함)
//
//  권위 모델:
//   - 전리품 생성은 서버가 결정 (몬스터 사망 시 시드 기반 결정적 드롭)
//   - 클라는 ITEM_PICKUP_REQUEST로 의도만 보냄
//   - 서버가 거리/재고/인벤토리 여유를 검증하고 결과를 통보
//   - 인벤토리 실체는 서버에만 있음. 클라는 INVENTORY_SYNC로 받아 표시만.
// ----------------------------------------------------------------------------

constexpr int MAX_LOOT_ENTRIES = 8;    // 컨테이너 1개의 최대 아이템 종류
constexpr int MAX_INVENTORY_ENTRIES = 32;   // 인벤토리 최대 아이템 종류(슬롯)
constexpr float LOOT_PICKUP_RANGE = 3.0f;  // 루팅 가능 거리(m)
constexpr float LOOT_PICKUP_RANGE_SQ = LOOT_PICKUP_RANGE * LOOT_PICKUP_RANGE;

// 아이템 1종 + 수량
struct ItemStack {
    int itemHash;
    int count;
};

// 루팅 실패 사유 (ItemPickupResult.failReason)
enum PickupFailReason : int {
    PICKUP_OK = 0,
    PICKUP_FAIL_NO_LOOT = 1,   // 그런 컨테이너 없음 (이미 소멸)
    PICKUP_FAIL_TOO_FAR = 2,   // 거리 초과
    PICKUP_FAIL_NO_ITEM = 3,   // 컨테이너에 그 아이템/수량 없음
    PICKUP_FAIL_INV_FULL = 4,   // 인벤토리 가득 참
    PICKUP_FAIL_DEAD = 5,   // 사망/탈출 상태
};

// C→S: 루팅 요청. count<=0 이면 해당 아이템 전량 요청으로 간주.
struct ItemPickupRequest {
    int lootId;
    int itemHash;
    int count;
};

// S→C: 루팅 결과 (요청한 본인에게만).
struct ItemPickupResult {
    int success;
    int lootId;
    int itemHash;
    int grantedCount;      // 실제로 획득한 수량 (부분 획득 가능)
    int failReason;        // PickupFailReason
};

// S→C: 전리품 컨테이너 생성.
struct LootSpawnData {
    int       lootId;
    int       sourceMonsterId;   // 몬스터 드롭이면 몬스터 id, 아니면 0
    float     posX, posY, posZ;
    int       entryCount;
    ItemStack entries[MAX_LOOT_ENTRIES];
};

// S→C: 컨테이너 소멸 (비었거나 세션 정리).
struct LootRemovedData {
    int lootId;
};

// S→C: 인벤토리 전체 동기화. 변경이 생길 때마다 본인에게 송신.
struct InventorySyncData {
    int       entryCount;
    int       totalCount;        // 전체 개수 합 (탈출 결과의 itemCount와 동일 기준)
    ItemStack entries[MAX_INVENTORY_ENTRIES];
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
    int playerTeams[MAX_SESSION_PLAYERS];   // 각 playerIds[i]의 로비 선택 팀 (0..MAX_TEAMS-1)
    // 채팅 발신자 표시 + PLAYER_ENTER_VIEW의 이름 채우기용.
    // 세션 매니저는 로그인 정보를 모르므로 로비가 넘겨준다.
    char playerNames[MAX_SESSION_PLAYERS][MAX_PLAYER_NAME];
};

// 세션 종료 시 플레이어 1명의 결과. 로비가 이걸 DB에 한 줄씩 넣는다.
// clientId가 아니라 이름을 싣는 이유: 세션이 끝날 때쯤엔 이미 접속을 끊은
// 클라가 있어서 로비의 clientId 매핑을 믿을 수 없다. 닉네임은 DB에서 UNIQUE다.
struct IpcPlayerResult {
    char playerName[MAX_PLAYER_NAME];
    int  extracted;        // 1=탈출 성공
    int  playSeconds;
    int  monsterKills;
    int  playerKills;
};

struct IpcSessionEnded {
    int sessionId;
    int reason;
    int totalPlayers;
    int survivors;
    int resultCount;                               // 아래 배열 유효 개수
    IpcPlayerResult results[MAX_SESSION_PLAYERS];
};

#pragma pack(pop)