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
    bool               isStarting;       // GAME_START 처리 중 (재요청 방지)

    RoomData(int id, const std::string& name, int hostId, int maxP);

    // 멤버 추가/제거. 성공 여부 반환.
    bool AddMember(int clientId);
    bool RemoveMember(int clientId);

    bool HasMember(int clientId) const;
    int  CurrentPlayers()      const { return (int)memberIds.size(); }
    bool IsFull()              const { return CurrentPlayers() >= maxPlayers; }
    bool IsEmpty()             const { return memberIds.empty(); }

    // 호스트가 떠나면 다음 멤버를 호스트로 승격. 빈 방이면 false.
    bool PromoteNewHostIfNeeded();
};
