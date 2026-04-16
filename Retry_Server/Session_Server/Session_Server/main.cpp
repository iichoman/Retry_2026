#include <iostream>
#include "SessionServer.h"

int main() {
    SessionServer server;

    // 테스트용: 서버 시작 시 1번 세션(방장 ID 1)을 미리 만들어 둠
    server.CreateNewSession(1, 1);

    if (server.Start(9001)) {
        while (true) {
            std::string cmd;
            std::cin >> cmd;
            if (cmd == "exit") break;
        }
    }
    return 0;
}