using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Offline authority only. Network bosses use the original prefab without this component.
[RequireComponent(typeof(Monster_State),typeof(BossVfxController),typeof(LootContainer))]
public sealed class LocalBossController : MonoBehaviour
{
    public Bounds Arena = new Bounds(Vector3.zero,new Vector3(40,30,40));
    public bool RequireNavMesh;
    private DungeonGenerator_ChunkMesh dungeon;
    private Room room;
    private Monster_State health;
    private BossVfxController view;
    private LootContainer loot;
    private Player_State target;
    private Player_State[] players = System.Array.Empty<Player_State>();
    private readonly HashSet<int> charged = new();
    private int id, phase=1, sequence, nextPattern=1, lastHp, repeat;
    private float scanAt, readyAt;
    private float stagger, groggyRemaining;
    private const float StaggerLimit=500f, GroggyDuration=7f;
    private bool casting, resolved, rewarded;
    private BossTelegraphData skill;
    public int Phase => phase;
    public int ActivePattern => casting ? skill.pattern : 0;
    public bool Defeated => rewarded;
    private const float BodyRadius=1.3f, ChargeLength=14f;

    public void Configure(DungeonGenerator_ChunkMesh generator,Room assigned)
    {
        dungeon=generator; room=assigned; RequireNavMesh=true;
        var a=generator.TileToWorld(assigned.bounds.min);
        var b=generator.TileToWorld(assigned.bounds.max);
        Arena=new Bounds((a+b)*.5f,new Vector3(b.x-a.x,30,b.z-a.z));
        if(view!=null) view.SetRoomBounds(Arena);
    }
    private void Awake()
    {
        health=GetComponent<Monster_State>(); view=GetComponent<BossVfxController>(); loot=GetComponent<LootContainer>();
        foreach(var ai in GetComponents<Monster>()) ai.enabled=false;
        var move=GetComponent<Monster_movetest>(); if(move!=null) move.enabled=false;
        var attack=GetComponent<Monster_Attack>(); if(attack!=null) attack.enabled=false;
        var nav=GetComponent<NavMeshAgent>(); if(nav!=null) nav.enabled=false;
        foreach(var box in GetComponentsInChildren<MonsterHitbox>(true)) box.enabled=false;
        var drops=GetComponent<MonsterLoot>(); if(drops!=null) drops.enabled=false;
        loot.SetAvailable(false);
    }
    private void Start()
    {
        if(NetworkRuntime.IsOnline) {gameObject.SetActive(false); return;}
        id=GetInstanceID(); lastHp=health.CurrentHp;
        view.Initialize(id,lastHp,health.MaxHp); view.SetRoomBounds(Arena);
        health.HpChanged+=OnHp; health.Died+=OnDeath; State(); readyAt=Time.time+2;
    }
    private bool InRoom(Vector3 point)
    {
        if(room!=null && dungeon!=null) {var tile=dungeon.WorldToTile(point); tile.y=0; return room.floorTiles.Contains(tile);}
        return point.x>=Arena.min.x && point.x<=Arena.max.x && point.z>=Arena.min.z && point.z<=Arena.max.z;
    }
    private void State() => view.ApplyState(new BossStateData{monsterId=id,phase=phase,hp=health.CurrentHp,maxHp=health.MaxHp,roomId=room!=null?room.id:0,state=health.IsDead?1:0,serverTimeMs=ServerClock.NowMs,stagger=stagger,staggerMax=StaggerLimit,groggyRemaining=groggyRemaining});
    private void OnHp(int hp,int maximum)
    {
        if(NetworkRuntime.IsOnline) return;
        if(hp>0 && hp<lastHp) {
            view.OnHit(lastHp-hp);
            if(groggyRemaining<=0){stagger=Mathf.Min(StaggerLimit,stagger+lastHp-hp);if(stagger>=StaggerLimit){groggyRemaining=GroggyDuration;casting=false;repeat=0;}}
        }
        lastHp=hp;
        if(hp>0 && hp*2<=maximum && phase==1) {phase=2; State();}
        view.SetHp(hp,maximum);
        State();
    }
    private void Update()
    {
        if(NetworkRuntime.IsOnline) {casting=false; gameObject.SetActive(false); return;}
        if(health.IsDead) return;
        if(groggyRemaining>0){groggyRemaining=Mathf.Max(0,groggyRemaining-Time.deltaTime);if(groggyRemaining==0){stagger=0;readyAt=Time.time+1;}State();return;}
        if(Time.timeScale<=0) {Cancel(); return;}
        if(Time.time>=scanAt)
        {
            scanAt=Time.time+.4f;
            players=FindObjectsByType<Player_State>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            target=null; float nearest=float.MaxValue;
            foreach(var player in players)
            {
                if(player.IsDead || !InRoom(player.transform.position)) continue;
                float distance=(player.transform.position-transform.position).sqrMagnitude;
                if(distance<nearest) {nearest=distance; target=player;}
            }
        }
        if(target==null || target.IsDead || !InRoom(target.transform.position)) {Cancel(); return;}
        view.SetPresentationVisible(true);
        if(!casting)
        {
            if(Time.time<readyAt) return;
            if(repeat>0) {repeat=0; BeginPattern(3); return;}
            Vector3 to=target.transform.position-transform.position; to.y=0;
            if(to.magnitude>6) {Move(to.normalized*2.5f*Time.deltaTime); Face(to); return;}
            BeginPattern(nextPattern); nextPattern=nextPattern%3+1; return;
        }
        long now=ServerClock.NowMs;
        if(now<skill.resolveTimeMs) return;
        if(skill.pattern==2)
        {
            Vector3 start=transform.position;
            Vector3 end=new Vector3(skill.targetX,skill.targetY,skill.targetZ);
            Move(Vector3.ClampMagnitude(end-start,(phase==2?22:16)*Time.deltaTime));
            DamageSegment(start,transform.position,skill.radius,22);
            if(now<skill.endTimeMs && (end-transform.position).sqrMagnitude>.04f) return;
        }
        else if(!resolved)
        {
            resolved=true; DamageSegment(transform.position,transform.position,skill.radius,skill.pattern==1?24:18);
        }
        view.Resolve(new BossResolveData{monsterId=id,sequence=skill.sequence,pattern=skill.pattern,phase=skill.phase,posX=transform.position.x,posY=transform.position.y,posZ=transform.position.z,radius=skill.radius,serverTimeMs=now});
        casting=false;
        readyAt=Time.time+(repeat>0?.25f:phase==2?1.1f:1.8f);
    }
    private void Cancel()
    {
        casting=false; repeat=0; readyAt=Time.time+1;
        view.SetPresentationVisible(false);
    }
    private void Face(Vector3 direction) {direction.y=0; if(direction.sqrMagnitude>.001f) transform.rotation=Quaternion.LookRotation(direction);}
    private void BeginPattern(int pattern)
    {
        if(NetworkRuntime.IsOnline || health.IsDead || target==null) return;
        var origin=transform.position; var direction=target.transform.position-origin; direction.y=0;
        if(direction.sqrMagnitude<.01f) direction=transform.forward;
        Face(direction);
        var end=pattern==2?SafeEnd(origin,direction.normalized*ChargeLength):origin;
        long now=ServerClock.NowMs, warning=phase==2?(pattern==1?1200:1300):(pattern==1?1400:1600);
        long travel=pattern==2?(long)(Vector3.Distance(origin,end)/(phase==2?22:16)*1000):0;
        skill=new BossTelegraphData{monsterId=id,sequence=++sequence,pattern=pattern,phase=phase,
            originX=origin.x,originY=origin.y,originZ=origin.z,targetX=end.x,targetY=end.y,targetZ=end.z,
            radius=pattern==1?4.5f:pattern==2?BodyRadius:phase==2?10:8,startTimeMs=now,resolveTimeMs=now+warning,endTimeMs=now+warning+travel,serverTimeMs=now};
        casting=true; resolved=false; charged.Clear(); view.Telegraph(skill);
        // Every third chosen pattern gets one extra warned wave in phase 2.
        if(pattern==3 && phase==2 && nextPattern==3) repeat=1;
    }
    private Vector3 SafeEnd(Vector3 start,Vector3 delta)
    {
        var end=start+delta;
        end.x=Mathf.Clamp(end.x,Arena.min.x+BodyRadius,Arena.max.x-BodyRadius);
        end.z=Mathf.Clamp(end.z,Arena.min.z+BodyRadius,Arena.max.z-BodyRadius);
        if(RequireNavMesh)
        {
            if(!NavMesh.SamplePosition(start,out var nav,2,NavMesh.AllAreas)) return start;
            if(NavMesh.Raycast(nav.position,end,out var hit,NavMesh.AllAreas)) end=Vector3.MoveTowards(hit.position,start,BodyRadius);
        }
        var offset=end-start; float length=offset.magnitude;
        if(length>.001f)
            foreach(var hit in Physics.SphereCastAll(start+Vector3.up*1.5f,BodyRadius,offset/length,length,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Player_State>()!=null) continue;
                length=Mathf.Min(length,Mathf.Max(0,hit.distance-.05f));
            }
        end=start+(offset.sqrMagnitude>.001f?offset.normalized*length:Vector3.zero);
        // Never cross non-floor tiles when a room is available.
        if(room!=null) for(float t=.2f;t<=Vector3.Distance(start,end);t+=.2f)
            if(!InRoom(Vector3.MoveTowards(start,end,t))) return Vector3.MoveTowards(start,end,Mathf.Max(0,t-BodyRadius));
        return end;
    }
    private void Move(Vector3 delta) => transform.position=SafeEnd(transform.position,delta);
    private void DamageSegment(Vector3 a,Vector3 b,float radius,int amount)
    {
        if(NetworkRuntime.IsOnline || health.IsDead) return;
        foreach(var player in players)
        {
            if(player==null || player.IsDead || !InRoom(player.transform.position) || charged.Contains(player.GetInstanceID())) continue;
            Vector3 p=player.transform.position; var segment=b-a; segment.y=0; var offset=p-a; offset.y=0;
            Vector3 closest=a+segment*(segment.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector3.Dot(offset,segment)/segment.sqrMagnitude));
            var difference=p-closest; difference.y=0;
            if(difference.sqrMagnitude>radius*radius || Mathf.Abs(p.y-a.y)>3) continue;
            bool blocked=false;
            foreach(var hit in Physics.RaycastAll(closest+Vector3.up,p-closest,(p-closest).magnitude,~0,QueryTriggerInteraction.Ignore))
                if(!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<Player_State>()==null) {blocked=true; break;}
            if(blocked) continue;
            charged.Add(player.GetInstanceID());
            player.TryTakeMeleeDamage(amount,gameObject);
        }
    }
    private void OnDeath(Monster_State state,GameObject attacker)
    {
        casting=false; repeat=0; view.OnDeath();
        if(NetworkRuntime.IsOnline || rewarded) return;
        rewarded=true; loot.Clear();
        DungeonExitManager.Instance?.RegisterLocalBossKill();
        var catalog=ItemCatalog.Load();
        if(catalog!=null)
        {
            loot.TryAdd(catalog.Resolve("boss_core"),1); loot.TryAdd(catalog.Resolve("rare_gem"),3);
            loot.TryAdd(catalog.Resolve("gold_coin"),100); loot.TryAdd(catalog.Resolve("sword_001"),1);
        }
        loot.SetAvailable(!loot.IsEmpty());
        PooledVfxSpawner.Play(CombatVfxKind.Shockwave,transform.position,Quaternion.identity,2);
    }
    private void OnDisable()
    {
        casting=false; repeat=0;
        if(health!=null) {health.HpChanged-=OnHp; health.Died-=OnDeath;}
        if(view!=null) view.SetPresentationVisible(false);
    }
}
