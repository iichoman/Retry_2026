#pragma once
#include "../Common/MathTypes.h"
#include "ABRules.h"
#include <unordered_set>
namespace Skills {
constexpr int CommandPacket=220,StatePacket=221,EventPacket=222;
constexpr float ECooldown=8, QCooldown=20, EWindup=.18f, QWindup=.75f, Recovery=.45f;
constexpr float Speed=22, Range=24, HalfWidth=.9f, QRange=6, QCos=.5f;
constexpr float SpinDuration=1.8f, SpinRadius=3.5f, DropRadius=3.5f, DropRange=16.f;
inline bool Spin(int skill){return skill==3||skill==5;}
#pragma pack(push,1)
struct Command { int version=1,sequence=0,skill=0;float dx=0,dz=1; };
struct State { int version=2,entityId=0,sequence=0,result=0;float eCooldown=0,qCooldown=0,castLeft=0;int skill=0,serial=0;
 float eDuration=ECooldown,qDuration=QCooldown,power=1,area=1,castSpeed=1,buffLeft=0;int resetSerial=0; };
struct Event { int version=2,entityId=0,serial=0,skill=0,phase=0;float x=0,y=0,z=0,dx=0,dz=1,remaining=0;
 int weapon=0;float area=1,windup=QWindup; };
#pragma pack(pop)
static_assert(sizeof(Command)==20 && sizeof(State)==64 && sizeof(Event)==56,"Skill wire layout");
struct Stats { float cooldown=1,power=1,area=1,castSpeed=1; };
inline Stats Calculate(const AB::Progress& p) {
 int t=std::clamp(p.tier,1,3); Stats s;
 s.cooldown=1-std::min(.5f,.05f*p.upgrades[8]+(p.passiveType==16?.02f+.06f*t:0));
 s.power=1+.1f*p.upgrades[9]+(p.passiveType==17?.02f+.1f*t:0)+(p.skillBuffLeft>0?p.skillBuffPower:0);
 s.area=std::min(1.6f,1+.06f*p.upgrades[10]+(p.passiveType==19?.02f+.08f*t:0));
 s.castSpeed=std::clamp(p.view.attackSpeed/(1-.05f*p.upgrades[13]),.5f,2.f);
 return s;
}
struct Payload { float damage=0,area=1,resonance=0,refund=0; AB::HitTraits traits; };
struct Cast { State view; Vec3 origin, direction; Payload payload; int weapon=0,pulses=0;float windup=0;bool released=false; };
inline bool LocksMovement(const Cast& c){return c.view.castLeft>0&&c.view.skill!=5;}
inline float Cooldown(const AB::Progress& p,bool q){float base=AB::IsGreatsword(p.weaponType)?(q?30.f:10.f):(q?QCooldown:ECooldown);return base*std::max(.5f,Calculate(p).cooldown-(AB::IsGreatsword(p.weaponType)?.05f*p.upgrades[q?18:17]:0));}
struct Wave { Event view;float distance=0;Payload payload;std::unordered_set<int> hit; };
inline float Windup(int skill){return skill==1?EWindup:QWindup;}
inline bool CanUse(const AB::Progress& p,const State& s,int effect,int tier) {
 if(effect==20)return p.skillResetLock<=0&&s.castLeft<=0&&(s.eCooldown>0||s.qCooldown>0);
 if(effect==21)return p.skillBuffLeft<=0||p.skillBuffPower<=.1f+.1f*tier+.001f;
 return true;
}
inline void Use(AB::Progress& p,State& s,int effect,int tier) {
 if(effect==20){float remaining=tier==1?.5f:tier==2?.25f:0;s.eCooldown*=remaining;s.qCooldown*=remaining;p.skillResetLock=10;++s.resetSerial;}
 if(effect==21){p.skillBuffLeft=10;p.skillBuffPower=.1f+.1f*tier;}
}
}
