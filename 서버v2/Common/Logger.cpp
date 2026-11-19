#define _CRT_SECURE_NO_WARNINGS
#include "Logger.h"
#include "MpscStack.h"

#include <atomic>
#include <chrono>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <Windows.h>

// ============================================================================
//  v2: 비동기 로그
//
//  v1은 로그 한 줄마다 전역 mutex를 잡고 콘솔에 직접 썼다. 콘솔 출력은 느려서
//  틱 스레드·워커들이 로그 때문에 서로 기다렸다 (프로세스 전체에 걸린 성긴 락).
//  v2는 한 줄을 만들어 MpscStack에 넣기만 하고, 로그 전용 스레드가 10ms마다
//  모아서 한 번에 출력한다. 호출한 스레드는 기다리지 않는다.
// ============================================================================

namespace Log {

namespace {
    // 한 줄. text는 len + 1 바이트로 할당한다.
    struct Line
    {
        Line* next;
        int   len;
        char  text[1];
    };

    constexpr int MAX_PENDING = 200000;     // 출력이 못 따라가면 이 이상은 버린다

    std::string g_module = "App";
    bool        g_async = true;
    std::mutex  g_syncMtx;                   // v1 방식(SetAsync(false))일 때만 사용

    class Writer
    {
    public:
        Writer() : running(true), pending(0), dropped(0)
        {
            th = std::thread([this] { Run(); });
        }

        ~Writer()
        {
            running = false;
            if (th.joinable()) th.join();
            Flush();                         // 남은 줄까지 출력
        }

        void Push(const char* s, int len)
        {
            if (pending.fetch_add(1, std::memory_order_relaxed) >= MAX_PENDING)
            {
                pending.fetch_sub(1, std::memory_order_relaxed);
                dropped.fetch_add(1, std::memory_order_relaxed);
                return;
            }
            Line* l = static_cast<Line*>(std::malloc(sizeof(Line) + len));
            if (!l)
            {
                pending.fetch_sub(1, std::memory_order_relaxed);
                return;
            }
            l->len = len;
            std::memcpy(l->text, s, len);
            l->text[len] = '\0';
            q.Push(l);
        }

    private:
        void Run()
        {
            while (running)
            {
                Flush();
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }
        }

        void Flush()
        {
            Line* l = q.PopAll();
            int n = 0;
            buf.clear();
            while (l)
            {
                Line* next = l->next;
                buf.append(l->text, l->len);
                std::free(l);
                l = next;
                ++n;
            }
            if (n > 0)
            {
                pending.fetch_sub(n, std::memory_order_relaxed);
                std::fwrite(buf.data(), 1, buf.size(), stdout);
            }

            unsigned long long d = dropped.exchange(0, std::memory_order_relaxed);
            if (d)
                std::fprintf(stdout, "\033[33m[로그] 출력이 밀려 %llu줄을 버렸습니다\033[0m\n", d);

            if (n > 0 || d) std::fflush(stdout);
        }

        MpscStack<Line>                   q;
        std::atomic<bool>                 running;
        std::atomic<int>                  pending;
        std::atomic<unsigned long long>   dropped;
        std::string                       buf;      // 로그 스레드 전용
        std::thread                       th;
    };

    Writer& W()
    {
        static Writer w;                     // 첫 로그 때 생성 (스레드 안전), 종료 시 남은 줄 출력
        return w;
    }

    // 콘솔 ANSI 색상 코드를 처음 한 번 활성화.
    // Windows 10 이상에서 동작. 그 이하는 색상 안 나오지만 로그 자체는 정상.
    void EnableVTMode()
    {
        HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut == INVALID_HANDLE_VALUE) return;
        DWORD mode = 0;
        if (!GetConsoleMode(hOut, &mode)) return;
        mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        SetConsoleMode(hOut, mode);
    }

    void Print(const char* level, const char* color, const char* fmt, va_list args)
    {
        char body[1024];
        vsnprintf(body, sizeof(body), fmt, args);

        // 시각 (HH:MM:SS)
        time_t now = time(nullptr);
        struct tm tm_buf;
        localtime_s(&tm_buf, &now);
        char tbuf[16];
        strftime(tbuf, sizeof(tbuf), "%H:%M:%S", &tm_buf);

        char line[1280];
        int len = snprintf(line, sizeof(line), "%s[%s][%s][%s] %s\033[0m\n",
            color, tbuf, g_module.c_str(), level, body);
        if (len < 0) return;
        if (len >= (int)sizeof(line)) len = (int)sizeof(line) - 1;

        if (!g_async)
        {
            // v1 방식: 전역 lock을 잡고 콘솔에 바로 쓴다
            std::lock_guard<std::mutex> lk(g_syncMtx);
            std::cout.write(line, len);
            return;
        }
        W().Push(line, len);
    }
}

void Init(const std::string& moduleName)
{
    g_module = moduleName;
    EnableVTMode();                          // 로그 스레드는 첫 비동기 로그 때 시작
}

void SetAsync(bool async)
{
    g_async = async;
}

void Info(const char* fmt, ...)
{
    va_list a; va_start(a, fmt);
    Print("INFO", "\033[0m", fmt, a);       // 기본 색
    va_end(a);
}

void Warn(const char* fmt, ...)
{
    va_list a; va_start(a, fmt);
    Print("WARN", "\033[33m", fmt, a);      // 노랑
    va_end(a);
}

void Error(const char* fmt, ...)
{
    va_list a; va_start(a, fmt);
    Print("ERROR", "\033[31m", fmt, a);     // 빨강
    va_end(a);
}

} // namespace Log
