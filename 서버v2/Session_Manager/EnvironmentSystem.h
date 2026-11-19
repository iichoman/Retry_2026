#pragma once
#include "Dungeon/DungeonGenerator.h"
#include "Dungeon/RoomBlueprint.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>
#include <unordered_set>
namespace Env {
constexpr int Packet=203, Version=1, ChestSource=-700000, RewardChestSource=-800000;
// Existing Unity clock wire contract (PacketProtocol.cs), not a gameplay command.
constexpr int TimeRequest=73,TimeResult=74;
constexpr float ChestCollisionX=1.25f,ChestCollisionZ=1.05f; // visual half-size + actor radius .5m
constexpr float ArrowOffset=1.4f,ArrowSpeed=32.f,ActiveSeconds=.5f,ActorRadius=.35f;
constexpr unsigned CorridorChestChancePercent=20; // One independent roll per corridor, maximum one chest.
#pragma pack(push,1)
struct TimeReply { std::int64_t clientTimeMs,serverTimeMs; };
struct State {
 int version=1,id=0,kind=0,state=0,serial=0; // kind 1 spikes, 2 arrows
 float x=0,y=1,z=0,dx=0,dz=1,length=2,width=2;
 std::int64_t deadline=0;
};
#pragma pack(pop)
static_assert(sizeof(State)==56,"Environment wire size");
static_assert(sizeof(TimeReply)==16,"Clock wire size");
struct Trap { State view; float timer=0; std::unordered_set<int> hit; };
inline bool OnPlate(const State& s,const Vec3& p){if(s.kind==1)return std::abs(p.x-s.x)<=s.width*.5f+ActorRadius && std::abs(p.z-s.z)<=s.length*.5f+ActorRadius && std::abs(p.y-s.y)<2.5f;return std::abs(p.x-s.x)<=.7f+ActorRadius && std::abs(p.z-s.z)<=.7f+ActorRadius && std::abs(p.y-s.y)<2.5f;}
inline float ActiveDuration(const State& s){return s.kind==1?ActiveSeconds:(s.length+.4f+ArrowOffset)/ArrowSpeed;}
inline bool Inside(const State& s,const Vec3& p,float radius=ActorRadius) {
 if(std::abs(p.y-s.y)>2.5f)return false;
 float x=p.x-s.x,z=p.z-s.z;
 if(s.kind==1)return std::abs(x)<=s.width*.5f+radius && std::abs(z)<=s.length*.5f+radius;
 float along=x*s.dx+z*s.dz,across=x*s.dz-z*s.dx;
 return along>=-ArrowOffset-radius && along<=s.length+radius && std::abs(across)<=s.width*.5f+radius;
}
inline unsigned Hash(int seed,int x,int z) { unsigned h=(unsigned)seed ^ ((unsigned)x*73856093u)^((unsigned)z*19349663u);h^=h>>16;h*=0x85ebca6bu;return h^(h>>13); }
struct Placement { Vec3 position; int kind; float dx,dz,length; }; // 0 chest, 3 reward chest
inline IntVec3 ChestFacing(const DungeonGenerator& d,const Vec3& p){auto t=d.WorldToTile(p);t.y=0;for(const auto& r:d.rooms)if(r.floorTiles.count(t))return Blueprints::Entrance(r);return {0,0,1};}
inline Vec3 ChestHalfSize(const DungeonGenerator& d,const Vec3& p){auto f=ChestFacing(d,p);return f.x!=0?Vec3(ChestCollisionZ,0,ChestCollisionX):Vec3(ChestCollisionX,0,ChestCollisionZ);}
inline int RewardRoom(const DungeonGenerator& d,int seed){return Blueprints::Reward(d.rooms,seed);}
// Find the two side walls of a straight corridor. Half-tile centers also handle even widths.
inline bool CorridorCenter(const DungeonGenerator& d,IntVec3 tile,Vec3& center,Vec3& along,int& width){
 if(d.RoomIdAt(d.TileToFloorTop(tile))>=0)return false;
 for(auto cross:std::vector<IntVec3>{{1,0,0},{0,0,1}}){
  int reach[2]={0,0};bool enclosed=true;
  for(int side=0;side<2;side++){
   int sign=side==0?-1:1;
   for(int n=1;n<=16;n++){
    IntVec3 q(tile.x+cross.x*n*sign,0,tile.z+cross.z*n*sign);
    if(d.IsWallTile({q.x,1,q.z})){reach[side]=n;break;}
    if(!d.IsFloorTile(q)||d.RoomIdAt(d.TileToFloorTop(q))>=0)break;
   }
   if(reach[side]==0)enclosed=false;
  }
  int span=reach[0]+reach[1]-1;if(!enclosed||span<5||span>14)continue;
  center=d.TileToFloorTop(tile)+Vec3((float)cross.x,0,(float)cross.z)*((reach[1]-reach[0])*.5f);
  along=Vec3((float)cross.z,0,(float)cross.x);width=span;return true;
 }
 return false;
}
inline std::vector<DungeonGenerator::FurnitureBox> Furniture(const DungeonGenerator& d,int seed){
 std::vector<DungeonGenerator::FurnitureBox> out;auto rooms=d.rooms;
 for(auto& s:d.assignedStartRooms){Room r;r.type=ROOM_TYPE_START;r.bounds=s.bounds;for(int x=r.bounds.xMin();x<r.bounds.xMax();x++)for(int z=r.bounds.zMin();z<r.bounds.zMax();z++)r.floorTiles.insert(IntVec3(x,0,z));rooms.push_back(r);}
 for(auto& r:rooms){int kind=Blueprints::Kind(seed,r);for(int v=0;v<21;v++)for(int u=0;u<21;u++){
  char c=Blueprints::Plans[kind][v][u];if(c=='.'||c=='#'||c=='L'||c=='N')continue;IntVec3 t=Blueprints::PropTile(r,u,v);
  if(std::abs(t.x-(r.bounds.xMin()+r.bounds.size.x/2))<2||std::abs(t.z-(r.bounds.zMin()+r.bounds.size.z/2))<2)continue;
  bool safe=true;for(auto door:r.doorwayFloorTiles)if(std::abs(door.x-t.x)+std::abs(door.z-t.z)<5)safe=false;
  for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){IntVec3 q(t.x+x,0,t.z+z);if(!r.floorTiles.count(q)||d.IsWallTile(q))safe=false;}if(!safe)continue;
  if(r.type==ROOM_TYPE_REWARD){auto mirror=Blueprints::Mirror(r,t);
   if(std::abs(mirror.x-(r.bounds.xMin()+r.bounds.size.x/2))<2||std::abs(mirror.z-(r.bounds.zMin()+r.bounds.size.z/2))<2)continue;
   for(auto door:r.doorwayFloorTiles)if(std::abs(door.x-mirror.x)+std::abs(door.z-mirror.z)<5)safe=false;
   for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){IntVec3 q(mirror.x+x,0,mirror.z+z);if(!r.floorTiles.count(q)||d.IsWallTile(q))safe=false;}if(!safe)continue;
  }
  float sx=c=='T'?2.4f:c=='C'?1.5f:c=='R'?1.6f:c=='B'?2.2f:.85f,sz=c=='T'?1.2f:c=='C'?1.3f:c=='R'?1.4f:c=='B'?.55f:.85f;
  if(r.type==ROOM_TYPE_REWARD&&Blueprints::Entrance(r).x!=0)std::swap(sx,sz);
  out.push_back({d.TileToFloorTop(t),sx*.5f+.5f,sz*.5f+.5f,false});
 }}return out;
}
inline std::vector<Placement> Layout(const DungeonGenerator& d,int seed) {
 std::vector<Placement> out;
 auto furniture=Furniture(d,seed);
 auto clear=[&](IntVec3 t){for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){IntVec3 q(t.x+x,0,t.z+z);if(!d.IsFloorTile(q)||d.IsWallTile(q))return false;}auto p=d.TileToFloorTop(t);for(auto& b:furniture)if(std::abs(p.x-b.center.x)<b.halfX+1&&std::abs(p.z-b.center.z)<b.halfZ+1)return false;return true;};
 auto distant=[&](Vec3 p){for(auto& a:out)if((a.position-p).LengthSq()<144)return false;return true;};
 for(auto& room:d.rooms){
  if(room.type==ROOM_TYPE_START||room.type==ROOM_TYPE_EXIT||room.type==ROOM_TYPE_BOSS)continue;
  std::vector<IntVec3> tiles(room.floorTiles.begin(),room.floorTiles.end());
  std::sort(tiles.begin(),tiles.end(),[&](auto a,auto b){auto ha=Hash(seed,a.x,a.z),hb=Hash(seed,b.x,b.z);return ha!=hb?ha<hb:a.x!=b.x?a.x<b.x:a.z<b.z;});
  bool reward=room.type==ROOM_TYPE_REWARD||room.id==RewardRoom(d,seed);
  int count=0,budget=reward?3:1;
  if(reward){
   auto facing=Blueprints::Entrance(room);int kind=Blueprints::Kind(seed,room);
   for(char marker:{'L','N'})for(int v=0;v<21;v++)for(int u=0;u<21;u++)if(Blueprints::Plans[kind][v][u]==marker&&count<budget){
    IntVec3 anchor=Blueprints::ChestTile(room,u,v);bool placed=false;
    for(int radius=0;radius<=3&&!placed;radius++)for(int x=-radius;x<=radius&&!placed;x++)for(int z=-radius;z<=radius&&!placed;z++){
     IntVec3 t(anchor.x+x,0,anchor.z+z);if(!room.floorTiles.count(t)||!clear(t))continue;auto p=d.TileToFloorTop(t);bool overlap=false;for(auto& a:out)if((a.position-p).LengthSq()<16)overlap=true;if(overlap)continue;
     out.push_back({p,marker=='L'?3:0,(float)facing.x,(float)facing.z,2});count++;placed=true;
    }
   }
   continue;
  }
  for(auto t:tiles){
   if(!clear(t))continue;bool door=false;for(auto q:room.doorwayFloorTiles)if(std::abs(q.x-t.x)+std::abs(q.z-t.z)<8)door=true;
   int edge=(std::min)({t.x-room.bounds.xMin(),room.bounds.xMax()-1-t.x,t.z-room.bounds.zMin(),room.bounds.zMax()-1-t.z});
   if(door||edge<4||edge>6)continue;auto p=d.TileToFloorTop(t);if(!distant(p))continue;
   auto facing=Blueprints::Entrance(room);out.push_back({p,0,(float)facing.x,(float)facing.z,2});if(++count>=budget)break;
  }
 }
 // Preserve corridor chest rolls, but select traps independently from valid firing lanes.
 for(const auto& corridor:d.corridors){
  std::vector<IntVec3> tiles(corridor.floorTiles.begin(),corridor.floorTiles.end());
  std::sort(tiles.begin(),tiles.end(),[](auto a,auto b){return a.x!=b.x?a.x<b.x:a.z<b.z;});
  if(tiles.empty()||Hash(seed^0x43484553,tiles.front().x,tiles.front().z)%100>=CorridorChestChancePercent)continue;
  for(auto t:tiles){if(!clear(t))continue;auto p=d.TileToFloorTop(t);if(d.RoomIdAt(p)>=0||!distant(p))continue;
   bool side=false;for(auto dir:std::vector<IntVec3>{{1,0,0},{-1,0,0},{0,0,1},{0,0,-1}})if(d.IsWallTile({t.x+dir.x*2,1,t.z+dir.z*2}))side=true;
   if(side){out.push_back({p,0,0,1,2});break;}
  }
 }
 auto blocked=[&](Vec3 p,float margin){
  auto t=d.WorldToTile(p);t.y=0;if(!d.IsFloorTile(t)||d.IsWallTile({t.x,1,t.z}))return true;
  for(auto b:furniture)if(std::abs(p.x-b.center.x)<b.halfX+margin && std::abs(p.z-b.center.z)<b.halfZ+margin)return true;
  for(auto a:out)if(a.kind==0||a.kind==3){auto size=ChestHalfSize(d,a.position);if(std::abs(p.x-a.position.x)<size.x+margin&&std::abs(p.z-a.position.z)<size.z+margin)return true;}
  return false;
 };
 // A continuous 4m-thick spike ring surrounds a safe 4x4m treasure island.
 // The two pressure plates fire from the far wall along the sides of the island.
 for(const auto& room:d.rooms)if(room.type==ROOM_TYPE_REWARD&&Blueprints::Kind(seed,room)==8){
  Vec3 center;bool found=false;for(auto a:out)if(a.kind==3&&d.RoomIdAt(a.position)==room.id){center=a.position;found=true;break;}if(!found)continue;
  for(int x=-4;x<=4;x+=4)for(int z=-4;z<=4;z+=4){
   if(x==0&&z==0)continue;auto p=center+Vec3((float)x,0,(float)z);bool fit=true;
   for(float dx=-1.95f;dx<=1.96f;dx+=.65f)for(float dz=-1.95f;dz<=1.96f;dz+=.65f)if(blocked(p+Vec3(dx,0,dz),0))fit=false;
   if(fit)out.push_back({p,1,0,1,4});
  }
  auto f=Blueprints::Entrance(room);
  for(int side:{-1,1}){
   auto p=center+Vec3((float)(f.x*7-f.z*side*4),0,(float)(f.z*7+f.x*side*4));
   auto t=d.WorldToTile(p);t.y=0;float distance=0;
   for(int n=0;n<=40;n++){
    IntVec3 q(t.x-f.x*n,0,t.z-f.z*n);
    if(d.IsWallTile({q.x,1,q.z})){if(n>=10)distance=n-.9f;break;}
    auto pos=d.TileToFloorTop(q);
    if(blocked(pos,.4f)||blocked(pos+Vec3(f.z*.4f,0,-f.x*.4f),0)||blocked(pos+Vec3(-f.z*.4f,0,f.x*.4f),0))break;
   }
   if(distance>=9.1f)out.push_back({p,2,(float)-f.x,(float)-f.z,distance});
  }
 }
 std::vector<IntVec3> candidates;
 for(auto& c:d.corridors)candidates.insert(candidates.end(),c.floorTiles.begin(),c.floorTiles.end());
 for(auto& r:d.rooms)if(r.type!=ROOM_TYPE_START&&r.type!=ROOM_TYPE_EXIT&&r.type!=ROOM_TYPE_BOSS&&r.type!=ROOM_TYPE_REWARD)
  candidates.insert(candidates.end(),r.floorTiles.begin(),r.floorTiles.end());
 std::sort(candidates.begin(),candidates.end(),[&](auto a,auto b){auto x=Hash(seed+71,a.x,a.z),y=Hash(seed+71,b.x,b.z);return x!=y?x<y:a.x!=b.x?a.x<b.x:a.z<b.z;});
 int arrowPlates=0,spikeCount=0;
 auto safeTrap=[&](Vec3 p){
  auto t=d.WorldToTile(p);t.y=0;
  for(auto& start:d.assignedStartRooms)if(t.x>=start.bounds.xMin()-8&&t.x<start.bounds.xMax()+8&&t.z>=start.bounds.zMin()-8&&t.z<start.bounds.zMax()+8)return false;
  for(auto& room:d.rooms){
   if((room.type==ROOM_TYPE_START||room.type==ROOM_TYPE_EXIT||room.type==ROOM_TYPE_BOSS||room.type==ROOM_TYPE_REWARD)&&room.floorTiles.count(t))return false;
   for(auto q:room.doorwayFloorTiles)if(std::abs(q.x-t.x)+std::abs(q.z-t.z)<9)return false;
  }return true;
 };
 auto spikeFits=[&](Vec3 p){
  if(!safeTrap(p))return false;
  for(float x=-1.95f;x<=1.96f;x+=.65f)for(float z=-1.95f;z<=1.96f;z+=.65f)
   if(blocked(p+Vec3(x,0,z),0)||!safeTrap(p+Vec3(x,0,z)))return false;
  return true;
 };
 // Centered stations: a single plate or a matched pair, with generous space between stations.
 for(auto t:candidates){
  if(spikeCount>=24)break;
  Vec3 center,along;int width=0;
  if(Hash(seed+117,t.x,t.z)%11!=0||!CorridorCenter(d,t,center,along,width)||!distant(center))continue;
  Vec3 across(along.z,0,along.x);bool pair=Hash(seed+118,t.x,t.z)%2==0;
  Vec3 offset=(width>=12?across:along)*3.f;
  std::vector<Vec3> station=pair?std::vector<Vec3>{center-offset,center+offset}:std::vector<Vec3>{center};
  if(spikeCount+(int)station.size()>24)continue;
  bool fit=true;
  for(auto p:station){
   if(!spikeFits(p)||!distant(p)){fit=false;break;}
   Vec3 actual,axis;int actualWidth=0;auto q=d.WorldToTile(p);q.y=0;
   if(!CorridorCenter(d,q,actual,axis,actualWidth)||actualWidth!=width||axis.x!=along.x||axis.z!=along.z){fit=false;break;}
   if(width<12&&(actual-p).LengthSq()>.01f){fit=false;break;}
  }
  if(!fit)continue;
  for(auto p:station){out.push_back({p,1,0,1,4});spikeCount++;}
 }
 // Ordinary rooms receive a sparse mirrored pair; doors, furniture and reward rooms stay reserved.
 for(const auto& room:d.rooms){
  if(spikeCount>=32||room.type!=ROOM_TYPE_NORMAL||Hash(seed+119,room.bounds.xMin(),room.bounds.zMin())%2!=0)continue;
  auto f=Blueprints::Entrance(room);Vec3 forward((float)f.x,0,(float)f.z),side((float)-f.z,0,(float)f.x);
  auto center=d.TileToWorld({room.bounds.xMin(),0,room.bounds.zMin()})+Vec3(room.bounds.size.x*.5f,1,room.bounds.size.z*.5f);
  float halfWidth=(f.z!=0?room.bounds.size.x:room.bounds.size.z)*.5f,halfDepth=(f.z!=0?room.bounds.size.z:room.bounds.size.x)*.5f;
  for(int sign:{-1,1}){
   Vec3 a=center+forward*((halfDepth-6)*sign)+side*(halfWidth-6),b=center+forward*((halfDepth-6)*sign)-side*(halfWidth-6);
   if((a-b).LengthSq()<36||!spikeFits(a)||!spikeFits(b)||!distant(a)||!distant(b))continue;
   out.push_back({a,1,0,1,4});out.push_back({b,1,0,1,4});spikeCount+=2;break;
  }
 }
 for(auto t:candidates){
  if(Hash(seed,t.x,t.z)%9!=0||!clear(t))continue;
  auto p=d.TileToFloorTop(t);
  bool separated=true;for(auto a:out)if((a.position-p).LengthSq()<((a.kind==2)?81.f:144.f))separated=false;
  if(!separated)continue;
  bool safe=true;
  for(auto& start:d.assignedStartRooms)if(t.x>=start.bounds.xMin()-8&&t.x<start.bounds.xMax()+8&&t.z>=start.bounds.zMin()-8&&t.z<start.bounds.zMax()+8)safe=false;
  for(auto& r:d.rooms){
   if((r.type==ROOM_TYPE_START||r.type==ROOM_TYPE_EXIT||r.type==ROOM_TYPE_BOSS||r.type==ROOM_TYPE_REWARD) && r.floorTiles.count(t))safe=false;
   for(auto q:r.doorwayFloorTiles)if(std::abs(q.x-t.x)+std::abs(q.z-t.z)<9)safe=false;
  }
  if(!safe)continue;
  bool wantArrow=Hash(seed+91,t.x,t.z)%3!=0;
  if(wantArrow && arrowPlates<36){
   int emitters=0;
   for(auto dir:std::vector<IntVec3>{{1,0,0},{-1,0,0},{0,0,1},{0,0,-1}}){
    float distance=0;
    for(int n=1;n<=40;n++){
     IntVec3 q(t.x+dir.x*n,0,t.z+dir.z*n);
     if(d.IsWallTile({q.x,1,q.z})){if(n>=10)distance=n-.9f;break;}
     auto pos=d.TileToFloorTop(q);
     // Inspect the entire arrow corridor, not only the endpoints.
     if(blocked(pos,.4f)||blocked(pos+Vec3(dir.z*.4f,0,-dir.x*.4f),0)||blocked(pos+Vec3(-dir.z*.4f,0,dir.x*.4f),0))break;
    }
    if(distance>=9.1f){out.push_back({p,2,(float)dir.x,(float)dir.z,distance});if(++emitters==2)break;}
   }
   if(emitters>0)arrowPlates++;
  }
 }
 return out;
}
}
