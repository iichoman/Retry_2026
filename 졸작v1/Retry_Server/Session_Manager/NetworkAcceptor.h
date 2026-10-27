#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <Mswsock.h>
#include <atomic>
#include <thread>
#include <vector>

class SessionRegistry;

// ============================================================================
//  NetworkAcceptor (게임 클라 측, 포트 9001)
//
//  로비의 NetworkAcceptor와 구조 동일. 차이는:
//   - 받는 객체가 SessionClientConnection
//   - 인증 흐름이 raw 8바이트로 시작 (그 후 표준 패킷)
//   - SessionRegistry를 알고 있어 인증 시점에 세션 lookup
// ============================================================================

class NetworkAcceptor
{
public:
    NetworkAcceptor(SessionRegistry* registry, int listenPort, int workerThreadCount);
    ~NetworkAcceptor();

    bool Start();
    void Stop();

private:
    SessionRegistry*          registry;
    int                       listenPort;
    int                       workerCount;

    SOCKET                    listenSock;
    HANDLE                    hIocp;

    std::vector<std::thread>  workers;
    std::atomic_bool          running;

    SOCKET                    acceptSock;
    LPFN_ACCEPTEX             acceptExFn;

    struct AcceptOver {
        WSAOVERLAPPED  over;
        int            opType;
        char           buf[2 * (sizeof(sockaddr_in) + 16)];
    } acceptOver;

    bool LoadAcceptEx();
    void PostAccept();
    void WorkerLoop();
};
