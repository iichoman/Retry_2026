#pragma once

#include <windows.h>
#include <string>

// ============================================================================
//  DbConfig - DB 접속 설정. 환경이 바뀌면 여기만 고치면 된다.
// ============================================================================

// 1) DSN을 만들어 뒀다면 그 이름을 적는다. (강의자료 방식: ODBC 데이터 원본 관리자)
//    비워두면("") 아래 DB_SERVER/DB_NAME으로 DSN 없이 직접 붙는다.
//    DSN 만들기가 귀찮으면 비워두는 쪽이 편하고 동작은 똑같다.
constexpr const wchar_t* DB_DSN = L"";

// 2) DSN 없이 붙을 때 쓰는 기본값.
//    SQL Server 인스턴스 이름은 PC마다 다르므로(SQLEXPRESS / SQLEXPRESS04 ...)
//    실행 파일 옆에 db.ini 를 두면 재빌드 없이 덮어쓸 수 있다.
//
//      [db]
//      server=localhost\SQLEXPRESS
//      database=RetryDB
//
//    db.ini 가 없으면 아래 기본값을 그대로 쓴다.
constexpr const wchar_t* DB_SERVER_DEFAULT = L"localhost\\SQLEXPRESS";
constexpr const wchar_t* DB_NAME_DEFAULT = L"RetryDB";

namespace dbcfg
{
    // 실행 파일이 있는 폴더의 db.ini 를 읽는다. (상대 경로로 주면 윈도 폴더를 뒤진다)
    inline std::wstring ReadIni(const wchar_t* key, const wchar_t* fallback)
    {
        wchar_t exePath[MAX_PATH] = {};
        GetModuleFileNameW(nullptr, exePath, MAX_PATH);
        std::wstring ini(exePath);
        size_t slash = ini.find_last_of(L"\\/");
        ini = (slash == std::wstring::npos ? std::wstring() : ini.substr(0, slash + 1)) + L"db.ini";

        wchar_t buf[256] = {};
        DWORD n = GetPrivateProfileStringW(L"db", key, fallback, buf, 256, ini.c_str());
        return std::wstring(buf, n);
    }
}

// 최초 1회만 읽고 캐싱한다.
inline const wchar_t* DbServer()
{
    static const std::wstring v = dbcfg::ReadIni(L"server", DB_SERVER_DEFAULT);
    return v.c_str();
}
inline const wchar_t* DbName()
{
    static const std::wstring v = dbcfg::ReadIni(L"database", DB_NAME_DEFAULT);
    return v.c_str();
}

// 3) 인증. 빈 문자열이면 Windows 인증(Trusted_Connection)을 쓴다.
//    SQL 인증을 쓰려면 SSMS에서 로그인을 만들고 여기에 적는다.
constexpr const wchar_t* DB_USER = L"";
constexpr const wchar_t* DB_PASSWORD = L"";

// 4) 로그인 타임아웃(초)
constexpr int DB_LOGIN_TIMEOUT_SEC = 5;

// 5) DB가 없어도 서버를 띄울지.
//    true  : DB 접속 실패해도 게임은 정상 동작(전적만 안 쌓임). 전시회용 안전장치.
//    false : DB 없으면 서버 시작을 중단.
constexpr bool DB_OPTIONAL = true;
