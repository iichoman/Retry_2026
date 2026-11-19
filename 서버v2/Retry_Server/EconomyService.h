#pragma once
#include "../Common/EconomyStore.h"

#include <functional>
#include <map>
#include <mutex>
#include <string>
#include <vector>

class DbWorker;
class OdbcSession;

// ============================================================================
//  EconomyService - 상점·보관함 저장 (로비 전용)
//
//   - DB 계정(dbPlayerId > 0)은 DB(dbo.Stash, dbo.StashItem)에 저장한다.
//     DB 없이 들어온 계정은 파일(Data\Economy)에 저장한다. → DB 없는 PC에서도 동작
//   - 읽기 → 규칙 적용 → 저장을 전부 DB 스레드 하나에서 순서대로 한다.
//     같은 계정을 두 곳에서 동시에 고칠 일이 없고, 로비 IOCP 스레드는 요청만 넣고 돌아간다.
//   - 세션 서버는 저장소를 모른다. 출격 장비는 세션 생성 IPC에 실어 보내고,
//     정산(탈출·사망·환불)은 세션이 IPC로 보고한다. → 세션 서버가 다른 PC여도 동작
// ============================================================================
class EconomyService
{
public:
    struct Member { int clientId = 0; int dbPlayerId = 0; std::string name; };
    using StateDone = std::function<void(Economy::Result, const Economy::Account&)>;
    // 세션 생성 요청. DB 스레드에서 불린다. grants는 BeginRun에 넘긴 members 순서.
    using CreateSession = std::function<bool(const std::string& token,
        const std::vector<Economy::Grant>& grants)>;

    explicit EconomyService(DbWorker& worker) : db(worker) {}

    // 상점·보관함 명령(패킷 210). done은 DB 스레드에서 불린다.
    void Request(const Member& who, const Economy::Command& command, StateDone done);

    // 출격: 전원 장비 예약 → createSession → done(성공 여부). 실패하면 예약을 되돌린다.
    void BeginRun(int sessionId, std::vector<Member> members,
        CreateSession createSession, std::function<void(bool)> done);

    // 세션의 정산 보고. 탈출이면 items 입고, refund면 가져간 장비 반환.
    void FinishRun(int sessionId, const std::string& token, int clientId,
        bool refund, std::map<int, int> items);

    // 세션 종료 보고 뒤: 정산 보고가 안 온 인원은 장비를 돌려주고 판을 닫는다.
    void EndRun(int sessionId);

private:
    struct Run { std::string token; std::map<int, Member> members; };   // clientId → 계정
    using Op = std::function<Economy::Result(Economy::Account&)>;

    DbWorker&          db;
    std::mutex         runsMtx;
    std::map<int, Run> runs;      // sessionId → 진행 중인 판

    // DB 스레드 전용: 계정 1개 읽기 → op → 저장
    Economy::Result Transact(OdbcSession* odbc, const Member& who, const Op& op, Economy::Account* out);
    // 끝난 판(세션 종료·로비 재시작)에 묶인 계정이면 장비를 돌려주고 푼다
    void RecoverIfEnded(Economy::Account& a);
};
