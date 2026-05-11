#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <atomic>

class GameSession;
class SessionRegistry;

// ============================================================================
//  SessionClientConnection
//
//  게임 세션 서버(포트 9001)에 연결된 클라이언트 1명을 표현.
//  로비의 ClientConnection과 구조 비슷하지만 인증/패킷 처리 흐름이 다름:
//
//   - 첫 8바이트: raw 인증 데이터 (sessionId 4 + clientId 4)
//   - 인증 성공 시: 해당 GameSession에 attach, 이후는 표준 패킷 형식
//   - 인증 실패 시: 즉시 연결 종료
// ============================================================================

class SessionClientConnection
{
public:
    enum State : int {
        ST_AWAIT_AUTH      = 0,    // 8바이트 인증 데이터 수신 대기
        ST_AUTHENTICATED   = 1,    // 세션 attach 완료, 게임 패킷 처리 중
    };

    SOCKET                socket;
    int                   clientId;       // 인증 후 채워짐
    int                   sessionId;      // 인증 후 채워짐
    std::atomic<State>    state;
    std::atomic_bool      active;

    GameSession*          session;        // 인증 후 attach (약한 참조)

    struct RecvOverlapped {
        WSAOVERLAPPED  over;
        WSABUF         wsabuf;
        char           buf[4096];
        int            opType;
    } recvOver;

    char  assembly[8192];
    int   assemblyUsed;

    explicit SessionClientConnection(SOCKET s);
    ~SessionClientConnection();

    bool PostRecv();
    bool OnRecvCompleted(int bytes, SessionRegistry* registry);
    void SendPacket(int packetType, const void* body, int bodySize);
    void Disconnect();
};

enum SessionOpType : int {
    OP_RECV   = 1,
    OP_SEND   = 2,
    OP_ACCEPT = 3,
};

struct SendOverlapped {
    WSAOVERLAPPED  over;
    WSABUF         wsabuf;
    int            opType;
    int            ownerClientId;
    char           buf[8192];
};
