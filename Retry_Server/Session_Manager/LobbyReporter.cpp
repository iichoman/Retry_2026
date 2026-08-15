#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "LobbyReporter.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"

#include <WinSock2.h>
#include <Ws2tcpip.h>
#include <cstring>

LobbyReporter::LobbyReporter(const std::string& lobbyIp, int lobbyEventPort)
    : ip(lobbyIp), port(lobbyEventPort)
{
}

bool LobbyReporter::ReportSessionEnded(int sessionId, int reason,
    int totalPlayers, int survivors)
{
    SOCKET sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock == INVALID_SOCKET)
    {
        Log::Warn("[Reporter] 소켓 생성 실패");
        return false;
    }

    sockaddr_in addr;
    if (!Net::MakeAddress(ip.c_str(), (unsigned short)port, addr))
    {
        Log::Warn("[Reporter] 주소 생성 실패: %s:%d", ip.c_str(), port);
        closesocket(sock);
        return false;
    }

    if (connect(sock, (sockaddr*)&addr, sizeof(addr)) != 0)
    {
        // 로비가 내려가 있어도 세션 종료는 진행되어야 한다. 경고만.
        Log::Warn("[Reporter] 로비 연결 실패 %s:%d (err=%d) — 보고 생략",
            ip.c_str(), port, WSAGetLastError());
        closesocket(sock);
        return false;
    }

    IpcSessionEnded msg;
    std::memset(&msg, 0, sizeof(msg));
    msg.sessionId = sessionId;
    msg.reason = reason;
    msg.totalPlayers = totalPlayers;
    msg.survivors = survivors;

    bool ok = Net::SendPacket(sock,
        static_cast<int>(PacketType::IPC_SESSION_ENDED),
        &msg, sizeof(msg));

    closesocket(sock);

    if (ok)
    {
        Log::Info("[Reporter] 세션 종료 보고: sid=%d reason=%d 생존=%d/%d",
            sessionId, reason, survivors, totalPlayers);
    }
    else
    {
        Log::Warn("[Reporter] IPC_SESSION_ENDED 송신 실패 sid=%d", sessionId);
    }
    return ok;
}
