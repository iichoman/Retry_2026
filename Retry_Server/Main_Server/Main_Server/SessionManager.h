#pragma once
#include <vector>
#include <mutex>
#include "Protocol.h"

class SessionManager {
public:
    static SessionManager& Instance() {
        static SessionManager instance;
        return instance;
    }

    RoomInfo CreateRoom(int hostId, const char* name);
    bool JoinRoom(int roomId, int playerId);
    std::vector<RoomInfo> GetRoomList();

private:
    std::vector<RoomInfo> rooms;
    std::mutex sessionMutex;
    int nextRoomId = 1;
};