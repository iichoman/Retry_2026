#include "SessionRegistry.h"
#include "GameSession.h"
#include "LobbyReporter.h"
#include "../Common/Logger.h"

#include <chrono>
#include <vector>

SessionRegistry::SessionRegistry(LobbyReporter* r)
    : reporter(r), reaping(true)
{
    reaper = std::thread([this] { ReapLoop(); });
}

SessionRegistry::~SessionRegistry()
{
    reaping = false;
    if (reaper.joinable()) reaper.join();
    Shutdown();
}

bool SessionRegistry::CreateSession(int sessionId, int hostClientId, int mapSeed,
    const std::vector<int>& playerIds,
    const std::vector<int>& playerTeams)
{
    std::lock_guard<std::mutex> lk(mtx);

    if (sessions.count(sessionId) > 0)
    {
        Log::Warn("이미 존재하는 sessionId=%d", sessionId);
        return false;
    }

    auto sess = std::make_unique<GameSession>(sessionId, hostClientId, mapSeed,
        playerIds, playerTeams, reporter);
    sess->Start();
    sessions[sessionId] = std::move(sess);
    return true;
}

GameSession* SessionRegistry::AuthClient(int sessionId, int clientId)
{
    std::lock_guard<std::mutex> lk(mtx);
    auto it = sessions.find(sessionId);
    if (it == sessions.end()) return nullptr;
    if (!it->second->IsAllowedPlayer(clientId)) return nullptr;
    return it->second.get();
}

void SessionRegistry::EndSession(int sessionId, int /*reason*/)
{
    std::unique_ptr<GameSession> sess;
    {
        std::lock_guard<std::mutex> lk(mtx);
        auto it = sessions.find(sessionId);
        if (it == sessions.end()) return;
        sess = std::move(it->second);
        sessions.erase(it);
    }
    // sess는 lock 밖에서 소멸 → tickThread join 시 lock 경합 방지
    sess->Stop();
    Log::Info("세션 종료: id=%d", sessionId);
}

// 1초마다 종료 판정된 세션을 회수한다.
// GameSession::IsFinished()는 SESSION_ENDED를 이미 보낸 세션에 true.
void SessionRegistry::ReapLoop()
{
    while (reaping)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(1000));

        std::vector<int> finished;
        {
            std::lock_guard<std::mutex> lk(mtx);
            for (auto& kv : sessions)
                if (kv.second->IsFinished()) finished.push_back(kv.first);
        }
        // EndSession이 자체 lock을 잡으므로 lock 밖에서 호출.
        for (int sid : finished) EndSession(sid, 0);
    }
}

void SessionRegistry::Shutdown()
{
    std::vector<std::unique_ptr<GameSession>> toRelease;
    {
        std::lock_guard<std::mutex> lk(mtx);
        for (auto& kv : sessions) toRelease.push_back(std::move(kv.second));
        sessions.clear();
    }
    for (auto& s : toRelease) s->Stop();
}