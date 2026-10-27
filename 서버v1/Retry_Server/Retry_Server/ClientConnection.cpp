#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include "ClientConnection.h"
#include "LobbyManager.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <cstring>

ClientConnection::ClientConnection(SOCKET s)
    : socket(s)
    , clientId(0)
    , state(ST_CONNECTED)
    , active(true)
    , currentRoomId(0)
    , assemblyUsed(0)
{
    playerName[0] = '\0';
    std::memset(&recvOver, 0, sizeof(recvOver));
}

ClientConnection::~ClientConnection()
{
    if (socket != INVALID_SOCKET)
    {
        closesocket(socket);
        socket = INVALID_SOCKET;
    }
}

bool ClientConnection::PostRecv()
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
    if (ret == SOCKET_ERROR)
    {
        int err = WSAGetLastError();
        if (err != WSA_IO_PENDING) return false;
    }
    return true;
}

bool ClientConnection::OnRecvCompleted(int bytes, LobbyManager* lobby)
{
    // 어셈블리 버퍼 오버플로 방지
    if (assemblyUsed + bytes > (int)sizeof(assembly))
    {
        Log::Error("client %d: 어셈블리 버퍼 오버플로 (used=%d, +%d)", 
                   clientId, assemblyUsed, bytes);
        return false;
    }

    std::memcpy(assembly + assemblyUsed, recvOver.buf, bytes);
    assemblyUsed += bytes;

    // 가능한 만큼 패킷 추출
    while (assemblyUsed >= (int)sizeof(PacketHeader))
    {
        PacketHeader* h = reinterpret_cast<PacketHeader*>(assembly);

        // sanity check
        if (h->size < 0 || h->size > (int)sizeof(assembly) - (int)sizeof(PacketHeader))
        {
            Log::Error("client %d: 비정상 패킷 크기 type=%d size=%d", 
                       clientId, (int)h->type, h->size);
            return false;
        }

        int total = sizeof(PacketHeader) + h->size;
        if (assemblyUsed < total) break;     // 본문이 아직 다 안 옴

        // 디스패치: lobby에 패킷 처리 요청
        lobby->HandlePacket(this,
                            static_cast<int>(h->type),
                            assembly + sizeof(PacketHeader),
                            h->size);

        // 처리한 만큼 앞으로 당기기
        std::memmove(assembly, assembly + total, assemblyUsed - total);
        assemblyUsed -= total;
    }

    return true;
}

void ClientConnection::SendPacket(int packetType, const void* body, int bodySize)
{
    if (!active) return;
    if (socket == INVALID_SOCKET) return;

    int total = (int)sizeof(PacketHeader) + bodySize;
    if (total > (int)sizeof(SendOverlapped::buf))
    {
        Log::Error("client %d: 송신 버퍼 초과 type=%d size=%d",
                   clientId, packetType, bodySize);
        return;
    }

    SendOverlapped* sov = new SendOverlapped();
    std::memset(&sov->over, 0, sizeof(sov->over));
    sov->opType    = OP_SEND;
    sov->ownerSlot = clientId;

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
    if (ret == SOCKET_ERROR)
    {
        int err = WSAGetLastError();
        if (err != WSA_IO_PENDING)
        {
            // 즉시 실패: heap 정리하고 종료 처리
            delete sov;
            Disconnect();
        }
    }
}

void ClientConnection::Disconnect()
{
    bool expected = true;
    if (!active.compare_exchange_strong(expected, false)) return;

    if (socket != INVALID_SOCKET)
    {
        closesocket(socket);
        socket = INVALID_SOCKET;
    }
}
