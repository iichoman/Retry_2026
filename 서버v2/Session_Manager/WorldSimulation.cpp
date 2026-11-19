#include "WorldSimulation.h"
#include "PlayerEntity.h"
#include "PositionValidator.h"
#include "MonsterNavigation.h"
#include "MummyGas.h"
#include "Dungeon/CSharpRandom.h"
#include "Dungeon/RoomBlueprint.h"
#include "EnvironmentSystem.h"
#include "../Common/Logger.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <vector>

using namespace std::chrono;

WorldSimulation::WorldSimulation()
    : nextMonsterId(1)
{
}

// ============================================================================
//  SpawnMonsters - 시드 기반 결정적 스폰
// ============================================================================
void WorldSimulation::SpawnMonsters(const DungeonGenerator& dungeon, int seed)
{
    monsters.clear();
    nextMonsterId=1;

    // "Mons" 마법수로 시드 분리 (던전 RNG와 충돌 방지)
    CSharpRandom random(seed ^ 0x4D6F6E73);

    for (const Room& room : dungeon.rooms)
    {
        if (room.type == ROOM_TYPE_START) continue;

        int spawnCount;
        int monsterKind;

        if (room.type == ROOM_TYPE_BOSS)
        {
            spawnCount = 1;
            monsterKind = MONSTER_BOSS;
        }
        else if (room.type == ROOM_TYPE_REWARD)
        {
            spawnCount = Blueprints::GuardCount(seed,room);
            monsterKind = MONSTER_ELITE;
        }
        else
        {
            spawnCount = random.Next(1, 4);
            monsterKind = MONSTER_NORMAL;
        }

        // unordered_set은 비결정적 → 결정성 보장 위해 정렬
        std::vector<IntVec3> floorList(room.floorTiles.begin(), room.floorTiles.end());
        floorList.erase(std::remove_if(floorList.begin(),floorList.end(),[&](const IntVec3& tile){return !PositionValidator::IsFloorPosition(dungeon,dungeon.TileToFloorTop(tile));}),floorList.end());
        std::sort(floorList.begin(), floorList.end(),
            [](const IntVec3& a, const IntVec3& b) {
                if (a.x != b.x) return a.x < b.x;
                if (a.y != b.y) return a.y < b.y;
                return a.z < b.z;
            });
        if (floorList.empty()) continue;

        for (int i = 0; i < spawnCount; ++i)
        {
            int idx = random.Next(0, (int)floorList.size());
            if(monsterKind==MONSTER_BOSS || monsterKind==MONSTER_ELITE){float best=1e30f;auto center=room.bounds.center();if(monsterKind==MONSTER_ELITE){auto anchor=Blueprints::GuardTile(seed,room,i);center.x=(float)anchor.x;center.z=(float)anchor.z;}for(int j=0;j<(int)floorList.size();j++){float dx=floorList[j].x-center.x,dz=floorList[j].z-center.z;float distance=dx*dx+dz*dz;if(distance<best){best=distance;idx=j;}}}
            const IntVec3& tile = floorList[idx];
            // 격자 좌표 → 월드 좌표 (worldOffset 적용된 것)
            // 바닥 윗면에 세운다. TileToWorldCenter(y=0)를 쓰면 바닥 아래라 추락한다.
            Vec3 worldPos = dungeon.TileToFloorTop(tile);

            int mid = nextMonsterId++;
            bool mummy = room.type == ROOM_TYPE_NORMAL && i == 0 && (static_cast<unsigned>(seed) + room.id) % 3 == 0;
            auto m = std::make_unique<MonsterEntity>(mid, mummy ? MONSTER_MUMMY : monsterKind, worldPos);
            if (monsterKind == MONSTER_ELITE)
                m->attackElement = static_cast<int>((static_cast<unsigned>(seed) + room.id) % 5) - 1;
            monsters[mid] = std::move(m);
        }
    }

    // Spread small encounters through genuine corridor floor, away from spawn rooms,
    // doors, chest footprints and trap plates. A separate hash leaves room RNG intact.
    std::vector<IntVec3> corridorTiles;
    for(const auto& corridor:dungeon.corridors)for(auto tile:corridor.floorTiles)if(tile.y==0)corridorTiles.push_back(tile);
    std::sort(corridorTiles.begin(),corridorTiles.end(),[&](auto a,auto b){auto ha=Env::Hash(seed^0x434D4F4E,a.x,a.z),hb=Env::Hash(seed^0x434D4F4E,b.x,b.z);return ha!=hb?ha<hb:a.x!=b.x?a.x<b.x:a.z<b.z;});
    auto placements=Env::Layout(dungeon,seed);
    int corridorCount=0;
    for(auto tile:corridorTiles){
        if(corridorCount>=24)break;
        auto p=dungeon.TileToFloorTop(tile);
        if(dungeon.RoomIdAt(p)>=0||!MonsterNavigation::Walkable(dungeon,tile))continue;
        bool safe=true;
        for(const auto& start:dungeon.assignedStartRooms)if(tile.x>=start.bounds.xMin()-12&&tile.x<start.bounds.xMax()+12&&tile.z>=start.bounds.zMin()-12&&tile.z<start.bounds.zMax()+12)safe=false;
        for(const auto& room:dungeon.rooms)for(auto door:room.doorwayFloorTiles)if(std::abs(door.x-tile.x)+std::abs(door.z-tile.z)<5)safe=false;
        for(const auto& placed:placements)if(p.DistanceSqXZ(placed.position)<16)safe=false;
        for(const auto& entry:monsters)if(p.DistanceSqXZ(entry.second->position)<100)safe=false;
        if(!safe)continue;
        int mid=nextMonsterId++;monsters[mid]=std::make_unique<MonsterEntity>(mid,MONSTER_NORMAL,p);++corridorCount;
    }
    Log::Info("[WorldSim] 복도 몬스터 %d마리",corridorCount);
    Log::Info("[WorldSim] 몬스터 스폰 완료: %d마리 (방 %d개)",
        (int)monsters.size(), (int)dungeon.rooms.size());
}

// ============================================================================
//  Step - 매 틱
// ============================================================================
void WorldSimulation::Step(float dt,
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players,
    const DungeonGenerator& dungeon,
    std::vector<AttackEvent>& outAttacks)
{
    long long nowMs = duration_cast<milliseconds>(
        system_clock::now().time_since_epoch()).count();

    breathUpdates.clear();bossStates.clear();patternStarts.clear();patternEnds.clear();
    for (auto& kv : monsters)
    {
        MonsterEntity& m = *kv.second;
        if((m.kind==MONSTER_BOSS||m.kind==MONSTER_ELITE)&&StepPattern(m,dt,nowMs,players,dungeon,outAttacks))continue;
        if (m.kind == MONSTER_MUMMY) {
            StepMummyBreath(m,dt,players,dungeon,outAttacks);
            if (m.breath.phase != 0) continue;
        }
        if (m.aiState == AI_DEAD) continue;
        StepMonsterAI(m, dt, nowMs, players, dungeon, outAttacks);
    }
}

void WorldSimulation::StepMummyBreath(MonsterEntity& m,float dt,
    std::unordered_map<int,std::unique_ptr<PlayerEntity>>& players,const DungeonGenerator& dungeon,
    std::vector<AttackEvent>& attacks)
{
    auto& gas=m.breath;
    if(gas.phase==0)return;
    bool changed=false;
    if(m.hp<=0||m.aiState==AI_DEAD||m.effects.Stunned()) {gas.phase=0;changed=true;}
    else {
        gas.elapsed+=dt*((gas.phase==1||gas.phase==3)?m.effects.Slow():1.f);
        if(gas.phase==3&&gas.elapsed>=MummyGas::ChargeWindup){gas.phase=4;gas.elapsed-=MummyGas::ChargeWindup;changed=true;}
        if(gas.phase==4){
            Vec3 a=m.position,b(gas.x+gas.dx*MummyGas::ChargeSpeed*(std::min)(gas.elapsed,MummyGas::ChargeDuration),gas.y,gas.z+gas.dz*MummyGas::ChargeSpeed*(std::min)(gas.elapsed,MummyGas::ChargeDuration));
            if(!PositionValidator::IsFloorPosition(dungeon,b)||!MonsterNavigation::Clear(dungeon,a,b)){gas.phase=0;changed=true;}
            else {
                m.position=b;Vec3 segment=b-a;float length=segment.LengthSq();
                for(auto& entry:players){auto& p=*entry.second;
                    if(!p.IsActiveInWorld()||!p.conn||std::fabs(p.position.y-a.y)>2||std::find(m.breathVictims.begin(),m.breathVictims.end(),p.clientId)!=m.breathVictims.end())continue;
                    Vec3 offset=p.position-a;float t=length<.00001f?0:std::clamp((offset.x*segment.x+offset.z*segment.z)/length,0.f,1.f);
                    if((a+segment*t).DistanceSqXZ(p.position)<=.81f&&MonsterNavigation::Clear(dungeon,a,p.position)){m.breathVictims.push_back(p.clientId);attacks.push_back({m.id,p.clientId,16,true,false});}
                }
                if(gas.elapsed>=MummyGas::ChargeDuration){gas.phase=0;changed=true;}
            }
        }
        if(gas.phase==1&&gas.elapsed>=MummyGas::Windup){gas.phase=2;gas.elapsed-=MummyGas::Windup;changed=true;}
        if(gas.phase==2) {
            Vec3 origin(gas.x,gas.y,gas.z),front(origin.x+gas.dx*MummyGas::Front(gas.elapsed),origin.y,origin.z+gas.dz*MummyGas::Front(gas.elapsed));
            if(gas.elapsed>=MummyGas::Lifetime||!MonsterNavigation::Clear(dungeon,origin,front)){gas.phase=0;changed=true;}
            else for(auto& entry:players) {
                auto& p=*entry.second;
                if(!p.IsActiveInWorld()||!p.conn||std::find(m.breathVictims.begin(),m.breathVictims.end(),p.clientId)!=m.breathVictims.end())continue;
                if(MummyGas::Contains(origin,gas.dx,gas.dz,gas.elapsed,p.position)&&MonsterNavigation::Clear(dungeon,origin,p.position)) {
                    m.breathVictims.push_back(p.clientId);attacks.push_back({m.id,p.clientId,m.attackDamage,false,true});
                }
            }
        }
    }
    m.breathSync-=dt;
    if(changed||m.breathSync<=0){breathUpdates.push_back(gas);m.breathSync=.1f;}
}

// ============================================================================
//  StepMonsterAI - 단일 몬스터 1프레임 AI
//
//  단순 FSM:
//    IDLE   - 가만히. 일정 주기마다 주변 검색 → 감지 시 CHASE
//    CHASE  - 타겟 추격. 사거리 안 → ATTACK. 타겟 사망/이탈 → IDLE
//    ATTACK - 공격 쿨다운 후 데미지 발생. 사거리 벗어나면 CHASE.
//    DEAD   - 처리 안 함
//
//  벽 충돌은 단순화 (직선 이동). 추후 CollisionMesh 적용 시 보강.
// ============================================================================
void WorldSimulation::StepMonsterAI(MonsterEntity& m, float dt, long long nowMs,
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players,
    const DungeonGenerator& dungeon,
    std::vector<AttackEvent>& outAttacks)
{
    if(m.effects.Stunned()){m.pendingHitTime=0;return;}
    if(m.pendingHitTime>0)m.pendingHitTime+=(long long)(dt*1000.f*(1.f-m.effects.Slow()));
    // 공격 wind-up: 예약된 타격 시간 도달 시 실제 데미지 발생(LAND)
    if (m.pendingHitTime > 0 && nowMs >= m.pendingHitTime)
    {
        AttackEvent land{};
        land.monsterId = m.id;
        land.victimClientId = m.pendingVictim;
        land.damage = m.pendingDamage;   // damage>0 → 클라는 데미지만 반영(애니 X)
        auto victim=players.find(m.pendingVictim);
        if(victim!=players.end() && victim->second->IsActiveInWorld()
            && m.position.DistanceXZ(victim->second->position)<=m.attackRange
            && std::fabs(m.position.y-victim->second->position.y)<2.5f
            && MonsterNavigation::Clear(dungeon,m.position,victim->second->position))outAttacks.push_back(land);
        m.pendingHitTime = 0;
        m.pendingVictim = 0;
        m.pendingDamage = 0;
    }

    // 타겟 유효성 검증
    PlayerEntity* target = nullptr;
    if (m.targetClientId != 0)
    {
        auto it = players.find(m.targetClientId);
        if (it != players.end() && it->second->IsActiveInWorld() && it->second->conn)
        {
            target = it->second.get();
        }
        else
        {
            m.targetClientId = 0;
            m.aiState = AI_IDLE;
        }
    }

    switch (m.aiState)
    {
    case AI_IDLE:
    {
        // 일정 주기마다 주변 플레이어 감지 (0.5초 주기)
        if (nowMs - m.lastDecisionTime > 500)
        {
            m.lastDecisionTime = nowMs;
            int found = FindNearestPlayerInRange(m, players);
            if (found != 0)
            {
                m.targetClientId = found;
                m.aiState = AI_CHASE;
            }
        }
        break;
    }

    case AI_CHASE:
    {
        if (!target) { m.aiState = AI_IDLE; m.targetClientId = 0; break; }

        float dist = m.position.DistanceXZ(target->position);

        // 추격 한계
        if (dist > m.detectRange * 1.5f)
        {
            m.targetClientId = 0;
            m.aiState = AI_IDLE;
            break;
        }

        bool direct=MonsterNavigation::Clear(dungeon,m.position,target->position);
        if (dist <= m.attackRange && direct && std::fabs(m.position.y-target->position.y)<2.5f)
        {
            m.aiState = AI_ATTACK;
            break;
        }

        Vec3 waypoint=target->position;
        if(!direct){
            if(nowMs>=m.nextPathTime){
                m.chasePath=MonsterNavigation::Find(dungeon,m.position,target->position);m.chasePathIndex=0;
                m.chaseGoal=target->position;m.nextPathTime=nowMs+MonsterNavigation::RepathMs+(m.id%5)*53;
            }
            while(m.chasePathIndex<m.chasePath.size()&&m.position.DistanceSqXZ(m.chasePath[m.chasePathIndex])<.09f)m.chasePathIndex++;
            if(m.chasePathIndex>=m.chasePath.size())break;
            waypoint=m.chasePath[m.chasePathIndex];
        }else{m.chasePath.clear();m.chasePathIndex=0;}
        float dx = waypoint.x - m.position.x;
        float dz = waypoint.z - m.position.z;
        float invLen = 1.f / std::max(0.001f, std::sqrt(dx * dx + dz * dz));
        float vx = dx * invLen * m.moveSpeed * m.effects.Slow();
        float vz = dz * invLen * m.moveSpeed * m.effects.Slow();

        // 새 위치 후보
        float step=(std::min)(dt,std::sqrt(dx*dx+dz*dz)/(std::max)(.001f,m.moveSpeed*m.effects.Slow()));
        Vec3 attempted(m.position.x + vx * step, m.position.y, m.position.z + vz * step);

        // 위치 검증: 벽 통과 시 슬라이딩, 텔레포트 시 거부
        m.position = PositionValidator::ValidateMove(
            dungeon, m.position, attempted, m.moveSpeed * m.effects.Slow(), dt);

        // 타겟 방향으로 회전 (degrees, Unity Y축)
        m.rotY = std::atan2(dx, dz) * 180.f / 3.14159265358979f;
        break;
    }

    case AI_ATTACK:
    {
        if (!target) { m.aiState = AI_IDLE; m.targetClientId = 0; break; }

        float dist = m.position.DistanceXZ(target->position);
        if (dist > m.attackRange || std::fabs(m.position.y-target->position.y)>=2.5f || !MonsterNavigation::Clear(dungeon,m.position,target->position))
        {
            m.aiState = AI_CHASE;
            break;
        }

        // 타겟 향해 회전
        float dx = target->position.x - m.position.x;
        float dz = target->position.z - m.position.z;
        m.rotY = std::atan2(dx, dz) * 180.f / 3.14159265358979f;

        // 쿨다운 후 공격: 준비동작(START, 애니만) → windup 뒤 실제 타격(LAND)
        if (nowMs - m.lastAttackTime >= (long long)(m.attackCooldownMs / m.effects.Slow()) && m.pendingHitTime == 0)
        {
            m.lastAttackTime = nowMs;
            if(m.kind==MONSTER_MUMMY) {
                float length=(std::max)(.001f,std::sqrt(dx*dx+dz*dz));
                auto& gas=m.breath;++gas.serial;gas.phase=(++m.mummyAttackCount%2)==0?3:1;gas.elapsed=0;
                gas.x=m.position.x;gas.y=m.position.y;gas.z=m.position.z;
                gas.dx=dx/length;gas.dz=dz/length;
                if(dx*dx+dz*dz<.0001f){float yaw=m.rotY*3.14159265f/180;gas.dx=std::sin(yaw);gas.dz=std::cos(yaw);}
                m.breathVictims.clear();m.breathSync=.1f;breathUpdates.push_back(gas);
                break;
            }

            // START: 공격 애니메이션 트리거 (damage=0 → HP 변화 없음)
            if(m.kind==MONSTER_ELITE)++m.basicAttackCount;
            AttackEvent start{};
            start.monsterId = m.id;
            start.victimClientId = target->clientId;
            start.damage = 0;
            outAttacks.push_back(start);

            // windup 후 실제 데미지 예약 (애니메이션 타격 프레임과 맞춤)
            // Imported goblin_Attack: BeginSwing=1.264550s; one tick transport/presentation margin.
            const long long kAttackWindupMs = m.kind == MONSTER_ELITE ? 720 : 1320;
            m.pendingHitTime = nowMs + kAttackWindupMs;
            m.pendingVictim = target->clientId;
            m.pendingDamage = m.attackDamage;
        }
        break;
    }

    default:
        break;
    }
}

#include "WorldSimulationPatterns.inl"

// ============================================================================
//  FindNearestPlayerInRange
// ============================================================================
int WorldSimulation::FindNearestPlayerInRange(const MonsterEntity& m,
    std::unordered_map<int, std::unique_ptr<PlayerEntity>>& players)
{
    int   nearestId = 0;
    float bestSq = m.detectRange * m.detectRange;

    for (auto& kv : players)
    {
        const PlayerEntity& p = *kv.second;
        if (!p.IsActiveInWorld()) continue;
        if (!p.conn) continue;       // 끊긴 클라는 인지 안 함 (단순화)

        float d2 = m.position.DistanceSqXZ(p.position);
        if (d2 < bestSq)
        {
            bestSq = d2;
            nearestId = p.clientId;
        }
    }
    return nearestId;
}

// ============================================================================
//  ApplyDamageToMonster
// ============================================================================
bool WorldSimulation::ApplyDamageToMonster(int monsterId, int damage)
{
    auto it = monsters.find(monsterId);
    if (it == monsters.end()) return false;

    MonsterEntity& m = *it->second;
    if (m.aiState == AI_DEAD) return false;

    if(damage<=0)return false;
    m.hp -= damage;
    if(m.kind==MONSTER_BOSS && m.hp>0 && m.groggyRemaining<=0){m.stagger+=(float)damage;m.patternSync=0;
        if(m.stagger>=MonsterEntity::StaggerLimit){m.stagger=MonsterEntity::StaggerLimit;m.groggyRemaining=MonsterEntity::GroggyDuration;m.pendingHitTime=0;m.pendingVictim=0;m.pendingDamage=0;}}
    if (m.hp <= 0)
    {
        m.hp = 0;
        m.aiState = AI_DEAD;
        m.targetClientId = 0;
        return true;
    }
    return false;
}
