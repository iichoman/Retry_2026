#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <atomic>
#include <thread>

class SessionRegistry;

// ============================================================================
//  IpcReceiver
//
//  메인 서버(Retry_Server)로부터 오는 IPC 명령을 받는 컴포넌트.
//  IOCP 안 쓰고 단순한 별도 스레드 + 동기 accept/recv로 처리.
//
//  근거:
//   - IPC 빈도가 매우 낮음 (게임 시작 시점에만)
//   - 단순한 send/recv로 충분
//   - IOCP 워커 풀과 분리되어 격리성 좋음
//
//  흐름:
//   1) listen on (port 9002)
//   2) accept 1개씩
//   3) RecvPacket → IpcCreateSession → registry.CreateSession
//   4) 응답으로 같은 IpcCreateSession 다시 echo (성공 알림)
//   5) close, 다음 accept
// ============================================================================

class IpcReceiver
{
public:
    IpcReceiver(SessionRegistry* registry, int listenPort);
    ~IpcReceiver();

    bool Start();
    void Stop();

private:
    SessionRegistry*  registry;
    int               listenPort;

    SOCKET            listenSock;
    std::thread       worker;
    std::atomic_bool  running;

    void RunLoop();
    void HandleOneClient(SOCKET clientSock);
};
