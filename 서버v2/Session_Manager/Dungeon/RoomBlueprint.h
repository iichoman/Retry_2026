#pragma once
#include "DungeonTypes.h"
#include "RoomBlueprintData.h"
#include <algorithm>
#include <cmath>
namespace Blueprints {
inline unsigned Hash(int seed,int x,int z){unsigned h=(unsigned)seed^(unsigned)x*73856093u^(unsigned)z*19349663u;h^=h>>16;h*=0x85ebca6bu;return h^(h>>13);}
inline int Kind(int seed,const Room& r){return r.type==ROOM_TYPE_BOSS?4:r.type==ROOM_TYPE_REWARD?(r.bounds.size.x>=24&&r.bounds.size.z>=24&&Hash(seed^0x5641554c,r.bounds.xMin(),r.bounds.zMin())%2?8:5):r.type==ROOM_TYPE_EXIT?6:r.type==ROOM_TYPE_START?7:Hash(seed,r.bounds.xMin(),r.bounds.zMin())%4;}
inline IntVec3 Entrance(const Room& r){
 int bestSide=4,bestOffset=0x7fffffff;IntVec3 best(0,0,-1);
 for(auto t:r.doorwayFloorTiles){
  int distances[]={std::abs(t.z-r.bounds.zMin()),std::abs(t.x-r.bounds.xMin()),std::abs(t.x-(r.bounds.xMax()-1)),std::abs(t.z-(r.bounds.zMax()-1))};
  int side=0;for(int i=1;i<4;i++)if(distances[i]<distances[side])side=i;
  int offset=side==0||side==3?std::abs(t.x-(r.bounds.xMin()+r.bounds.size.x/2)):std::abs(t.z-(r.bounds.zMin()+r.bounds.size.z/2));
  if(side<bestSide||(side==bestSide&&offset<bestOffset)){bestSide=side;bestOffset=offset;best=side==0?IntVec3(0,0,-1):side==1?IntVec3(-1,0,0):side==2?IntVec3(1,0,0):IntVec3(0,0,1);}
 }return best;
}
inline IntVec3 ChestTile(const Room& r,int u,int v){
 int cx=r.bounds.xMin()+r.bounds.size.x/2,cz=r.bounds.zMin()+r.bounds.size.z/2;auto f=Entrance(r);
 int width=f.z!=0?r.bounds.size.x:r.bounds.size.z,depth=f.z!=0?r.bounds.size.z:r.bounds.size.x;
 int x=(u-10)*width/21,z=(v-10)*depth/21;
 return {cx-f.z*x-f.x*z,0,cz+f.x*x-f.z*z};
}
inline IntVec3 PropTile(const Room& r,int u,int v){return r.type==ROOM_TYPE_REWARD?ChestTile(r,u,v):IntVec3(r.bounds.xMin()+(2*u+1)*r.bounds.size.x/42,0,r.bounds.zMin()+(2*v+1)*r.bounds.size.z/42);}
inline IntVec3 Mirror(const Room& r,IntVec3 t){if(Entrance(r).z!=0)t.x=2*(r.bounds.xMin()+r.bounds.size.x/2)-t.x;else t.z=2*(r.bounds.zMin()+r.bounds.size.z/2)-t.z;return t;}
inline int GuardCount(int seed,const Room& r){return 1+(Hash(seed^0x47554152,r.bounds.xMin(),r.bounds.zMin())%2);}
inline IntVec3 GuardTile(int seed,const Room& r,int index){auto f=Entrance(r);int side=(index==0?1:-1)*(Kind(seed,r)==8?8:4);return {r.bounds.xMin()+r.bounds.size.x/2-f.z*side+f.x*2,0,r.bounds.zMin()+r.bounds.size.z/2+f.x*side+f.z*2};}
inline void Carve(Room& r,int cx,int cz,int radius){for(int x=cx-radius;x<=cx+radius;x++)for(int z=cz-radius;z<=cz+radius;z++){if(x<r.bounds.xMin()||x>=r.bounds.xMax()||z<r.bounds.zMin()||z>=r.bounds.zMax())continue;IntVec3 t(x,0,z);r.blockedTiles.erase(t);r.floorTiles.insert(t);}}
inline void Apply(int seed,Room& r,int corridorWidth){int kind=Kind(seed,r);r.floorTiles.clear();r.blockedTiles.clear();r.layoutType=LAYOUT_OPEN;for(int x=r.bounds.xMin();x<r.bounds.xMax();x++)for(int z=r.bounds.zMin();z<r.bounds.zMax();z++){int u=(x-r.bounds.xMin())*21/r.bounds.size.x,v=(z-r.bounds.zMin())*21/r.bounds.size.z;IntVec3 t(x,0,z);if(Plans[kind][v][u]=='#')r.blockedTiles.insert(t);else r.floorTiles.insert(t);}int cx=r.bounds.xMin()+r.bounds.size.x/2,cz=r.bounds.zMin()+r.bounds.size.z/2;for(auto d:r.doorwayFloorTiles){int x=d.x,z=d.z;bool xFirst=std::abs(x-cx)>=std::abs(z-cz);while(x!=cx||z!=cz){Carve(r,x,z,(std::max)(2,corridorWidth/2));if((xFirst&&x!=cx)||z==cz)x+=(cx>x)-(cx<x);else z+=(cz>z)-(cz<z);}Carve(r,cx,cz,2);}}
inline int Reward(const std::vector<Room>& rooms,int seed,bool selectNew=false){int id=-1;unsigned best=~0u;for(auto& r:rooms){if(!selectNew&&r.type==ROOM_TYPE_REWARD)return r.id;if(r.type!=ROOM_TYPE_NORMAL)continue;unsigned score=Hash(seed,r.bounds.xMin(),r.bounds.zMin())>>1;if(r.neighbors.size()>1)score|=0x80000000u;if(score<best){best=score;id=r.id;}}return id;}
}
