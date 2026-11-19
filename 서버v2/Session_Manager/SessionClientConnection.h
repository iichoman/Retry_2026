#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <atomic>
#include <vector>

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

    // ── v2: 틱마다 묶어 보내기 (v2.ini send=batch) ──
    // SendPacket은 모아 두기만 하고, 세션이 틱 끝(또는 급한 일감 처리 뒤)에 FlushSend로 한 번에 보낸다.
    // 둘 다 세션 쪽에서만 부르므로(큐 모드: 틱 스레드 / 락 모드: 세션 락 안) 버퍼에 락이 필요 없다.
    void FlushSend();
    bool OnBatchSendDone(bool ok, DWORD bytes);   // IOCP 워커가 송신 완료 시 호출. 정상이면 true

    struct BatchSendOverlapped {                   // SendOverlapped와 앞부분(over, wsabuf, opType) 배치가 같다
        WSAOVERLAPPED  over;
        WSABUF         wsabuf;
        int            opType;
    } batchOver;

private:
    std::vector<char>     pendingBuf;               // 이번 틱에 모은 패킷
    std::vector<char>     inflightBuf;              // 커널이 보내는 중인 패킷
    std::atomic_bool      sending{ false };         // 송신 진행 중 (완료 통지 때 워커가 false로)
};

enum SessionOpType : int {
    OP_RECV   = 1,
    OP_SEND   = 2,
    OP_ACCEPT = 3,
    OP_SEND_BATCH = 4,     // v2: 연결마다 하나인 묶음 송신 (delete하지 않는다)
};

struct SendOverlapped {
    WSAOVERLAPPED  over;
    WSABUF         wsabuf;
    int            opType;
    int            ownerClientId;
    char           buf[8192];
};
