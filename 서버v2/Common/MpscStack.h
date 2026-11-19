#pragma once
#include <atomic>
#include <functional>
#include <immintrin.h>   // _mm_pause
#include <mutex>
#include <thread>

// ============================================================================
//  MpscStack - 생산자 여럿, 소비자 하나용 Custom 비멈춤 자료구조
//  출처: 작년 멀티코어프로그래밍(청강) 강의·실습자료의 부품만 조합
//
//  Push   : 무잠금 스택 push               07 p14–22, 실습 "Back Off Stack" LF_STACK::push
//           백오프(07 p23–42)는 MPSC_BENCH에서 오히려 느려 기본 끔 (16생산자 1036 → 1307ms)
//  PopAll : getAndSet(top, nullptr) 한 번   05-2 p47 GetAndSet, p127 CLH 잠금의 tail 교환과 같은 연산
//           → 정해진 단계에 끝나는 wait-free (03 p63)
//  ABA    : 꺼내는 쪽이 소비자 하나 + 통째로라 "노드를 읽고 나서 CAS"하는 곳이 없다 (06 p37–38, 07 p22)
//           → EBR·스탬프 없이 꺼낸 노드를 바로 delete
//  순서   : 스택이라 역순 → PopAll에서 뒤집어 넣은 순서(FIFO)로
//  패딩   : top을 캐시라인 하나에 따로 (거짓 공유, 01 p124–125)
//  정확성 : 선형화 지점 Push = 성공한 CAS, PopAll = exchange (05 p101: 직접 만든 알고리즘은 증명하고 쓴다)
//
//  사용처: 세션 일감 큐(워커 → 틱 스레드), 비동기 로그(모든 스레드 → 로그 스레드)
//  T는 `T* next` 멤버가 있어야 한다 (침습형 연결 리스트).
// ============================================================================

// 실습 "Back Off Stack"의 BACKOFF 그대로 (07 p23–35).
// rand()만 스레드별 xorshift로 바꿈: MSVC rand는 스레드마다 시드 1로 시작해 지연이 똑같이 겹친다.
class Backoff
{
public:
    Backoff(int minDelay, int maxDelay) : maxDelay(maxDelay), limit(minDelay) {}

    void Do()
    {
        thread_local unsigned seed = (unsigned)std::hash<std::thread::id>{}(std::this_thread::get_id()) | 1u;
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        int delay = (int)(seed % (unsigned)limit);
        limit += limit;
        if (limit > maxDelay) limit = maxDelay;
        for (int i = 0; i < delay; ++i) _mm_pause();
    }

    // 실습처럼 최대 지연 = 스레드 수 (여기선 하드웨어 스레드 수)
    static int MaxDelay()
    {
        static const int n = (int)std::thread::hardware_concurrency() < 2 ? 2 : (int)std::thread::hardware_concurrency();
        return n;
    }

private:
    int maxDelay;
    int limit;
};

template <class T, bool UseBackoff = false>  // true = 실습 LFBO_STACK식 백오프 (MPSC_BENCH 비교용)
class MpscStack
{
public:
    // 반환값 true = 비어 있던 스택에 처음 넣었음
    bool Push(T* node)
    {
        T* old = top.load(std::memory_order_relaxed);
        node->next = old;
        if (top.compare_exchange_strong(old, node, std::memory_order_release, std::memory_order_relaxed))
            return old == nullptr;

        // CAS가 실패했을 때만 백오프 (경쟁이 없으면 비용 0)
        Backoff bo(1, Backoff::MaxDelay());
        for (;;)
        {
            if (UseBackoff) bo.Do();
            node->next = old;                   // 실패한 CAS가 old를 현재 top으로 갱신해 준다
            if (top.compare_exchange_strong(old, node, std::memory_order_release, std::memory_order_relaxed))
                return old == nullptr;
        }
    }

    // 소비자 전용. 넣은 순서대로 연결된 리스트를 돌려준다 (없으면 nullptr).
    T* PopAll()
    {
        T* list = top.exchange(nullptr, std::memory_order_acquire);
        T* fifo = nullptr;
        while (list)
        {
            T* n = list->next;
            list->next = fifo;
            fifo = list;
            list = n;
        }
        return fifo;
    }

    bool Empty() const { return top.load(std::memory_order_relaxed) == nullptr; }

private:
    // 앞뒤 64바이트 패딩: 생산자들이 CAS하는 top이 다른 멤버와 캐시라인을 나누지 않게.
    // alignas(64)는 C++14의 new가 정렬을 보장하지 않아 패딩으로 대신한다.
    char             padFront[64];
    std::atomic<T*>  top{ nullptr };
    char             padBack[64];
};

// ============================================================================
//  CoarseQueue - 비교용 성긴 락 큐 (v2.ini sync=cqueue)
//  실습 "LFQUEUE"의 C_QUEUE처럼 모든 메소드를 락 하나로 (06 p7–15).
//  모양(Push/PopAll)은 MpscStack과 같다 → 일감 큐 구조는 그대로 두고 자료구조만 바꿔 잰다.
// ============================================================================
template <class T>
class CoarseQueue
{
public:
    bool Push(T* node)
    {
        node->next = nullptr;
        std::lock_guard<std::mutex> g(lock);
        bool wasEmpty = (head == nullptr);
        if (tail) tail->next = node; else head = node;
        tail = node;
        return wasEmpty;
    }

    // 넣은 순서대로 전부 (뒤집을 필요 없음)
    T* PopAll()
    {
        std::lock_guard<std::mutex> g(lock);
        T* list = head;
        head = tail = nullptr;
        return list;
    }

private:
    std::mutex lock;
    T*         head = nullptr;
    T*         tail = nullptr;
};
