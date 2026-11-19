#pragma once
#define WIN32_LEAN_AND_MEAN
#include <WinSock2.h>
#include <Windows.h>
#include <sql.h>
#include <sqlext.h>

#include <string>
#include <utility>
#include <vector>

// ============================================================================
//  OdbcSession - ODBC 핸들 1세트(환경/연결)를 들고 저장 프로시저를 호출한다.
//
//  강의자료 메모 반영:
//   - SQLConnect는 비싸다 → 핸들을 계속 들고 재사용한다.
//   - 멀티스레드면 핸들마다 락이 필요하다 → 이 객체는 DbWorker 스레드
//     한 곳에서만 쓴다. 그래서 내부에 락이 없다. 다른 스레드에서 직접
//     호출하지 말 것.
//   - 한글 때문에 SQLCHAR가 아니라 SQLWCHAR를 쓴다.
// ============================================================================
class OdbcSession
{
public:
    ~OdbcSession();

    // 접속. RetryDB가 없으면 만들고, 표·프로시저를 서버 버전에 맞춘다 (여러 번 해도 안전).
    // 실패 시 진단 메시지를 로그로 남긴다.
    bool Connect();
    void Disconnect();
    bool IsConnected() const { return hdbc_ != SQL_NULL_HDBC && connected_; }

    // 끊겼으면 한 번 재접속을 시도한다. (핸들 기본 타임아웃 8시간 대비)
    bool EnsureConnected();

    // DB만 준비하고 끝낼 때 (--db-init / --db-reset). reset=true면 RetryDB를 지우고 새로 만든다.
    bool Setup(bool reset);

    // ── 저장 프로시저 호출 ────────────────────────────────────
    // 로그인. 반환 result: 0=성공, 1=없는 닉네임, 2=비밀번호 불일치, -1=DB 오류
    int LoginPlayer(const std::wstring& nickname, const std::wstring& password,
        int& outPlayerId, int& outRuns, int& outExtractions);

    // 회원가입. 0=성공, 1=닉네임 중복, -1=DB 오류
    int RegisterPlayer(const std::wstring& nickname, const std::wstring& password);

    // 비밀번호 재설정. 0=성공, 1=없는 닉네임, -1=DB 오류
    int ResetPassword(const std::wstring& nickname, const std::wstring& password);

    // 전적 조회. 실패면 false.
    bool GetPlayerStats(const std::wstring& nickname,
        int& runs, int& extractions, int& monsterKills,
        int& playerKills, int& bestSeconds);

    // 전적 1줄 기록.
    bool InsertPlayRecord(const std::wstring& nickname, int sessionId,
        bool extracted, int playSeconds,
        int monsterKills, int playerKills);

    // ── 상점·보관함 ────────────────────────────────────────────
    struct StashRow
    {
        long long credits = 300;
        int weapon = 0, item = 0;
        std::string run, lastRun;                   // 진행 중인 판 / 마지막으로 정산한 판
        int carriedWeapon = 0, carriedItem = 0, carriedCount = 0;
        std::vector<std::pair<int, int>> items;     // (아이템 해시, 개수)
    };
    // 보관함 읽기. 처음이면 기본값으로 만든다. 실패면 false.
    bool LoadStash(int playerId, StashRow& out);
    // 보관함 저장. 아이템 목록은 통째로 바꾼다 (한 트랜잭션). 실패면 false.
    bool SaveStash(int playerId, const StashRow& row);

private:
    SQLHENV  henv_ = SQL_NULL_HENV;
    SQLHDBC  hdbc_ = SQL_NULL_HDBC;
    bool     connected_ = false;

    bool AllocEnv();
    bool ConnectTo(const wchar_t* database, bool* dbMissing);   // dbMissing: 서버는 켜져 있는데 DB가 없음
    bool CreateDatabase(bool reset);                            // master에서 RetryDB 만들기 (reset이면 지우고)
    bool PrepareSchema();                                       // 표·프로시저 만들기/최신화
    bool LoadStashOnce(int playerId, StashRow& out);
    bool SaveStashOnce(int playerId, const StashRow& row);
    bool schemaOk_ = false;
    bool schemaMissing_ = false;    // 마지막 호출이 "표/프로시저 없음"으로 실패했나
    // 회원가입/비밀번호 재설정은 호출 모양이 같아 하나로 처리한다.
    int  CallAccountProc(const wchar_t* call,
        const std::wstring& nickname, const std::wstring& password);
    // 실패한 ODBC 호출의 원인을 로그로 출력.
    static void DumpDiagnostics(const char* where, SQLHANDLE h, SQLSMALLINT type);
    // 통신이 끊긴 종류의 오류인지 판단(재접속 대상).
    static bool IsConnectionLost(SQLHANDLE h, SQLSMALLINT type);
};

// UTF-8(char) ↔ UTF-16(wchar_t) 변환. 서버는 이름을 UTF-8로 들고 있고
// ODBC는 UTF-16을 받으므로 경계에서 변환이 필요하다.
std::wstring Utf8ToWide(const char* s);
std::string  WideToUtf8(const wchar_t* s);
