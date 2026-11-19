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
    public GameObject GetActor(int id) => entries.TryGetValue(id, out var entry) ? entry.go : null;

    public GameObject MonsterPrefab;
    public GameObject BossPrefab;
    public GameObject ElitePrefab;
    public GameObject MummyPrefab;
    readonly Dictionary<int,(MummyBreathState state,float time)> mummyStates = new();
    public void OnMummyBreath(MummyBreathState state)
    {
        if(state.version!=1||state.phase<0||state.phase>4||!float.IsFinite(state.elapsed)||state.elapsed<0||state.elapsed>3)return;
        if(state.phase==0)mummyStates.Remove(state.monsterId);else mummyStates[state.monsterId]=(state,Time.unscaledTime);
        GetActor(state.monsterId)?.GetComponent<MummyBreathView>()?.Apply(state);
    }
    public bool IsNearServerBossPosition(Vector3 position)
    {
        foreach(var entry in entries.Values)
        {
            if(entry.isDead || entry.boss == null || entry.go == null) continue;
            Vector3 delta=position-entry.targetPos;
            if(Mathf.Abs(delta.y)<=4f && delta.x*delta.x+delta.z*delta.z<=36f) return true;
        }
        return false;
    }
    private NetworkBootstrap bootstrap;
    private DungeonGenerator_ChunkMesh dungeon;
    private float nextRoomCheck;
    private readonly Dictionary<int, BossStateData> bossStates = new();
    private readonly Dictionary<int,BossTelegraphData> patterns=new();

    public void Initialize(NetworkBootstrap bs, DungeonGenerator_ChunkMesh generator)
    {
        bootstrap = bs;
        dungeon = generator;
    }

    private readonly Dictionary<int, RemoteMonsterEntry> entries = new();

    private class RemoteMonsterEntry
    {
        public GameObject go;
        public Animator animator;
        public Component monsterState;          // Monster_State (있으면)
        public bool isDead;                     // server-confirmed death (corpse)
        public BossVfxController boss;
        public HitEffectReceiver hitEffect;
        public Collider bodyCollider;
        public int boundedRoom = -1;
        public FieldInfo hpBackingField;        // <CurrentHp>k__BackingField

        public Vector3 targetPos;
        public float targetYaw;
        public int aiState;
        public MonsterAlertEffect alert;
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
        if (Time.unscaledTime >= nextRoomCheck)
        {
            nextRoomCheck = Time.unscaledTime + 0.15f;
            foreach (var kv in entries)
                if (kv.Value.boss != null) UpdateBossVisibility(kv.Key, kv.Value);
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

        if (ElitePrefab == null) ElitePrefab = Resources.Load<GameObject>("KnightElite");
        if (MummyPrefab == null) MummyPrefab = Resources.Load<GameObject>("Mummy");
        var selectedPrefab = p.monsterKind == (int)MonsterKind.BOSS && BossPrefab != null ? BossPrefab
            : p.monsterKind == (int)MonsterKind.MUMMY ? MummyPrefab
            : p.monsterKind == (int)MonsterKind.ELITE && ElitePrefab != null ? ElitePrefab : MonsterPrefab;
        if (selectedPrefab == null)
        {
            Debug.LogError("[RemoteMonsters] MonsterPrefab 미설정!");
            return;
        }

        var spawnPos = new Vector3(p.posX, p.posY, p.posZ);
        var spawnRot = Quaternion.Euler(0f, p.rotY, 0f);
        var go = Instantiate(selectedPrefab, spawnPos, spawnRot, transform);
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
            hitEffect = go.GetComponentInChildren<HitEffectReceiver>(),
            bodyCollider = go.GetComponent<Collider>(),
            targetPos = spawnPos,
            targetYaw = p.rotY,
            aiState = (int)MonsterAiState.IDLE,
            alert = go.GetComponent<MonsterAlertEffect>() ?? go.AddComponent<MonsterAlertEffect>(),
            isDead = p.hp <= 0,
        };

        var ms = go.GetComponent("Monster_State") as Component;
        if (entry.animator != null) entry.animator.applyRootMotion = false;
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
                ms0.IsNetworkBoss = p.monsterKind == (int)MonsterKind.BOSS;
                ms0.NetworkSetMax(p.maxHp, p.hp);   // match HP bar scale to server maxHp
                // already-dead monster (killed before we came into view) -> spawn as a corpse
                if (p.hp <= 0)
                {
                    ms0.NetworkDie();
                    go.GetComponent<Monster_Animation>()?.ShowCorpse();
                }
            }
            else ApplyHp(entry, p.hp);
        }

        entries[p.monsterId] = entry;
        if(p.monsterKind==(int)MonsterKind.ELITE&&go.GetComponent<KnightHeavyAttack>()==null)go.AddComponent<KnightHeavyAttack>();
        if(p.monsterKind==(int)MonsterKind.MUMMY&&p.hp>0&&mummyStates.TryGetValue(p.monsterId,out var gas)&&Time.unscaledTime-gas.time<.3f)
        {var state=gas.state;state.elapsed+=Time.unscaledTime-gas.time;go.GetComponent<MummyBreathView>()?.Apply(state);}
        if (p.monsterKind == (int)MonsterKind.BOSS)
        {
            entry.boss = go.GetComponent<BossVfxController>();
            if (entry.boss == null) entry.boss = go.AddComponent<BossVfxController>();
            entry.boss.Initialize(p.monsterId, p.hp, p.maxHp);
            if (bossStates.TryGetValue(p.monsterId, out var state)) entry.boss.ApplyState(state);
            UpdateBossVisibility(p.monsterId, entry);
            if (p.hp <= 0) entry.boss.OnDeath();
        }
        if(p.hp>0&&patterns.TryGetValue(p.monsterId,out var pattern)&&ServerClock.NowMs<pattern.endTimeMs){
            if(pattern.pattern==4)go.GetComponent<KnightHeavyAttack>()?.Telegraph(pattern);else entry.boss?.Telegraph(pattern);
        }
    }

    public void OnLeaveView(int monsterId)
    {
        if (!entries.TryGetValue(monsterId, out var e)) return;
        if (e.boss != null) e.boss.SetPresentationVisible(false);
        if (e.go != null) Destroy(e.go);
        entries.Remove(monsterId);
    }

    public void OnMove(MonsterMove p)
    {
        if (!entries.TryGetValue(p.monsterId, out var e)) return;
        if (e.isDead || (e.monsterState as Monster_State)?.IsDead == true) return;
        e.targetPos = new Vector3(p.posX, p.posY, p.posZ);
        e.targetYaw = p.rotY;

        if (p.aiState == (int)MonsterAiState.DEAD) { OnDied(p.monsterId); return; }
        ApplyDetectionState(e, p.aiState);
    }

    private static void ApplyDetectionState(RemoteMonsterEntry e, int next)
    {
        bool wasTracking = e.aiState == (int)MonsterAiState.CHASE || e.aiState == (int)MonsterAiState.ATTACK;
        bool isTracking = next == (int)MonsterAiState.CHASE || next == (int)MonsterAiState.ATTACK;
        if (!wasTracking && isTracking) e.alert?.Show();
        e.aiState = next;
    }

    public void OnDied(int monsterId)
    {
        patterns.Remove(monsterId);
        mummyStates.Remove(monsterId);
        if (!entries.TryGetValue(monsterId, out var e)) return;
        if (e.isDead) return;
        e.alert?.Hide();
        e.boss?.OnDeath();
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
        if (e.isDead || e.go == null || (e.monsterState as Monster_State)?.IsDead == true) return;
        ApplyDetectionState(e, (int)MonsterAiState.ATTACK);
        e.boss?.OnBasicAttackStarted();
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
    public void OnHpChanged(int monsterId, int hp, int damage = 0, int weaponKind = 0)
    {
        if (!entries.TryGetValue(monsterId, out var e)) return;
        // The COMBAT_EVENT is confirmation even when a preceding snapshot already applied HP.
        bool confirmedHit = damage > 0;
        if(confirmedHit && e.boss==null && e.go!=null)MonsterHitFlash.Play(e.go);
        ApplyHp(e, hp);
        if (e.boss != null)
        {
            e.boss.SetHp(hp, (e.monsterState as Monster_State)?.MaxHp ?? hp);
            if (confirmedHit && hp > 0) e.boss.OnHit(damage);
        }
        else if (confirmedHit && (weaponKind == (int)WeaponKind.SWORD || weaponKind == (int)WeaponKind.BIG_SWORD) && e.go != null)
        {
            // Packets contain no contact point. Place cosmetic sparks on the visible body
            // surface, not inside the opaque torso. Depth testing still hides them behind walls.
            var camera = Camera.main;
            var center = e.bodyCollider != null ? e.bodyCollider.bounds.center : e.go.transform.position + Vector3.up;
            var normal = camera != null ? camera.transform.position - center : -e.go.transform.forward;
            normal = normal.sqrMagnitude > .001f ? normal.normalized : Vector3.forward;
            var point = e.bodyCollider != null ? e.bodyCollider.ClosestPoint(center + normal * 10f) : center + normal * .5f;
            point += normal * .08f;
            if (e.hitEffect != null) e.hitEffect.Play(HitEffectType.Melee, point, normal);
            else HitEffectReceiver.PlayBuiltInEffect(HitEffectType.Melee, point, Quaternion.LookRotation(normal));
        }
    }

    public void OnBossState(BossStateData state)
    {
        bossStates[state.monsterId] = state;
        if (!entries.TryGetValue(state.monsterId, out var entry) || entry.boss == null) return;
        UpdateBossVisibility(state.monsterId, entry);
        entry.boss.ApplyState(state);
    }

    public void OnBossTelegraph(BossTelegraphData data)
    {
        patterns[data.monsterId]=data;
        if (!entries.TryGetValue(data.monsterId, out var entry) || entry.boss == null || entry.isDead) return;
        UpdateBossVisibility(data.monsterId, entry);
        entry.boss.Telegraph(data);
    }

    public void OnBossResolve(BossResolveData data)
    {
        patterns.Remove(data.monsterId);
        if (entries.TryGetValue(data.monsterId, out var entry) && entry.boss != null && !entry.isDead)
            entry.boss.Resolve(data);
    }
    public void OnKnightTelegraph(BossTelegraphData data)
    {
        ServerClock.Observe(data.serverTimeMs);patterns[data.monsterId]=data;
        GetActor(data.monsterId)?.GetComponent<KnightHeavyAttack>()?.Telegraph(data);
    }
    public void OnKnightResolve(BossResolveData data)
    {
        patterns.Remove(data.monsterId);GetActor(data.monsterId)?.GetComponent<KnightHeavyAttack>()?.Resolve(data);
    }

    private void UpdateBossVisibility(int monsterId, RemoteMonsterEntry entry)
    {
        bool sameRoom = false;
        if (bootstrap != null && bootstrap.LocalPlayer != null && dungeon != null)
        {
            var tile = dungeon.WorldToTile(bootstrap.LocalPlayer.transform.position);
            tile.y = 0;
            // v1/v2 send MONSTER_ENTER_VIEW/MOVE but no BOSS_STATE. Use their reported
            // boss position to locate the room for presentation only; never run local AI.
            bool hasState = bossStates.TryGetValue(monsterId, out var state);
            var bossTile = dungeon.WorldToTile(entry.targetPos);
            foreach (var room in dungeon.Rooms)
                if (hasState ? room.id == state.roomId :
                    bossTile.x >= room.bounds.xMin && bossTile.x < room.bounds.xMax &&
                    bossTile.z >= room.bounds.zMin && bossTile.z < room.bounds.zMax)
                {
                    sameRoom = room.floorTiles.Contains(tile);
                    if (entry.boundedRoom != room.id && room.floorTiles.Count > 0)
                    {
                        var minimum = new Vector3Int(int.MaxValue, 0, int.MaxValue);
                        var maximum = new Vector3Int(int.MinValue, 0, int.MinValue);
                        foreach (var floor in room.floorTiles)
                        {
                            minimum.x = Mathf.Min(minimum.x, floor.x); minimum.z = Mathf.Min(minimum.z, floor.z);
                            maximum.x = Mathf.Max(maximum.x, floor.x); maximum.z = Mathf.Max(maximum.z, floor.z);
                        }
                        Vector3 a = dungeon.TileToWorld(minimum), b = dungeon.TileToWorld(maximum + new Vector3Int(1, 0, 1));
                        entry.boss.SetRoomBounds(new Bounds((a + b) * 0.5f, new Vector3(b.x - a.x, 100, b.z - a.z)));
                        entry.boundedRoom = room.id;
                    }
                    break;
                }
        }
        entry.boss.SetPresentationVisible(sameRoom);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────

    private void DisableLocalAI(GameObject go)
    {
        foreach (var loot in go.GetComponentsInChildren<MonsterLoot>(true)) loot.enabled = false;
        foreach (var container in go.GetComponentsInChildren<LootContainer>(true))
        {
            container.SetAvailable(false);
            container.enabled = false;
        }
        // 자체 AI/타겟 감지를 끔. 시각 컴포넌트(Animator, MeshRenderer)는 살림.
        var monster = go.GetComponent("Monster") as MonoBehaviour;
        if (monster != null) monster.enabled = false;

        // Monster_movetest는 제거 (이동을 자체 처리하면 안 됨)
        var moveTest = go.GetComponent("Monster_movetest") as MonoBehaviour;
        if (moveTest != null) moveTest.enabled = false;

        // NavMeshAgent 비활성화 (서버가 위치 결정)
        var nav = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null)
        {
            // BaseOffset은 프리팹에 10이 들어가 있을 수 있다. 서버가 이미 바닥 윗면
            // 좌표를 보내므로 0으로 내려 두지 않으면 재활성화 시 공중에 뜬다.
            nav.baseOffset = 0f;
            nav.enabled = false;
        }

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
            case MonsterKind.ELITE:
                if (ElitePrefab == null) go.transform.localScale = Vector3.one * 1.3f;
                if (go.GetComponent<EliteMonsterOutline>() == null) go.AddComponent<EliteMonsterOutline>();
                break;
            case MonsterKind.BOSS:
                if (BossPrefab == null) go.transform.localScale = Vector3.one * 2.0f;
                break;
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
