#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "IpcReceiver.h"
#include "SessionRegistry.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"

#include <Ws2tcpip.h>
#include <vector>
#include <cstring>

#pragma comment(lib, "ws2_32.lib")

IpcReceiver::IpcReceiver(SessionRegistry* r, int port)
    : registry(r), listenPort(port), listenSock(INVALID_SOCKET), running(false)
{
}

IpcReceiver::~IpcReceiver() { Stop(); }

bool IpcReceiver::Start()
{
    listenSock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listenSock == INVALID_SOCKET)
    {
        Log::Error("IPC listen 소켓 생성 실패: %d", WSAGetLastError());
        return false;
    }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = inet_addr("127.0.0.1");      // 로컬만
    addr.sin_port = htons((u_short)listenPort);

    if (bind(listenSock, (sockaddr*)&addr, sizeof(addr)) != 0)
    {
        Log::Error("IPC bind 실패 port=%d err=%d", listenPort, WSAGetLastError());
        return false;
    }
    if (listen(listenSock, 16) != 0)
    {
        Log::Error("IPC listen 실패: %d", WSAGetLastError());
        return false;
    }

    running = true;
    worker = std::thread([this] { RunLoop(); });
    Log::Info("IPC 수신 시작: port=%d", listenPort);
    return true;
}

void IpcReceiver::Stop()
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

void IpcReceiver::RunLoop()
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

void IpcReceiver::HandleOneClient(SOCKET cs)
{
    char buf[2048];
    int n = Net::RecvPacket(cs, buf, sizeof(buf));
    if (n <= 0)
    {
        Log::Warn("IPC 패킷 수신 실패");
        return;
    }

    PacketHeader* h = reinterpret_cast<PacketHeader*>(buf);
    if (h->type != PacketType::IPC_CREATE_SESSION)
    {
        Log::Warn("알 수 없는 IPC 타입: %d", (int)h->type);
        return;
    }
    if (h->size < (int)sizeof(IpcCreateSession))
    {
        Log::Warn("IPC_CREATE_SESSION 크기 부족: %d", h->size);
        return;
    }

    IpcCreateSession* req = reinterpret_cast<IpcCreateSession*>(buf + sizeof(*h));

    std::vector<int> playerIds;
    std::vector<int> playerTeams;
    playerIds.reserve(req->playerCount);
    playerTeams.reserve(req->playerCount);
    for (int i = 0; i < req->playerCount && i < MAX_SESSION_PLAYERS; ++i)
    {
        playerIds.push_back(req->playerIds[i]);
        playerTeams.push_back(req->playerTeams[i]);
    }

    bool ok = registry->CreateSession(req->sessionId, req->hostClientId,
        req->mapSeed, playerIds, playerTeams);
    if (!ok)
    {
        Log::Warn("세션 생성 실패: id=%d", req->sessionId);
        // 응답 안 보내거나 실패 표시. 본 단계에선 단순히 응답 안 보냄.
        return;
    }

    Log::Info("IPC: 세션 %d 생성 (host=%d seed=%d 인원=%d)",
        req->sessionId, req->hostClientId, req->mapSeed, req->playerCount);

    // 성공 echo (메인 서버가 이걸 보고 SESSION_ASSIGN 송신)
    Net::SendPacket(cs, (int)PacketType::IPC_CREATE_SESSION, req, sizeof(*req));
}