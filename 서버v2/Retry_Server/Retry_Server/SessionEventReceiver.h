#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <atomic>
#include <thread>

class LobbyManager;

// ============================================================================
//  SessionEventReceiver
//
//  세션 매니저 → 로비 서버 역방향 IPC 수신 (포트 9003).
//  Session_Manager의 IpcReceiver와 대칭 구조.
//
//  받는 것: IPC_SESSION_ENDED (세션 종료 보고)
//   → LobbyManager::OnSessionEnded 호출 → 방 정리 + 클라 상태 복귀
//
//  설계 메모:
//   - 빈도가 낮아 IOCP 없이 단일 스레드 blocking accept로 충분.
//   - 127.0.0.1 로만 bind (외부 노출 금지).
// ============================================================================

class SessionEventReceiver
{
public:
    SessionEventReceiver(LobbyManager* lobby, int port);
    ~SessionEventReceiver();

    bool Start();
    void Stop();

private:
    LobbyManager*    lobby;
    int              listenPort;
    SOCKET           listenSock;
    std::atomic_bool running;
    std::thread      worker;

    void RunLoop();
    void HandleOneClient(SOCKET cs);
};
