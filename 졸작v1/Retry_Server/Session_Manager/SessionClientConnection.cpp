#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "SessionClientConnection.h"
#include "SessionRegistry.h"
#include "GameSession.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <cstring>

SessionClientConnection::SessionClientConnection(SOCKET s)
    : socket(s)
    , clientId(0)
    , sessionId(0)
    , state(ST_AWAIT_AUTH)
    , active(true)
    , session(nullptr)
    , assemblyUsed(0)
{
    std::memset(&recvOver, 0, sizeof(recvOver));
}

SessionClientConnection::~SessionClientConnection()
{
    if (socket != INVALID_SOCKET)
    {
        closesocket(socket);
        socket = INVALID_SOCKET;
    }
}

bool SessionClientConnection::PostRecv()
{
    if (!active) return false;
    if (socket == INVALID_SOCKET) return false;

    std::memset(&recvOver.over, 0, sizeof(recvOver.over));
    recvOver.wsabuf.buf = recvOver.buf;
    recvOver.wsabuf.len = sizeof(recvOver.buf);
    recvOver.opType     = OP_RECV;

    DWORD flags = 0;
    int ret = WSARecv(socket, &recvOver.wsabuf, 1, NULL, &flags,
                      &recvOver.over, NULL);
    if (ret == SOCKET_ERROR && WSAGetLastError() != WSA_IO_PENDING) return false;
    return true;
}

bool SessionClientConnection::OnRecvCompleted(int bytes, SessionRegistry* registry)
{
    if (assemblyUsed + bytes > (int)sizeof(assembly))
    {
        Log::Error("어셈블리 오버플로 (used=%d +%d)", assemblyUsed, bytes);
        return false;
    }
    std::memcpy(assembly + assemblyUsed, recvOver.buf, bytes);
    assemblyUsed += bytes;

    // ── 인증 단계: 첫 8바이트 (sessionId 4 + clientId 4) ──
    if (state == ST_AWAIT_AUTH)
    {
        if (assemblyUsed < 8) return true;        // 더 받아야 함

        int sid, cid;
        std::memcpy(&sid, assembly,     4);
        std::memcpy(&cid, assembly + 4, 4);

        // 8바이트 소비
        std::memmove(assembly, assembly + 8, assemblyUsed - 8);
        assemblyUsed -= 8;

        GameSession* sess = registry->AuthClient(sid, cid);
        if (!sess)
        {
            Log::Warn("세션 인증 실패: sessionId=%d clientId=%d", sid, cid);
            return false;
        }

        sessionId = sid;
        clientId  = cid;
        session   = sess;
        state     = ST_AUTHENTICATED;

        sess->AttachClient(cid, this);
        Log::Info("세션 인증 성공: sessionId=%d clientId=%d", sid, cid);
        // 다음 if 블록으로 흘러가서 남은 어셈블리 데이터(있다면) 처리
    }

    // ── 인증 후: 표준 패킷 형식 처리 ──
    if (state == ST_AUTHENTICATED)
    {
        while (assemblyUsed >= (int)sizeof(PacketHeader))
        {
            PacketHeader* h = reinterpret_cast<PacketHeader*>(assembly);
            if (h->size < 0 || h->size > (int)sizeof(assembly) - (int)sizeof(PacketHeader))
            {
                Log::Error("비정상 패킷 크기 type=%d size=%d", (int)h->type, h->size);
                return false;
            }
            int total = sizeof(PacketHeader) + h->size;
            if (assemblyUsed < total) break;

            if (session)
            {
                session->HandlePacket(clientId,
                                      static_cast<int>(h->type),
                                      assembly + sizeof(PacketHeader),
                                      h->size);
            }

            std::memmove(assembly, assembly + total, assemblyUsed - total);
            assemblyUsed -= total;
        }
    }

    return true;
}

void SessionClientConnection::SendPacket(int packetType, const void* body, int bodySize)
{
    if (!active) return;
    if (socket == INVALID_SOCKET) return;

    int total = (int)sizeof(PacketHeader) + bodySize;
    if (total > (int)sizeof(SendOverlapped::buf))
    {
        Log::Error("송신 버퍼 초과 type=%d size=%d", packetType, bodySize);
        return;
    }

    SendOverlapped* sov = new SendOverlapped();
    std::memset(&sov->over, 0, sizeof(sov->over));
    sov->opType        = OP_SEND;
    sov->ownerClientId = clientId;

    PacketHeader h;
    h.type = static_cast<PacketType>(packetType);
    h.size = bodySize;
    std::memcpy(sov->buf, &h, sizeof(h));
    if (body && bodySize > 0)
    {
        std::memcpy(sov->buf + sizeof(h), body, bodySize);
    }

    sov->wsabuf.buf = sov->buf;
    sov->wsabuf.len = total;

    int ret = WSASend(socket, &sov->wsabuf, 1, NULL, 0, &sov->over, NULL);
    if (ret == SOCKET_ERROR && WSAGetLastError() != WSA_IO_PENDING)
    {
        delete sov;
        Disconnect();
    }
}

void SessionClientConnection::Disconnect()
{
    bool expected = true;
    if (!active.compare_exchange_strong(expected, false)) return;

    if (socket != INVALID_SOCKET)
    {
        closesocket(socket);
        socket = INVALID_SOCKET;
    }
}
