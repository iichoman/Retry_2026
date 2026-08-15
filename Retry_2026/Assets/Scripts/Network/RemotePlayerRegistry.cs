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
    public GameObject RemotePlayerPrefab;

    private readonly Dictionary<int, RemotePlayerEntry> entries = new();

    private class RemotePlayerEntry
    {
        public GameObject go;
        public Animator animator;
        public Component playerState;
        public FieldInfo hpBackingField;

        public Vector3 targetPos;
        public float targetYaw;
        public float speed;
        public int animState;
    }

    private void Update()
    {
        float lerpRate = 1f - Mathf.Exp(-15f * Time.deltaTime);
        foreach (var kv in entries)
        {
            var e = kv.Value;
            if (e.go == null) continue;
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
        e.targetPos = new Vector3(p.posX, p.posY, p.posZ);
        e.targetYaw = p.rotY;
        e.speed = p.speed;
        e.animState = p.animState;
    }

    /// <summary>다른 플레이어의 공격 액션 알림. Animator에 attack trigger 발동.</summary>
    public void OnAttackBroadcast(PlayerAttackBroadcast pab)
    {
        if (!entries.TryGetValue(pab.attackerId, out var e)) return;
        if (e.animator == null) return;

        // 무기 종류와 콤보 인덱스로 attack state 결정.
        // 클라 Animator는 sword_attack_1~4 → Blend Tree 구조.
        // 일단 콤보 인덱스로 sword_attack_{combo} state로 CrossFade 시도.
        int combo = Mathf.Clamp(pab.comboIndex, 1, 4);
        string stateName = $"sword_attack_{combo}";
        int stateHash = Animator.StringToHash(stateName);
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
    public void OnHpChanged(int clientId, int hp)
    {
        if (!entries.TryGetValue(clientId, out var e)) return;
        if (e.hpBackingField != null && e.playerState != null)
        {
            e.hpBackingField.SetValue(e.playerState, hp);
        }
    }

    /// <summary>다른 플레이어 사망. 추후 사망 애니/이펙트.</summary>
    public void OnPlayerDied(int clientId)
    {
        if (!entries.TryGetValue(clientId, out var e)) return;
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

    private void DisableLocalControl(GameObject go)
    {
        // Remote 플레이어는 자기가 입력/이동 처리 안 함
        DisableComponentByName(go, "Player_Movement");
        DisableComponentByName(go, "Defalult_Input");
        DisableComponentByName(go, "Player_Attack");
        DisableComponentByName(go, "Player_ClassicAttack");
        DisableComponentByName(go, "Player_Camera");
        DisableComponentByName(go, "Player_Camera_Action");
        DisableComponentByName(go, "Player_Camera_Controller");
        DisableComponentByName(go, "Player_LockOnSystem");

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