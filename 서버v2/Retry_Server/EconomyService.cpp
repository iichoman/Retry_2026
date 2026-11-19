#include "EconomyService.h"
#include "../DB/DbWorker.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

static_assert(Economy::Capacity == IPC_ECONOMY_ITEMS, "정산 IPC 아이템 수 = 보관함 크기");

namespace
{
    Economy::Account FromRow(const OdbcSession::StashRow& r)
    {
        Economy::Account a;
        a.credits = r.credits;
        a.weapon = r.weapon;
        a.item = r.item;
        a.run = r.run;
        a.lastRun = r.lastRun;
        a.carriedWeapon = r.carriedWeapon;
        a.carriedItem = r.carriedItem;
        a.carriedCount = r.carriedCount;
        for (const auto& entry : r.items) a.stash[entry.first] = entry.second;
        return a;
    }

    OdbcSession::StashRow ToRow(const Economy::Account& a)
    {
        OdbcSession::StashRow r;
        r.credits = a.credits;
        r.weapon = a.weapon;
        r.item = a.item;
        r.run = a.run;
        r.lastRun = a.lastRun;
        r.carriedWeapon = a.carriedWeapon;
        r.carriedItem = a.carriedItem;
        r.carriedCount = a.carriedCount;
        for (const auto& entry : a.stash) r.items.emplace_back(entry.first, entry.second);
        return r;
    }
}

Economy::Result EconomyService::Transact(OdbcSession* odbc, const Member& who,
    const Op& op, Economy::Account* out)
{
    // DB 없이 들어온 계정 → 파일 저장소
    if (who.dbPlayerId <= 0)
        return Economy::Transact(Economy::Key(0, who.name), op, out);

    // DB 계정인데 DB가 끊겼으면 저장하지 않는다 (파일로 갈라지면 둘이 어긋난다)
    if (!odbc) return Economy::StorageError;
    try
    {
        OdbcSession::StashRow row;
        if (!odbc->LoadStash(who.dbPlayerId, row)) return Economy::StorageError;
        Economy::Account a = FromRow(row);
        if (!Economy::Normalize(a)) return Economy::StorageError;

        Economy::Result status = op(a);
        if (status == Economy::Ok && !odbc->SaveStash(who.dbPlayerId, ToRow(a)))
            return Economy::StorageError;
        if (out) *out = a;
        return status;
    }
    catch (...) { return Economy::StorageError; }    // 보관함 한도 초과 등
}

void EconomyService::RecoverIfEnded(Economy::Account& a)
{
    if (a.run.empty()) return;
    {
        std::lock_guard<std::mutex> lk(runsMtx);
        for (const auto& kv : runs)
            if (kv.second.token == a.run) return;   // 아직 진행 중인 판
    }
    Economy::Recover(a);
}

void EconomyService::Request(const Member& who, const Economy::Command& command, StateDone done)
{
    db.Post([this, who, command, done](OdbcSession* odbc)
        {
            Economy::Account account;
            Economy::Result status = Transact(odbc, who, [&](Economy::Account& a)
                {
                    RecoverIfEnded(a);
                    return Economy::Apply(a, command);
                }, &account);
            if (done) done(status, account);
        });
}

void EconomyService::BeginRun(int sessionId, std::vector<Member> members,
    CreateSession createSession, std::function<void(bool)> done)
{
    db.Post([this, sessionId, members, createSession, done](OdbcSession* odbc)
        {
            const std::string token = Economy::RunToken(sessionId);
            {
                // 예약 전에 등록: 이 판이 "끝난 판"으로 잘못 정리되지 않게
                std::lock_guard<std::mutex> lk(runsMtx);
                Run& run = runs[sessionId];
                run.token = token;
                for (const Member& m : members) run.members[m.clientId] = m;
            }

            // 1) 전원 장비 예약. 보관함을 못 쓰는 사람(DB 오류 등)은 출격을 막지 않고
            //    그 사람만 기본 장비로 보낸다 (이번 판은 저장하지 않는다)
            std::vector<Economy::Grant> grants(members.size());
            std::vector<bool> reserved(members.size(), false);
            for (size_t i = 0; i < members.size(); ++i)
            {
                Economy::Grant& grant = grants[i];
                Economy::Result status = Transact(odbc, members[i], [&](Economy::Account& a)
                    {
                        RecoverIfEnded(a);
                        return Economy::Reserve(a, token, grant);
                    }, nullptr);
                reserved[i] = status == Economy::Ok;
                if (!reserved[i])
                {
                    Log::Warn("[Economy] 보관함을 못 써서 기본 장비로 출격 (이번 판 저장 안 함): name=%s result=%d",
                        members[i].name.c_str(), (int)status);
                    grant = Economy::StarterGrant();
                    std::lock_guard<std::mutex> lk(runsMtx);
                    runs[sessionId].members.erase(members[i].clientId);
                }
            }

            // 2) 세션 생성 요청 (로비 락 없이 여기서 기다린다)
            bool ok = createSession && createSession(token, grants);
            if (!ok)
            {
                // 예약한 장비를 그대로 돌려준다
                for (size_t i = 0; i < members.size(); ++i)
                    if (reserved[i])
                        Transact(odbc, members[i], [&](Economy::Account& a)
                            { return Economy::Settle(a, token, {}, true); }, nullptr);
                std::lock_guard<std::mutex> lk(runsMtx);
                runs.erase(sessionId);
            }
            if (done) done(ok);
        });
}

void EconomyService::FinishRun(int sessionId, const std::string& token, int clientId,
    bool refund, std::map<int, int> items)
{
    db.Post([this, sessionId, token, clientId, refund, items](OdbcSession* odbc)
        {
            Member who;
            {
                std::lock_guard<std::mutex> lk(runsMtx);
                auto run = runs.find(sessionId);
                if (run == runs.end() || run->second.token != token)
                {
                    Log::Warn("[Economy] 모르는 판의 정산 보고 무시: sid=%d cid=%d", sessionId, clientId);
                    return;
                }
                auto member = run->second.members.find(clientId);
                if (member == run->second.members.end()) return;     // 이미 정산됨
                who = member->second;
                run->second.members.erase(member);
            }

            Economy::Result status = Transact(odbc, who, [&](Economy::Account& a)
                { return Economy::Settle(a, token, items, refund); }, nullptr);
            if (status == Economy::Ok)
                Log::Info("[Economy] 정산: name=%s sid=%d 반환=%d 입고=%d종",
                    who.name.c_str(), sessionId, refund ? 1 : 0, (int)items.size());
            else
                Log::Error("[Economy] 정산 저장 실패: name=%s sid=%d result=%d",
                    who.name.c_str(), sessionId, (int)status);
        });
}

void EconomyService::EndRun(int sessionId)
{
    db.Post([this, sessionId](OdbcSession* odbc)
        {
            Run run;
            {
                std::lock_guard<std::mutex> lk(runsMtx);
                auto it = runs.find(sessionId);
                if (it == runs.end()) return;
                run = std::move(it->second);
                runs.erase(it);
            }
            // 정산 보고가 안 온 인원(보고 유실) → 가져간 장비를 돌려준다
            for (const auto& entry : run.members)
            {
                const Member& who = entry.second;
                Log::Warn("[Economy] 정산 보고 없음 → 장비 반환: name=%s sid=%d", who.name.c_str(), sessionId);
                Transact(odbc, who, [&](Economy::Account& a)
                    { return Economy::Settle(a, run.token, {}, true); }, nullptr);
            }
        });
}
