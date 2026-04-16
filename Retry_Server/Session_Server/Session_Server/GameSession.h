#pragma once
#include <winsock2.h>
#include <map>
#include <mutex>
#include "Protocol.h"

class GameSession {
public:
    GameSession(int sessionId, int hostId);
    ~GameSession();

    void SetHost(SOCKET hostSocket);
    void AddGuest(int guestId, SOCKET guestSocket);

    // 방장이 보낸 데이터를 모든 게스트에게 중계
    void RelayFromHost(const char* data, int size);

    // 게스트가 보낸 데이터를 방장에게 전달 (Hybrid P2P 핵심)
    void RelayToHost(const char* data, int size);

    // 보낸 사람을 제외한 세션 내 모든 인원에게 전송
    void Broadcast(SOCKET senderSocket, char* data, int len);

    int GetSessionId() const { return sessionId; }
    int GetHostId() const { return hostId; }

private:
    int sessionId;
    int hostId;

    SOCKET hostSocket = INVALID_SOCKET;
    // 관리 효율을 위해 map 하나만 사용합니다.
    std::map<int, SOCKET> guestPlayers;

    std::mutex sessionMutex;
};