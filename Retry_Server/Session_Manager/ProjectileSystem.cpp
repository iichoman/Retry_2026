#include "ProjectileSystem.h"
#include "GameSession.h"
#include "PlayerEntity.h"
#include "MonsterEntity.h"
#include "Dungeon/DungeonGenerator.h"
#include "../Common/PacketProtocol.h"
#include "../Common/Logger.h"

#include <cmath>
#include <cstring>
#include <vector>
#include <algorithm>

// ============================================================================
//  Spawn - 투사체 생성
// ============================================================================
void ProjectileSystem::Spawn(GameSession& session, int ownerId, int weaponKind,
    const Vec3& origin, const Vec3& dir,
    int damage, float maxDistance, float speed)
{
    // 방향을 XZ 평면으로 정규화 (서버는 수평 비행으로 단순화)
    Vec3 d(dir.x, 0.f, dir.z);
    d = d.Normalized();
    if (d.LengthSq() < 1e-6f)
        return;     // 방향 없음 → 발사 안 함

    ProjectileEntity proj;
    proj.id = nextId++;
    proj.ownerId = ownerId;
    proj.weaponKind = weaponKind;
    proj.damage = damage;
    proj.position = origin;
    proj.velocity = d * speed;
    proj.speed = speed;
    proj.maxDistance = maxDistance;
    proj.traveled = 0.f;
    proj.alive = true;

    int id = proj.id;
    projectiles[id] = proj;

    // 생성 broadcast (전체 클라). 클라는 직육면체를 띄운다.
    ProjectileSpawn sp{};
    sp.projectileId = id;
    sp.ownerId = ownerId;
    sp.weaponKind = weaponKind;
    sp.posX = origin.x; sp.posY = origin.y; sp.posZ = origin.z;
    sp.dirX = d.x;      sp.dirY = d.y;      sp.dirZ = d.z;
    sp.speed = speed;
    session.Broadcast((int)PacketType::PROJECTILE_SPAWN, &sp, sizeof(sp), 0);

    Log::Info("[Projectile] spawn id=%d owner=%d weapon=%d speed=%.0f",
        id, ownerId, weaponKind, speed);
}

// ============================================================================
//  Update - 매 틱 이동 + 충돌
// ============================================================================
void ProjectileSystem::Update(GameSession& session, float dtSec)
{
    if (projectiles.empty()) return;

    const DungeonGenerator& dungeon = session.dungeon;
    auto& players = session.players;
    auto& monsters = session.worldSim.GetMonsters();

    std::vector<int> toRemove;

    for (auto& kv : projectiles)
    {
        ProjectileEntity& p = kv.second;
        if (!p.alive) { toRemove.push_back(p.id); continue; }

        Vec3 prev = p.position;
        Vec3 next = p.position + p.velocity * dtSec;

        float segLen = prev.DistanceXZ(next);
        int steps = (int)std::ceil(segLen / 0.5f);
        if (steps < 1) steps = 1;

        bool  hit = false;
        int   hitType = 3;        // 기본 수명
        int   hitTarget = 0;
        Vec3  hitPos = next;

        // 이동 경로를 0.5m 간격 샘플링하며 충돌 검사 (tunneling 방지)
        for (int i = 1; i <= steps && !hit; ++i)
        {
            float t = (float)i / (float)steps;
            Vec3 s = prev + (next - prev) * t;

            // 1) 벽 충돌
            IntVec3 tile = dungeon.WorldToTile(s);
            tile.y = 0;
            if (dungeon.IsWallTile(tile))
            {
                hit = true; hitType = 0; hitTarget = 0; hitPos = s;
                break;
            }

            // 2) 몬스터 충돌 (살아있는 것)
            for (auto& mkv : monsters)
            {
                MonsterEntity& m = *mkv.second;
                if (m.aiState == AI_DEAD) continue;
                if (s.DistanceSqXZ(m.position) <= HIT_RADIUS * HIT_RADIUS)
                {
                    hit = true; hitType = 1; hitTarget = m.id; hitPos = s;
                    break;
                }
            }
            if (hit) break;

            // 3) 플레이어 충돌 (발사자 제외, 살아있는 것)
            for (auto& pkv : players)
            {
                if (pkv.first == p.ownerId) continue;
                PlayerEntity& pl = *pkv.second;
                if (pl.hp <= 0) continue;
                if (s.DistanceSqXZ(pl.position) <= HIT_RADIUS * HIT_RADIUS)
                {
                    hit = true; hitType = 2; hitTarget = pl.clientId; hitPos = s;
                    break;
                }
            }
        }

        if (hit)
        {
            // ── 명중 처리 ──
            if (hitType == 1)
            {
                // 몬스터 명중
                bool killed = session.worldSim.ApplyDamageToMonster(hitTarget, p.damage);
                int hpAfter = 0;
                auto mIt = monsters.find(hitTarget);
                if (mIt != monsters.end()) hpAfter = mIt->second->hp;

                CombatEvent ce{};
                ce.attackerId = p.ownerId;
                ce.targetId = -hitTarget;        // 음수 = 몬스터
                ce.damage = p.damage;
                ce.weaponKind = p.weaponKind;
                ce.comboIndex = 0;
                ce.targetHpAfter = hpAfter;
                ce.isCritical = 0;
                session.Broadcast((int)PacketType::COMBAT_EVENT, &ce, sizeof(ce), 0);

                Log::Info("[Projectile] id=%d hit monsterId=%d dmg=%d hpAfter=%d%s",
                    p.id, hitTarget, p.damage, hpAfter, killed ? " [KILLED]" : "");

                if (killed)
                {
                    MonsterDied md{};
                    md.monsterId = hitTarget;
                    md.killerId = p.ownerId;
                    session.Broadcast((int)PacketType::MONSTER_DIED, &md, sizeof(md), 0);
                }
            }
            else if (hitType == 2)
            {
                // 플레이어 명중 (PvP)
                auto pIt = players.find(hitTarget);
                if (pIt != players.end())
                {
                    PlayerEntity& victim = *pIt->second;
                    victim.hp -= p.damage;
                    if (victim.hp < 0) victim.hp = 0;

                    CombatEvent ce{};
                    ce.attackerId = p.ownerId;
                    ce.targetId = hitTarget;     // 양수 = 플레이어
                    ce.damage = p.damage;
                    ce.weaponKind = p.weaponKind;
                    ce.comboIndex = 0;
                    ce.targetHpAfter = victim.hp;
                    ce.isCritical = 0;
                    session.Broadcast((int)PacketType::COMBAT_EVENT, &ce, sizeof(ce), 0);

                    Log::Info("[Projectile] id=%d hit playerId=%d dmg=%d hpAfter=%d",
                        p.id, hitTarget, p.damage, victim.hp);

                    if (victim.hp <= 0)
                    {
                        PlayerDied pd{};
                        pd.victimId = hitTarget;
                        pd.killerId = p.ownerId;
                        session.Broadcast((int)PacketType::PLAYER_DIED, &pd, sizeof(pd), 0);
                    }
                }
            }
            // hitType == 0 (벽)이면 데미지 없음

            // 소멸 broadcast
            ProjectileDespawn dp{};
            dp.projectileId = p.id;
            dp.hitType = hitType;
            dp.hitTargetId = (hitType == 1) ? -hitTarget
                : (hitType == 2) ? hitTarget : 0;
            dp.posX = hitPos.x; dp.posY = hitPos.y; dp.posZ = hitPos.z;
            session.Broadcast((int)PacketType::PROJECTILE_DESPAWN, &dp, sizeof(dp), 0);

            p.alive = false;
            toRemove.push_back(p.id);
            continue;
        }

        // ── 충돌 없음: 이동 확정 ──
        p.position = next;
        p.traveled += segLen;

        if (p.traveled >= p.maxDistance)
        {
            // 사거리 초과 → 소멸 (수명)
            ProjectileDespawn dp{};
            dp.projectileId = p.id;
            dp.hitType = 3;       // 수명
            dp.hitTargetId = 0;
            dp.posX = p.position.x; dp.posY = p.position.y; dp.posZ = p.position.z;
            session.Broadcast((int)PacketType::PROJECTILE_DESPAWN, &dp, sizeof(dp), 0);

            p.alive = false;
            toRemove.push_back(p.id);
        }
        else
        {
            // 위치 갱신 broadcast
            ProjectileMove mv{};
            mv.projectileId = p.id;
            mv.posX = p.position.x; mv.posY = p.position.y; mv.posZ = p.position.z;
            session.Broadcast((int)PacketType::PROJECTILE_MOVE, &mv, sizeof(mv), 0);
        }
    }

    // 죽은 투사체 제거
    for (int id : toRemove)
        projectiles.erase(id);
}