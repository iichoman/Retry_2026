using System;
using System.Collections.Generic;
using UnityEngine;
public static class RoomBlueprint {
 public static int Kind(int seed,Room r)=>r.type==RoomType.Boss?4:r.type==RoomType.Reward?(r.bounds.size.x>=24&&r.bounds.size.z>=24&&DungeonSetDressing.Hash(seed^0x5641554c,r.bounds.xMin,r.bounds.zMin)%2!=0?8:5):r.type==RoomType.Exit?6:r.type==RoomType.Start?7:(int)(DungeonSetDressing.Hash(seed,r.bounds.xMin,r.bounds.zMin)%4);
 public static Vector3Int Entrance(Room r){
  int bestSide=4,bestOffset=int.MaxValue;var best=Vector3Int.back;
  foreach(var t in r.doorwayFloorTiles){int[] distances={Math.Abs(t.z-r.bounds.zMin),Math.Abs(t.x-r.bounds.xMin),Math.Abs(t.x-(r.bounds.xMax-1)),Math.Abs(t.z-(r.bounds.zMax-1))};int side=0;for(int i=1;i<4;i++)if(distances[i]<distances[side])side=i;
   int offset=side==0||side==3?Math.Abs(t.x-(r.bounds.xMin+r.bounds.size.x/2)):Math.Abs(t.z-(r.bounds.zMin+r.bounds.size.z/2));
   if(side<bestSide||side==bestSide&&offset<bestOffset){bestSide=side;bestOffset=offset;best=side==0?Vector3Int.back:side==1?Vector3Int.left:side==2?Vector3Int.right:Vector3Int.forward;}
  }return best;
 }
 public static Vector3Int ChestTile(Room r,int u,int v){var f=Entrance(r);int width=f.z!=0?r.bounds.size.x:r.bounds.size.z,depth=f.z!=0?r.bounds.size.z:r.bounds.size.x;int x=(u-10)*width/21,z=(v-10)*depth/21;return new Vector3Int(r.bounds.xMin+r.bounds.size.x/2-f.z*x-f.x*z,0,r.bounds.zMin+r.bounds.size.z/2+f.x*x-f.z*z);}
 public static Vector3Int PropTile(Room r,int u,int v)=>r.type==RoomType.Reward?ChestTile(r,u,v):new Vector3Int(r.bounds.xMin+(2*u+1)*r.bounds.size.x/42,0,r.bounds.zMin+(2*v+1)*r.bounds.size.z/42);
 public static Vector3Int Mirror(Room r,Vector3Int t){if(Entrance(r).z!=0)t.x=2*(r.bounds.xMin+r.bounds.size.x/2)-t.x;else t.z=2*(r.bounds.zMin+r.bounds.size.z/2)-t.z;return t;}
 public static int GuardCount(int seed,Room r)=>1+(int)(DungeonSetDressing.Hash(seed^0x47554152,r.bounds.xMin,r.bounds.zMin)%2);
 public static Vector3Int GuardTile(int seed,Room r,int index){var f=Entrance(r);int side=(index==0?1:-1)*(Kind(seed,r)==8?8:4);return new Vector3Int(r.bounds.xMin+r.bounds.size.x/2-f.z*side+f.x*2,0,r.bounds.zMin+r.bounds.size.z/2+f.x*side+f.z*2);}
 public static void ApplyAll(DungeonGenerator_ChunkMesh g,IReadOnlyList<Room> rooms){for(int i=0;i<3;i++){int reward=DungeonSetDressing.RewardRoom(g,true);if(reward<0)break;foreach(var r in rooms)if(r.id==reward)r.type=RoomType.Reward;}foreach(var r in rooms){Apply(g.seed,r,g.corridorWidth);foreach(var c in g.Corridors)foreach(var t in c.floorTiles)if(t.x>=r.bounds.xMin&&t.x<r.bounds.xMax&&t.z>=r.bounds.zMin&&t.z<r.bounds.zMax){r.blockedTiles.Remove(t);r.floorTiles.Add(t);}}}
 public static void Apply(int seed,Room r,int corridorWidth){
  int kind=Kind(seed,r);r.floorTiles.Clear();r.blockedTiles.Clear();r.layoutType=RoomLayoutType.Open;
  for(int x=r.bounds.xMin;x<r.bounds.xMax;x++)for(int z=r.bounds.zMin;z<r.bounds.zMax;z++){int u=(x-r.bounds.xMin)*21/r.bounds.size.x,v=(z-r.bounds.zMin)*21/r.bounds.size.z;var t=new Vector3Int(x,0,z);if(RoomBlueprintData.Plans[kind][v][u]=='#')r.blockedTiles.Add(t);else r.floorTiles.Add(t);}
  int cx=r.bounds.xMin+r.bounds.size.x/2,cz=r.bounds.zMin+r.bounds.size.z/2;
  foreach(var d in r.doorwayFloorTiles){int x=d.x,z=d.z;bool xFirst=Math.Abs(x-cx)>=Math.Abs(z-cz);while(x!=cx||z!=cz){Carve(r,x,z,Math.Max(2,corridorWidth/2));if(xFirst&&x!=cx||z==cz)x+=Math.Sign(cx-x);else z+=Math.Sign(cz-z);}Carve(r,cx,cz,2);}
 }
 static void Carve(Room r,int cx,int cz,int radius){for(int x=cx-radius;x<=cx+radius;x++)for(int z=cz-radius;z<=cz+radius;z++){if(x<r.bounds.xMin||x>=r.bounds.xMax||z<r.bounds.zMin||z>=r.bounds.zMax)continue;var t=new Vector3Int(x,0,z);r.blockedTiles.Remove(t);r.floorTiles.Add(t);}}
 public static IEnumerable<(char marker,Vector3Int tile)> Sockets(int seed,Room r){var rows=RoomBlueprintData.Plans[Kind(seed,r)];var used=new HashSet<Vector3Int>();for(int v=0;v<21;v++)for(int u=0;u<21;u++){char c=rows[v][u];if(c=='.'||c=='#')continue;var t=PropTile(r,u,v);if(c=='L'||c=='N')t=ChestTile(r,u,v);if(used.Add(t))yield return(c,t);}}
 public static Vector3 Size(char c)=>c=='T'?new Vector3(2.4f,1.05f,1.2f):c=='C'?new Vector3(1.5f,1.2f,1.3f):c=='R'?new Vector3(1.6f,.9f,1.4f):c=='B'?new Vector3(2.2f,2.2f,.55f):new Vector3(.85f,1.1f,.85f);
 public static bool Safe(Room r,Vector3Int t,Func<Vector3Int,bool> isWall){if(Math.Abs(t.x-(r.bounds.xMin+r.bounds.size.x/2))<2||Math.Abs(t.z-(r.bounds.zMin+r.bounds.size.z/2))<2)return false;foreach(var d in r.doorwayFloorTiles)if(Math.Abs(d.x-t.x)+Math.Abs(d.z-t.z)<5)return false;for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)if(!r.floorTiles.Contains(t+new Vector3Int(x,0,z))||isWall(t+new Vector3Int(x,0,z)))return false;return true;}
}
