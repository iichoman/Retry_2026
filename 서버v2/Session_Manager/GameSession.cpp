#include "GameSession.h"
#include "../Common/Logger.h"
#include "../Common/PacketProtocol.h"
#include "ABItems.h"
#include "LobbyReporter.h"
#include "LootSystem.h"
#include "MonsterEntity.h"
#include "PlayerEntity.h"
#include "PositionValidator.h"
#include "SessionClientConnection.h"
#include "PositionSync.h"
#include "StormSystem.h"
#include "V2Config.h"

#include <chrono>
#include <climits>
#include <cstdlib>
#include <cstring>
#include <new>
#include <vector>

using namespace std::chrono;

// ============================================================================
//  v2: 세션 일감 할당 (본문을 구조체 뒤에 붙여 한 번에)
// ============================================================================

SessionJob* SessionJob::Create(Kind k, int cid, SessionClientConnection* c,
    int type, const char* body, int size)
{
    if (size < 0) size = 0;
    void* mem = std::malloc(sizeof(SessionJob) + (size_t)size);
    if (!mem) return nullptr;

    SessionJob* j = new (mem) SessionJob();
    j->next = nullptr;
    j->kind = k;
    j->clientId = cid;
    j->conn = c;
    j->packetType = type;
    j->size = size;
    if (body && size > 0) std::memcpy(j->Body(), body, (size_t)size);
    return j;
}

void SessionJob::Destroy(SessionJob* j)
{
    std::free(j);
}

static void SendPositionState(PlayerEntity& p,bool teleport) {
    PositionSync::State state;state.serial=++p.positionSerial;state.inputTimestamp=p.lastInputTimestamp;
    state.x=p.position.x;state.y=p.position.y;state.z=p.position.z;state.teleport=teleport?1:0;
    if(p.conn&&p.conn->active)p.conn->SendPacket(PositionSync::Packet,&state,sizeof(state));
}

// ============================================================================
//  [디버그 치트] 즉시 탈출
//
//  true면 EXTRACTION_REQUEST의 위치/체류시간 검증을 건너뛴다.
//  탈출 방까지 걸어가서 7초 서 있는 과정 없이 탈출 흐름을 테스트할 때 사용.
//
//  주의: 서버가 권위라 클라 치트키만으로는 동작하지 않는다. 이 값이 true여야 함.
//        배포 전 반드시 false로. 켜져 있으면 세션 시작 로그에 경고가 찍힌다.
//
//  기본값 false: F9 치트는 "탈출 방 근처로 이동"이라 검증을 건너뛸 필요가 없다.
//  (이동 후 실제로 걸어들어가 7초 홀드하는 정상 흐름을 그대로 테스트한다)
//  검증 자체를 건너뛰고 싶을 때만 true로.
// ============================================================================
static constexpr bool DEBUG_INSTANT_EXTRACT = false;

// [디버그 치트] 탈출 방 이동 허용. 배포 전 false로.
static constexpr bool DEBUG_ALLOW_TELEPORT = true;

GameSession::GameSession(int sid, int hid, int seed, const std::vector<int> &allowedPlayerIds,
                         const std::vector<int> &playerTeams, const std::vector<std::string> &playerNames,
                         LobbyReporter *rep, const std::vector<int>& dbPlayerIds,
                         const std::string& runToken, const std::vector<Economy::Grant>& grants)
    : sessionId(sid), hostClientId(hid), mapSeed(seed), running(false), reporter(rep)
{
    economyRun = runToken;   // 로비가 만든 판 토큰 (정산 보고 때 그대로 돌려준다)
    for (size_t i = 0; i < allowedPlayerIds.size(); ++i)
    {
        int id = allowedPlayerIds[i];
        allowedPlayers.insert(id);
        if(!runToken.empty()&&i<grants.size())economyGrants[id]=grants[i];
        playerTeamMap[id] = (i < playerTeams.size()) ? playerTeams[i] : 0;
        if (i < playerNames.size() && !playerNames[i].empty())
            playerNameMap[id] = playerNames[i];
    }

    // 워커가 인증할 때 보는 명단 (세션 상태와 분리)
    joinCount = (int)allowedPlayerIds.size();
    joinIds.reset(new std::atomic<int>[joinCount > 0 ? joinCount : 1]);
    for (int i = 0; i < joinCount; ++i)
        joinIds[i].store(allowedPlayerIds[i], std::memory_order_relaxed);

    hWake = CreateEventW(nullptr, FALSE, FALSE, nullptr);   // 자동 리셋 이벤트

    if (V2::Get().preciseTick)
    {
        // Win10 1803+ 고해상도 타이머. 안 되면 일반 타이머 (Main에서 timeBeginPeriod(1))
        hTimer = CreateWaitableTimerExW(nullptr, nullptr,
            CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (!hTimer) hTimer = CreateWaitableTimerW(nullptr, FALSE, nullptr);
    }
}

GameSession::~GameSession()
{
    Stop();
    FreeJobs();         // 멈춘 뒤에 들어온 일감까지 정리
    if (hWake) CloseHandle((HANDLE)hWake);
    if (hTimer) CloseHandle((HANDLE)hTimer);
}

bool GameSession::Start()
{
    if (running)
        return true;

    // 던전 데이터를 시드로 생성. 동기 호출.
    // 클라가 같은 시드로 자체 생성하므로 결과가 일치해야 함.
    dungeon.Generate(mapSeed);
    EnvironmentInit();
    StormInit();

    // 던전 위에 몬스터 결정적 배치 (같은 시드 → 같은 몬스터 위치).
    worldSim.SpawnMonsters(dungeon, mapSeed);

    running = true;
    tickThread = std::thread([this] { TickLoop(); });
    Log::Info("세션 시작: id=%d host=%d seed=%d 인원=%d", sessionId, hostClientId, mapSeed, (int)allowedPlayers.size());

    if (DEBUG_INSTANT_EXTRACT)
        Log::Warn("*** 치트 활성: 즉시 탈출(DEBUG_INSTANT_EXTRACT) — 배포 전 끌 것 ***");
    if (DEBUG_ALLOW_TELEPORT)
        Log::Warn("*** 치트 활성: 탈출 방 이동(DEBUG_ALLOW_TELEPORT) — 배포 전 끌 것 ***");
    return true;
}

void GameSession::Stop()
{
    if (!running.exchange(false))
        return;
    if (hWake) SetEvent((HANDLE)hWake);     // 대기 중인 틱 스레드를 바로 깨운다
    if (tickThread.joinable())
        tickThread.join();

    // 틱 스레드가 끝났으므로 이제 세션을 만지는 스레드는 이 스레드뿐이다.
    // (락 모드는 워커가 아직 들어올 수 있으므로 v1처럼 락을 잡는다)
    std::unique_lock<std::recursive_mutex> lk(mtx, std::defer_lock);
    if (!V2::Get().queueSync) lk.lock();
    for (auto &kv : players)
    {
        if (kv.second->conn)
            kv.second->conn->Disconnect();
    }
    CloseEconomy();   // 셧다운 등으로 끝날 때 남은 정산 보고
    players.clear();
    statPlayers.store(0, std::memory_order_relaxed);
    FreeJobs(true);
}

// ============================================================================
//  v2: 일감 큐
//
//  워커 스레드 : Post*() → 일감을 만들어 MpscStack에 넣기만 한다 (대기 없음)
//  틱 스레드   : DrainJobs() → 통째로 꺼내 넣은 순서대로 처리한다
//
//  급한 일감(입장, 공격, 줍기, 탈출, 채팅, 치트)은 이벤트로 틱 스레드를 깨워 바로 처리한다.
//  PLAYER_INPUT은 다음 틱에 몰아서 처리한다 (어차피 시야 갱신은 틱에서 나간다).
// ============================================================================

void GameSession::Post(SessionJob* job, bool urgent)
{
    if (!job) return;
    // 멈춘 세션: 쌓지 않고 버린다. 입장 요청이면 연결을 닫아
    // 그 연결의 IOCP 완료가 세션이 지워지기 전(유예 10초 안)에 끝나게 한다.
    if (!running.load())
    {
        if (job->kind == SessionJob::ATTACH && job->conn) job->conn->Disconnect();
        SessionJob::Destroy(job);
        return;
    }
    if (V2::Get().coarseQueue) jobsCoarse.Push(job);
    else jobs.Push(job);
    // 급한 일감이면 깨운다. 이미 깨우기 신호가 나가 있으면 시스템 콜을 생략한다.
    if (urgent && hWake && !wakePending.exchange(true, std::memory_order_acq_rel))
        SetEvent((HANDLE)hWake);
}

void GameSession::LockTimed(std::unique_lock<std::recursive_mutex>& lk)
{
    auto t0 = steady_clock::now();
    lk.lock();
    auto us = duration_cast<microseconds>(steady_clock::now() - t0).count();
    statLockWaitUs.fetch_add((unsigned long long)us, std::memory_order_relaxed);
    statLockCount.fetch_add(1, std::memory_order_relaxed);
}

// 락 모드(sync=lock)는 v1처럼 워커가 세션 락을 잡고 바로 처리한다 (비교 실험용).

void GameSession::PostAttach(int clientId, SessionClientConnection* conn)
{
    if (!V2::Get().queueSync)
    {
        std::unique_lock<std::recursive_mutex> lk(mtx, std::defer_lock);
        LockTimed(lk);
        if (!running) { if (conn) conn->Disconnect(); return; }   // 멈춘 세션에는 붙이지 않는다
        AttachClient(clientId, conn);
        FlushSends();
        return;
    }
    Post(SessionJob::Create(SessionJob::ATTACH, clientId, conn, 0, nullptr, 0), true);
}

void GameSession::PostDetach(int clientId, SessionClientConnection* conn)
{
    if (!V2::Get().queueSync)
    {
        std::unique_lock<std::recursive_mutex> lk(mtx, std::defer_lock);
        LockTimed(lk);
        DetachClient(clientId, conn);
        return;
    }
    Post(SessionJob::Create(SessionJob::DETACH, clientId, conn, 0, nullptr, 0), false);
}

void GameSession::PostPacket(int clientId, int packetType, const char* body, int bodySize)
{
    bool urgent = (packetType != (int)PacketType::PLAYER_INPUT);
    if (!V2::Get().queueSync)
    {
        std::unique_lock<std::recursive_mutex> lk(mtx, std::defer_lock);
        LockTimed(lk);
        HandlePacket(clientId, packetType, body, bodySize);
        if (urgent) FlushSends();               // 공격·채팅 응답은 다음 틱까지 미루지 않는다
        return;
    }
    Post(SessionJob::Create(SessionJob::PACKET, clientId, nullptr, packetType, body, bodySize), urgent);
}

// send=batch: 모아 둔 패킷을 연결마다 WSASend 한 번으로 보낸다
void GameSession::FlushSends()
{
    if (!V2::Get().batchSend) return;
    for (auto& kv : players)
    {
        SessionClientConnection* c = kv.second->conn;
        if (c) c->FlushSend();
    }
}

// sync=cqueue는 같은 자리에 락 큐를 쓴다 → 일감 큐 구조는 같고 자료구조만 다르다
SessionJob* GameSession::TakeJobs()
{
    return V2::Get().coarseQueue ? jobsCoarse.PopAll() : jobs.PopAll();
}

void GameSession::DrainJobs()
{
    SessionJob* j = TakeJobs();
    while (j)
    {
        SessionJob* next = j->next;
        switch (j->kind)
        {
        case SessionJob::ATTACH: AttachClient(j->clientId, j->conn); break;
        case SessionJob::DETACH: DetachClient(j->clientId, j->conn); break;
        case SessionJob::PACKET: HandlePacket(j->clientId, j->packetType, j->Body(), j->size); break;
        }
        SessionJob::Destroy(j);
        ++drainedJobs;
        j = next;
    }
}

void GameSession::FreeJobs(bool closeConns)
{
    SessionJob* j = TakeJobs();
    while (j)
    {
        SessionJob* next = j->next;
        // 처리 못 한 입장 요청의 연결은 닫는다 (세션을 가리킨 채 남지 않게)
        if (closeConns && j->kind == SessionJob::ATTACH && j->conn) j->conn->Disconnect();
        SessionJob::Destroy(j);
        j = next;
    }
}

GameSession::TickStats GameSession::TakeStats()
{
    TickStats s;
    s.ticks = statTicks.exchange(0, std::memory_order_relaxed);
    s.workUs = statWorkUs.exchange(0, std::memory_order_relaxed);
    s.jobs = statJobs.exchange(0, std::memory_order_relaxed);
    s.maxUs = statMaxUs.exchange(0, std::memory_order_relaxed);
    s.players = statPlayers.load(std::memory_order_relaxed);
    s.lockWaitUs = statLockWaitUs.exchange(0, std::memory_order_relaxed);
    s.lockCount = statLockCount.exchange(0, std::memory_order_relaxed);
    return s;
}

// 워커 스레드(인증)에서 호출 → 세션 상태 대신 원자 변수 명단만 읽는다
bool GameSession::IsAllowedPlayer(int clientId) const
{
    if (clientId == INT_MIN) return false;
    for (int i = 0; i < joinCount; ++i)
        if (joinIds[i].load(std::memory_order_acquire) == clientId) return true;
    return false;
}

// 틱 스레드 전용. 자진 이탈한 플레이어는 다시 인증되지 않게 한다.
void GameSession::RevokeJoin(int clientId)
{
    allowedPlayers.erase(clientId);
    for (int i = 0; i < joinCount; ++i)
        if (joinIds[i].load(std::memory_order_relaxed) == clientId)
            joinIds[i].store(INT_MIN, std::memory_order_release);
}

void GameSession::AttachClient(int clientId, SessionClientConnection *conn)
{
    // 자진 이탈 등으로 빠진 사람: 연결을 닫는다 (세션을 가리킨 채 남지 않게)
    if (!allowedPlayers.count(clientId)) { if (conn) conn->Disconnect(); return; }

    auto saved = disconnectedPlayers.find(clientId);
    if (saved != disconnectedPlayers.end())
    {
        players[clientId] = std::move(saved->second);
        disconnectedPlayers.erase(saved);
    }
    auto it = players.find(clientId);
    if (it != players.end())
    {
        // 재접속: 기존 entity에 새 conn 연결. 아직 열린 예전 연결은 닫는다
        // (그 끊김 통지는 연결 비교로 무시되고, 세션이 지워진 뒤 늦게 오는 일이 없다)
        if (it->second->conn && it->second->conn != conn) it->second->conn->Disconnect();
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
            for (auto &kv : players)
            {
                auto t2 = playerTeamMap.find(kv.first);
                int otherTeam = (t2 != playerTeamMap.end()) ? t2->second : 0;
                if (otherTeam == teamSlot)
                    ++slotInTeam;
            }

            teamSlot = teamSlot % (int)dungeon.assignedStartRooms.size();

            const StartRoom &sr = dungeon.assignedStartRooms[teamSlot];
            const Vec3 &spawn = (slotInTeam < (int)sr.playerSpawnPositions.size()) ? sr.playerSpawnPositions[slotInTeam]
                                                                                   : sr.teamAnchorPosition;
            pe->position = Vec3(spawn.x + dungeon.worldOffset.x, spawn.y, spawn.z + dungeon.worldOffset.z);
            pe->rotY = sr.spawnYawDegrees;
        }
        // 이름을 넣어야 다른 클라의 PLAYER_ENTER_VIEW에 표시된다.
        auto nIt = playerNameMap.find(clientId);
        if (nIt != playerNameMap.end())
        {
            std::strncpy(pe->playerName, nIt->second.c_str(), sizeof(pe->playerName) - 1);
            pe->playerName[sizeof(pe->playerName) - 1] = '\0';
        }

        pe->conn = conn;
        pe->joinedAtMs = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
        players[clientId] = std::move(pe);
        Log::Info("[Session %d] 입장: clientId=%d (현재 %d명) pos=(%.1f, %.1f, %.1f)", sessionId, clientId,
                  (int)players.size(), players[clientId]->position.x, players[clientId]->position.y,
                  players[clientId]->position.z);
    }

    everJoined = true;
    statPlayers.store((int)players.size(), std::memory_order_relaxed);

    // 6단계: 시야 처리 활성화.
    //   - 새 입장자에게: 시야 안 다른 플레이어/몬스터의 ENTER_VIEW 송신
    //   - 시야 안 다른 플레이어들에게: 새 입장자의 ENTER_VIEW 송신
    //   - 양쪽 viewedXxx 갱신
    im.OnPlayerJoin(*this, clientId);

    // 이미 월드에 있는 전리품 컨테이너 + 본인 인벤토리 동기화.
    auto &ab = players[clientId]->ab;
    if (abInitialGrants.insert(clientId).second)
    {
        ab.initialized = true;
        ab.randomState = (unsigned)mapSeed ^ (unsigned)clientId;
        Economy::Grant grant;grant.weapon=ItemHash::Of("sword_001");grant.item=ItemHash::Of("health_potion");grant.count=3;
        auto selected=economyGrants.find(clientId);if(selected!=economyGrants.end())grant=selected->second;
        auto& inventory=loot.GetInventory(clientId);ab.view.weapon=grant.weapon;inventory.Add(grant.weapon,1,false);inventory.Add(grant.item,grant.count,false);
        if(grant.freeWeapon)inventory.MarkFreeStarter(grant.weapon,1);if(grant.freeItem)inventory.MarkFreeStarter(grant.item,grant.count);
        if(auto weapon=AB::Find(grant.weapon))ab.weaponType=weapon->effect;else if(grant.weapon==ItemHash::Of("gun_001"))ab.weaponType=4;
        if(auto item=AB::Find(grant.item);item&&item->kind==1){ab.view.passive=grant.item;ab.passiveType=item->effect;ab.tier=item->tier;}
        ab.Recompute();players[clientId]->maxHp=players[clientId]->hp=ab.view.maxHp;
    }
    loot.SendAllLootTo(*this, clientId);
    EnvironmentSync(clientId);
    ABSync(clientId);

    // 본인 시작 위치 즉시 알림 (클라가 시작방 좌표로 transform 갱신용).
    // OnPlayerJoin은 다른 객체 ENTER만 처리. 본인 PLAYER_MOVE는 별도 송신.
    PlayerEntity *p = players[clientId].get();
    p->startPosResendTicks = 20; // 20틱(=1초) 동안 재송신 예약
    if (p && p->conn && p->conn->active)
    {
        long long ts = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
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

    // 전원이 다 붙었으면 여기서 동시에 시작한다.
    TryStartGame();
}

// 전원 입장 대기 배리어. force=true면 타임아웃으로 강제 시작.
void GameSession::TryStartGame(bool force)
{
    if (gameStarted.load())
        return;

    int joined = (int)players.size();
    int expected = (int)allowedPlayers.size();
    if (!force && joined < expected)
        return;

    gameStarted.store(true);
    Broadcast((int)PacketType::SESSION_START_GAME, nullptr, 0);
    Log::Info("[Session %d] 게임 시작 (입장 %d/%d%s)", sessionId, joined, expected, force ? ", 타임아웃 강제" : "");
}

void GameSession::DetachClient(int clientId, SessionClientConnection *conn)
{
    auto it = players.find(clientId);
    if (it == players.end())
        return;
    if (conn && it->second->conn != conn)
        return; // 이미 새 연결로 다시 붙음 (예전 연결의 늦은 끊김 통지)
    it->second->conn = nullptr; // 먼저 연결 해제 (이후 이 엔티티로의 송신 차단)

    // 시야 안 다른 플레이어들에게 LEAVE_VIEW 송신 + viewedPlayers에서 제거 (엔티티 살아있는 동안)
    im.OnPlayerLeave(*this, clientId);

    // 엔티티 완전 제거 → 캐릭터가 세션에서 완전히 빠짐 (재접속 미지원 단계).
    // 몬스터는 targetClientId(int)로 대상을 참조하므로 댕글링 포인터 없음.
    disconnectedPlayers[clientId] = std::move(it->second);
    players.erase(it);
    statPlayers.store((int)players.size(), std::memory_order_relaxed);

    Log::Info("[Session %d] 퇴장(엔티티 제거): clientId=%d", sessionId, clientId);
}

void GameSession::HandlePacket(int clientId, int packetType, const char *body, int bodySize)
{
    if(packetType==Skills::CommandPacket){SkillCommand(clientId,body,bodySize);return;}
    if (packetType == Env::TimeRequest)
    {
        if (bodySize != sizeof(std::int64_t)) return;
        auto player = GetPlayer(clientId);
        if (!player || !player->conn) return;
        Env::TimeReply reply{};
        std::memcpy(&reply.clientTimeMs, body, sizeof(reply.clientTimeMs));
        reply.serverTimeMs = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
        player->conn->SendPacket(Env::TimeResult, &reply, sizeof(reply));
        return;
    }
    if (packetType == AB::CommandPacket)
    {
        ABCommand(clientId, body, bodySize);
        return;
    }
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

    case PacketType::EXTRACTION_REQUEST:
        HandleExtraction(clientId, body, bodySize);
        break;

    case PacketType::ITEM_PICKUP_REQUEST:
        HandleItemPickup(clientId, body, bodySize);
        break;

    case PacketType::CHAT_SEND:
        HandleChat(clientId, body, bodySize);
        break;

    case PacketType::DEBUG_TELEPORT_BOSS: // [치트] 배포 시 제거
        HandleDebugTeleportBoss(clientId);
        break;

    case PacketType::DEBUG_TELEPORT_EXIT: // [치트] 배포 시 제거
        HandleDebugTeleportExit(clientId);
        break;

    default:
        Log::Warn("[Session %d] 알 수 없는 인게임 패킷 type=%d from cid=%d", sessionId, packetType, clientId);
        break;
    }
}

void GameSession::Broadcast(int packetType, const void *body, int size, int exceptClientId)
{
    // v2: 틱 스레드에서만 호출되므로 락이 필요 없다.
    for (auto &kv : players)
    {
        if (kv.first == exceptClientId)
            continue;
        if (kv.second->conn && kv.second->conn->active)
        {
            kv.second->conn->SendPacket(packetType, body, size);
        }
    }
}

// ============================================================================
//  내부: PLAYER_INPUT 처리
// ============================================================================

void GameSession::HandlePlayerInput(int clientId, const char *body, int bodySize, float dt)
{
    if (bodySize < (int)sizeof(PlayerInput))
        return;
    const PlayerInput *in = reinterpret_cast<const PlayerInput *>(body);

    PlayerEntity *p = GetPlayer(clientId);
    if (!p)
        return;
    if (!p->IsActiveInWorld())
        return; // 사망/탈출한 플레이어 입력 무시

    // 클라가 보낸 위치를 PositionValidator로 검증.
    // - 텔레포트 거리 초과 → 직전 위치 유지
    // - 벽 통과 시도 → 슬라이딩 또는 직전 위치
    // 클라 walkSpeed=6, sprintSpeed=10. 10을 cap으로 사용.
    if (!std::isfinite(in->posX) || !std::isfinite(in->posY) || !std::isfinite(in->posZ) || !std::isfinite(in->rotY))
        return;
    Vec3 attempted(in->posX, in->posY, in->posZ);
    if(in->timestamp>0&&in->timestamp<=p->lastInputTimestamp)return;
    long long now=duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
    // In-flight input from before F9/F10 must not drag the server back towards the old room.
    if(p->teleportInputDeadlineMs>now&&p->position.DistanceSqXZ(attempted)>4.f){SendPositionState(*p,true);return;}
    p->teleportInputDeadlineMs=0;
    Vec3 validated = PositionValidator::ValidateMove(dungeon, p->position, attempted, 10.0f * p->ab.effects.Slow(), dt);
    if (p->ab.effects.Stunned() || Skills::LocksMovement(p->skill))
        validated = p->position;

    p->ab.lastSprintInput = in->sprint && p->position.DistanceSqXZ(validated) > .0001f ? .15f : 0.f;
    if (in->sprint && p->ab.view.stamina <= 0 && p->ab.sprintFree <= 0)
        validated = PositionValidator::ValidateMove(dungeon, p->position, validated, 6.f, dt);
    p->ApplyInput(validated.x, validated.y, validated.z, p->skill.view.castLeft>0?p->rotY:in->rotY, in->moveX, in->moveY, in->sprint, in->timestamp, dt);
    if(validated.DistanceSqXZ(attempted)>.0025f)SendPositionState(*p,false);
}

// ============================================================================
//  내부: PLAYER_ATTACK_REQUEST 처리 (7단계)
// ============================================================================

void GameSession::HandlePlayerAttack(int clientId, const char *body, int bodySize)
{
    if (bodySize < (int)sizeof(PlayerAttackRequest))
        return;
    const PlayerAttackRequest *req = reinterpret_cast<const PlayerAttackRequest *>(body);

    cr.HandleAttack(*this, clientId, *req);
}

// ============================================================================
//  내부: 채팅
//
//  채널
//   CHAT_PARTY : 이 세션(같은 던전)에 있는 전원
//   CHAT_TEAM  : 같은 팀 슬롯인 인원만
//   CHAT_ALL   : 세션엔 없는 채널이라 PARTY로 강등한다.
//
//  탈출/사망한 플레이어도 결과창에서 대화를 이어갈 수 있어야 하므로
//  IsActiveInWorld가 아니라 접속 여부만 본다.
// ============================================================================

void GameSession::HandleChat(int clientId, const char *body, int bodySize)
{
    if (bodySize < (int)sizeof(ChatSendRequest))
        return;
    const ChatSendRequest *req = reinterpret_cast<const ChatSendRequest *>(body);

    char text[MAX_CHAT_TEXT];
    std::memcpy(text, req->text, MAX_CHAT_TEXT);
    text[MAX_CHAT_TEXT - 1] = '\0';
    if (text[0] == '\0')
        return;

    if (players.find(clientId) == players.end())
        return;

    int channel = (req->channel == CHAT_TEAM) ? CHAT_TEAM : CHAT_PARTY;

    auto tIt = playerTeamMap.find(clientId);
    int senderTeam = (tIt != playerTeamMap.end()) ? tIt->second : TEAM_UNASSIGNED;

    ChatMessageData msg{};
    msg.channel = channel;
    msg.senderId = clientId;
    msg.senderTeam = senderTeam;
    auto nIt = playerNameMap.find(clientId);
    if (nIt != playerNameMap.end())
        std::strncpy(msg.senderName, nIt->second.c_str(), sizeof(msg.senderName) - 1);
    std::strncpy(msg.text, text, sizeof(msg.text) - 1);

    int sent = 0;
    for (auto &kv : players)
    {
        if (channel == CHAT_TEAM && !IsSameTeam(clientId, kv.first))
            continue;
        PlayerEntity &p = *kv.second;
        if (!p.conn || !p.conn->active)
            continue;
        p.conn->SendPacket((int)PacketType::CHAT_MESSAGE, &msg, sizeof(msg));
        ++sent;
    }

    Log::Info("[채팅/%s] sid=%d %s: %s (%d명)", channel == CHAT_TEAM ? "팀" : "파티", sessionId, msg.senderName,
              msg.text, sent);
}

void GameSession::HandleItemPickup(int clientId, const char *body, int bodySize)
{
    if (bodySize < (int)sizeof(ItemPickupRequest))
        return;
    const ItemPickupRequest *req = reinterpret_cast<const ItemPickupRequest *>(body);

    loot.HandlePickupRequest(*this, clientId, *req);
}

// 몬스터가 죽었을 때 전리품 생성. 전투 코드에서 호출된다.
void GameSession::OnMonsterKilled(int monsterId, int killerClientId)
{
    auto &monsters = worldSim.GetMonsters();
    auto it = monsters.find(monsterId);
    if (it == monsters.end() || it->second->hp > 0)
        return;
    if (!abRewardedMonsters.insert(monsterId).second)
        return;
    // 전적 집계 (세션 종료 시 로비 → DB로 넘어간다)
    if (PlayerEntity *killer = GetPlayer(killerClientId))
    {
        ++killer->monsterKills;
        killer->ab.bossKills += it->second->kind == MONSTER_BOSS ? 1 : 0;
        killer->ab.Award(it->second->kind == MONSTER_BOSS ? 250 : it->second->kind == MONSTER_ELITE ? 60 : 25);
        if (killer->ab.passiveType == 8)
            killer->ab.view.stamina =
                (std::min)(killer->ab.view.maxStamina, killer->ab.view.stamina + 4 + 4 * killer->ab.tier);
        ABSync(killerClientId);
    }

    loot.SpawnMonsterLoot(*this, monsterId, it->second->position, it->second->kind, mapSeed);
}

// ============================================================================
//  [디버그 치트] 탈출 방 근처로 이동          ※ 배포 시 이 함수 통째로 삭제
//
//  위치는 서버가 권위라 클라가 스스로 순간이동하면 PositionValidator가
//  되돌린다. 그래서 서버가 직접 좌표를 옮기고 PLAYER_MOVE로 통보한다.
//
//  착지 지점: 탈출 방의 floorTiles 중 방 중심에 가장 가까운 타일.
//  bounds 중심을 그대로 쓰면 기둥/장애물(blockedTiles) 위에 낄 수 있다.
// ============================================================================

void GameSession::HandleDebugTeleportExit(int clientId)
{
    if (!DEBUG_ALLOW_TELEPORT)
        return;


    if (dungeon.exitRoomId < 0)
    {
        Log::Warn("[치트] 이 던전에 탈출 방이 없음 (createExitRoom 확인)");
        return;
    }

    Vec3 dest;
    if (!RoomCenterStandPos(dungeon.exitRoomId, dest))
    {
        Log::Warn("[치트] 탈출 방 %d 의 바닥 타일을 찾지 못함", dungeon.exitRoomId);
        return;
    }
    DebugTeleportTo(clientId, dest, "탈출 방");
}

// F10 — 보스 옆으로. 보스가 살아 있으면 그 옆, 없으면 보스 방 중앙.
void GameSession::HandleDebugTeleportBoss(int clientId)
{
    if (!DEBUG_ALLOW_TELEPORT)
        return;


    for (auto &kv : worldSim.GetMonsters())
    {
        const MonsterEntity &m = *kv.second;
        if (m.kind != MONSTER_BOSS || m.aiState == AI_DEAD)
            continue;

        Vec3 dest = m.position;
        dest.x += 3.0f; // 몸통에 끼지 않게 옆으로 비켜 세운다
        DebugTeleportTo(clientId, dest, "보스 옆");
        return;
    }

    for (const Room &r : dungeon.rooms)
    {
        if (r.type != ROOM_TYPE_BOSS)
            continue;
        Vec3 dest;
        if (!RoomCenterStandPos(r.id, dest))
            continue;
        DebugTeleportTo(clientId, dest, "보스 방");
        return;
    }

    Log::Warn("[치트] 살아 있는 보스도, 보스 방도 찾지 못함");
}

// 방 중심에 가장 가까운 바닥 타일 위 좌표.
// bounds 중심을 그대로 쓰면 기둥/장애물 위에 낄 수 있어서 타일로 스냅한다.
bool GameSession::RoomCenterStandPos(int roomId, Vec3 &out) const
{
    const Room *room = nullptr;
    for (const Room &r : dungeon.rooms)
        if (r.id == roomId)
        {
            room = &r;
            break;
        }

    if (!room || room->floorTiles.empty())
        return false;

    float cx = (room->bounds.xMin() + room->bounds.xMax()) * 0.5f;
    float cz = (room->bounds.zMin() + room->bounds.zMax()) * 0.5f;

    const IntVec3 *best = nullptr;
    float bestDistSq = 0.f;
    for (const IntVec3 &t : room->floorTiles)
    {
        if(!PositionValidator::IsFloorPosition(dungeon,dungeon.TileToStandPosition(t)))continue;
        float dx = (float)t.x - cx;
        float dz = (float)t.z - cz;
        float dsq = dx * dx + dz * dz;
        if (!best || dsq < bestDistSq)
        {
            best = &t;
            bestDistSq = dsq;
        }
    }
    if (!best)
        return false;

    out = dungeon.TileToStandPosition(*best);
    return true;
}

// 서버가 좌표를 옮기고 PLAYER_MOVE로 통보한다. 클라는 이 좌표로 스냅한다.
void GameSession::DebugTeleportTo(int clientId, const Vec3 &dest, const char *label)
{
    PlayerEntity *p = GetPlayer(clientId);
    if (!p)
        return;
    if (!p->IsActiveInWorld())
    {
        Log::Warn("[치트] cid=%d 이동 불가 (사망/탈출 상태)", clientId);
        return;
    }

    Vec3 landing=dest;
    if(!PositionValidator::IsFloorPosition(dungeon,landing)){
        bool found=false;
        for(int radius=1;radius<=5&&!found;radius++)for(int x=-radius;x<=radius&&!found;x++)for(int z=-radius;z<=radius&&!found;z++){
            Vec3 candidate=dest+Vec3((float)x,0,(float)z);
            if(PositionValidator::IsFloorPosition(dungeon,candidate)){landing=candidate;found=true;}
        }
        if(!found)return;
    }
    p->position = landing;
    p->speed = 0.f;
    p->extractionHoldSec = 0.f;
    p->startPosResendTicks = 10; // 클라가 놓치지 않도록 재송신 예약
    p->teleportInputDeadlineMs=duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count()+4000;

    if (p->conn && p->conn->active)
    {
        long long ts = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
        PlayerMove pm{};
        pm.clientId = clientId;
        pm.posX = landing.x;
        pm.posY = landing.y;
        pm.posZ = landing.z;
        pm.rotY = p->rotY;
        pm.speed = 0.f;
        pm.animState = 0;
        pm.timestamp = ts;
        p->conn->SendPacket((int)PacketType::PLAYER_MOVE, &pm, sizeof(pm));
    }

    SendPositionState(*p,true);
    Log::Warn("[치트] cid=%d %s(으)로 이동: (%.1f, %.1f, %.1f)", clientId, label, landing.x, landing.y, landing.z);
}

// ============================================================================
//  내부: 탈출 (서버 권위 판정)
//
//  클라는 포탈에서 holdDuration(7초)을 채우면 EXTRACTION_REQUEST를 보낸다.
//  서버는 이걸 신뢰하지 않고 자체 누적한 체류 시간(extractionHoldSec)으로 검증.
//  체류 시간은 UpdateExtractionHold가 매 틱 서버 권위 위치 기준으로 갱신한다.
//
//  성공 시:
//   - extracted 플래그 → 이후 시야/전투/몬스터 타겟에서 제외 (IsActiveInWorld)
//   - 본인에게 EXTRACTION_RESULT, 나머지에게 PLAYER_EXTRACTED
//   - 다른 클라 화면에서 제거 (OnPlayerLeave → PLAYER_LEAVE_VIEW)
//   - 남은 인원 0이면 세션 종료
// ============================================================================

void GameSession::HandleExtraction(int clientId, const char *body, int bodySize)
{
    if (bodySize < (int)sizeof(ExtractionRequest))
        return;

    PlayerEntity *p = GetPlayer(clientId);
    if (!p)
        return;

    ExtractionResult res{};
    res.itemCount = loot.GetTotalItemCount(clientId); // 서버 권위 인벤토리 기준
    res.heldSec = p->extractionHoldSec;

    // 1) 상태 검사 (치트로도 우회 불가 — 죽었거나 이미 나간 건 탈출 불가)
    if (p->extracted)
        res.failReason = EXTRACT_FAIL_ALREADY;
    else if (p->hp <= 0)
        res.failReason = EXTRACT_FAIL_DEAD;
    else if (DEBUG_INSTANT_EXTRACT)
    {
        // [치트] 위치/체류시간 검증 생략
        res.failReason = EXTRACT_OK;
        Log::Warn("[치트] cid=%d 즉시 탈출 (위치/시간 검증 생략)", clientId);
    }
    else if (dungeon.exitRoomId < 0)
        res.failReason = EXTRACT_FAIL_NO_EXIT_ROOM;
    // 2) 위치 검사: 서버 권위 위치가 실제 탈출 방 안인가
    else if (dungeon.RoomIdAt(p->position) != dungeon.exitRoomId)
        res.failReason = EXTRACT_FAIL_NOT_IN_ZONE;
    // 3) 체류 시간 검사: 클라가 보낸 시간이 아니라 서버 누적치로 판정
    else if (p->extractionHoldSec < EXTRACTION_HOLD_SEC * EXTRACTION_HOLD_TOLERANCE)
        res.failReason = EXTRACT_FAIL_HOLD_TOO_SHORT;
    else
        res.failReason = EXTRACT_OK;

    res.success = (res.failReason == EXTRACT_OK) ? 1 : 0;
    if(res.success)FinishEconomy(clientId,true);   // 저장은 로비가 DB 스레드에서 한다

    if (p->conn && p->conn->active)
    {
        if (res.success)
        {
            AB::Extraction summary;
            loot.GetInventory(clientId).FillExtraction(summary);
            summary.bossKillCount = p->ab.bossKills;
            p->conn->SendPacket(68, &summary, sizeof(summary));
        }
        p->conn->SendPacket((int)PacketType::EXTRACTION_RESULT, &res, sizeof(res));
    }

    if (!res.success)
    {
        Log::Warn("[Session %d] 탈출 거부: cid=%d reason=%d held=%.1fs room=%d(exit=%d)", sessionId, clientId,
                  res.failReason, p->extractionHoldSec, dungeon.RoomIdAt(p->position), dungeon.exitRoomId);
        return;
    }

    // 4) 탈출 확정
    p->extracted = true;
    im.OnPlayerLeave(*this, clientId); // 다른 클라 화면에서 제거

    int remaining = 0;
    for (auto &kv : players)
        if (kv.second->IsActiveInWorld())
            ++remaining;

    PlayerExtracted pe{};
    pe.clientId = clientId;
    pe.remainingPlayers = remaining;
    Broadcast((int)PacketType::PLAYER_EXTRACTED, &pe, sizeof(pe), clientId);

    Log::Info("[Session %d] 탈출 성공: cid=%d held=%.1fs 남은인원=%d", sessionId, clientId, p->extractionHoldSec,
              remaining);

    CheckSessionEnd();
}

// 매 틱 호출. 서버 권위 위치 기준으로 탈출 방 체류 시간 누적.
void GameSession::UpdateExtractionHold(float dt)
{
    if (dungeon.exitRoomId < 0)
        return;

    for (auto &kv : players)
    {
        PlayerEntity &p = *kv.second;
        if (!p.IsActiveInWorld())
        {
            p.extractionHoldSec = 0.f;
            continue;
        }

        if (dungeon.RoomIdAt(p.position) == dungeon.exitRoomId)
            p.extractionHoldSec += dt;
        else
            p.extractionHoldSec = 0.f; // 방을 벗어나면 리셋 (클라 UI와 동일)
    }
}

// 월드에 남은 인원이 0이면 세션 종료 통보. 중복 송신 방지.
void GameSession::CheckSessionEnd()
{
    if (sessionEndSignaled.load())
        return;

    int reason = 0; // 정상 종료
    if (players.empty())
    {
        // v2: 전원이 접속을 끊으면 세션을 정리한다 (v1은 빈 세션이 영원히 남았다).
        //     끊긴 플레이어는 다시 붙을 수 있으므로 유예 시간을 둔다.
        if (!everJoined && !gameStarted.load())
            return;                                 // 아직 입장 대기 중
        auto now = steady_clock::now();
        if (emptySince == steady_clock::time_point{})
            emptySince = now;
        if (now - emptySince < seconds(EMPTY_GRACE_SEC))
            return;
        reason = everJoined ? 1 : 3;                // 1 = 전원 접속 종료, 3 = 입장 없음
    }
    else
    {
        emptySince = steady_clock::time_point{};
        for (auto &kv : players)
            if (kv.second->IsActiveInWorld())
                return; // 아직 진행 중
    }

    sessionEndSignaled.store(true);

    SessionEnded se{};
    se.reason = reason;
    Broadcast((int)PacketType::SESSION_ENDED, &se, sizeof(se), 0);

    int survivors = 0;
    for (auto &kv : players)
        if (kv.second->extracted)
            ++survivors;

    int total = (int)players.size();
    Log::Info("[Session %d] 종료: %s (탈출 %d / 전체 %d)", sessionId,
              reason == 0 ? "전원 탈출/사망" : (reason == 1 ? "전원 접속 종료" : "입장 없음"), survivors, total);

    // 플레이어별 전적을 모아 로비로 넘긴다. 로비가 DB에 한 줄씩 넣는다.
    long long nowMs = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();

    std::vector<IpcPlayerResult> results;
    results.reserve(players.size());
    for (auto &kv : players)
    {
        const PlayerEntity &p = *kv.second;
        if (p.playerName[0] == '\0')
            continue; // 이름 없으면 DB에서 못 찾는다

        IpcPlayerResult r{};
        std::strncpy(r.playerName, p.playerName, sizeof(r.playerName) - 1);
        r.extracted = p.extracted ? 1 : 0;
        r.playSeconds = (p.joinedAtMs > 0) ? (int)((nowMs - p.joinedAtMs) / 1000) : 0;
        r.monsterKills = p.monsterKills;
        r.playerKills = p.playerKills;
        results.push_back(r);

        if ((int)results.size() >= MAX_SESSION_PLAYERS)
            break;
    }

    // 정산 보고를 종료 보고보다 먼저 넣는다 (같은 큐라 순서대로 도착한다)
    CloseEconomy();

    // 로비에 종료 보고 → 로비가 방 정리 + 클라 상태 복귀 + 전적 저장.
    // 실패해도 세션 종료 자체는 진행된다 (LobbyReporter가 경고만 남김).
    if (reporter)
        reporter->ReportSessionEnded(sessionId, se.reason, total, survivors, results);
}

void GameSession::AddPlayerKill(int killerClientId, int victimClientId)
{
    auto* killer=GetPlayer(killerClientId);auto* victim=GetPlayer(victimClientId);
    if(!killer||!victim||killerClientId==victimClientId||victim->hp>0||IsSameTeam(killerClientId,victimClientId)||!rewardedPlayerDeaths.insert(victimClientId).second)return;
    ++killer->playerKills;
    killer->ab.Award(50*std::clamp(victim->ab.view.level,1,AB::MaxLevel));
    ABSync(killerClientId);
}

PlayerEntity *GameSession::GetPlayer(int clientId)
{
    auto it = players.find(clientId);
    if (it == players.end())
        return nullptr;
    return it->second.get();
}

// ============================================================================
//  틱 루프 (50ms 주기)
// ============================================================================

void GameSession::TickLoop()
{
    const V2::Config& cfg = V2::Get();
    constexpr int TICK_MS = 50;
    const auto tick = milliseconds(TICK_MS);
    auto last = steady_clock::now();
    auto next = last + tick;

    while (running)
    {
        // tick=sleep  : v1처럼 틱이 끝난 뒤 50ms 기다린다 (일이 길수록 주기가 늘어난다)
        // tick=precise: 50ms 마감 시각을 기준으로 기다린다 (주기가 일정)
        if (!cfg.preciseTick) next = steady_clock::now() + tick;
        WaitUntil(next);
        if (!running) break;

        auto t0 = steady_clock::now();
        float dt = duration<float>(t0 - last).count();
        last = t0;

        {
            std::unique_lock<std::recursive_mutex> lk(mtx, std::defer_lock);
            if (!cfg.queueSync) LockTimed(lk);   // 락 모드: v1처럼 틱 전체를 세션 락 안에서
            else DrainJobs();                    // 큐 모드: 쌓인 입력을 먼저 반영
            TickStep(dt);
            FlushSends();
        }

        // 내부 측정 (회수 스레드가 1초마다 가져간다)
        unsigned us = (unsigned)duration_cast<microseconds>(steady_clock::now() - t0).count();
        statTicks.fetch_add(1, std::memory_order_relaxed);
        statWorkUs.fetch_add(us, std::memory_order_relaxed);
        statJobs.fetch_add(drainedJobs, std::memory_order_relaxed);
        drainedJobs = 0;
        if (us > statMaxUs.load(std::memory_order_relaxed)) statMaxUs.store(us, std::memory_order_relaxed);

        if (cfg.preciseTick)
        {
            next += tick;
            auto now = steady_clock::now();
            if (next < now) next = now + tick;   // 한 틱 넘게 밀리면 따라잡지 않고 다시 맞춘다
        }
    }
}

// 마감 시각까지 기다린다. 큐 모드면 그 사이 급한 일감(공격·채팅 등)을 바로 처리한다.
void GameSession::WaitUntil(steady_clock::time_point deadline)
{
    const V2::Config& cfg = V2::Get();
    for (;;)
    {
        auto left = deadline - steady_clock::now();
        if (left <= steady_clock::duration::zero()) return;

        HANDLE hs[2];
        DWORD n = 0;
        bool useTimer = cfg.preciseTick && hTimer;
        if (useTimer)
        {
            LARGE_INTEGER due;
            due.QuadPart = -(LONGLONG)(duration_cast<nanoseconds>(left).count() / 100);  // 상대 시간(100ns 단위)
            if (due.QuadPart >= 0) due.QuadPart = -1;
            SetWaitableTimer((HANDLE)hTimer, &due, 0, nullptr, nullptr, FALSE);
            hs[n++] = (HANDLE)hTimer;
        }
        if (hWake) hs[n++] = (HANDLE)hWake;
        if (n == 0) { std::this_thread::sleep_for(left); return; }

        DWORD timeout = useTimer ? INFINITE
            : (DWORD)duration_cast<milliseconds>(left + microseconds(999)).count();
        DWORD r = WaitForMultipleObjects(n, hs, FALSE, timeout);
        if (!running) return;

        if (r == WAIT_TIMEOUT) continue;                       // 남은 시간 다시 계산
        if (r == WAIT_FAILED) { std::this_thread::sleep_for(left); return; }
        if (useTimer && r == WAIT_OBJECT_0) return;            // 마감 도달
        if (hWake && r == WAIT_OBJECT_0 + (n - 1) && cfg.queueSync)
        {
            wakePending.store(false, std::memory_order_release);  // 꺼내기 전에 내려야 새 신호를 놓치지 않는다
            // 마감 2ms 전이면 틱 시작 때 같이 처리한다 (급한 일감이 틱을 밀어내지 않게)
            if (deadline - steady_clock::now() > milliseconds(2))
            {
                DrainJobs();                                   // 급한 일감
                FlushSends();
            }
        }
    }
}

void GameSession::TickStep(float dt)
{
    std::vector<WorldSimulation::AttackEvent> attackEvents;
    std::vector<MonsterAttackEvent> attackPackets;
    std::vector<PlayerDied> deathPackets;

    // v2: 틱 스레드 혼자 세션을 소유하므로 락 없이 진행한다.
    {
        // 0) 전원 입장 전에는 월드를 멈춘다. (먼저 들어온 사람만 맞고 시간이 흐르는 것 방지)
        //    시야 갱신은 그대로 해서 서로 보이게 두고, AI/탈출/종료 판정만 보류한다.
        if (!gameStarted.load())
        {
            preStartElapsed += dt;
            if (preStartElapsed >= START_TIMEOUT_SEC)
                TryStartGame(true);
            im.UpdateAll(*this);
            return;
        }

        // 1) 몬스터 AI (위치 갱신 + 공격 결정)
        ABTick(dt);
        SkillTick(dt);
        EnvironmentTick(dt);
        StormTick(dt);
        cr.Update(*this, dt);
        worldSim.Step(dt, players, dungeon, attackEvents);
        for(const auto& gas:worldSim.BreathUpdates())
            Broadcast((int)PacketType::MUMMY_BREATH_STATE,&gas,sizeof(gas));
        for(const auto& state:worldSim.bossStates)Broadcast(70,&state,sizeof(state));
        for(const auto& pattern:worldSim.patternStarts)Broadcast(pattern.pattern==4?241:71,&pattern,sizeof(pattern));
        for(const auto& resolve:worldSim.patternEnds){
            auto actor=worldSim.GetMonsters().find(resolve.monsterId);
            Broadcast(actor!=worldSim.GetMonsters().end()&&actor->second->kind==MONSTER_ELITE?242:72,&resolve,sizeof(resolve));
        }
        for(auto& entry:players)if(entry.second->hp<=0)FinishEconomy(entry.first,false);

        // 2) 몬스터 공격 결과 처리: 플레이어 HP 차감 + 패킷 데이터 준비
        for (const auto &ev : attackEvents)
        {
            auto it = players.find(ev.victimClientId);
            if (it == players.end())
                continue;
            PlayerEntity &victim = *it->second;
            if (victim.hp <= 0)
                continue;

            auto attackingMonster = worldSim.GetMonsters().find(ev.monsterId);
            bool gasAttack = ev.poison;
            if (ev.damage > 0 && victim.ab.parry > 0 && !gasAttack && ev.parryable)
            {
                auto mi = worldSim.GetMonsters().find(ev.monsterId);
                if (mi != worldSim.GetMonsters().end())
                {
                    auto d = mi->second->position - victim.position;
                    float yaw = victim.rotY * 3.14159265f / 180;
                    if (d.x * std::sin(yaw) + d.z * std::cos(yaw) > 0)
                    {
                        victim.ab.parry = 0;
                        victim.ab.counter = 3;
                        ++victim.ab.parrySerial;
                        mi->second->effects.Apply(2, 1, victim.clientId, AB::ParryStun / .3f);
                        mi->second->pendingHitTime = 0;
                        ABSync(victim.clientId);
                        continue;
                    }
                }
            }
            if (ev.damage > 0)
                victim.ab.idle = 0;
            int appliedDamage = victim.ab.shield > 0 ? int(ev.damage * (1.f - victim.ab.shieldReduction)) : ev.damage;
            victim.InterruptExtractionOnDamage(appliedDamage);
            victim.hp -= appliedDamage;
            if (appliedDamage > 0 && victim.hp > 0 && victim.ab.immune <= 0)
            {
                const auto attacker = worldSim.GetMonsters().find(ev.monsterId);
                if (attacker != worldSim.GetMonsters().end())
                {
                    int element = attacker->second->kind == MONSTER_MUMMY && !gasAttack ? -1 : attacker->second->attackElement;
                    victim.ab.effects.Apply(element, element == 0 ? 3 : element == 3 ? 2 : 1, -ev.monsterId, gasAttack ? .5f : 1.f);
                    ABSync(victim.clientId);
                }
            }
            if (victim.hp < 0)
                victim.hp = 0;

            // 7단계 후속: 몬스터 → 플레이어 공격 로그 (콘솔에서 추적 가능)
            Log::Info("[MonsterAttack] mid=%d hit cid=%d dmg=%d hpAfter=%d%s", ev.monsterId, ev.victimClientId,
                      ev.damage, victim.hp, victim.hp <= 0 ? " [PLAYER_DIED]" : "");

            MonsterAttackEvent mae{};
            mae.monsterId = ev.monsterId;
            mae.victimClientId = ev.victimClientId;
            mae.damage = appliedDamage;
            mae.victimHpAfter = victim.hp;
            attackPackets.push_back(mae);

            if (victim.hp <= 0)
            {
                PlayerDied pd{};
                pd.victimId = ev.victimClientId;
                pd.killerId = -ev.monsterId; // 음수 = 몬스터 ID
                deathPackets.push_back(pd);
            }
        }

        // 2-a) 탈출 방 체류 시간 누적 (서버 권위 위치 기준).
        //      EXTRACTION_REQUEST가 오면 이 값으로 검증한다.
        UpdateExtractionHold(dt);

        // 2-b) 원거리 투사체 이동 + 충돌 처리 (활/총).
        //      벽/몬스터/플레이어 명중 시 데미지 + 소멸 패킷을 자체 broadcast.
        projSystem.Update(*this, dt);

        // 본인 시작 위치 재송신 (원격 클라는 active가 늦게 켜져 단발 송신을 놓침)
        for (auto &kv : players)
        {
            PlayerEntity &pe = *kv.second;
            if (pe.startPosResendTicks > 0 && pe.conn && pe.conn->active)
            {
                long long ts = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
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

        // Every damage source (melee, projectile, monster, status, storm, traps) converges here.
        for (auto& entry : players) if (entry.second->hp <= 0 && !entry.second->extracted) {
            loot.SpawnPlayerLoot(*this, entry.first, entry.second->position);
            FinishEconomy(entry.first, false);
        }

        // 3) 시야 갱신 + ENTER/LEAVE/MOVE 패킷 송신.
        //    각 플레이어의 viewedXxx를 새 시야와 비교하여 차이를 처리.
        //    PLAYER_MOVE/MONSTER_MOVE는 시야 안 클라에게만 송신 (대역폭 절약).
        im.UpdateAll(*this);

        // 4) 전원 탈출/사망 여부 확인 (사망으로 세션이 끝나는 경우 포함).
        CheckSessionEnd();
    }

    // 시야 무관한 중요 이벤트는 전체 broadcast (몬스터 공격, 사망).
    for (const auto &mae : attackPackets)
        Broadcast((int)PacketType::MONSTER_ATTACK_EVENT, &mae, sizeof(mae), 0);
    for (const auto &pd : deathPackets)
        Broadcast((int)PacketType::PLAYER_DIED, &pd, sizeof(pd), 0);
}
// 정산은 로비에 보고만 한다 (Lock-free 큐 → 송신 스레드). 저장은 로비가 하므로 틱 스레드는 기다리지 않는다.
bool GameSession::FinishEconomy(int clientId,bool extracted){
    if(economyRun.empty()||!economyGrants.count(clientId)||!economyFinished.insert(clientId).second)return true;
    auto items=extracted?loot.GetInventory(clientId).PersistentItems():std::map<int,int>{};
    if(reporter)reporter->ReportEconomy(sessionId,economyRun,clientId,false,items);
    return true;
}
// 정산 안 된 인원 마무리: 입장도 못 했으면 장비 반환, 들어왔다면 판이 끝나며 잃는다.
void GameSession::CloseEconomy(){
    if(economyRun.empty())return;
    for(auto& grant:economyGrants){
        if(!economyFinished.insert(grant.first).second)continue;
        bool neverJoined=!abInitialGrants.count(grant.first);
        if(reporter)reporter->ReportEconomy(sessionId,economyRun,grant.first,neverJoined,{});
    }
}

void GameSession::ABSync(int cid, bool inventoryChanged)
{
    auto *p = GetPlayer(cid);
    if (!p)
        return;
    p->ab.Recompute();
    p->maxHp = p->ab.view.maxHp;
    p->hp = (std::min)(p->hp, p->maxHp);
    p->ab.ObserveHealth(p->hp,p->maxHp);
    p->ab.Recompute();
    p->ab.view.hp = p->hp;
    SkillSync(cid);
    if (!p->conn)
        return;
    p->conn->SendPacket(AB::StatePacket, &p->ab.view, sizeof(AB::Snapshot));
    if (inventoryChanged)
    {
        InventorySyncData inv{};
        loot.GetInventory(cid).FillSyncPacket(inv);
        p->conn->SendPacket((int)PacketType::INVENTORY_SYNC, &inv, sizeof(inv));
    }
    auto sendActor = [&](PlayerEntity &actor) {
        AB::ActorState state;
        state.entityId = actor.clientId;
        state.weapon = actor.ab.view.weapon;
        state.statusMask = actor.IsActiveInWorld() ? actor.ab.effects.Mask() : 0;
        state.parrySerial = actor.ab.parrySerial;
        state.range = actor.ab.view.range;
        state.attackSpeed = actor.ab.view.attackSpeed;
        p->conn->SendPacket(AB::ActorPacket, &state, sizeof(state));
        AB::Presentation visual;
        visual.entityId=actor.clientId; visual.team=playerTeamMap.at(actor.clientId);
        visual.action=actor.ab.defenseLeft>0 && actor.IsActiveInWorld() && !actor.ab.effects.Stunned()?actor.ab.defenseAction:0;
        visual.serial=actor.ab.defenseSerial; visual.remaining=actor.ab.defenseLeft;
        visual.duration=actor.ab.defenseDuration; visual.dx=actor.ab.defenseX; visual.dz=actor.ab.defenseZ;
        p->conn->SendPacket(AB::PresentationPacket, &visual, sizeof(visual));
        if(actor.skill.view.castLeft>0){
            Skills::Event cast;cast.entityId=actor.clientId;cast.serial=actor.skill.view.serial;cast.skill=actor.skill.view.skill;cast.phase=1;
            cast.x=actor.skill.origin.x;cast.y=actor.skill.origin.y;cast.z=actor.skill.origin.z;
            cast.dx=actor.skill.direction.x;cast.dz=actor.skill.direction.z;cast.remaining=actor.skill.view.castLeft;
            cast.weapon=actor.skill.weapon;cast.area=actor.skill.payload.area;cast.windup=actor.skill.windup;
            p->conn->SendPacket(Skills::EventPacket,&cast,sizeof(cast));
        }
    };
    sendActor(*p);
    for (int id : p->viewedPlayers)
    {
        auto *other = GetPlayer(id);
        if (other)
            sendActor(*other);
    }
    for (int id : p->viewedMonsters)
    {
        auto it = worldSim.GetMonsters().find(id);
        if (it == worldSim.GetMonsters().end())
            continue;
        AB::ActorState state;
        state.entityId = -id;
        state.statusMask = it->second->hp > 0 ? it->second->effects.Mask() | ((it->second->attackElement + 1) << 8) : 0;
        state.attackSpeed = it->second->effects.Slow();
        p->conn->SendPacket(AB::ActorPacket, &state, sizeof(state));
    }
}
void GameSession::ABCommand(int cid, const char *body, int size)
{
    auto *p = GetPlayer(cid);
    if (!p || size != sizeof(AB::Command))
        return;
    AB::Command c{};
    std::memcpy(&c, body, sizeof(c));
    auto &a = p->ab;
    if (c.version != AB::Version || c.sequence <= a.lastSequence)
        return;
    a.lastSequence = c.sequence;
    a.view.sequence = c.sequence;
    a.view.result = 1;
    // Voluntary departure is terminal, unlike a recoverable network disconnect.
    // Accept it while stunned, dead or waiting for the start barrier as well.
    if(c.action == AB::Abandon) {
        if(!p->extracted) {
            bool wasAlive = p->hp > 0;
            p->hp = 0; p->speed = 0; p->extractionHoldSec = 0;
            a.effects.Clear(); a.defenseLeft = a.parry = a.counter = 0;
            p->skill.view.castLeft = 0;
            loot.SpawnPlayerLoot(*this, cid, p->position);
            if(wasAlive) { PlayerDied dead{}; dead.victimId = cid;
                Broadcast((int)PacketType::PLAYER_DIED, &dead, sizeof(dead)); }
        }
        a.view.result = FinishEconomy(cid, p->extracted) ? 0 : 5;
        RevokeJoin(cid); // The old run must never restore this actor.
        ABSync(cid, true);
        if(a.view.result == 0) CheckSessionEnd();
        return;
    }
    a.ObserveHealth(p->hp,p->maxHp);
    if(DEBUG_ALLOW_TELEPORT && ((c.action>=8 && c.action<=11)||c.action==AB::RewardTeleport) && p->IsActiveInWorld() && gameStarted.load()) {
        if(c.action==8) {a.demoInfiniteStamina=!a.demoInfiniteStamina;a.view.result=0;}
        if(c.action==9 && stormRadius>0) {
            auto now=duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
            auto current=Storm::At(stormElapsed,stormRadius,stormX,stormZ,now);
            if(current.stage<4){int next=current.stage+1;stormElapsed=Storm::Begins[next]+Storm::Durations[next];}
            auto state=Storm::At(stormElapsed,stormRadius,stormX,stormZ,now);
            Broadcast(Storm::Packet,&state,sizeof(state));a.view.result=0;
        }
        if(c.action==10) {
            auto chests=loot.DemoChests();
            if(!chests.empty()) {
                auto it=std::find_if(chests.begin(),chests.end(),[&](const auto& v){return v.first>a.demoChestCursor;});
                if(it==chests.end())it=chests.begin();a.demoChestCursor=it->first;
                auto target=it->second+Vec3(0,0,-2.5f);target.y=dungeon.TileToStandPosition(dungeon.WorldToTile(target)).y;
                DebugTeleportTo(cid,target,"demo chest");a.view.result=0;
            }
        }
        if(c.action==AB::RewardTeleport){
            std::vector<int> ids;for(const auto& room:dungeon.rooms)if(room.type==ROOM_TYPE_REWARD)ids.push_back(room.id);std::sort(ids.begin(),ids.end());
            if(!ids.empty()){auto it=std::upper_bound(ids.begin(),ids.end(),p->rewardTeleportCursor);if(it==ids.end())it=ids.begin();Vec3 dest;if(RoomCenterStandPos(*it,dest)){p->rewardTeleportCursor=*it;DebugTeleportTo(cid,dest,"reward room");a.view.result=0;}}
        }
        if(c.action==11) {
            std::vector<const Env::State*> plates;
            for(auto& trap:environmentTraps){bool duplicate=false;for(auto* s:plates)if(s->x==trap.view.x&&s->z==trap.view.z)duplicate=true;if(!duplicate)plates.push_back(&trap.view);}
            if(!plates.empty()) {
                auto* s=plates[a.demoTrapCursor%plates.size()];a.demoTrapCursor=(a.demoTrapCursor+1)%plates.size();
                Vec3 target(s->x-s->dx*2.5f,s->y,s->z-s->dz*2.5f);
                target.y=dungeon.TileToStandPosition(dungeon.WorldToTile(target)).y;
                DebugTeleportTo(cid,target,"demo trap");a.view.result=0;
            }
        }
        ABSync(cid);return;
    }
    if (!p->IsActiveInWorld() || !gameStarted.load() || a.effects.Stunned())
    {
        ABSync(cid);
        return;
    }
    auto *item = AB::Find(c.itemHash);
    auto &inv = loot.GetInventory(cid);
    bool recovery = a.hitRecoveryLeft>0 || c.action==AB::RecoveryDash;
    float dashCost = AB::DashCost + (recovery ? AB::RecoveryDashExtraCost : 0);
    if ((c.action == AB::Dash || c.action == AB::RecoveryDash) && a.view.stamina >= dashCost && a.defenseLeft <= 0 &&
        (recovery ? a.recentHitLeft>0 && a.recoveryDashCooldown<=0 : a.dashCooldown<=0))
    {
        if(!a.demoInfiniteStamina) a.view.stamina -= dashCost;
        if(recovery){a.hitRecoveryLeft=a.recentHitLeft=0;a.recoveryDashCooldown=.8f;}
        a.regenDelay = .7f;
        a.dashCooldown = .8f;
        a.parry = 0;
        a.defenseAction = AB::Dash; a.defenseLeft = a.defenseDuration = .7f;
        // Dash direction is a quantized yaw in the otherwise unused offer field.
        float yaw = std::clamp(c.offer, -18000, 18000) * .01f * 3.14159265f / 180.f;
        a.defenseX = std::sin(yaw); a.defenseZ = std::cos(yaw); ++a.defenseSerial;
        a.view.result = 0;
    }
    else if (c.action == AB::Parry && a.view.weapon!=0 && a.weaponType != 4 && a.hitRecoveryLeft<=0 && a.parryCooldown <= 0 && a.view.stamina >= AB::ParryCost && a.defenseLeft <= 0)
    {
        a.parry = AB::ParryWindow;
        a.defenseAction = AB::Parry; a.defenseLeft = a.defenseDuration = .5f; ++a.defenseSerial;
        a.parryCooldown = .8f;
        if(!a.demoInfiniteStamina) a.view.stamina -= AB::ParryCost;
        a.regenDelay = .5f;
        a.view.result = 0;
    }
    else if (c.action == AB::Choose)
    {
        if (a.Select(c.offer, c.itemHash))
            a.view.result = 0;
    }
    else if (c.action == AB::Drop)
    {
        if(a.dropCooldown<=0 && loot.DropItem(*this,cid,c.itemHash,c.offer)) {
            a.view.result=0;a.dropCooldown=.2f;
        } else a.view.result=2;
    }
    else if (item && inv.GetCount(c.itemHash) > 0)
    {
        if (c.action == AB::Use && item->effect == 15 && item->kind == 1 && a.procCooldown <= 0 &&
            a.view.passive == c.itemHash)
        {
            a.shield = 3;
            a.shieldReduction = .05f + .1f * a.tier;
            a.procCooldown = 30;
            a.view.result = 0;
        }
        else if (c.action == AB::Equip && item->kind > 0)
        {
            if (item->kind == 2)
            {
                a.view.weapon = c.itemHash;
                a.weaponType = item->effect;
            }
            else
            {
                a.view.passive = c.itemHash;
                a.passiveType = item->effect;
                a.tier = item->tier;
                a.hits = 0;
                a.regen = 0;
                a.lifestealRemainder = 0;
            }
            a.view.result = 0;
        }
        else if (c.action == AB::Use && a.useCooldown <= 0 && item->kind == 0 &&
                 Skills::CanUse(a,p->skill.view,item->effect,item->tier) && inv.Remove(c.itemHash, 1))
        {
            int t = item->tier;
            if (item->effect == 0)
                p->hp = (std::min)(p->maxHp, p->hp + int(p->maxHp * (t == 1 ? .25f : t == 2 ? .4f : .6f)));
            if (item->effect == 1)
                a.view.stamina = (std::min)(a.view.maxStamina, a.view.stamina + a.view.maxStamina * (t == 1   ? .35f
                                                                                                     : t == 2 ? .6f
                                                                                                              : 1.f));
            if (item->effect == 2)
            {
                a.effects.Clear();
                a.immune = (float)t;
            }
            if (item->effect == 3)
                a.sprintFree = 2.f + 2 * t;
            Skills::Use(a,p->skill.view,item->effect,t);
            a.useCooldown = .5f;
            a.view.result = 0;
        }
    }
    ABSync(cid, (c.action == AB::Use || c.action == AB::Drop) && a.view.result == 0);
    if (a.view.result == 0 && (c.action == AB::Dash || c.action == AB::RecoveryDash || c.action == AB::Parry || c.action == AB::Drop))
        for (auto& peer : players) if (peer.first != cid) ABSync(peer.first);

}
void GameSession::ABTick(float dt)
{
    for (auto &kv : players)
    {
        auto &p = *kv.second;
        auto &a = p.ab;
        a.ObserveHealth(p.hp,p.maxHp);
        a.hitRecoveryLeft=std::max(0.f,a.hitRecoveryLeft-dt);a.recentHitLeft=std::max(0.f,a.recentHitLeft-dt);
        a.recoveryDashCooldown=std::max(0.f,a.recoveryDashCooldown-dt);a.dropCooldown=std::max(0.f,a.dropCooldown-dt);
        if (!p.IsActiveInWorld())
        {
            if (a.view.hp != p.hp || a.effects.Mask() != 0)
            {
                a.effects.Clear();
                ABSync(kv.first);
            }
            continue;
        }
        a.defenseLeft = (std::max)(0.f, a.defenseLeft - dt);
        if(a.effects.Stunned()){a.defenseLeft=0; a.parry=0;}
        a.useCooldown = (std::max)(0.f, a.useCooldown - dt);
        a.sprintFree = (std::max)(0.f, a.sprintFree - dt);
        a.dashCooldown = (std::max)(0.f, a.dashCooldown - dt);
        a.parryCooldown = (std::max)(0.f, a.parryCooldown - dt);
        bool sprinting = a.lastSprintInput > 0 && !a.effects.Stunned();
        a.lastSprintInput = (std::max)(0.f, a.lastSprintInput - dt);
        if (sprinting && a.sprintFree <= 0 && !a.demoInfiniteStamina)
            a.view.stamina = (std::max)(0.f, a.view.stamina - 5 * dt);
        a.regenDelay = (std::max)(0.f, a.regenDelay - dt);
        if (!sprinting && a.regenDelay <= 0)
            a.view.stamina = (std::min)(a.view.maxStamina, a.view.stamina + dt * (10 + 2 * a.upgrades[5]));
        a.parry = (std::max)(0.f, a.parry - dt);
        a.counter = (std::max)(0.f, a.counter - dt);
        a.shield = (std::max)(0.f, a.shield - dt);
        a.idle += dt;
        a.sync += dt;
        a.procCooldown = (std::max)(0.f, a.procCooldown - dt);
        a.immune = (std::max)(0.f, a.immune - dt);
        if (a.passiveType == 6 && a.idle >= 6)
        {
            a.regen += dt * p.maxHp * (.0025f + .0025f * a.tier);
            if (a.regen >= 1)
            {
                p.hp = (std::min)(p.maxHp, p.hp + (int)a.regen);
                a.regen -= int(a.regen);
            }
        }
        a.Recompute();
        if (a.sync >= .2f)
        {
            a.sync = 0;
            ABSync(kv.first);
        }
        a.effects.Step(dt, [&](int damage, int source) {
            if (!p.IsActiveInWorld())
                return;
            a.idle = 0;
            p.InterruptExtractionOnDamage(damage);
            p.hp = (std::max)(0, p.hp - damage);
            CombatEvent ce{};
            ce.attackerId = source;
            ce.targetId = p.clientId;
            ce.damage = damage;
            ce.targetHpAfter = p.hp;
            Broadcast((int)PacketType::COMBAT_EVENT, &ce, sizeof(ce));
            if (!p.hp)
            {
                PlayerDied dead{};
                dead.victimId = p.clientId;
                dead.killerId = source;
                Broadcast((int)PacketType::PLAYER_DIED, &dead, sizeof(dead));
                AddPlayerKill(source,p.clientId);
            }
        });
    }
    for (auto &kv : worldSim.GetMonsters())
    {
        auto &m = *kv.second;
        if (m.aiState == AI_DEAD)
        {
            m.effects.Clear();
            continue;
        }
        m.effects.Step(dt, [&](int damage, int source) {
            if (m.aiState == AI_DEAD)
                return;
            bool dead = worldSim.ApplyDamageToMonster(m.id, damage);
            CombatEvent ce{};
            ce.attackerId = source;
            ce.targetId = -m.id;
            ce.damage = damage;
            ce.targetHpAfter = m.hp;
            Broadcast((int)PacketType::COMBAT_EVENT, &ce, sizeof(ce));
            if (dead)
            {
                MonsterDied d{};
                d.monsterId = m.id;
                d.killerId = source;
                Broadcast((int)PacketType::MONSTER_DIED, &d, sizeof(d));
                OnMonsterKilled(m.id, source);
            }
        });
    }
}

void GameSession::ABOnHit(int source, int target, int actualDamage, bool elemental, const AB::HitTraits* snapshot)
{
    auto *p = GetPlayer(source);
    if (!p || !p->IsActiveInWorld() || actualDamage <= 0)
        return;
    auto &a = p->ab;
    const auto traits = snapshot ? *snapshot : a.Traits();
    AB::Effects *fx = nullptr;
    if (target < 0)
    {
        auto it = worldSim.GetMonsters().find(-target);
        if (it != worldSim.GetMonsters().end() && it->second->hp > 0)
            fx = &it->second->effects;
    }
    else
    {
        auto *v = GetPlayer(target);
        if (v)
        {
            v->ab.idle = 0;
            if (v->hp > 0 && v->ab.immune <= 0)
                fx = &v->ab.effects;
        }
    }
    if (traits.passiveType == 7)
    {
        a.lifestealRemainder += actualDamage * (1 + 2 * traits.tier) / 100.f;
        int healed = (int)a.lifestealRemainder;
        a.lifestealRemainder -= healed;
        p->hp = (std::min)(p->maxHp, p->hp + healed);
    }
    if (fx && traits.passiveType == 12 && ++a.hits >= 3)
    {
        a.hits = 0;
        fx->Apply(3, 2 * traits.tier, source, traits.duration);
    }
    if (fx && traits.passiveType == 13 && a.procCooldown <= 0)
    {
        fx->Apply(2, 1, source);
        a.procCooldown = 24.f - 4 * traits.tier;
    }
    if (fx && elemental)
    {
        if (AB::WeaponElement(traits.weaponType) == 1)
            fx->Apply(0, 3 + (traits.passiveType == 10 ? traits.tier : 0), source, traits.duration);
        if (AB::WeaponElement(traits.weaponType) == 2)
            fx->Apply(1, 1, source, traits.duration);
    }
    ABSync(source);
}

#include "GameSessionEnvironment.inl"
#include "GameSessionSkills.inl"

void GameSession::StormInit(){
    for(const auto& room:dungeon.rooms)if(room.id==dungeon.exitRoomId){
        auto center=dungeon.TileToWorld({int((room.bounds.xMin()+room.bounds.xMax())*.5f),0,int((room.bounds.zMin()+room.bounds.zMax())*.5f)});
        stormX=center.x;stormZ=center.z;
    }
    dungeon.floorTiles.ForEach([&](const IntVec3& tile){auto p=dungeon.TileToWorldCenter(tile);stormRadius=std::max(stormRadius,std::hypot(p.x-stormX,p.z-stormZ)+2.f);});
}
void GameSession::StormTick(float dt){
    if(stormRadius<=0)return;
    stormElapsed+=dt;stormPulse+=dt;if(stormPulse<1.f)return;stormPulse=0;
    auto now=duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
    auto state=Storm::At(stormElapsed,stormRadius,stormX,stormZ,now);
    Broadcast(Storm::Packet,&state,sizeof(state));
    for(auto& entry:players){auto& p=*entry.second;if(!p.IsActiveInWorld()||!Storm::Outside(state,p.position.x,p.position.z))continue;
        // Observe any preceding combat hit before consuming storm HP without a hit reaction.
        p.ab.ObserveHealth(p.hp,p.maxHp);
        int damage=std::min(10,2+state.stage*2);p.InterruptExtractionOnDamage(damage);p.hp=std::max(0,p.hp-damage);
        p.ab.observedHp=p.hp;
        Storm::Damage hit;hit.victimId=p.clientId;hit.hp=p.hp;
        Broadcast(Storm::DamagePacket,&hit,sizeof(hit));ABSync(p.clientId);
        if(p.hp==0){FinishEconomy(p.clientId,false);PlayerDied dead{};dead.victimId=p.clientId;dead.killerId=0;Broadcast((int)PacketType::PLAYER_DIED,&dead,sizeof(dead));}
    }
}
