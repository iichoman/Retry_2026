using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
[RequireComponent(typeof(DungeonGenerator_ChunkMesh))]
public sealed class DungeonSetDressing:MonoBehaviour {
 public DungeonRoomSets catalog;
 DungeonGenerator_ChunkMesh generator;Transform root;
 readonly List<Mesh> ownedMeshes=new();
 static readonly Vector3Int[] Directions={Vector3Int.forward,Vector3Int.right,Vector3Int.back,Vector3Int.left};
 void OnEnable(){generator=GetComponent<DungeonGenerator_ChunkMesh>();generator.DungeonGenerated+=Generated;}
 void OnDisable(){if(generator!=null)generator.DungeonGenerated-=Generated;Clear();}
 void Generated(DungeonGenerator_ChunkMesh _)=>Rebuild();
 public static uint Hash(int seed,int x,int z){unchecked{uint h=(uint)seed^(uint)x*73856093u^(uint)z*19349663u;h^=h>>16;h*=0x85ebca6bu;return h^(h>>13);}}
 public static int RewardRoom(DungeonGenerator_ChunkMesh g,bool selectNew=false){if(!selectNew)foreach(var room in g.Rooms)if(room.type==RoomType.Reward)return room.id;int id=-1;uint best=uint.MaxValue;foreach(var r in g.Rooms)if(r.type==RoomType.Normal){uint score=Hash(g.seed,r.bounds.xMin,r.bounds.zMin)>>1;if(r.neighbors.Count>1)score|=0x80000000u;if(score<best){best=score;id=r.id;}}return id;}
 public void Rebuild(){
  Clear();generator=GetComponent<DungeonGenerator_ChunkMesh>();if(catalog==null)catalog=Resources.Load<DungeonRoomSets>("DungeonRoomSets");if(catalog==null||catalog.sets==null||catalog.sets.Length<4)return;
  root=new GameObject("DungeonSetDressing").transform;root.SetParent(transform,false);root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild=true;
  foreach(var room in generator.Rooms){
   int kind=RoomBlueprint.Kind(generator.seed,room);int style=catalog.sets.Length>=8?(kind==8?5:kind):(kind==5||kind==8?3:kind==1?1:kind==2||kind==3?2:0);
   var set=catalog.sets[style];var group=new GameObject($"Room_{room.id}_{set.label}").transform;group.SetParent(root,false);
   if(room.type==RoomType.Reward){PlaceRewardFeatures(room,set.feature,group);PlantRoomClusters(room,kind,group);Combine(group);continue;}
   var used=new List<Vector3>();
   // Hand-authored bays (25/50/75% of each wall), selected against the actual footprint.
   for(int side=0;side<4;side++)for(int bay=0;bay<3;bay++){
    var outward=Directions[side];Vector3 ideal=room.bounds.center;
    if(side%2==0){ideal.x=room.bounds.xMin+room.bounds.size.x*(.25f+.25f*bay);ideal.z=side==0?room.bounds.zMax:room.bounds.zMin;}
    else{ideal.z=room.bounds.zMin+room.bounds.size.z*(.25f+.25f*bay);ideal.x=side==1?room.bounds.xMax:room.bounds.xMin;}
    float best=25;Vector3Int tile=default;bool found=false;
    foreach(var t in room.floorTiles){if(room.blockedTiles.Contains(t)||!generator.IsWallTile(t+outward))continue;
     bool door=false;foreach(var d in room.doorwayFloorTiles)if((d-t).sqrMagnitude<36)door=true;if(door)continue;
     float dist=(new Vector2(t.x+.5f,t.z+.5f)-new Vector2(ideal.x,ideal.z)).sqrMagnitude;
     if(dist<best || found&&dist==best&&(t.x<tile.x||t.x==tile.x&&t.z<tile.z)){best=dist;tile=t;found=true;}}
    if(!found)continue;
    var face=generator.TileToWorld(tile)+new Vector3(.5f,0,.5f)+(Vector3)outward*.49f;bool overlap=false;foreach(var p in used)if((p-face).sqrMagnitude<49)overlap=true;if(overlap)continue;used.Add(face);
    var inward=-(Vector3)outward;var rot=Quaternion.LookRotation(inward);
    var prefab=bay==1?set.feature:null;
    if(prefab!=null){var obj=Instantiate(prefab,group);obj.name=$"{set.label}_Bay_{side}_{bay}";obj.transform.SetPositionAndRotation(face+inward*.45f+Vector3.up*2.7f,rot);PrepareFeature(obj);}
   }
   PlantRoomClusters(room,kind,group);
   Combine(group);
  }
 }
 static void PrepareFeature(GameObject obj){
  foreach(var collider in obj.GetComponentsInChildren<Collider>(true))collider.enabled=false;
  // Old composition prefabs contain their own cloth banners as well as separate banner slots.
  foreach(var child in obj.GetComponentsInChildren<Transform>(true))if(child!=obj.transform&&IsBanner(child.name))child.gameObject.SetActive(false);
 }
 public static bool IsBanner(string name)=>name.IndexOf("banner",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("flag",StringComparison.OrdinalIgnoreCase)>=0&&name.IndexOf("flagstone",StringComparison.OrdinalIgnoreCase)<0;
 public static Vector3Int MirrorWall(Room room,Vector3Int tile){if(RoomBlueprint.Entrance(room).z!=0)tile.x=room.bounds.xMin+room.bounds.xMax-1-tile.x;else tile.z=room.bounds.zMin+room.bounds.zMax-1-tile.z;return tile;}
 Vector3 MirrorPosition(Room room,Vector3 p){var center=generator.TileToWorld(new Vector3Int(room.bounds.xMin,0,room.bounds.zMin))+(Vector3)room.bounds.size*.5f;if(RoomBlueprint.Entrance(room).z!=0)p.x=2*center.x-p.x;else p.z=2*center.z-p.z;return p;}
 bool WallClear(Room room,Vector3Int tile,Vector3Int outward){
  if(!room.floorTiles.Contains(tile)||!generator.IsWallTile(tile+outward))return false;
  foreach(var door in room.doorwayFloorTiles)if((door-tile).sqrMagnitude<49)return false;
  return true;
 }
 void PlaceRewardFeatures(Room room,GameObject prefab,Transform group){
  if(prefab==null)return;var facing=RoomBlueprint.Entrance(room);var across=new Vector3Int(-facing.z,0,facing.x);
  // Matching wall bays are accepted together, so an extra entrance cannot leave an orphan decoration.
  foreach(int v in new[]{5,15}){
   var ideal=RoomBlueprint.ChestTile(room,1,v);Vector3Int best=default;float distance=36;bool found=false;
   foreach(var tile in room.floorTiles){
    if(!WallClear(room,tile,-across)||!WallClear(room,MirrorWall(room,tile),across))continue;
    float score=(tile-ideal).sqrMagnitude;
    if(score<distance||found&&score==distance&&(tile.x<best.x||tile.x==best.x&&tile.z<best.z)){distance=score;best=tile;found=true;}
   }
   if(!found)continue;
   for(int side=0;side<2;side++){
    var tile=side==0?best:MirrorWall(room,best);var inward=(Vector3)(side==0?across:-across);
    var obj=Instantiate(prefab,group);obj.name=$"RewardFeature_{v}_{side}";
    obj.transform.SetPositionAndRotation(generator.TileToWorld(tile)+new Vector3(.5f,2.7f,.5f)-inward*.04f,Quaternion.LookRotation(inward));PrepareFeature(obj);
   }
  }
 }
 // Select a few damp pockets per room; leave most walls and whole dry rooms bare.
 void PlantRoomClusters(Room room,int kind,Transform group){
  int[] chance={30,30,75,85,10,35,8,15,35};
  uint roomHash=Hash(generator.seed^0x504C414E,room.bounds.xMin,room.bounds.zMin);
  if(roomHash%100>=chance[Mathf.Clamp(kind,0,8)])return;
  bool paired=room.type==RoomType.Reward;
  var candidates=new List<Vector3Int>();
  foreach(var tile in room.floorTiles){
   if(room.blockedTiles.Contains(tile))continue;
   int walls=0;foreach(var dir in Directions)if(generator.IsWallTile(tile+dir))walls++;
   if(walls==0)continue;
   bool doorway=false;foreach(var door in room.doorwayFloorTiles)if((door-tile).sqrMagnitude<49)doorway=true;
   if(!doorway)candidates.Add(tile);
  }
  candidates.Sort((a,b)=>{uint ha=Hash(generator.seed^0x47524F57,a.x,a.z),hb=Hash(generator.seed^0x47524F57,b.x,b.z);int c=ha.CompareTo(hb);return c!=0?c:a.x!=b.x?a.x.CompareTo(b.x):a.z.CompareTo(b.z);});
  int budget=paired?1:1+(int)((roomHash>>8)%(kind==2||kind==3?3u:2u));
  var centers=new List<Vector3>();
  foreach(var tile in candidates){
   if(centers.Count>=budget)break;
   Vector3 inward=Vector3.zero;foreach(var dir in Directions)if(generator.IsWallTile(tile+dir))inward-=(Vector3)dir;
   if(inward.sqrMagnitude<.1f)continue;inward.Normalize();
   Vector3 center=generator.TileToWorldCenter(tile)+inward*2.35f+Vector3.up*1.02f;
   if(!PlantClear(room,center,.8f))continue;
   if(paired&&(!PlantClear(room,MirrorPosition(room,center),.8f)||(MirrorPosition(room,center)-center).sqrMagnitude<64))continue;
   bool close=false;foreach(var other in centers)if((other-center).sqrMagnitude<64)close=true;if(close)continue;
   centers.Add(center);var cluster=new GameObject("FoliagePocket_"+centers.Count).transform;cluster.SetParent(group,false);
   uint hash=Hash(generator.seed^0x4645524E,tile.x,tile.z);var random=new System.Random(unchecked((int)hash));
   var tangent=Vector3.Cross(Vector3.up,inward);int count=random.Next(9,15);
   for(int n=0;n<count;n++){
    float angle=(float)random.NextDouble()*Mathf.PI*2,radial=n==0?0:Mathf.Sqrt((float)random.NextDouble())*1.65f;
    Vector3 pos=center+tangent*(Mathf.Cos(angle)*radial)+inward*(Mathf.Sin(angle)*radial*.65f);
    bool large=n==0||n<3&&random.NextDouble()<.65;float scale=large?1.15f+(float)random.NextDouble()*.8f:.7f+(float)random.NextDouble()*.75f;
    float radius=large?1.25f*scale:.62f*scale;
    if(!PlantClear(room,pos,radius)||paired&&!PlantClear(room,MirrorPosition(room,pos),radius))continue;
    float yaw=(float)random.NextDouble()*360;var prefab=large?catalog.largeBush:catalog.smallBush;
    Plant(prefab,cluster,pos,Quaternion.Euler(0,yaw,0),scale);
    if(paired)Plant(prefab,cluster,MirrorPosition(room,pos),Quaternion.Euler(0,RoomBlueprint.Entrance(room).z!=0?-yaw:180-yaw,0),scale);
   }
   if((kind==2||kind==3)&&random.NextDouble()<.65)Plant(catalog.moss,cluster,center-Vector3.up*.01f,Quaternion.Euler(0,random.Next(360),0),1.1f);
   // Ceiling growth occurs only above some selected damp pockets, never every corner.
   if(!paired&&random.NextDouble()<.4){
    var top=generator.TileToWorldCenter(tile)+inward*.5f+Vector3.up*generator.mapHeight;
    Plant(catalog.hangingBush,cluster,top,Quaternion.Euler(180,random.Next(360),0),.85f+(float)random.NextDouble()*.65f);
    if(random.NextDouble()<.4)Plant(catalog.hangingBush,cluster,top+tangent*.6f,Quaternion.Euler(180,random.Next(360),0),.45f);
   }
  }
 }
 bool PlantClear(Room room,Vector3 position,float radius){
  foreach(var offset in new[]{Vector3.zero,Vector3.right*radius,Vector3.left*radius,Vector3.forward*radius,Vector3.back*radius}){
   var tile=generator.WorldToTile(position+offset);tile.y=0;
   if(!room.floorTiles.Contains(tile)||generator.IsWallTile(tile))return false;
   foreach(var door in room.doorwayFloorTiles)if((door-tile).sqrMagnitude<36)return false;
  }
  foreach(var socket in RoomBlueprint.Sockets(generator.seed,room)){
   var prop=generator.TileToWorldCenter(socket.tile);var size=RoomBlueprint.Size(socket.marker);
   if(Mathf.Abs(position.x-prop.x)<size.x*.5f+radius&&Mathf.Abs(position.z-prop.z)<size.z*.5f+radius)return false;
  }
  return true;
 }
 void Plant(GameObject prefab,Transform parent,Vector3 position,Quaternion rotation,float scale){if(prefab==null)return;var obj=Instantiate(prefab,parent);obj.transform.SetPositionAndRotation(position,rotation);obj.transform.localScale=Vector3.one*scale;}
 void Combine(Transform group){
  var batches=new Dictionary<Material,List<CombineInstance>>();
  foreach(var r in group.GetComponentsInChildren<MeshRenderer>()){var filter=r.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;for(int i=0;i<filter.sharedMesh.subMeshCount;i++){var mat=r.sharedMaterials[Mathf.Min(i,r.sharedMaterials.Length-1)];if(!batches.TryGetValue(mat,out var list)){list=new List<CombineInstance>();batches.Add(mat,list);}list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=i,transform=group.worldToLocalMatrix*r.transform.localToWorldMatrix});}r.enabled=false;}
  foreach(var batch in batches){var mesh=new Mesh{name="Room set batch",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(batch.Value.ToArray());ownedMeshes.Add(mesh);var obj=new GameObject("Batch_"+batch.Key.name);obj.transform.SetParent(group,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=batch.Key;}
 }
 public void Clear(){if(root==null)root=transform.Find("DungeonSetDressing");if(root!=null){root.gameObject.SetActive(false);if(Application.isPlaying)Destroy(root.gameObject);else DestroyImmediate(root.gameObject);}root=null;foreach(var m in ownedMeshes)if(m!=null){if(Application.isPlaying)Destroy(m);else DestroyImmediate(m);}ownedMeshes.Clear();}
}
