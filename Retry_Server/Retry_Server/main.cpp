#include <iostream>
#include <vector>
#include <winsock2.h>
#include <thread>
#include <mutex>

#pragma comment(lib, "ws2_32.lib")

// 유니티와 맞춘 데이터 구조체
struct PlayerData {
	int id; // 몇 번째 클라이언트인지 구분하기 위한 ID
	float x, y, z; // 3D 위치
	float rotY; // Y축 회전 (플레이어가 바라보는 방향)
	float speed; // 속도(애니메이션의 속도 맞추기 위한 용도)
    int state; // 0: Move, 1: Attack
};

// 클라이언트 목록 관리
std::vector<SOCKET> clients;
std::mutex clientsMutex;

// 모든 클라이언트(보낸 사람 제외)에게 데이터 전송
void Broadcast(PlayerData data, SOCKET senderSocket) {
    std::lock_guard<std::mutex> lock(clientsMutex);
    for (SOCKET client : clients) {
        if (client != senderSocket) {
            send(client, (char*)&data, sizeof(PlayerData), 0);
        }
    }
}

void HandleClient(SOCKET clientSocket, int clientId) {
    PlayerData data;
    while (true) {
        // 데이터 수신
        int valread = recv(clientSocket, (char*)&data, sizeof(PlayerData), 0);

        if (valread <= 0) { // 접속 종료 시
            break;
        }

        // 수신된 데이터에 서버가 부여한 ID를 입혀서 다시 배포
        data.id = clientId;
        Broadcast(data, clientSocket);
    }

    // 접속 종료 처리
    {
        std::lock_guard<std::mutex> lock(clientsMutex);
        for (auto it = clients.begin(); it != clients.end(); ++it) {
            if (*it == clientSocket) {
                clients.erase(it);
                break;
            }
        }
    }
    closesocket(clientSocket);
    std::cout << "클라이언트 접속 해제 (ID: " << clientId << ")" << std::endl;
}

int main() {
    // 서버 한글 깨짐 방지를 위해 인코딩을 UTF-8(BOM)으로 저장하세요.
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) return -1;

    SOCKET serverSocket = socket(AF_INET, SOCK_STREAM, 0);
    sockaddr_in address;
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = INADDR_ANY;
    address.sin_port = htons(9000);

    bind(serverSocket, (struct sockaddr*)&address, sizeof(address));
    listen(serverSocket, 10);

    std::cout << "[서버] 클라이언트 접속 대기 중..." << std::endl;

    int idCounter = 1;
    while (true) {
        SOCKET clientSocket = accept(serverSocket, nullptr, nullptr);

        {
            std::lock_guard<std::mutex> lock(clientsMutex);
            clients.push_back(clientSocket);
        }

        std::cout << "클라이언트 접속 성공 (ID: " << idCounter << ")" << std::endl;

        // 스레드 생성 시 클라이언트 소켓과 ID 부여
        std::thread(HandleClient, clientSocket, idCounter++).detach();
    }

    closesocket(serverSocket);
    WSACleanup();
    return 0;
}