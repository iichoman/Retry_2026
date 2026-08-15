#pragma once
#include <atomic>
#include <memory>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <vector>

class GameSession;
class LobbyReporter;

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
    // reporter: 세션 종료를 로비에 보고할 채널. nullptr이면 보고 생략.
    explicit SessionRegistry(LobbyReporter* reporter = nullptr);
    ~SessionRegistry();

    // 세션 생성. 성공 시 true. 이미 있는 sessionId면 실패.
    bool CreateSession(int sessionId, int hostClientId, int mapSeed,
        const std::vector<int>& playerIds,
        const std::vector<int>& playerTeams);

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
    LobbyReporter*                                             reporter;

    // 종료된 세션 회수 스레드.
    // GameSession은 자기 틱 스레드 안에서 종료를 판정하므로 스스로를 지울 수 없다
    // (join 자기 자신 → 데드락). 별도 스레드가 주기적으로 회수한다.
    std::atomic_bool                                           reaping;
    std::thread                                                reaper;
    void ReapLoop();
};