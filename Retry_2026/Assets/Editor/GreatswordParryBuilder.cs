using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
[InitializeOnLoad]
public static class GreatswordParryBuilder
{
 static GreatswordParryBuilder(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Artifacts/parry.request"))return;File.Delete("Artifacts/parry.request");try{Build();File.WriteAllText("Artifacts/parry-result.txt","PASS greatsword support-hand parry humanoid clip, upper-body state, stun attack cancellation and Hit transition\n");}catch(Exception e){File.WriteAllText("Artifacts/parry-result.txt",e.ToString());Debug.LogException(e);}}
 static void Build(){
  var source=AssetDatabase.LoadAllAssetsAtPath("Assets/GameObjects/Player/Player_Model/chA/greatSwordAni.fbx").OfType<AnimationClip>().First(c=>c.name=="greatSword_Parring");
  if(!source.isHumanMotion)throw new Exception("Parry clip must be humanoid");
  string path=KnightGreatswordBuilder.Folder+"/GreatswordParry.anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
  if(clip==null){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
  clip.name="GreatswordParry";AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.keepOriginalPositionXZ=true;settings.loopBlendPositionXZ=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(KnightGreatswordBuilder.PlayerController);var layer=controller.layers.First(l=>l.name=="Defense Upper Body");
  var state=layer.stateMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==PlayerDefenseAnimation.GreatswordParryState)??layer.stateMachine.AddState(PlayerDefenseAnimation.GreatswordParryState);state.motion=clip;state.speed=0;state.writeDefaultValues=true;
  EditorUtility.SetDirty(clip);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
  foreach(string prefab in new[]{"LocalPlayerPrefab","RemotePlayerPrefab"}){
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+prefab+".prefab"));
   try{foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
    var animator=go.GetComponentInChildren<Animator>();animator.Rebind();animator.Update(0);int index=animator.GetLayerIndex("Defense Upper Body");if(!animator.HasState(index,Animator.StringToHash("GreatswordParry")))throw new Exception("Missing parry state");
    animator.SetLayerWeight(index,1);animator.Play("GreatswordParry",index,.5f);animator.Update(0);animator.SetLayerWeight(index,0);
    var reaction=go.AddComponent<StunReactionView>();reaction.SetNetworkStunned(true);typeof(StunReactionView).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(reaction,null);
    if(!animator.GetCurrentAnimatorStateInfo(0).IsTag("Hit")||animator.speed==0)throw new Exception("Stun did not switch to animated Hit: "+prefab);
    reaction.SetNetworkStunned(false);typeof(StunReactionView).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(reaction,null);
   }finally{UnityEngine.Object.DestroyImmediate(go);}
  }
 }
}
