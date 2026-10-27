#pragma once
#include <atomic>

// ============================================================================
//  MpscStack - 생산자 여럿, 소비자 하나용 Custom 비멈춤 자료구조
//
//  Push   : 여러 스레드가 동시에 호출한다. CAS 루프 (lock-free)
//  PopAll : 소비자 스레드 하나만 호출한다. exchange 한 번으로 전부 가져간다 (wait-free)
//
//  이 구조를 고른 이유 (멀티코어프로그래밍 06·07장 기준)
//   - 소비자가 하나씩 꺼내지 않고 통째로 가져가므로 ABA 문제가 생기지 않는다.
//     생산자는 top의 주소값만 비교할 뿐 다른 노드를 역참조하지 않기 때문이다.
//   - 그래서 꺼낸 노드는 바로 delete해도 안전하다 → EBR 같은 회수 기법이 필요 없다.
//   - 스택이라 꺼내면 역순이다 → PopAll에서 뒤집어 넣은 순서(FIFO)로 돌려준다.
//
//  사용처: 세션 일감 큐(워커 → 틱 스레드), 비동기 로그(모든 스레드 → 로그 스레드)
//  T는 `T* next` 멤버가 있어야 한다 (침습형 연결 리스트).
// ============================================================================
template <class T>
class MpscStack
{
public:
    // 반환값 true = 비어 있던 스택에 처음 넣었음
    bool Push(T* node)
    {
        T* old = top.load(std::memory_order_relaxed);
        do { node->next = old; }
        while (!top.compare_exchange_weak(old, node,
            std::memory_order_release, std::memory_order_relaxed));
        return old == nullptr;
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
    // 앞뒤 64바이트 패딩: 생산자들이 CAS하는 top이 다른 멤버와 캐시라인을 나누지 않게 (거짓 공유 방지).
    // alignas(64)는 C++14의 new가 정렬을 보장하지 않아 패딩으로 대신한다.
    char             padFront[64];
    std::atomic<T*>  top{ nullptr };
    char             padBack[64];
};
