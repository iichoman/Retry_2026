#include "OdbcSession.h"
#include "DbConfig.h"
#include "../Common/Logger.h"

#include <cstdint>
#include <cstring>
#include <cwchar>
#include <vector>

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

bool OdbcSession::Connect()
{
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
            cs += L"Database="; cs += DbName(); cs += L";";
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
        }
    }

    if (!Ok(r))
    {
        DumpDiagnostics("SQLConnect", hdbc_, SQL_HANDLE_DBC);
        return false;
    }

    connected_ = true;
    Log::Info("[DB] 접속 성공 (%s)",
        DB_DSN[0] ? WideToUtf8(DB_DSN).c_str() : WideToUtf8(DbServer()).c_str());
    return true;
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
