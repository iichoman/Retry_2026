
#include <chrono>
static long long EnvironmentNow(){return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count();}
void GameSession::EnvironmentInit(){
 environmentTraps.clear();dungeon.environmentChests.clear();dungeon.environmentFurniture=Env::Furniture(dungeon,mapSeed);int id=0;
 for(auto& p:Env::Layout(dungeon,mapSeed)){
  ++id;
  if(p.kind==0||p.kind==3){loot.SpawnEnvironmentChest(*this,(p.kind==3?Env::RewardChestSource:Env::ChestSource)-id,p.position,p.kind==3,mapSeed);dungeon.environmentChests.push_back(p.position);continue;}
  Env::Trap trap;auto& s=trap.view;s.id=id;s.kind=p.kind;s.x=p.position.x;s.y=p.position.y;s.z=p.position.z;s.dx=p.dx;s.dz=p.dz;s.length=p.length;s.width=p.kind==1?4.f:.8f;environmentTraps.push_back(trap);
 }
}
void GameSession::EnvironmentSync(int cid){auto p=GetPlayer(cid);if(!p||!p->conn)return;for(auto& trap:environmentTraps)p->conn->SendPacket(Env::Packet,&trap.view,sizeof(Env::State));}
void GameSession::EnvironmentTick(float dt){
 auto now=EnvironmentNow();
 for(auto& trap:environmentTraps){auto& s=trap.view;trap.timer-=dt;
  if(s.state==0){bool trigger=false;for(auto& kv:players){auto& p=*kv.second;if(p.hp>0&&!p.extracted&&Env::OnPlate(s,p.position))trigger=true;}
   for(auto& kv:worldSim.GetMonsters()){auto& m=*kv.second;if(m.hp>0&&Env::OnPlate(s,m.position))trigger=true;}
   if(!trigger)continue;s.state=1;trap.timer=s.kind==1?1.f:.25f;s.deadline=now+(s.kind==1?1000:250);++s.serial;trap.hit.clear();Broadcast(Env::Packet,&s,sizeof(s));continue;}
  if(s.state==1&&trap.timer<=0){s.state=2;trap.timer=Env::ActiveDuration(s);s.deadline=now+(long long)(trap.timer*1000);++s.serial;Broadcast(Env::Packet,&s,sizeof(s));}
  if(s.state==2){
   auto inShot=[&](const Vec3& pos){if(!Env::Inside(s,pos))return false;if(s.kind==1)return true;float along=(pos.x-s.x)*s.dx+(pos.z-s.z)*s.dz;float front=s.length+.4f-(Env::ActiveDuration(s)-trap.timer)*Env::ArrowSpeed;return along>=front-.5f&&along<=front+dt*Env::ArrowSpeed+.5f;};
   for(auto& kv:players){auto& p=*kv.second;if(p.hp<=0||p.extracted||!inShot(p.position)||!trap.hit.insert(p.clientId).second)continue;
    int damage=s.kind==1?18:12;if(p.ab.shield>0)damage=(int)(damage*(1-p.ab.shieldReduction));p.InterruptExtractionOnDamage(damage);p.hp=(std::max)(0,p.hp-damage);p.ab.idle=0;ABSync(p.clientId);
    MonsterAttackEvent hit{};hit.monsterId=0;hit.victimClientId=p.clientId;hit.damage=damage;hit.victimHpAfter=p.hp;Broadcast((int)PacketType::MONSTER_ATTACK_EVENT,&hit,sizeof(hit));
    if(p.hp==0){PlayerDied death{};death.victimId=p.clientId;death.killerId=0;Broadcast((int)PacketType::PLAYER_DIED,&death,sizeof(death));}
   }
   for(auto& kv:worldSim.GetMonsters()){auto& m=*kv.second;if(m.hp<=0||!inShot(m.position)||!trap.hit.insert(-m.id).second)continue;if(worldSim.ApplyDamageToMonster(m.id,s.kind==1?18:12))OnMonsterKilled(m.id,0);}
   if(trap.timer<=0){s.state=3;trap.timer=4;s.deadline=now+4000;++s.serial;Broadcast(Env::Packet,&s,sizeof(s));}
  }else if(s.state==3&&trap.timer<=0){s.state=0;s.deadline=0;++s.serial;Broadcast(Env::Packet,&s,sizeof(s));}
 }
}
