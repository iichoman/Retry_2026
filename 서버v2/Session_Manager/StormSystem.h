#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
namespace Storm {
constexpr int Packet=205;
constexpr int DamagePacket=243;
constexpr float FinalRadius=12.f;
constexpr float Begins[]={0.f,180.f,360.f,510.f,630.f};
constexpr float Durations[]={0.f,60.f,50.f,45.f,40.f};
#pragma pack(push,1)
struct State { int version=1,stage=0; float x=0,z=0,radius=0,target=0; int64_t time=0,end=0,next=0; };
struct Damage { int version=1, victimId=0, hp=0; };
#pragma pack(pop)
static_assert(sizeof(State)==48,"Storm wire layout");
static_assert(sizeof(Damage)==12,"Storm damage wire layout");
inline float Target(float initial,int stage){const float ratios[]={1.f,.7f,.45f,.25f,0.f};return std::max(FinalRadius,initial*ratios[std::clamp(stage,0,4)]);}
inline State At(float elapsed,float initial,float x,float z,int64_t now){
 State s;s.x=x;s.z=z;s.time=now;
 for(int stage=1;stage<=4;++stage)if(elapsed>=Begins[stage])s.stage=stage;
 s.target=Target(initial,s.stage);
 float age=std::max(0.f,elapsed-Begins[s.stage]);
 float t=s.stage==0?1.f:std::clamp(age/Durations[s.stage],0.f,1.f);
 s.radius=s.stage==0?initial:Target(initial,s.stage-1)+(s.target-Target(initial,s.stage-1))*t;
 s.end=now+int64_t(std::max(0.f,Durations[s.stage]-age)*1000);
 s.next=s.stage<4?now+int64_t(std::max(0.f,Begins[s.stage+1]-elapsed)*1000):0;return s;
}
inline bool Outside(const State& s,float x,float z){return (x-s.x)*(x-s.x)+(z-s.z)*(z-s.z)>s.radius*s.radius;}
}
