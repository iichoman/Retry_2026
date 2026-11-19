#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"
#include "LobbyManager.h"
#include "NetworkAcceptor.h"
#include "SessionDispatcher.h"
#include "SessionEventReceiver.h"
#include "../DB/DbConfig.h"

#include <cstdio>
#include <cstring>
#include <iostream>
#include <string>
#include <windows.h>

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
constexpr int SESSION_EVENT_PORT = 9003;   // 세션 매니저 → 로비 역방향 IPC
constexpr int WORKER_THREAD_COUNT = 6;

// server.ini [net] session_host = 세션 서버 PC 주소. 없으면 기본값(같은 PC)으로 만든다.
static std::string LoadSessionHost()
{
    char path[MAX_PATH];
    DWORD n = GetModuleFileNameA(nullptr, path, MAX_PATH);
    char* slash = (n > 0) ? strrchr(path, '\\') : nullptr;
    if (slash) strcpy_s(slash + 1, MAX_PATH - (slash + 1 - path), "server.ini");
    else strcpy_s(path, "server.ini");
    if (GetFileAttributesA(path) == INVALID_FILE_ATTRIBUTES)
    {
        FILE* f = nullptr;
        if (fopen_s(&f, path, "w") == 0 && f)
        {
            fputs("; 분산 배치: 세션 서버를 다른 PC에서 돌릴 때 그 PC의 IP를 적는다.\n"
                  "; 같은 PC면 127.0.0.1 (기본). 다른 PC면 보관함 파일을 같이 쓸 수 없어 기본 장비로 출격한다.\n"
                  "[net]\n"
                  "session_host=127.0.0.1\n", f);
            fclose(f);
        }
    }
    char buf[64];
    GetPrivateProfileStringA("net", "session_host", "127.0.0.1", buf, sizeof(buf), path);
    char* sp = strpbrk(buf, " \t;");
    if (sp) *sp = 0;
    return buf[0] ? std::string(buf) : std::string("127.0.0.1");
}

int main(int argc,char** argv)
{
    SetConsoleOutputCP(CP_UTF8);
    Log::Init("Lobby");

    // DB만 준비하고 끝내는 모드 (DB_설치.bat / DB_초기화.bat)
    //   --db-init  : 없으면 만들고 표·프로시저를 최신으로 (데이터 유지)
    //   --db-reset : RetryDB를 지우고 처음부터 (계정·전적·보관함 전부 삭제)
    if (argc > 1 && (std::string(argv[1]) == "--db-init" || std::string(argv[1]) == "--db-reset"))
    {
        OdbcSession setup;
        const bool ok = setup.Setup(std::string(argv[1]) == "--db-reset");
        if (ok) Log::Info("[DB] 준비 완료: %s", WideToUtf8(DbName()).c_str());
        else    Log::Error("[DB] 준비 실패 - SQL Server가 켜져 있는지, db.ini의 server 이름이 맞는지 확인");
        return ok ? 0 : 1;
    }

    if (!Net::StartupWinsock())
    {
        Log::Error("Winsock 초기화 실패");
        return 1;
    }

    const bool isolated=argc>1 && std::string(argv[1])=="--c-test";
    const std::string sessionHost = isolated ? std::string("127.0.0.1") : LoadSessionHost();
    const bool remoteSession = !Net::IsLoopbackHost(sessionHost.c_str());
    SessionDispatcher dispatcher(sessionHost, isolated?39102:SESSION_IPC_PORT);
    LobbyManager      lobby(&dispatcher);
    lobby.sessionGamePort=isolated?39101:9001;
    lobby.sessionHost=sessionHost;
    NetworkAcceptor   acceptor(&lobby, isolated?39100:LOBBY_LISTEN_PORT, WORKER_THREAD_COUNT);
    SessionEventReceiver eventRx(&lobby, isolated?39103:SESSION_EVENT_PORT);
    eventRx.acceptRemote = remoteSession;   // 세션 서버가 다른 PC면 그 PC의 종료 보고를 받는다

    // DB 워커 시작. 접속에 실패해도 DB_OPTIONAL이면 게임은 그대로 돌아간다.
    // 테스트 실행(--c-test)은 DB 없이 작업 스레드만 돌린다 (보관함은 파일)
    if (!lobby.StartDb(!isolated) && !isolated && !DB_OPTIONAL)
    {
        Log::Error("DB 접속 실패 (DB_OPTIONAL=false)");
        Net::CleanupWinsock();
        return 1;
    }

    if (!eventRx.Start())
    {
        Log::Error("세션 이벤트 수신 시작 실패");
        Net::CleanupWinsock();
        return 1;
    }
    if (!acceptor.Start())
    {
        Log::Error("로비 서버 시작 실패");
        eventRx.Stop();
        Net::CleanupWinsock();
        return 1;
    }

    Log::Info("==== Retry_Server (로비) 시작 ====");
    Log::Info("  - 클라 접속 포트: %d", LOBBY_LISTEN_PORT);
    Log::Info("  - 세션 매니저 IPC: %s:%d%s", sessionHost.c_str(), SESSION_IPC_PORT, remoteSession ? " (다른 PC, 보관함 저장 없음)" : "");
    Log::Info("  - 세션 종료 보고 수신 포트: %d", SESSION_EVENT_PORT);
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
    eventRx.Stop();
    lobby.StopDb();      // 큐에 남은 전적까지 마저 기록하고 종료
    lobby.Shutdown();
    Net::CleanupWinsock();
    Log::Info("로비 서버 종료 완료");
    return 0;
}
