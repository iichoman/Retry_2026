#include "RoomData.h"
#include <algorithm>

RoomData::RoomData(int id, const std::string& name, int hostId, int maxP)
    : roomId(id)
    , roomName(name)
    , hostClientId(hostId)
    , maxPlayers(maxP)
    , isStarting(false)
{
    memberIds.reserve(maxP);
    memberIds.push_back(hostId);     // 호스트는 자동 입장
}

bool RoomData::AddMember(int clientId)
{
    if (IsFull()) return false;
    if (HasMember(clientId)) return false;
    memberIds.push_back(clientId);
    return true;
}

bool RoomData::RemoveMember(int clientId)
{
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return false;
    memberIds.erase(it);
    return true;
}

bool RoomData::HasMember(int clientId) const
{
    return std::find(memberIds.begin(), memberIds.end(), clientId) != memberIds.end();
}

bool RoomData::PromoteNewHostIfNeeded()
{
    if (memberIds.empty()) return false;
    if (HasMember(hostClientId)) return true;     // 호스트가 아직 있음

    hostClientId = memberIds.front();              // 첫 멤버를 호스트로
    return true;
}
