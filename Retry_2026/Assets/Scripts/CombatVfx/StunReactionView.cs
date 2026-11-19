using UnityEngine;
[DefaultExecutionOrder(900)]
public sealed class StunReactionView : MonoBehaviour
{
 bool networkStunned,active;float began,offlineUntil;
 Animator animator;
 public bool IsStunned=>networkStunned||(!NetworkRuntime.IsOnline&&Time.time<offlineUntil);
 public void SetNetworkStunned(bool value){networkStunned=value;if(value)Begin();}
 public void StunOffline(float duration){offlineUntil=Time.time+duration;Begin();}
 void Begin(){
  if(active)return;active=true;began=Time.time;animator=GetComponentInChildren<Animator>();
  GetComponent<Player_Attack>()?.CancelCurrentAttack();GetComponent<PlayerDefenseController>()?.CancelDefense();GetComponent<PlayerSkillView>()?.Cancel();GetComponent<Monster_Attack>()?.CancelAttack();GetComponent<MummyBreathView>()?.Stop();
  foreach(var trail in GetComponentsInChildren<WeaponSwingTrail>(true))trail.CancelSwing();
 }
 void LateUpdate(){
  if(!active)return;
  bool dead=GetComponent<Player_State>()?.IsDead==true||GetComponent<Monster_State>()?.IsDead==true;
  if(dead||!IsStunned){active=false;if(animator!=null){animator.speed=1;if(!dead){string idle=GetComponent<Monster_State>()!=null?"Locomotion":"Blend Tree";if(animator.HasState(0,Animator.StringToHash(idle)))animator.CrossFadeInFixedTime(idle,.08f);}}return;}
  GetComponent<Monster_Attack>()?.CancelAttack();
  if(animator==null)return;
  int state=Animator.StringToHash("Hit");if(!animator.HasState(0,state))state=Animator.StringToHash("Base Layer.test.Hit");
  if(animator.HasState(0,state)){animator.speed=1;animator.Play(state,0,Mathf.Min(.95f,(Time.time-began)/.35f));animator.Update(0);}
 }
}
