#pragma once
#include "../Common/PacketProtocol.h"
#include "../Common/MpscStack.h"

#include <atomic>
#include <map>
#include <string>
#include <thread>
#include <vector>

// ============================================================================
//  LobbyReporter
//
//  세션 매니저 → 로비 서버(Retry_Server) 역방향 IPC 송신.
//  SessionDispatcher(로비 → 세션)의 반대 방향이며 구조도 대칭이다.
//
//  용도: 세션 종료 보고(전적), 플레이어 정산 보고(상점·보관함)
//
//  설계 메모:
//   - 보고는 Lock-free 작업 큐(MpscStack)에 넣기만 하고 바로 돌아간다.
//     전용 송신 스레드 하나가 넣은 순서대로 보낸다.
//     → 세션 틱 스레드가 TCP 연결을 기다리지 않는다
//     → 정산 보고가 항상 종료 보고보다 먼저 로비에 도착한다
//   - 빈도가 낮으므로 메시지마다 새 TCP 연결. 풀링 불필요.
//   - 로비가 죽어 있어도 세션은 계속 진행. 연결만 몇 번 재시도하고 버린다.
// ============================================================================

class LobbyReporter
{
public:
    LobbyReporter(const std::string& lobbyIp, int lobbyEventPort);
    ~LobbyReporter();   // 남은 보고를 다 보내고 끝낸다

    // 세션 종료 보고 (플레이어별 전적 포함)
    void ReportSessionEnded(int sessionId, int reason,
        int totalPlayers, int survivors,
        const std::vector<IpcPlayerResult>& results);

    // 플레이어 1명 정산. 탈출: items 입고 / 사망: 빈 items / refund: 가져간 장비 반환
    void ReportEconomy(int sessionId, const std::string& runToken, int clientId,
        bool refund, const std::map<int, int>& items);

private:
    struct Msg { int type = 0; std::vector<char> body; Msg* next = nullptr; };

    std::string        ip;
    int                port;
    MpscStack<Msg>     queue;
    void*              wake = nullptr;      // 자동 리셋 이벤트 (HANDLE)
    std::atomic<bool>  running{ true };
    std::thread        sender;

    void Enqueue(int type, const void* body, int size);
    void SendLoop();
    bool SendNow(const Msg& m);
};
