#pragma once
#include <string>
#include <vector>

// ============================================================================
//  RoomData
//
//  로비의 방 1개를 표현. 단순 자료구조 + 작은 헬퍼들.
//  실제 클라이언트 객체는 LobbyManager가 ID로 관리하므로,
//  RoomData는 멤버를 ID 목록으로만 들고 있다.
// ============================================================================

class RoomData
{
public:
    int                roomId;
    std::string        roomName;
    int                hostClientId;
    int                maxPlayers;
    std::vector<int>   memberIds;        // 호스트도 포함
    std::vector<int>   memberTeams;      // memberIds와 1:1 정렬. -1=미배정, 0..MAX_TEAMS-1
    bool               isStarting;       // GAME_START 처리 중 (재요청 방지)

    RoomData(int id, const std::string& name, int hostId, int maxP);

    // 멤버 추가/제거. 성공 여부 반환.
    bool AddMember(int clientId);
    bool RemoveMember(int clientId);

    bool HasMember(int clientId) const;
    int  CurrentPlayers()      const { return (int)memberIds.size(); }
    bool IsFull()              const { return CurrentPlayers() >= maxPlayers; }
    bool IsEmpty()             const { return memberIds.empty(); }

    // 팀 선택. 정원 초과면 실패. teamId<0 이면 미배정으로 되돌림(허용).
    bool SetTeam(int clientId, int teamId);
    int  GetTeam(int clientId) const;       // 없으면 -1
    int  CountInTeam(int teamId) const;     // 해당 팀 인원
    // 미배정 멤버를 빈 팀 슬롯에 자동 배정(게임 시작 직전 호출).
    void AutoAssignUnassignedTeams();

    // 호스트가 떠나면 다음 멤버를 호스트로 승격. 빈 방이면 false.
    bool PromoteNewHostIfNeeded();
};