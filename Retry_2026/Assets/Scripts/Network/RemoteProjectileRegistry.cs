using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  RemoteProjectileRegistry
//
//  서버가 보내는 원거리 투사체(활/총의 직육면체)를 월드에 표시한다.
//   - OnSpawn   : 직육면체 생성 (크기는 projectileSize로 고정)
//   - OnMove    : 위치 갱신 (틱 사이 보간)
//   - OnDespawn : 제거. 본인이 쏜 것이면 OnLocalProjectileGone 이벤트 발생
//                 (LocalPlayerAttackSender가 구독해 미쿠 손 Cube를 복귀시킴)
//
//  서버가 모든 충돌/소멸/비행을 판정하므로 클라는 표시만 담당한다.
// ============================================================================
public class RemoteProjectileRegistry : MonoBehaviour
{
    [Tooltip("투사체로 쓸 prefab. 비우면 Cube를 자동 생성.")]
    public GameObject ProjectilePrefab;

    [Tooltip("투사체 한 변의 크기(m). 손 Cube보다 작게 보이는 문제 방지용 고정 크기.")]
    [SerializeField] private Vector3 projectileScale = new Vector3(0.3f, 0.3f, 1.5f);

    // 본인 발사체 판정용. NetworkBootstrap이 주입.
    public LocalIdentity LocalIdentity;

    // 본인이 쏜 투사체가 사라질 때 호출 (손 Cube 복귀 등). LocalPlayerAttackSender가 구독.
    public System.Action OnLocalProjectileGone;

    private readonly Dictionary<int, GameObject> active = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, Vector3> targetPos = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, int> ownerOf = new Dictionary<int, int>();

    public void OnSpawn(ProjectileSpawn sp)
    {
        if (active.ContainsKey(sp.projectileId)) return;

        GameObject go;
        if (ProjectilePrefab != null)
        {
            go = Instantiate(ProjectilePrefab);
            go.transform.SetParent(null, true);    // 손에 붙지 않도록 월드 루트로
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // 기획에 맞는 직육면체 스케일(Vector3) 적용
        go.transform.localScale = projectileScale;

        go.name = $"Projectile_{sp.projectileId}";
        Vector3 pos = new Vector3(sp.posX, sp.posY, sp.posZ);
        go.transform.position = pos;

        // 진행 방향으로 회전
        Vector3 dir = new Vector3(sp.dirX, sp.dirY, sp.dirZ);
        if (dir.sqrMagnitude > 0.0001f)
            go.transform.rotation = Quaternion.LookRotation(dir.normalized);

        active[sp.projectileId] = go;
        targetPos[sp.projectileId] = pos;
        ownerOf[sp.projectileId] = sp.ownerId;
    }

    public void OnMove(ProjectileMove mv)
    {
        if (active.ContainsKey(mv.projectileId))
            targetPos[mv.projectileId] = new Vector3(mv.posX, mv.posY, mv.posZ);
    }

    public void OnDespawn(ProjectileDespawn dp)
    {
        if (active.TryGetValue(dp.projectileId, out var go) && go != null)
        {
            go.transform.position = new Vector3(dp.posX, dp.posY, dp.posZ);
            // TODO: hitType별 이펙트 (0=벽,1=몬스터,2=플레이어,3=수명)
            Destroy(go);
        }

        // 본인이 쏜 투사체면 이벤트 발생 (손 Cube 복귀)
        if (LocalIdentity != null &&
            ownerOf.TryGetValue(dp.projectileId, out int owner) &&
            owner == LocalIdentity.LocalClientId)
        {
            OnLocalProjectileGone?.Invoke();
        }

        active.Remove(dp.projectileId);
        targetPos.Remove(dp.projectileId);
        ownerOf.Remove(dp.projectileId);
    }

    private void Update()
    {
        // 서버 틱(50ms) 사이를 보간. 속도가 느리므로 부드럽게 따라감.
        foreach (var kv in active)
        {
            if (kv.Value == null) continue;
            if (targetPos.TryGetValue(kv.Key, out var tp))
                kv.Value.transform.position = Vector3.Lerp(
                    kv.Value.transform.position, tp, Time.deltaTime * 12f);
        }
    }
}