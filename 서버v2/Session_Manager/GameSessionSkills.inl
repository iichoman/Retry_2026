#include "MonsterNavigation.h"
void GameSession::SkillSync(int cid){
 auto p=GetPlayer(cid);if(!p)return;
 auto stats=Skills::Calculate(p->ab);auto& v=p->skill.view;
 v.entityId=cid;v.power=stats.power;v.area=stats.area;v.castSpeed=stats.castSpeed;if(AB::IsGreatsword(p->ab.weaponType))v.area=std::min(2.f,stats.area+.08f*p->ab.upgrades[20]);v.buffLeft=p->ab.skillBuffLeft;
 if(v.eCooldown<=0)v.eDuration=Skills::Cooldown(p->ab,false);
 if(v.qCooldown<=0)v.qDuration=Skills::Cooldown(p->ab,true);
 if(p->conn)p->conn->SendPacket(Skills::StatePacket,&v,sizeof(v));
}
void GameSession::SkillEvent(PlayerEntity& p,int phase){
 Skills::Event ev;ev.entityId=p.clientId;ev.serial=p.skill.view.serial;ev.skill=p.skill.view.skill;ev.phase=phase;
 ev.x=p.skill.origin.x;ev.y=p.skill.origin.y;ev.z=p.skill.origin.z;ev.dx=p.skill.direction.x;ev.dz=p.skill.direction.z;
 ev.remaining=p.skill.view.castLeft;ev.weapon=p.skill.weapon;ev.area=p.skill.payload.area;ev.windup=p.skill.windup;
 Broadcast(Skills::EventPacket,&ev,sizeof(ev));
}
void GameSession::SkillCommand(int cid,const char* body,int size){
 auto p=GetPlayer(cid);if(!p||size!=sizeof(Skills::Command))return;
 Skills::Command c;std::memcpy(&c,body,sizeof(c));auto& cast=p->skill;auto& v=cast.view;
 if((c.version!=1&&c.version!=2)||c.sequence<=v.sequence)return;v.sequence=c.sequence;v.result=1;
 bool great=AB::IsGreatsword(p->ab.weaponType),drop=great&&c.skill==2;
 Vec3 aim=drop?Vec3(c.dx,p->position.y,c.dz)-p->position:Vec3(c.dx,0,c.dz);
 float norm=aim.x*aim.x+aim.z*aim.z;
 if(!gameStarted||!p->IsActiveInWorld()||p->ab.effects.Stunned()||p->ab.defenseLeft>0||v.castLeft>0||p->ab.weaponType==4
    ||(c.skill!=1&&c.skill!=2)||!std::isfinite(norm)||(drop?(c.version!=2||norm>Skills::DropRange*Skills::DropRange):(norm<.01f||norm>4))
    ||loot.GetInventory(cid).GetCount(p->ab.view.weapon)<=0
    ||(c.skill==1?v.eCooldown:v.qCooldown)>0){SkillSync(cid);return;}
 p->ab.Recompute();auto stats=Skills::Calculate(p->ab);cast.weapon=p->ab.view.weapon;
 Vec3 destination=p->position;
 if(drop){auto tile=dungeon.WorldToTile(Vec3(c.dx,p->position.y,c.dz));tile.y=0;destination=dungeon.TileToStandPosition(tile);destination.x=c.dx;destination.z=c.dz;
  if(!PositionValidator::IsFloorPosition(dungeon,destination)||!MonsterNavigation::Clear(dungeon,p->position,destination)){SkillSync(cid);return;}}
 v.skill=great?(c.skill==2?4:p->ab.upgrades[15]>0?5:3):c.skill;
 cast.payload.damage=p->ab.view.damage*(great?(drop?4.f:.8f):(c.skill==1?1.25f:2.5f))*stats.power;
 if(drop)cast.payload.damage*=1+.15f*p->ab.upgrades[16];
 cast.payload.area=great?std::min(2.f,stats.area+.08f*p->ab.upgrades[drop?20:19]):stats.area;
 cast.payload.traits=p->ab.Traits();cast.payload.resonance=.08f*p->ab.upgrades[11];
 cast.payload.refund=.35f*p->ab.upgrades[12];cast.windup=great?(drop?1.2f/stats.castSpeed:Skills::SpinDuration):Skills::Windup(c.skill)/stats.castSpeed;
 cast.origin=drop?destination:p->position;cast.direction=norm>.0001f?aim*(1.f/std::sqrt(norm)):Vec3(0,0,1);cast.released=false;cast.pulses=0;
 p->rotY=std::atan2(cast.direction.x,cast.direction.z)*180.f/3.14159265f;
 v.result=0;++v.serial;v.castLeft=cast.windup+Skills::Recovery;
 if(c.skill==1)v.eCooldown=v.eDuration=Skills::Cooldown(p->ab,false);else v.qCooldown=v.qDuration=Skills::Cooldown(p->ab,true);
 SkillSync(cid);SkillEvent(*p,1);
}
bool GameSession::SkillDamage(int source,int target,const Skills::Payload& payload,bool parry){
 auto p=GetPlayer(source);if(!p)return false;
 int mask=0;
 if(target>0){auto victim=GetPlayer(target);if(victim)mask=victim->ab.effects.Mask();}
 else {auto it=worldSim.GetMonsters().find(-target);if(it!=worldSim.GetMonsters().end())mask=it->second->effects.Mask();}
 float bonus=mask?1+payload.resonance:1;
 if((mask&2)&&payload.traits.passiveType==11)bonus*=1+.04f*payload.traits.tier;
 int damage=std::max(1,int(payload.damage*bonus));
 int after=0,actual=0;
 if(target>0){auto victim=GetPlayer(target);if(!victim||!victim->IsActiveInWorld()||IsSameTeam(source,target))return false;
  Vec3 toward=p->position-victim->position;float yaw=victim->rotY*3.14159265f/180;
  if(parry&&victim->ab.parry>0&&toward.x*std::sin(yaw)+toward.z*std::cos(yaw)>0){
   victim->ab.parry=0;victim->ab.counter=3;++victim->ab.parrySerial;p->ab.effects.Apply(2,1,target,AB::ParryStun/.3f);ABSync(target);ABSync(source);return true;
  }
  if(victim->ab.shield>0)damage=int(damage*(1-victim->ab.shieldReduction));
  actual=std::min(victim->hp,damage);victim->InterruptExtractionOnDamage(actual);victim->hp-=actual;after=victim->hp;ABSync(target);
 }else{auto it=worldSim.GetMonsters().find(-target);if(it==worldSim.GetMonsters().end()||it->second->hp<=0)return false;
  actual=std::min(it->second->hp,damage);worldSim.ApplyDamageToMonster(-target,damage);after=it->second->hp;
 }
 CombatEvent hit{};hit.attackerId=source;hit.targetId=target;hit.damage=actual;hit.targetHpAfter=after;hit.weaponKind=WEAPON_SWORD;
 Broadcast((int)PacketType::COMBAT_EVENT,&hit,sizeof(hit));ABOnHit(source,target,actual,true,&payload.traits);
 if(after==0&&actual>0&&p->IsActiveInWorld()){
  auto& v=p->skill.view;
  v.eCooldown=std::max(0.f,v.eCooldown-payload.refund);v.qCooldown=std::max(0.f,v.qCooldown-payload.refund);
  if(payload.traits.passiveType==18&&p->ab.echoCooldown<=0){v.eCooldown=0;p->ab.echoCooldown=24.f-4*payload.traits.tier;++v.resetSerial;}
  SkillSync(source);
 }
 if(after==0){if(target<0){MonsterDied death{};death.monsterId=-target;death.killerId=source;Broadcast((int)PacketType::MONSTER_DIED,&death,sizeof(death));OnMonsterKilled(-target,source);}
  else{PlayerDied death{};death.victimId=target;death.killerId=source;Broadcast((int)PacketType::PLAYER_DIED,&death,sizeof(death));AddPlayerKill(source,target);}}
 return false;
}
void GameSession::SkillTick(float dt){
 for(auto& pair:players){auto& p=*pair.second;auto& cast=p.skill;auto& v=cast.view;
  v.eCooldown=std::max(0.f,v.eCooldown-dt);v.qCooldown=std::max(0.f,v.qCooldown-dt);
  p.ab.skillBuffLeft=std::max(0.f,p.ab.skillBuffLeft-dt);
  p.ab.skillResetLock=std::max(0.f,p.ab.skillResetLock-dt);p.ab.echoCooldown=std::max(0.f,p.ab.echoCooldown-dt);
  if(v.castLeft<=0)continue;
  if(!p.IsActiveInWorld()||p.ab.effects.Stunned()||p.ab.defenseLeft>0||cast.weapon!=p.ab.view.weapon){v.castLeft=0;SkillEvent(p,0);SkillSync(p.clientId);continue;}
  v.castLeft=std::max(0.f,v.castLeft-dt);
  if(Skills::Spin(v.skill)||v.skill==4){
   auto strike=[&](Vec3 center,float radius){
    auto inside=[&](Vec3 pos){return std::abs(pos.y-center.y)<2.5f&&pos.DistanceSqXZ(center)<=radius*radius&&MonsterNavigation::Clear(dungeon,center,pos);};
    for(auto& target:players)if(!IsSameTeam(p.clientId,target.first)&&target.second->IsActiveInWorld()&&inside(target.second->position))SkillDamage(p.clientId,target.first,cast.payload,false);
    for(auto& target:worldSim.GetMonsters())if(target.second->hp>0&&inside(target.second->position))SkillDamage(p.clientId,-target.first,cast.payload,false);
   };
   if(Skills::Spin(v.skill)){
    while(cast.pulses<3&&cast.windup+Skills::Recovery-v.castLeft+.0001f>=(cast.pulses+1)*cast.windup/3){++cast.pulses;cast.origin=p.position;strike(p.position,Skills::SpinRadius*cast.payload.area);}
   }else if(!cast.released&&v.castLeft<=Skills::Recovery){cast.released=true;SkillEvent(p,2);strike(cast.origin,Skills::DropRadius*cast.payload.area);}
   if(v.castLeft<=0){SkillEvent(p,3);SkillSync(p.clientId);}continue;
  }
  if(!cast.released&&v.castLeft<=Skills::Recovery){cast.released=true;SkillEvent(p,2);
   if(v.skill==1){Skills::Wave wave;wave.view.entityId=p.clientId;wave.view.serial=v.serial;wave.view.skill=1;wave.view.phase=2;
    wave.view.x=cast.origin.x;wave.view.y=cast.origin.y;wave.view.z=cast.origin.z;wave.view.dx=cast.direction.x;wave.view.dz=cast.direction.z;
    wave.view.weapon=cast.weapon;wave.view.area=cast.payload.area;wave.view.windup=cast.windup;wave.payload=cast.payload;skillWaves.push_back(wave);
   }else{
    auto inside=[&](Vec3 pos){auto delta=pos-cast.origin;float dist=delta.DistanceXZ(Vec3());return std::abs(delta.y)<2.5f&&dist<=Skills::QRange*cast.payload.area&&dist>.001f
     &&(delta.x*cast.direction.x+delta.z*cast.direction.z)>=dist*Skills::QCos&&MonsterNavigation::Clear(dungeon,cast.origin,pos);};
    bool stopped=false;
    for(auto& target:players)if(!IsSameTeam(p.clientId,target.first)&&target.second->IsActiveInWorld()&&inside(target.second->position))
     if(SkillDamage(p.clientId,target.first,cast.payload,true)){stopped=true;break;}
    if(!stopped)for(auto& target:worldSim.GetMonsters())if(target.second->hp>0&&inside(target.second->position))SkillDamage(p.clientId,-target.first,cast.payload,false);
   }
  }
  if(v.castLeft<=0){SkillEvent(p,3);SkillSync(p.clientId);}
 }
 for(auto it=skillWaves.begin();it!=skillWaves.end();){auto& w=*it;Vec3 dir(w.view.dx,0,w.view.dz);bool ended=false;
  float range=Skills::Range*w.payload.area, width=Skills::HalfWidth*w.payload.area;
  float travel=std::min(dt*Skills::Speed,range-w.distance);int steps=std::max(1,int(std::ceil(travel/.2f)));
  for(int i=0;i<steps&&!ended;i++){
   Vec3 next=Vec3(w.view.x,w.view.y,w.view.z)+dir*(travel/steps);
   for(float side:{-width,0.f,width})if(!PositionValidator::IsFloorPosition(dungeon,next+Vec3(dir.z*side,0,-dir.x*side)))ended=true;
   if(ended)break;w.view.x=next.x;w.view.z=next.z;w.distance+=travel/steps;
   auto hit=[&](int id,Vec3 pos){auto delta=pos-next;float side=delta.x*dir.z-delta.z*dir.x,along=delta.x*dir.x+delta.z*dir.z;
    if(std::abs(delta.y)<2.5f&&std::abs(side)<=width+.35f&&std::abs(along)<=.45f&&w.hit.insert(id).second)SkillDamage(w.view.entityId,id,w.payload,false);};
   for(auto& target:players)if(target.second->IsActiveInWorld()&&!IsSameTeam(w.view.entityId,target.first))hit(target.first,target.second->position);
   for(auto& target:worldSim.GetMonsters())if(target.second->hp>0)hit(-target.first,target.second->position);
  }
  ended|=w.distance>=range-.001f;
  w.view.phase=ended?4:2;w.view.remaining=std::max(0.f,(range-w.distance)/Skills::Speed);
  Broadcast(Skills::EventPacket,&w.view,sizeof(w.view));
  if(ended)it=skillWaves.erase(it);else ++it;
 }
}
