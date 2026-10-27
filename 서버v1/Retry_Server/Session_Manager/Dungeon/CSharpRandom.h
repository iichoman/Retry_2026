#pragma once
#include <cstdint>

// ============================================================================
//  CSharpRandom
//
//  C# System.Random 알고리즘의 C++ 비트-호환 재구현.
//  서버와 클라이언트가 같은 시드를 받았을 때 Next()/NextDouble() 결과가
//  완전히 동일해야 같은 던전이 생성됨.
//
//  근거: Mono 구현 (mcs/class/corlib/System/Random.cs)
//   - Knuth's subtractive method
//   - SeedArray[56] (인덱스 1~55 사용)
//   - inext, inextp 포인터 두 개
//
//  C# 정확한 동작 재현이 목표이므로, 코드 형태도 원본과 비슷하게 유지.
//  성능 최적화나 코드 정리는 시드 호환성을 깨뜨릴 수 있으므로 자제.
// ============================================================================

class CSharpRandom
{
public:
    explicit CSharpRandom(int seed);

    // C# System.Random 메서드들과 1:1 대응
    int    Next();                                  // 0 ~ int.MaxValue-1
    int    Next(int maxValue);                      // 0 ~ maxValue-1
    int    Next(int minValue, int maxValue);        // minValue ~ maxValue-1
    double NextDouble();                            // [0.0, 1.0)

private:
    static constexpr int MBIG  = 0x7FFFFFFF;        // int.MaxValue
    static constexpr int MSEED = 161803398;

    int seedArray[56];
    int inext;
    int inextp;

    int    InternalSample();
    double Sample();
    double GetSampleForLargeRange();
};
