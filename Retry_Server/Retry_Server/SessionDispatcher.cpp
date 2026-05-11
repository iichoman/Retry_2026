#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "SessionDispatcher.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"

#include <WinSock2.h>
#include <Ws2tcpip.h>
#include <cstring>

SessionDispatcher::SessionDispatcher(const std::string& sessionMgrIp, int sessionMgrPort)
    : ip(sessionMgrIp), port(sessionMgrPort)
{
}

bool SessionDispatcher::RequestSessionCreate(int sessionId,
                                             int hostClientId,
                                             int mapSeed,
                                             const std::vector<int>& playerIds)
{
    if ((int)playerIds.size() > MAX_SESSION_PLAYERS)
    {
        Log::Error("세션 인원 초과: %d > %d", (int)playerIds.size(), MAX_SESSION_PLAYERS);
        return false;
    }

    // 1. 세션 매니저 IPC 포트로 TCP 연결
    SOCKET sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock == INVALID_SOCKET)
    {
        Log::Error("IPC 소켓 생성 실패");
        return false;
    }

    sockaddr_in addr;
    if (!Net::MakeAddress(ip.c_str(), (unsigned short)port, addr))
    {
        Log::Error("IPC 주소 생성 실패: %s:%d", ip.c_str(), port);
        closesocket(sock);
        return false;
    }

    if (connect(sock, (sockaddr*)&addr, sizeof(addr)) != 0)
    {
        Log::Error("세션 매니저 연결 실패: %s:%d (err=%d)",
                   ip.c_str(), port, WSAGetLastError());
        closesocket(sock);
        return false;
    }

    // 2. IpcCreateSession 패킷 구성 및 송신
    IpcCreateSession ipc;
    std::memset(&ipc, 0, sizeof(ipc));
    ipc.sessionId    = sessionId;
    ipc.hostClientId = hostClientId;
    ipc.mapSeed      = mapSeed;
    ipc.playerCount  = (int)playerIds.size();
    for (int i = 0; i < ipc.playerCount; ++i)
    {
        ipc.playerIds[i] = playerIds[i];
    }

    bool sendOk = Net::SendPacket(sock,
                                  static_cast<int>(PacketType::IPC_CREATE_SESSION),
                                  &ipc, sizeof(ipc));
    if (!sendOk)
    {
        Log::Error("IPC_CREATE_SESSION 송신 실패");
        closesocket(sock);
        return false;
    }

    // 3. 응답 대기 (세션 매니저가 같은 패킷을 다시 보내거나, 빈 응답)
    char respBuf[1024];
    int n = Net::RecvPacket(sock, respBuf, sizeof(respBuf));
    closesocket(sock);

    if (n <= 0)
    {
        Log::Error("IPC 응답 수신 실패 sessionId=%d", sessionId);
        return false;
    }

    // 응답이 IPC_CREATE_SESSION이고 같은 sessionId면 성공으로 본다.
    PacketHeader* h = reinterpret_cast<PacketHeader*>(respBuf);
    if (h->type != PacketType::IPC_CREATE_SESSION) return false;
    if (h->size < (int)sizeof(IpcCreateSession))   return false;

    IpcCreateSession* echo = reinterpret_cast<IpcCreateSession*>(respBuf + sizeof(*h));
    if (echo->sessionId != sessionId) return false;

    return true;
}
