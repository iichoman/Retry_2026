#pragma once
#include <cstdint>

// ============================================================================
//  ItemHash
//
//  아이템 문자열 id("iron_ore" 등)를 32bit 정수로 변환.
//  FNV-1a 32bit. 클라(C#)의 ItemHash.Of와 반드시 동일한 결과여야 한다.
//
//  이 방식을 쓰는 이유:
//   - 클라 아이템은 ScriptableObject(ItemData)라 서버가 직접 읽을 수 없음
//   - 수동 ID 테이블을 양쪽에 두면 추가할 때마다 동기화 사고가 남
//   - 문자열 id 하나만 맞추면 정수 id가 자동으로 일치
//
//  주의: 해시 충돌은 이론상 가능하나 아이템 수백 종 규모에선 무시 가능.
//        새 아이템 추가 시 서버 로그에 충돌 경고가 뜨면 id를 바꿀 것.
// ============================================================================

namespace ItemHash {

    inline int Of(const char* itemId)
    {
        if (!itemId) return 0;
        uint32_t h = 2166136261u;              // FNV offset basis
        for (const char* p = itemId; *p; ++p)
        {
            h ^= (uint32_t)(unsigned char)(*p);
            h *= 16777619u;                    // FNV prime
        }
        return (int)h;
    }

} // namespace ItemHash
