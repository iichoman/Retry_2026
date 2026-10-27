#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <Mswsock.h>
#include <atomic>
#include <thread>
#include <vector>

class LobbyManager;

// ============================================================================
//  NetworkAcceptor
//
//  TCP listen 소켓 + AcceptEx 비동기 수락 + IOCP 워커 스레드 풀.
//   - Start()로 listen + 워커 시작
//   - 워커들이 GQCS 돌면서 ACCEPT/RECV/SEND 완료 처리
//   - 클라 접속 시 ClientConnection 만들어 LobbyManager에 등록
//   - RECV 완료 시 해당 ClientConnection.OnRecvCompleted 호출
//   - SEND 완료 시 SendOverlapped delete
//   - Stop()으로 정리
// ============================================================================

class NetworkAcceptor
{
public:
    NetworkAcceptor(LobbyManager* lobby, int listenPort, int workerThreadCount);
    ~NetworkAcceptor();

    bool Start();
    void Stop();

    HANDLE GetIOCP() const { return hIocp; }

private:
    LobbyManager*             lobby;
    int                       listenPort;
    int                       workerCount;

    SOCKET                    listenSock;
    HANDLE                    hIocp;

    std::vector<std::thread>  workers;
    std::atomic_bool          running;

    // AcceptEx용 임시 클라 소켓 + 버퍼 (재사용)
    SOCKET                    acceptSock;
    LPFN_ACCEPTEX             acceptExFn;

    struct AcceptOver {
        WSAOVERLAPPED  over;
        int            opType;     // OP_ACCEPT
        char           buf[2 * (sizeof(sockaddr_in) + 16)];
    } acceptOver;

    bool LoadAcceptEx();
    void PostAccept();
    void WorkerLoop();
};
