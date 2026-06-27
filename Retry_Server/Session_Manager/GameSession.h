#pragma once
#include "Dungeon/DungeonGenerator.h"
#include "WorldSimulation.h"
#include "InterestManagement.h"
#include "CombatResolver.h"
#include "ProjectileSystem.h"

#include <atomic>
#include <memory>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <vector>

class PlayerEntity;
class SessionClientConnection;

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

public:
    int                         sessionId;
    int                         hostClientId;
    int                         mapSeed;

    GameSession(int sid, int hid, int seed,
        const std::vector<int>& allowedPlayerIds,
        const std::vector<int>& playerTeams);
    ~GameSession();

    void Start();
    void Stop();

    bool IsAllowedPlayer(int clientId) const;
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

    std::atomic_bool                                           running;
    std::thread                                                tickThread;

    void TickLoop();
    void TickStep(float dt);

    void HandlePlayerInput(int clientId, const char* body, int bodySize, float dt);
    void HandlePlayerAttack(int clientId, const char* body, int bodySize);
    PlayerEntity* GetPlayer(int clientId);
};