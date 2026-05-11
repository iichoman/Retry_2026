#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <atomic>
#include <string>

// ============================================================================
//  ClientConnection
//
//  로비에 연결된 클라이언트 1명을 표현.
//  - TCP 소켓 + 비동기 수신용 OverlappedEx 멤버 보유
//  - 수신 데이터를 어셈블리 버퍼에 누적해 패킷 단위로 자른 후
//    LobbyManager로 디스패치
//  - 송신은 매번 새 OverlappedEx를 heap 할당해 WSASend
//
//  전방 선언으로 LobbyManager 참조 (헤더 의존성 분리)
// ============================================================================

class LobbyManager;

class ClientConnection
{
public:
    enum State : int {
        ST_CONNECTED      = 0,    // TCP 연결됨, 인증 전
        ST_AUTHENTICATED  = 1,    // LOGIN 완료, clientId 받음
        ST_IN_ROOM        = 2,    // 방 입장 중
        ST_STARTING       = 3,    // GAME_START 처리 중 (곧 disconnect)
    };

    SOCKET                 socket;
    int                    clientId;        // 인증 전엔 0
    char                   playerName[32];
    std::atomic<State>     state;
    std::atomic_bool       active;
    int                    currentRoomId;   // 0 = 방 없음

    // 수신용 OverlappedEx (멤버로 임베드, heap 할당 안 함)
    struct RecvOverlapped {
        WSAOVERLAPPED  over;
        WSABUF         wsabuf;
        char           buf[4096];
        int            opType;     // OP_RECV
    } recvOver;

    // 패킷 어셈블리 버퍼 (TCP 스트림을 패킷 단위로 자르기 위한 누적)
    char  assembly[8192];
    int   assemblyUsed;

    explicit ClientConnection(SOCKET s);
    ~ClientConnection();

    // IOCP에 다음 비동기 수신 등록.
    // 실패 시 false → 호출자가 disconnect 처리.
    bool PostRecv();

    // 수신 완료 처리: 어셈블리에 데이터 추가 → 완성 패킷이면 lobby로 디스패치.
    // 실패 시 false → 연결 끊기.
    bool OnRecvCompleted(int bytes, LobbyManager* lobby);

    // 헤더 + 본문 합쳐 비동기 송신. 매번 새 OverlappedEx 생성.
    // 실패해도 false 반환 안 함 (워커가 송신 완료 시 알아서 처리).
    void SendPacket(int packetType, const void* body, int bodySize);

    // 소켓 닫고 active = false. 한 번만 실행되도록 atomic 보호.
    void Disconnect();
};

// IOCP overlapped 종류 식별자.
// recv는 ClientConnection 소속, send는 heap 할당, accept는 NetworkAcceptor 소속.
enum OpType : int {
    OP_RECV   = 1,
    OP_SEND   = 2,
    OP_ACCEPT = 3,
};

// 송신용 OverlappedEx (heap 할당). 송신 완료 시 워커가 delete.
struct SendOverlapped {
    WSAOVERLAPPED  over;
    WSABUF         wsabuf;
    int            opType;       // OP_SEND
    int            ownerSlot;    // 통계/디버그용 (clientId)
    // 가변 길이 버퍼는 별도 할당 대신 고정 크기 사용.
    // 더 큰 패킷이 필요해지면 동적 할당으로 변경 가능.
    char           buf[8192];
};
