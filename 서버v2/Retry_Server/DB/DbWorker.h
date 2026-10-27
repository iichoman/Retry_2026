#pragma once
#include "OdbcSession.h"

#include <atomic>
#include <condition_variable>
#include <deque>
#include <functional>
#include <mutex>
#include <string>
#include <thread>

// ============================================================================
//  DbWorker - DB 작업을 전담하는 스레드 1개 + 작업 큐
//
//  왜 이렇게 하나:
//    SQL 호출은 수 ms가 걸린다. 이걸 IOCP 워커 스레드에서 그냥 부르면
//    그 시간 동안 다른 클라의 패킷 처리가 통째로 멈춘다. 로비는 mutex
//    하나로 직렬화되어 있어서 더 심하다.
//    → 요청만 큐에 넣고 즉시 돌아오고, 결과는 콜백으로 받는다.
//
//  ODBC 핸들을 이 스레드 하나만 만지므로 핸들 락이 필요 없다.
//  (강의자료의 "Thread마다 핸들을 하나씩" 을 스레드 1개로 단순화한 형태)
// ============================================================================
class DbWorker
{
public:
    ~DbWorker();

    // DB 접속 + 워커 스레드 시작.
    // 반환 false = 접속 실패. DB_OPTIONAL이 true면 호출자는 무시하고 진행해도 된다.
    bool Start();
    void Stop();

    bool IsRunning() const { return running_.load(); }
    // DB가 실제로 붙어 있는지. false면 로그인은 DB 없이 통과시킨다.
    bool IsAvailable() const { return available_.load(); }

    // ── 비동기 요청 ───────────────────────────────────────────
    // 로그인. 완료되면 done(ok, playerId, totalRuns, totalExtractions)이
    // DB 스레드에서 호출된다. 콜백 안에서는 오래 걸리는 일을 하지 말 것.
    // result: 0=성공, 1=없는 닉네임, 2=비밀번호 불일치, -1=DB 사용 불가(통과 처리)
    using LoginDone = std::function<void(int result, int playerId)>;
    void RequestLogin(const std::string& nicknameUtf8,
        const std::string& passwordUtf8, LoginDone done);

    // 회원가입 / 비밀번호 재설정. result: 0=성공, 1=실패, -1=DB 없음
    using AccountDone = std::function<void(int result)>;
    void RequestRegister(const std::string& nicknameUtf8,
        const std::string& passwordUtf8, AccountDone done);
    void RequestResetPassword(const std::string& nicknameUtf8,
        const std::string& passwordUtf8, AccountDone done);

    // 전적 조회
    struct Stats { int runs, extractions, monsterKills, playerKills, bestSeconds; };
    using StatsDone = std::function<void(bool ok, Stats s)>;
    void RequestStats(const std::string& nicknameUtf8, StatsDone done);

    // 전적 기록. 결과를 기다릴 필요가 없어 콜백 없음.
    void RequestPlayRecord(const std::string& nicknameUtf8, int sessionId,
        bool extracted, int playSeconds,
        int monsterKills, int playerKills);

    // 대기 중인 작업 수 (로그/모니터링용)
    size_t PendingCount() const;

private:
    OdbcSession                        db_;
    std::thread                        worker_;
    std::atomic_bool                   running_{ false };
    std::atomic_bool                   available_{ false };

    mutable std::mutex                 mtx_;
    std::condition_variable            cv_;
    std::deque<std::function<void()>>  jobs_;

    void RunLoop();
    void Push(std::function<void()> job);
};
