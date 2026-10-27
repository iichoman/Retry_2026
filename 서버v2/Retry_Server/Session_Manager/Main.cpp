#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"
#include "SessionRegistry.h"
#include "NetworkAcceptor.h"
#include "IpcReceiver.h"
#include "LobbyReporter.h"
#include "V2Config.h"

#include <cstdio>
#include <cstring>
#include <iostream>
#include <string>
#include <windows.h>
#include <mmsystem.h>

#pragma comment(lib, "winmm.lib")

// ============================================================================
//  v2: 기능 스위치 읽기 (실행 파일 옆 v2.ini). 없으면 기본값(전부 v2)으로 새로 만든다.
// ============================================================================
static void LoadV2Config()
{
    char path[MAX_PATH];
    DWORD n = GetModuleFileNameA(nullptr, path, MAX_PATH);
    char* slash = (n > 0) ? strrchr(path, '\\') : nullptr;
    if (slash) strcpy_s(slash + 1, MAX_PATH - (slash + 1 - path), "v2.ini");
    else strcpy_s(path, "v2.ini");

    if (GetFileAttributesA(path) == INVALID_FILE_ATTRIBUTES)
    {
        FILE* f = nullptr;
        if (fopen_s(&f, path, "w") == 0 && f)
        {
            fputs("; v2 기능 스위치. 왼쪽 값이 v1 방식, 오른쪽 값이 v2 방식.\n"
                  "[v2]\n"
                  "sync=queue      ; lock | queue   (세션 동기화: 성긴 락 / Custom 비멈춤 일감 큐)\n"
                  "log=async       ; sync | async   (로그: 전역 락 즉시 출력 / 로그 스레드)\n"
                  "send=batch      ; direct | batch (송신: 패킷마다 / 틱마다 묶어서)\n"
                  "tick=precise    ; sleep | precise (틱: 일 끝나고 50ms / 50ms 마감 기준)\n"
                  "power=high      ; default | high (윈도우 전력 절약 모드 해제)\n", f);
            fclose(f);
        }
    }

    auto read = [&](const char* key, const char* def) {
        static char buf[32];
        GetPrivateProfileStringA("v2", key, def, buf, sizeof(buf), path);
        char* sp = strpbrk(buf, " \t;");     // 값 뒤 주석 제거
        if (sp) *sp = 0;
        return std::string(buf);
    };

    V2::Config& c = V2::Mutable();
    c.queueSync   = read("sync", "queue") != "lock";
    c.asyncLog    = read("log", "async") != "sync";
    c.batchSend   = read("send", "batch") != "direct";
    c.preciseTick = read("tick", "precise") != "sleep";
    c.highPower   = read("power", "high") != "default";
}

// v2: 창을 최소화해도 E코어로 밀리거나 타이머가 느려지지 않게 전력 절약(EcoQoS)을 끈다.
static void DisablePowerThrottling()
{
    PROCESS_POWER_THROTTLING_STATE st;
    std::memset(&st, 0, sizeof(st));
    st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
    st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | 0x4;  // 0x4 = IGNORE_TIMER_RESOLUTION (Win11)
    st.StateMask = 0;                                                  // 0 = 절약 안 함
    if (!SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &st, sizeof(st)))
    {
        st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;     // Win10은 0x4를 모른다
        SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, &st, sizeof(st));
    }
}

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
    LoadV2Config();
    const V2::Config& cfg = V2::Get();
    Log::Init("Session");
    Log::SetAsync(cfg.asyncLog);
    if (cfg.preciseTick) timeBeginPeriod(1);    // 고해상도 타이머가 안 될 때의 대비
    if (cfg.highPower) DisablePowerThrottling();
    Log::Info("v2 설정: sync=%s log=%s send=%s tick=%s power=%s",
        cfg.queueSync ? "queue" : "lock", cfg.asyncLog ? "async" : "sync",
        cfg.batchSend ? "batch" : "direct", cfg.preciseTick ? "precise" : "sleep",
        cfg.highPower ? "high" : "default");

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
