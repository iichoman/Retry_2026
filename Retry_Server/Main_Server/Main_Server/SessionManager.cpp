#include "SessionManager.h"

RoomInfo SessionManager::CreateRoom(int hostId, const char* name) {
    std::lock_guard<std::mutex> lock(sessionMutex);
    RoomInfo ri;
    ri.roomId = nextRoomId++;
    ri.hostId = hostId;
    ri.currentPlayers = 1;
    strcpy_s(ri.roomName, name);

    rooms.push_back(ri);
    return ri;
}

bool SessionManager::JoinRoom(int roomId, int playerId) {
    std::lock_guard<std::mutex> lock(sessionMutex);
    for (auto& room : rooms) {
        if (room.roomId == roomId) {
            room.currentPlayers++;
            return true;
        }
    }
    return false;
}

std::vector<RoomInfo> SessionManager::GetRoomList() {
    std::lock_guard<std::mutex> lock(sessionMutex);
    return rooms;
}