using UnityEngine;

[DisallowMultipleComponent]
public class Player_LockOnSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Defalult_Input input;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform lockOnOrigin;

    [Header("Lock-On")]
    [SerializeField, Min(0.1f)] private float maxLockOnDistance = 15f;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private Vector2 viewportCenter = new Vector2(0.5f, 0.5f);
    [SerializeField, Min(0f)] private float centerScoreWeight = 8f;
    [SerializeField, Min(0f)] private float distanceScoreWeight = 1f;

    [Header("Line Of Sight")]
    [SerializeField] private bool requireLineOfSight = false;
    [SerializeField] private LayerMask lineOfSightBlockers = ~0;
    [SerializeField, Min(0f)] private float lineOfSightOriginHeight = 1.5f;

    [Header("Debug")]
    [SerializeField] private Transform currentTarget;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logDebug = false;

    [SerializeField, Min(0f)] private float occlusionGrace = .6f;
    [SerializeField, Min(1f)] private float releaseDistanceMultiplier = 1.2f;
    private float occludedTime;
    private Player_State ownerState;
    private Collider aimCollider;
    private readonly RaycastHit[] sightHits = new RaycastHit[64];
    private readonly Collider[] targetResults = new Collider[128];
    public Vector3 TargetAimPosition => GetAimPosition(currentTarget, aimCollider);

    private static Vector3 GetAimPosition(Transform target, Collider body)
    {
        if (target == null) return Vector3.zero;
        if (body != null && body.enabled && body.bounds.size.y > .1f)
            return body.bounds.center + Vector3.up * (body.bounds.extents.y * .25f);
        return target.position + Vector3.up * 1.2f;
    }
    private bool lockOnActive;

    public Transform CurrentTarget => currentTarget;
    public bool IsLockedOn => lockOnActive && currentTarget != null;

    private void Awake()
    {
        ownerState = GetComponent<Player_State>();
        if (input == null)
        {
            input = GetComponent<Defalult_Input>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (lockOnOrigin == null)
        {
            lockOnOrigin = transform;
        }
    }

    private void OnEnable()
    {
        if (input != null)
        {
            input.HandleLockon += ToggleLockOn;
        }
    }

    private void OnDisable()
    {
        if (input != null)
        {
            input.HandleLockon -= ToggleLockOn;
        }
        ClearLockOn();
    }

    private void Update()
    {
        if (!lockOnActive)
        {
            return;
        }

        if ((ownerState != null && ownerState.IsDead) || !IsTargetStillLockable(currentTarget))
        {
            ClearLockOn();
            return;
        }
        if (requireLineOfSight && !HasLineOfSight(currentTarget)) occludedTime += Time.deltaTime;
        else occludedTime = 0;
        if (occludedTime > occlusionGrace) ClearLockOn();
    }

    public void ToggleLockOn()
    {
        if (input != null && !input.IsGameplayInputEnabled) return;
        if (lockOnActive)
        {
            ClearLockOn();
            return;
        }

        TryLockOn();
    }

    public bool TryLockOn()
    {
        if ((ownerState != null && ownerState.IsDead) || (input != null && !input.IsGameplayInputEnabled)) return false;
        Transform bestTarget = FindBestTarget();
        currentTarget = bestTarget;
        aimCollider = currentTarget != null ? currentTarget.GetComponentInChildren<Collider>() : null;
        occludedTime = 0;
        lockOnActive = currentTarget != null;

        if (logDebug)
        {
            Debug.Log(
                currentTarget != null
                    ? $"LockOn target acquired: {currentTarget.name}"
                    : "LockOn failed: no valid target found.",
                this
            );
        }

        return currentTarget != null;
    }

    public void ClearLockOn()
    {
        if (logDebug && currentTarget != null)
        {
            Debug.Log($"LockOn cleared: {currentTarget.name}", this);
        }

        currentTarget = null;
        aimCollider = null;
        occludedTime = 0;
        lockOnActive = false;
    }

    private Transform FindBestTarget()
    {
        if (targetCamera == null || lockOnOrigin == null)
        {
            return null;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            lockOnOrigin.position,
            maxLockOnDistance,
            targetResults,
            targetLayer,
            QueryTriggerInteraction.Ignore
        );

        if (logDebug)
        {
            Debug.Log($"LockOn scan: layerMask={targetLayer.value}, hitCount={hitCount}", this);
        }

        Transform bestTarget = null;
        float bestScore = float.MaxValue;

        void Consider(Transform candidate)
        {
            if (!IsTargetVisibleAndLockable(candidate)) return;
            Vector3 point = GetAimPosition(candidate, candidate.GetComponentInChildren<Collider>());
            Vector3 viewport = targetCamera.WorldToViewportPoint(point);
            float score = Vector2.Distance(new Vector2(viewport.x, viewport.y), viewportCenter) * centerScoreWeight
                + Vector3.Distance(lockOnOrigin.position, candidate.position) * distanceScoreWeight;
            if (score < bestScore) { bestScore = score; bestTarget = candidate; }
        }
        for (int i = 0; i < hitCount; i++)
        {
            var hit = targetResults[i]; targetResults[i] = null;
            if (hit != null) Consider(ResolveTarget(hit.transform));
        }
        // Remote CharacterControllers are disabled; enumerate actors only on user acquisition,
        // not every frame, so network players remain selectable without adding physics authority.
        foreach (var player in FindObjectsByType<Player_State>(FindObjectsSortMode.None))
            if (player.GetComponentInParent<RemotePlayerRegistry>() != null) Consider(player.transform);

        return bestTarget;
    }

    private bool IsTargetStillLockable(Transform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy || target == transform || target.IsChildOf(transform) || targetCamera == null || lockOnOrigin == null)
        {
            return false;
        }

        if (!IsTargetAlive(target))
        {
            return false;
        }

        float distance = Vector3.Distance(lockOnOrigin.position, target.position);
        if (distance > maxLockOnDistance * releaseDistanceMultiplier)
        {
            return false;
        }

        return true;
    }

    private bool IsTargetVisibleAndLockable(Transform target)
    {
        if (!IsTargetStillLockable(target))
        {
            return false;
        }

        if (Vector3.Distance(lockOnOrigin.position, target.position) > maxLockOnDistance || (requireLineOfSight && !HasLineOfSight(target))) return false;
        Vector3 viewportPosition = targetCamera.WorldToViewportPoint(GetAimPosition(target, target.GetComponentInChildren<Collider>()));
        bool isVisible =
            viewportPosition.z > 0f &&
            viewportPosition.x >= 0f &&
            viewportPosition.x <= 1f &&
            viewportPosition.y >= 0f &&
            viewportPosition.y <= 1f;

        if (!isVisible)
        {
            return false;
        }

        return true;
    }

    private bool HasLineOfSight(Transform target)
    {
        Vector3 origin = lockOnOrigin.position + Vector3.up * lineOfSightOriginHeight;
        Vector3 targetPosition = GetAimPosition(target, target == currentTarget ? aimCollider : target.GetComponentInChildren<Collider>());
        Vector3 direction = targetPosition - origin;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        int count = Physics.RaycastNonAlloc(origin, direction / distance, sightHits, distance, lineOfSightBlockers, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].transform;
            if (hit == null || hit == transform || hit.IsChildOf(transform) || hit == target || hit.IsChildOf(target)) continue;
            return false;
        }
        return true;
    }

    private bool IsTargetAlive(Transform target)
    {
        Player_State player = target.GetComponentInParent<Player_State>();
        if (player != null) return !player.IsDead;
        Monster_State state = target.GetComponent<Monster_State>();
        if (state == null)
        {
            state = target.GetComponentInParent<Monster_State>();
        }

        return state == null || !state.IsDead;
    }

    private Transform ResolveTarget(Transform candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        Player_State player = candidate.GetComponentInParent<Player_State>();
        if (player != null) return player.transform;
        Monster_State state = candidate.GetComponentInParent<Monster_State>();
        if (state != null) return state.transform;
        Monster monster = candidate.GetComponent<Monster>();
        if (monster == null)
        {
            monster = candidate.GetComponentInParent<Monster>();
        }

        if (monster != null)
        {
            return monster.transform;
        }

        return null; // Scenery is never a combat target.
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Transform origin = lockOnOrigin != null ? lockOnOrigin : transform;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin.position, maxLockOnDistance);

        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin.position, currentTarget.position);
            Gizmos.DrawWireSphere(currentTarget.position, 0.35f);
        }
    }
}
