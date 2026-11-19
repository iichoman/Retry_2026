#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "NetworkAcceptor.h"
#include "SessionClientConnection.h"
#include "SessionRegistry.h"
#include "GameSession.h"
#include "../Common/Logger.h"

#include <Ws2tcpip.h>
#include <cstring>

#pragma comment(lib, "Mswsock.lib")
#pragma comment(lib, "ws2_32.lib")

NetworkAcceptor::NetworkAcceptor(SessionRegistry* r, int port, int wcount)
    : registry(r), listenPort(port), workerCount(wcount)
    , listenSock(INVALID_SOCKET), hIocp(NULL), running(false)
    , acceptSock(INVALID_SOCKET), acceptExFn(nullptr)
{
    std::memset(&acceptOver, 0, sizeof(acceptOver));
}

NetworkAcceptor::~NetworkAcceptor() { Stop(); }

bool NetworkAcceptor::LoadAcceptEx()
{
    GUID guid = WSAID_ACCEPTEX;
    DWORD bytes = 0;
    int ret = WSAIoctl(listenSock, SIO_GET_EXTENSION_FUNCTION_POINTER,
                       &guid, sizeof(guid), &acceptExFn, sizeof(acceptExFn),
                       &bytes, NULL, NULL);
    return ret == 0;
}

bool NetworkAcceptor::Start()
{
    listenSock = WSASocketW(AF_INET, SOCK_STREAM, IPPROTO_TCP, NULL, 0, WSA_FLAG_OVERLAPPED);
    if (listenSock == INVALID_SOCKET)
    {
        Log::Error("게임 listen 소켓 생성 실패: %d", WSAGetLastError());
        return false;
    }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family      = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port        = htons((u_short)listenPort);
    if (bind(listenSock, (sockaddr*)&addr, sizeof(addr)) != 0)
    {
        Log::Error("bind 실패 port=%d err=%d", listenPort, WSAGetLastError());
        return false;
    }
    if (listen(listenSock, SOMAXCONN) != 0)
    {
        Log::Error("listen 실패: %d", WSAGetLastError());
        return false;
    }

    hIocp = CreateIoCompletionPort(INVALID_HANDLE_VALUE, NULL, 0, 0);
    if (!hIocp) { Log::Error("IOCP 생성 실패"); return false; }
    CreateIoCompletionPort((HANDLE)listenSock, hIocp, 0, 0);

    if (!LoadAcceptEx()) { Log::Error("AcceptEx 획득 실패"); return false; }

    running = true;
    workers.reserve(workerCount);
    for (int i = 0; i < workerCount; ++i)
        workers.emplace_back([this] { WorkerLoop(); });

    PostAccept();
    Log::Info("게임 listen 시작: port=%d workers=%d", listenPort, workerCount);
    return true;
}

void NetworkAcceptor::Stop()
{
    if (!running) return;
    running = false;
    for (size_t i = 0; i < workers.size(); ++i)
        PostQueuedCompletionStatus(hIocp, 0, 0, NULL);

    if (listenSock != INVALID_SOCKET) { closesocket(listenSock); listenSock = INVALID_SOCKET; }
    if (acceptSock != INVALID_SOCKET) { closesocket(acceptSock); acceptSock = INVALID_SOCKET; }

    for (auto& t : workers) if (t.joinable()) t.join();
    workers.clear();
    if (hIocp) { CloseHandle(hIocp); hIocp = NULL; }
}

void NetworkAcceptor::PostAccept()
{
    acceptSock = WSASocketW(AF_INET, SOCK_STREAM, IPPROTO_TCP, NULL, 0, WSA_FLAG_OVERLAPPED);
    if (acceptSock == INVALID_SOCKET) return;

    std::memset(&acceptOver.over, 0, sizeof(acceptOver.over));
    acceptOver.opType = OP_ACCEPT;

    DWORD bytes = 0;
    BOOL ok = acceptExFn(listenSock, acceptSock, acceptOver.buf, 0,
                         sizeof(sockaddr_in) + 16, sizeof(sockaddr_in) + 16,
                         &bytes, &acceptOver.over);
    if (!ok && WSAGetLastError() != ERROR_IO_PENDING)
    {
        closesocket(acceptSock);
        acceptSock = INVALID_SOCKET;
    }
}

void NetworkAcceptor::WorkerLoop()
{
    while (running)
    {
        DWORD bytes = 0;
        ULONG_PTR key = 0;
        OVERLAPPED* pover = nullptr;
        BOOL ok = GetQueuedCompletionStatus(hIocp, &bytes, &key, &pover, INFINITE);

        if (!running) break;
        if (!pover) continue;

        // ── ACCEPT 완료 ──
        if (pover == &acceptOver.over)
        {
            if (!ok)
            {
                if (acceptSock != INVALID_SOCKET) closesocket(acceptSock);
                acceptSock = INVALID_SOCKET;
                if (running) PostAccept();
                continue;
            }
            int lsock = (int)listenSock;
            setsockopt(acceptSock, SOL_SOCKET, SO_UPDATE_ACCEPT_CONTEXT,
                       (char*)&lsock, sizeof(lsock));

            auto* conn = new SessionClientConnection(acceptSock);
            CreateIoCompletionPort((HANDLE)acceptSock, hIocp, (ULONG_PTR)conn, 0);

            if (!conn->PostRecv())
            {
                conn->Disconnect();
                delete conn;
            }
            // 인증 후 GameSession이 conn을 약한 참조로 보관함.
            // 객체 자체는 자기 소켓이 끊길 때까지 살아 있어야 함 → leak처럼 보이지만
            // 종료 시 일괄 정리 필요. 본 단계에선 셧다운 시 정리 안 함 (단순화).
            // 5단계에서 SessionRegistry가 disconnected pool 관리하도록 개선 가능.

            if (running) PostAccept();
            continue;
        }

        // ── RECV / SEND 완료 ──
        auto* conn = reinterpret_cast<SessionClientConnection*>(key);
        if (!conn) continue;

        if (pover == &conn->recvOver.over)
        {
            // RECV
            if (!ok || bytes == 0)
            {
                if (conn->session) conn->session->DetachClient(conn->clientId);
                conn->Disconnect();
                continue;
            }
            if (!conn->OnRecvCompleted((int)bytes, registry))
            {
                if (conn->session) conn->session->DetachClient(conn->clientId);
                conn->Disconnect();
                continue;
            }
            if (!conn->PostRecv())
            {
                if (conn->session) conn->session->DetachClient(conn->clientId);
                conn->Disconnect();
            }
        }
        else
        {
            // SEND (heap SendOverlapped)
            SendOverlapped* sov = reinterpret_cast<SendOverlapped*>(
                reinterpret_cast<char*>(pover) - offsetof(SendOverlapped, over));
            if (!ok)
            {
                if (conn->session) conn->session->DetachClient(conn->clientId);
                conn->Disconnect();
            }
            delete sov;
        }
    }
}
