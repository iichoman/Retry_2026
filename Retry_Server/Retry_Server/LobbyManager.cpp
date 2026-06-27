#include "LobbyManager.h"
#include "ClientConnection.h"
#include "RoomData.h"
#include "SessionDispatcher.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <cstring>
#include <vector>

LobbyManager::LobbyManager(SessionDispatcher* d)
    : nextClientId(1)
    , nextRoomId(1)
    , nextSessionId(1)
    , nextPendingId(-1)
    , dispatcher(d)
{
}

LobbyManager::~LobbyManager()
{
    Shutdown();
}

// ============================================================================
//  등록 / 종료
// ============================================================================

void LobbyManager::RegisterClient(std::unique_ptr<ClientConnection> conn)
{
    std::lock_guard<std::mutex> lk(mtx);
    int tempId = nextPendingId--;             // 음수로 임시 ID
    conn->clientId = 0;                        // 인증 전엔 0
    pendingClients[tempId] = std::move(conn);
}

void LobbyManager::OnClientDisconnected(ClientConnection* conn)
{
    std::lock_guard<std::mutex> lk(mtx);

    // 주의: 여기서 ClientConnection 객체를 erase하지 않는다.
    // 이유: IOCP 워커들이 그 객체에 대한 outstanding I/O completion을
    //       아직 받지 않았을 수 있어, 즉시 erase하면 use-after-free 위험.
    // 따라서 socket만 닫고 active=false 처리하여 불필요한 추가 작업을 막고,
    // 실제 메모리 정리는 Shutdown()에서 일괄 처리.

    int cid = conn->clientId;
    if (cid > 0)
    {
        Log::Info("클라 연결 종료: id=%d", cid);
        RemoveClientFromRoom(cid);
    }
    // active는 이미 conn->Disconnect()에서 false. 추가 작업 불필요.
}

void LobbyManager::Shutdown()
{
    std::lock_guard<std::mutex> lk(mtx);
    for (auto& kv : authenticatedClients) kv.second->Disconnect();
    for (auto& kv : pendingClients)       kv.second->Disconnect();
    authenticatedClients.clear();
    pendingClients.clear();
    rooms.clear();
}

// ============================================================================
//  패킷 디스패치
// ============================================================================

void LobbyManager::HandlePacket(ClientConnection* conn, int packetType,
    const char* body, int bodySize)
{
    std::lock_guard<std::mutex> lk(mtx);

    PacketType type = static_cast<PacketType>(packetType);
    switch (type)
    {
    case PacketType::LOGIN_REQUEST:
        HandleLogin(conn, body, bodySize);
        break;
    case PacketType::ROOM_CREATE_REQUEST:
        HandleRoomCreate(conn, body, bodySize);
        break;
    case PacketType::ROOM_JOIN_REQUEST:
        HandleRoomJoin(conn, body, bodySize);
        break;
    case PacketType::ROOM_LIST_REQUEST:
        HandleRoomList(conn);
        break;
    case PacketType::GAME_START_REQUEST:
        HandleGameStart(conn);
        break;
    case PacketType::ROOM_LEAVE_REQUEST:
        HandleRoomLeave(conn);
        break;
    case PacketType::ROOM_SELECT_TEAM_REQUEST:
        HandleSelectTeam(conn, body, bodySize);
        break;
    default:
        Log::Warn("로비에서 알 수 없는 패킷 type=%d from clientId=%d",
            packetType, conn->clientId);
        break;
    }
}

// ============================================================================
//  핸들러: LOGIN
// ============================================================================

void LobbyManager::HandleLogin(ClientConnection* conn, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(LoginRequest))
    {
        Log::Warn("LOGIN_REQUEST 크기 부족: %d", bodySize);
        return;
    }
    if (conn->state != ClientConnection::ST_CONNECTED)
    {
        Log::Warn("이미 인증된 클라가 LOGIN 재요청 cid=%d", conn->clientId);
        return;
    }

    const LoginRequest* req = reinterpret_cast<const LoginRequest*>(body);

    // 새 clientId 할당, 인증된 맵으로 이동
    int newId = nextClientId++;
    std::strncpy(conn->playerName, req->playerName, sizeof(conn->playerName) - 1);
    conn->playerName[sizeof(conn->playerName) - 1] = '\0';
    conn->clientId = newId;
    conn->state = ClientConnection::ST_AUTHENTICATED;

    // pending → authenticated 이동
    std::unique_ptr<ClientConnection> moved;
    for (auto it = pendingClients.begin(); it != pendingClients.end(); ++it)
    {
        if (it->second.get() == conn)
        {
            moved = std::move(it->second);
            pendingClients.erase(it);
            break;
        }
    }
    if (!moved)
    {
        Log::Error("LOGIN: pending에서 클라 못 찾음");
        return;
    }
    authenticatedClients[newId] = std::move(moved);

    // 응답
    LoginResult res;
    std::memset(&res, 0, sizeof(res));
    res.success = 1;
    res.clientId = newId;
    conn->SendPacket((int)PacketType::LOGIN_RESULT, &res, sizeof(res));

    Log::Info("로그인: id=%d name=%s", newId, conn->playerName);
}

// ============================================================================
//  핸들러: ROOM_CREATE
// ============================================================================

void LobbyManager::HandleRoomCreate(ClientConnection* conn, const char* body, int bodySize)
{
    if (conn->state != ClientConnection::ST_AUTHENTICATED)
    {
        Log::Warn("인증 안 된 클라가 ROOM_CREATE");
        return;
    }
    if (conn->currentRoomId != 0)
    {
        Log::Warn("이미 방에 있는 클라가 ROOM_CREATE: cid=%d", conn->clientId);
        return;
    }
    if (bodySize < (int)sizeof(RoomCreateRequest)) return;

    const RoomCreateRequest* req = reinterpret_cast<const RoomCreateRequest*>(body);

    int newRoomId = nextRoomId++;
    auto room = std::make_unique<RoomData>(newRoomId, req->roomName,
        conn->clientId, MAX_SESSION_PLAYERS);
    rooms[newRoomId] = std::move(room);

    conn->currentRoomId = newRoomId;
    conn->state = ClientConnection::ST_IN_ROOM;

    RoomCreateResult res{};
    res.success = 1;
    res.roomId = newRoomId;
    conn->SendPacket((int)PacketType::ROOM_CREATE_RESULT, &res, sizeof(res));

    BroadcastRoomState(GetRoom(newRoomId));   // 본인에게 초기 방 현황(ROOM_STATE)

    Log::Info("방 생성: roomId=%d host=%d name=\"%s\"",
        newRoomId, conn->clientId, req->roomName);
}

// ============================================================================
//  핸들러: ROOM_JOIN
// ============================================================================

void LobbyManager::HandleRoomJoin(ClientConnection* conn, const char* body, int bodySize)
{
    if (conn->state != ClientConnection::ST_AUTHENTICATED) return;
    if (conn->currentRoomId != 0) return;
    if (bodySize < (int)sizeof(RoomJoinRequest)) return;

    const RoomJoinRequest* req = reinterpret_cast<const RoomJoinRequest*>(body);

    RoomJoinResult res{};
    res.roomId = req->roomId;

    RoomData* room = GetRoom(req->roomId);
    if (!room)
    {
        res.success = 0;
        std::strncpy(res.failReason, "방이 존재하지 않습니다", sizeof(res.failReason) - 1);
    }
    else if (room->IsFull())
    {
        res.success = 0;
        std::strncpy(res.failReason, "방이 가득 찼습니다", sizeof(res.failReason) - 1);
    }
    else if (room->isStarting)
    {
        res.success = 0;
        std::strncpy(res.failReason, "이미 시작 중인 방입니다", sizeof(res.failReason) - 1);
    }
    else
    {
        room->AddMember(conn->clientId);
        conn->currentRoomId = req->roomId;
        conn->state = ClientConnection::ST_IN_ROOM;

        res.success = 1;
        res.hostClientId = room->hostClientId;
        res.currentPlayers = room->CurrentPlayers();
        res.maxPlayers = room->maxPlayers;

        Log::Info("방 참가: cid=%d roomId=%d (now %d/%d)",
            conn->clientId, req->roomId,
            room->CurrentPlayers(), room->maxPlayers);
    }

    conn->SendPacket((int)PacketType::ROOM_JOIN_RESULT, &res, sizeof(res));

    if (res.success)
        BroadcastRoomState(GetRoom(req->roomId));   // 새 멤버 포함 전원에게 현황 push
}

// ============================================================================
//  핸들러: ROOM_LIST
// ============================================================================

void LobbyManager::HandleRoomList(ClientConnection* conn)
{
    RoomListResult res{};
    int idx = 0;
    for (auto& kv : rooms)
    {
        if (idx >= MAX_ROOM_LIST) break;
        if (kv.second->isStarting) continue;       // 시작 중인 방은 안 보임

        RoomListEntry& e = res.rooms[idx++];
        e.roomId = kv.second->roomId;
        e.hostClientId = kv.second->hostClientId;
        e.currentPlayers = kv.second->CurrentPlayers();
        e.maxPlayers = kv.second->maxPlayers;
        std::strncpy(e.roomName, kv.second->roomName.c_str(), sizeof(e.roomName) - 1);
    }
    res.count = idx;
    conn->SendPacket((int)PacketType::ROOM_LIST_RESULT, &res, sizeof(res));
}

// ============================================================================
//  핸들러: GAME_START
// ============================================================================

void LobbyManager::HandleGameStart(ClientConnection* conn)
{
    if (conn->state != ClientConnection::ST_IN_ROOM) return;
    if (conn->currentRoomId == 0) return;

    RoomData* room = GetRoom(conn->currentRoomId);
    if (!room) return;
    if (room->hostClientId != conn->clientId)
    {
        Log::Warn("호스트 아닌 클라가 GAME_START 시도: cid=%d roomId=%d",
            conn->clientId, room->roomId);
        return;
    }
    if (room->isStarting) return;

    room->isStarting = true;

    int sessionId = nextSessionId++;
    int mapSeed = (int)((uintptr_t)room ^ sessionId ^ 0x12345);
    if (mapSeed < 0) mapSeed = -mapSeed;

    room->AutoAssignUnassignedTeams();           // 미배정 멤버를 빈 팀 슬롯에 자동 배정
    std::vector<int> playerIds = room->memberIds;
    std::vector<int> playerTeams = room->memberTeams;

    // 세션 매니저에게 IPC로 세션 생성 요청 (동기 대기)
    bool ok = dispatcher->RequestSessionCreate(sessionId, room->hostClientId,
        mapSeed, playerIds, playerTeams);
    if (!ok)
    {
        Log::Error("세션 생성 실패 sessionId=%d", sessionId);
        room->isStarting = false;
        return;
    }

    // 모든 멤버에게 SESSION_ASSIGN 송신
    SessionAssignData assign{};
    assign.sessionId = sessionId;
    assign.mapSeed = mapSeed;
    assign.sessionServerPort = 9001;
    std::strncpy(assign.sessionServerIP, "127.0.0.1", sizeof(assign.sessionServerIP) - 1);

    for (int memberId : playerIds)
    {
        ClientConnection* mc = GetClient(memberId);
        if (mc)
        {
            mc->SendPacket((int)PacketType::SESSION_ASSIGN, &assign, sizeof(assign));
            mc->state = ClientConnection::ST_STARTING;
        }
    }

    Log::Info("게임 시작: sessionId=%d host=%d seed=%d 인원=%d",
        sessionId, room->hostClientId, mapSeed, (int)playerIds.size());

    // 방은 곧 비워질 거라 정리는 클라들이 disconnect 할 때 자연스럽게 됨.
    // 여기선 명시적으로 안 지움 (멤버들이 떠나면서 자동 정리).
}

// ============================================================================
//  내부 헬퍼
// ============================================================================

ClientConnection* LobbyManager::GetClient(int clientId)
{
    auto it = authenticatedClients.find(clientId);
    if (it == authenticatedClients.end()) return nullptr;
    if (!it->second->active) return nullptr;       // 끊긴 클라 제외
    return it->second.get();
}

RoomData* LobbyManager::GetRoom(int roomId)
{
    auto it = rooms.find(roomId);
    if (it == rooms.end()) return nullptr;
    return it->second.get();
}

void LobbyManager::RemoveClientFromRoom(int clientId)
{
    ClientConnection* conn = GetClient(clientId);
    if (!conn) return;
    int rid = conn->currentRoomId;
    if (rid == 0) return;

    RoomData* room = GetRoom(rid);
    if (!room) return;

    room->RemoveMember(clientId);
    conn->currentRoomId = 0;

    if (room->IsEmpty())
    {
        Log::Info("빈 방 삭제: roomId=%d", rid);
        rooms.erase(rid);
    }
    else
    {
        if (room->hostClientId == clientId)
        {
            room->PromoteNewHostIfNeeded();
            Log::Info("호스트 변경: roomId=%d → 새 host=%d", rid, room->hostClientId);
        }
        BroadcastRoomState(room);     // 남은 멤버들에게 갱신된 현황 push
    }
}

// ============================================================================
//  핸들러: ROOM_LEAVE / SELECT_TEAM  +  방 현황 broadcast
// ============================================================================

void LobbyManager::HandleRoomLeave(ClientConnection* conn)
{
    if (conn->currentRoomId == 0) return;

    RemoveClientFromRoom(conn->clientId);                 // 제거 + 남은 멤버에게 ROOM_STATE
    conn->state = ClientConnection::ST_AUTHENTICATED;     // 다시 로비 상태로

    RoomLeaveResult res{};
    res.success = 1;
    conn->SendPacket((int)PacketType::ROOM_LEAVE_RESULT, &res, sizeof(res));
    Log::Info("방 나가기: cid=%d", conn->clientId);
}

void LobbyManager::HandleSelectTeam(ClientConnection* conn, const char* body, int bodySize)
{
    if (conn->currentRoomId == 0) return;
    if (bodySize < (int)sizeof(RoomSelectTeamRequest)) return;
    const RoomSelectTeamRequest* req = reinterpret_cast<const RoomSelectTeamRequest*>(body);

    RoomData* room = GetRoom(conn->currentRoomId);
    if (!room || room->isStarting) return;

    bool ok = room->SetTeam(conn->clientId, req->teamId);
    if (ok)
        BroadcastRoomState(room);   // 전원에게 갱신된 팀 현황
    // 실패(정원 초과 등)는 무시 → 클라는 다음 ROOM_STATE로 정정됨
}

void LobbyManager::BroadcastRoomState(RoomData* room)
{
    if (!room) return;

    RoomStateData st;
    std::memset(&st, 0, sizeof(st));
    st.roomId = room->roomId;
    st.hostClientId = room->hostClientId;

    int n = 0;
    for (size_t i = 0; i < room->memberIds.size() && n < MAX_SESSION_PLAYERS; ++i)
    {
        int cid = room->memberIds[i];
        RoomMemberEntry& m = st.members[n++];
        m.clientId = cid;
        m.teamId = (i < room->memberTeams.size()) ? room->memberTeams[i] : TEAM_UNASSIGNED;
        m.isHost = (cid == room->hostClientId) ? 1 : 0;
        ClientConnection* mc = GetClient(cid);
        if (mc) std::strncpy(m.playerName, mc->playerName, sizeof(m.playerName) - 1);
    }
    st.memberCount = n;

    for (int cid : room->memberIds)
    {
        ClientConnection* mc = GetClient(cid);
        if (mc) mc->SendPacket((int)PacketType::ROOM_STATE, &st, sizeof(st));
    }
}