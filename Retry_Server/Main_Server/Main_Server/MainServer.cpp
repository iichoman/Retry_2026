#include "MainServer.h"
#include "SessionManager.h"
#include <iostream>

#pragma comment(lib, "ws2_32.lib")

MainServer::MainServer() : listenSocket(INVALID_SOCKET), isRunning(false) {}

MainServer::~MainServer() { Stop(); }

bool MainServer::Start(int port) {
    WSADATA wsaData;
    // WSAStartup 반환값 확인
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        std::cerr << "WSAStartup 실패!" << std::endl;
        return false;
    }

    listenSocket = socket(AF_INET, SOCK_STREAM, 0);
    sockaddr_in addr = {};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    addr.sin_addr.s_addr = INADDR_ANY;

    if (bind(listenSocket, (sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR) return false;
    listen(listenSocket, SOMAXCONN);

    isRunning = true;
    std::thread(&MainServer::AcceptThread, this).detach();
    return true;
}

void MainServer::AcceptThread() {
    int idCounter = 1;
    while (isRunning) {
        SOCKET clientSocket = accept(listenSocket, nullptr, nullptr);
        if (clientSocket == INVALID_SOCKET) {
            if (isRunning) std::cerr << "[Main] Accept Error" << std::endl;
            break;
        }

		SetNoDelay(clientSocket);

        {
            std::lock_guard<std::mutex> lock(clientsMutex);
            clients[idCounter] = clientSocket;
        }

        std::cout << "[Main] 클라이언트 접속: ID " << idCounter << std::endl;
        std::thread(&MainServer::ClientThread, this, clientSocket, idCounter++).detach();
    }
}

void MainServer::ClientThread(SOCKET clientSocket, int clientId) {
    while (isRunning) {
        PacketHeader header;
        int res = recv(clientSocket, (char*)&header, sizeof(header), 0);
        if (res <= 0) break;

        if (header.type == PacketType::ROOM_CREATE) {
            RoomInfo newRoom = SessionManager::Instance().CreateRoom(clientId, "New Dungeon");
            std::cout << "[Main] ID " << clientId << "가 방을 생성함. RoomID: " << newRoom.roomId << std::endl;

            // 유니티가 해석할 수 있도록 Header를 먼저 보냅니다.
            PacketHeader resHeader;
            resHeader.type = PacketType::ROOM_CREATE;
            resHeader.size = sizeof(RoomInfo);
            send(clientSocket, (char*)&resHeader, sizeof(PacketHeader), 0);

            // 그 다음 RoomInfo를 보냅니다.
            send(clientSocket, (char*)&newRoom, sizeof(RoomInfo), 0);
        }
        else if (header.type == PacketType::ROOM_JOIN) {
            int targetRoomId;
            recv(clientSocket, (char*)&targetRoomId, sizeof(int), 0);

            if (SessionManager::Instance().JoinRoom(targetRoomId, clientId)) {
                std::cout << "[Main] ID " << clientId << "가 " << targetRoomId << "번 방에 참가함." << std::endl;

                // 참가 성공 후 유니티에게 해당 방의 정보를 응답으로 되돌려줍니다.
                RoomInfo targetRoom = {};
                auto roomList = SessionManager::Instance().GetRoomList();
                for (const auto& r : roomList) {
                    if (r.roomId == targetRoomId) { targetRoom = r; break; }
                }

                // [중요 수정] 참가자의 경우, 유니티가 myClientId를 설정할 수 있도록 
                // targetRoom.hostId를 현재 접속한 clientId로 덮어씌워서 보냅니다.
                targetRoom.hostId = clientId;

                PacketHeader resHeader;
                resHeader.type = PacketType::ROOM_JOIN;
                resHeader.size = sizeof(RoomInfo);

                send(clientSocket, (char*)&resHeader, sizeof(PacketHeader), 0);
                send(clientSocket, (char*)&targetRoom, sizeof(RoomInfo), 0);
            }
        }
    }

    closesocket(clientSocket);
    std::lock_guard<std::mutex> lock(clientsMutex);
    clients.erase(clientId);
}

void MainServer::Stop() {
    isRunning = false;
    closesocket(listenSocket);
    WSACleanup();
}

void MainServer::SetNoDelay(SOCKET socket) {
    BOOL optVal = TRUE;
    setsockopt(socket, IPPROTO_TCP, TCP_NODELAY, (char*)&optVal, sizeof(optVal));
}