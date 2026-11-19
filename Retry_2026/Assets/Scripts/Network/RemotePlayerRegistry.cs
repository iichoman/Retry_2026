using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// ============================================================================
//  RemotePlayerRegistry
//
//  다른 플레이어들 (본인 제외)을 서버에서 받은 데이터로 표시.
//   - PLAYER_ENTER_VIEW → RemotePlayerPrefab instantiate
//   - PLAYER_MOVE → 위치/회전/애니 갱신 (Lerp 보간)
//   - PLAYER_LEAVE_VIEW → destroy
//
//  중요: RemotePlayerPrefab엔 Player_Movement, Defalult_Input 등 자체 동작
//        컴포넌트가 있으면 비활성화. 시각 컴포넌트(Animator, Renderer)만 살림.
// ============================================================================
public class RemotePlayerRegistry : MonoBehaviour
{
    public GameObject GetActor(int id) => entries.TryGetValue(id, out var entry) ? entry.go : null;

    public GameObject RemotePlayerPrefab;

    private readonly Dictionary<int, RemotePlayerEntry> entries = new();
    private readonly Dictionary<int, PlayerPresentation> presentations = new();
    public void ClearSession()
    {
        foreach(var entry in entries.Values)if(entry.go!=null)Destroy(entry.go);
        entries.Clear();equipmentStates.Clear();presentations.Clear();PlayerNameplate.LocalTeam=-1;
    }
    public void OnPresentation(PlayerPresentation data)
    {
        presentations[data.entityId]=data;
        if(!entries.TryGetValue(data.entityId,out var e) || e.go==null)return;
        var label=e.go.GetComponent<PlayerNameplate>() ?? e.go.AddComponent<PlayerNameplate>();
        label.SetTeam(data.team);
        if(!e.dead)e.go.GetComponent<PlayerDefenseController>()?.ApplyRemoteDefense(data);
    }
    private readonly Dictionary<int, EquipmentSyncData> equipmentStates = new();

    private class RemotePlayerEntry
    {
        public GameObject go;
        public Animator animator;
        public Component playerState;
        public FieldInfo hpBackingField;
        public WeaponSwingTrail[] swingTrails;

        public Vector3 targetPos;
        public float targetYaw;
        public float speed;
        public int animState;
        public bool dead;
    }

    private void Update()
    {
        float lerpRate = 1f - Mathf.Exp(-15f * Time.deltaTime);
        foreach (var kv in entries)
        {
            var e = kv.Value;
            if (e.go == null || e.dead) continue;
            e.go.transform.position = Vector3.Lerp(e.go.transform.position, e.targetPos, lerpRate);
            float curYaw = e.go.transform.eulerAngles.y;
            float newYaw = Mathf.LerpAngle(curYaw, e.targetYaw, lerpRate);
            e.go.transform.eulerAngles = new Vector3(0f, newYaw, 0f);

            // 애니용 speed 파라미터 (Player_Animation이 "Mspeed" 같은 거 있다면)
            if (e.animator != null)
            {
                ApplyAnimSpeed(e.animator, e.speed);
            }
        }
    }

    public void OnEnterView(PlayerEnterView p)
    {
        if (entries.ContainsKey(p.clientId))
        {
            var existing = entries[p.clientId];
            existing.targetPos = new Vector3(p.posX, p.posY, p.posZ);
            existing.targetYaw = p.rotY;
            return;
        }
        if (RemotePlayerPrefab == null)
        {
            Debug.LogError("[RemotePlayers] RemotePlayerPrefab 미설정!");
            return;
        }

        var spawnPos = new Vector3(p.posX, p.posY, p.posZ);
        var spawnRot = Quaternion.Euler(0f, p.rotY, 0f);
        var go = Instantiate(RemotePlayerPrefab, spawnPos, spawnRot, transform);
        go.name = $"RemotePlayer_{p.clientId}({p.playerName})";

        DisableLocalControl(go);

        var entry = new RemotePlayerEntry
        {
            go = go,
            animator = go.GetComponentInChildren<Animator>(),
            swingTrails = go.GetComponentsInChildren<WeaponSwingTrail>(true),
            targetPos = spawnPos,
            targetYaw = p.rotY,
        };

        var ps = go.GetComponent("Player_State") as Component;
        if (ps != null)
        {
            entry.playerState = ps;
            entry.hpBackingField = ps.GetType().GetField(
                "<CurrentHp>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            entry.hpBackingField?.SetValue(ps, p.hp);
        }

        entries[p.clientId] = entry;
        var nameplate=go.AddComponent<PlayerNameplate>(); nameplate.SetName(p.playerName);
        if(presentations.TryGetValue(p.clientId,out var visual)) OnPresentation(visual);
        if(p.hp<=0) OnPlayerDied(p.clientId);
        if (equipmentStates.TryGetValue(p.clientId, out var equipment)) OnEquipmentSync(equipment);
        Debug.Log($"[RemotePlayers] ENTER cid={p.clientId} name={p.playerName} hp={p.hp}");
    }

    public void OnLeaveView(int clientId)
    {
        if (!entries.TryGetValue(clientId, out var e)) return;
        if (e.go != null) Destroy(e.go);
        entries.Remove(clientId);
        Debug.Log($"[RemotePlayers] LEAVE cid={clientId}");
    }

    public void OnMove(PlayerMove p)
    {
        if (!entries.TryGetValue(p.clientId, out var e)) return;
        if(e.dead) return;
        e.targetPos = new Vector3(p.posX, p.posY, p.posZ);
        e.targetYaw = p.rotY;
        e.speed = p.speed;
        e.animState = p.animState;
    }

    /// <summary>다른 플레이어의 공격 액션 알림. Animator에 attack trigger 발동.</summary>
    // Match the shared controller's 20%-53% swing window; remote actors have no local combo driver.
    private static float SwordPlaybackSeconds(int combo) => combo switch { 1 => .75f, 2 => .8f, 3 => .9f, _ => 1f };
    public void OnAttackBroadcast(PlayerAttackBroadcast pab)
    {
        if (!entries.TryGetValue(pab.attackerId, out var e)) return;
        if(e.dead) return;
        float swingSpeed=pab.weaponKind==(int)WeaponKind.BIG_SWORD&&e.animator!=null?Mathf.Clamp(e.animator.speed,.1f,2f):1f;
        if ((pab.weaponKind == (int)WeaponKind.SWORD || pab.weaponKind == (int)WeaponKind.BIG_SWORD) && e.swingTrails != null)
            foreach (var trail in e.swingTrails)
                if (trail != null && trail.gameObject.activeInHierarchy) trail.PlayServerSwing(pab.weaponKind == (int)WeaponKind.BIG_SWORD ? GreatswordWeapon.SwingDurationFor(pab.comboIndex)/swingSpeed : SwordPlaybackSeconds(pab.comboIndex)*.33f,
                    pab.weaponKind == (int)WeaponKind.BIG_SWORD ? GreatswordWeapon.SwingStartFor(pab.comboIndex)/swingSpeed : SwordPlaybackSeconds(pab.comboIndex)*.20f);
        if (e.animator == null) return;

        // 무기 종류와 콤보 인덱스로 attack state 결정.
        // 클라 Animator는 sword_attack_1~4 → Blend Tree 구조.
        // 일단 콤보 인덱스로 sword_attack_{combo} state로 CrossFade 시도.
        int combo = Mathf.Clamp(pab.comboIndex,1,pab.weaponKind==(int)WeaponKind.BIG_SWORD?3:4);
        string prefix = pab.weaponKind == (int)WeaponKind.BIG_SWORD ? "greatsword" : pab.weaponKind == (int)WeaponKind.GUN ? "gun" : "sword";
        string stateName = $"Base Layer.test.{prefix}_attack_{combo}";
        int stateHash = Animator.StringToHash(stateName);
        if(!e.animator.HasState(0,stateHash))
            stateHash=Animator.StringToHash($"{prefix}_attack_{combo}");
        if (e.animator.HasState(0, stateHash))
        {
            e.animator.CrossFadeInFixedTime(stateHash, 0.05f, 0, 0f);
        }
        else
        {
            // 폴백: "Attack" trigger 시도
            var ps = e.animator.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].name == "Attack" && ps[i].type == AnimatorControllerParameterType.Trigger)
                {
                    e.animator.SetTrigger("Attack");
                    return;
                }
            }
        }
    }

    /// <summary>다른 플레이어의 HP 갱신 (COMBAT_EVENT 받았을 때).</summary>
    public void OnHpChanged(int clientId, int hp, bool hitReaction = true)
    {
        if (!entries.TryGetValue(clientId, out var e)) return;
        if(e.dead) return;
        bool damaged=hitReaction && e.playerState is Player_State state && hp<state.CurrentHp;
        if (e.hpBackingField != null && e.playerState != null)
        {
            e.hpBackingField.SetValue(e.playerState, hp);
        }
        if(damaged)e.go.GetComponent<PlayerDefenseController>()?.CancelDefense();
        if(hp<=0) OnPlayerDied(clientId);
        else if(damaged && e.animator!=null && e.animator.HasState(0,Animator.StringToHash("Hit")))
            e.animator.CrossFadeInFixedTime("Hit",.04f,0,0);
    }

    /// <summary>서버 사망 통보를 한 번만 재생하고 이후 이동/공격 표시를 중단한다.</summary>
    public void OnPlayerDied(int clientId)
    {
        if (!entries.TryGetValue(clientId, out var e)) return;
        if(e.dead) return;
        e.dead=true; e.speed=0;
        e.go?.GetComponent<PlayerDefenseController>()?.CancelDefense();
        (e.playerState as Player_State)?.NetworkDie();
        e.go?.GetComponent<Player_Attack>()?.CancelCurrentAttack();
        if(e.animator!=null && e.animator.HasState(0,Animator.StringToHash("Die")))
        {
            e.animator.CrossFadeInFixedTime("Die",.08f,0,0);
            return;
        }
        // 일단 단순 destroy. LEAVE_VIEW가 도착하면 그쪽이 처리해도 OK이지만
        // 즉시 시각적 피드백을 위해 여기서도 처리.
        if (e.go != null)
        {
            // 사망 애니가 있으면 발동, 잠시 후 destroy
            if (e.animator != null)
            {
                var ps = e.animator.parameters;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].name == "Die" && ps[i].type == AnimatorControllerParameterType.Trigger)
                    {
                        e.animator.SetTrigger("Die");
                        return;
                    }
                }
            }
            // Die 애니 없으면 그냥 둠 (LEAVE_VIEW가 곧 올 것)
        }
    }

    public void OnEquipmentSync(EquipmentSyncData equipment)
    {
        equipmentStates[equipment.clientId] = equipment;
        if (!entries.TryGetValue(equipment.clientId, out var e) || e.go == null) return;
        var attack = e.go.GetComponent<Player_Attack>();
        if (attack != null)
            attack.EquipWeapon(equipment.weaponKind == (int)WeaponKind.BIG_SWORD ? GreatswordWeapon.Id : equipment.weaponKind == (int)WeaponKind.GUN ? "test_gun" : "test_sword");
    }

    private void DisableLocalControl(GameObject go)
    {
        // Remote 플레이어는 자기가 입력/이동 처리 안 함
        var defense=go.GetComponent<PlayerDefenseController>() ?? go.AddComponent<PlayerDefenseController>();
        defense.SetRemotePresentation();
        (go.GetComponent<PlayerDefenseAnimation>() ?? go.AddComponent<PlayerDefenseAnimation>()).enabled=true;
        (go.GetComponent<PlayerDefensePose>() ?? go.AddComponent<PlayerDefensePose>()).enabled=true;
        DisableComponentByName(go, "Player_Movement");
        DisableComponentByName(go, "Player_Animation");
        DisableComponentByName(go, "Defalult_Input");
        DisableComponentByName(go, "Player_Attack");
        DisableComponentByName(go, "Player_ClassicAttack");
        DisableComponentByName(go, "Player_Camera");
        DisableComponentByName(go, "Player_Camera_Action");
        DisableComponentByName(go, "Player_Camera_Controller");
        DisableComponentByName(go, "Player_LockOnSystem");
        DisableComponentByName(go, "PlayerEquipment");
        DisableComponentByName(go, "PlayerPickupInteractor");

        // CharacterController는 disable (서버가 위치 보내줌)
        var cc = go.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // PlayerInput (Unity Input System) 끄기
        var pi = go.GetComponent("PlayerInput") as MonoBehaviour;
        if (pi != null) pi.enabled = false;
    }

    private static void DisableComponentByName(GameObject go, string typeName)
    {
        var c = go.GetComponent(typeName) as MonoBehaviour;
        if (c != null) c.enabled = false;
    }

    private static void ApplyAnimSpeed(Animator anim, float speed)
    {
        // Animator의 이동 속도 파라미터를 자동 탐색.
        // 클라 측 Player_AnimCtrl의 실제 파라미터 이름은 "speed" (소문자).
        var ps = anim.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].type != AnimatorControllerParameterType.Float) continue;
            if (ps[i].name == "speed"
                || ps[i].name == "Pspeed"
                || ps[i].name == "Speed"
                || ps[i].name == "MoveSpeed")
            {
                anim.SetFloat(ps[i].nameHash, speed);
                return;
            }
        }
    }
}
