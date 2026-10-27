#include "LobbyManager.h"
#include "ClientConnection.h"
#include "RoomData.h"
#include "SessionDispatcher.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <WS2tcpip.h>   // inet_ntop

#include <chrono>
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
    case PacketType::REGISTER_REQUEST:
        HandleRegister(conn, body, bodySize);
        break;
    case PacketType::RESET_PW_REQUEST:
        HandleResetPassword(conn, body, bodySize);
        break;
    case PacketType::PLAYER_STATS_REQUEST:
        HandlePlayerStats(conn);
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

    char nick[MAX_PLAYER_NAME];
    std::strncpy(nick, req->playerName, sizeof(nick) - 1);
    nick[sizeof(nick) - 1] = '\0';

    auto fail = [&](const char* why)
    {
        LoginResult res{};
        res.success = 0;
        res.clientId = 0;
        std::strncpy(res.failReason, why, sizeof(res.failReason) - 1);
        conn->SendPacket((int)PacketType::LOGIN_RESULT, &res, sizeof(res));
        Log::Info("로그인 거부: name=%s (%s)", nick, why);
    };

    if (nick[0] == '\0') { fail("닉네임을 입력하세요."); return; }

    // 같은 닉네임으로 이미 접속해 있으면 거부. (DB의 UNIQUE는 계정 중복을 막고,
    //  이 검사는 같은 계정으로 동시 접속하는 것을 막는다)
    if (IsNicknameTaken(nick, req->reconnectClientId))
    {
        fail("이미 접속 중인 닉네임입니다."); return;
    }

    // pendingClients의 키를 찾아 둔다. DB 응답이 돌아왔을 때 이 키로 다시 찾는다.
    // 포인터를 들고 있으면 그 사이 연결이 끊겨 삭제됐을 때 위험하다.
    int pendingId = 0;
    for (auto& kv : pendingClients)
    {
        if (kv.second.get() == conn) { pendingId = kv.first; break; }
    }
    if (pendingId == 0)
    {
        Log::Error("LOGIN: pending에서 클라 못 찾음");
        return;
    }

    // DB 조회는 수 ms가 걸린다. IOCP 스레드를 잡아두면 안 되므로 큐에 넘기고
    // 즉시 반환한다. 응답은 FinishLogin이 보낸다.
    char pw[MAX_PASSWORD];
    std::strncpy(pw, req->password, sizeof(pw) - 1);
    pw[sizeof(pw) - 1] = '\0';

    std::string nickStr(nick);
    std::string pwStr(pw);
    int reconnectCid = req->reconnectClientId;

    db.RequestLogin(nickStr, pwStr,
        [this, pendingId, nickStr, reconnectCid](int result, int playerId)
        {
            if (result == 1) { SendAccountResult(pendingId, false,
                "존재하지 않는 닉네임입니다.", (int)PacketType::LOGIN_RESULT); return; }
            if (result == 2) { SendAccountResult(pendingId, false,
                "비밀번호가 올바르지 않습니다.", (int)PacketType::LOGIN_RESULT); return; }
            // result 0 = 성공, -1 = DB 없음(그냥 통과)
            FinishLogin(pendingId, nickStr, playerId, reconnectCid);
        });
}

// DB 조회 완료 후 로그인 마무리. DB 워커 스레드에서 호출된다.
void LobbyManager::FinishLogin(int pendingId, const std::string& nickname,
    int dbPlayerId, int reconnectClientId)
{
    std::lock_guard<std::mutex> lk(mtx);

    auto it = pendingClients.find(pendingId);
    if (it == pendingClients.end()) return;          // 그 사이 접속이 끊겼다

    ClientConnection* conn = it->second.get();
    if (!conn || !conn->active.load()) return;
    if (conn->state != ClientConnection::ST_CONNECTED) return;

    // DB를 기다리는 동안 같은 닉네임이 먼저 들어왔을 수 있다.
    if (IsNicknameTaken(nickname.c_str(), reconnectClientId))
    {
        LoginResult res{};
        res.success = 0;
        std::strncpy(res.failReason, "이미 접속 중인 닉네임입니다.",
            sizeof(res.failReason) - 1);
        conn->SendPacket((int)PacketType::LOGIN_RESULT, &res, sizeof(res));
        return;
    }

    // 주인이 돌아온 경우: 예전 커넥션의 잠금을 먼저 푼다.
    if (reconnectClientId != 0)
    {
        auto old = authenticatedClients.find(reconnectClientId);
        if (old != authenticatedClients.end() && old->second &&
            !old->second->active.load() &&
            std::strcmp(old->second->playerName, nickname.c_str()) == 0)
        {
            old->second->state = ClientConnection::ST_CONNECTED;
            old->second->startingAtMs = 0;
        }
    }

    int newId = nextClientId++;
    std::strncpy(conn->playerName, nickname.c_str(), sizeof(conn->playerName) - 1);
    conn->playerName[sizeof(conn->playerName) - 1] = '\0';
    conn->clientId = newId;
    conn->dbPlayerId = dbPlayerId;
    conn->state = ClientConnection::ST_AUTHENTICATED;

    std::unique_ptr<ClientConnection> moved = std::move(it->second);
    pendingClients.erase(it);
    authenticatedClients[newId] = std::move(moved);

    LoginResult res{};
    res.success = 1;
    res.clientId = newId;
    conn->SendPacket((int)PacketType::LOGIN_RESULT, &res, sizeof(res));

    Log::Info("로그인: id=%d name=%s dbPlayerId=%d", newId, conn->playerName, dbPlayerId);
}

// 닉네임 잠금 유효 시간. 세션 종료 보고가 유실돼도 이 시간이 지나면 풀린다.
static constexpr long long NAME_LOCK_TIMEOUT_MS = 30 * 60 * 1000;   // 30분

static long long NowMs()
{
    using namespace std::chrono;
    return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
}

bool LobbyManager::IsNicknameTaken(const char* nickname, int reconnectClientId) const
{
    if (!nickname || !*nickname) return false;

    const long long now = NowMs();

    for (const auto& kv : authenticatedClients)
    {
        const ClientConnection* c = kv.second.get();
        if (!c) continue;
        if (std::strcmp(c->playerName, nickname) != 0) continue;

        // (1) 로비에 붙어 있다 → 확실히 사용 중
        if (c->active.load()) return true;

        // (2) 인게임에 들어가느라 로비 연결만 끊은 상태.
        //     연결은 죽었지만 객체는 남아 있어서 이름을 잠가 둘 수 있다.
        if (c->state == ClientConnection::ST_STARTING)
        {
            if (kv.first == reconnectClientId) return false;   // 주인이 돌아왔다
            if (c->startingAtMs > 0 &&
                now - c->startingAtMs > NAME_LOCK_TIMEOUT_MS)
                return false;                                   // 잠금 만료
            return true;
        }
        // (3) 그냥 나간 연결 → 잠글 이유 없음
    }
    return false;
}

void LobbyManager::ReleaseNameLocks(const std::vector<int>& memberIds)
{
    for (int cid : memberIds)
    {
        auto it = authenticatedClients.find(cid);
        if (it == authenticatedClients.end()) continue;

        ClientConnection* c = it->second.get();
        if (!c || c->active.load()) continue;          // 살아있으면 건드리지 않는다

        if (c->state == ClientConnection::ST_STARTING)
        {
            // 객체를 erase하면 IOCP의 지연 완료와 겹쳐 위험하다.
            // 상태만 되돌려 잠금을 푼다. 실제 정리는 Shutdown에서.
            c->state = ClientConnection::ST_CONNECTED;
            c->startingAtMs = 0;
        }
    }
}

bool LobbyManager::StartDb()
{
    return db.Start();
}

void LobbyManager::StopDb()
{
    db.Stop();
}

// ============================================================================
//  핸들러: 계정 (회원가입 / 비밀번호 재설정 / 전적)
// ============================================================================

// 결과 패킷을 보낸다. connKey가 음수면 pendingClients(인증 전), 양수면 authenticated.
// DB 스레드에서 호출되므로 그 사이 끊겼을 수 있어 매번 다시 찾는다.
void LobbyManager::SendAccountResult(int connKey, bool success,
    const char* reason, int packetType)
{
    std::lock_guard<std::mutex> lk(mtx);
    SendAccountResultLocked(connKey, success, reason, packetType);
}

void LobbyManager::SendAccountResultLocked(int connKey, bool success,
    const char* reason, int packetType)
{
    ClientConnection* conn = nullptr;
    if (connKey < 0)
    {
        auto it = pendingClients.find(connKey);
        if (it != pendingClients.end()) conn = it->second.get();
    }
    else
    {
        auto it = authenticatedClients.find(connKey);
        if (it != authenticatedClients.end()) conn = it->second.get();
    }
    if (!conn || !conn->active.load()) return;

    if (packetType == (int)PacketType::LOGIN_RESULT)
    {
        LoginResult res{};
        res.success = success ? 1 : 0;
        res.clientId = 0;
        if (reason) std::strncpy(res.failReason, reason, sizeof(res.failReason) - 1);
        conn->SendPacket(packetType, &res, sizeof(res));
    }
    else
    {
        AccountResult res{};
        res.success = success ? 1 : 0;
        if (reason) std::strncpy(res.failReason, reason, sizeof(res.failReason) - 1);
        conn->SendPacket(packetType, &res, sizeof(res));
    }
}

// 인증 전 커넥션의 임시 키를 찾는다. (mtx 잠긴 상태에서 호출)
static int FindPendingKey(
    const std::unordered_map<int, std::unique_ptr<ClientConnection>>& pending,
    const ClientConnection* conn)
{
    for (const auto& kv : pending)
        if (kv.second.get() == conn) return kv.first;
    return 0;
}

void LobbyManager::HandleRegister(ClientConnection* conn, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(AccountRequest)) return;
    const AccountRequest* req = reinterpret_cast<const AccountRequest*>(body);

    char nick[MAX_PLAYER_NAME], pw[MAX_PASSWORD];
    std::strncpy(nick, req->playerName, sizeof(nick) - 1); nick[sizeof(nick) - 1] = '\0';
    std::strncpy(pw, req->password, sizeof(pw) - 1);       pw[sizeof(pw) - 1] = '\0';

    int key = (conn->clientId > 0) ? conn->clientId
        : FindPendingKey(pendingClients, conn);
    if (key == 0) return;

    if (nick[0] == '\0' || pw[0] == '\0')
    {
        SendAccountResultLocked(key, false, "닉네임과 비밀번호를 입력하세요.",
            (int)PacketType::REGISTER_RESULT);
        return;
    }

    db.RequestRegister(std::string(nick), std::string(pw),
        [this, key](int result)
        {
            if (result == 0)
                SendAccountResult(key, true, "", (int)PacketType::REGISTER_RESULT);
            else if (result == 1)
                SendAccountResult(key, false, "이미 존재하는 닉네임입니다.",
                    (int)PacketType::REGISTER_RESULT);
            else
                SendAccountResult(key, false, "서버 DB에 연결할 수 없습니다.",
                    (int)PacketType::REGISTER_RESULT);
        });
}

void LobbyManager::HandleResetPassword(ClientConnection* conn, const char* body, int bodySize)
{
    if (bodySize < (int)sizeof(AccountRequest)) return;
    const AccountRequest* req = reinterpret_cast<const AccountRequest*>(body);

    char nick[MAX_PLAYER_NAME], pw[MAX_PASSWORD];
    std::strncpy(nick, req->playerName, sizeof(nick) - 1); nick[sizeof(nick) - 1] = '\0';
    std::strncpy(pw, req->password, sizeof(pw) - 1);       pw[sizeof(pw) - 1] = '\0';

    int key = (conn->clientId > 0) ? conn->clientId
        : FindPendingKey(pendingClients, conn);
    if (key == 0) return;

    if (nick[0] == '\0' || pw[0] == '\0')
    {
        SendAccountResultLocked(key, false, "닉네임과 새 비밀번호를 입력하세요.",
            (int)PacketType::RESET_PW_RESULT);
        return;
    }

    // 지금 그 계정으로 접속해 있으면 바꾸지 못하게 막는다.
    if (IsNicknameTaken(nick, 0))
    {
        SendAccountResultLocked(key, false, "접속 중인 계정은 변경할 수 없습니다.",
            (int)PacketType::RESET_PW_RESULT);
        return;
    }

    db.RequestResetPassword(std::string(nick), std::string(pw),
        [this, key](int result)
        {
            if (result == 0)
                SendAccountResult(key, true, "", (int)PacketType::RESET_PW_RESULT);
            else if (result == 1)
                SendAccountResult(key, false, "존재하지 않는 닉네임입니다.",
                    (int)PacketType::RESET_PW_RESULT);
            else
                SendAccountResult(key, false, "서버 DB에 연결할 수 없습니다.",
                    (int)PacketType::RESET_PW_RESULT);
        });
}

void LobbyManager::HandlePlayerStats(ClientConnection* conn)
{
    if (conn->state == ClientConnection::ST_CONNECTED) return;   // 인증 후에만
    int cid = conn->clientId;
    if (cid <= 0) return;

    std::string nick(conn->playerName);

    db.RequestStats(nick,
        [this, cid](bool ok, DbWorker::Stats s)
        {
            std::lock_guard<std::mutex> lk(mtx);
            auto it = authenticatedClients.find(cid);
            if (it == authenticatedClients.end()) return;
            ClientConnection* c = it->second.get();
            if (!c || !c->active.load()) return;

            PlayerStatsResult res{};
            res.success = ok ? 1 : 0;
            res.totalRuns = s.runs;
            res.totalExtractions = s.extractions;
            res.monsterKills = s.monsterKills;
            res.playerKills = s.playerKills;
            res.bestSeconds = s.bestSeconds;
            c->SendPacket((int)PacketType::PLAYER_STATS_RESULT, &res, sizeof(res));
        });
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

    // 모든 멤버에게 SESSION_ASSIGN 송신.
    // 세션 서버 주소는 "그 클라가 실제로 접속해 들어온 서버 IP"를 그대로 돌려준다.
    // 127.0.0.1로 고정하면 다른 PC에서 접속한 클라가 자기 자신에게 붙으려다 실패한다.
    // (같은 PC면 getsockname이 127.0.0.1을 주므로 기존 동작과 동일하다)
    for (int memberId : playerIds)
    {
        ClientConnection* mc = GetClient(memberId);
        if (!mc) continue;

        SessionAssignData assign{};
        assign.sessionId = sessionId;
        assign.mapSeed = mapSeed;
        assign.sessionServerPort = 9001;
        ResolveSessionServerIP(mc, assign.sessionServerIP, sizeof(assign.sessionServerIP));

        mc->SendPacket((int)PacketType::SESSION_ASSIGN, &assign, sizeof(assign));
        mc->state = ClientConnection::ST_STARTING;
        mc->startingAtMs = NowMs();      // 여기서부터 닉네임이 잠긴다
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

// 이 클라가 접속해 들어온 쪽의 서버 IP를 문자열로 채운다.
// 로컬이면 127.0.0.1, LAN이면 192.168.x.x 가 그대로 나온다.
void LobbyManager::ResolveSessionServerIP(ClientConnection* conn, char* out, int outSize)
{
    if (!out || outSize <= 0) return;
    out[0] = '\0';

    sockaddr_in local{};
    int len = (int)sizeof(local);
    if (conn && conn->socket != INVALID_SOCKET &&
        getsockname(conn->socket, (sockaddr*)&local, &len) == 0)
    {
        char buf[INET_ADDRSTRLEN] = {};
        if (inet_ntop(AF_INET, &local.sin_addr, buf, sizeof(buf)) != nullptr &&
            buf[0] != '\0' && std::strcmp(buf, "0.0.0.0") != 0)
        {
            std::strncpy(out, buf, outSize - 1);
            out[outSize - 1] = '\0';
            return;
        }
    }
    std::strncpy(out, "127.0.0.1", outSize - 1);   // 못 구하면 기존 동작
    out[outSize - 1] = '\0';
}

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
    int totalPlayers, int survivors,
    const IpcPlayerResult* results, int resultCount)
{
    std::lock_guard<std::mutex> lk(mtx);

    Log::Info("세션 종료 보고 수신: sid=%d reason=%d 생존=%d/%d",
        sessionId, reason, totalPlayers, survivors);

    // 전적 저장. DB 스레드에 넘기고 즉시 넘어간다(여기서 기다리면 로비가 멈춘다).
    for (int i = 0; i < resultCount; ++i)
    {
        const IpcPlayerResult& r = results[i];
        if (r.playerName[0] == '\0') continue;
        db.RequestPlayRecord(std::string(r.playerName), sessionId,
            r.extracted != 0, r.playSeconds, r.monsterKills, r.playerKills);
    }
    if (resultCount > 0)
        Log::Info("[DB] 전적 %d건 기록 요청 (대기 %zu건)", resultCount, db.PendingCount());

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

    // 인게임 동안 잠가 둔 닉네임을 푼다.
    ReleaseNameLocks(room->memberIds);

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