using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class StoneWallValidation
{
    const string Output="Artifacts/StoneWalls";
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static bool running;
    static StoneWallValidation()=>EditorApplication.update+=Poll;
    static async void Poll(){if(running||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Output+"/validate.request"))return;
        File.Delete(Output+"/validate.request");running=true;
        try{await Run();}catch(Exception e){File.WriteAllText(Output+"/validation.txt","FAIL\n"+e);UnityEngine.Debug.LogException(e);}finally{running=false;}}
    static void Check(bool value,string label){if(!value)throw new Exception(label);}
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    static Task Frame(){var task=new TaskCompletionSource<bool>();void Tick(){EditorApplication.update-=Tick;task.SetResult(true);}EditorApplication.update+=Tick;EditorApplication.QueuePlayerLoopUpdate();return task.Task;}
    public static async void Batch(){running=true;try{await Run();EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Output+"/validation.txt","FAIL\n"+e);UnityEngine.Debug.LogException(e);EditorApplication.Exit(1);}}
    public static async Task Run()
    {
        Directory.CreateDirectory(Output);
        var assetScene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        var source=assetScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DungeonGenerator_ChunkMesh>(true)).First();
        var preview=EditorSceneManager.NewPreviewScene();var report=new List<string>();
        try{
            var fixture=new GameObject("Stone full-map validation");SceneManager.MoveGameObjectToScene(fixture,preview);
            var generator=fixture.AddComponent<DungeonGenerator_ChunkMesh>();EditorUtility.CopySerialized(source,generator);generator.useProceduralStoneWalls=true;generator.randomizeSeedOnGenerate=generator.generateOnStart=false;
            foreach(int seed in new[]{1,7,12345}){
                generator.seed=seed;generator.GenerateDataForValidation();var floor=generator.FloorTiles.OrderBy(t=>t.x).ThenBy(t=>t.z).ToArray();
                var sections=(IDictionary)Call(generator,"BuildMeshSections");var root=new GameObject("Wall test root");root.transform.SetParent(fixture.transform,false);
                ((HashSet<Vector3Int>)typeof(DungeonGenerator_ChunkMesh).GetField("masonryFaces",Private).GetValue(generator)).Clear();
                var watch=Stopwatch.StartNew();
                foreach(DictionaryEntry entry in sections)Call(generator,"CreateMasonrySection",entry.Value,root.transform,generator.wallMaterial,true,(LayerMask)0);
                watch.Stop();Check(generator.FloorTiles.Count==floor.Length&&floor.All(generator.FloorTiles.Contains),"cosmetic walls preserve shared floor data");
                var colliders=root.GetComponentsInChildren<MeshCollider>();var detail=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Stone relief").ToArray();
                var distant=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Distant masonry").ToArray();
                Check(detail.Length>0&&detail.Length==distant.Length&&root.GetComponentsInChildren<LODGroup>().Length==detail.Length,"one LOD group per wall chunk");
                foreach(var collider in colliders){Check(!detail.Any(f=>f.sharedMesh==collider.sharedMesh),"relief must never be a collision mesh");Check(collider.sharedMesh.vertices.All(v=>Mathf.Abs(v.x-Mathf.Round(v.x))<.001f&&Mathf.Abs(v.y-Mathf.Round(v.y))<.001f&&Mathf.Abs(v.z-Mathf.Round(v.z))<.001f),"collision remains original axis-aligned integer grid");}
                long highTriangles=detail.Sum(f=>(long)f.sharedMesh.triangles.Length/3),lowTriangles=distant.Sum(f=>(long)f.sharedMesh.triangles.Length/3);
                Check(lowTriangles<highTriangles/8,"distant LOD reduces wall triangles at least eightfold");
                foreach(var filter in detail){var m=filter.sharedMesh;Check(m.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"finite stone mesh");Check(m.normals.All(n=>n.sqrMagnitude>.9f),"no degenerate stone triangles");}
                report.Add($"Seed {seed}: {watch.ElapsedMilliseconds} ms wall generation; chunks={detail.Length}, colliders={colliders.Length}, detailed vertices={detail.Sum(f=>(long)f.sharedMesh.vertexCount)}, detailed triangles={highTriangles}, distant triangles={lowTriangles}");
                File.WriteAllLines(Output+"/progress.txt",report);
                // Unity's actual NavMesh source collector must omit relief and retain flat wall render sources.
                var surface=root.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
                var sources=(List<NavMeshBuildSource>)typeof(Unity.AI.Navigation.NavMeshSurface).GetMethod("CollectSources",Private).Invoke(surface,null);
                Check(sources.All(s=>!detail.Any(f=>s.sourceObject==f.sharedMesh)),"NavMesh excludes decorative stone relief");
                Check(distant.Any(f=>sources.Any(s=>s.sourceObject==f.sharedMesh)),"NavMesh retains simple wall surface");
                var generated=detail.Select(f=>f.sharedMesh).Concat(distant.Select(f=>f.sharedMesh)).Concat(colliders.Select(c=>c.sharedMesh)).ToArray();Object.DestroyImmediate(root);
                Check(generated.All(m=>m==null),"regeneration releases owned native meshes");
            }
            generator.mapSize=160;generator.targetRoomCount=4;generator.baseRoomSize=24;generator.minLeafSize=32;generator.teamCount=1;generator.bossRoomCount=0;
            generator.GenerateDungeon();
            var actualWalls=fixture.transform.Find("DungeonRoot_ChunkMesh/WallChunks");
            Check(actualWalls!=null&&actualWalls.GetComponentsInChildren<LODGroup>().Length>0,"normal GenerateDungeon path uses procedural walls");
            var ownedMaterials=actualWalls.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            Check(ownedMaterials.All(m=>m.name.EndsWith("rough stone")&&Mathf.Abs(m.GetFloat("_Smoothness")-.16f)<.001f),"shared rough wall materials wired in real generator");
            Object.DestroyImmediate(fixture);Check(ownedMaterials.All(m=>m==null),"generated materials released with walls");
            var tiles=new List<Vector3Int>();var walls=new HashSet<Vector3Int>();var floors=new HashSet<Vector3Int>();
            for(int x=0;x<16;x++)for(int z=1;z<9;z++)floors.Add(new Vector3Int(x,0,z));
            for(int x=0;x<16;x++)if(x<10||x>12)for(int y=0;y<=7;y++)walls.Add(new Vector3Int(x,y,0));
            for(int z=1;z<9;z++)for(int y=0;y<=7;y++)walls.Add(new Vector3Int(-1,y,z));tiles.AddRange(walls);
            var materials=source.wallMaterials.ToArray();
            var stage=new GameObject("Stone wall preview");SceneManager.MoveGameObjectToScene(stage,preview);
            materials=stage.AddComponent<DungeonMasonryResources>().RoughMaterials(materials);
            void Build(){DungeonStoneMesher.Create(stage.transform,tiles,walls,floors,Vector3Int.zero,12345,7,materials,2,new HashSet<Vector3Int>(),stage.AddComponent<DungeonMasonryResources>(),(t,d,n)=>0);}
            Build();var meshes=stage.GetComponentsInChildren<MeshFilter>();var signature=meshes.SelectMany(f=>f.sharedMesh.vertices).ToArray();
            CheckCornerCoverage(stage,Quaternion.identity);
            foreach(var f in meshes.Where(f=>f.name=="Stone relief")){
                Check(f.sharedMesh.vertices.All(v=>v.y>=0&&v.y<=8.001f),"stone stays in authored height range");
                foreach(var v in f.sharedMesh.vertices){var p=f.transform.TransformPoint(v);Check(!(p.z>1.001f&&p.x>.001f&&p.x<16&&p.z<9),"stone relief never protrudes into walkable corridor");}
            }
            var copy=new GameObject("Determinism fixture");SceneManager.MoveGameObjectToScene(copy,preview);
            DungeonStoneMesher.Create(copy.transform,tiles.AsEnumerable().Reverse().ToList(),walls,floors,Vector3Int.zero,12345,7,materials,2,new HashSet<Vector3Int>(),copy.AddComponent<DungeonMasonryResources>(),(t,d,n)=>0);
            var repeated=copy.GetComponentsInChildren<MeshFilter>().SelectMany(f=>f.sharedMesh.vertices).ToArray();Check(signature.SequenceEqual(repeated),"stone layout deterministic regardless of tile iteration order");Object.DestroyImmediate(copy);
            // Regenerate all four facing combinations, including negative chunk coordinates.
            for(int turn=1;turn<4;turn++){
                var rotation=Quaternion.Euler(0,90*turn,0);
                Vector3Int Rotate(Vector3Int t){var center=rotation*((Vector3)t+new Vector3(.5f,0,.5f));return new Vector3Int(Mathf.FloorToInt(center.x),t.y,Mathf.FloorToInt(center.z));}
                var rotatedWalls=new HashSet<Vector3Int>(walls.Select(Rotate));var rotatedFloors=new HashSet<Vector3Int>(floors.Select(Rotate));
                var cornerFixture=new GameObject("Rotated corner");SceneManager.MoveGameObjectToScene(cornerFixture,preview);
                DungeonStoneMesher.Create(cornerFixture.transform,rotatedWalls.ToList(),rotatedWalls,rotatedFloors,Vector3Int.zero,12345,7,materials,2,new HashSet<Vector3Int>(),cornerFixture.AddComponent<DungeonMasonryResources>(),(t,d,n)=>0);
                CheckCornerCoverage(cornerFixture,rotation);Object.DestroyImmediate(cornerFixture);
            }
            ValidateOpenBackCorners(preview,materials);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(stage.transform,false);ground.transform.localPosition=new Vector3(7.5f,.5f,4.5f);ground.transform.localScale=new Vector3(17,1,9);ground.GetComponent<Renderer>().sharedMaterial=source.floorMaterial;
            foreach(var detail in stage.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Stone relief"))detail.sharedMesh.UploadMeshData(true);
            var navigation=stage.AddComponent<Unity.AI.Navigation.NavMeshSurface>();navigation.collectObjects=Unity.AI.Navigation.CollectObjects.Children;
            var navigationSources=(List<NavMeshBuildSource>)typeof(Unity.AI.Navigation.NavMeshSurface).GetMethod("CollectSources",Private).Invoke(navigation,null);
            Check(navigationSources.All(s=>!(s.sourceObject is Mesh m)||m.isReadable),"runtime NavMesh sources remain CPU-readable after relief upload");
            var navData=NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0),navigationSources,new Bounds(new Vector3(7,4,4),new Vector3(22,12,16)),Vector3.zero,Quaternion.identity);
            Check(navData!=null,"native runtime NavMesh bake succeeds");var instance=NavMesh.AddNavMeshData(navData);
            try{Check(NavMesh.SamplePosition(new Vector3(5,1,4),out _,.2f,NavMesh.AllAreas),"floor remains navigable after relief GPU upload");}
            finally{instance.Remove();Object.DestroyImmediate(navData);}
            var light=new GameObject("Stone side light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,preview);light.type=LightType.Directional;light.intensity=2.4f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(37,-145,0);
            var camera=new GameObject("Stone camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,preview);camera.scene=preview;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(preview);camera.fieldOfView=58;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.045f,.05f);
            foreach(var lod in stage.GetComponentsInChildren<LODGroup>())lod.ForceLOD(0);
            camera.transform.position=new Vector3(9,4.1f,11);camera.transform.LookAt(new Vector3(5,3.1f,.5f));await Frame();await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/stone-wall.png");
            camera.transform.position=new Vector3(5.5f,2.8f,4);camera.transform.LookAt(new Vector3(3.7f,2.6f,.8f));await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/stone-close.png");
            camera.transform.position=new Vector3(1.4f,3.5f,3);camera.transform.LookAt(new Vector3(0,3.2f,1));await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/stone-corner.png");
            foreach(var lod in stage.GetComponentsInChildren<LODGroup>())lod.ForceLOD(1);await Frame();CombatContentBuilder.CaptureCamera(camera,Output+"/stone-distant-lod.png");
            report.Add(CameraCollisionValidation.Run());
            report.Insert(0,"PASS compilation, 3 full dungeon seeds, actual GenerateDungeon integration, unchanged grid collision, open-backed cardinal-only corner regression in four directions / mixed LODs, unchanged floor/object placement space, native NavMesh bake after GPU upload, deterministic geometry, native mesh/material cleanup and preview renders.");
            File.WriteAllLines(Output+"/validation.txt",report);
        }finally{EditorSceneManager.ClosePreviewScene(preview);EditorSceneManager.ClosePreviewScene(assetScene);}
    }

    // The actual generator adds cardinal neighbours of floors, leaving the diagonal corner empty.
    // The former test had a solid diagonal cell and therefore missed this common topology.
    public static void BatchCorners()
    {
        Directory.CreateDirectory(Output);
        var preview=EditorSceneManager.NewPreviewScene();
        try{
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/EContent/Stone.mat");
            ValidateOpenBackCorners(preview,new[]{material});
            File.WriteAllText(Output+"/corner-validation.txt","PASS open-backed corners in all four directions, both LODs and mixed LODs.");EditorApplication.Exit(0);
        }catch(Exception e){File.WriteAllText(Output+"/corner-validation.txt","FAIL\n"+e);UnityEngine.Debug.LogException(e);EditorApplication.Exit(1);}
        finally{EditorSceneManager.ClosePreviewScene(preview);}
    }

    static void ValidateOpenBackCorners(Scene preview,Material[] materials)
    {
        var floors=new HashSet<Vector3Int>();
        for(int x=0;x<16;x++)for(int z=1;z<9;z++)floors.Add(new Vector3Int(x,0,z));
        var walls=new HashSet<Vector3Int>();
        foreach(var tile in floors)foreach(var direction in new[]{Vector3Int.right,Vector3Int.left,Vector3Int.forward,Vector3Int.back}){
            var neighbour=tile+direction;if(floors.Contains(neighbour))continue;
            for(int y=0;y<=7;y++)walls.Add(neighbour+Vector3Int.up*y);
        }
        Check(!walls.Contains(new Vector3Int(-1,0,0)),"fixture reproduces the empty diagonal cell in real generated rooms");
        for(int turn=0;turn<4;turn++){
            var rotation=Quaternion.Euler(0,turn*90,0);
            Vector3Int Rotate(Vector3Int t){var center=rotation*((Vector3)t+new Vector3(.5f,0,.5f));return new Vector3Int(Mathf.FloorToInt(center.x),t.y,Mathf.FloorToInt(center.z));}
            var rotatedWalls=new HashSet<Vector3Int>(walls.Select(Rotate));var rotatedFloors=new HashSet<Vector3Int>(floors.Select(Rotate));
            var fixture=new GameObject("Open-backed room corner");SceneManager.MoveGameObjectToScene(fixture,preview);
            try{
                DungeonStoneMesher.Create(fixture.transform,rotatedWalls.ToList(),rotatedWalls,rotatedFloors,Vector3Int.zero,12345,7,materials,2,new HashSet<Vector3Int>(),fixture.AddComponent<DungeonMasonryResources>(),(t,d,n)=>0);
                CheckCornerCoverage(fixture,rotation);
                foreach(var filter in fixture.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices){
                    var p=filter.transform.TransformPoint(vertex);var cell=new Vector3Int(Mathf.FloorToInt(p.x+.00001f),0,Mathf.FloorToInt(p.z+.00001f));
                    if(!rotatedFloors.Contains(cell))continue;
                    Check(Mathf.Abs(p.x-Mathf.Round(p.x))<.0001f||Mathf.Abs(p.z-Mathf.Round(p.z))<.0001f,"corner closures never enter floor/object placement space");
                }
            }finally{Object.DestroyImmediate(fixture);}
        }
    }

    static void CheckCornerCoverage(GameObject stage,Quaternion rotation)
    {
        var chunkNumbers=stage.GetComponentsInChildren<LODGroup>().Select((g,i)=>(g.transform,i)).ToDictionary(pair=>pair.transform,pair=>pair.i);
        foreach(string lod in new[]{"Stone relief","Distant masonry","Mixed A","Mixed B"}){
            var colliders=new List<MeshCollider>();
            try{
                foreach(var filter in stage.GetComponentsInChildren<MeshFilter>()){
                    string selected=lod.StartsWith("Mixed")?((chunkNumbers[filter.transform.parent]+(lod=="Mixed A"?0:1))%2==0?"Stone relief":"Distant masonry"):lod;
                    if(filter.name!=selected)continue;
                    var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;colliders.Add(collider);
                }
                Physics.SyncTransforms();
                foreach(float y in new[]{1.1f,2.35f,4.71f,7.9f})for(int step=0;step<17;step++){
                    var from=rotation*new Vector3(.2f+step*.025f,y,1.6f-step*.025f);
                    var toward=rotation*new Vector3(-.09f,y,.91f);var ray=new Ray(from,(toward-from).normalized);
                    Check(colliders.Any(c=>c.Raycast(ray,out _,3)),lod+" inside corner is opaque, facing="+rotation.eulerAngles.y+", sample="+step);
                }
            }finally{foreach(var collider in colliders)Object.DestroyImmediate(collider);}
        }
    }
}
