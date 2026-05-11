#pragma once
#include <memory>
#include <unordered_map>
#include <mutex>

class ClientConnection;
class RoomData;
class SessionDispatcher;

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

    // 정상 종료. 모든 클라/방 정리.
    void Shutdown();

private:
    std::mutex                                                  mtx;
    std::unordered_map<int, std::unique_ptr<ClientConnection>>  authenticatedClients;
    std::unordered_map<int, std::unique_ptr<ClientConnection>>  pendingClients;   // 인증 전
    std::unordered_map<int, std::unique_ptr<RoomData>>          rooms;

    int  nextClientId;
    int  nextRoomId;
    int  nextSessionId;
    int  nextPendingId;

    SessionDispatcher* dispatcher;

    // 패킷 핸들러들 (mtx 잠긴 상태에서 호출됨)
    void HandleLogin(ClientConnection* conn, const char* body, int bodySize);
    void HandleRoomCreate(ClientConnection* conn, const char* body, int bodySize);
    void HandleRoomJoin(ClientConnection* conn, const char* body, int bodySize);
    void HandleRoomList(ClientConnection* conn);
    void HandleGameStart(ClientConnection* conn);

    // 헬퍼 (mtx 잠긴 상태에서만 호출)
    ClientConnection* GetClient(int clientId);
    RoomData*         GetRoom(int roomId);

    // 방에서 클라 제거. 빈 방이면 방도 삭제.
    void RemoveClientFromRoom(int clientId);
};
