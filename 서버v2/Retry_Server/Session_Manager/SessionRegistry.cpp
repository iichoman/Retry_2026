#include "SessionRegistry.h"
#include "GameSession.h"
#include "LobbyReporter.h"
#include "../Common/Logger.h"
#include "V2Config.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>

#include <chrono>
#include <vector>

// v2: 멈춘 세션을 실제로 지우기까지의 유예 시간
static constexpr int GRAVE_SECONDS = 10;

SessionRegistry::SessionRegistry(LobbyReporter* r)
    : reporter(r), reaping(true)
{
    // v2: 서버 내부 측정 파일 (실행 폴더에 server_metrics_설정_날짜_시각.csv)
    const V2::Config& c = V2::Get();
    SYSTEMTIME st;
    GetLocalTime(&st);
    char path[160];
    sprintf_s(path, "server_metrics_%s-%s-%s-%s_%04d%02d%02d_%02d%02d%02d.csv",
        c.queueSync ? "queue" : "lock", c.asyncLog ? "async" : "sync",
        c.batchSend ? "batch" : "direct", c.preciseTick ? "precise" : "sleep",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    if (fopen_s(&metricsCsv, path, "w") == 0 && metricsCsv)
        fprintf(metricsCsv, "t_s,sessions,players,ticks,tick_work_avg_us,tick_work_max_us,jobs,lock_wait_ms,lock_count\n");
    else
        metricsCsv = nullptr;
    metricsStart = Clock::now();

    reaper = std::thread([this] { ReapLoop(); });
}

SessionRegistry::~SessionRegistry()
{
    reaping = false;
    if (reaper.joinable()) reaper.join();
    Shutdown();
    if (metricsCsv) { fclose(metricsCsv); metricsCsv = nullptr; }
}

bool SessionRegistry::CreateSession(int sessionId, int hostClientId, int mapSeed,
    const std::vector<int>& playerIds,
    const std::vector<int>& playerTeams,
    const std::vector<std::string>& playerNames)
{
    std::lock_guard<std::mutex> lk(mtx);

    if (sessions.count(sessionId) > 0)
    {
        Log::Warn("이미 존재하는 sessionId=%d", sessionId);
        return false;
    }

    auto sess = std::make_unique<GameSession>(sessionId, hostClientId, mapSeed,
        playerIds, playerTeams, playerNames, reporter);
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

    // v2: 바로 지우지 않고 유예 시간 뒤에 지운다
    std::lock_guard<std::mutex> lk(mtx);
    graveyard.emplace_back(Clock::now(), std::move(sess));
}

void SessionRegistry::PurgeGraveyard(bool all)
{
    std::vector<std::unique_ptr<GameSession>> dead;
    {
        std::lock_guard<std::mutex> lk(mtx);
        auto now = Clock::now();
        for (size_t i = 0; i < graveyard.size();)
        {
            if (all || now - graveyard[i].first >= std::chrono::seconds(GRAVE_SECONDS))
            {
                dead.push_back(std::move(graveyard[i].second));
                graveyard[i] = std::move(graveyard.back());
                graveyard.pop_back();
            }
            else ++i;
        }
    }
    // 락 밖에서 지운다 (던전 데이터 해제가 오래 걸릴 수 있음)
}

void SessionRegistry::WriteMetrics()
{
    if (!metricsCsv) return;

    int sessCount = 0, players = 0;
    unsigned long long ticks = 0, workUs = 0, jobs = 0, lockWaitUs = 0, lockCount = 0;
    unsigned maxUs = 0;
    {
        std::lock_guard<std::mutex> lk(mtx);
        for (auto& kv : sessions)
        {
            GameSession::TickStats s = kv.second->TakeStats();
            ++sessCount;
            players += s.players;
            ticks += s.ticks;
            workUs += s.workUs;
            jobs += s.jobs;
            lockWaitUs += s.lockWaitUs;
            lockCount += s.lockCount;
            if (s.maxUs > maxUs) maxUs = s.maxUs;
        }
    }

    double t = std::chrono::duration<double>(Clock::now() - metricsStart).count();
    double avgUs = ticks ? (double)workUs / (double)ticks : 0.0;
    fprintf(metricsCsv, "%.1f,%d,%d,%llu,%.1f,%u,%llu,%.2f,%llu\n",
        t, sessCount, players, ticks, avgUs, maxUs, jobs, lockWaitUs / 1000.0, lockCount);
    fflush(metricsCsv);
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

        PurgeGraveyard(false);
        WriteMetrics();
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
    PurgeGraveyard(true);   // 셧다운 시에는 워커가 이미 멈춘 뒤라 바로 지워도 된다
}