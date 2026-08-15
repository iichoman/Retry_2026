#include "GameSession.h"
#include "PlayerEntity.h"
#include "MonsterEntity.h"
#include "SessionClientConnection.h"
#include "PositionValidator.h"
#include "LobbyReporter.h"
#include "LootSystem.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <chrono>
#include <cstring>

using namespace std::chrono;

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

GameSession::GameSession(int sid, int hid, int seed,
    const std::vector<int>& allowedPlayerIds,
    const std::vector<int>& playerTeams,
    LobbyReporter* rep)
    : sessionId(sid)
    , hostClientId(hid)
    , mapSeed(seed)
    , running(false)
    , reporter(rep)
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

    if (DEBUG_INSTANT_EXTRACT)
        Log::Warn("*** 치트 활성: 즉시 탈출(DEBUG_INSTANT_EXTRACT) — 배포 전 끌 것 ***");
    if (DEBUG_ALLOW_TELEPORT)
        Log::Warn("*** 치트 활성: 탈출 방 이동(DEBUG_ALLOW_TELEPORT) — 배포 전 끌 것 ***");
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

    // 이미 월드에 있는 전리품 컨테이너 + 본인 인벤토리 동기화.
    loot.SendAllLootTo(*this, clientId);

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
    it->second->conn = nullptr;     // 먼저 연결 해제 (이후 이 엔티티로의 송신 차단)

    // 시야 안 다른 플레이어들에게 LEAVE_VIEW 송신 + viewedPlayers에서 제거 (엔티티 살아있는 동안)
    im.OnPlayerLeave(*this, clientId);

    // 엔티티 완전 제거 → 캐릭터가 세션에서 완전히 빠짐 (재접속 미지원 단계).
    // 몬스터는 targetClientId(int)로 대상을 참조하므로 댕글링 포인터 없음.
    players.erase(clientId);

    Log::Info("[Session %d] 퇴장(엔티티 제거): clientId=%d", sessionId, clientId);
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

    case PacketType::EXTRACTION_REQUEST:
        HandleExtraction(clientId, body, bodySize);
        break;

    case PacketType::ITEM_PICKUP_REQUEST:
        HandleItemPickup(clientId, body, bodySize);
        break;

    case PacketType::DEBUG_TELEPORT_EXIT:   // [치트] 배포 시 제거
        HandleDebugTeleportExit(clientId);
        break;

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
    if (!p->IsActiveInWorld()) return;   // 사망/탈출한 플레이어 입력 무시

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

void GameSession::HandleItemPickup(int clientId, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(ItemPickupRequest)) return;
    const ItemPickupRequest* req = reinterpret_cast<const ItemPickupRequest*>(body);

    std::lock_guard<std::recursive_mutex> lk(mtx);
    loot.HandlePickupRequest(*this, clientId, *req);
}

// 몬스터가 죽었을 때 전리품 생성. 전투 코드에서 호출된다.
void GameSession::OnMonsterKilled(int monsterId, int /*killerClientId*/)
{
    std::lock_guard<std::recursive_mutex> lk(mtx);

    auto& monsters = worldSim.GetMonsters();
    auto it = monsters.find(monsterId);
    if (it == monsters.end()) return;

    loot.SpawnMonsterLoot(*this, monsterId, it->second->position,
        it->second->kind, mapSeed);
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
    if (!DEBUG_ALLOW_TELEPORT) return;

    std::lock_guard<std::recursive_mutex> lk(mtx);
    PlayerEntity* p = GetPlayer(clientId);
    if (!p) return;
    if (!p->IsActiveInWorld())
    {
        Log::Warn("[치트] cid=%d 이동 불가 (사망/탈출 상태)", clientId);
        return;
    }

    if (dungeon.exitRoomId < 0)
    {
        Log::Warn("[치트] 이 던전에 탈출 방이 없음 (createExitRoom 확인)");
        return;
    }

    const Room* exitRoom = nullptr;
    for (const Room& r : dungeon.rooms)
        if (r.id == dungeon.exitRoomId) { exitRoom = &r; break; }

    if (!exitRoom || exitRoom->floorTiles.empty())
    {
        Log::Warn("[치트] 탈출 방 %d 의 바닥 타일을 찾지 못함", dungeon.exitRoomId);
        return;
    }

    // 방 중심에 가장 가까운 바닥 타일 선택
    float cx = (exitRoom->bounds.xMin() + exitRoom->bounds.xMax()) * 0.5f;
    float cz = (exitRoom->bounds.zMin() + exitRoom->bounds.zMax()) * 0.5f;

    const IntVec3* best = nullptr;
    float bestDistSq = 0.f;
    for (const IntVec3& t : exitRoom->floorTiles)
    {
        float dx = (float)t.x - cx;
        float dz = (float)t.z - cz;
        float dsq = dx * dx + dz * dz;
        if (!best || dsq < bestDistSq) { best = &t; bestDistSq = dsq; }
    }
    if (!best) return;

    Vec3 dest = dungeon.TileToWorldCenter(*best);

    p->position = dest;
    p->speed = 0.f;
    p->extractionHoldSec = 0.f;         // 도착 직후부터 홀드 시작
    p->startPosResendTicks = 10;        // 클라가 놓치지 않도록 재송신 예약

    // 클라에게 즉시 통보 (클라는 이 좌표로 캐릭터를 스냅시킨다)
    if (p->conn && p->conn->active)
    {
        long long ts = duration_cast<milliseconds>(
            system_clock::now().time_since_epoch()).count();
        PlayerMove pm{};
        pm.clientId = clientId;
        pm.posX = dest.x;
        pm.posY = dest.y;
        pm.posZ = dest.z;
        pm.rotY = p->rotY;
        pm.speed = 0.f;
        pm.animState = 0;
        pm.timestamp = ts;
        p->conn->SendPacket((int)PacketType::PLAYER_MOVE, &pm, sizeof(pm));
    }

    Log::Warn("[치트] cid=%d 탈출 방(%d)으로 이동: (%.1f, %.1f, %.1f)",
        clientId, dungeon.exitRoomId, dest.x, dest.y, dest.z);
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

void GameSession::HandleExtraction(int clientId, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(ExtractionRequest)) return;

    std::lock_guard<std::recursive_mutex> lk(mtx);
    PlayerEntity* p = GetPlayer(clientId);
    if (!p) return;

    ExtractionResult res{};
    res.itemCount = loot.GetTotalItemCount(clientId);   // 서버 권위 인벤토리 기준
    res.heldSec = p->extractionHoldSec;

    // 1) 상태 검사 (치트로도 우회 불가 — 죽었거나 이미 나간 건 탈출 불가)
    if (p->extracted)            res.failReason = EXTRACT_FAIL_ALREADY;
    else if (p->hp <= 0)         res.failReason = EXTRACT_FAIL_DEAD;
    else if (DEBUG_INSTANT_EXTRACT)
    {
        // [치트] 위치/체류시간 검증 생략
        res.failReason = EXTRACT_OK;
        Log::Warn("[치트] cid=%d 즉시 탈출 (위치/시간 검증 생략)", clientId);
    }
    else if (dungeon.exitRoomId < 0) res.failReason = EXTRACT_FAIL_NO_EXIT_ROOM;
    // 2) 위치 검사: 서버 권위 위치가 실제 탈출 방 안인가
    else if (dungeon.RoomIdAt(p->position) != dungeon.exitRoomId)
        res.failReason = EXTRACT_FAIL_NOT_IN_ZONE;
    // 3) 체류 시간 검사: 클라가 보낸 시간이 아니라 서버 누적치로 판정
    else if (p->extractionHoldSec < EXTRACTION_HOLD_SEC * EXTRACTION_HOLD_TOLERANCE)
        res.failReason = EXTRACT_FAIL_HOLD_TOO_SHORT;
    else
        res.failReason = EXTRACT_OK;

    res.success = (res.failReason == EXTRACT_OK) ? 1 : 0;

    if (p->conn && p->conn->active)
        p->conn->SendPacket((int)PacketType::EXTRACTION_RESULT, &res, sizeof(res));

    if (!res.success)
    {
        Log::Warn("[Session %d] 탈출 거부: cid=%d reason=%d held=%.1fs room=%d(exit=%d)",
            sessionId, clientId, res.failReason, p->extractionHoldSec,
            dungeon.RoomIdAt(p->position), dungeon.exitRoomId);
        return;
    }

    // 4) 탈출 확정
    p->extracted = true;
    im.OnPlayerLeave(*this, clientId);   // 다른 클라 화면에서 제거

    int remaining = 0;
    for (auto& kv : players)
        if (kv.second->IsActiveInWorld()) ++remaining;

    PlayerExtracted pe{};
    pe.clientId = clientId;
    pe.remainingPlayers = remaining;
    Broadcast((int)PacketType::PLAYER_EXTRACTED, &pe, sizeof(pe), clientId);

    Log::Info("[Session %d] 탈출 성공: cid=%d held=%.1fs 남은인원=%d",
        sessionId, clientId, p->extractionHoldSec, remaining);

    CheckSessionEnd();
}

// 매 틱 호출. 서버 권위 위치 기준으로 탈출 방 체류 시간 누적.
void GameSession::UpdateExtractionHold(float dt)
{
    if (dungeon.exitRoomId < 0) return;

    for (auto& kv : players)
    {
        PlayerEntity& p = *kv.second;
        if (!p.IsActiveInWorld())
        {
            p.extractionHoldSec = 0.f;
            continue;
        }

        if (dungeon.RoomIdAt(p.position) == dungeon.exitRoomId)
            p.extractionHoldSec += dt;
        else
            p.extractionHoldSec = 0.f;   // 방을 벗어나면 리셋 (클라 UI와 동일)
    }
}

// 월드에 남은 인원이 0이면 세션 종료 통보. 중복 송신 방지.
void GameSession::CheckSessionEnd()
{
    if (sessionEndSignaled.load()) return;
    if (players.empty()) return;

    for (auto& kv : players)
        if (kv.second->IsActiveInWorld()) return;   // 아직 진행 중

    sessionEndSignaled.store(true);

    SessionEnded se{};
    se.reason = 0;      // 정상 종료
    Broadcast((int)PacketType::SESSION_ENDED, &se, sizeof(se), 0);

    int survivors = 0;
    for (auto& kv : players)
        if (kv.second->extracted) ++survivors;

    int total = (int)players.size();
    Log::Info("[Session %d] 종료: 전원 탈출/사망 (탈출 %d / 전체 %d)",
        sessionId, survivors, total);

    // 로비에 종료 보고 → 로비가 방 정리 + 클라 상태 복귀.
    // 실패해도 세션 종료 자체는 진행된다 (LobbyReporter가 경고만 남김).
    if (reporter)
        reporter->ReportSessionEnded(sessionId, se.reason, total, survivors);
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

        // 2-a) 탈출 방 체류 시간 누적 (서버 권위 위치 기준).
        //      EXTRACTION_REQUEST가 오면 이 값으로 검증한다.
        UpdateExtractionHold(dt);

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

        // 4) 전원 탈출/사망 여부 확인 (사망으로 세션이 끝나는 경우 포함).
        CheckSessionEnd();
    }

    // 시야 무관한 중요 이벤트는 전체 broadcast (몬스터 공격, 사망).
    // Broadcast는 자체 lock 잡음.
    for (const auto& mae : attackPackets)
        Broadcast((int)PacketType::MONSTER_ATTACK_EVENT, &mae, sizeof(mae), 0);
    for (const auto& pd : deathPackets)
        Broadcast((int)PacketType::PLAYER_DIED, &pd, sizeof(pd), 0);
}