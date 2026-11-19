using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WeaponIconBuilder
{
    const string Folder = "Assets/CombatContent/Items/AB/WeaponIcons";
    public static Sprite Get(int effect) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/weapon_{effect}.png");
    public static Task NextFrame()
    {
        var source = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; source.SetResult(true); }
        EditorApplication.update += Tick; EditorApplication.QueuePlayerLoopUpdate(); return source.Task;
    }
    public static async Task Build()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (int model in new[] { 0, 5, 3 })
            {
                var visual = new GameObject("Weapon icon model"); SceneManager.MoveGameObjectToScene(visual, scene);
                if (model == 3)
                {
                    var beam = Object.Instantiate(Resources.Load<GameObject>("AB/Beam"), visual.transform);
                    var line = beam.GetComponent<LineRenderer>(); line.useWorldSpace = false;
                    line.SetPosition(0, Vector3.zero); line.SetPosition(1, Vector3.forward * 1.5f);
                    var core = beam.transform.Find("Beam Core"); core.localPosition = Vector3.forward * .75f; core.localScale = new Vector3(.035f, .75f, .035f);
                    visual.transform.rotation = Quaternion.FromToRotation(Vector3.forward, new Vector3(.55f, 1, 0).normalized);
                }
                else
                {
                    var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
                    SceneManager.MoveGameObjectToScene(player, scene);
                    foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                    var weapon = player.GetComponentsInChildren<Weapon_Sword>(true).First(w => (w is GreatswordWeapon) == (model == 5));
                    if (weapon is GreatswordWeapon great) great.CorrectBladeFacing();
                    var parts = weapon.GetComponentsInChildren<Transform>(true);
                    var bladeBase = parts.First(t => t.name == "Blade Trail Base"); var tip = parts.First(t => t.name == "Blade Trail Tip");
                    Vector3 direction = tip.position - bladeBase.position;
                    foreach (var filter in weapon.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (renderer == null || filter.sharedMesh == null || filter.GetComponent<WeaponSwingTrail>() != null) continue;
                        var copy = new GameObject(filter.name); copy.transform.SetParent(visual.transform, false);
                        copy.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation); copy.transform.localScale = filter.transform.lossyScale;
                        copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh; copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                    }
                    var axis = new Vector3(.55f, 1, 0).normalized;
                    visual.transform.rotation = Quaternion.FromToRotation(direction.normalized, axis);
                    // Find the broad face of the authored blade, independent of its hand-socket rotation.
                    var facing = visual.transform.rotation; float widest = -1; int bestAngle=0;
                    var across = new Vector3(axis.y, -axis.x, 0);
                    for (int angle=0;angle<180;angle+=15)
                    {
                        visual.transform.rotation=Quaternion.AngleAxis(angle,axis)*facing;
                        float min=float.PositiveInfinity,max=float.NegativeInfinity;
                        foreach(var filter in visual.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices)
                        {float projection=Vector3.Dot(filter.transform.TransformPoint(vertex),across);min=Mathf.Min(min,projection);max=Mathf.Max(max,projection);}
                        if(max-min>widest){widest=max-min;bestAngle=angle;}
                    }
                    visual.transform.rotation=Quaternion.AngleAxis(bestAngle,axis)*facing;
                    Object.DestroyImmediate(player);
                }
                var renderers = visual.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                visual.transform.position -= bounds.center;
                var camera = new GameObject("Icon camera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.transform.position = new Vector3(0,0,-10); camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.size.x,bounds.size.y)*.61f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.06f,.08f,1);
                var light = new GameObject("Icon light").AddComponent<Light>(); SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                light.type=LightType.Directional;light.intensity=3f;light.transform.rotation=Quaternion.Euler(25,-25,0);
                var texture = new Texture2D(256,256,TextureFormat.RGBA32,false);
                var target = new RenderTexture(256,256,24,RenderTextureFormat.ARGB32) { antiAliasing=4 };
                var previous=RenderTexture.active;
                try
                {
                    await NextFrame(); await NextFrame();
                    camera.targetTexture=target;
                    for(int frame=0;frame<3;frame++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();
                    var originalMaterials=renderers.Select(r=>r.sharedMaterials).ToArray();
                    foreach(var item in ABItemDefinitions.All.Where(i=>i.kind==2&&(model==5?i.IsGreatsword:model==3?i.effect==3:i.effect>=0&&i.effect<=2)))
                    {
                        var skin=Resources.Load<Material>("AB/WeaponSkins/"+item.id);
                        for(int r=0;r<renderers.Length;r++)renderers[r].sharedMaterials=skin==null?originalMaterials[r]:Enumerable.Repeat(skin,originalMaterials[r].Length).ToArray();
                        for(int frame=0;frame<3;frame++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                        RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();
                        Color color=EquipmentPresentation.Accent(ItemCatalog.Load().Resolve(item.id));
                        for(int y=4;y<252;y++)for(int x=4;x<252;x++)if(x<10||x>=246||y<10||y>=246)texture.SetPixel(x,y,color);
                        texture.Apply();string path=$"{Folder}/weapon_{item.effect}.png";File.WriteAllBytes(path,texture.EncodeToPNG());
                        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                        importer.spritePixelsPerUnit=256;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                        var asset=ItemCatalog.Load().Resolve(item.id);var serialized=new SerializedObject(asset);
                        serialized.FindProperty("icon").objectReferenceValue=Get(item.effect);serialized.FindProperty("description").stringValue=EquipmentPresentation.WeaponDescription(item);
                        serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);
                    }
                }
                finally { camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(texture);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(visual); }
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
