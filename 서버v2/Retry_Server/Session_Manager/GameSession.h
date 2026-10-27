#pragma once
#include "Dungeon/DungeonGenerator.h"
#include "WorldSimulation.h"
#include "InterestManagement.h"
#include "CombatResolver.h"
#include "ProjectileSystem.h"
#include "LootSystem.h"
#include "../Common/MpscStack.h"

#include <atomic>
#include <chrono>
#include <memory>
#include <mutex>
#include <thread>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

class PlayerEntity;
class SessionClientConnection;
class LobbyReporter;

// ============================================================================
//  v2: 세션 일감
//
//  IOCP 워커는 세션 상태를 직접 건드리지 않고 일감만 만들어 MpscStack에 넣는다.
//  틱 스레드 혼자 꺼내서 처리하므로 세션 안에는 락이 하나도 없다.
//  패킷 본문은 구조체 바로 뒤에 붙여서 한 번에 할당한다.
// ============================================================================
struct SessionJob
{
    enum Kind : int { ATTACH, DETACH, PACKET };

    SessionJob*              next;
    Kind                     kind;
    int                      clientId;
    SessionClientConnection* conn;          // ATTACH만 사용
    int                      packetType;
    int                      size;

    char* Body() { return reinterpret_cast<char*>(this + 1); }

    static SessionJob* Create(Kind k, int cid, SessionClientConnection* c,
        int type, const char* body, int size);
    static void Destroy(SessionJob* j);
};

// ============================================================================
//  GameSession
//
//  단일 매치 1개를 관리. 30명까지의 플레이어가 1개의 던전에서 게임.
//
//  3단계 (현재): 골격
//   - IPC로 전달받은 멤버 명단(allowedPlayerIds) 보관
//   - 클라가 인증 시도하면 명단에 있는지 확인 (AuthClient에서)
//   - AttachClient: 인증된 클라가 PlayerEntity로 합류
//   - 50ms 틱 루프 (TickLoop)
//   - PLAYER_INPUT 받으면 PlayerEntity.position 갱신
//   - 매 틱 모든 플레이어에게 PLAYER_MOVE 브로드캐스트 (시야 처리 없이)
//
//  5단계 이후: 던전, 시야, 전투, 몬스터 추가
// ============================================================================

class GameSession
{
    // InterestManagement/CombatResolver가 private 멤버 직접 access 필요
    friend class InterestManagement;
    friend class CombatResolver;
    friend class ProjectileSystem;
    friend class LootSystem;

public:
    int                         sessionId;
    int                         hostClientId;
    int                         mapSeed;

    GameSession(int sid, int hid, int seed,
        const std::vector<int>& allowedPlayerIds,
        const std::vector<int>& playerTeams,
        const std::vector<std::string>& playerNames,
        LobbyReporter* reporter = nullptr);
    ~GameSession();

    void Start();
    void Stop();

    bool IsAllowedPlayer(int clientId) const;

    // 세션이 끝났는지 (SESSION_ENDED 송신 완료). SessionRegistry 회수 스레드가 확인.
    bool IsFinished() const { return sessionEndSignaled.load(); }

    // LootSystem이 플레이어를 조회할 때 사용 (틱 스레드에서만 호출).
    PlayerEntity* GetPlayerForLoot(int clientId) { return GetPlayer(clientId); }

    // ── v2: IOCP 워커가 부르는 입구. 일감만 넣고 바로 돌아간다 ──
    void PostAttach(int clientId, SessionClientConnection* conn);
    void PostDetach(int clientId);
    void PostPacket(int clientId, int packetType, const char* body, int bodySize);

    // ── v2: 서버 내부 측정 (회수 스레드가 1초마다 가져가고 0으로 되돌린다) ──
    struct TickStats
    {
        unsigned long long ticks = 0;     // 틱 횟수
        unsigned long long workUs = 0;    // 틱 처리 시간 합 (일감 처리 + TickStep)
        unsigned long long jobs = 0;      // 처리한 일감 수
        unsigned           maxUs = 0;     // 가장 오래 걸린 틱
        int                players = 0;   // 현재 인원
        unsigned long long lockWaitUs = 0; // sync=lock: 세션 락을 얻으려고 기다린 시간 합 (워커 + 틱)
        unsigned long long lockCount = 0;  // sync=lock: 락 획득 횟수
    };
    TickStats TakeStats();

    // 피아식별. 같은 팀이면 true → 공격이 통하지 않는다.
    // 둘 중 하나라도 팀 미배정(-1)이면 적으로 취급해 공격을 허용한다.
    bool IsSameTeam(int clientA, int clientB) const
    {
        if (clientA == clientB) return true;
        auto a = playerTeamMap.find(clientA);
        auto b = playerTeamMap.find(clientB);
        if (a == playerTeamMap.end() || b == playerTeamMap.end()) return false;
        if (a->second < 0 || b->second < 0) return false;
        return a->second == b->second;
    }

    // 몬스터 사망 시 전리품 생성 (CombatResolver/ProjectileSystem이 호출).
    void OnMonsterKilled(int monsterId, int killerClientId);
    // 플레이어 처치 집계. CombatResolver/ProjectileSystem이 PLAYER_DIED와 함께 호출.
    void AddPlayerKill(int killerClientId);

    // 시야 무관 broadcast (전체 attached 클라). MONSTER_ATTACK_EVENT, PLAYER_DIED 등에 사용.
    // 틱 스레드에서만 호출.
    void Broadcast(int packetType, const void* body, int size, int exceptClientId = 0);

private:
    // v1은 워커가 직접 불렀다. v2는 틱 스레드가 일감을 꺼내 부른다.
    void AttachClient(int clientId, SessionClientConnection* conn);
    void DetachClient(int clientId);
    void HandlePacket(int clientId, int packetType, const char* body, int bodySize);

    // ── v2: 일감 큐 (v2.ini sync=queue) ──
    MpscStack<SessionJob>                                      jobs;
    void*                                                      hWake = nullptr;   // 급한 일감·종료 요청이 오면 틱 스레드를 깨우는 이벤트
    void*                                                      hTimer = nullptr;  // tick=precise용 고해상도 타이머
    void Post(SessionJob* job, bool urgent);
    void DrainJobs();                                          // 틱 스레드 전용
    void FreeJobs();                                           // 틱 스레드가 멈춘 뒤 남은 일감 버리기
    void WaitUntil(std::chrono::steady_clock::time_point deadline);  // 기다리며 급한 일감 처리
    void FlushSends();                                         // send=batch: 모아 둔 패킷을 연결마다 한 번에 송신

    // ── v1 방식 비교용 (v2.ini sync=lock): 세션 전체를 감싸는 성긴 락 ──
    std::recursive_mutex                                       mtx;

    std::atomic<unsigned long long>                            statTicks{ 0 };
    std::atomic<unsigned long long>                            statWorkUs{ 0 };
    std::atomic<unsigned long long>                            statJobs{ 0 };
    std::atomic<unsigned>                                      statMaxUs{ 0 };
    std::atomic<int>                                           statPlayers{ 0 };
    std::atomic<unsigned long long>                            statLockWaitUs{ 0 };
    std::atomic<unsigned long long>                            statLockCount{ 0 };
    std::atomic_bool                                           wakePending{ false };  // 깨우기 신호 중복 방지
    void LockTimed(std::unique_lock<std::recursive_mutex>& lk); // sync=lock: 대기 시간 재며 잠금
    unsigned long long                                         drainedJobs = 0;  // 틱 스레드 전용

    std::unordered_set<int>                                    allowedPlayers;
    std::unordered_map<int, int>                               playerTeamMap;   // clientId → 로비 선택 팀(0..MAX_TEAMS-1)
    std::unordered_map<int, std::string>                       playerNameMap;   // clientId → 표시 이름(채팅/시야)
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>     players;
    bool                                                       everJoined = false; // 한 명이라도 들어온 적 있는지

    DungeonGenerator                                           dungeon;
    WorldSimulation                                            worldSim;

    // 6단계 신규: 시야 처리 (ViewList Phase 1).
    InterestManagement                                         im;

    // 7단계 신규: 플레이어 공격 처리.
    CombatResolver                                             cr;

    // 전투 신규: 원거리 투사체(활/총) 관리.
    ProjectileSystem                                          projSystem;

    // 아이템 신규: 서버 권위 전리품 + 인벤토리.
    LootSystem                                                loot;

    std::atomic_bool                                           running;
    std::thread                                                tickThread;

    // ── 동시 입장 배리어 ──────────────────────────────────────
    // 클라마다 던전 생성 시간이 달라서 먼저 들어온 사람만 시간이 흐르고
    // 몬스터에게 먼저 맞는다. 전원이 붙을 때까지 시뮬레이션을 멈추고,
    // 다 모이면 SESSION_START_GAME을 한꺼번에 보내 같이 시작한다.
    std::atomic_bool                                           gameStarted{ false };
    double                                                     preStartElapsed{ 0.0 };
    static constexpr double                                    START_TIMEOUT_SEC = 20.0;

    // 틱 스레드에서 호출. 전원 입장(또는 타임아웃)이면 시작 신호를 보낸다.
    void TryStartGame(bool force = false);

    void TickLoop();
    void TickStep(float dt);

    std::atomic_bool                                           sessionEndSignaled{ false };
    LobbyReporter* reporter;

    void HandlePlayerInput(int clientId, const char* body, int bodySize, float dt);
    void HandlePlayerAttack(int clientId, const char* body, int bodySize);
    void HandleExtraction(int clientId, const char* body, int bodySize);
    void HandleItemPickup(int clientId, const char* body, int bodySize);
    // 채팅. PARTY=세션 전원, TEAM=같은 팀.
    void HandleChat(int clientId, const char* body, int bodySize);

    // [디버그 치트] 탈출 방 근처로 이동. 배포 시 이 선언과 구현 삭제.
    void HandleDebugTeleportExit(int clientId);
    void HandleDebugTeleportBoss(int clientId);
    bool RoomCenterStandPos(int roomId, Vec3& out) const;
    void DebugTeleportTo(int clientId, const Vec3& dest, const char* label);
    PlayerEntity* GetPlayer(int clientId);

    // 매 틱: 탈출 방 안 체류 시간 누적 (방 밖이면 0 리셋). 틱 스레드에서 호출.
    void UpdateExtractionHold(float dt);
    // 월드에 남은 인원(생존+미탈출)이 0이면 SESSION_ENDED broadcast. 1회만 발생.
    // v2: 전원이 접속을 끊어도 종료한다 (v1은 세션이 영원히 남았다).
    void CheckSessionEnd();
};