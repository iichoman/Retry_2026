using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomCompositionValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);

    [MenuItem("Retry/Dungeon/Apply and Validate Room Composition")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before rebuilding room composition assets.");
        checks = 0;
        const string output = "Artifacts/RoomComposition";
        Directory.CreateDirectory(output);
        var palette = DungeonPropAssetBuilder.Build();
        foreach(var entry in palette.entries)
        {
            Check(entry.prefab!=null,"Missing palette prefab");
            foreach(var node in entry.prefab.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject)==0,"Missing script in prop prefab");
            foreach(var renderer in entry.prefab.GetComponentsInChildren<Renderer>(true))
                Check(renderer.sharedMaterials.All(m=>m!=null && m.shader!=null),"Missing prop material/shader");
        }
        var sample = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
        bool opened = !sample.isLoaded;
        if (opened) sample = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        var source = sample.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RoomObjectPlacer2>(true)).Single(p => p.enabled);
        var serialized = new SerializedObject(source);
        serialized.FindProperty("useRoomComposition").boolValue = true;
        serialized.FindProperty("compositionPalette").objectReferenceValue = palette;
        serialized.FindProperty("anchorWallLightingToRoomHeight").boolValue = true;
        serialized.FindProperty("wallLightingHeightRatio").floatValue = .60f;
        serialized.FindProperty("wallLightingModelHeight").floatValue = 1.25f;
        serialized.FindProperty("wallLightingVerticalMargin").floatValue = .25f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(sample); EditorSceneManager.SaveScene(sample);
        var scene = EditorSceneManager.NewPreviewScene();
        var stage = new GameObject("Room Composition Preview"); SceneManager.MoveGameObjectToScene(stage, scene);
        var previousAmbient = RenderSettings.ambientLight; var previousMode = RenderSettings.ambientMode;
        bool previousOnline = NetworkRuntime.IsOnline;
        try
        {
            var generator = stage.AddComponent<DungeonGenerator_ChunkMesh>(); generator.generateOnStart = false;
            generator.randomizeSeedOnGenerate = false; generator.centerMapAtOrigin = false; generator.mapHeight = 10;
            var placer = stage.AddComponent<RoomObjectPlacer2>();
            EditorUtility.CopySerialized(source, placer); Set(placer, "dungeonGenerator", generator);
            Room room = Fixture(generator);
            var sourceGenerator = source.GetComponent<DungeonGenerator_ChunkMesh>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(stage.transform);
            floor.transform.position = new Vector3(20, .9f, 16); floor.transform.localScale = new Vector3(40,.2f,32);
            floor.GetComponent<Renderer>().sharedMaterial = sourceGenerator.floorMaterial;
            var wallMaterial = sourceGenerator.wallMaterial != null ? sourceGenerator.wallMaterial : sourceGenerator.wallMaterials.FirstOrDefault(m => m != null);
            foreach (var pose in new[] { (new Vector3(20,5.5f,32.5f),new Vector3(40,9,1)), (new Vector3(40.5f,5.5f,16),new Vector3(1,9,32)) })
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(stage.transform);
                wall.transform.position=pose.Item1; wall.transform.localScale=pose.Item2; wall.GetComponent<Renderer>().sharedMaterial=wallMaterial;
            }
            var camera = new GameObject("Composition Camera").AddComponent<Camera>(); camera.transform.SetParent(stage.transform);
            camera.transform.position=new Vector3(-6,38,-10); camera.transform.LookAt(new Vector3(20,2,16));
            camera.fieldOfView=47; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.045f,.045f,.06f);
            camera.scene=scene; camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene); camera.cullingMask=1<<31;
            var light=new GameObject("Preview Light").AddComponent<Light>(); light.transform.SetParent(stage.transform);
            light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(45,25,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight=new Color(.6f,.6f,.6f);
            var rows=new List<string>(); var counts=new List<string>(); var captured=new HashSet<DungeonPropLayout.Theme>();
            typeof(NetworkRuntime).GetProperty("IsOnline").SetValue(null,true);
            for (int seed=12345; seed<12357; seed++)
            {
                generator.seed=seed; placer.PlaceRoomObjects(); Physics.SyncTransforms();
                var plan=DungeonPropLayout.GenerateRoom(generator,room);
                var props=stage.GetComponentsInChildren<DungeonPropObstacle>();
                Check(plan.Count==props.Length && props.Length>0,"Missing planned props "+seed);
                for(int i=0;i<plan.Count;i++)
                {
                    var slot=plan[i]; var prop=props[i]; var box=prop.GetComponent<BoxCollider>();
                    Check(box!=null && box.enabled && !box.isTrigger,"Missing solid collider");
                    Check(prop.gameObject.layer==LayerMask.NameToLayer("Wall"),"Collider layer not Wall");
                    Check(Vector3.Distance(box.bounds.min,slot.Bounds.min)<.002f && Vector3.Distance(box.bounds.max,slot.Bounds.max)<.002f,"Collider differs from server contract");
                    var mesh=DungeonPropAssetBuilder.MeshBounds(prop.gameObject);
                    Check(Vector3.Distance(mesh.min,slot.Bounds.min)<.015f && Vector3.Distance(mesh.max,slot.Bounds.max)<.015f,"Mesh scale differs from collision");
                    Check(prop.GetComponent<NavMeshModifierVolume>()!=null,"Missing offline navigation exclusion");
                    if (!slot.Supported)
                    {
                        Check(Mathf.Abs(slot.Bounds.min.y-1)<.001f,"Floating floor prop");
                        foreach(var door in room.doorwayFloorTiles)
                            Check(Vector2.Distance(new Vector2(slot.Center.x,slot.Center.z),new Vector2(door.x+.5f,door.z+.5f))>3,"Blocked entrance");
                    }
                    else Check(plan.Any(p=>!p.Supported && p.Bounds.max.y<=slot.Bounds.min.y+.001f && Mathf.Abs(p.Bounds.max.y-slot.Bounds.min.y)<.02f && p.Bounds.min.x<=slot.Bounds.min.x && p.Bounds.max.x>=slot.Bounds.max.x && p.Bounds.min.z<=slot.Bounds.min.z && p.Bounds.max.z>=slot.Bounds.max.z),"Unsupported stacked item");
                    for(int j=i+1;j<plan.Count;j++)
                    {
                        var a=slot.Bounds; a.Expand(-.002f);
                        Check(!a.Intersects(plan[j].Bounds),"Interpenetrating objects");
                    }
                    rows.Add(string.Join(",",seed,slot.RoomId,slot.GroupId,(int)slot.GroupTheme,(int)slot.PropKind,
                        F(slot.Center.x),F(slot.Center.y),F(slot.Center.z),F(slot.Size.x),F(slot.Size.y),F(slot.Size.z),
                        slot.Yaw.ToString("F0",CultureInfo.InvariantCulture),slot.Supported?1:0));
                }
                string signature=Signature(props);
                placer.PlaceRoomObjects();
                Check(signature==Signature(stage.GetComponentsInChildren<DungeonPropObstacle>()),"Non-deterministic placement");
                counts.Add($"seed {seed}: {plan.Count} props, {plan.Select(p=>p.GroupId).Distinct().Count()} groups");
                foreach(var theme in plan.Select(p=>p.GroupTheme).Distinct())
                {
                    if(!captured.Add(theme))continue;
                    var first=stage.GetComponentsInChildren<DungeonPropObstacle>().First(p=>plan.Any(s=>s.GroupId==p.GroupId && s.GroupTheme==theme));
                    Detail(stage,camera,first.transform.parent,output+"/"+theme+"_Detail.png");
                }
                if(seed==12345)Capture(stage,camera,output+"/After.png");
            }
            Check(captured.Count==3,"Not all composition themes rendered");
            File.WriteAllLines(output+"/layout-unity.csv",rows);
            File.WriteAllLines(output+"/seed-counts.txt",counts);
            ValidateTorches(placer,generator,stage,camera,output);
            // Clear/regenerate must remove old colliders as well as visuals.
            placer.ClearPlacedObjects();
            Check(stage.GetComponentsInChildren<DungeonPropObstacle>().Length==0,"Leaked obstacle after clear");
            room.layoutType=RoomLayoutType.CenterBlock;
            for(int x=17;x<23;x++)for(int z=13;z<19;z++)room.blockedTiles.Add(new Vector3Int(x,0,z));
            placer.PlaceRoomObjects();
            Check(!room.blockedTiles.Any(Field<HashSet<Vector3Int>>(placer,"occupiedFloorTiles").Contains),"Structural block overlap");
            room.type=RoomType.Boss; room.layoutType=RoomLayoutType.Open;room.blockedTiles.Clear();
            placer.PlaceRoomObjects();
            float arena=Mathf.Min(16,Mathf.Min(room.bounds.size.x,room.bounds.size.z)*.30f);
            Check(Field<HashSet<Vector3Int>>(placer,"occupiedFloorTiles").All(t=>new Vector2(t.x+.5f-20,t.z+.5f-16).sqrMagnitude>=arena*arena),"Arena blocked");
            // Include real generated room/doorway geometry, not only a rectangular fixture.
            var dataObject = new GameObject("Full dungeon parity"); dataObject.transform.SetParent(stage.transform);
            var actual = dataObject.AddComponent<DungeonGenerator_ChunkMesh>();
            EditorUtility.CopySerialized(sourceGenerator, actual);
            actual.seed = 12345; actual.randomizeSeedOnGenerate = false; actual.generateOnStart = false;
            actual.GenerateDataForValidation();
            var fullRows = new List<string>();
            foreach (var generatedRoom in actual.Rooms)
                foreach (var slot in DungeonPropLayout.GenerateRoom(actual, generatedRoom))
                    fullRows.Add(string.Join(",",actual.seed,slot.RoomId,slot.GroupId,(int)slot.GroupTheme,(int)slot.PropKind,
                        F(slot.Center.x),F(slot.Center.y),F(slot.Center.z),F(slot.Size.x),F(slot.Size.y),F(slot.Size.z),
                        slot.Yaw.ToString("F0",CultureInfo.InvariantCulture),slot.Supported?1:0));
            Check(fullRows.Count>0,"No props in full generated dungeon");
            File.WriteAllLines(output+"/layout-full-unity.csv",fullRows);
            File.WriteAllText(output+"/validation.txt","PASS "+checks+" checks\n12 seeds; deterministic geometry, exact render/collider bounds, support, separation, routes, online solid colliders, offline navigation exclusion, four torch orientations.\n"+string.Join("\n",counts));
        }
        catch(Exception ex){File.WriteAllText(output+"/validation.txt","FAIL\n"+ex);throw;}
        finally
        {
            UnityEngine.Object.DestroyImmediate(stage);EditorSceneManager.ClosePreviewScene(scene);
            typeof(NetworkRuntime).GetProperty("IsOnline").SetValue(null,previousOnline);
            RenderSettings.ambientLight=previousAmbient;RenderSettings.ambientMode=previousMode;
            if(opened)EditorSceneManager.CloseScene(sample,true);
        }
    }
    static string F(float value)=>value.ToString("F3",CultureInfo.InvariantCulture);
    static string Signature(DungeonPropObstacle[] props)=>string.Join("\n",props.Select(p=>p.name+p.transform.position.ToString("F4")+p.transform.localScale.ToString("F4")));

    static void ValidateTorches(RoomObjectPlacer2 placer,DungeonGenerator_ChunkMesh generator,GameObject stage,Camera camera,string output)
    {
        var type=typeof(RoomObjectPlacer2);
        var slotType=type.GetNestedType("WallSlot",BindingFlags.NonPublic);
        var candidateType=type.GetNestedType("WallCandidate",BindingFlags.NonPublic);
        var categoryType=type.GetNestedType("PropCategory",BindingFlags.NonPublic);
        object slot=Activator.CreateInstance(slotType,true);
        var categories=(System.Collections.IList)slotType.GetField("propCategories").GetValue(slot);
        categories.Add(Enum.Parse(categoryType,"벽조명"));slotType.GetField("wallSurfaceOffset").SetValue(slot,.14f);
        var library=new SerializedObject(placer).FindProperty("propLibrary");
        GameObject prefab=null;
        var it=library.Copy(); var end=library.GetEndProperty();
        while(it.NextVisible(true)&&!SerializedProperty.EqualContents(it,end))
            if(it.propertyType==SerializedPropertyType.ObjectReference && it.objectReferenceValue is GameObject go && go.name.ToLowerInvariant().Contains("torch")){prefab=go;break;}
        Check(prefab!=null,"Missing scene torch prefab");
        placer.ClearPlacedObjects();
        var mounting=new GameObject("Torch mounting test").transform; mounting.SetParent(stage.transform);
        foreach(var direction in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left})
        {
            var wall=new Vector3Int(20,0,32);
            var tile=wall+Vector3Int.RoundToInt(direction);
            object candidate=Activator.CreateInstance(candidateType,new object[]{tile,wall,direction});
            var instance=(GameObject)type.GetMethod("SpawnWallObject",Private).Invoke(placer,new[]{(object)prefab,candidate,slot,mounting});
            var b=DungeonPropAssetBuilder.MeshBounds(instance);
            Check(Mathf.Abs(b.center.y-6.4f)<.01f && Mathf.Abs(b.size.y-1.25f)<.01f,"Torch height not at wall 60 percent");
            var face=generator.TileToWorldCenter(wall)+direction*.5f;
            float extent=Mathf.Abs(direction.x)*b.extents.x+Mathf.Abs(direction.z)*b.extents.z;
            Check(Mathf.Abs(Vector3.Dot(b.center-face,direction)-extent-.14f)<.01f,"Torch intersects wall");
            if(direction==Vector3.back)
            {
                camera.transform.position=new Vector3(15,6,23); camera.transform.LookAt(new Vector3(20.5f,5.5f,32));
                Capture(stage,camera,output+"/Torch_Height.png");
            }
            UnityEngine.Object.DestroyImmediate(instance);
        }
        UnityEngine.Object.DestroyImmediate(mounting.gameObject);
    }
    static Room Fixture(DungeonGenerator_ChunkMesh generator)
    {
        var room = new Room { id = 1, type = RoomType.Normal, shape = RoomShape.Normal, layoutType = RoomLayoutType.Open, bounds = new BoundsInt(0,0,0,40,10,32) };
        for (int x = 0; x < 40; x++) for (int z = 0; z < 32; z++) room.floorTiles.Add(new Vector3Int(x,0,z));
        room.doorwayFloorTiles.Add(new Vector3Int(0,0,16)); room.doorwayFloorTiles.Add(new Vector3Int(39,0,16)); room.doorwayFloorTiles.Add(new Vector3Int(20,0,0));
        Field<List<Room>>(generator, "rooms").Add(room);
        Field<HashSet<Vector3Int>>(generator, "floorTiles").UnionWith(room.floorTiles);
        var walls = Field<HashSet<Vector3Int>>(generator, "wallTiles");
        for (int x = -1; x <= 40; x++) { walls.Add(new Vector3Int(x,0,-1)); walls.Add(new Vector3Int(x,0,32)); }
        for (int z = -1; z <= 32; z++) { walls.Add(new Vector3Int(-1,0,z)); walls.Add(new Vector3Int(40,0,z)); }
        return room;
    }
    static void Detail(GameObject stage,Camera camera,Transform group,string path)
    {
        var previous=camera.transform.position;var rotation=camera.transform.rotation;
        var members=group.GetComponentsInChildren<DungeonPropObstacle>();Bounds bounds=DungeonPropAssetBuilder.MeshBounds(group.gameObject);
        var forward=members[0].transform.forward;
        camera.transform.position=bounds.center+forward*7+members[0].transform.right*4+Vector3.up*4;
        camera.transform.LookAt(bounds.center);
        Capture(stage,camera,path);camera.transform.SetPositionAndRotation(previous,rotation);
    }
    static void Capture(GameObject stage,Camera camera,string path)
    {
        var transforms=stage.GetComponentsInChildren<Transform>(true); var layers=transforms.Select(t=>t.gameObject.layer).ToArray();
        try{foreach(var t in transforms)t.gameObject.layer=31;CombatContentBuilder.CaptureCamera(camera,Path.GetFullPath(path));}
        finally{for(int i=0;i<transforms.Length;i++)if(transforms[i]!=null)transforms[i].gameObject.layer=layers[i];}
    }
}
