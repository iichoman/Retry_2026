#include "CSharpRandom.h"
#include <cstdlib>      // std::abs
#include <climits>      // INT_MIN

// ============================================================================
//  C# System.Random 알고리즘 (Mono 기준).
//  주의: 이 코드는 "올바른 코드"가 아니라 "C#과 동일하게 동작하는 코드"가 목표.
//        의도적으로 원본 동작을 그대로 옮긴다.
// ============================================================================

CSharpRandom::CSharpRandom(int seed)
{
    // Math.Abs(int.MinValue) → C# 도 overflow 동작.
    // C++의 std::abs(INT_MIN)는 UB지만, 대부분의 컴파일러에서 동일하게
    // INT_MIN을 반환. C#과 일치하도록 일부러 그대로 둠.
    int subtraction = (seed == INT_MIN) ? INT_MAX : std::abs(seed);
    int mj = MSEED - subtraction;

    seedArray[55] = mj;
    int mk = 1;

    for (int i = 1; i < 55; i++)
    {
        int ii = (21 * i) % 55;
        seedArray[ii] = mk;
        mk = mj - mk;
        if (mk < 0) mk += MBIG;
        mj = seedArray[ii];
    }

    for (int k = 1; k < 5; k++)
    {
        for (int i = 1; i < 56; i++)
        {
            seedArray[i] -= seedArray[1 + (i + 30) % 55];
            if (seedArray[i] < 0) seedArray[i] += MBIG;
        }
    }

    inext  = 0;
    inextp = 21;
}

int CSharpRandom::InternalSample()
{
    int locINext  = inext;
    int locINextp = inextp;

    if (++locINext  >= 56) locINext  = 1;
    if (++locINextp >= 56) locINextp = 1;

    int retVal = seedArray[locINext] - seedArray[locINextp];

    if (retVal == MBIG) retVal--;
    if (retVal < 0) retVal += MBIG;

    seedArray[locINext] = retVal;

    inext  = locINext;
    inextp = locINextp;

    return retVal;
}

double CSharpRandom::Sample()
{
    return InternalSample() * (1.0 / MBIG);
}

double CSharpRandom::GetSampleForLargeRange()
{
    int result = InternalSample();
    bool negative = (InternalSample() % 2 == 0);
    if (negative) result = -result;
    double d = result;
    d += (INT_MAX - 1);
    d /= 2.0 * (uint32_t)INT_MAX - 1;
    return d;
}

int CSharpRandom::Next()
{
    return InternalSample();
}

int CSharpRandom::Next(int maxValue)
{
    if (maxValue < 0) return 0;        // C#은 예외 던지지만 서버는 안전한 기본값
    return (int)(Sample() * maxValue);
}

int CSharpRandom::Next(int minValue, int maxValue)
{
    if (minValue > maxValue) return minValue;     // 안전 기본값

    long long range = (long long)maxValue - (long long)minValue;
    if (range <= INT_MAX)
    {
        return ((int)(Sample() * range)) + minValue;
    }
    else
    {
        return (int)((long long)(GetSampleForLargeRange() * range)) + minValue;
    }
}

double CSharpRandom::NextDouble()
{
    return Sample();
}
