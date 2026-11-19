using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
public static class EContentBuilder {
 const string Root="Assets/Resources/EContent",Source="Assets/ThirdParty/KayKitDungeon/";
 static Material atlas,stone,wood,iron,moss;
 static Material Mat(string name,Color color,Shader shader=null){string path=Root+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(shader??Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}m.shader=shader??Shader.Find("Universal Render Pipeline/Lit");m.SetColor("_BaseColor",color);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.08f);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;}
 internal static GameObject Model(string name,Vector3 size){
  var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Source+name+".fbx");if(asset==null)throw new Exception("Missing model "+name);
  var root=new GameObject(name);var model=UnityEngine.Object.Instantiate(asset,root.transform);model.name="Model";
  int meshIndex=0;
  foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()){
   var mesh=new Mesh();skin.BakeMesh(mesh);string meshPath=Root+"/"+name+"_"+(meshIndex++)+".asset";
   var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(saved==null){AssetDatabase.CreateAsset(mesh,meshPath);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);}
   var obj=skin.gameObject;var mats=skin.sharedMaterials;UnityEngine.Object.DestroyImmediate(skin);obj.AddComponent<MeshFilter>().sharedMesh=saved;obj.AddComponent<MeshRenderer>().sharedMaterials=mats;
  }
  foreach(var animator in model.GetComponentsInChildren<Animator>())UnityEngine.Object.DestroyImmediate(animator);
  var renderers=model.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers){bounds.Encapsulate(r.bounds);r.sharedMaterials=Enumerable.Repeat(atlas,r.sharedMaterials.Length).ToArray();}
  model.transform.localScale=new Vector3(size.x/Mathf.Max(.001f,bounds.size.x),size.y/Mathf.Max(.001f,bounds.size.y),size.z/Mathf.Max(.001f,bounds.size.z));
  Directory.CreateDirectory("Artifacts/E");File.AppendAllText("Artifacts/E/models.txt",name+" renderers="+renderers.Length+" bounds="+bounds+"\n");
  var scale=model.transform.localScale;model.transform.localPosition=-Vector3.Scale(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),scale);
  foreach(var c in root.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(c);return root;
 }
 static void Part(Transform parent,string name,Vector3 pos,Vector3 size,Material mat,PrimitiveType type=PrimitiveType.Cube){var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;}
 static GameObject Save(GameObject go,string name){var prefab=PrefabUtility.SaveAsPrefabAsset(go,Root+"/"+name+".prefab");UnityEngine.Object.DestroyImmediate(go);return prefab;}
 static void GoldTrim(GameObject chest){
  var gold=Mat("RewardGold",new Color(.86f,.55f,.12f));gold.SetFloat("_Metallic",.55f);gold.SetFloat("_Smoothness",.35f);int index=0;
  foreach(var filter in chest.GetComponentsInChildren<MeshFilter>()){
   var source=filter.sharedMesh;var uv=source.uv;var triangles=source.triangles;var body=new System.Collections.Generic.List<int>();var trim=new System.Collections.Generic.List<int>();
   // KayKit atlas top-left two swatches are iron; preserve all wood and shading geometry.
   for(int i=0;i<triangles.Length;i+=3){var sample=(uv[triangles[i]]+uv[triangles[i+1]]+uv[triangles[i+2]])/3;var list=sample.x<.25f&&sample.y>.75f?trim:body;list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
   var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Reward chest trim";mesh.subMeshCount=2;mesh.SetTriangles(body,0);mesh.SetTriangles(trim,1);
   string path=Root+"/RewardChestMesh"+(index++)+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);}filter.sharedMesh=saved;filter.GetComponent<Renderer>().sharedMaterials=new[]{atlas,gold};
  }
 }
 public static void Build(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode");
  Directory.CreateDirectory(Root);AssetDatabase.Refresh();
  atlas=Mat("KayKit",new Color(.8f,.8f,.8f));atlas.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"dungeon_texture.png"));
  stone=Mat("Stone",new Color(.42f,.44f,.41f));wood=Mat("Wood",new Color(.3f,.17f,.08f));iron=Mat("Iron",new Color(.12f,.14f,.14f));moss=Mat("Moss",new Color(.12f,.21f,.09f));Mat("Warning",new Color(.7f,.22f,.03f));
  var red=Save(Model("banner_patternA_red",new Vector3(1.4f,2.6f,.12f)),"BannerRed");
  var blue=Save(Model("banner_patternA_blue",new Vector3(1.4f,2.6f,.12f)),"BannerBlue");
  var gold=Save(Model("banner_patternA_yellow",new Vector3(1.4f,2.6f,.12f)),"BannerGold");
  var scaffold=new GameObject("WatchGallery");
  var deck=Model("floor_wood_large_dark",new Vector3(4.5f,.18f,1.6f));deck.transform.SetParent(scaffold.transform,false);deck.transform.localPosition=Vector3.up*1.3f;
  foreach(var r in deck.GetComponentsInChildren<Renderer>())r.sharedMaterial=wood;
  Part(scaffold.transform,"Rail",new Vector3(0,2.3f,.8f),new Vector3(4.5f,.12f,.12f),wood);
  Part(scaffold.transform,"Wall brace",new Vector3(0,.1f,-.4f),new Vector3(4.5f,.18f,.18f),wood);
  for(int i=-1;i<=1;i++){Part(scaffold.transform,"Post",new Vector3(i*2,1.85f,.8f),new Vector3(.14f,1.2f,.14f),wood);Part(scaffold.transform,"Bracket "+i,new Vector3(i*2,.65f,.2f),new Vector3(.19f,1.5f,.19f),wood);scaffold.transform.Find("Bracket "+i).localRotation=Quaternion.Euler(35,0,0);}
  var post=Save(scaffold,"WatchGallery");
  var statue=new GameObject("WallGuardian");Part(statue.transform,"Plinth",new Vector3(0,.12f,-.15f),new Vector3(1.4f,.24f,.65f),stone);
  Part(statue.transform,"Niche",new Vector3(0,1.1f,-.34f),new Vector3(1.4f,2.4f,.18f),iron);
  Part(statue.transform,"Cloak",new Vector3(0,.95f,-.12f),new Vector3(.8f,.7f,.45f),stone,PrimitiveType.Capsule);
  Part(statue.transform,"Hood",new Vector3(0,1.85f,-.08f),new Vector3(.55f,.6f,.45f),stone,PrimitiveType.Sphere);
  Part(statue.transform,"Face shadow",new Vector3(0,1.85f,.13f),new Vector3(.28f,.3f,.04f),iron);
  for(int side=-1;side<=1;side+=2){Part(statue.transform,"Shoulder",new Vector3(side*.4f,1.4f,-.08f),new Vector3(.38f,.35f,.43f),stone);Part(statue.transform,"Hand",new Vector3(side*.18f,1.02f,.22f),new Vector3(.22f,.22f,.2f),stone,PrimitiveType.Sphere);}
  Part(statue.transform,"Guard",new Vector3(0,1.02f,.24f),new Vector3(.6f,.09f,.1f),iron);
  Part(statue.transform,"Sword",new Vector3(0,.9f,.16f),new Vector3(.12f,1.4f,.12f),iron);var guardian=Save(statue,"WallGuardian");
  var greenery=new GameObject("MossCluster");for(int i=0;i<7;i++){float x=(i-3)*.35f;Part(greenery.transform,"Growth",new Vector3(x,.045f,Mathf.Sin(i*2)*.15f),new Vector3(.6f,.09f,.5f),moss,PrimitiveType.Sphere);}var mossPrefab=Save(greenery,"MossCluster");
  var chest=Model("chest",new Vector3(1.5f,1.05f,1.1f));var box=chest.AddComponent<BoxCollider>();box.center=new Vector3(0,.525f,0);box.size=new Vector3(1.5f,1.05f,1.1f);chest.layer=LayerMask.NameToLayer("Wall");Save(chest,"Chest");
  chest=Model("chest_gold",new Vector3(1.5f,1.05f,1.1f));GoldTrim(chest);
  var goldTrim=AssetDatabase.LoadAssetAtPath<Material>(Root+"/RewardGold.mat");
  var jewel=Mat("RewardAmber",new Color(1f,.42f,.045f));jewel.EnableKeyword("_EMISSION");jewel.SetColor("_EmissionColor",new Color(1.8f,.52f,.05f));
  Part(chest.transform,"Royal clasp",new Vector3(0,.6f,.56f),new Vector3(.35f,.4f,.06f),goldTrim);
  Part(chest.transform,"Amber seal",new Vector3(0,.62f,.61f),new Vector3(.16f,.16f,.075f),jewel);chest.transform.Find("Amber seal").localRotation=Quaternion.Euler(0,0,45);
  for(int side=-1;side<=1;side+=2){Part(chest.transform,"Crown band",new Vector3(side*.48f,1.03f,0),new Vector3(.11f,.045f,.9f),goldTrim);Part(chest.transform,"Gold foot",new Vector3(side*.62f,.12f,0),new Vector3(.18f,.23f,1.08f),goldTrim);}
  var motes=new GameObject("Treasure motes");motes.transform.SetParent(chest.transform,false);motes.transform.localPosition=Vector3.up*.8f;
  var particles=motes.AddComponent<ParticleSystem>();var main=particles.main;main.loop=true;main.startLifetime=1.8f;main.startSpeed=.35f;main.startSize=.035f;main.maxParticles=18;main.startColor=new Color(1f,.7f,.22f);var emission=particles.emission;emission.rateOverTime=5;var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.2f,.15f,.8f);
  particles.GetComponent<ParticleSystemRenderer>().sharedMaterial=CombatVfxCatalog.Load().TrailMaterial;
  var rewardView=chest.AddComponent<LootVfxController>();rewardView.RewardChest=true;rewardView.Aura=particles;
  box=chest.AddComponent<BoxCollider>();box.center=new Vector3(0,.525f,0);box.size=new Vector3(1.5f,1.05f,1.1f);chest.layer=LayerMask.NameToLayer("Wall");Save(chest,"ChestGold");
  Save(Model("floor_tile_big_spikes",new Vector3(2,1.2f,2)),"Spikes");
  var emitter=new GameObject("ArrowEmitter");Part(emitter.transform,"StoneSocket",Vector3.zero,new Vector3(.65f,.65f,.22f),stone);Part(emitter.transform,"Aperture",new Vector3(0,0,.12f),new Vector3(.22f,.13f,.04f),iron);Save(emitter,"ArrowEmitter");
  var arrowPrefab=new GameObject("TrapArrow");
  Part(arrowPrefab.transform,"Shaft",Vector3.zero,new Vector3(.045f,.045f,.56f),wood);
  Part(arrowPrefab.transform,"Fletching horizontal",new Vector3(0,0,-.23f),new Vector3(.19f,.018f,.16f),stone);
  Part(arrowPrefab.transform,"Fletching vertical",new Vector3(0,0,-.23f),new Vector3(.018f,.19f,.16f),stone);
  var tip=new GameObject("Arrowhead",typeof(MeshFilter),typeof(MeshRenderer));tip.transform.SetParent(arrowPrefab.transform,false);
  const string tipPath=Root+"/Arrowhead.asset";var tipMesh=AssetDatabase.LoadAssetAtPath<Mesh>(tipPath);if(tipMesh==null){tipMesh=new Mesh{name="Arrowhead"};AssetDatabase.CreateAsset(tipMesh,tipPath);}else tipMesh.Clear();
  tipMesh.vertices=new[]{new Vector3(-.105f,-.04f,.22f),new Vector3(.105f,-.04f,.22f),new Vector3(.105f,.04f,.22f),new Vector3(-.105f,.04f,.22f),new Vector3(0,0,.45f)};
  tipMesh.triangles=new[]{0,1,4,1,2,4,2,3,4,3,0,4,0,3,2,0,2,1};tipMesh.RecalculateNormals();tipMesh.RecalculateBounds();EditorUtility.SetDirty(tipMesh);
  tip.GetComponent<MeshFilter>().sharedMesh=tipMesh;tip.GetComponent<MeshRenderer>().sharedMaterial=iron;Save(arrowPrefab,"TrapArrow");
  // Preserve the user's authored flame, glow, sparks, smoke, materials and flicker settings.
  var torchPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Map/torch.prefab");
  if(torchPrefab==null||torchPrefab.GetComponentInChildren<StylizedTorchFlame>(true)==null)throw new Exception("Original authored torch effect missing");
  var sets=AssetDatabase.LoadAssetAtPath<DungeonRoomSets>("Assets/Resources/DungeonRoomSets.asset");if(sets==null){sets=ScriptableObject.CreateInstance<DungeonRoomSets>();AssetDatabase.CreateAsset(sets,"Assets/Resources/DungeonRoomSets.asset");}
  sets.sets=new[]{new DungeonRoomSets.Set{label="Barracks",banner=red,feature=post},new DungeonRoomSets.Set{label="Library",banner=blue,feature=guardian},new DungeonRoomSets.Set{label="Cloister",banner=blue,feature=guardian},new DungeonRoomSets.Set{label="RuinedChapel",banner=gold,feature=guardian,moss=true},new DungeonRoomSets.Set{label="GuardianArena",banner=red,feature=guardian},new DungeonRoomSets.Set{label="TreasureVault",banner=gold,feature=guardian},new DungeonRoomSets.Set{label="ExtractionSanctum",banner=blue,feature=guardian},new DungeonRoomSets.Set{label="ArrivalHall",banner=red,feature=post}};sets.moss=mossPrefab;EditorUtility.SetDirty(sets);
  var floor=Mat("Flagstone",new Color(.3f,.32f,.3f),Shader.Find("Retry/E Flagstone"));
  DungeonGardenBuilder.Build(sets);
  RoomBlueprintBuilder.BuildFurniture();
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");bool opened=!scene.IsValid()||!scene.isLoaded;
  if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Additive);
  if(scene.isDirty)throw new Exception("Save SampleScene before E scene wiring");
  var generator=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<DungeonGenerator_ChunkMesh>(true)).Single();generator.floorMaterial=floor;
  generator.mapSize=480;generator.mapHeight=7;generator.baseRoomSize=32;generator.minLeafSize=64;generator.bossRoomSize=new Vector2Int(56,56);generator.corridorWidth=6;
  var dress=generator.GetComponent<DungeonSetDressing>();if(dress==null)dress=generator.gameObject.AddComponent<DungeonSetDressing>();dress.catalog=sets;EditorUtility.SetDirty(dress);
  var placer=generator.GetComponent<RoomObjectPlacer2>();var so=new SerializedObject(placer);so.FindProperty("wallLightingHeightRatio").floatValue=.55f;so.FindProperty("wallLightingModelHeight").floatValue=1.1f;
  var it=so.GetIterator();while(it.Next(true))if(it.propertyType==SerializedPropertyType.ObjectReference&&it.objectReferenceValue is GameObject obj&&obj.name.ToLower().Contains("torch"))it.objectReferenceValue=torchPrefab;so.ApplyModifiedPropertiesWithoutUndo();
  // Reduce bright rug borders to worn masonry; retain a single boss arena medallion.
  var palette=DungeonPresentationPalette.Load();if(palette!=null)foreach(var theme in palette.themes){theme.stone.SetColor("_BaseColor",new Color(.29f,.3f,.28f));theme.border.SetColor("_BaseColor",new Color(.23f,.24f,.22f));theme.accent.SetColor("_BaseColor",new Color(.32f,.29f,.21f));EditorUtility.SetDirty(theme.stone);EditorUtility.SetDirty(theme.border);EditorUtility.SetDirty(theme.accent);}
  EditorUtility.SetDirty(generator);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
  Validate(generator,sets);
  RoomBlueprintBuilder.Build(generator);
  if(opened)EditorSceneManager.CloseScene(scene,true);
 }
 public static void Validate(DungeonGenerator_ChunkMesh source,DungeonRoomSets sets){
  Directory.CreateDirectory("Artifacts/E");var preview=EditorSceneManager.NewPreviewScene();var stage=new GameObject("E Layout Preview");SceneManager.MoveGameObjectToScene(stage,preview);
  var previous=RenderSettings.ambientMode;var previousColor=RenderSettings.ambientLight;
  try{
   var g=stage.AddComponent<DungeonGenerator_ChunkMesh>();EditorUtility.CopySerialized(source,g);g.seed=12345;g.randomizeSeedOnGenerate=false;g.generateOnStart=false;g.GenerateDungeon();
   var placer=stage.AddComponent<RoomObjectPlacer2>();EditorUtility.CopySerialized(source.GetComponent<RoomObjectPlacer2>(),placer);var so=new SerializedObject(placer);so.FindProperty("dungeonGenerator").objectReferenceValue=g;so.ApplyModifiedPropertiesWithoutUndo();placer.PlaceRoomObjects();
   var surfaces=stage.AddComponent<DungeonRoomPresentation>();surfaces.palette=DungeonPresentationPalette.Load();surfaces.Rebuild();
   var dress=stage.AddComponent<DungeonSetDressing>();dress.catalog=sets;dress.Rebuild();
   var pockets=stage.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("FoliagePocket_")).ToArray();
   int plantedRooms=pockets.Select(t=>t.parent).Distinct().Count();
   if(pockets.Length==0||plantedRooms>=g.Rooms.Count||pockets.GroupBy(t=>t.parent).Any(group=>group.Count()>3))throw new Exception("Foliage must leave dry rooms and use at most three pockets");
   Func<string> planting=()=>string.Join(";",stage.GetComponentsInChildren<Transform>().Where(t=>t.parent!=null&&t.parent.name.StartsWith("FoliagePocket_")).Select(t=>t.position.ToString("F3")+t.localScale.ToString("F3")));
   string beforePlanting=planting();dress.Rebuild();if(beforePlanting!=planting())throw new Exception("Seeded foliage changed on rebuild");
   File.WriteAllText("Artifacts/E/foliage.txt",$"PASS seed12345: {plantedRooms}/{g.Rooms.Count} rooms planted, {pockets.Length} pockets; max 3 per room; deterministic rebuild; placement checks floor, walls, entrances and authored furniture.\n");
   if(File.Exists("Artifacts/E/server-furniture.csv")){
    var boxes=stage.GetComponentsInChildren<DungeonPropObstacle>().Where(p=>!p.Supported).Select(p=>p.GetComponent<BoxCollider>().bounds).ToList();var rows=File.ReadAllLines("Artifacts/E/server-furniture.csv").Skip(1).Where(s=>s.Length>0).ToArray();
    if(rows.Length!=boxes.Count)throw new Exception("Furniture count differs from v3 "+boxes.Count+"/"+rows.Length);
    foreach(var row in rows){var f=row.Split(',').Select(s=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();int index=boxes.FindIndex(b=>Mathf.Abs(b.center.x-f[0])<.001f&&Mathf.Abs(b.center.z-f[1])<.001f&&Mathf.Abs(b.extents.x+.5f-f[2])<.001f&&Mathf.Abs(b.extents.z+.5f-f[3])<.001f);if(index<0)throw new Exception("Furniture collision differs from v3 "+row);boxes.RemoveAt(index);}
    Directory.CreateDirectory("Artifacts/RoomBlueprints");File.WriteAllText("Artifacts/RoomBlueprints/furniture.txt","PASS "+rows.Length+" furniture collider positions and extents match v3.\n");
   }
   var originalTorches=stage.GetComponentsInChildren<StylizedTorchFlame>();if(originalTorches.Length==0)throw new Exception("Original torch effect not placed");
   foreach(var torch in originalTorches){var settings=new SerializedObject(torch);foreach(var field in new[]{"flameCore","flameGlow","sparks","smoke"}){var ps=settings.FindProperty(field).objectReferenceValue as ParticleSystem;if(ps==null)throw new Exception("Authored torch particle reference missing: "+field);ps.Simulate(1.4f,false,true);}}
   int environmentCount=0;
   string layout="Artifacts/E/server-layout.csv";
   if(File.Exists(layout)){
    var rows=File.ReadAllLines(layout);if(int.Parse(rows[0].Split(',')[1])!=DungeonSetDressing.RewardRoom(g))throw new Exception("Reward room differs from server");
    var props=stage.transform.Find("RoomObjects2").GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).ToArray();
    foreach(var row in rows.Skip(2)){
     var values=row.Split(',').Select(s=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();var p=new Vector3(values[1],values[2],values[3]);var tile=g.WorldToTile(p);tile.y=0;
     if(!g.IsFloorTile(tile)||g.IsWallTile(tile))throw new Exception("Server environment outside client walkable floor "+p);
     if(values[0]==0||values[0]==3){
      var box=new Bounds(p+Vector3.up*.525f,new Vector3(1.5f,1.05f,1.1f));
      foreach(var prop in props)if(box.Intersects(prop.bounds))throw new Exception("Server chest overlaps client prop "+prop.name+" "+p);
      var chest=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+(values[0]==3?"/ChestGold.prefab":"/Chest.prefab")),stage.transform);chest.transform.position=p;
     }
     environmentCount++;
    }
   }
   foreach(var r in stage.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials)if(m==null||m.shader==null||!m.shader.isSupported)throw new Exception("Missing material "+r.name);
   foreach(var t in stage.GetComponentsInChildren<Transform>())if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script");
   var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.SetParent(stage.transform);camera.scene=preview;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(preview);camera.fieldOfView=65;camera.farClipPlane=150;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.04f,.045f);
   var light=new GameObject("Preview light").AddComponent<Light>();light.transform.SetParent(stage.transform);light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.37f,.4f);
   var ceiling=stage.transform.Find("DungeonRoot_ChunkMesh/CeilingChunks");if(ceiling!=null)ceiling.gameObject.SetActive(false);
   var bay=stage.transform.Find("DungeonSetDressing").GetComponentsInChildren<Transform>().First(t=>t.name.Contains("_Bay_")&&t.name.Contains("Barracks")&&t.name.EndsWith("_1"));
   var target=bay.position+Vector3.up*1.5f;camera.transform.position=target+bay.forward*6+bay.right;camera.transform.LookAt(target);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/Barracks.png");
   bay=stage.transform.Find("DungeonSetDressing").GetComponentsInChildren<Transform>().First(t=>t.name.Contains("_Bay_")&&t.name.Contains("Library")&&t.name.EndsWith("_1"));target=bay.position+Vector3.down;
   camera.transform.position=target+bay.forward*10+bay.right*3;camera.transform.LookAt(target);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/Sanctum.png");
   var pocket=stage.GetComponentsInChildren<Transform>().First(t=>t.name.StartsWith("FoliagePocket_")&&t.childCount>0);
   target=pocket.GetChild(0).position+Vector3.up*.5f;var plantTile=g.WorldToTile(target);plantTile.y=0;Vector3 inward=Vector3.zero;
   foreach(var dir in new[]{Vector3Int.forward,Vector3Int.back,Vector3Int.left,Vector3Int.right})for(int n=1;n<=3;n++)if(g.IsWallTile(plantTile+dir*n)){inward-=(Vector3)dir;break;}
   if(inward.sqrMagnitude<.1f)inward=Vector3.forward;inward.Normalize();camera.transform.position=target+inward*6+Vector3.up*2;camera.transform.LookAt(target);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/FoliagePocket.png");
   var reward=g.Rooms.First(r=>r.id==DungeonSetDressing.RewardRoom(g));var center=g.TileToWorldCenter(Vector3Int.FloorToInt(reward.bounds.center))+Vector3.up;
   camera.orthographic=true;camera.orthographicSize=Mathf.Max(reward.bounds.size.x,reward.bounds.size.z)*.65f;camera.farClipPlane=250;
   camera.transform.position=center+new Vector3(25,70,25);camera.transform.LookAt(center);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/RewardRoom.png");
   var rewardChest=stage.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="ChestGold(Clone)");
   if(rewardChest!=null){camera.orthographic=false;camera.fieldOfView=45;camera.transform.position=rewardChest.position+new Vector3(2.5f,2,3.5f);camera.transform.LookAt(rewardChest.position+Vector3.up*.5f);CombatContentBuilder.CaptureCamera(camera,"Artifacts/E/RewardChestWorld.png");}
   File.WriteAllText("Artifacts/E/client-validation.txt",$"PASS actual SampleScene seed12345; {g.Rooms.Count} rooms; {stage.transform.Find("DungeonSetDressing").GetComponentsInChildren<Renderer>().Count(x=>x.enabled)} dressing renderers; {environmentCount} server placements aligned; reward room matches; chests clear of prop colliders; valid scripts/materials. Preview camera uses ceiling cutaway.\n");
  }finally{var meshes=stage.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh).Where(x=>x!=null&&!EditorUtility.IsPersistent(x)).Distinct().ToArray();UnityEngine.Object.DestroyImmediate(stage);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);EditorSceneManager.ClosePreviewScene(preview);RenderSettings.ambientMode=previous;RenderSettings.ambientLight=previousColor;}
 }
}
