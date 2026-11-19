#include "CombatResolver.h"
#include "../Common/Logger.h"
#include "../Common/PacketProtocol.h"
#include "GameSession.h"
#include "MonsterEntity.h"
#include "PlayerEntity.h"
#include "SessionClientConnection.h"

#include <chrono>
#include <cmath>
#include <cstring>
#include <vector>

using namespace std::chrono;

// ============================================================================
//  무기 데이터 (졸업 데모용 디폴트)
//
//  근거리(검): 직육면체 범위 공격. range=박스 길이, width=박스 폭.
//  원거리(활/총): 투사체. range=최대 비행거리, projectileSpeed=속력.
// ============================================================================
CombatResolver::WeaponData CombatResolver::GetWeaponData(int weaponKind)
{
    switch (weaponKind)
    {
        // 근거리: 닿는 모든 대상 피격 (범위)
    case WEAPON_BIG_SWORD:
        return {45, 3.5f, 3.0f, 1400, false, 0.f}; // 양손검: 넓고 강함, 느림
    case WEAPON_SWORD:
        return {20, 2.5f, 2.0f, 400, false, 0.f}; // 한손검: 표준 (기준 무기)

        // 원거리: 투사체 1발 (단일 대상)
        // 원거리: 투사체 1발 (단일 대상). 테스트용으로 아주 느리게 비행.
    case WEAPON_BOW:
        return {30, 15.0f, 0.f, 600, true, 6.f}; // 활: 6 m/s (천천히)
    case WEAPON_GUN:
        return {10, 20.0f, 0.f, 200, true, 8.f}; // 총: 8 m/s (천천히)

    default:
        return {10, 2.5f, 2.0f, 400, false, 0.f};
    }
}

// ============================================================================
//  근거리 직육면체(OBB) 판정
//   origin에서 dir 방향으로 길이 length, 폭 width 인 박스 안에 target이 있는가.
//   XZ 평면 기준. forward 축 [0, length], 좌우 축 [-width/2, +width/2].
// ============================================================================
static bool InMeleeBox(const Vec3 &origin, const Vec3 &dir, float length, float width, const Vec3 &target)
{
    float dl = std::sqrt(dir.x * dir.x + dir.z * dir.z);
    if (dl < 1e-4f)
        return false;
    float fx = dir.x / dl, fz = dir.z / dl; // 정규화 forward

    float rx = target.x - origin.x;
    float rz = target.z - origin.z;

    float forward = rx * fx + rz * fz; // 전방 거리
    if (forward < 0.f || forward > length)
        return false;

    // 우측 축 = forward를 -90도 회전한 (fz, -fx). 그 투영의 절댓값이 좌우 거리.
    float side = rx * fz - rz * fx;
    if (std::fabs(side) > width * 0.5f)
        return false;

    return true;
}

// ============================================================================
//  HandleAttack
// ============================================================================
void CombatResolver::HandleAttack(GameSession &session, int attackerId, const PlayerAttackRequest &req)
{
    // 1) 공격자 검증
    auto it = session.players.find(attackerId);
    if (it == session.players.end())
        return;
    PlayerEntity &attacker = *it->second;
    if (!attacker.IsActiveInWorld())
        return; // 죽은 자는 공격 못 함

    // 2) 쿨다운 검사
    long long nowMs = duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
    if (!std::isfinite(req.dirX) || !std::isfinite(req.dirZ) || req.comboIndex < 1 || req.comboIndex > 4)
        return;
    const float directionLengthSq=req.dirX*req.dirX+req.dirZ*req.dirZ;
    if(!std::isfinite(directionLengthSq)||directionLengthSq>4.f)return;
    const int equippedKind = AB::IsGreatsword(attacker.ab.weaponType) ? WEAPON_BIG_SWORD : attacker.ab.weaponType == 4 ? WEAPON_GUN : WEAPON_SWORD;
    if (equippedKind == WEAPON_BIG_SWORD && req.comboIndex > 3) return;
    if (req.weaponKind != equippedKind || attacker.ab.effects.Stunned() || attacker.ab.defenseLeft > 0 || attacker.skill.view.castLeft > 0)
        return;
    auto &ab = attacker.ab;
    ab.Recompute();
    if (session.loot.GetInventory(attackerId).GetCount(ab.view.weapon) <= 0)
        return;
    WeaponData wd = GetWeaponData(equippedKind);
    wd.damage = (int)(ab.view.damage * (ab.passiveType == 9 && ab.counter > 0 ? 1.05f + .1f * ab.tier : 1.f));
    wd.range *= ab.view.range;
    wd.width *= ab.view.range;
    wd.cooldownMs = (int)(wd.cooldownMs / ab.view.attackSpeed);
    if (nowMs - attacker.lastAttackTime < wd.cooldownMs)
        return; // 쿨다운 미충족
    if(equippedKind==WEAPON_BIG_SWORD&&req.comboIndex>1&&
       (attacker.lastGreatswordWeapon!=ab.view.weapon||attacker.lastGreatswordCombo!=req.comboIndex-1||nowMs-attacker.lastAttackTime>2400/ab.view.attackSpeed))return;
    if (ab.view.stamina < AB::AttackCost)
        return;
    if(!ab.demoInfiniteStamina) ab.view.stamina -= AB::AttackCost;
    ab.regenDelay = .5f;
    ab.counter = 0;
    attacker.lastGreatswordCombo=equippedKind==WEAPON_BIG_SWORD?req.comboIndex:0;
    attacker.lastGreatswordWeapon=ab.view.weapon;
    attacker.lastAttackTime = nowMs;
    session.ABSync(attackerId);

    // 3) PLAYER_ATTACK_BROADCAST: 모든 다른 클라에게 액션 애니 알림
    {
        PlayerAttackBroadcast pab{};
        pab.attackerId = attackerId;
        pab.weaponKind = req.weaponKind;
        pab.comboIndex = req.comboIndex;
        pab.originX = attacker.position.x;
        pab.originY = attacker.position.y;
        pab.originZ = attacker.position.z;
        pab.dirX = req.dirX;
        pab.dirY = req.dirY;
        pab.dirZ = req.dirZ;
        pab.timestamp = req.timestamp;
        session.Broadcast((int)PacketType::PLAYER_ATTACK_BROADCAST, &pab, sizeof(pab), attackerId);
    }

    // 공격 방향 결정: 클라가 보낸 dir 우선, 없으면 캐릭터 rotY로 계산.
    Vec3 origin = attacker.position; // 위치는 서버 신뢰
    Vec3 dir(req.dirX, 0.f, req.dirZ);
    if (dir.LengthSq() < 1e-6f)
    {
        // Unity 규약: yaw 0 = +Z, 시계방향
        float rad = attacker.rotY * 3.14159265f / 180.f;
        dir = Vec3(std::sin(rad), 0.f, std::cos(rad));
    }

    // ────────────────────────────────────────────────────────────────
    //  4) 원거리: 투사체 발사 (단일 대상은 투사체 비행 중 판정)
    // ────────────────────────────────────────────────────────────────
    if (wd.isRanged)
    {
        session.projSystem.Spawn(session, attackerId, req.weaponKind, origin, dir, wd.damage, wd.range,
                                 wd.projectileSpeed);
        return;
    }

    // ────────────────────────────────────────────────────────────────
    //  5) 근거리: 직육면체 범위 안 "모든" 대상 피격
    // ────────────────────────────────────────────────────────────────
    if(equippedKind==WEAPON_BIG_SWORD) {
        pending.push_back({attackerId,ab.view.weapon,attacker.lastAttackTime,(req.comboIndex==3?.72f:.56f)/ab.view.attackSpeed,req,wd});
        return;
    }
    ResolveMelee(session,attackerId,req,wd);
}

void CombatResolver::Update(GameSession& session,float dt)
{
    for(size_t i=0;i<pending.size();) {
        auto& swing=pending[i];auto it=session.players.find(swing.attackerId);
        bool cancel=it==session.players.end();
        if(!cancel){auto& p=*it->second;cancel=!p.IsActiveInWorld()||p.ab.effects.Stunned()||p.ab.defenseLeft>0||p.skill.view.castLeft>0||p.ab.view.weapon!=swing.weapon||p.lastAttackTime!=swing.token;}
        if(cancel){pending.erase(pending.begin()+i);continue;}
        swing.remaining-=dt;
        if(swing.remaining>0){++i;continue;}
        auto ready=swing;pending.erase(pending.begin()+i);
        ResolveMelee(session,ready.attackerId,ready.request,ready.data);
    }
}

void CombatResolver::ResolveMelee(GameSession& session,int attackerId,const PlayerAttackRequest& req,const WeaponData& wd)
{
    auto& attacker=*session.players.at(attackerId);auto& ab=attacker.ab;
    Vec3 origin=attacker.position,dir(req.dirX,0,req.dirZ);
    if(dir.LengthSq()<1e-6f){float yaw=attacker.rotY*3.14159265f/180;dir=Vec3(std::sin(yaw),0,std::cos(yaw));}
    int hitCount = 0;

    // 5a) 몬스터 (살아있는 것 모두 검사)
    {
        auto &monsters = session.worldSim.GetMonsters();
        // 사망/명중으로 컬렉션이 바뀌지 않도록 대상 id 먼저 수집
        std::vector<int> targets;
        for (auto &kv : monsters)
        {
            const MonsterEntity &m = *kv.second;
            if (m.aiState == AI_DEAD)
                continue;
            if (InMeleeBox(origin, dir, wd.range, wd.width, m.position))
                targets.push_back(m.id);
        }

        for (int mid : targets)
        {
            int hitDamage = wd.damage;
            if (ab.passiveType == 11 && monsters.at(mid)->effects.Slow() < 1)
                hitDamage = int(hitDamage * (1.f + .04f * ab.tier));
            int beforeHp = monsters.at(mid)->hp;
            bool killed = session.worldSim.ApplyDamageToMonster(mid, hitDamage);
            session.ABOnHit(attackerId, -mid, (std::min)(beforeHp, hitDamage));
            int hpAfter = 0;
            auto mIt = monsters.find(mid);
            if (mIt != monsters.end())
                hpAfter = mIt->second->hp;

            Log::Info("[근접] cid=%d weapon=%d combo=%d → 몬스터 %d  데미지 %d  HP %d→%d%s", attackerId, req.weaponKind,
                      req.comboIndex, mid, wd.damage, hpAfter + wd.damage, hpAfter, killed ? "  [처치]" : "");

            CombatEvent ce{};
            ce.attackerId = attackerId;
            ce.targetId = -mid; // 음수 = 몬스터
            ce.damage = hitDamage;
            ce.weaponKind = req.weaponKind;
            ce.comboIndex = req.comboIndex;
            ce.targetHpAfter = hpAfter;
            ce.isCritical = 0;
            session.Broadcast((int)PacketType::COMBAT_EVENT, &ce, sizeof(ce), 0);
            hitCount++;

            if (killed)
            {
                MonsterDied md{};
                md.monsterId = mid;
                md.killerId = attackerId;
                session.Broadcast((int)PacketType::MONSTER_DIED, &md, sizeof(md), 0);

                // 처치 시 전리품 생성 (서버 권위)
                session.OnMonsterKilled(mid, attackerId);
            }
        }
    }

    // 5b) 다른 플레이어 (PvP, 박스 안 모두)
    {
        std::vector<int> targets;
        for (auto &kv : session.players)
        {
            if (kv.first == attackerId)
                continue;
            const PlayerEntity &p = *kv.second;
            if (!p.IsActiveInWorld())
                continue;
            // 피아식별: 같은 팀은 때릴 수 없다.
            if (session.IsSameTeam(attackerId, kv.first))
                continue;
            if (InMeleeBox(origin, dir, wd.range, wd.width, p.position))
                targets.push_back(p.clientId);
        }

        for (int pid : targets)
        {
            auto pIt = session.players.find(pid);
            if (pIt == session.players.end())
                continue;
            PlayerEntity &victim = *pIt->second;

            int before = victim.hp;
            Vec3 toward = attacker.position - victim.position;
            float yaw = victim.rotY * 3.14159265f / 180.f;
            if (victim.ab.parry > 0 && toward.x * std::sin(yaw) + toward.z * std::cos(yaw) > 0)
            {
                victim.ab.parry = 0;
                victim.ab.counter = 3;
                ++victim.ab.parrySerial;
                attacker.ab.effects.Apply(2, 1, pid, AB::ParryStun / .3f);
                attacker.ab.defenseLeft = 0;
                session.ABSync(attackerId);
                session.ABSync(pid);
                return; // A parried swing cannot keep damaging other players.

            }
            int hitDamage = wd.damage;
            if (ab.passiveType == 11 && victim.ab.effects.Slow() < 1)
                hitDamage = int(hitDamage * (1.f + .04f * ab.tier));
            if (victim.ab.shield > 0)
                hitDamage = int(hitDamage * (1.f - victim.ab.shieldReduction));
            victim.InterruptExtractionOnDamage(hitDamage);
            victim.hp -= hitDamage;
            if (victim.hp < 0)
                victim.hp = 0;
            session.ABOnHit(attackerId, pid, before - victim.hp);
            session.ABSync(pid);

            Log::Info("[근접] cid=%d weapon=%d combo=%d → 플레이어 %d  데미지 %d  HP %d→%d%s", attackerId,
                      req.weaponKind, req.comboIndex, pid, wd.damage, before, victim.hp,
                      victim.hp <= 0 ? "  [사망]" : "");

            CombatEvent ce{};
            ce.attackerId = attackerId;
            ce.targetId = pid; // 양수 = 플레이어
            ce.damage = before - victim.hp;
            ce.weaponKind = req.weaponKind;
            ce.comboIndex = req.comboIndex;
            ce.targetHpAfter = victim.hp;
            ce.isCritical = 0;
            session.Broadcast((int)PacketType::COMBAT_EVENT, &ce, sizeof(ce), 0);
            hitCount++;

            if (victim.hp <= 0)
            {
                PlayerDied pd{};
                pd.victimId = pid;
                pd.killerId = attackerId;
                session.Broadcast((int)PacketType::PLAYER_DIED, &pd, sizeof(pd), 0);
                session.AddPlayerKill(attackerId,pid);
            }
        }
    }

    if (hitCount > 0)
        Log::Info("[Combat] cid=%d melee weapon=%d hit %d target(s)", attackerId, req.weaponKind, hitCount);
}