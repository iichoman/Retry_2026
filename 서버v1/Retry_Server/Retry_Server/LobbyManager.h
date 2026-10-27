#pragma once
#include "../DB/DbWorker.h"

#include <memory>
#include <string>
#include <unordered_map>
#include <mutex>
#include <vector>

class ClientConnection;
class RoomData;
class SessionDispatcher;
struct IpcPlayerResult;

// ============================================================================
//  LobbyManager
//
//  로비 서버의 도메인 로직 전체를 담당.
//   - 접속한 모든 ClientConnection 관리 (clientId 할당)
//   - 모든 방(RoomData) 관리 (roomId 할당)
//   - 세션 시작 시 sessionId 할당 + SessionDispatcher 통해 세션 매니저로 명령
//   - 패킷 처리 (HandlePacket): ClientConnection이 패킷 완성 시마다 호출
//
//  스레드 모델:
//   - IOCP 워커 N개가 동시에 HandlePacket 호출 가능 → mutex로 직렬화
//   - 모든 도메인 메서드는 mtx 잠긴 상태에서 동작
// ============================================================================

class LobbyManager
{
public:
    explicit LobbyManager(SessionDispatcher* dispatcher);
    ~LobbyManager();

    // 새 클라이언트 등록 (NetworkAcceptor가 호출).
    // 소유권을 LobbyManager가 갖는다.
    // 임시 ID 부여만 하고 LOGIN 받기 전엔 clientId=0.
    void RegisterClient(std::unique_ptr<ClientConnection> conn);

    // 패킷 처리. ClientConnection이 OnRecvCompleted에서 호출.
    void HandlePacket(ClientConnection* conn, int packetType,
        const char* body, int bodySize);

    // 클라이언트 연결 종료 처리 (소켓 끊김 등).
    // 방 정리, 호스트 마이그레이션, 다른 멤버에게 통보 처리.
    void OnClientDisconnected(ClientConnection* conn);

    // 세션 매니저가 IPC_SESSION_ENDED로 보고했을 때 호출 (SessionEventReceiver).
    // 해당 세션의 방을 정리하고 아직 남아있는 멤버를 로비 상태로 되돌린다.
    void OnSessionEnded(int sessionId, int reason, int totalPlayers, int survivors,
        const IpcPlayerResult* results, int resultCount);

    // DB 워커 시작/종료. Main에서 호출.
    bool StartDb();
    void StopDb();

    // 정상 종료. 모든 클라/방 정리.
    void Shutdown();

private:
    std::mutex                                                  mtx;
    std::unordered_map<int, std::unique_ptr<ClientConnection>>  authenticatedClients;
    std::unordered_map<int, std::unique_ptr<ClientConnection>>  pendingClients;   // 인증 전
    std::unordered_map<int, std::unique_ptr<RoomData>>          rooms;
    std::unordered_map<int, int>                                sessionToRoom;   // sessionId → roomId

    int  nextClientId;
    int  nextRoomId;
    int  nextSessionId;
    int  nextPendingId;

    SessionDispatcher* dispatcher;
    DbWorker           db;

    // DB 조회가 끝난 뒤 로그인을 마무리한다. DB 스레드에서 호출됨.
    void FinishLogin(int pendingId, const std::string& nickname,
        int dbPlayerId, int reconnectClientId);
    // 회원가입 / 비밀번호 재설정 / 전적 조회 (전부 비동기)
    void HandleRegister(ClientConnection* conn, const char* body, int bodySize);
    void HandleResetPassword(ClientConnection* conn, const char* body, int bodySize);
    void HandlePlayerStats(ClientConnection* conn);
    // pendingId 또는 clientId로 아직 살아있는 커넥션을 찾아 결과를 보낸다.
    // DB 스레드에서 호출용 (안에서 mtx를 잡는다)
    void SendAccountResult(int connKey, bool success, const char* reason, int packetType);
    // 이미 mtx를 쥔 상태에서 호출용. mtx는 재귀 락이 아니라 반드시 구분해야 한다.
    void SendAccountResultLocked(int connKey, bool success, const char* reason, int packetType);
    // 그 닉네임을 지금 누가 쓰고 있는지 (mtx 잠긴 상태에서 호출).
    //  - 로비에 접속해 있으면 사용 중
    //  - 인게임(ST_STARTING)이면 잠긴 상태. 단 reconnectClientId가 본인이면 통과.
    // 반환 true = 거부해야 함.
    bool IsNicknameTaken(const char* nickname, int reconnectClientId) const;
    // 세션이 끝난 뒤 그 방 멤버들의 닉네임 잠금을 푼다.
    void ReleaseNameLocks(const std::vector<int>& memberIds);

    // 패킷 핸들러들 (mtx 잠긴 상태에서 호출됨)
    void HandleLogin(ClientConnection* conn, const char* body, int bodySize);
    void HandleRoomCreate(ClientConnection* conn, const char* body, int bodySize);
    void HandleRoomJoin(ClientConnection* conn, const char* body, int bodySize);
    void HandleRoomList(ClientConnection* conn);
    void HandleGameStart(ClientConnection* conn);
    void HandleRoomLeave(ClientConnection* conn);
    void HandleSelectTeam(ClientConnection* conn, const char* body, int bodySize);
    // 채팅. 채널에 따라 전체(로비 접속자) 또는 파티(같은 방)로 중계.
    void HandleChat(ClientConnection* conn, const char* body, int bodySize);
    // 준비 상태 토글 (방장 제외).
    void HandleRoomReady(ClientConnection* conn);

    // 서버가 만든 [시스템] 알림. 방 전원 또는 특정 1인에게.
    // SESSION_ASSIGN에 넣을 세션 서버 IP를 그 클라 기준으로 구한다.
    void ResolveSessionServerIP(ClientConnection* conn, char* out, int outSize);
    void SendSystemToRoom(RoomData* room, const char* text);
    void SendSystemTo(ClientConnection* conn, const char* text);

    // 방 멤버/팀 현황을 그 방 전원에게 push (입장/퇴장/팀변경/방장변경 시).
    void BroadcastRoomState(RoomData* room);

    // 헬퍼 (mtx 잠긴 상태에서만 호출)
    ClientConnection* GetClient(int clientId);
    RoomData* GetRoom(int roomId);

    // 방에서 클라 제거. 빈 방이면 방도 삭제.
    void RemoveClientFromRoom(int clientId);
};