#include "OdbcSession.h"
#include "DbConfig.h"
#include "RetryDBSchema.h"
#include "../Common/Logger.h"

#include <cstdint>
#include <cstring>
#include <cwchar>
#include <vector>
#include <initializer_list>

#pragma comment(lib, "odbc32.lib")

// ============================================================================
//  문자열 변환
// ============================================================================
std::wstring Utf8ToWide(const char* s)
{
    if (!s || !*s) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, 0, s, -1, nullptr, 0);
    if (n <= 1) return std::wstring();
    std::wstring w((size_t)n - 1, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s, -1, &w[0], n);
    return w;
}

std::string WideToUtf8(const wchar_t* s)
{
    if (!s || !*s) return std::string();
    int n = WideCharToMultiByte(CP_UTF8, 0, s, -1, nullptr, 0, nullptr, nullptr);
    if (n <= 1) return std::string();
    std::string u((size_t)n - 1, '\0');
    WideCharToMultiByte(CP_UTF8, 0, s, -1, &u[0], n, nullptr, nullptr);
    return u;
}

static bool Ok(SQLRETURN r)
{
    return r == SQL_SUCCESS || r == SQL_SUCCESS_WITH_INFO;
}

// 진단 레코드에 이 SQL Server 오류 번호가 있나 (4060 DB 없음, 208 개체 없음, 2812 프로시저 없음)
static bool HasNativeError(SQLHANDLE h, SQLSMALLINT type, std::initializer_list<int> codes)
{
    SQLSMALLINT rec = 0;
    SQLINTEGER  native = 0;
    SQLWCHAR    state[8] = {};
    SQLWCHAR    msg[512] = {};
    SQLSMALLINT len = 0;
    while (SQLGetDiagRecW(type, h, ++rec, state, &native, msg,
        (SQLSMALLINT)(sizeof(msg) / sizeof(SQLWCHAR)), &len) == SQL_SUCCESS)
    {
        for (int code : codes) if (native == code) return true;
        if (rec >= 8) break;
    }
    return false;
}

// ============================================================================
//  진단
// ============================================================================
void OdbcSession::DumpDiagnostics(const char* where, SQLHANDLE h, SQLSMALLINT type)
{
    SQLSMALLINT rec = 0;
    SQLINTEGER  native = 0;
    SQLWCHAR    state[8] = {};
    SQLWCHAR    msg[512] = {};
    SQLSMALLINT len = 0;

    while (SQLGetDiagRecW(type, h, ++rec, state, &native, msg,
        (SQLSMALLINT)(sizeof(msg) / sizeof(SQLWCHAR)), &len) == SQL_SUCCESS)
    {
        Log::Error("[DB] %s : [%s] %s (%d)",
            where, WideToUtf8(state).c_str(), WideToUtf8(msg).c_str(), (int)native);
        if (rec >= 4) break;      // 너무 길어지지 않게
    }
}

bool OdbcSession::IsConnectionLost(SQLHANDLE h, SQLSMALLINT type)
{
    SQLSMALLINT rec = 0;
    SQLINTEGER  native = 0;
    SQLWCHAR    state[8] = {};
    SQLWCHAR    msg[512] = {};
    SQLSMALLINT len = 0;

    while (SQLGetDiagRecW(type, h, ++rec, state, &native, msg,
        (SQLSMALLINT)(sizeof(msg) / sizeof(SQLWCHAR)), &len) == SQL_SUCCESS)
    {
        // 08S01 통신 링크 실패 / 08003 연결 없음 / HYT00 타임아웃
        if (wcsncmp(state, L"08", 2) == 0) return true;
        if (wcscmp(state, L"HYT00") == 0)  return true;
        if (rec >= 4) break;
    }
    return false;
}

// ============================================================================
//  연결
// ============================================================================
OdbcSession::~OdbcSession()
{
    Disconnect();
    if (henv_ != SQL_NULL_HENV) { SQLFreeHandle(SQL_HANDLE_ENV, henv_); henv_ = SQL_NULL_HENV; }
}

bool OdbcSession::AllocEnv()
{
    if (henv_ != SQL_NULL_HENV) return true;

    if (!Ok(SQLAllocHandle(SQL_HANDLE_ENV, SQL_NULL_HANDLE, &henv_)))
    {
        Log::Error("[DB] 환경 핸들 할당 실패");
        return false;
    }
    if (!Ok(SQLSetEnvAttr(henv_, SQL_ATTR_ODBC_VERSION, (SQLPOINTER)SQL_OV_ODBC3, 0)))
    {
        DumpDiagnostics("SQLSetEnvAttr", henv_, SQL_HANDLE_ENV);
        return false;
    }
    return true;
}

bool OdbcSession::ConnectTo(const wchar_t* database, bool* dbMissing)
{
    if (dbMissing) *dbMissing = false;
    if (IsConnected()) return true;
    if (!AllocEnv()) return false;

    if (hdbc_ == SQL_NULL_HDBC &&
        !Ok(SQLAllocHandle(SQL_HANDLE_DBC, henv_, &hdbc_)))
    {
        DumpDiagnostics("SQLAllocHandle(DBC)", henv_, SQL_HANDLE_ENV);
        return false;
    }

    SQLSetConnectAttr(hdbc_, SQL_LOGIN_TIMEOUT,
        (SQLPOINTER)(intptr_t)DB_LOGIN_TIMEOUT_SEC, 0);

    SQLRETURN r;
    if (DB_DSN[0] != L'\0')
    {
        // DSN 방식 (강의자료와 동일)
        r = SQLConnectW(hdbc_,
            (SQLWCHAR*)DB_DSN, SQL_NTS,
            (SQLWCHAR*)DB_USER, SQL_NTS,
            (SQLWCHAR*)DB_PASSWORD, SQL_NTS);
    }
    else
    {
        // DSN 없이 직접. 드라이버 이름은 설치된 것 중 최신부터 시도한다.
        static const wchar_t* kDrivers[] = {
            L"ODBC Driver 18 for SQL Server",
            L"ODBC Driver 17 for SQL Server",
            L"SQL Server",
        };

        r = SQL_ERROR;
        for (const wchar_t* drv : kDrivers)
        {
            std::wstring cs;
            cs += L"Driver={"; cs += drv; cs += L"};";
            cs += L"Server="; cs += DbServer(); cs += L";";
            cs += L"Database="; cs += database; cs += L";";
            if (DB_USER[0] == L'\0')
                cs += L"Trusted_Connection=yes;";
            else
            {
                cs += L"Uid="; cs += DB_USER; cs += L";";
                cs += L"Pwd="; cs += DB_PASSWORD; cs += L";";
            }
            // 드라이버 18은 암호화가 기본 필수라 로컬 자체서명 인증서에서 막힌다.
            cs += L"Encrypt=optional;TrustServerCertificate=yes;";

            r = SQLDriverConnectW(hdbc_, nullptr,
                (SQLWCHAR*)cs.c_str(), SQL_NTS,
                nullptr, 0, nullptr, SQL_DRIVER_NOPROMPT);
            if (Ok(r))
            {
                Log::Info("[DB] 드라이버 '%s' 로 접속", WideToUtf8(drv).c_str());
                break;
            }
            // 서버는 켜져 있는데 DB만 없다 (4060) → 만들면 된다
            if (dbMissing && HasNativeError(hdbc_, SQL_HANDLE_DBC, { 4060 })) *dbMissing = true;
        }
    }

    if (!Ok(r))
    {
        if (!(dbMissing && *dbMissing)) DumpDiagnostics("SQLConnect", hdbc_, SQL_HANDLE_DBC);
        return false;
    }

    connected_ = true;
    Log::Info("[DB] 접속 성공 (%s / %s)",
        DB_DSN[0] ? WideToUtf8(DB_DSN).c_str() : WideToUtf8(DbServer()).c_str(),
        WideToUtf8(database).c_str());
    return true;
}

bool OdbcSession::Connect()
{
    if (IsConnected()) return true;
    bool dbMissing = false;
    if (!ConnectTo(DbName(), &dbMissing))
    {
        if (!dbMissing) return false;
        // SQL Server는 켜져 있는데 RetryDB만 없다 → 만들고 다시 붙는다
        Log::Warn("[DB] %s 가 없어 새로 만든다", WideToUtf8(DbName()).c_str());
        if (!CreateDatabase(false) || !ConnectTo(DbName(), nullptr)) return false;
    }
    schemaOk_ = PrepareSchema();      // 빠진 표·프로시저를 채우고 최신으로 맞춘다
    return true;
}

bool OdbcSession::CreateDatabase(bool reset)
{
    if (DB_DSN[0] != L'\0') return false;           // DSN 방식은 DB가 DSN에 정해져 있다

    OdbcSession master;                             // master에 따로 붙어서 처리하고 끊는다
    if (!master.ConnectTo(L"master", nullptr)) return false;

    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, master.hdbc_, &stmt))) return false;

    SQLWCHAR name[129] = {};
    SQLLEN   nameLen = SQL_NTS;
    SQLCHAR  doReset = reset ? 1 : 0;
    wcsncpy_s(name, DbName(), _TRUNCATE);
    SQLBindParameter(stmt, 1, SQL_PARAM_INPUT, SQL_C_WCHAR, SQL_WVARCHAR, 128, 0,
        name, sizeof(name), &nameLen);
    SQLBindParameter(stmt, 2, SQL_PARAM_INPUT, SQL_C_BIT, SQL_BIT, 0, 0, &doReset, 0, nullptr);

    // DB 이름은 QUOTENAME으로 감싸 넣는다 (db.ini 값이 SQL로 해석되지 않게)
    SQLRETURN r = SQLExecDirectW(stmt, (SQLWCHAR*)
        L"DECLARE @n sysname = ?, @reset bit = ?, @q nvarchar(300); "
        L"SET @q = QUOTENAME(@n); "
        L"IF @reset = 1 AND DB_ID(@n) IS NOT NULL "
        L"BEGIN "
        L"  EXEC(N'ALTER DATABASE ' + @q + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE'); "
        L"  EXEC(N'DROP DATABASE ' + @q); "
        L"END; "
        L"IF DB_ID(@n) IS NULL EXEC(N'CREATE DATABASE ' + @q);", SQL_NTS);
    while (Ok(r)) r = SQLMoreResults(stmt);
    bool ok = (r == SQL_NO_DATA);
    if (!ok) DumpDiagnostics("CREATE DATABASE", stmt, SQL_HANDLE_STMT);
    else Log::Info(reset ? "[DB] %s 를 지우고 새로 만들었다" : "[DB] %s 준비됨", WideToUtf8(DbName()).c_str());

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return ok;
}

bool OdbcSession::PrepareSchema()
{
    int failed = 0;
    for (const wchar_t* batch : kRetryDbSchema)
    {
        SQLHSTMT stmt = SQL_NULL_HSTMT;
        if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return false;

        SQLRETURN r = SQLExecDirectW(stmt, (SQLWCHAR*)batch, SQL_NTS);
        while (Ok(r)) r = SQLMoreResults(stmt);     // 결과를 끝까지 받아야 뒤쪽 문장의 오류도 보인다
        if (r != SQL_NO_DATA)
        {
            DumpDiagnostics("스키마 준비", stmt, SQL_HANDLE_STMT);
            if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
            ++failed;
        }
        SQLFreeHandle(SQL_HANDLE_STMT, stmt);
        if (!connected_) break;
    }
    if (failed) Log::Warn("[DB] 스키마 준비 중 %d개 실패 (위 오류 확인)", failed);
    else Log::Info("[DB] 스키마 준비 완료 (표·프로시저 최신)");
    return failed == 0;
}

bool OdbcSession::Setup(bool reset)
{
    Disconnect();
    if (reset && !CreateDatabase(true)) return false;
    return Connect() && schemaOk_;
}

void OdbcSession::Disconnect()
{
    if (hdbc_ != SQL_NULL_HDBC)
    {
        if (connected_) SQLDisconnect(hdbc_);
        SQLFreeHandle(SQL_HANDLE_DBC, hdbc_);
        hdbc_ = SQL_NULL_HDBC;
    }
    connected_ = false;
}

bool OdbcSession::EnsureConnected()
{
    if (IsConnected()) return true;
    Disconnect();
    return Connect();
}

// ============================================================================
//  저장 프로시저 호출
// ============================================================================
// 문자열 파라미터 바인딩 헬퍼. 버퍼는 호출자가 살려 둬야 한다.
static void BindText(SQLHSTMT stmt, SQLUSMALLINT idx,
    SQLWCHAR* buf, SQLLEN bufBytes, SQLLEN* lenInd, int columnSize)
{
    *lenInd = SQL_NTS;
    SQLBindParameter(stmt, idx, SQL_PARAM_INPUT, SQL_C_WCHAR, SQL_WVARCHAR,
        columnSize, 0, buf, bufBytes, lenInd);
}

int OdbcSession::LoginPlayer(const std::wstring& nickname, const std::wstring& password,
    int& outPlayerId, int& outRuns, int& outExtractions)
{
    outPlayerId = 0; outRuns = 0; outExtractions = 0;
    if (!EnsureConnected()) return -1;

    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return -1;

    SQLWCHAR nickBuf[64] = {}, pwBuf[128] = {};
    SQLLEN   nickLen = 0, pwLen = 0;
    wcsncpy_s(nickBuf, nickname.c_str(), _TRUNCATE);
    wcsncpy_s(pwBuf, password.c_str(), _TRUNCATE);
    BindText(stmt, 1, nickBuf, sizeof(nickBuf), &nickLen, 16);
    BindText(stmt, 2, pwBuf, sizeof(pwBuf), &pwLen, 64);

    int result = -1;
    SQLRETURN r = SQLExecDirectW(stmt,
        (SQLWCHAR*)L"{CALL dbo.usp_LoginPlayer(?,?)}", SQL_NTS);

    if (Ok(r))
    {
        SQLINTEGER res = -1, pid = 0, runs = 0, ext = 0;
        SQLLEN l0 = 0, l1 = 0, l2 = 0, l3 = 0;
        SQLBindCol(stmt, 1, SQL_C_SLONG, &res, 0, &l0);
        SQLBindCol(stmt, 2, SQL_C_SLONG, &pid, 0, &l1);
        SQLBindCol(stmt, 3, SQL_C_SLONG, &runs, 0, &l2);
        SQLBindCol(stmt, 4, SQL_C_SLONG, &ext, 0, &l3);

        if (SQLFetch(stmt) == SQL_SUCCESS)
        {
            result = (int)res;
            outPlayerId = (int)pid;
            outRuns = (int)runs;
            outExtractions = (int)ext;
        }
        SQLCloseCursor(stmt);
    }
    else
    {
        DumpDiagnostics("usp_LoginPlayer", stmt, SQL_HANDLE_STMT);
        if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
    }

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return result;
}

// 회원가입 / 비밀번호 재설정은 호출 모양이 같아 한 함수로 처리한다.
int OdbcSession::CallAccountProc(const wchar_t* call,
    const std::wstring& nickname, const std::wstring& password)
{
    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return -1;

    SQLWCHAR nickBuf[64] = {}, pwBuf[128] = {};
    SQLLEN   nickLen = 0, pwLen = 0;
    wcsncpy_s(nickBuf, nickname.c_str(), _TRUNCATE);
    wcsncpy_s(pwBuf, password.c_str(), _TRUNCATE);
    BindText(stmt, 1, nickBuf, sizeof(nickBuf), &nickLen, 16);
    BindText(stmt, 2, pwBuf, sizeof(pwBuf), &pwLen, 64);

    int result = -1;
    SQLRETURN r = SQLExecDirectW(stmt, (SQLWCHAR*)call, SQL_NTS);
    if (Ok(r))
    {
        SQLINTEGER res = -1;
        SQLLEN l0 = 0;
        SQLBindCol(stmt, 1, SQL_C_SLONG, &res, 0, &l0);
        if (SQLFetch(stmt) == SQL_SUCCESS) result = (int)res;
        SQLCloseCursor(stmt);
    }
    else
    {
        DumpDiagnostics("account proc", stmt, SQL_HANDLE_STMT);
        if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
    }

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return result;
}

int OdbcSession::RegisterPlayer(const std::wstring& nickname, const std::wstring& password)
{
    if (!EnsureConnected()) return -1;
    return CallAccountProc(L"{CALL dbo.usp_RegisterPlayer(?,?)}", nickname, password);
}

int OdbcSession::ResetPassword(const std::wstring& nickname, const std::wstring& password)
{
    if (!EnsureConnected()) return -1;
    return CallAccountProc(L"{CALL dbo.usp_ResetPassword(?,?)}", nickname, password);
}

bool OdbcSession::GetPlayerStats(const std::wstring& nickname,
    int& runs, int& extractions, int& monsterKills,
    int& playerKills, int& bestSeconds)
{
    runs = extractions = monsterKills = playerKills = bestSeconds = 0;
    if (!EnsureConnected()) return false;

    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return false;

    SQLWCHAR nickBuf[64] = {};
    SQLLEN   nickLen = 0;
    wcsncpy_s(nickBuf, nickname.c_str(), _TRUNCATE);
    BindText(stmt, 1, nickBuf, sizeof(nickBuf), &nickLen, 16);

    bool ok = false;
    SQLRETURN r = SQLExecDirectW(stmt,
        (SQLWCHAR*)L"{CALL dbo.usp_GetPlayerStats(?)}", SQL_NTS);

    if (Ok(r))
    {
        SQLINTEGER a = 0, b = 0, c = 0, e = 0, f = 0;
        SQLLEN l[5] = {};
        SQLBindCol(stmt, 1, SQL_C_SLONG, &a, 0, &l[0]);
        SQLBindCol(stmt, 2, SQL_C_SLONG, &b, 0, &l[1]);
        SQLBindCol(stmt, 3, SQL_C_SLONG, &c, 0, &l[2]);
        SQLBindCol(stmt, 4, SQL_C_SLONG, &e, 0, &l[3]);
        SQLBindCol(stmt, 5, SQL_C_SLONG, &f, 0, &l[4]);

        if (SQLFetch(stmt) == SQL_SUCCESS)
        {
            runs = (int)a; extractions = (int)b;
            monsterKills = (int)c; playerKills = (int)e; bestSeconds = (int)f;
        }
        // 한 판도 안 했으면 결과 행이 없다. 그것도 성공으로 본다(전부 0).
        ok = true;
        SQLCloseCursor(stmt);
    }
    else
    {
        DumpDiagnostics("usp_GetPlayerStats", stmt, SQL_HANDLE_STMT);
        if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
    }

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return ok;
}

bool OdbcSession::InsertPlayRecord(const std::wstring& nickname, int sessionId,
    bool extracted, int playSeconds,
    int monsterKills, int playerKills)
{
    if (!EnsureConnected()) return false;

    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return false;

    SQLWCHAR   nickBuf[64] = {};
    SQLLEN     nickLen = SQL_NTS;
    wcsncpy_s(nickBuf, nickname.c_str(), _TRUNCATE);

    SQLINTEGER pSid = sessionId;
    SQLCHAR    pExt = extracted ? 1 : 0;
    SQLINTEGER pSec = playSeconds;
    SQLINTEGER pMk = monsterKills;
    SQLINTEGER pPk = playerKills;

    SQLBindParameter(stmt, 1, SQL_PARAM_INPUT, SQL_C_WCHAR, SQL_WVARCHAR, 16, 0,
        nickBuf, sizeof(nickBuf), &nickLen);
    SQLBindParameter(stmt, 2, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &pSid, 0, nullptr);
    SQLBindParameter(stmt, 3, SQL_PARAM_INPUT, SQL_C_BIT, SQL_BIT, 0, 0, &pExt, 0, nullptr);
    SQLBindParameter(stmt, 4, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &pSec, 0, nullptr);
    SQLBindParameter(stmt, 5, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &pMk, 0, nullptr);
    SQLBindParameter(stmt, 6, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &pPk, 0, nullptr);

    SQLRETURN r = SQLExecDirectW(stmt,
        (SQLWCHAR*)L"{CALL dbo.usp_InsertPlayRecord(?,?,?,?,?,?)}", SQL_NTS);

    bool ok = Ok(r) || r == SQL_NO_DATA;
    if (!ok)
    {
        DumpDiagnostics("usp_InsertPlayRecord", stmt, SQL_HANDLE_STMT);
        if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
    }

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return ok;
}

// ============================================================================
//  상점·보관함
// ============================================================================
bool OdbcSession::LoadStashOnce(int playerId, StashRow& out)
{
    out = StashRow{};
    if (!EnsureConnected()) return false;

    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return false;

    SQLINTEGER pid = playerId;
    SQLBindParameter(stmt, 1, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &pid, 0, nullptr);

    bool ok = false;
    SQLRETURN r = SQLExecDirectW(stmt, (SQLWCHAR*)L"{CALL dbo.usp_LoadStash(?)}", SQL_NTS);
    if (Ok(r))
    {
        // 결과 1: 계정 1줄
        SQLBIGINT  credits = 0;
        SQLINTEGER weapon = 0, item = 0, cw = 0, ci = 0, cc = 0;
        SQLCHAR    run[65] = {}, last[65] = {};
        SQLLEN     len[8] = {};
        SQLBindCol(stmt, 1, SQL_C_SBIGINT, &credits, 0, &len[0]);
        SQLBindCol(stmt, 2, SQL_C_SLONG, &weapon, 0, &len[1]);
        SQLBindCol(stmt, 3, SQL_C_SLONG, &item, 0, &len[2]);
        SQLBindCol(stmt, 4, SQL_C_CHAR, run, sizeof(run), &len[3]);
        SQLBindCol(stmt, 5, SQL_C_CHAR, last, sizeof(last), &len[4]);
        SQLBindCol(stmt, 6, SQL_C_SLONG, &cw, 0, &len[5]);
        SQLBindCol(stmt, 7, SQL_C_SLONG, &ci, 0, &len[6]);
        SQLBindCol(stmt, 8, SQL_C_SLONG, &cc, 0, &len[7]);
        if (Ok(SQLFetch(stmt)))
        {
            out.credits = (long long)credits;
            out.weapon = (int)weapon;
            out.item = (int)item;
            out.run = (const char*)run;
            out.lastRun = (const char*)last;
            out.carriedWeapon = (int)cw;
            out.carriedItem = (int)ci;
            out.carriedCount = (int)cc;
            ok = true;
        }

        // 결과 2: 아이템 목록
        SQLFreeStmt(stmt, SQL_UNBIND);
        if (ok && Ok(SQLMoreResults(stmt)))
        {
            SQLINTEGER hash = 0, count = 0;
            SQLLEN     lh = 0, lc = 0;
            SQLBindCol(stmt, 1, SQL_C_SLONG, &hash, 0, &lh);
            SQLBindCol(stmt, 2, SQL_C_SLONG, &count, 0, &lc);
            while (Ok(SQLFetch(stmt)))
                out.items.emplace_back((int)hash, (int)count);
        }
        else ok = false;
        SQLCloseCursor(stmt);
    }
    if (!ok)
    {
        schemaMissing_ = HasNativeError(stmt, SQL_HANDLE_STMT, { 208, 2812 });
        DumpDiagnostics("usp_LoadStash", stmt, SQL_HANDLE_STMT);
        if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
    }

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return ok;
}

bool OdbcSession::SaveStashOnce(int playerId, const StashRow& row)
{
    if (!EnsureConnected()) return false;

    // 아이템은 <i h="해시" c="개수"/> 를 이어 붙여 한 번에 넘긴다. 프로시저가 표로 풀어 통째로 바꾼다.
    std::string items;
    for (const auto& entry : row.items)
        items += "<i h=\"" + std::to_string(entry.first) + "\" c=\"" + std::to_string(entry.second) + "\"/>";
    if (items.size() > 8000) return false;          // 96종 x 31자라 넘을 일 없다

    SQLHSTMT stmt = SQL_NULL_HSTMT;
    if (!Ok(SQLAllocHandle(SQL_HANDLE_STMT, hdbc_, &stmt))) return false;

    SQLINTEGER pid = playerId, weapon = row.weapon, item = row.item;
    SQLINTEGER cw = row.carriedWeapon, ci = row.carriedItem, cc = row.carriedCount;
    SQLBIGINT  credits = row.credits;
    std::string run = row.run.substr(0, 64), last = row.lastRun.substr(0, 64);
    SQLLEN     runLen = SQL_NTS, lastLen = SQL_NTS, itemsLen = SQL_NTS;

    SQLBindParameter(stmt, 1, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &pid, 0, nullptr);
    SQLBindParameter(stmt, 2, SQL_PARAM_INPUT, SQL_C_SBIGINT, SQL_BIGINT, 0, 0, &credits, 0, nullptr);
    SQLBindParameter(stmt, 3, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &weapon, 0, nullptr);
    SQLBindParameter(stmt, 4, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &item, 0, nullptr);
    SQLBindParameter(stmt, 5, SQL_PARAM_INPUT, SQL_C_CHAR, SQL_VARCHAR, 64, 0, (SQLPOINTER)run.c_str(), 0, &runLen);
    SQLBindParameter(stmt, 6, SQL_PARAM_INPUT, SQL_C_CHAR, SQL_VARCHAR, 64, 0, (SQLPOINTER)last.c_str(), 0, &lastLen);
    SQLBindParameter(stmt, 7, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &cw, 0, nullptr);
    SQLBindParameter(stmt, 8, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &ci, 0, nullptr);
    SQLBindParameter(stmt, 9, SQL_PARAM_INPUT, SQL_C_SLONG, SQL_INTEGER, 0, 0, &cc, 0, nullptr);
    SQLBindParameter(stmt, 10, SQL_PARAM_INPUT, SQL_C_CHAR, SQL_VARCHAR, 8000, 0, (SQLPOINTER)items.c_str(), 0, &itemsLen);

    bool ok = false;
    SQLRETURN r = SQLExecDirectW(stmt,
        (SQLWCHAR*)L"{CALL dbo.usp_SaveStash(?,?,?,?,?,?,?,?,?,?)}", SQL_NTS);
    if (Ok(r))
    {
        // 마지막 SELECT 0 까지 왔으면 커밋된 것
        SQLINTEGER res = -1;
        SQLLEN     l0 = 0;
        SQLBindCol(stmt, 1, SQL_C_SLONG, &res, 0, &l0);
        ok = Ok(SQLFetch(stmt)) && res == 0;
        SQLCloseCursor(stmt);
    }
    if (!ok)
    {
        schemaMissing_ = HasNativeError(stmt, SQL_HANDLE_STMT, { 208, 2812 });
        DumpDiagnostics("usp_SaveStash", stmt, SQL_HANDLE_STMT);
        if (IsConnectionLost(stmt, SQL_HANDLE_STMT)) connected_ = false;
    }

    SQLFreeHandle(SQL_HANDLE_STMT, stmt);
    return ok;
}

// 누가 표·프로시저를 지웠으면(DB 초기화 등) 다시 만들고 한 번 더 한다
bool OdbcSession::LoadStash(int playerId, StashRow& out)
{
    schemaMissing_ = false;
    if (LoadStashOnce(playerId, out)) return true;
    if (!schemaMissing_ || !connected_) return false;
    Log::Warn("[DB] 보관함 표/프로시저가 없어 다시 만든다");
    schemaOk_ = PrepareSchema();
    return LoadStashOnce(playerId, out);
}

bool OdbcSession::SaveStash(int playerId, const StashRow& row)
{
    schemaMissing_ = false;
    if (SaveStashOnce(playerId, row)) return true;
    if (!schemaMissing_ || !connected_) return false;
    Log::Warn("[DB] 보관함 표/프로시저가 없어 다시 만든다");
    schemaOk_ = PrepareSchema();
    return SaveStashOnce(playerId, row);
}
