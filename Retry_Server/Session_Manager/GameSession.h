#pragma once
#include "Dungeon/DungeonGenerator.h"
#include "WorldSimulation.h"

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
//  현재: 골격
//   - IPC로 전달받은 멤버 명단(allowedPlayerIds) 보관
//   - 클라가 인증 시도하면 명단에 있는지 확인 (AuthClient에서)
//   - AttachClient: 인증된 클라가 PlayerEntity로 합류
//   - 50ms 틱 루프 (TickLoop)
//   - PLAYER_INPUT 받으면 PlayerEntity.position 갱신
//   - 매 틱 모든 플레이어에게 PLAYER_MOVE 브로드캐스트 (시야 처리 없이)
//
//  이후: 시야, 전투 추가
// ============================================================================

class GameSession
{
public:
    int                         sessionId;
    int                         hostClientId;
    int                         mapSeed;

    GameSession(int sid, int hid, int seed,
                const std::vector<int>& allowedPlayerIds);
    ~GameSession();

    void Start();
    void Stop();

    // 인증 단계용: clientId가 이 세션의 멤버인지 확인.
    bool IsAllowedPlayer(int clientId) const;

    // 인증 통과한 클라가 합류. PlayerEntity 생성.
    void AttachClient(int clientId, SessionClientConnection* conn);

    // 클라 끊김.
    void DetachClient(int clientId);

    // 인증 후 패킷 처리 (SessionClientConnection에서 호출).
    void HandlePacket(int clientId, int packetType, const char* body, int bodySize);

    // 시야 안 모든 클라에게 브로드캐스트.
    // 본 단계에선 시야 없이 모든 attached 클라에게 송신.
    void Broadcast(int packetType, const void* body, int size, int exceptClientId = 0);

private:
    std::unordered_set<int>                                    allowedPlayers;
    std::mutex                                                 mtx;
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>     players;

    // 서버 측 던전 데이터 (시드로 생성, 클라와 동일).
    // 추후 단계에서 시야/충돌/AI 검증에 활용.
    DungeonGenerator                                           dungeon;

    // 게임 월드 시뮬레이션 (몬스터 스폰/AI/위치).
    // 5단계: 스폰 후 정지. 8단계에서 본격 AI.
    WorldSimulation                                            worldSim;

    std::atomic_bool                                           running;
    std::thread                                                tickThread;

    void TickLoop();
    void TickStep(float dt);

    // 패킷 핸들러
    void HandlePlayerInput(int clientId, const char* body, int bodySize, float dt);

    // 헬퍼
    PlayerEntity* GetPlayer(int clientId);

    // ENTER_VIEW 패킷 송신 헬퍼 (5단계: 시야 처리 없이 모든 객체).
    // 6단계에서 InterestManagement 적용 시 시야 안 객체만 보내도록 변경.
    void SendInitialEnterViews(int clientId);
    void SendPlayerEnterViewToOthers(const PlayerEntity& p, int exceptClientId);
};
