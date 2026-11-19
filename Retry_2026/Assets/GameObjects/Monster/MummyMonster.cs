using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class MummyMonster : Monster
{
    NavMeshAgent agent;
    MummyBreathView breath;
    MummyBreathState gas;
    float nextAttack;
    int attackCount;
    readonly HashSet<int> victims=new();
    public static bool SpawnsInRoom(int seed,Room room,int index)=>room.type==RoomType.Normal&&index==0&&(unchecked((uint)seed+(uint)room.id)%3)==0;
    protected override void Awake(){base.Awake();agent=GetComponent<NavMeshAgent>();breath=GetComponent<MummyBreathView>();}
    protected override void Update()
    {
        if(NetworkRuntime.IsOnline)return;
        if(state!=null&&(state.IsDead||state.IsHit)){gas.phase=0;breath.Stop();StopMoving();return;}
        base.Update();
    }
    void StopMoving(){if(agent!=null&&agent.enabled&&agent.isOnNavMesh){agent.isStopped=true;agent.ResetPath();}}
    protected override void Tick()
    {
        if(gas.phase!=0) {
            StopMoving();gas.elapsed+=Time.deltaTime;
            if(gas.phase==3&&gas.elapsed>=MummyBreathView.ChargeWindup){gas.phase=4;gas.elapsed-=MummyBreathView.ChargeWindup;}
            if(gas.phase==4){
                var from=transform.position;
                var to=new Vector3(gas.x,gas.y,gas.z)+new Vector3(gas.dx,0,gas.dz)*MummyBreathView.ChargeSpeed*Mathf.Min(gas.elapsed,MummyBreathView.ChargeDuration);
                if(!Clear(from,to)||!NavMesh.SamplePosition(to,out var hit,.3f,NavMesh.AllAreas)||NavMesh.Raycast(from,to,out _,NavMesh.AllAreas)){gas.phase=0;breath.Apply(gas);return;}
                agent.Warp(hit.position);
                foreach(var player in FindObjectsByType<Player_State>(FindObjectsSortMode.None)){
                    var segment=to-from;var offset=player.transform.position-from;
                    float t=segment.sqrMagnitude<.00001f?0:Mathf.Clamp01(Vector3.Dot(offset,segment)/segment.sqrMagnitude);
                    if(player.IsDead||victims.Contains(player.GetInstanceID())||Mathf.Abs(offset.y)>2||Vector3.ProjectOnPlane(player.transform.position-(from+segment*t),Vector3.up).sqrMagnitude>.81f||!Clear(from,player.transform.position))continue;
                    victims.Add(player.GetInstanceID());player.TakeDamage(16,gameObject);
                }
                if(gas.elapsed>=MummyBreathView.ChargeDuration)gas.phase=0;
                breath.Apply(gas);return;
            }
            if(gas.phase==1&&gas.elapsed>=MummyBreathView.Windup){gas.phase=2;gas.elapsed-=MummyBreathView.Windup;}
            if(gas.phase==2) {
                var origin=new Vector3(gas.x,gas.y,gas.z);var direction=new Vector3(gas.dx,0,gas.dz);
                if(gas.elapsed>=MummyBreathView.Lifetime||!Clear(origin,origin+direction*MummyBreathView.Front(gas.elapsed))){gas.phase=0;breath.Stop();return;}
                foreach(var player in FindObjectsByType<Player_State>(FindObjectsSortMode.None)) {
                    if(player.IsDead||victims.Contains(player.GetInstanceID())||!MummyBreathView.Contains(origin,direction,gas.elapsed,player.transform.position)||!Clear(origin,player.transform.position))continue;
                    victims.Add(player.GetInstanceID());player.TakeDamage(8,gameObject);
                    if(!player.IsDead)(player.GetComponent<MummyPoison>()??player.gameObject.AddComponent<MummyPoison>()).Apply(gameObject);
                }
            }
            breath.Apply(gas);return;
        }
        if(target==null){StopMoving();return;}
        var delta=target.position-transform.position;delta.y=0;
        if(delta.sqrMagnitude<=attackRange*attackRange&&Clear(transform.position,target.position)) {
            StopMoving();if(delta.sqrMagnitude>.001f)transform.rotation=Quaternion.LookRotation(delta);
            if(Time.time<nextAttack)return;
            nextAttack=Time.time+4.2f;victims.Clear();gas=new MummyBreathState{version=1,serial=gas.serial+1,phase=(++attackCount%2)==0?3:1,x=transform.position.x,y=transform.position.y,z=transform.position.z,dx=transform.forward.x,dz=transform.forward.z};
            breath.Apply(gas);
        } else if(agent!=null&&agent.enabled&&agent.isOnNavMesh){agent.isStopped=false;agent.speed=moveSpeed;agent.SetDestination(target.position);if(agent.desiredVelocity.sqrMagnitude>.01f)transform.rotation=Quaternion.LookRotation(agent.desiredVelocity);}
    }
    static bool Clear(Vector3 a,Vector3 b)=>!Physics.Linecast(a+Vector3.up,b+Vector3.up,LayerMask.GetMask("Wall","Default"),QueryTriggerInteraction.Ignore);
}
