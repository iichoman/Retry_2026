#pragma once
#include <cstdint>
namespace PositionSync {
constexpr int Packet=204, Version=1;
#pragma pack(push,1)
struct State {
    int version=Version, serial=0;
    std::int64_t inputTimestamp=0;
    float x=0,y=0,z=0;
    int teleport=0;
};
#pragma pack(pop)
static_assert(sizeof(State)==32,"Position sync wire size");
}
