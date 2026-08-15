#pragma once
#include "Dungeon/DungeonGenerator.h"
#include "WorldSimulation.h"
#include "InterestManagement.h"
#include "CombatResolver.h"
#include "ProjectileSystem.h"
#include "LootSystem.h"

#include <atomic>
#include <memory>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <vector>

class PlayerEntity;
class SessionClientConnection;
class LobbyReporter;

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
        LobbyReporter* reporter = nullptr);
    ~GameSession();

    void Start();
    void Stop();

    bool IsAllowedPlayer(int clientId) const;

    // 세션이 끝났는지 (SESSION_ENDED 송신 완료). SessionRegistry 회수 스레드가 확인.
    bool IsFinished() const { return sessionEndSignaled.load(); }

    // LootSystem이 플레이어를 조회할 때 사용 (mtx 잠긴 상태에서만 호출).
    PlayerEntity* GetPlayerForLoot(int clientId) { return GetPlayer(clientId); }

    // 몬스터 사망 시 전리품 생성 (CombatResolver/ProjectileSystem이 호출).
    void OnMonsterKilled(int monsterId, int killerClientId);
    void AttachClient(int clientId, SessionClientConnection* conn);
    void DetachClient(int clientId);
    void HandlePacket(int clientId, int packetType, const char* body, int bodySize);

    // 시야 무관 broadcast (전체 attached 클라). MONSTER_ATTACK_EVENT, PLAYER_DIED 등에 사용.
    void Broadcast(int packetType, const void* body, int size, int exceptClientId = 0);

private:
    std::unordered_set<int>                                    allowedPlayers;
    std::unordered_map<int, int>                               playerTeamMap;   // clientId → 로비 선택 팀(0..MAX_TEAMS-1)
    std::recursive_mutex                                       mtx;
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>     players;

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

    void TickLoop();
    void TickStep(float dt);

    std::atomic_bool                                           sessionEndSignaled{ false };
    LobbyReporter* reporter;

    void HandlePlayerInput(int clientId, const char* body, int bodySize, float dt);
    void HandlePlayerAttack(int clientId, const char* body, int bodySize);
    void HandleExtraction(int clientId, const char* body, int bodySize);
    void HandleItemPickup(int clientId, const char* body, int bodySize);

    // [디버그 치트] 탈출 방 근처로 이동. 배포 시 이 선언과 구현 삭제.
    void HandleDebugTeleportExit(int clientId);
    PlayerEntity* GetPlayer(int clientId);

    // 매 틱: 탈출 방 안 체류 시간 누적 (방 밖이면 0 리셋). mtx 잠긴 상태에서 호출.
    void UpdateExtractionHold(float dt);
    // 월드에 남은 인원(생존+미탈출)이 0이면 SESSION_ENDED broadcast. 1회만 발생.
    void CheckSessionEnd();
};