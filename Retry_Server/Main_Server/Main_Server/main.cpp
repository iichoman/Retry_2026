#include <iostream>
#include "MainServer.h"

int main() {
    MainServer server;
    if (server.Start(9000)) {
        std::cout << "메인 서버 시작 (포트 번호: 9000)" << std::endl;
        std::cout << "방 생성 및 참가를 관리" << std::endl;

        // 서버가 종료되지 않게 대기
        while (true) {
            std::string command;
            std::cin >> command;
            if (command == "exit") break;
        }
    }
    else {
        std::cerr << "서버 시작 실패!" << std::endl;
    }

    return 0;
}