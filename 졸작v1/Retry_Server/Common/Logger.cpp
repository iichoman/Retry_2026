#define _CRT_SECURE_NO_WARNINGS
#include "Logger.h"

#include <iostream>
#include <cstdarg>
#include <cstdio>
#include <mutex>
#include <ctime>
#include <Windows.h>

namespace Log {

static std::mutex   g_mtx;
static std::string  g_module = "App";

// 콘솔 ANSI 색상 코드를 처음 한 번 활성화.
// Windows 10 이상에서 동작. 그 이하는 색상 안 나오지만 로그 자체는 정상.
static void EnableVTMode()
{
    HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
    if (hOut == INVALID_HANDLE_VALUE) return;
    DWORD mode = 0;
    if (!GetConsoleMode(hOut, &mode)) return;
    mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
    SetConsoleMode(hOut, mode);
}

void Init(const std::string& moduleName)
{
    g_module = moduleName;
    EnableVTMode();
}

static void Print(const char* level, const char* color, const char* fmt, va_list args)
{
    char body[1024];
    vsnprintf(body, sizeof(body), fmt, args);

    // 시각 (HH:MM:SS)
    time_t now = time(nullptr);
    struct tm tm_buf;
    localtime_s(&tm_buf, &now);
    char tbuf[16];
    strftime(tbuf, sizeof(tbuf), "%H:%M:%S", &tm_buf);

    // 한 줄로 묶어 atomic 출력 (lock 보호)
    std::lock_guard<std::mutex> lk(g_mtx);
    std::cout << color
              << "[" << tbuf << "][" << g_module << "][" << level << "] "
              << body
              << "\033[0m\n";
}

void Info(const char* fmt, ...)
{
    va_list a; va_start(a, fmt);
    Print("INFO", "\033[0m", fmt, a);     // 기본 색
    va_end(a);
}

void Warn(const char* fmt, ...)
{
    va_list a; va_start(a, fmt);
    Print("WARN", "\033[33m", fmt, a);    // 노랑
    va_end(a);
}

void Error(const char* fmt, ...)
{
    va_list a; va_start(a, fmt);
    Print("ERROR", "\033[31m", fmt, a);   // 빨강
    va_end(a);
}

} // namespace Log
