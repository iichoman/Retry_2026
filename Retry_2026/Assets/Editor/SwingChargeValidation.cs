using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
[InitializeOnLoad]
public static class SwingChargeValidation
{
    const string Output="Artifacts/SwingCharge";
    static SwingChargeValidation(){EditorApplication.update+=Poll;}
    static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(Output+"/validate.request"))return;File.Delete(Output+"/validate.request");try{Validate();File.WriteAllText(Output+"/validation.txt","PASS runtime weapon skin switching/restoration on local and remote prefabs; mummy charge/hit animation bounds and network phases; pose preview rendered\n");}catch(Exception e){File.WriteAllText(Output+"/validation.txt",e.ToString());Debug.LogException(e);}}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Validate(){
        File.WriteAllText(Output+"/pose-bounds.txt","");
        var scene=EditorSceneManager.NewPreviewScene();var meshes=new List<Mesh>();
        try{
            foreach(string prefab in new[]{"LocalPlayerPrefab","RemotePlayerPrefab"}){
                var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+prefab+".prefab"));SceneManager.MoveGameObjectToScene(player,scene);
                foreach(var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                var ab=player.GetComponent<ABActorView>();if(ab==null)ab=player.AddComponent<ABActorView>();
                foreach(string id in new[]{"flame_sword","frost_sword","flame_greatsword","frost_greatsword"}){
                    var weapon=player.GetComponentsInChildren<Weapon_Sword>(true).First(w=>(w is GreatswordWeapon)==id.Contains("greatsword"));
                    foreach(var other in player.GetComponentsInChildren<Weapon_Sword>(true))other.gameObject.SetActive(other==weapon);
                    var trail=weapon.GetComponentInChildren<WeaponSwingTrail>(true);var renderer=weapon.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.GetComponent<WeaponSwingTrail>()==null);
                    var original=renderer.sharedMaterials[0];ab.Apply(new ABActorState{weapon=ItemHash.Of(id),attackSpeed=1,range=1});
                    typeof(WeaponSwingTrail).GetMethod("UpdateElement",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(trail,null);
                    var expected=Resources.Load<Material>("AB/WeaponSkins/"+id);Check(renderer.sharedMaterial==expected&&expected.mainTexture!=null,prefab+" "+id+" texture; renderer="+renderer.name+" material="+renderer.sharedMaterial?.name+" expected="+expected?.name+" active="+weapon.gameObject.activeInHierarchy);
                    var fxField=typeof(WeaponSwingTrail).GetField("elementalFx",BindingFlags.Instance|BindingFlags.NonPublic);var fx=fxField.GetValue(trail) as GameObject;if(fx!=null)Object.DestroyImmediate(fx);fxField.SetValue(trail,null);
                    ab.Apply(new ABActorState{weapon=ItemHash.Of(id.Contains("greatsword")?"greatsword_001":"sword_001"),attackSpeed=1,range=1});
                    typeof(WeaponSwingTrail).GetMethod("UpdateElement",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(trail,null);Check(renderer.sharedMaterial==original,prefab+" restores base material");
                }
                Object.DestroyImmediate(player);
            }
            var registryObject=new GameObject("Mummy phase fixture");SceneManager.MoveGameObjectToScene(registryObject,scene);var registry=registryObject.AddComponent<RemoteMonsterRegistry>();
            registry.OnEnterView(new MonsterEnterView{monsterId=700,monsterKind=(int)MonsterKind.MUMMY,hp=100,maxHp=100});
            var remote=registry.GetActor(700);Check(!remote.GetComponent<MummyMonster>().enabled,"remote mummy AI disabled");
            registry.OnMummyBreath(new MummyBreathState{version=1,monsterId=700,serial=1,phase=3,dz=1});Check(remote.GetComponent<MummyBreathView>().Phase==3,"network charge windup");
            registry.OnMummyBreath(new MummyBreathState{version=1,monsterId=700,serial=1,phase=4,dz=1,elapsed=.2f});Check(remote.GetComponent<MummyBreathView>().Phase==4,"network charge travel");
            registry.OnMummyBreath(new MummyBreathState{version=1,monsterId=700,serial=1,phase=0});Check(remote.GetComponent<MummyBreathView>().Phase==0,"network charge cancel");Object.DestroyImmediate(remote);Object.DestroyImmediate(registryObject);
            for(int i=0;i<3;i++){
                var mummy=Object.Instantiate(Resources.Load<GameObject>("Mummy"));SceneManager.MoveGameObjectToScene(mummy,scene);mummy.transform.position=Vector3.right*(i-1)*2.2f;
                foreach(var behaviour in mummy.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
                foreach(var canvas in mummy.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
                var animator=mummy.GetComponentInChildren<Animator>();animator.Rebind();animator.Update(0);
                string state=i==0?"Locomotion":i==1?"Charge":"Hit";animator.Play(state,0,i==0?0:.45f);animator.Update(0);
                Check(animator.GetCurrentAnimatorStateInfo(0).IsName(state),"mummy "+state+" reachable");
                var bones=mummy.GetComponentsInChildren<Transform>();Check(bones.All(b=>float.IsFinite(b.position.x)&&float.IsFinite(b.position.y)),"retarget finite bones");
                foreach(var skin in mummy.GetComponentsInChildren<SkinnedMeshRenderer>()){
                    var mesh=new Mesh();skin.BakeMesh(mesh,true);meshes.Add(mesh);Check(mesh.bounds.size.magnitude>0&&mesh.bounds.size.magnitude<10,"mummy pose bounds");
                    File.AppendAllText(Output+"/pose-bounds.txt",state+" feet="+mesh.vertices.Min(v=>skin.transform.TransformPoint(v).y)+" top="+mesh.vertices.Max(v=>skin.transform.TransformPoint(v).y)+"\n");
                    var copy=new GameObject("Baked "+state);copy.transform.SetParent(skin.transform,false);copy.AddComponent<MeshFilter>().sharedMesh=mesh;copy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
                }
            }
            var camera=new GameObject("Pose camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.transform.position=new Vector3(4,3.4f,8);camera.transform.LookAt(new Vector3(0,1,0));camera.orthographic=true;camera.orthographicSize=3.6f;camera.backgroundColor=new Color(.08f,.10f,.14f);camera.clearFlags=CameraClearFlags.SolidColor;
            var light=new GameObject("Pose light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=2.5f;light.transform.rotation=Quaternion.Euler(40,-25,0);
            CombatContentBuilder.CaptureCamera(camera,Output+"/mummy-poses.png");
        }finally{EditorSceneManager.ClosePreviewScene(scene);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);}
    }
}
