#pragma once
#include <winsock2.h>
#include <vector>
#include <thread>
#include <map>
#include <mutex>
#include "Protocol.h"

class MainServer {
public:
    MainServer();
    ~MainServer();

    bool Start(int port);
    void Stop();
    void SetNoDelay(SOCKET socket);

private:
    void AcceptThread();
    void ClientThread(SOCKET clientSocket, int clientId);

    SOCKET listenSocket;
    bool isRunning;
    std::map<int, SOCKET> clients;
    std::mutex clientsMutex;
};