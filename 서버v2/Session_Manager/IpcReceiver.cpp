#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "IpcReceiver.h"
#include "SessionRegistry.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"
#include "V2Config.h"

#include <Ws2tcpip.h>
#include <filesystem>
#include <string>
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
    addr.sin_addr.s_addr = V2::Get().remoteLobby ? INADDR_ANY : inet_addr("127.0.0.1");   // 기본은 로컬만
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
    char buf[sizeof(PacketHeader) + sizeof(IpcCreateSessionWithEconomy)];
    int n = Net::RecvPacket(cs, buf, sizeof(buf));
    if (n <= 0)
    {
        Log::Warn("IPC 패킷 수신 실패");
        return;
    }

    PacketHeader* h = reinterpret_cast<PacketHeader*>(buf);
    const bool hasEconomy = h->type == PacketType::IPC_CREATE_SESSION_WITH_ECONOMY;
    if (!hasEconomy && h->type != PacketType::IPC_CREATE_SESSION)
    {
        Log::Warn("알 수 없는 IPC 타입: %d", (int)h->type);
        return;
    }
    const auto expectedSize = hasEconomy ? sizeof(IpcCreateSessionWithEconomy) : sizeof(IpcCreateSession);
    if (h->size != static_cast<int>(expectedSize))
    {
        Log::Warn("IPC_CREATE_SESSION 잘못된 크기: %d", h->size);
        return;
    }

    IpcCreateSession* req = reinterpret_cast<IpcCreateSession*>(buf + sizeof(*h));
    if (req->playerCount < 1 || req->playerCount > MAX_SESSION_PLAYERS) return;

    // 출격 장비 (로비가 보관함에서 예약). 정산 보고 때 runToken을 그대로 돌려준다.
    std::string runToken;
    std::vector<Economy::Grant> grants;
    if (hasEconomy)
    {
        const auto* request = reinterpret_cast<const IpcCreateSessionWithEconomy*>(req);
        char token[sizeof(request->runToken)];
        std::memcpy(token, request->runToken, sizeof(token));
        token[sizeof(token) - 1] = '\0';
        runToken = token;
        if (runToken.empty()) return;
        grants.resize(req->playerCount);
        for (int i = 0; i < req->playerCount; ++i)
        {
            const IpcLoadout& loadout = request->loadouts[i];
            grants[i].weapon = loadout.weapon;
            grants[i].item = loadout.item;
            grants[i].count = loadout.count;
            grants[i].freeWeapon = loadout.freeWeapon != 0;
            grants[i].freeItem = loadout.freeItem != 0;
        }
    }

    std::vector<int> playerIds,dbPlayerIds;
    std::vector<int> playerTeams;
    std::vector<std::string> playerNames;
    playerIds.reserve(req->playerCount);
    playerTeams.reserve(req->playerCount);
    playerNames.reserve(req->playerCount);
    for (int i = 0; i < req->playerCount; ++i)
    {
        playerIds.push_back(req->playerIds[i]);dbPlayerIds.push_back(req->dbPlayerIds[i]);
        playerTeams.push_back(req->playerTeams[i]);

        // 널 종료 보장 후 복사 (로비가 보낸 값을 그대로 믿지 않는다)
        char nameBuf[MAX_PLAYER_NAME];
        std::memcpy(nameBuf, req->playerNames[i], MAX_PLAYER_NAME);
        nameBuf[MAX_PLAYER_NAME - 1] = '\0';
        playerNames.push_back(nameBuf);
    }

    bool ok = registry->CreateSession(req->sessionId, req->hostClientId,
        req->mapSeed, playerIds, playerTeams, playerNames, dbPlayerIds, runToken, grants);
    if (!ok)
    {
        Log::Warn("세션 생성 실패: id=%d", req->sessionId);
        // 응답 안 보내거나 실패 표시. 본 단계에선 단순히 응답 안 보냄.
        return;
    }

    Log::Info("IPC: 세션 %d 생성 (host=%d seed=%d 인원=%d)",
        req->sessionId, req->hostClientId, req->mapSeed, req->playerCount);

    // 성공 echo (메인 서버가 이걸 보고 SESSION_ASSIGN 송신)
    Net::SendPacket(cs, static_cast<int>(h->type), req, sizeof(*req));
}
