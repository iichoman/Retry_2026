#include "GameSession.h"
#include "PlayerEntity.h"
#include "MonsterEntity.h"
#include "SessionClientConnection.h"
#include "PositionValidator.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <chrono>
#include <cstring>

using namespace std::chrono;

GameSession::GameSession(int sid, int hid, int seed,
    const std::vector<int>& allowedPlayerIds,
    const std::vector<int>& playerTeams)
    : sessionId(sid)
    , hostClientId(hid)
    , mapSeed(seed)
    , running(false)
{
    for (size_t i = 0; i < allowedPlayerIds.size(); ++i)
    {
        int id = allowedPlayerIds[i];
        allowedPlayers.insert(id);
        playerTeamMap[id] = (i < playerTeams.size()) ? playerTeams[i] : 0;
    }
}

GameSession::~GameSession()
{
    Stop();
}

void GameSession::Start()
{
    if (running) return;

    // 던전 데이터를 시드로 생성. 동기 호출.
    // 클라가 같은 시드로 자체 생성하므로 결과가 일치해야 함.
    dungeon.Generate(mapSeed);

    // 던전 위에 몬스터 결정적 배치 (같은 시드 → 같은 몬스터 위치).
    worldSim.SpawnMonsters(dungeon, mapSeed);

    running = true;
    tickThread = std::thread([this] { TickLoop(); });
    Log::Info("세션 시작: id=%d host=%d seed=%d 인원=%d",
        sessionId, hostClientId, mapSeed, (int)allowedPlayers.size());
}

void GameSession::Stop()
{
    if (!running.exchange(false)) return;
    if (tickThread.joinable()) tickThread.join();

    std::lock_guard<std::recursive_mutex> lk(mtx);
    for (auto& kv : players)
    {
        if (kv.second->conn) kv.second->conn->Disconnect();
    }
    players.clear();
}

bool GameSession::IsAllowedPlayer(int clientId) const
{
    return allowedPlayers.count(clientId) > 0;
}

void GameSession::AttachClient(int clientId, SessionClientConnection* conn)
{
    std::lock_guard<std::recursive_mutex> lk(mtx);

    auto it = players.find(clientId);
    if (it != players.end())
    {
        // 재접속: 기존 entity에 새 conn 연결
        it->second->conn = conn;
        Log::Info("[Session %d] 재접속: clientId=%d", sessionId, clientId);
    }
    else
    {
        auto pe = std::make_unique<PlayerEntity>(clientId);
        // 시작 위치: 로비에서 고른 팀의 시작방 spawn 좌표 사용.
        if (!dungeon.assignedStartRooms.empty())
        {
            // 로비 선택 팀 (없으면 0)
            auto tIt = playerTeamMap.find(clientId);
            int teamSlot = (tIt != playerTeamMap.end()) ? tIt->second : 0;

            // 같은 팀에 이미 들어와 있는 인원 수 = 이 팀에서의 슬롯(0,1,2)
            int slotInTeam = 0;
            for (auto& kv : players)
            {
                auto t2 = playerTeamMap.find(kv.first);
                int otherTeam = (t2 != playerTeamMap.end()) ? t2->second : 0;
                if (otherTeam == teamSlot) ++slotInTeam;
            }

            teamSlot = teamSlot % (int)dungeon.assignedStartRooms.size();

            const StartRoom& sr = dungeon.assignedStartRooms[teamSlot];
            const Vec3& spawn = (slotInTeam < (int)sr.playerSpawnPositions.size())
                ? sr.playerSpawnPositions[slotInTeam]
                : sr.teamAnchorPosition;
            pe->position = Vec3(spawn.x + dungeon.worldOffset.x,
                spawn.y,
                spawn.z + dungeon.worldOffset.z);
            pe->rotY = sr.spawnYawDegrees;
        }
        pe->conn = conn;
        players[clientId] = std::move(pe);
        Log::Info("[Session %d] 입장: clientId=%d (현재 %d명) pos=(%.1f, %.1f, %.1f)",
            sessionId, clientId, (int)players.size(),
            players[clientId]->position.x,
            players[clientId]->position.y,
            players[clientId]->position.z);
    }

    // 6단계: 시야 처리 활성화.
    //   - 새 입장자에게: 시야 안 다른 플레이어/몬스터의 ENTER_VIEW 송신
    //   - 시야 안 다른 플레이어들에게: 새 입장자의 ENTER_VIEW 송신
    //   - 양쪽 viewedXxx 갱신
    im.OnPlayerJoin(*this, clientId);

    // 본인 시작 위치 즉시 알림 (클라가 시작방 좌표로 transform 갱신용).
    // OnPlayerJoin은 다른 객체 ENTER만 처리. 본인 PLAYER_MOVE는 별도 송신.
    PlayerEntity* p = players[clientId].get();
    p->startPosResendTicks = 20;   // 20틱(=1초) 동안 재송신 예약
    if (p && p->conn && p->conn->active)
    {
        long long ts = duration_cast<milliseconds>(
            system_clock::now().time_since_epoch()).count();
        PlayerMove pm{};
        pm.clientId = p->clientId;
        pm.posX = p->position.x;
        pm.posY = p->position.y;
        pm.posZ = p->position.z;
        pm.rotY = p->rotY;
        pm.speed = 0.f;
        pm.animState = 0;
        pm.timestamp = ts;
        p->conn->SendPacket((int)PacketType::PLAYER_MOVE, &pm, sizeof(pm));
    }
}

void GameSession::DetachClient(int clientId)
{
    std::lock_guard<std::recursive_mutex> lk(mtx);
    auto it = players.find(clientId);
    if (it == players.end()) return;
    it->second->conn = nullptr;     // entity는 유지, 연결만 해제

    // 6단계: 시야 안 다른 플레이어들에게 LEAVE_VIEW 송신 + viewedPlayers에서 제거
    im.OnPlayerLeave(*this, clientId);

    Log::Info("[Session %d] 퇴장: clientId=%d", sessionId, clientId);
}

void GameSession::HandlePacket(int clientId, int packetType,
    const char* body, int bodySize)
{
    PacketType type = static_cast<PacketType>(packetType);
    switch (type)
    {
    case PacketType::PLAYER_INPUT:
        // dt는 직전 입력과의 시간 차이. 본 단계에선 단순 0.05f 가정.
        HandlePlayerInput(clientId, body, bodySize, 0.05f);
        break;

    case PacketType::PLAYER_ATTACK_REQUEST:
        HandlePlayerAttack(clientId, body, bodySize);
        break;

        // 추후 추가:
        // case PacketType::EXTRACTION_REQUEST:   ...

    default:
        Log::Warn("[Session %d] 알 수 없는 인게임 패킷 type=%d from cid=%d",
            sessionId, packetType, clientId);
        break;
    }
}

void GameSession::Broadcast(int packetType, const void* body, int size, int exceptClientId)
{
    // 주의: 호출자가 mtx를 들고 있다고 가정하지 않음.
    // 본 단계에선 단순화를 위해 매번 lock.
    std::lock_guard<std::recursive_mutex> lk(mtx);
    for (auto& kv : players)
    {
        if (kv.first == exceptClientId) continue;
        if (kv.second->conn && kv.second->conn->active)
        {
            kv.second->conn->SendPacket(packetType, body, size);
        }
    }
}

// ============================================================================
//  내부: PLAYER_INPUT 처리
// ============================================================================

void GameSession::HandlePlayerInput(int clientId, const char* body, int bodySize, float dt)
{
    if (bodySize < (int)sizeof(PlayerInput)) return;
    const PlayerInput* in = reinterpret_cast<const PlayerInput*>(body);

    std::lock_guard<std::recursive_mutex> lk(mtx);
    PlayerEntity* p = GetPlayer(clientId);
    if (!p) return;
    if (p->hp <= 0) return;     // 죽은 플레이어 입력 무시

    // 클라가 보낸 위치를 PositionValidator로 검증.
    // - 텔레포트 거리 초과 → 직전 위치 유지
    // - 벽 통과 시도 → 슬라이딩 또는 직전 위치
    // 클라 walkSpeed=6, sprintSpeed=10. 10을 cap으로 사용.
    Vec3 attempted(in->posX, in->posY, in->posZ);
    Vec3 validated = PositionValidator::ValidateMove(
        dungeon, p->position, attempted, 10.0f, dt);

    p->ApplyInput(validated.x, validated.y, validated.z, in->rotY,
        in->moveX, in->moveY, in->sprint,
        in->timestamp, dt);
}

// ============================================================================
//  내부: PLAYER_ATTACK_REQUEST 처리 (7단계)
// ============================================================================

void GameSession::HandlePlayerAttack(int clientId, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(PlayerAttackRequest)) return;
    const PlayerAttackRequest* req = reinterpret_cast<const PlayerAttackRequest*>(body);

    std::lock_guard<std::recursive_mutex> lk(mtx);
    cr.HandleAttack(*this, clientId, *req);
}

PlayerEntity* GameSession::GetPlayer(int clientId)
{
    auto it = players.find(clientId);
    if (it == players.end()) return nullptr;
    return it->second.get();
}

// ============================================================================
//  틱 루프 (50ms 주기)
// ============================================================================

void GameSession::TickLoop()
{
    constexpr int TICK_MS = 50;
    auto last = high_resolution_clock::now();

    while (running)
    {
        std::this_thread::sleep_for(milliseconds(TICK_MS));

        auto now = high_resolution_clock::now();
        float dt = duration_cast<milliseconds>(now - last).count() / 1000.f;
        last = now;

        TickStep(dt);
    }
}

void GameSession::TickStep(float dt)
{
    std::vector<WorldSimulation::AttackEvent> attackEvents;
    std::vector<MonsterAttackEvent>           attackPackets;
    std::vector<PlayerDied>                   deathPackets;

    // 시뮬레이션 + 시야 갱신 모두 lock 안에서.
    {
        std::lock_guard<std::recursive_mutex> lk(mtx);

        // 1) 몬스터 AI (위치 갱신 + 공격 결정)
        worldSim.Step(dt, players, dungeon, attackEvents);

        // 2) 몬스터 공격 결과 처리: 플레이어 HP 차감 + 패킷 데이터 준비
        for (const auto& ev : attackEvents)
        {
            auto it = players.find(ev.victimClientId);
            if (it == players.end()) continue;
            PlayerEntity& victim = *it->second;
            if (victim.hp <= 0) continue;

            victim.hp -= ev.damage;
            if (victim.hp < 0) victim.hp = 0;

            // 7단계 후속: 몬스터 → 플레이어 공격 로그 (콘솔에서 추적 가능)
            Log::Info("[MonsterAttack] mid=%d hit cid=%d dmg=%d hpAfter=%d%s",
                ev.monsterId, ev.victimClientId, ev.damage, victim.hp,
                victim.hp <= 0 ? " [PLAYER_DIED]" : "");

            MonsterAttackEvent mae{};
            mae.monsterId = ev.monsterId;
            mae.victimClientId = ev.victimClientId;
            mae.damage = ev.damage;
            mae.victimHpAfter = victim.hp;
            attackPackets.push_back(mae);

            if (victim.hp <= 0)
            {
                PlayerDied pd{};
                pd.victimId = ev.victimClientId;
                pd.killerId = -ev.monsterId;       // 음수 = 몬스터 ID
                deathPackets.push_back(pd);
            }
        }

        // 2-b) 원거리 투사체 이동 + 충돌 처리 (활/총).
        //      벽/몬스터/플레이어 명중 시 데미지 + 소멸 패킷을 자체 broadcast.
        projSystem.Update(*this, dt);

        // 본인 시작 위치 재송신 (원격 클라는 active가 늦게 켜져 단발 송신을 놓침)
        for (auto& kv : players)
        {
            PlayerEntity& pe = *kv.second;
            if (pe.startPosResendTicks > 0 && pe.conn && pe.conn->active)
            {
                long long ts = duration_cast<milliseconds>(
                    system_clock::now().time_since_epoch()).count();
                PlayerMove pm{};
                pm.clientId = pe.clientId;
                pm.posX = pe.position.x;
                pm.posY = pe.position.y;
                pm.posZ = pe.position.z;
                pm.rotY = pe.rotY;
                pm.speed = 0.f;
                pm.animState = 0;
                pm.timestamp = ts;
                pe.conn->SendPacket((int)PacketType::PLAYER_MOVE, &pm, sizeof(pm));
                pe.startPosResendTicks--;
            }
        }

        // 3) 시야 갱신 + ENTER/LEAVE/MOVE 패킷 송신.
        //    각 플레이어의 viewedXxx를 새 시야와 비교하여 차이를 처리.
        //    PLAYER_MOVE/MONSTER_MOVE는 시야 안 클라에게만 송신 (대역폭 절약).
        im.UpdateAll(*this);
    }

    // 시야 무관한 중요 이벤트는 전체 broadcast (몬스터 공격, 사망).
    // Broadcast는 자체 lock 잡음.
    for (const auto& mae : attackPackets)
        Broadcast((int)PacketType::MONSTER_ATTACK_EVENT, &mae, sizeof(mae), 0);
    for (const auto& pd : deathPackets)
        Broadcast((int)PacketType::PLAYER_DIED, &pd, sizeof(pd), 0);
}

// 5단계의 SendInitialEnterViews / SendPlayerEnterViewToOthers 헬퍼는 제거됨.
// 6단계부터 InterestManagement.OnPlayerJoin/OnPlayerLeave가 통합 처리.