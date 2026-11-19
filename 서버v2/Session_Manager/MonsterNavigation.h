#pragma once
#include "PositionValidator.h"
#include "Dungeon/DungeonGenerator.h"
#include <algorithm>
#include <queue>
#include <unordered_map>
#include <vector>
namespace MonsterNavigation {
constexpr int MaxExpanded=2048;
constexpr long long RepathMs=750;
inline bool Clear(const DungeonGenerator& dungeon,const Vec3& from,const Vec3& to){
    float distance=from.DistanceXZ(to);int steps=(std::max)(1,(int)std::ceil(distance/.3f));
    for(int n=1;n<=steps;n++){float t=(float)n/steps;Vec3 p=from+(to-from)*t;if(!PositionValidator::IsFloorPosition(dungeon,p))return false;}
    return true;
}
inline bool Walkable(const DungeonGenerator& dungeon,IntVec3 tile){
    auto p=dungeon.TileToFloorTop(tile);
    if(!PositionValidator::IsFloorPosition(dungeon,p))return false;
    // Keep the body clear of masonry corners; furniture already includes actor radius.
    for(auto offset:std::vector<Vec3>{{.35f,0,0},{-.35f,0,0},{0,0,.35f},{0,0,-.35f}}){auto t=dungeon.WorldToTile(p+offset);t.y=0;if(!dungeon.IsFloorTile(t)||dungeon.IsWallTile(t))return false;}
    return true;
}
inline std::vector<Vec3> Find(const DungeonGenerator& dungeon,const Vec3& from,const Vec3& to){
    IntVec3 start=dungeon.WorldToTile(from),goal=dungeon.WorldToTile(to);start.y=goal.y=0;
    struct Node {IntVec3 tile;int g,f;};
    struct Later {bool operator()(const Node&a,const Node&b)const{return a.f!=b.f?a.f>b.f:a.tile.x!=b.tile.x?a.tile.x>b.tile.x:a.tile.z>b.tile.z;}};
    std::priority_queue<Node,std::vector<Node>,Later> open;
    std::unordered_map<IntVec3,int> cost;std::unordered_map<IntVec3,IntVec3> parent;
    auto heuristic=[&](IntVec3 t){return std::abs(t.x-goal.x)+std::abs(t.z-goal.z);};
    open.push({start,0,heuristic(start)});cost[start]=0;int expanded=0;
    while(!open.empty()&&expanded++<MaxExpanded){auto node=open.top();open.pop();if(cost[node.tile]!=node.g)continue;
        if(node.tile==goal){std::vector<Vec3> result;auto current=goal;while(!(current==start)){result.push_back(dungeon.TileToFloorTop(current));current=parent.at(current);}std::reverse(result.begin(),result.end());return result;}
        for(auto dir:std::vector<IntVec3>{{1,0,0},{0,0,1},{-1,0,0},{0,0,-1}}){IntVec3 next(node.tile.x+dir.x,0,node.tile.z+dir.z);
            if(!Walkable(dungeon,next))continue;int g=node.g+1;auto old=cost.find(next);if(old!=cost.end()&&old->second<=g)continue;
            cost[next]=g;parent[next]=node.tile;open.push({next,g,g+heuristic(next)});
        }
    }
    return {};
}
}
