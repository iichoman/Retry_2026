#include "RoomData.h"
#include "../Common/PacketProtocol.h"
#include <algorithm>

RoomData::RoomData(int id, const std::string& name, int hostId, int maxP)
    : roomId(id)
    , roomName(name)
    , hostClientId(hostId)
    , maxPlayers(maxP)
    , isStarting(false)
{
    memberIds.reserve(maxP);
    memberTeams.reserve(maxP);
    memberIds.push_back(hostId);          // 호스트는 자동 입장
    memberTeams.push_back(TEAM_UNASSIGNED);
}

bool RoomData::AddMember(int clientId)
{
    if (IsFull()) return false;
    if (HasMember(clientId)) return false;
    memberIds.push_back(clientId);
    memberTeams.push_back(TEAM_UNASSIGNED);
    return true;
}

bool RoomData::RemoveMember(int clientId)
{
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return false;
    size_t idx = (size_t)(it - memberIds.begin());
    memberIds.erase(memberIds.begin() + idx);
    if (idx < memberTeams.size()) memberTeams.erase(memberTeams.begin() + idx);
    return true;
}

// ── 팀 선택 ──────────────────────────────────────────────────────────────
bool RoomData::SetTeam(int clientId, int teamId)
{
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return false;
    size_t idx = (size_t)(it - memberIds.begin());

    if (teamId < 0)                       // 미배정으로 되돌리기
    {
        memberTeams[idx] = TEAM_UNASSIGNED;
        return true;
    }
    if (teamId >= MAX_TEAMS) return false;
    if (memberTeams[idx] == teamId) return true;            // 이미 그 팀
    if (CountInTeam(teamId) >= TEAM_CAPACITY) return false; // 정원 초과
    memberTeams[idx] = teamId;
    return true;
}

int RoomData::GetTeam(int clientId) const
{
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return TEAM_UNASSIGNED;
    size_t idx = (size_t)(it - memberIds.begin());
    return (idx < memberTeams.size()) ? memberTeams[idx] : TEAM_UNASSIGNED;
}

int RoomData::CountInTeam(int teamId) const
{
    int c = 0;
    for (int t : memberTeams) if (t == teamId) ++c;
    return c;
}

void RoomData::AutoAssignUnassignedTeams()
{
    for (size_t i = 0; i < memberTeams.size(); ++i)
    {
        if (memberTeams[i] != TEAM_UNASSIGNED) continue;
        for (int t = 0; t < MAX_TEAMS; ++t)
        {
            if (CountInTeam(t) < TEAM_CAPACITY) { memberTeams[i] = t; break; }
        }
        if (memberTeams[i] == TEAM_UNASSIGNED) memberTeams[i] = 0; // 안전망
    }
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