using UnityEngine;
using UnityEngine.AI;

// The server owns online hits. Offline encounters use the same warned sector.
public sealed class KnightHeavyAttack : MonoBehaviour
{
    BossTelegraphData cast;
    SkillEffect warning;
    int serial,normalAttacks;
    bool active;
    public bool IsActive=>active;
    public bool TryStartOffline()
    {
        if(NetworkRuntime.IsOnline||++normalAttacks<3)return false;
        normalAttacks=0;
        var origin=transform.position;var target=GetComponent<Monster>()?.Target;
        var direction=target!=null?Vector3.ProjectOnPlane(target.position-origin,Vector3.up).normalized:transform.forward;
        if(direction.sqrMagnitude<.01f)direction=transform.forward;
        transform.rotation=Quaternion.LookRotation(direction);var end=origin+direction*6f;long now=ServerClock.NowMs;
        Telegraph(new BossTelegraphData{monsterId=GetInstanceID(),sequence=serial+1,pattern=4,phase=1,
            originX=origin.x,originY=origin.y,originZ=origin.z,targetX=end.x,targetY=end.y,targetZ=end.z,radius=6f,
            startTimeMs=now,resolveTimeMs=now+1100,endTimeMs=now+1150,serverTimeMs=now});
        return true;
    }
    public void Telegraph(BossTelegraphData value)
    {
        if(value.sequence<=serial||GetComponent<Monster_State>().IsDead||ServerClock.NowMs>=value.endTimeMs)return;
        cast=value;serial=value.sequence;active=true;
        GetComponent<Monster_Attack>()?.NetworkPlayAttack();
        var animator=GetComponentInChildren<Animator>();
        if(animator!=null&&animator.HasState(0,Animator.StringToHash("HeavyAttack")))
            animator.Play("HeavyAttack",0,Mathf.Clamp01((ServerClock.NowMs-value.startTimeMs)/1800f));
        if(warning!=null)Destroy(warning.gameObject);
        var origin=new Vector3(value.originX,value.originY,value.originZ);var direction=new Vector3(value.targetX,value.targetY,value.targetZ)-origin;direction.y=0;
        warning=SkillEffect.Charge(origin,direction.normalized,0,value.radius/6f,Mathf.Max(.05f,(value.resolveTimeMs-ServerClock.NowMs)/1000f));warning.SetTint(new Color(1,.15f,.06f));
        if(!NetworkRuntime.IsOnline){var agent=GetComponent<NavMeshAgent>();if(agent!=null&&agent.enabled&&agent.isOnNavMesh){agent.isStopped=true;agent.ResetPath();}}
    }
    public void Resolve(BossResolveData value)
    {
        if(value.sequence<serial)return;
        serial=value.sequence;active=false;if(warning!=null){Destroy(warning.gameObject);warning=null;}
        if(value.pattern==0){GetComponent<Monster_Attack>()?.CancelAttack();return;}
        SkillEffect.Slash(new Vector3(value.posX,value.posY,value.posZ),new Vector3(cast.targetX-cast.originX,0,cast.targetZ-cast.originZ).normalized,0,value.radius/6f);
    }
    void Update()
    {
        if(!active)return;
        var health=GetComponent<Monster_State>();
        if(health.IsDead||(!NetworkRuntime.IsOnline&&health.IsHit)){Cancel();return;}
        long now=ServerClock.NowMs;
        if(NetworkRuntime.IsOnline){if(now>cast.endTimeMs+500)Cancel();return;}
        if(now<cast.resolveTimeMs)return;
        var origin=new Vector3(cast.originX,cast.originY,cast.originZ);var end=new Vector3(cast.targetX,cast.targetY,cast.targetZ);
        foreach(var player in FindObjectsByType<Player_State>(FindObjectsSortMode.None)){
            if(player.IsDead||!Contains(origin,end,cast.radius,player.transform.position))continue;
            if(Physics.Linecast(origin+Vector3.up,player.transform.position+Vector3.up,LayerMask.GetMask("Default","Wall"),QueryTriggerInteraction.Ignore))continue;
            player.TakeDamage(28,gameObject);
        }
        Resolve(new BossResolveData{sequence=serial,pattern=4,phase=1,posX=origin.x,posY=origin.y,posZ=origin.z,radius=cast.radius,serverTimeMs=now});
    }
    public static bool Contains(Vector3 origin,Vector3 end,float radius,Vector3 point)
    {
        if(Mathf.Abs(point.y-origin.y)>2.5f)return false;
        var segment=end-origin;segment.y=0;var offset=point-origin;offset.y=0;
        float distance=offset.magnitude;
        return distance<=radius && Vector3.Dot(offset,segment.normalized)>=distance*.5f;
    }
    void Cancel(){active=false;if(warning!=null){Destroy(warning.gameObject);warning=null;}GetComponent<Monster_Attack>()?.CancelAttack();}
    void OnDisable()=>Cancel();
    void OnDestroy(){if(warning!=null)Destroy(warning.gameObject);}
}
