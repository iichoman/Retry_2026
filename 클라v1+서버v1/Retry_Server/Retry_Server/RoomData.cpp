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
    memberReady.reserve(maxP);
    memberIds.push_back(hostId);          // 호스트는 자동 입장
    memberTeams.push_back(TEAM_UNASSIGNED);
    // ★ 여기를 빠뜨리면 memberReady 인덱스가 1칸 밀려서
    //   마지막에 들어온 멤버의 준비 버튼이 먹지 않는다. (누가 나가면 크기가 맞아 정상화됨)
    memberReady.push_back(0);
}

bool RoomData::AddMember(int clientId)
{
    if (IsFull()) return false;
    if (HasMember(clientId)) return false;
    memberIds.push_back(clientId);
    memberTeams.push_back(TEAM_UNASSIGNED);
    memberReady.push_back(0);          // 새 멤버는 준비 안 됨
    EnsureParallelSizes();
    return true;
}

// memberIds / memberTeams / memberReady 는 항상 같은 길이여야 한다.
// 하나라도 짧으면 인덱스가 밀려 준비 상태가 엉킨다.
void RoomData::EnsureParallelSizes()
{
    memberTeams.resize(memberIds.size(), TEAM_UNASSIGNED);
    memberReady.resize(memberIds.size(), 0);
}

bool RoomData::RemoveMember(int clientId)
{
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return false;
    size_t idx = (size_t)(it - memberIds.begin());
    memberIds.erase(memberIds.begin() + idx);
    if (idx < memberTeams.size()) memberTeams.erase(memberTeams.begin() + idx);
    if (idx < memberReady.size()) memberReady.erase(memberReady.begin() + idx);
    EnsureParallelSizes();
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

// ── 준비 상태 ────────────────────────────────────────────────────────────
void RoomData::SetReady(int clientId, bool ready)
{
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return;
    size_t idx = (size_t)(it - memberIds.begin());
    if (idx < memberReady.size()) memberReady[idx] = ready ? 1 : 0;
}

bool RoomData::IsReady(int clientId) const
{
    if (clientId == hostClientId) return true;     // 방장은 준비 개념 없음
    auto it = std::find(memberIds.begin(), memberIds.end(), clientId);
    if (it == memberIds.end()) return false;
    size_t idx = (size_t)(it - memberIds.begin());
    return idx < memberReady.size() && memberReady[idx] != 0;
}

bool RoomData::AllMembersReady() const
{
    for (size_t i = 0; i < memberIds.size(); ++i)
    {
        if (memberIds[i] == hostClientId) continue;      // 방장 제외
        if (i >= memberReady.size() || memberReady[i] == 0) return false;
    }
    return true;
}

bool RoomData::PromoteNewHostIfNeeded()
{
    if (memberIds.empty()) return false;
    if (HasMember(hostClientId)) return true;     // 호스트가 아직 있음

    hostClientId = memberIds.front();              // 첫 멤버를 호스트로
    // 새 방장은 준비 개념에서 벗어나므로 플래그를 정리한다.
    SetReady(hostClientId, false);
    return true;
}