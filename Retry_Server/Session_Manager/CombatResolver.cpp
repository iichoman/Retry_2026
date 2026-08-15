#include "CombatResolver.h"
#include "GameSession.h"
#include "PlayerEntity.h"
#include "MonsterEntity.h"
#include "SessionClientConnection.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <chrono>
#include <cstring>
#include <cmath>
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
    case WEAPON_BIG_SWORD: return { 45, 3.5f, 3.0f, 900, false,  0.f };  // 양손검: 넓고 강함, 느림
    case WEAPON_SWORD:     return { 10, 2.5f, 2.0f, 400, false,  0.f };  // 한손검: 표준

                     // 원거리: 투사체 1발 (단일 대상)
                     // 원거리: 투사체 1발 (단일 대상). 테스트용으로 아주 느리게 비행.
    case WEAPON_BOW:       return { 30, 15.0f, 0.f, 600, true,  6.f };  // 활: 6 m/s (천천히)
    case WEAPON_GUN:       return { 15, 20.0f, 0.f, 200, true,  8.f };  // 총: 8 m/s (천천히)

    default:               return { 10, 2.5f, 2.0f, 400, false,  0.f };
    }
}

// ============================================================================
//  근거리 직육면체(OBB) 판정
//   origin에서 dir 방향으로 길이 length, 폭 width 인 박스 안에 target이 있는가.
//   XZ 평면 기준. forward 축 [0, length], 좌우 축 [-width/2, +width/2].
// ============================================================================
static bool InMeleeBox(const Vec3& origin, const Vec3& dir,
    float length, float width, const Vec3& target)
{
    float dl = std::sqrt(dir.x * dir.x + dir.z * dir.z);
    if (dl < 1e-4f) return false;
    float fx = dir.x / dl, fz = dir.z / dl;     // 정규화 forward

    float rx = target.x - origin.x;
    float rz = target.z - origin.z;

    float forward = rx * fx + rz * fz;          // 전방 거리
    if (forward < 0.f || forward > length) return false;

    // 우측 축 = forward를 -90도 회전한 (fz, -fx). 그 투영의 절댓값이 좌우 거리.
    float side = rx * fz - rz * fx;
    if (std::fabs(side) > width * 0.5f) return false;

    return true;
}

// ============================================================================
//  HandleAttack
// ============================================================================
void CombatResolver::HandleAttack(GameSession& session, int attackerId,
    const PlayerAttackRequest& req)
{
    // 1) 공격자 검증
    auto it = session.players.find(attackerId);
    if (it == session.players.end()) return;
    PlayerEntity& attacker = *it->second;
    if (!attacker.IsActiveInWorld()) return;          // 죽은 자는 공격 못 함

    // 2) 쿨다운 검사
    long long nowMs = duration_cast<milliseconds>(
        system_clock::now().time_since_epoch()).count();
    WeaponData wd = GetWeaponData(req.weaponKind);
    if (nowMs - attacker.lastAttackTime < wd.cooldownMs)
        return;                            // 쿨다운 미충족
    attacker.lastAttackTime = nowMs;

    // 3) PLAYER_ATTACK_BROADCAST: 모든 다른 클라에게 액션 애니 알림
    {
        PlayerAttackBroadcast pab{};
        pab.attackerId = attackerId;
        pab.weaponKind = req.weaponKind;
        pab.comboIndex = req.comboIndex;
        pab.originX = req.originX; pab.originY = req.originY; pab.originZ = req.originZ;
        pab.dirX = req.dirX;    pab.dirY = req.dirY;    pab.dirZ = req.dirZ;
        pab.timestamp = req.timestamp;
        session.Broadcast((int)PacketType::PLAYER_ATTACK_BROADCAST,
            &pab, sizeof(pab), attackerId);
    }

    // 공격 방향 결정: 클라가 보낸 dir 우선, 없으면 캐릭터 rotY로 계산.
    Vec3 origin = attacker.position;       // 위치는 서버 신뢰
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
        session.projSystem.Spawn(session, attackerId, req.weaponKind,
            origin, dir, wd.damage, wd.range, wd.projectileSpeed);
        return;
    }

    // ────────────────────────────────────────────────────────────────
    //  5) 근거리: 직육면체 범위 안 "모든" 대상 피격
    // ────────────────────────────────────────────────────────────────
    int hitCount = 0;

    // 5a) 몬스터 (살아있는 것 모두 검사)
    {
        auto& monsters = session.worldSim.GetMonsters();
        // 사망/명중으로 컬렉션이 바뀌지 않도록 대상 id 먼저 수집
        std::vector<int> targets;
        for (auto& kv : monsters)
        {
            const MonsterEntity& m = *kv.second;
            if (m.aiState == AI_DEAD) continue;
            if (InMeleeBox(origin, dir, wd.range, wd.width, m.position))
                targets.push_back(m.id);
        }

        for (int mid : targets)
        {
            bool killed = session.worldSim.ApplyDamageToMonster(mid, wd.damage);
            int hpAfter = 0;
            auto mIt = monsters.find(mid);
            if (mIt != monsters.end()) hpAfter = mIt->second->hp;

            CombatEvent ce{};
            ce.attackerId = attackerId;
            ce.targetId = -mid;             // 음수 = 몬스터
            ce.damage = wd.damage;
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
        for (auto& kv : session.players)
        {
            if (kv.first == attackerId) continue;
            const PlayerEntity& p = *kv.second;
            if (!p.IsActiveInWorld()) continue;
            if (InMeleeBox(origin, dir, wd.range, wd.width, p.position))
                targets.push_back(p.clientId);
        }

        for (int pid : targets)
        {
            auto pIt = session.players.find(pid);
            if (pIt == session.players.end()) continue;
            PlayerEntity& victim = *pIt->second;

            victim.hp -= wd.damage;
            if (victim.hp < 0) victim.hp = 0;

            CombatEvent ce{};
            ce.attackerId = attackerId;
            ce.targetId = pid;              // 양수 = 플레이어
            ce.damage = wd.damage;
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
            }
        }
    }

    if (hitCount > 0)
        Log::Info("[Combat] cid=%d melee weapon=%d hit %d target(s)",
            attackerId, req.weaponKind, hitCount);
}