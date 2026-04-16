#include "GameSession.h"
#include <iostream>

GameSession::GameSession(int sessionId, int hostId)
    : sessionId(sessionId), hostId(hostId), hostSocket(INVALID_SOCKET) {
}

GameSession::~GameSession() {
    std::lock_guard<std::mutex> lock(sessionMutex);

    // 1. 방장 소켓 닫기
    if (hostSocket != INVALID_SOCKET) {
        closesocket(hostSocket);
    }

    // 2. 모든 참가자 소켓 닫기
    for (auto it = guestPlayers.begin(); it != guestPlayers.end(); ++it) {
        if (it->second != INVALID_SOCKET) {
            closesocket(it->second);
        }
    }
    guestPlayers.clear();
}

void GameSession::SetHost(SOCKET socket) {
    std::lock_guard<std::mutex> lock(sessionMutex);
    hostSocket = socket;
    std::cout << "[Session " << sessionId << "] 방장(Host) 입장 완료. (Socket: " << socket << ")" << std::endl;
}

void GameSession::AddGuest(int guestId, SOCKET socket) {
    std::lock_guard<std::mutex> lock(sessionMutex);
    guestPlayers[guestId] = socket;
    std::cout << "[Session " << sessionId << "] 참가자(ID: " << guestId << ") 입장 완료. (Socket: " << socket << ")" << std::endl;
}

void GameSession::RelayFromHost(const char* data, int size) {
    std::lock_guard<std::mutex> lock(sessionMutex);
    // 방장이 보낸 것을 모든 게스트에게 전달
    for (auto it = guestPlayers.begin(); it != guestPlayers.end(); ++it) {
        send(it->second, data, size, 0);
    }
}

void GameSession::RelayToHost(const char* data, int size) {
    std::lock_guard<std::mutex> lock(sessionMutex);
    // 게스트가 보낸 것을 방장에게 전달
    if (hostSocket != INVALID_SOCKET) {
        send(hostSocket, data, size, 0);
    }
}

void GameSession::Broadcast(SOCKET senderSocket, char* data, int len) {
    std::lock_guard<std::mutex> lock(sessionMutex);

    // 1. 방장에게 전송 (보낸 사람이 방장이 아닐 때만)
    if (hostSocket != INVALID_SOCKET && hostSocket != senderSocket) {
        send(hostSocket, data, len, 0);
    }

    // 2. 모든 게스트에게 전송 (보낸 사람 제외)
    // C++17 [id, socket] 문법 대신 호환성이 높은 it(iterator) 방식을 사용합니다.
    for (auto it = guestPlayers.begin(); it != guestPlayers.end(); ++it) {
        SOCKET targetSocket = it->second;
        if (targetSocket != INVALID_SOCKET && targetSocket != senderSocket) {
            send(targetSocket, data, len, 0);
        }
    }
}