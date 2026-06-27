#include "SessionRegistry.h"
#include "GameSession.h"
#include "../Common/Logger.h"

SessionRegistry::SessionRegistry()
{
}

SessionRegistry::~SessionRegistry()
{
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

    auto sess = std::make_unique<GameSession>(sessionId, hostClientId, mapSeed, playerIds, playerTeams);
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