#pragma once
#include "../Common/MathTypes.h"
#include <algorithm>
#include <cmath>

namespace MummyGas {
constexpr float ChargeWindup=.6f, ChargeDuration=.8f, ChargeSpeed=7.5f;
constexpr float Windup = .9f, Lifetime = 1.8f, Speed = 2.7f, TrailLength = 1.5f;
inline float Front(float age) { return .6f + Speed * age; }
inline float Radius(float age) { return .45f + .35f * age; }
inline bool Contains(const Vec3& origin, float dx, float dz, float age, const Vec3& point) {
    if (std::fabs(point.y-origin.y)>2.f) return false;
    float front=Front(age), rear=(std::max)(.25f,front-TrailLength);
    float along=(point.x-origin.x)*dx+(point.z-origin.z)*dz;
    float nearest=(std::max)(rear,(std::min)(front,along));
    float x=point.x-origin.x-dx*nearest,z=point.z-origin.z-dz*nearest;
    float radius=Radius(age)+.3f; // Player body's horizontal radius.
    return along>=0 && x*x+z*z<=radius*radius;
}
}
