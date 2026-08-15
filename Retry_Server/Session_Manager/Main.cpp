#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"
#include "SessionRegistry.h"
#include "NetworkAcceptor.h"
#include "IpcReceiver.h"
#include "LobbyReporter.h"

#include <iostream>
#include <string>
#include <windows.h>

// ============================================================================
//  Session_Manager 진입점
//
//  - 포트 9001: 게임 클라이언트 TCP 접속 (IOCP)
//  - 포트 9002: 메인 서버(Retry_Server)로부터 IPC 수신 (별도 스레드)
//
//  주요 흐름:
//   1) Winsock 초기화
//   2) SessionRegistry (세션들의 보관소)
//   3) IpcReceiver 시작 (메인 서버 IPC 받기)
//   4) NetworkAcceptor 시작 (게임 클라 받기)
//   5) 콘솔 입력 대기 ("exit"로 종료)
// ============================================================================

constexpr int GAME_LISTEN_PORT     = 9001;
constexpr int IPC_LISTEN_PORT      = 9002;
constexpr int LOBBY_EVENT_PORT     = 9003;   // 로비로 세션 종료 보고
constexpr int WORKER_THREAD_COUNT  = 6;

int main()
{
    SetConsoleOutputCP(CP_UTF8);
    Log::Init("Session");

    if (!Net::StartupWinsock())
    {
        Log::Error("Winsock 초기화 실패");
        return 1;
    }

    LobbyReporter    reporter("127.0.0.1", LOBBY_EVENT_PORT);
    SessionRegistry  registry(&reporter);
    IpcReceiver      ipc(&registry, IPC_LISTEN_PORT);
    NetworkAcceptor  acceptor(&registry, GAME_LISTEN_PORT, WORKER_THREAD_COUNT);

    if (!ipc.Start())
    {
        Log::Error("IPC 수신 시작 실패");
        Net::CleanupWinsock();
        return 1;
    }
    if (!acceptor.Start())
    {
        Log::Error("게임 listen 시작 실패");
        ipc.Stop();
        Net::CleanupWinsock();
        return 1;
    }

    Log::Info("==== Session_Manager 시작 ====");
    Log::Info("  - 게임 클라 포트: %d", GAME_LISTEN_PORT);
    Log::Info("  - 메인 서버 IPC 포트: %d", IPC_LISTEN_PORT);
    Log::Info("  - 로비 보고 포트: %d", LOBBY_EVENT_PORT);
    Log::Info("  - IOCP 워커: %d개", WORKER_THREAD_COUNT);
    Log::Info("종료하려면 'exit' 입력.");

    std::string cmd;
    while (std::cin >> cmd)
    {
        if (cmd == "exit") break;
    }

    Log::Info("종료 요청 수신, 정리 중...");
    acceptor.Stop();
    ipc.Stop();
    registry.Shutdown();
    Net::CleanupWinsock();
    Log::Info("Session_Manager 종료 완료");
    return 0;
}
