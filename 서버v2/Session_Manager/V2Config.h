#pragma once

// ============================================================================
//  V2Config - v2 개선 기능 스위치 (실행 폴더의 v2.ini에서 읽는다)
//
//  같은 빌드에서 기능을 하나씩 껐다 켜며 v1 대비 각 개선의 효과를 따로 잰다.
//  모두 v1 쪽으로 두면 v1과 같은 방식으로 동작한다.
//
//  [v2]
//  sync=queue      ; lock   = v1 성긴 락 (세션 recursive_mutex)
//                  ; cqueue = 일감 큐는 그대로, 큐만 성긴 락 (CoarseQueue, 비교용)
//                  ; queue  = Custom 비멈춤 일감 큐 (MpscStack, 세션 락 없음)
//  log=async       ; sync  = v1 (전역 mutex + 콘솔 직접 출력) / async = 로그 스레드
//  send=batch      ; direct = v1 (패킷마다 8KB 할당 + WSASend) / batch = 틱마다 묶어서 한 번
//  tick=precise    ; sleep = v1 (일 끝나고 50ms 대기) / precise = 50ms 마감 기준 + 고해상도 타이머
//  power=high      ; default = 윈도우 기본 / high = 전력 절약 모드(EcoQoS) 해제
//  nodelay=on      ; off = v1 (윈도우 기본 Nagle) / on = TCP_NODELAY (작은 패킷을 ACK 안 기다리고 바로 보냄)
// ============================================================================

namespace V2
{
    struct Config
    {
        bool queueSync   = true;    // false = 성긴 락
        bool coarseQueue = false;   // true = sync=cqueue (일감 큐 자료구조만 락 버전)
        bool asyncLog    = true;
        bool batchSend   = true;
        bool preciseTick = true;
        bool highPower   = true;
        bool noDelay     = true;    // 세션 접속 소켓 TCP_NODELAY
        bool remoteLobby = false;   // [net] lobby_host가 다른 PC (IPC를 모든 주소에서 받음)
    };

    // 시작할 때 Main에서 한 번 채우고, 그 뒤로는 읽기만 한다.
    inline Config& Mutable()
    {
        static Config c;
        return c;
    }

    inline const Config& Get() { return Mutable(); }
}
