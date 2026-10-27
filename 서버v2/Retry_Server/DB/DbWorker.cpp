#include "DbWorker.h"
#include "DbConfig.h"
#include "../Common/Logger.h"

DbWorker::~DbWorker()
{
    Stop();
}

bool DbWorker::Start()
{
    if (running_.load()) return true;

    bool ok = db_.Connect();
    available_.store(ok);

    if (!ok)
    {
        if (DB_OPTIONAL)
            Log::Warn("[DB] 접속 실패 - DB 없이 계속 진행한다 (전적은 저장되지 않음)");
        else
            Log::Error("[DB] 접속 실패");
        if (!DB_OPTIONAL) return false;
    }

    running_.store(true);
    worker_ = std::thread([this] { RunLoop(); });
    return ok;
}

void DbWorker::Stop()
{
    if (!running_.exchange(false)) return;
    cv_.notify_all();
    if (worker_.joinable()) worker_.join();
    db_.Disconnect();
    available_.store(false);
}

void DbWorker::Push(std::function<void()> job)
{
    // 워커가 없으면(Start 전/후) 별도 스레드로 넘긴다.
    // 호출한 스레드에서 바로 실행하면 안 된다 — 호출자는 로비 mutex를 쥔
    // IOCP 스레드이고, 콜백이 그 mutex를 다시 잡으면 데드락이다.
    if (!running_.load())
    {
        std::thread(std::move(job)).detach();
        return;
    }
    {
        std::lock_guard<std::mutex> lk(mtx_);
        jobs_.push_back(std::move(job));
    }
    cv_.notify_one();
}

size_t DbWorker::PendingCount() const
{
    std::lock_guard<std::mutex> lk(mtx_);
    return jobs_.size();
}

void DbWorker::RunLoop()
{
    while (running_.load())
    {
        std::function<void()> job;
        {
            std::unique_lock<std::mutex> lk(mtx_);
            cv_.wait(lk, [this] { return !jobs_.empty() || !running_.load(); });
            if (!running_.load() && jobs_.empty()) break;
            job = std::move(jobs_.front());
            jobs_.pop_front();
        }
        job();
    }

    // 종료 시 남은 작업(주로 전적 기록)은 마저 처리한다.
    for (;;)
    {
        std::function<void()> job;
        {
            std::lock_guard<std::mutex> lk(mtx_);
            if (jobs_.empty()) break;
            job = std::move(jobs_.front());
            jobs_.pop_front();
        }
        job();
    }
}

void DbWorker::RequestLogin(const std::string& nicknameUtf8,
    const std::string& passwordUtf8, LoginDone done)
{
    // DB가 없어도 콜백은 반드시 다른 스레드에서 부른다. (데드락 방지)
    // -1 = DB 없이 통과. 이 경우 로비가 계정 검증 없이 입장시킨다.
    if (!available_.load())
    {
        Push([done]() { if (done) done(-1, 0); });
        return;
    }

    Push([this, nicknameUtf8, passwordUtf8, done]()
        {
            int pid = 0, runs = 0, ext = 0;
            int r = db_.LoginPlayer(Utf8ToWide(nicknameUtf8.c_str()),
                Utf8ToWide(passwordUtf8.c_str()), pid, runs, ext);
            if (r < 0)
            {
                available_.store(db_.IsConnected());
                if (done) done(-1, 0);
                return;
            }
            if (done) done(r, pid);
        });
}

void DbWorker::RequestRegister(const std::string& nicknameUtf8,
    const std::string& passwordUtf8, AccountDone done)
{
    if (!available_.load()) { Push([done]() { if (done) done(-1); }); return; }

    Push([this, nicknameUtf8, passwordUtf8, done]()
        {
            int r = db_.RegisterPlayer(Utf8ToWide(nicknameUtf8.c_str()),
                Utf8ToWide(passwordUtf8.c_str()));
            if (r < 0) available_.store(db_.IsConnected());
            if (done) done(r);
        });
}

void DbWorker::RequestResetPassword(const std::string& nicknameUtf8,
    const std::string& passwordUtf8, AccountDone done)
{
    if (!available_.load()) { Push([done]() { if (done) done(-1); }); return; }

    Push([this, nicknameUtf8, passwordUtf8, done]()
        {
            int r = db_.ResetPassword(Utf8ToWide(nicknameUtf8.c_str()),
                Utf8ToWide(passwordUtf8.c_str()));
            if (r < 0) available_.store(db_.IsConnected());
            if (done) done(r);
        });
}

void DbWorker::RequestStats(const std::string& nicknameUtf8, StatsDone done)
{
    if (!available_.load())
    {
        Push([done]() { if (done) done(false, Stats{ 0,0,0,0,0 }); });
        return;
    }

    Push([this, nicknameUtf8, done]()
        {
            Stats s{ 0,0,0,0,0 };
            bool ok = db_.GetPlayerStats(Utf8ToWide(nicknameUtf8.c_str()),
                s.runs, s.extractions, s.monsterKills, s.playerKills, s.bestSeconds);
            if (!ok)
            {
                available_.store(db_.IsConnected());
                // 여기서 실패하는 원인은 둘 중 하나다.
                //  (1) DB 미연결  (2) 그 닉네임이 dbo.Player에 없음
                //      → DB가 꺼진 채로 로그인하면 계정 검증 없이 통과하므로
                //        DB에는 그 계정이 존재하지 않는다.
                Log::Warn("[DB] 전적 조회 실패: name=%s (연결=%s). "
                    "DB가 꺼진 상태로 로그인했거나 계정이 DB에 없다.",
                    nicknameUtf8.c_str(), db_.IsConnected() ? "정상" : "끊김");
            }
            else
            {
                Log::Info("[DB] 전적 조회: name=%s runs=%d ext=%d mk=%d pk=%d best=%d",
                    nicknameUtf8.c_str(), s.runs, s.extractions,
                    s.monsterKills, s.playerKills, s.bestSeconds);
            }
            if (done) done(ok, s);
        });
}

void DbWorker::RequestPlayRecord(const std::string& nicknameUtf8, int sessionId,
    bool extracted, int playSeconds,
    int monsterKills, int playerKills)
{
    if (!available_.load()) return;

    Push([this, nicknameUtf8, sessionId, extracted, playSeconds, monsterKills, playerKills]()
        {
            bool ok = db_.InsertPlayRecord(Utf8ToWide(nicknameUtf8.c_str()), sessionId,
                extracted, playSeconds, monsterKills, playerKills);
            if (!ok)
            {
                available_.store(db_.IsConnected());
                Log::Warn("[DB] 전적 기록 실패: name=%s sid=%d (연결=%s)",
                    nicknameUtf8.c_str(), sessionId,
                    db_.IsConnected() ? "정상" : "끊김");
            }
            else
            {
                Log::Info("[DB] 전적 기록: name=%s sid=%d 탈출=%d %d초 몬스터%d 플레이어%d",
                    nicknameUtf8.c_str(), sessionId, extracted ? 1 : 0,
                    playSeconds, monsterKills, playerKills);
            }
        });
}
