// Authoritative boss patterns and knight heavy attacks. The client only presents these.
bool WorldSimulation::StepPattern(MonsterEntity& m,float dt,long long now,
    std::unordered_map<int,std::unique_ptr<PlayerEntity>>& players,
    const DungeonGenerator& dungeon,std::vector<AttackEvent>& attacks)
{
    const bool boss=m.kind==MONSTER_BOSS;
    const int room=dungeon.RoomIdAt(m.patrolOrigin),phase=m.hp*2<=m.maxHp?2:1;
    auto inArena=[&](const Vec3& p){return !boss||room<0||dungeon.RoomIdAt(p)==room;};
    auto clearBody=[&](const Vec3& p){
        if(!inArena(p)||!PositionValidator::IsFloorPosition(dungeon,p))return false;
        if(boss)for(auto offset:std::vector<Vec3>{{1.3f,0,0},{-1.3f,0,0},{0,0,1.3f},{0,0,-1.3f},{.92f,0,.92f},{-.92f,0,.92f},{.92f,0,-.92f},{-.92f,0,-.92f}})
            if(!inArena(p+offset)||!PositionValidator::IsFloorPosition(dungeon,p+offset))return false;
        return true;
    };
    auto finish=[&](bool cancelled){
        auto& p=m.pattern;
        patternEnds.push_back({m.id,p.sequence,cancelled?0:p.pattern,p.phase,m.position.x,m.position.y,m.position.z,p.radius,now});
        m.patternActive=false;m.patternCooldown=boss?(phase==2?1.5f:2.2f):1.3f;
        m.aiState=m.hp<=0?AI_DEAD:AI_CHASE;
    };
    if(m.groggyRemaining>0){m.groggyRemaining=(std::max)(0.f,m.groggyRemaining-dt);if(m.groggyRemaining==0){m.stagger=0;m.patternSync=0;m.patternCooldown=1.f;}}
    m.patternCooldown=(std::max)(0.f,m.patternCooldown-dt);
    m.patternSync-=dt;
    if(m.patternSync<=0){
        if(boss)bossStates.push_back({m.id,phase,m.hp,m.maxHp,room,m.hp<=0?1:0,now,m.stagger,MonsterEntity::StaggerLimit,m.groggyRemaining,0});
        m.patternSync=.25f;
        if(m.patternActive&&m.groggyRemaining<=0&&!m.effects.Stunned()){auto p=m.pattern;p.serverTimeMs=now;patternStarts.push_back(p);}
    }
    if(m.hp<=0||m.aiState==AI_DEAD||m.effects.Stunned()||m.groggyRemaining>0){
        m.pendingHitTime=0;m.pendingVictim=0;m.pendingDamage=0;
        if(m.patternActive)finish(true);
        if(m.hp>0)m.aiState=AI_IDLE;
        return boss||m.hp<=0||m.effects.Stunned();
    }
    PlayerEntity* target=nullptr;float nearest=m.detectRange*m.detectRange;
    for(auto& entry:players){auto& p=*entry.second;
        if(!p.IsActiveInWorld()||!p.conn||!inArena(p.position)||std::fabs(p.position.y-m.position.y)>2.5f)continue;
        float distance=m.position.DistanceSqXZ(p.position);
        if(distance<nearest){nearest=distance;target=&p;}
    }
    if(!target){if(m.patternActive)finish(true);if(boss){m.aiState=AI_IDLE;m.targetClientId=0;}return boss;}
    if(m.patternActive){
        auto& p=m.pattern;
        float warning=(p.resolveTimeMs-p.startTimeMs)/1000.f,total=(p.endTimeMs-p.startTimeMs)/1000.f;
        float before=m.patternElapsed;m.patternElapsed+=dt;
        if(m.patternElapsed<warning)return true;
        Vec3 origin(p.originX,p.originY,p.originZ),end(p.targetX,p.targetY,p.targetZ),a=origin,b=origin;
        bool hit=false;
        if(p.pattern==2){
            a=m.position;
            float fraction=std::clamp((m.patternElapsed-warning)/(std::max)(.001f,total-warning),0.f,1.f);
            b=origin+(end-origin)*fraction;
            // The swept path was checked before the warning; recheck dynamic obstacles too.
            if(!clearBody(b)||!MonsterNavigation::Clear(dungeon,a,b)){finish(true);return true;}
            m.position=b;hit=true;
        }else if(before<warning){b=p.pattern==4?end:origin;hit=true;}
        if(hit)for(auto& entry:players){auto& victim=*entry.second;
            if(!victim.IsActiveInWorld()||!victim.conn||!inArena(victim.position)||std::fabs(victim.position.y-a.y)>2.5f
                ||std::find(m.patternVictims.begin(),m.patternVictims.end(),victim.clientId)!=m.patternVictims.end())continue;
            Vec3 segment=b-a,offset=victim.position-a;segment.y=offset.y=0;
            float along=offset.x*segment.x+offset.z*segment.z;
            if(p.pattern==4){float distance=std::sqrt(offset.LengthSq());
                if(distance>p.radius||along<distance*std::sqrt(segment.LengthSq())*.5f)continue;
            }
            float t=segment.LengthSq()<.001f?0:std::clamp(along/segment.LengthSq(),0.f,1.f);
            Vec3 closest=a+segment*t;
            if((p.pattern!=4&&closest.DistanceSqXZ(victim.position)>p.radius*p.radius)||!MonsterNavigation::Clear(dungeon,a,victim.position))continue;
            m.patternVictims.push_back(victim.clientId);
            attacks.push_back({m.id,victim.clientId,p.pattern==4?28:p.pattern==1?30:p.pattern==2?22:20,false});
        }
        if(m.patternElapsed>=total)finish(false);
        return true;
    }
    if(m.patternCooldown>0)return boss;
    m.targetClientId=target->clientId;
    bool heavy=boss||(m.basicAttackCount>=2&&m.pendingHitTime==0&&now-m.lastAttackTime>=(long long)m.attackCooldownMs);
    int kind=boss?m.nextPattern:4;
    float range=boss?(kind==1?4.2f:kind==2?12.f:7.f):5.5f;
    if(!heavy||nearest>range*range||!MonsterNavigation::Clear(dungeon,m.position,target->position)){
        if(!boss)return false;
        // Chase through normal navigation, with melee disabled: patterns own boss damage.
        m.aiState=AI_CHASE;float oldRange=m.attackRange;m.attackRange=0;
        StepMonsterAI(m,dt,now,players,dungeon,attacks);m.attackRange=oldRange;
        return true;
    }
    Vec3 direction=target->position-m.position;direction.y=0;direction=direction.Normalized();
    if(direction.LengthSq()<.001f)direction=Vec3(0,0,1);
    m.rotY=std::atan2(direction.x,direction.z)*180.f/3.14159265f;m.aiState=AI_ATTACK;
    Vec3 end=m.position;
    if(kind==2){for(float length=.25f;length<=14;length+=.25f){auto point=m.position+direction*length;if(!clearBody(point))break;end=point;}}
    if(kind==4)end=m.position+direction*6.f;
    float warning=kind==4?1.1f:phase==2?1.1f:1.4f;
    float travel=kind==2?m.position.DistanceXZ(end)/(phase==2?22.f:16.f):.05f;
    int serial=m.pattern.sequence+1;
    m.pattern={m.id,serial,kind,phase,m.position.x,m.position.y,m.position.z,end.x,end.y,end.z,
        kind==4?6.f:kind==1?4.5f:kind==2?1.65f:phase==2?10.f:8.f,now,now+(long long)(warning*1000),now+(long long)((warning+travel)*1000),now};
    m.patternActive=true;m.patternElapsed=0;m.patternVictims.clear();m.lastAttackTime=now;
    if(boss)m.nextPattern=m.nextPattern%3+1;else m.basicAttackCount=0;
    patternStarts.push_back(m.pattern);
    return true;
}
