#include "GameSession.h"
#include "PlayerEntity.h"
#include "MonsterEntity.h"
#include "SessionClientConnection.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <chrono>
#include <cstring>

using namespace std::chrono;

GameSession::GameSession(int sid, int hid, int seed,
                         const std::vector<int>& allowedPlayerIds)
    : sessionId(sid)
    , hostClientId(hid)
    , mapSeed(seed)
    , running(false)
{
    for (int id : allowedPlayerIds) allowedPlayers.insert(id);
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

    std::lock_guard<std::mutex> lk(mtx);
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
    std::lock_guard<std::mutex> lk(mtx);

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
        // 시작 위치: 본인 팀의 시작방의 정확한 spawn 좌표 사용 (StartRoomManager 1:1)
        // 입장 순서로 팀 슬롯 배정 (clientId 별도 전달 없으니 임시).
        // worldOffset 적용해서 월드 좌표로 변환.
        if (!dungeon.assignedStartRooms.empty())
        {
            int playerIndex = (int)players.size();        // 0,1,2,...
            int teamSlot    = playerIndex / PLAYERS_PER_TEAM;     // 0,0,0, 1,1,1, ...
            int slotInTeam  = playerIndex % PLAYERS_PER_TEAM;     // 0,1,2, 0,1,2, ...
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

    // 5단계 추가: 신규/재입장한 클라에게 현재 모든 객체의 ENTER_VIEW 송신
    SendInitialEnterViews(clientId);

    // 다른 클라들에게는 이 입장자의 ENTER_VIEW 송신
    PlayerEntity* p = players[clientId].get();
    SendPlayerEnterViewToOthers(*p, clientId);
}

void GameSession::DetachClient(int clientId)
{
    std::lock_guard<std::mutex> lk(mtx);
    auto it = players.find(clientId);
    if (it == players.end()) return;
    it->second->conn = nullptr;     // entity는 유지, 연결만 해제

    // 다른 클라들에게 PLAYER_LEAVE_VIEW 송신
    PlayerLeaveView lv{};
    lv.clientId = clientId;
    for (auto& kv : players)
    {
        if (kv.first == clientId) continue;
        SessionClientConnection* c = kv.second->conn;
        if (c && c->active)
        {
            c->SendPacket((int)PacketType::PLAYER_LEAVE_VIEW, &lv, sizeof(lv));
        }
    }

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

    // 5~7단계에서 추가:
    // case PacketType::PLAYER_ATTACK_REQUEST: ...
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
    std::lock_guard<std::mutex> lk(mtx);
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

    std::lock_guard<std::mutex> lk(mtx);
    PlayerEntity* p = GetPlayer(clientId);
    if (!p) return;

    p->ApplyInput(in->posX, in->posY, in->posZ, in->rotY,
                  in->moveX, in->moveY, in->sprint,
                  in->timestamp, dt);
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

    // 1단계: 시뮬레이션 (몬스터 AI). lock 안에서 players access.
    {
        std::lock_guard<std::mutex> lk(mtx);
        worldSim.Step(dt, players, attackEvents);

        // 몬스터의 공격 이벤트 처리: 플레이어 HP 차감 + 패킷용 데이터 준비
        for (const auto& ev : attackEvents)
        {
            auto it = players.find(ev.victimClientId);
            if (it == players.end()) continue;
            PlayerEntity& victim = *it->second;
            if (victim.hp <= 0) continue;

            victim.hp -= ev.damage;
            if (victim.hp < 0) victim.hp = 0;
        }
    }

    // 2단계: 스냅샷 만들기 (lock 안에서) 후 lock 풀고 broadcast.
    std::vector<PlayerMove>           playerSnapshots;
    std::vector<MonsterMove>          monsterSnapshots;
    std::vector<MonsterAttackEvent>   attackPackets;
    std::vector<PlayerDied>           deathPackets;
    {
        std::lock_guard<std::mutex> lk(mtx);

        long long ts = duration_cast<milliseconds>(
            system_clock::now().time_since_epoch()).count();

        playerSnapshots.reserve(players.size());
        for (auto& kv : players)
        {
            const PlayerEntity* p = kv.second.get();
            PlayerMove pm{};
            pm.clientId  = p->clientId;
            pm.posX = p->position.x; pm.posY = p->position.y; pm.posZ = p->position.z;
            pm.rotY = p->rotY;
            pm.speed = p->speed;
            pm.animState = p->animState;
            pm.timestamp = ts;
            playerSnapshots.push_back(pm);
        }

        const auto& mons = worldSim.GetMonsters();
        monsterSnapshots.reserve(mons.size());
        for (const auto& kv : mons)
        {
            const MonsterEntity& m = *kv.second;
            if (m.aiState == AI_DEAD) continue;
            MonsterMove mm{};
            mm.monsterId = m.id;
            mm.posX = m.position.x; mm.posY = m.position.y; mm.posZ = m.position.z;
            mm.rotY = m.rotY;
            mm.aiState = m.aiState;
            mm.targetClientId = m.targetClientId;
            mm.timestamp = ts;
            monsterSnapshots.push_back(mm);
        }

        // 공격 이벤트 → 클라에 알릴 패킷 데이터로 변환
        for (const auto& ev : attackEvents)
        {
            auto it = players.find(ev.victimClientId);
            if (it == players.end()) continue;
            const PlayerEntity& victim = *it->second;

            MonsterAttackEvent mae{};
            mae.monsterId      = ev.monsterId;
            mae.victimClientId = ev.victimClientId;
            mae.damage         = ev.damage;
            mae.victimHpAfter  = victim.hp;
            attackPackets.push_back(mae);

            if (victim.hp <= 0)
            {
                PlayerDied pd{};
                pd.victimId = ev.victimClientId;
                pd.killerId = -ev.monsterId;       // 음수 = 몬스터
                deathPackets.push_back(pd);
            }
        }
    }

    // 3단계: broadcast (Broadcast 자체가 lock 잡음).
    // 시야 처리 없는 단순 브로드캐스트 (6단계에서 InterestManagement로 교체).
    for (const auto& pm : playerSnapshots)
        Broadcast((int)PacketType::PLAYER_MOVE, &pm, sizeof(pm), pm.clientId);
    for (const auto& mm : monsterSnapshots)
        Broadcast((int)PacketType::MONSTER_MOVE, &mm, sizeof(mm), 0);
    for (const auto& mae : attackPackets)
        Broadcast((int)PacketType::MONSTER_ATTACK_EVENT, &mae, sizeof(mae), 0);
    for (const auto& pd : deathPackets)
        Broadcast((int)PacketType::PLAYER_DIED, &pd, sizeof(pd), 0);
}

// ============================================================================
//  ENTER_VIEW 헬퍼 (5단계: 시야 처리 없이 일괄 송신)
// ============================================================================

void GameSession::SendInitialEnterViews(int clientId)
{
    // 호출자가 mtx 잠긴 상태. 추가 lock 안 함.
    auto it = players.find(clientId);
    if (it == players.end()) return;
    SessionClientConnection* targetConn = it->second->conn;
    if (!targetConn || !targetConn->active) return;

    // 1) 다른 모든 플레이어 정보 보내기
    for (auto& kv : players)
    {
        if (kv.first == clientId) continue;
        const PlayerEntity& p = *kv.second;

        PlayerEnterView ev{};
        ev.clientId = p.clientId;
        std::strncpy(ev.playerName, p.playerName, sizeof(ev.playerName) - 1);
        ev.posX = p.position.x;
        ev.posY = p.position.y;
        ev.posZ = p.position.z;
        ev.rotY = p.rotY;
        ev.hp = p.hp;
        ev.maxHp = p.maxHp;
        targetConn->SendPacket((int)PacketType::PLAYER_ENTER_VIEW, &ev, sizeof(ev));
    }

    // 2) 모든 몬스터 정보 보내기
    const auto& monsters = worldSim.GetMonsters();
    for (const auto& kv : monsters)
    {
        const MonsterEntity& m = *kv.second;
        if (m.aiState == AI_DEAD) continue;

        MonsterEnterView ev{};
        ev.monsterId   = m.id;
        ev.monsterKind = m.kind;
        ev.posX = m.position.x;
        ev.posY = m.position.y;
        ev.posZ = m.position.z;
        ev.rotY = m.rotY;
        ev.hp = m.hp;
        ev.maxHp = m.maxHp;
        targetConn->SendPacket((int)PacketType::MONSTER_ENTER_VIEW, &ev, sizeof(ev));
    }

    // 3) 본인의 시작 위치를 PLAYER_MOVE로 즉시 알림.
    //    클라는 첫 본인 PLAYER_MOVE를 받으면 transform.position을 갱신.
    //    이게 없으면 클라가 50ms 후 본인 위치를 (0,0.1,0)으로 보내서 시작방 위치 손실됨.
    {
        const PlayerEntity& me = *it->second;
        long long ts = duration_cast<milliseconds>(
            system_clock::now().time_since_epoch()).count();
        PlayerMove pm{};
        pm.clientId  = me.clientId;
        pm.posX = me.position.x;
        pm.posY = me.position.y;
        pm.posZ = me.position.z;
        pm.rotY = me.rotY;
        pm.speed = 0.f;
        pm.animState = 0;
        pm.timestamp = ts;
        targetConn->SendPacket((int)PacketType::PLAYER_MOVE, &pm, sizeof(pm));
    }
}

void GameSession::SendPlayerEnterViewToOthers(const PlayerEntity& p, int exceptClientId)
{
    // 호출자가 mtx 잠긴 상태.
    PlayerEnterView ev{};
    ev.clientId = p.clientId;
    std::strncpy(ev.playerName, p.playerName, sizeof(ev.playerName) - 1);
    ev.posX = p.position.x;
    ev.posY = p.position.y;
    ev.posZ = p.position.z;
    ev.rotY = p.rotY;
    ev.hp = p.hp;
    ev.maxHp = p.maxHp;

    for (auto& kv : players)
    {
        if (kv.first == exceptClientId) continue;
        SessionClientConnection* c = kv.second->conn;
        if (c && c->active)
        {
            c->SendPacket((int)PacketType::PLAYER_ENTER_VIEW, &ev, sizeof(ev));
        }
    }
}
