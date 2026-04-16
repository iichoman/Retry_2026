#pragma once
#include <winsock2.h>
#include <map>
#include <thread>
#include <mutex>
#include "GameSession.h"

class SessionServer {
public:
    SessionServer();
    ~SessionServer();

    bool Start(int port);

    // 메인 서버로부터 세션 생성 명령을 받았을 때 호출 (현재는 간략화)
    void CreateNewSession(int sessionId, int hostId);

private:
    void AcceptThread();
    void HandleClient(SOCKET clientSocket);
    void Stop();
    void SetNoDelay(SOCKET socket);

    SOCKET listenSocket;
    bool isRunning;
    std::map<int, GameSession*> sessions; // SessionID -> Session
    std::mutex sessionsMutex;
};