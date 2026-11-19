using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomBlueprintBuilder {
 const string Root="Assets/RoomBlueprints/Previews/";
 public static void BuildFurniture(){
  Directory.CreateDirectory(Root);Directory.CreateDirectory("Assets/Resources/RoomBlueprints");AssetDatabase.Refresh();var root=new GameObject("Bookcase");
  var wood=Resources.Load<Material>("EContent/Wood");var colors=new[]{new Color(.24f,.08f,.05f),new Color(.07f,.16f,.21f),new Color(.21f,.24f,.09f)};var leather=new Material[3];
  for(int i=0;i<3;i++){string path=Root+"BookLeather"+i+".mat";leather[i]=AssetDatabase.LoadAssetAtPath<Material>(path);if(leather[i]==null){leather[i]=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(leather[i],path);}leather[i].SetColor("_BaseColor",colors[i]);EditorUtility.SetDirty(leather[i]);}
  Action<string,Vector3,Vector3,Material> part=(name,pos,size,material)=>{var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform,false);go.transform.localPosition=pos;go.transform.localScale=size;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;};
  part("Back",new Vector3(0,0,-.47f),new Vector3(1,1,.06f),wood);for(int s=-1;s<=1;s+=2)part("Side",new Vector3(s*.47f,0,0),new Vector3(.06f,1,1),wood);
  for(int row=0;row<=4;row++)part("Shelf",new Vector3(0,-.4875f+row*.24375f,0),new Vector3(1,.025f,1),wood);
  for(int row=0;row<4;row++)for(int book=0;book<11;book++)part("Book",new Vector3(-.4f+book*.078f,-.37f+row*.24375f,0),new Vector3(.055f,.17f+(book%3)*.012f,.65f),leather[(row+book)%3]);
  foreach(var batch in root.GetComponentsInChildren<MeshRenderer>().GroupBy(r=>r.sharedMaterial).ToArray()){var mesh=new Mesh();mesh.CombineMeshes(batch.Select(r=>new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,transform=r.transform.localToWorldMatrix}).ToArray());var obj=new GameObject("Shelf mesh");obj.transform.SetParent(root.transform,false);obj.AddComponent<MeshFilter>().sharedMesh=SaveMesh(mesh,"Bookcase_"+batch.Key.name);obj.AddComponent<MeshRenderer>().sharedMaterial=batch.Key;foreach(var old in batch)UnityEngine.Object.DestroyImmediate(old.gameObject);}
  root.AddComponent<BoxCollider>().size=Vector3.one;
  PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/RoomBlueprints/Bookcase.prefab");UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();
 }
 static Mesh SaveMesh(Mesh mesh,string name){var path=Root+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);return old;}
 public static void Build(DungeonGenerator_ChunkMesh source){
  ValidateParity(source);
  Directory.CreateDirectory(Root);Directory.CreateDirectory("Artifacts/RoomBlueprints");AssetDatabase.Refresh();
  var preview=EditorSceneManager.NewPreviewScene();var previousMode=RenderSettings.ambientMode;var previousColor=RenderSettings.ambientLight;
  var scratch=GameObject.CreatePrimitive(PrimitiveType.Cube);var cube=scratch.GetComponent<MeshFilter>().sharedMesh;UnityEngine.Object.DestroyImmediate(scratch);
  try{for(int kind=0;kind<RoomBlueprintData.Plans.Length;kind++){
   int size=kind==4?56:32;var room=new Room{type=kind<4?RoomType.Normal:kind==4?RoomType.Boss:kind==5||kind==8?RoomType.Reward:kind==6?RoomType.Exit:RoomType.Start,bounds=new BoundsInt(0,0,0,size,7,size)};
   int seed=0;while(RoomBlueprint.Kind(seed,room)!=kind)seed++;
   room.doorwayFloorTiles.Add(new Vector3Int(size/2,0,0));if(kind<4){room.doorwayFloorTiles.Add(new Vector3Int(size/2,0,size-1));room.doorwayFloorTiles.Add(new Vector3Int(0,0,size/2));room.doorwayFloorTiles.Add(new Vector3Int(size-1,0,size/2));}
   RoomBlueprint.Apply(seed,room,6);
   var root=new GameObject(RoomBlueprintData.Names[kind]+"_WholeRoom");SceneManager.MoveGameObjectToScene(root,preview);
   var floors=new List<CombineInstance>();var walls=new List<CombineInstance>();
   for(int x=-1;x<=size;x++)for(int z=-1;z<=size;z++){
    var tile=new Vector3Int(x,0,z);bool floor=room.floorTiles.Contains(tile);
    if(floor)floors.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(new Vector3(x+.5f,.5f,z+.5f),Quaternion.identity,Vector3.one)});
    else if(room.blockedTiles.Contains(tile)||x==-1||z==-1||x==size||z==size){if((x<0||x==size)&&Mathf.Abs(z-size/2)<=3||(z<0||z==size)&&Mathf.Abs(x-size/2)<=3)continue;walls.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(new Vector3(x+.5f,4,z+.5f),Quaternion.identity,new Vector3(1,6,1))});}
   }
   Transform wallRoot=null;
   for(int layer=0;layer<2;layer++){var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes((layer==0?floors:walls).ToArray());var saved=SaveMesh(mesh,RoomBlueprintData.Names[kind]+(layer==0?"_Floor":"_Walls"));var go=new GameObject(layer==0?"Floor":"Walls");go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=saved;go.AddComponent<MeshRenderer>().sharedMaterial=layer==0?source.floorMaterial:source.wallMaterial;go.AddComponent<MeshCollider>().sharedMesh=saved;if(layer==1)wallRoot=go.transform;}
   foreach(var socket in RoomBlueprint.Sockets(seed,room)){
    if(socket.marker!='L'&&socket.marker!='N'&&!RoomBlueprint.Safe(room,socket.tile,t=>room.blockedTiles.Contains(t)))continue;
    GameObject asset=null;var dimensions=RoomBlueprint.Size(socket.marker);
    if(socket.marker=='L')asset=Resources.Load<GameObject>("EContent/ChestGold");
    else if(socket.marker=='N')asset=Resources.Load<GameObject>("EContent/Chest");
    else if(socket.marker=='B')asset=Resources.Load<GameObject>("RoomBlueprints/Bookcase");
    else asset=DungeonPropPalette.Load().Resolve(socket.marker=='T'?DungeonPropLayout.Kind.Table:socket.marker=='C'?DungeonPropLayout.Kind.Crate:socket.marker=='R'?DungeonPropLayout.Kind.Rock:DungeonPropLayout.Kind.Pot);
    if(asset==null)throw new Exception("Blueprint prop missing");var go=UnityEngine.Object.Instantiate(asset,root.transform);bool chest=socket.marker=='L'||socket.marker=='N';go.transform.localPosition=(Vector3)socket.tile+new Vector3(.5f,1+(chest?0:dimensions.y*.5f),.5f);if(!chest)go.transform.localScale=dimensions;else go.transform.rotation=Quaternion.LookRotation((Vector3)RoomBlueprint.Entrance(room));
   }
   foreach(var shelf in root.GetComponentsInChildren<Transform>().Where(t=>t.name=="Bookcase(Clone)"&&t.localPosition.z>size*.5f))shelf.localRotation=Quaternion.Euler(0,180,0);
   var marker=new GameObject(kind==4?"BossSpawn_ClearArena":kind==6?"SingleExitPortal_Centre":kind==7?"TeamSpawn_ClearZone":"MainRoute");marker.transform.SetParent(root.transform,false);marker.transform.localPosition=new Vector3(size/2f,1,size/2f);
   PrefabUtility.SaveAsPrefabAsset(root,Root+RoomBlueprintData.Names[kind]+".prefab");
   var camera=new GameObject("Plan Camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,preview);camera.scene=preview;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(preview);camera.orthographic=true;camera.orthographicSize=size*.65f;camera.transform.position=new Vector3(size*.5f,80,size*.5f-8);camera.transform.LookAt(new Vector3(size*.5f,0,size*.5f));camera.backgroundColor=new Color(.045f,.055f,.065f);camera.clearFlags=CameraClearFlags.SolidColor;
   var light=new GameObject("Plan light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,preview);light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(60,30,0);light.intensity=1.5f;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
   wallRoot.localScale=new Vector3(1,.3f,1);CombatContentBuilder.CaptureCamera(camera,"Artifacts/RoomBlueprints/"+RoomBlueprintData.Names[kind]+".png");
   UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(root);
  }AssetDatabase.SaveAssets();File.WriteAllText("Artifacts/RoomBlueprints/build.txt","PASS 8 complete floor/wall/furniture preview prefabs generated. Screenshots use lowered walls. Runtime uses the same cell plans and adapts only entrances.\n");}
  finally{EditorSceneManager.ClosePreviewScene(preview);RenderSettings.ambientMode=previousMode;RenderSettings.ambientLight=previousColor;}
 }
 static void ValidateParity(DungeonGenerator_ChunkMesh source){
  string path="Artifacts/E/server-room-plans.csv";if(!File.Exists(path))throw new Exception("Run v3 tests to export room plans first");
  var rows=File.ReadAllLines(path).Skip(1).Where(s=>s.Length>0).Select(s=>s.Split(',')).ToArray();var go=new GameObject("Blueprint parity");var g=go.AddComponent<DungeonGenerator_ChunkMesh>();EditorUtility.CopySerialized(source,g);g.randomizeSeedOnGenerate=false;g.generateOnStart=false;int checkedRooms=0;
  try{foreach(int seed in rows.Select(r=>int.Parse(r[0])).Distinct()){g.seed=seed;g.GenerateDataForValidation();foreach(var row in rows.Where(r=>int.Parse(r[0])==seed)){
   var room=g.Rooms.Single(r=>r.id==int.Parse(row[1]));uint hash=2166136261;unchecked{foreach(var t in room.floorTiles.OrderBy(t=>t.x).ThenBy(t=>t.z)){hash=(hash^(uint)t.x)*16777619;hash=(hash^(uint)t.z)*16777619;}}
   if(RoomBlueprint.Kind(seed,room)!=int.Parse(row[2])||room.bounds.xMin!=int.Parse(row[3])||room.bounds.zMin!=int.Parse(row[4])||room.bounds.size.x!=int.Parse(row[5])||room.bounds.size.z!=int.Parse(row[6])||room.floorTiles.Count!=int.Parse(row[7])||hash!=uint.Parse(row[8]))throw new Exception("Room plan differs from v3: server="+string.Join(",",row)+" client="+RoomBlueprint.Kind(seed,room)+","+room.bounds+","+room.floorTiles.Count+","+hash);checkedRooms++;
  }}Directory.CreateDirectory("Artifacts/RoomBlueprints");File.WriteAllText("Artifacts/RoomBlueprints/parity.txt","PASS "+checkedRooms+" room floor hashes/types/bounds match v3 across "+rows.Select(r=>r[0]).Distinct().Count()+" seeds.\n");}
  finally{UnityEngine.Object.DestroyImmediate(go);}
 }
}
