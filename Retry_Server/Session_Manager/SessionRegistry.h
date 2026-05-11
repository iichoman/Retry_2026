#pragma once
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>

class GameSession;

// ============================================================================
//  SessionRegistry
//
//  세션 매니저 프로세스 안에서 모든 활성 GameSession을 관리.
//
//   - CreateSession: IPC로 세션 생성 명령 받았을 때 호출
//   - AuthClient: 게임 클라가 게임 포트로 접속 후 인증할 때 호출 (sessionId, clientId)
//   - EndSession: 세션 정상/비정상 종료
//
//  스레드 안전: mutex로 모든 메서드 보호.
// ============================================================================

class SessionRegistry
{
public:
    SessionRegistry();
    ~SessionRegistry();

    // 세션 생성. 성공 시 true. 이미 있는 sessionId면 실패.
    bool CreateSession(int sessionId, int hostClientId, int mapSeed,
                       const std::vector<int>& playerIds);

    // 인증: sessionId 존재 + clientId가 그 세션의 멤버여야 통과.
    // 통과 시 GameSession 포인터 반환, 실패 시 nullptr.
    GameSession* AuthClient(int sessionId, int clientId);

    // 세션 종료
    void EndSession(int sessionId, int reason);

    // 모든 세션 정리 (셧다운 시)
    void Shutdown();

private:
    std::mutex                                                 mtx;
    std::unordered_map<int, std::unique_ptr<GameSession>>      sessions;
};
