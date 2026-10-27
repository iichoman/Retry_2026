#pragma once
#include <cmath>
#include <functional>

// ============================================================================
//  서버용 공통 수학 타입
//
//  Unity의 Vector3와 동일한 좌표계 (Y가 위, 오른손 좌표계).
//  서버는 사실상 XZ 평면(수평)만 신경 쓰면 되지만, 클라와 위치 데이터를
//  주고받을 때 Y도 함께 보내야 하므로 Vec3로 통일.
//
//  시야 처리는 XZ 거리만 사용 (DistanceXZ).
// ============================================================================

struct Vec3
{
    float x, y, z;

    Vec3() : x(0.f), y(0.f), z(0.f) {}
    Vec3(float a, float b, float c) : x(a), y(b), z(c) {}

    // 기본 연산
    Vec3 operator+(const Vec3& o) const { return { x + o.x, y + o.y, z + o.z }; }
    Vec3 operator-(const Vec3& o) const { return { x - o.x, y - o.y, z - o.z }; }
    Vec3 operator*(float s)       const { return { x * s,   y * s,   z * s   }; }

    Vec3& operator+=(const Vec3& o) { x += o.x; y += o.y; z += o.z; return *this; }

    // 길이
    float Length()   const { return std::sqrt(x*x + y*y + z*z); }
    float LengthSq() const { return x*x + y*y + z*z; }

    // 시야/거리 검사용 - XZ 평면 거리 (Y 무시)
    float DistanceXZ(const Vec3& o) const
    {
        float dx = x - o.x;
        float dz = z - o.z;
        return std::sqrt(dx*dx + dz*dz);
    }

    float DistanceSqXZ(const Vec3& o) const
    {
        float dx = x - o.x;
        float dz = z - o.z;
        return dx*dx + dz*dz;
    }

    Vec3 Normalized() const
    {
        float len = Length();
        if (len < 1e-6f) return { 0.f, 0.f, 0.f };
        float inv = 1.f / len;
        return { x * inv, y * inv, z * inv };
    }
};

// ----------------------------------------------------------------------------
//  격자 좌표 (시야 격자, 던전 타일)
//
//  던전 알고리즘에서 Vector3Int를 그대로 쓰지만, Y는 거의 0이라
//  서버 계산은 IntVec2(x, z)만 있으면 충분한 경우가 많다.
// ----------------------------------------------------------------------------
struct IntVec2
{
    int x, z;

    IntVec2() : x(0), z(0) {}
    IntVec2(int a, int b) : x(a), z(b) {}

    bool operator==(const IntVec2& o) const { return x == o.x && z == o.z; }
    bool operator!=(const IntVec2& o) const { return !(*this == o); }
};

struct IntVec3
{
    int x, y, z;

    IntVec3() : x(0), y(0), z(0) {}
    IntVec3(int a, int b, int c) : x(a), y(b), z(c) {}

    bool operator==(const IntVec3& o) const { return x == o.x && y == o.y && z == o.z; }
    bool operator!=(const IntVec3& o) const { return !(*this == o); }
};

// std::unordered_set/map에 사용할 해시 함수
namespace std {

    template<>
    struct hash<IntVec2>
    {
        size_t operator()(const IntVec2& v) const noexcept
        {
            // 큰 소수 곱으로 해시 충돌 줄이기
            size_t h = static_cast<size_t>(v.x) * 73856093u;
            h       ^= static_cast<size_t>(v.z) * 19349663u;
            return h;
        }
    };

    template<>
    struct hash<IntVec3>
    {
        size_t operator()(const IntVec3& v) const noexcept
        {
            size_t h = static_cast<size_t>(v.x) * 73856093u;
            h       ^= static_cast<size_t>(v.y) * 83492791u;
            h       ^= static_cast<size_t>(v.z) * 19349663u;
            return h;
        }
    };

} // namespace std
