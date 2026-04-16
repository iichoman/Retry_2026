#include "SessionServer.h"
#include <iostream>

#pragma comment(lib, "ws2_32.lib")

SessionServer::SessionServer() : listenSocket(INVALID_SOCKET), isRunning(false) {}

// 파괴자 구현 추가
SessionServer::~SessionServer() {
    Stop();
}

bool SessionServer::Start(int port) {
    WSADATA wsaData;
    // WSAStartup 반환값 확인
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        std::cerr << "WSAStartup 실패!" << std::endl;
        return false;
    }

    listenSocket = socket(AF_INET, SOCK_STREAM, 0);
    if (listenSocket == INVALID_SOCKET) return false;

    sockaddr_in addr = {};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    addr.sin_addr.s_addr = INADDR_ANY;

    if (bind(listenSocket, (sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR) {
        std::cerr << "Bind 실패!" << std::endl;
        return false;
    }

    if (listen(listenSocket, SOMAXCONN) == SOCKET_ERROR) return false;

    isRunning = true;
    std::cout << "==== Session Server Started (Port: " << port << ") ====" << std::endl;

    // 접속 수락 스레드 시작
    std::thread(&SessionServer::AcceptThread, this).detach();
    return true;
}

void SessionServer::CreateNewSession(int sessionId, int hostId) {
    std::lock_guard<std::mutex> lock(sessionsMutex);
    sessions[sessionId] = new GameSession(sessionId, hostId);
    std::cout << "[System] 새 세션 생성 완료: ID " << sessionId << " (방장 ID: " << hostId << ")" << std::endl;
}

void SessionServer::AcceptThread() {
    while (isRunning) {
        SOCKET clientSocket = accept(listenSocket, nullptr, nullptr);
        if (clientSocket != INVALID_SOCKET) {
            SetNoDelay(clientSocket);
            std::cout << "[Session] 새 클라이언트 소켓 수락 및 TCP_NODELAY 설정 완료." << std::endl;
            std::thread(&SessionServer::HandleClient, this, clientSocket).detach();
        }
    }
}

void SessionServer::HandleClient(SOCKET clientSocket) {
    // 유니티 클라이언트가 접속하자마자 보낼 인증 데이터 (SessionID, ClientID)
    int sessionId = 0, clientId = 0;

    if (recv(clientSocket, (char*)&sessionId, sizeof(int), 0) <= 0) return;
    if (recv(clientSocket, (char*)&clientId, sizeof(int), 0) <= 0) return;

    GameSession* mySession = nullptr;
    {
        std::lock_guard<std::mutex> lock(sessionsMutex);
        if (sessions.count(sessionId)) {
            mySession = sessions[sessionId];
        }
    }

    if (!mySession) {
        std::cout << "[Error] 유효하지 않은 세션 ID: " << sessionId << std::endl;
        closesocket(clientSocket);
        return;
    }

    // 방장 여부 확인 및 등록
    bool isHost = (clientId == mySession->GetHostId());
    if (isHost) {
        mySession->SetHost(clientSocket);
        std::cout << "[Session " << sessionId << "] 방장(ID: " << clientId << ") 입장." << std::endl;
    }
    else {
        mySession->AddGuest(clientId, clientSocket);
        std::cout << "[Session " << sessionId << "] 참가자(ID: " << clientId << ") 입장." << std::endl;
        // 새 참가자에게 현재 방장의 정보를 전송
        // 방장의 소켓이나 ID 정보를 활용해 "현재 방장은 이런 상태야"라고 
        // 개별적으로 send를 한 번 해주는 로직이 필요합니다.
        // PlayerData hostData = mySession->GetHostData(); 
        // send(clientSocket, (char*)&hostData, sizeof(PlayerData), 0);
    }

    // Hybrid P2P 데이터 중계 루프
    char buffer[4096];
    while (isRunning) {
        int len = recv(clientSocket, buffer, sizeof(buffer), 0);
        if (len <= 0) break;

        // Super Peer 로직: 방장이 보낸 건 게스트들에게, 게스트가 보낸 건 방장에게
        /*if (isHost) {
            mySession->RelayFromHost(buffer, len);
        }
        else {
            mySession->RelayToHost(buffer, len);
        }*/

        // 기존: isHost에 따라 RelayFromHost 또는 RelayToHost 호출
        // 수정: 세션 내의 '나를 제외한 모든 인원'에게 브로드캐스트
        mySession->Broadcast(clientSocket, buffer, len);
    }

    std::cout << "[Session] 클라이언트 종료: ID " << clientId << std::endl;
    closesocket(clientSocket);
}

void SessionServer::Stop() {
    isRunning = false;
    if (listenSocket != INVALID_SOCKET) {
        closesocket(listenSocket);
        listenSocket = INVALID_SOCKET;
    }
    WSACleanup();
}

void SessionServer::SetNoDelay(SOCKET socket) {
    BOOL optVal = TRUE;
    setsockopt(socket, IPPROTO_TCP, TCP_NODELAY, (char*)&optVal, sizeof(optVal));
}