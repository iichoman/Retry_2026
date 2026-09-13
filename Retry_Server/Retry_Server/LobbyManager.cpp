#include "LobbyManager.h"
#include "ClientConnection.h"
#include "RoomData.h"
#include "SessionDispatcher.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <cstdio>
#include <cstring>
#include <string>
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
    case PacketType::CHAT_SEND:
        HandleChat(conn, body, bodySize);
        break;
    case PacketType::ROOM_READY_REQUEST:
        HandleRoomReady(conn);
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
    {
        RoomData* joined = GetRoom(req->roomId);
        BroadcastRoomState(joined);                 // 새 멤버 포함 전원에게 현황 push

        char line[MAX_CHAT_TEXT];
        std::snprintf(line, sizeof(line), "%s님이 파티에 입장하였습니다.", conn->playerName);
        SendSystemToRoom(joined, line);
    }
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

    // 준비 확인. 방장을 뺀 전원이 준비돼야 시작한다.
    if (!room->AllMembersReady())
    {
        SendSystemTo(conn, "아직 준비되지 않은 파티원이 있습니다.");
        Log::Info("시작 거부(준비 미완료): roomId=%d host=%d", room->roomId, conn->clientId);
        return;
    }

    room->isStarting = true;

    // 방 전원에게 "던전 생성 시작" 공지. 클라는 이걸 받아 로딩 화면을 띄운다.
    // (서버 던전 생성이 ~400ms 걸려서 SESSION_ASSIGN만 기다리면 그만큼 멈춘 것처럼 보인다)
    for (int cid : room->memberIds)
    {
        ClientConnection* mc = GetClient(cid);
        if (mc) mc->SendPacket((int)PacketType::GAME_START_NOTICE, nullptr, 0);
    }

    int sessionId = nextSessionId++;
    int mapSeed = (int)((uintptr_t)room ^ sessionId ^ 0x12345);
    if (mapSeed < 0) mapSeed = -mapSeed;

    room->AutoAssignUnassignedTeams();           // 미배정 멤버를 빈 팀 슬롯에 자동 배정
    std::vector<int> playerIds = room->memberIds;
    std::vector<int> playerTeams = room->memberTeams;

    // 세션에서 채팅 발신자 이름을 표시하려면 로비가 이름을 넘겨줘야 한다.
    std::vector<std::string> playerNames;
    playerNames.reserve(playerIds.size());
    for (int memberId : playerIds)
    {
        ClientConnection* mc = GetClient(memberId);
        playerNames.push_back(mc ? std::string(mc->playerName) : std::string());
    }

    // 세션 매니저에게 IPC로 세션 생성 요청 (동기 대기)
    bool ok = dispatcher->RequestSessionCreate(sessionId, room->hostClientId,
        mapSeed, playerIds, playerTeams, playerNames);
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

    // 세션 종료 보고(IPC_SESSION_ENDED)를 받았을 때 어느 방인지 찾기 위해 기록.
    sessionToRoom[sessionId] = room->roomId;

    Log::Info("게임 시작: sessionId=%d host=%d seed=%d 인원=%d",
        sessionId, room->hostClientId, mapSeed, (int)playerIds.size());

    // 방은 곧 비워질 거라 정리는 클라들이 disconnect 할 때 자연스럽게 됨.
    // 여기선 명시적으로 안 지움 (멤버들이 떠나면서 자동 정리).
}

// ============================================================================
//  [시스템] 알림
//
//  채팅과 같은 패킷을 쓰되 채널을 CHAT_SYSTEM으로 보낸다.
//  클라는 이 채널을 발신자 없이 노란색 [시스템] 줄로 그린다.
// ============================================================================

void LobbyManager::SendSystemToRoom(RoomData* room, const char* text)
{
    if (!room || !text) return;

    ChatMessageData msg{};
    msg.channel = CHAT_SYSTEM;
    msg.senderId = 0;
    msg.senderTeam = TEAM_UNASSIGNED;
    std::strncpy(msg.text, text, sizeof(msg.text) - 1);

    for (int cid : room->memberIds)
    {
        ClientConnection* mc = GetClient(cid);
        if (mc) mc->SendPacket((int)PacketType::CHAT_MESSAGE, &msg, sizeof(msg));
    }
}

void LobbyManager::SendSystemTo(ClientConnection* conn, const char* text)
{
    if (!conn || !text) return;

    ChatMessageData msg{};
    msg.channel = CHAT_SYSTEM;
    msg.senderId = 0;
    msg.senderTeam = TEAM_UNASSIGNED;
    std::strncpy(msg.text, text, sizeof(msg.text) - 1);
    conn->SendPacket((int)PacketType::CHAT_MESSAGE, &msg, sizeof(msg));
}

// ============================================================================
//  핸들러: 준비 토글
//
//  방장은 준비 대상이 아니다(시작 버튼을 가짐).
//  상태가 바뀌면 방 전원에게 ROOM_STATE를 다시 보내 버튼 표시를 맞춘다.
// ============================================================================

void LobbyManager::HandleRoomReady(ClientConnection* conn)
{
    if (conn->currentRoomId == 0) return;
    RoomData* room = GetRoom(conn->currentRoomId);
    if (!room) return;
    if (conn->clientId == room->hostClientId) return;   // 방장은 준비 없음

    bool now = !room->IsReady(conn->clientId);
    room->SetReady(conn->clientId, now);

    char line[MAX_CHAT_TEXT];
    std::snprintf(line, sizeof(line), "%s님이 %s",
        conn->playerName, now ? "준비를 완료했습니다." : "준비를 해제했습니다.");
    SendSystemToRoom(room, line);

    BroadcastRoomState(room);
}

// ============================================================================
//  핸들러: 채팅
//
//  채널
//   CHAT_ALL   : 로비에 로그인한 전원 (방 안이든 밖이든)
//   CHAT_PARTY : 같은 방 멤버만. 방이 없으면 전체로 강등한다.
//
//  이름과 팀은 서버가 채운다. 클라가 보낸 값은 쓰지 않는다(위조 방지).
// ============================================================================

void LobbyManager::HandleChat(ClientConnection* conn, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(ChatSendRequest)) return;
    if (conn->state == ClientConnection::ST_CONNECTED) return;   // 로그인 전엔 차단

    const ChatSendRequest* req = reinterpret_cast<const ChatSendRequest*>(body);

    // 본문 정리: 널 보장 + 빈 문자열 거부
    char text[MAX_CHAT_TEXT];
    std::memcpy(text, req->text, MAX_CHAT_TEXT);
    text[MAX_CHAT_TEXT - 1] = '\0';
    if (text[0] == '\0') return;

    int channel = req->channel;
    RoomData* room = (conn->currentRoomId != 0) ? GetRoom(conn->currentRoomId) : nullptr;

    // 로비에 팀 채널은 없다. 방이 없으면 파티도 성립하지 않는다.
    if (channel == CHAT_TEAM) channel = CHAT_PARTY;
    if (channel == CHAT_PARTY && room == nullptr) channel = CHAT_ALL;
    if (channel != CHAT_ALL && channel != CHAT_PARTY) channel = CHAT_ALL;

    ChatMessageData msg{};
    msg.channel = channel;
    msg.senderId = conn->clientId;
    msg.senderTeam = room ? room->GetTeam(conn->clientId) : TEAM_UNASSIGNED;
    std::strncpy(msg.senderName, conn->playerName, sizeof(msg.senderName) - 1);
    std::strncpy(msg.text, text, sizeof(msg.text) - 1);

    if (channel == CHAT_PARTY)
    {
        for (int memberId : room->memberIds)
        {
            ClientConnection* mc = GetClient(memberId);
            if (mc) mc->SendPacket((int)PacketType::CHAT_MESSAGE, &msg, sizeof(msg));
        }
        Log::Info("[채팅/파티] room=%d %s: %s", room->roomId, msg.senderName, msg.text);
    }
    else
    {
        for (auto& kv : authenticatedClients)
        {
            if (!kv.second->active) continue;
            kv.second->SendPacket((int)PacketType::CHAT_MESSAGE, &msg, sizeof(msg));
        }
        Log::Info("[채팅/전체] %s: %s", msg.senderName, msg.text);
    }
}

// ============================================================================
//  세션 종료 보고 처리 (Session_Manager → SessionEventReceiver → 여기)
//
//  세션이 끝나면 해당 방은 더 이상 의미가 없다.
//   - 아직 로비에 붙어 있는 멤버는 ST_AUTHENTICATED(로비)로 되돌린다.
//   - 방 자체를 제거한다 (게임 중 방을 유지할 이유가 없음).
//  이미 접속을 끊고 나간 클라는 GetClient가 nullptr을 주므로 자연히 건너뛴다.
// ============================================================================

void LobbyManager::OnSessionEnded(int sessionId, int reason,
    int totalPlayers, int survivors)
{
    std::lock_guard<std::mutex> lk(mtx);

    Log::Info("세션 종료 보고 수신: sid=%d reason=%d 생존=%d/%d",
        sessionId, reason, totalPlayers, survivors);

    auto sit = sessionToRoom.find(sessionId);
    if (sit == sessionToRoom.end())
    {
        Log::Warn("알 수 없는 sessionId=%d (이미 정리됨)", sessionId);
        return;
    }

    int roomId = sit->second;
    sessionToRoom.erase(sit);

    RoomData* room = GetRoom(roomId);
    if (!room) return;

    // 멤버 상태 복귀 (남아있는 연결만).
    std::vector<int> members = room->memberIds;
    for (int memberId : members)
    {
        ClientConnection* mc = GetClient(memberId);
        if (!mc) continue;
        mc->currentRoomId = 0;
        mc->state = ClientConnection::ST_AUTHENTICATED;
    }

    rooms.erase(roomId);
    Log::Info("방 정리 완료: roomId=%d (세션 %d 종료)", roomId, sessionId);
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

    // 알림 문구는 멤버를 지우기 전에 만들어 둔다.
    char leaveLine[MAX_CHAT_TEXT];
    std::snprintf(leaveLine, sizeof(leaveLine), "%s님이 파티에서 나갔습니다.", conn->playerName);

    room->RemoveMember(clientId);
    conn->currentRoomId = 0;

    if (room->IsEmpty())
    {
        Log::Info("빈 방 삭제: roomId=%d", rid);
        rooms.erase(rid);
    }
    else
    {
        SendSystemToRoom(room, leaveLine);

        if (room->hostClientId == clientId)
        {
            room->PromoteNewHostIfNeeded();
            Log::Info("호스트 변경: roomId=%d → 새 host=%d", rid, room->hostClientId);

            ClientConnection* nh = GetClient(room->hostClientId);
            if (nh)
            {
                char hostLine[MAX_CHAT_TEXT];
                std::snprintf(hostLine, sizeof(hostLine), "%s님이 새 파티장이 되었습니다.", nh->playerName);
                SendSystemToRoom(room, hostLine);
            }
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
        m.isReady = room->IsReady(cid) ? 1 : 0;
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