#pragma once
#include "DungeonTypes.h"

#include <algorithm>
#include <cstdint>
#include <vector>

// ============================================================================
//  TileBitmap - 던전 타일 집합을 비트 1개로 보관
//
//  왜 필요한가:
//    unordered_set<IntVec3>는 원소 1개에 노드+해시버킷까지 50바이트 가까이 쓴다.
//    floor/wall/ceiling/solid 를 합치면 던전 1개가 40~48MB. 세션 40개면 2GB다.
//    같은 정보를 비트맵에 담으면 던전 1개가 2.5MB 수준으로 떨어진다.
//
//  주의:
//    - 셋의 "삽입 순서"에 의존하는 코드에는 쓰면 안 된다. 이 클래스는
//      좌표 순서(x→y→z)로만 순회한다. 던전 생성에서 이 집합들을 순회하는 곳은
//      벽/천장 만들기뿐이고, 그 안의 연산은 순서와 무관하다(단순 삽입).
//    - 격자 밖 좌표도 담을 수 있어야 한다. 바닥이 맵 가장자리에 닿으면
//      벽이 -1 좌표에 생기기 때문. PAD 만큼 여유를 두고 오프셋을 준다.
// ============================================================================
class TileBitmap
{
public:
    static constexpr int PAD = 2;   // 맵 밖 벽을 담기 위한 여유

    // sizeX/sizeZ = mapSize, sizeY = mapHeight + 2 (천장 한 칸 위까지)
    void Reset(int sizeX, int sizeY, int sizeZ)
    {
        sx_ = sizeX + PAD * 2;
        sy_ = sizeY;
        sz_ = sizeZ + PAD * 2;
        words_.assign((TotalBits() + 63) / 64, 0ull);
        count_ = 0;
    }

    void Clear()
    {
        std::fill(words_.begin(), words_.end(), 0ull);
        count_ = 0;
    }

    // 담을 수 있는 좌표인지. 밖이면 애초에 존재하지 않는 타일로 취급한다.
    bool InRange(const IntVec3& t) const
    {
        int x = t.x + PAD, y = t.y, z = t.z + PAD;
        return x >= 0 && y >= 0 && z >= 0 && x < sx_ && y < sy_ && z < sz_;
    }

    // 새로 넣었으면 true, 이미 있었거나 범위 밖이면 false. (set::insert().second와 동일)
    bool Insert(const IntVec3& t)
    {
        if (!InRange(t)) return false;
        std::size_t i = Index(t);
        std::uint64_t mask = 1ull << (i & 63);
        std::uint64_t& w = words_[i >> 6];
        if (w & mask) return false;
        w |= mask;
        ++count_;
        return true;
    }

    bool Test(const IntVec3& t) const
    {
        if (!InRange(t)) return false;
        std::size_t i = Index(t);
        return ((words_[i >> 6] >> (i & 63)) & 1ull) != 0;
    }

    // unordered_set 과 같은 이름을 제공해 호출부를 덜 고치게 한다.
    std::size_t count(const IntVec3& t) const { return Test(t) ? 1u : 0u; }
    std::size_t size()  const { return count_; }
    bool        empty() const { return count_ == 0; }

    // 켜진 비트를 좌표 순서로 순회. fn(const IntVec3&)
    // 비트가 0인 워드는 통째로 건너뛰므로 실제 타일 수에 비례한 비용이다.
    template <class F>
    void ForEach(F fn) const
    {
        const std::size_t total = words_.size();
        const std::size_t limit = TotalBits();
        for (std::size_t w = 0; w < total; ++w)
        {
            std::uint64_t bits = words_[w];
            while (bits)
            {
                int b = CountTrailingZeros(bits);
                bits &= bits - 1;                       // 최하위 1비트 제거
                std::size_t i = (w << 6) + (std::size_t)b;
                if (i >= limit) break;
                fn(FromIndex(i));
            }
        }
    }

    std::size_t MemoryBytes() const { return words_.size() * sizeof(std::uint64_t); }

private:
    std::size_t TotalBits() const
    {
        return (std::size_t)sx_ * (std::size_t)sy_ * (std::size_t)sz_;
    }

    std::size_t Index(const IntVec3& t) const
    {
        std::size_t x = (std::size_t)(t.x + PAD);
        std::size_t y = (std::size_t)(t.y);
        std::size_t z = (std::size_t)(t.z + PAD);
        return (x * (std::size_t)sy_ + y) * (std::size_t)sz_ + z;
    }

    IntVec3 FromIndex(std::size_t i) const
    {
        std::size_t z = i % (std::size_t)sz_;
        std::size_t r = i / (std::size_t)sz_;
        std::size_t y = r % (std::size_t)sy_;
        std::size_t x = r / (std::size_t)sy_;
        return IntVec3((int)x - PAD, (int)y, (int)z - PAD);
    }

    static int CountTrailingZeros(std::uint64_t v)
    {
        int n = 0;
        while ((v & 1ull) == 0ull) { v >>= 1; ++n; }
        return n;
    }

    std::vector<std::uint64_t> words_;
    std::size_t                count_ = 0;
    int sx_ = 0, sy_ = 0, sz_ = 0;
};
