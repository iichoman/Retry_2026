#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"
#include "LobbyManager.h"
#include "NetworkAcceptor.h"
#include "SessionDispatcher.h"

#include <iostream>
#include <string>

// ============================================================================
//  Retry_Server (로비 서버) 진입점
//
//  - 포트 9000: 클라이언트 TCP 접속 받기
//  - 포트 9002 (외향): 세션 매니저로 IPC 송신
//
//  컴포넌트 조립 순서:
//   1) Winsock 초기화
//   2) SessionDispatcher (세션 매니저로 명령 송신할 채널)
//   3) LobbyManager (도메인 로직)
//   4) NetworkAcceptor (TCP 수락 + IOCP 워커 풀)
//   5) 콘솔 입력 대기 ("exit"로 종료)
// ============================================================================

constexpr int LOBBY_LISTEN_PORT  = 9000;
constexpr int SESSION_IPC_PORT   = 9002;
constexpr int WORKER_THREAD_COUNT = 6;

int main()
{
    Log::Init("Lobby");

    if (!Net::StartupWinsock())
    {
        Log::Error("Winsock 초기화 실패");
        return 1;
    }

    SessionDispatcher dispatcher("127.0.0.1", SESSION_IPC_PORT);
    LobbyManager      lobby(&dispatcher);
    NetworkAcceptor   acceptor(&lobby, LOBBY_LISTEN_PORT, WORKER_THREAD_COUNT);

    if (!acceptor.Start())
    {
        Log::Error("로비 서버 시작 실패");
        Net::CleanupWinsock();
        return 1;
    }

    Log::Info("==== Retry_Server (로비) 시작 ====");
    Log::Info("  - 클라 접속 포트: %d", LOBBY_LISTEN_PORT);
    Log::Info("  - 세션 매니저 IPC 포트: %d", SESSION_IPC_PORT);
    Log::Info("  - IOCP 워커: %d개", WORKER_THREAD_COUNT);
    Log::Info("종료하려면 'exit' 입력.");

    std::string cmd;
    while (std::cin >> cmd)
    {
        if (cmd == "exit") break;
        if (cmd == "help")
        {
            Log::Info("명령어: exit");
        }
    }

    Log::Info("종료 요청 수신, 정리 중...");
    acceptor.Stop();
    lobby.Shutdown();
    Net::CleanupWinsock();
    Log::Info("로비 서버 종료 완료");
    return 0;
}
