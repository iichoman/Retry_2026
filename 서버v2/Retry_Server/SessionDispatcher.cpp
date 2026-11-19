#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "SessionDispatcher.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"

#include <WinSock2.h>
#include <Ws2tcpip.h>
#include <cstring>
#include "../Common/EconomyStore.h"

SessionDispatcher::SessionDispatcher(const std::string& sessionMgrIp, int sessionMgrPort)
    : ip(sessionMgrIp), port(sessionMgrPort)
{
}

bool SessionDispatcher::RequestSessionCreate(int sessionId,
    int hostClientId,
    int mapSeed,
    const std::vector<int>& playerIds,
    const std::vector<int>& playerTeams,
    const std::vector<std::string>& playerNames, const std::vector<int>& dbPlayerIds,
    const std::string& runToken, const std::vector<Economy::Grant>& grants)
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

    // 2. 세션 생성 패킷 구성 및 송신.
    //    출격 장비(로비가 보관함에서 예약)를 함께 싣는다.
    //    세션 서버는 저장소를 모르므로 다른 PC에 있어도 그대로 동작한다.
    IpcCreateSessionWithEconomy request{};
    auto& ipc = request.session;
    ipc.sessionId = sessionId;
    ipc.hostClientId = hostClientId;
    ipc.mapSeed = mapSeed;
    ipc.playerCount = (int)playerIds.size();
    for (int i = 0; i < ipc.playerCount; ++i)
    {
        ipc.playerIds[i] = playerIds[i];
        ipc.dbPlayerIds[i] = i<(int)dbPlayerIds.size()?dbPlayerIds[i]:0;
        ipc.playerTeams[i] = (i < (int)playerTeams.size()) ? playerTeams[i] : 0;
        // 채팅 표시용 이름. 세션 매니저는 로그인 정보를 모른다.
        if (i < (int)playerNames.size())
            std::strncpy(ipc.playerNames[i], playerNames[i].c_str(), MAX_PLAYER_NAME - 1);

        if (i < (int)grants.size())
        {
            IpcLoadout& loadout = request.loadouts[i];
            loadout.weapon = grants[i].weapon;
            loadout.item = grants[i].item;
            loadout.count = grants[i].count;
            loadout.freeWeapon = grants[i].freeWeapon ? 1 : 0;
            loadout.freeItem = grants[i].freeItem ? 1 : 0;
        }
    }
    std::strncpy(request.runToken, runToken.c_str(), sizeof(request.runToken) - 1);

    const PacketType requestType = PacketType::IPC_CREATE_SESSION_WITH_ECONOMY;
    if (!Net::SendPacket(sock, static_cast<int>(requestType), &request, sizeof(request)))
    {
        Log::Error("IPC_CREATE_SESSION 송신 실패");
        closesocket(sock);
        return false;
    }

    // 3. 세션 생성 확인. 응답에는 세션 정보만 포함된다.
    char respBuf[sizeof(PacketHeader) + sizeof(IpcCreateSession)];
    int n = Net::RecvPacket(sock, respBuf, sizeof(respBuf));
    closesocket(sock);

    if (n <= 0)
    {
        Log::Error("IPC 응답 수신 실패 sessionId=%d", sessionId);
        return false;
    }

    // 요청과 같은 타입 및 sessionId를 확인한다.
    PacketHeader* h = reinterpret_cast<PacketHeader*>(respBuf);
    if (h->type != requestType) return false;
    if (h->size != (int)sizeof(IpcCreateSession)) return false;

    const auto* echo = reinterpret_cast<const IpcCreateSession*>(respBuf + sizeof(*h));
    return echo->sessionId == sessionId;
}
