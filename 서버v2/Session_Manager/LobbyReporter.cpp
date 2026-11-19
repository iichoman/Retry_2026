#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "LobbyReporter.h"
#include "../Common/PacketProtocol.h"
#include "../Common/NetworkPrimitives.h"
#include "../Common/Logger.h"

#include <WinSock2.h>
#include <Ws2tcpip.h>
#include <Windows.h>
#include <cstring>

LobbyReporter::LobbyReporter(const std::string& lobbyIp, int lobbyEventPort)
    : ip(lobbyIp), port(lobbyEventPort)
{
    wake = CreateEventW(nullptr, FALSE, FALSE, nullptr);     // 자동 리셋
    sender = std::thread([this] { SendLoop(); });
}

LobbyReporter::~LobbyReporter()
{
    running.store(false);
    if (wake) SetEvent((HANDLE)wake);
    if (sender.joinable()) sender.join();     // 남은 보고는 SendLoop가 다 보내고 끝난다
    if (wake) CloseHandle((HANDLE)wake);
}

void LobbyReporter::Enqueue(int type, const void* body, int size)
{
    Msg* m = new Msg;
    m->type = type;
    m->body.assign(static_cast<const char*>(body), static_cast<const char*>(body) + size);
    // 비어 있던 큐에 처음 넣을 때만 깨운다 (그 뒤 것은 같은 PopAll에 딸려 간다)
    if (queue.Push(m) && wake) SetEvent((HANDLE)wake);
}

void LobbyReporter::SendLoop()
{
    for (;;)
    {
        WaitForSingleObject((HANDLE)wake, 500);
        const bool stopping = !running.load();

        // 넣은 순서대로 보낸다 (정산 → 종료 보고 순서가 로비까지 그대로 간다)
        for (Msg* m = queue.PopAll(); m; )
        {
            Msg* next = m->next;
            SendNow(*m);
            delete m;
            m = next;
        }
        if (stopping) break;
    }
}

bool LobbyReporter::SendNow(const Msg& m)
{
    // 연결 실패만 재시도한다. 보낸 뒤 실패는 다시 보내지 않는다 (전적이 두 번 들어갈 수 있다).
    for (int attempt = 0; attempt < 3; ++attempt)
    {
        if (attempt > 0) Sleep(300);

        SOCKET sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (sock == INVALID_SOCKET) continue;

        sockaddr_in addr;
        if (!Net::MakeAddress(ip.c_str(), (unsigned short)port, addr))
        {
            Log::Warn("[Reporter] 주소 생성 실패: %s:%d", ip.c_str(), port);
            closesocket(sock);
            return false;
        }
        if (connect(sock, (sockaddr*)&addr, sizeof(addr)) != 0)
        {
            closesocket(sock);
            continue;
        }

        bool ok = Net::SendPacket(sock, m.type, m.body.data(), (int)m.body.size());
        closesocket(sock);
        if (!ok) Log::Warn("[Reporter] 로비 보고 송신 실패 type=%d", m.type);
        return ok;
    }
    // 로비가 내려가 있어도 세션은 계속 진행되어야 한다. 경고만.
    Log::Warn("[Reporter] 로비 연결 실패 %s:%d (err=%d) — 보고 생략 type=%d",
        ip.c_str(), port, WSAGetLastError(), m.type);
    return false;
}

void LobbyReporter::ReportSessionEnded(int sessionId, int reason,
    int totalPlayers, int survivors,
    const std::vector<IpcPlayerResult>& results)
{
    IpcSessionEnded msg;
    std::memset(&msg, 0, sizeof(msg));
    msg.sessionId = sessionId;
    msg.reason = reason;
    msg.totalPlayers = totalPlayers;
    msg.survivors = survivors;

    int n = (int)results.size();
    if (n > MAX_SESSION_PLAYERS) n = MAX_SESSION_PLAYERS;
    msg.resultCount = n;
    for (int i = 0; i < n; ++i) msg.results[i] = results[i];

    Enqueue(static_cast<int>(PacketType::IPC_SESSION_ENDED), &msg, sizeof(msg));
    Log::Info("[Reporter] 세션 종료 보고: sid=%d reason=%d 생존=%d/%d",
        sessionId, reason, survivors, totalPlayers);
}

void LobbyReporter::ReportEconomy(int sessionId, const std::string& runToken, int clientId,
    bool refund, const std::map<int, int>& items)
{
    IpcEconomyFinish msg;
    std::memset(&msg, 0, sizeof(msg));
    msg.sessionId = sessionId;
    msg.clientId = clientId;
    std::strncpy(msg.runToken, runToken.c_str(), sizeof(msg.runToken) - 1);
    msg.refund = refund ? 1 : 0;
    for (const auto& entry : items)
    {
        if (msg.itemCount >= IPC_ECONOMY_ITEMS)
        {
            Log::Warn("[Reporter] 정산 아이템 종류가 너무 많아 잘림 cid=%d", clientId);
            break;
        }
        msg.items[msg.itemCount].hash = entry.first;
        msg.items[msg.itemCount].count = entry.second;
        ++msg.itemCount;
    }
    Enqueue(static_cast<int>(PacketType::IPC_ECONOMY_FINISH), &msg, sizeof(msg));
}
