#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "NetworkPrimitives.h"
#include "PacketProtocol.h"

#include <WinSock2.h>
#include <Ws2tcpip.h>
#include <cstring>

#pragma comment(lib, "ws2_32.lib")

namespace Net {

bool RecvAll(SOCKET sock, char* buf, int length)
{
    int total = 0;
    while (total < length)
    {
        int n = recv(sock, buf + total, length - total, 0);
        if (n <= 0) return false; // 0=상대 정상 종료, -1=오류
        total += n;
    }
    return true;
}

bool SendAll(SOCKET sock, const char* buf, int length)
{
    int total = 0;
    while (total < length)
    {
        int n = send(sock, buf + total, length - total, 0);
        if (n <= 0) return false;
        total += n;
    }
    return true;
}

bool SendPacket(SOCKET sock, int packetType,
                const void* body, int bodySize)
{
    PacketHeader h;
    h.type = static_cast<PacketType>(packetType);
    h.size = bodySize;

    // 한 버퍼에 합쳐 한 번에 보냄. 헤더만 따로 보내면 Nagle/TCP 분할로
    // 상대방이 본문을 늦게 받는 경우가 생길 수 있음.
    constexpr int MAX_INLINE = 8192;
    char buf[MAX_INLINE];

    int total = sizeof(h) + bodySize;
    if (total > MAX_INLINE) return false; // 큰 패킷은 호출자가 분할 책임

    std::memcpy(buf, &h, sizeof(h));
    if (body && bodySize > 0)
    {
        std::memcpy(buf + sizeof(h), body, bodySize);
    }

    return SendAll(sock, buf, total);
}

int RecvPacket(SOCKET sock, char* outBuf, int outBufLen)
{
    if (outBufLen < (int)sizeof(PacketHeader)) return 0;

    // 1. 헤더 수신
    if (!RecvAll(sock, outBuf, sizeof(PacketHeader))) return 0;

    PacketHeader* h = reinterpret_cast<PacketHeader*>(outBuf);

    // 2. 본문 크기 sanity check
    if (h->size < 0) return 0;
    if (h->size > outBufLen - (int)sizeof(PacketHeader)) return 0;

    // 3. 본문 수신
    if (h->size > 0)
    {
        if (!RecvAll(sock, outBuf + sizeof(PacketHeader), h->size)) return 0;
    }

    return sizeof(PacketHeader) + h->size;
}

bool MakeAddress(const char* ip, unsigned short port, sockaddr_in& outAddr)
{
    std::memset(&outAddr, 0, sizeof(outAddr));
    outAddr.sin_family = AF_INET;
    outAddr.sin_port   = htons(port);
    outAddr.sin_addr.s_addr = inet_addr(ip);
    if (outAddr.sin_addr.s_addr == INADDR_NONE) return false;
    return true;
}

bool StartupWinsock()
{
    WSADATA wsa;
    int err = WSAStartup(MAKEWORD(2, 2), &wsa);
    return err == 0;
}

void CleanupWinsock()
{
    WSACleanup();
}

} // namespace Net
