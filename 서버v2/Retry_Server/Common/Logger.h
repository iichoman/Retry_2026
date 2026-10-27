#pragma once
#include <string>

// ============================================================================
//  로거
//
//  메인/세션 어느 쪽이든 동일한 인터페이스로 호출.
//  Init("Lobby") 또는 Init("Session") 처럼 모듈 이름을 한 번 등록.
//  이후 Info/Warn/Error 호출만 하면 됨.
//
//  사용 예:
//    Log::Init("Lobby");
//    Log::Info("클라이언트 접속: id=%d ip=%s", id, ip);
//    Log::Warn("패킷 크기 비정상: %d", size);
//    Log::Error("소켓 오류: %d", err);
//
//  다중 스레드에서 동시 호출해도 출력이 섞이지 않는다.
//  v2: 기본은 비동기(로그 스레드가 모아서 출력). SetAsync(false)면 v1처럼 전역 lock + 즉시 출력.
// ============================================================================

namespace Log {

    void Init(const std::string& moduleName);

    // v2: true(기본) = 비동기 출력, false = v1 방식. Init 직후 한 번만 바꾼다.
    void SetAsync(bool async);

    void Info(const char* fmt, ...);
    void Warn(const char* fmt, ...);
    void Error(const char* fmt, ...);

} // namespace Log
