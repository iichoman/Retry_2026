#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "NetworkAcceptor.h"
#include "ClientConnection.h"
#include "LobbyManager.h"
#include "../Common/Logger.h"

#include <Ws2tcpip.h>
#include <cstring>

#pragma comment(lib, "Mswsock.lib")
#pragma comment(lib, "ws2_32.lib")

NetworkAcceptor::NetworkAcceptor(LobbyManager* l, int port, int wcount)
    : lobby(l)
    , listenPort(port)
    , workerCount(wcount)
    , listenSock(INVALID_SOCKET)
    , hIocp(NULL)
    , running(false)
    , acceptSock(INVALID_SOCKET)
    , acceptExFn(nullptr)
{
    std::memset(&acceptOver, 0, sizeof(acceptOver));
}

NetworkAcceptor::~NetworkAcceptor()
{
    Stop();
}

bool NetworkAcceptor::LoadAcceptEx()
{
    GUID guid = WSAID_ACCEPTEX;
    DWORD bytes = 0;
    int ret = WSAIoctl(listenSock,
                       SIO_GET_EXTENSION_FUNCTION_POINTER,
                       &guid, sizeof(guid),
                       &acceptExFn, sizeof(acceptExFn),
                       &bytes, NULL, NULL);
    return ret == 0;
}

bool NetworkAcceptor::Start()
{
    // listen 소켓 (overlapped 옵션 필수 - AcceptEx 사용)
    listenSock = WSASocketW(AF_INET, SOCK_STREAM, IPPROTO_TCP,
                            NULL, 0, WSA_FLAG_OVERLAPPED);
    if (listenSock == INVALID_SOCKET)
    {
        Log::Error("listen 소켓 생성 실패: %d", WSAGetLastError());
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

    // IOCP 생성 + listen 소켓 등록
    hIocp = CreateIoCompletionPort(INVALID_HANDLE_VALUE, NULL, 0, 0);
    if (!hIocp)
    {
        Log::Error("IOCP 생성 실패");
        return false;
    }
    CreateIoCompletionPort((HANDLE)listenSock, hIocp, 0, 0);

    if (!LoadAcceptEx())
    {
        Log::Error("AcceptEx 함수 포인터 획득 실패");
        return false;
    }

    // 워커 스레드 시작
    running = true;
    workers.reserve(workerCount);
    for (int i = 0; i < workerCount; ++i)
    {
        workers.emplace_back([this] { WorkerLoop(); });
    }

    // 첫 AcceptEx 등록
    PostAccept();

    Log::Info("로비 listen 시작: port=%d workers=%d", listenPort, workerCount);
    return true;
}

void NetworkAcceptor::Stop()
{
    if (!running) return;
    running = false;

    // 워커들 깨우기 (각자 깨어나서 running=false 보고 종료)
    for (size_t i = 0; i < workers.size(); ++i)
    {
        PostQueuedCompletionStatus(hIocp, 0, 0, NULL);
    }

    if (listenSock != INVALID_SOCKET)
    {
        closesocket(listenSock);
        listenSock = INVALID_SOCKET;
    }
    if (acceptSock != INVALID_SOCKET)
    {
        closesocket(acceptSock);
        acceptSock = INVALID_SOCKET;
    }

    for (auto& t : workers) if (t.joinable()) t.join();
    workers.clear();

    if (hIocp) { CloseHandle(hIocp); hIocp = NULL; }
}

void NetworkAcceptor::PostAccept()
{
    acceptSock = WSASocketW(AF_INET, SOCK_STREAM, IPPROTO_TCP,
                            NULL, 0, WSA_FLAG_OVERLAPPED);
    if (acceptSock == INVALID_SOCKET) return;

    std::memset(&acceptOver.over, 0, sizeof(acceptOver.over));
    acceptOver.opType = OP_ACCEPT;

    DWORD bytes = 0;
    BOOL ok = acceptExFn(listenSock, acceptSock,
                         acceptOver.buf, 0,
                         sizeof(sockaddr_in) + 16,
                         sizeof(sockaddr_in) + 16,
                         &bytes, &acceptOver.over);
    if (!ok && WSAGetLastError() != ERROR_IO_PENDING)
    {
        Log::Error("AcceptEx 실패: %d", WSAGetLastError());
        closesocket(acceptSock);
        acceptSock = INVALID_SOCKET;
    }
}

void NetworkAcceptor::WorkerLoop()
{
    while (running)
    {
        DWORD       bytes = 0;
        ULONG_PTR   key   = 0;
        OVERLAPPED* pover = nullptr;

        BOOL ok = GetQueuedCompletionStatus(hIocp, &bytes, &key, &pover, INFINITE);

        if (!running) break;
        if (!pover) continue;     // PostQueuedCompletionStatus(0,0,NULL) 깨움

        // opType 추출 (OverlappedEx는 모두 첫 멤버가 WSAOVERLAPPED, 그 다음 등이 다름 →
        // OP type 식별을 위해 over의 포인터를 ClientConnection의 recvOver와 비교하기는
        // 어려우므로, OverlappedEx마다 opType 필드를 같은 위치에 넣어 분기)
        // → 모든 *Overlapped 구조체에서 opType이 첫 멤버 over 다음에 위치하도록 통일

        // 안전한 방법: 각 Overlapped 구조체의 opType 필드 읽기.
        // RecvOverlapped, SendOverlapped, AcceptOver 모두 [WSAOVERLAPPED][opType...] 구조.
        int* opTypePtr = reinterpret_cast<int*>(
            reinterpret_cast<char*>(pover) + sizeof(WSAOVERLAPPED));
        // ※ RecvOverlapped는 [WSAOVERLAPPED, WSABUF, char[4096], opType] 순이라
        //    이 방식이 안 맞음. 안전을 위해 opType을 WSAOVERLAPPED 바로 다음으로 옮기는 게
        //    원칙이지만, 본 단계에선 ClientConnection의 RecvOverlapped 구조와
        //    SendOverlapped 구조에서 opType을 찾는 별도 분기 필요.

        // 본 단계에서 채택한 분기 방식:
        //   - acceptOver의 주소와 같으면 ACCEPT
        //   - 아니면 OP_RECV/OP_SEND 구분이 필요
        //   - SendOverlapped는 heap 할당이고 opType이 [WSAOVERLAPPED, WSABUF, opType] 순
        //   - RecvOverlapped는 ClientConnection 멤버이고 [WSAOVERLAPPED, WSABUF, buf, opType] 순
        //
        // 단순화를 위해 이렇게 분기:
        //   - pover == &acceptOver.over → ACCEPT
        //   - SendOverlapped 시그니처(opType in known offset) → SEND
        //   - 그 외 → RECV

        if (pover == &acceptOver.over)
        {
            // ── ACCEPT 완료 ──
            if (!ok)
            {
                Log::Warn("AcceptEx 완료 실패: %d", WSAGetLastError());
                closesocket(acceptSock);
                acceptSock = INVALID_SOCKET;
                if (running) PostAccept();
                continue;
            }

            // listen 소켓 컨텍스트 상속
            int lsock = (int)listenSock;
            setsockopt(acceptSock, SOL_SOCKET, SO_UPDATE_ACCEPT_CONTEXT,
                       (char*)&lsock, sizeof(lsock));

            // ClientConnection 생성
            auto conn = std::make_unique<ClientConnection>(acceptSock);

            // IOCP에 클라 소켓 등록 (key = ClientConnection 포인터)
            CreateIoCompletionPort((HANDLE)acceptSock, hIocp,
                                   (ULONG_PTR)conn.get(), 0);

            ClientConnection* connRaw = conn.get();
            lobby->RegisterClient(std::move(conn));

            // 첫 수신 등록
            if (!connRaw->PostRecv())
            {
                connRaw->Disconnect();
                lobby->OnClientDisconnected(connRaw);
            }

            // 다음 AcceptEx
            if (running) PostAccept();
            continue;
        }

        // ── RECV / SEND 완료 ──
        // key는 ClientConnection 포인터
        ClientConnection* conn = reinterpret_cast<ClientConnection*>(key);
        if (!conn) continue;

        // SendOverlapped vs RecvOverlapped 구분:
        //   - pover == &conn->recvOver.over 이면 RECV
        //   - 그 외 → SEND (heap의 SendOverlapped)
        if (pover == &conn->recvOver.over)
        {
            // RECV
            if (!ok || bytes == 0)
            {
                conn->Disconnect();
                lobby->OnClientDisconnected(conn);
                continue;
            }

            if (!conn->OnRecvCompleted((int)bytes, lobby))
            {
                conn->Disconnect();
                lobby->OnClientDisconnected(conn);
                continue;
            }

            if (!conn->PostRecv())
            {
                conn->Disconnect();
                lobby->OnClientDisconnected(conn);
            }
        }
        else
        {
            // SEND (heap 할당된 SendOverlapped)
            SendOverlapped* sov = reinterpret_cast<SendOverlapped*>(
                reinterpret_cast<char*>(pover) - offsetof(SendOverlapped, over));
            // 송신 실패 / 부분 송신 → 종료 (헤더 8KB 이내라 부분 송신 거의 없음)
            if (!ok)
            {
                conn->Disconnect();
                lobby->OnClientDisconnected(conn);
            }
            delete sov;
        }
    }
}
