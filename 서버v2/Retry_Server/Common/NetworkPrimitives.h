#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>

// ============================================================================
//  네트워크 기본 헬퍼
//
//  동기 블로킹 방식의 TCP 송수신 함수들. 단순한 흐름(IPC, 인증 등)에서 사용.
//  IOCP 비동기 처리는 NetworkAcceptor에서 별도로 다룬다.
//
//  소켓 옵션 설정 등은 호출하는 쪽에서 책임진다.
// ============================================================================

namespace Net {

    // 정확히 length 바이트가 도착할 때까지 블로킹 수신.
    // 부분 수신(TCP 특성)을 끝까지 시도. 연결 끊김/오류 시 false.
    bool RecvAll(SOCKET sock, char* buf, int length);

    // 정확히 length 바이트 송신. 부분 송신을 끝까지 시도.
    bool SendAll(SOCKET sock, const char* buf, int length);

    // 헤더 + 본문을 한 버퍼에 합쳐 한 번의 SendAll로 송신.
    // packetType은 PacketType 값 (int로 캐스팅됨).
    bool SendPacket(SOCKET sock, int packetType,
                    const void* body, int bodySize);

    // 패킷 1개 수신: 먼저 헤더(8바이트), 그 다음 본문(헤더의 size만큼).
    // outBuf에 [헤더][본문] 통째로 쓴다.
    // 반환: 받은 전체 바이트 수 (헤더+본문). 실패 시 0.
    int RecvPacket(SOCKET sock, char* outBuf, int outBufLen);

    // 호스트 이름/IP 문자열을 받아 sockaddr_in 채워 반환.
    bool MakeAddress(const char* ip, unsigned short port, sockaddr_in& outAddr);

    // 윈속 초기화 / 정리.
    bool StartupWinsock();
    void CleanupWinsock();

} // namespace Net
