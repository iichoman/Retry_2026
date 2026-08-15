#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "SessionEventReceiver.h"
#include "LobbyManager.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"

#include <Ws2tcpip.h>
#include <cstring>

#pragma comment(lib, "ws2_32.lib")

SessionEventReceiver::SessionEventReceiver(LobbyManager* l, int port)
    : lobby(l), listenPort(port), listenSock(INVALID_SOCKET), running(false)
{
}

SessionEventReceiver::~SessionEventReceiver() { Stop(); }

bool SessionEventReceiver::Start()
{
    listenSock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listenSock == INVALID_SOCKET)
    {
        Log::Error("세션 이벤트 listen 소켓 생성 실패: %d", WSAGetLastError());
        return false;
    }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = inet_addr("127.0.0.1");      // 로컬만
    addr.sin_port = htons((u_short)listenPort);

    if (bind(listenSock, (sockaddr*)&addr, sizeof(addr)) != 0)
    {
        Log::Error("세션 이벤트 bind 실패 port=%d err=%d", listenPort, WSAGetLastError());
        return false;
    }
    if (listen(listenSock, 16) != 0)
    {
        Log::Error("세션 이벤트 listen 실패: %d", WSAGetLastError());
        return false;
    }

    running = true;
    worker = std::thread([this] { RunLoop(); });
    Log::Info("세션 이벤트 수신 시작: port=%d", listenPort);
    return true;
}

void SessionEventReceiver::Stop()
{
    if (!running) return;
    running = false;

    if (listenSock != INVALID_SOCKET)
    {
        closesocket(listenSock);     // accept 깨우기
        listenSock = INVALID_SOCKET;
    }
    if (worker.joinable()) worker.join();
}

void SessionEventReceiver::RunLoop()
{
    while (running)
    {
        sockaddr_in cli;
        int clen = sizeof(cli);
        SOCKET cs = accept(listenSock, (sockaddr*)&cli, &clen);
        if (cs == INVALID_SOCKET)
        {
            if (!running) break;
            continue;
        }
        HandleOneClient(cs);
        closesocket(cs);
    }
}

void SessionEventReceiver::HandleOneClient(SOCKET cs)
{
    char buf[1024];
    int n = Net::RecvPacket(cs, buf, sizeof(buf));
    if (n <= 0)
    {
        Log::Warn("세션 이벤트 수신 실패");
        return;
    }

    PacketHeader* h = reinterpret_cast<PacketHeader*>(buf);
    if (h->type != PacketType::IPC_SESSION_ENDED)
    {
        Log::Warn("알 수 없는 세션 이벤트 타입: %d", (int)h->type);
        return;
    }
    if (h->size < (int)sizeof(IpcSessionEnded))
    {
        Log::Warn("IPC_SESSION_ENDED 크기 부족: %d", h->size);
        return;
    }

    IpcSessionEnded* msg = reinterpret_cast<IpcSessionEnded*>(buf + sizeof(*h));
    if (lobby)
    {
        lobby->OnSessionEnded(msg->sessionId, msg->reason,
            msg->totalPlayers, msg->survivors);
    }
}
