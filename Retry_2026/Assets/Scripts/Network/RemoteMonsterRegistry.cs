using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// ============================================================================
//  RemoteMonsterRegistry
//
//  서버 권위 몬스터 시스템의 클라 측 표시 담당.
//   - MONSTER_ENTER_VIEW → prefab instantiate, id로 등록
//   - MONSTER_MOVE → 위치/aiState 갱신 (Lerp으로 부드럽게)
//   - MONSTER_LEAVE_VIEW → destroy
//   - MONSTER_DIED → 사망 처리 (즉시 destroy 또는 fade)
//   - MONSTER_ATTACK_EVENT → 공격 애니 트리거
//
//  중요: 클라의 Monster.cs는 자체 AI를 가지므로 서버 권위 모드에서
//        반드시 비활성화. ENTER_VIEW에서 instantiate 직후 처리.
// ============================================================================
public class RemoteMonsterRegistry : MonoBehaviour
{
    public GameObject MonsterPrefab;

    private readonly Dictionary<int, RemoteMonsterEntry> entries = new();

    private class RemoteMonsterEntry
    {
        public GameObject go;
        public Animator animator;
        public Component monsterState;          // Monster_State (있으면)
        public bool isDead;                     // server-confirmed death (corpse)
        public FieldInfo hpBackingField;        // <CurrentHp>k__BackingField

        public Vector3 targetPos;
        public float targetYaw;
        public int aiState;
    }

    private void Update()
    {
        // 위치/회전 부드럽게 보간 (서버 50ms 갱신 → Lerp으로 시각적 부드러움)
        float lerpRate = 1f - Mathf.Exp(-15f * Time.deltaTime);   // 시간독립적 Lerp
        foreach (var kv in entries)
        {
            var e = kv.Value;
            if (e.go == null) continue;
            e.go.transform.position = Vector3.Lerp(e.go.transform.position, e.targetPos, lerpRate);
            float curYaw = e.go.transform.eulerAngles.y;
            float newYaw = Mathf.LerpAngle(curYaw, e.targetYaw, lerpRate);
            e.go.transform.eulerAngles = new Vector3(0f, newYaw, 0f);
        }
    }

    // ── 패킷 처리 진입점들 ───────────────────────────────────────────

    public void OnEnterView(MonsterEnterView p)
    {
        if (entries.ContainsKey(p.monsterId))
        {
            // 이미 있음 (시야 재진입 등) → 위치만 갱신
            var existing = entries[p.monsterId];
            existing.targetPos = new Vector3(p.posX, p.posY, p.posZ);
            existing.targetYaw = p.rotY;
            return;
        }

        if (MonsterPrefab == null)
        {
            Debug.LogError("[RemoteMonsters] MonsterPrefab 미설정!");
            return;
        }

        var spawnPos = new Vector3(p.posX, p.posY, p.posZ);
        var spawnRot = Quaternion.Euler(0f, p.rotY, 0f);
        var go = Instantiate(MonsterPrefab, spawnPos, spawnRot, transform);
        go.name = $"RemoteMonster_{p.monsterId}({(MonsterKind)p.monsterKind})";

        // 종류별 시각 차별화 (간단히 스케일/색상)
        ApplyKindVisuals(go, p.monsterKind);

        // 자체 AI 컴포넌트 비활성화 (Monster.cs가 Update에서 OverlapSphere 함)
        DisableLocalAI(go);

        // HP 등 동기화용 reflection 캐싱
        var entry = new RemoteMonsterEntry
        {
            go = go,
            animator = go.GetComponentInChildren<Animator>(),
            targetPos = spawnPos,
            targetYaw = p.rotY,
            aiState = (int)MonsterAiState.IDLE,
        };

        var ms = go.GetComponent("Monster_State") as Component;
        if (ms != null)
        {
            entry.monsterState = ms;
            entry.hpBackingField = ms.GetType().GetField(
                "<CurrentHp>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            // maxHp는 SerializeField. 기존 값 유지 또는 reflection으로 변경 가능.
            var ms0 = entry.monsterState as Monster_State;
            if (ms0 != null)
            {
                ms0.NetworkSetMax(p.maxHp, p.hp);   // match HP bar scale to server maxHp
                // already-dead monster (killed before we came into view) -> spawn as a corpse
                if (p.hp <= 0) ms0.NetworkDie();
            }
            else ApplyHp(entry, p.hp);
        }

        entries[p.monsterId] = entry;
        Debug.Log($"[RemoteMonsters] ENTER monsterId={p.monsterId} kind={(MonsterKind)p.monsterKind} hp={p.hp}");
    }

    public void OnLeaveView(int monsterId)
    {
        if (!entries.TryGetValue(monsterId, out var e)) return;
        if (e.go != null) Destroy(e.go);
        entries.Remove(monsterId);
    }

    public void OnMove(MonsterMove p)
    {
        if (!entries.TryGetValue(p.monsterId, out var e)) return;
        e.targetPos = new Vector3(p.posX, p.posY, p.posZ);
        e.targetYaw = p.rotY;

        // aiState 변화: ATTACK 진입 시 애니 트리거
        if (e.aiState != p.aiState)
        {
            e.aiState = p.aiState;
            // 추후: aiState별 애니 처리 (Animator의 trigger 사용 가능)
        }
    }

    public void OnDied(int monsterId)
    {
        if (!entries.TryGetValue(monsterId, out var e)) return;
        Debug.Log($"[RemoteMonsters] DIED monsterId={monsterId}");
        // play death anim and KEEP the corpse (do not destroy)
        var ms = e.monsterState as Monster_State;
        if (ms != null) ms.NetworkDie();
        else if (e.go != null) Destroy(e.go);
        e.isDead = true;
        // keep the entry: LEAVE/ENTER view must still manage this corpse.
        // (removing it spawned a DUPLICATE corpse when the monster re-entered view)
    }

    public void OnMonsterAttack(MonsterAttackEvent ev)
    {
        if (!entries.TryGetValue(ev.monsterId, out var e)) return;
        // drive Monster_Animation attack via pending-request system
        var ma = e.go != null ? e.go.GetComponent<Monster_Attack>() : null;
        if (ma != null) { ma.NetworkPlayAttack(); return; }
        if (e.animator != null)
        {
            var ps = e.animator.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].name == "Attack" && ps[i].type == AnimatorControllerParameterType.Trigger)
                {
                    e.animator.SetTrigger("Attack");
                    break;
                }
            }
        }
    }

    /// <summary>7단계: COMBAT_EVENT로 받은 몬스터 HP 갱신.</summary>
    public void OnHpChanged(int monsterId, int hp)
    {
        if (!entries.TryGetValue(monsterId, out var e)) return;
        ApplyHp(e, hp);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────

    private void DisableLocalAI(GameObject go)
    {
        // 자체 AI/타겟 감지를 끔. 시각 컴포넌트(Animator, MeshRenderer)는 살림.
        var monster = go.GetComponent("Monster") as MonoBehaviour;
        if (monster != null) monster.enabled = false;

        // Monster_movetest는 제거 (이동을 자체 처리하면 안 됨)
        var moveTest = go.GetComponent("Monster_movetest") as MonoBehaviour;
        if (moveTest != null) moveTest.enabled = false;

        // NavMeshAgent 비활성화 (서버가 위치 결정)
        var nav = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null) nav.enabled = false;

        // Monster_Attack도 자체 동작 막기 (서버가 공격 결정)
        var attack = go.GetComponent("Monster_Attack") as MonoBehaviour;
        if (attack != null) attack.enabled = false;

        // disable attack hitboxes: networked monster swing must not deal LOCAL damage (server authoritative)
        var boxes = go.GetComponentsInChildren<MonsterHitbox>(true);
        for (int i = 0; i < boxes.Length; i++) if (boxes[i] != null) boxes[i].enabled = false;
    }

    private void ApplyKindVisuals(GameObject go, int monsterKind)
    {
        // 임시: 종류별 스케일 변경
        switch ((MonsterKind)monsterKind)
        {
            case MonsterKind.NORMAL: go.transform.localScale = Vector3.one * 1.0f; break;
            case MonsterKind.ELITE: go.transform.localScale = Vector3.one * 1.3f; break;
            case MonsterKind.BOSS: go.transform.localScale = Vector3.one * 2.0f; break;
        }
    }

    private void ApplyHp(RemoteMonsterEntry e, int newHp)
    {
        // normal path: HpChanged event (HP bar) + hit/death anim
        var ms = e.monsterState as Monster_State;
        if (ms != null) { ms.NetworkApplyHp(newHp); return; }
        if (e.hpBackingField != null && e.monsterState != null)
        {
            e.hpBackingField.SetValue(e.monsterState, newHp);
        }
    }
}