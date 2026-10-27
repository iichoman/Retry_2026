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
    [SerializeField] private bool logDebug = true;

    private readonly Collider[] targetResults = new Collider[32];
    private bool lockOnActive;

    public Transform CurrentTarget => currentTarget;
    public bool IsLockedOn => lockOnActive && currentTarget != null;

    private void Awake()
    {
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
    }

    private void Update()
    {
        if (!lockOnActive)
        {
            return;
        }

        if (!IsTargetStillLockable(currentTarget))
        {
            ClearLockOn();
        }
    }

    public void ToggleLockOn()
    {
        if (lockOnActive)
        {
            ClearLockOn();
            return;
        }

        TryLockOn();
    }

    public bool TryLockOn()
    {
        Transform bestTarget = FindBestTarget();
        currentTarget = bestTarget;
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

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = targetResults[i];
            targetResults[i] = null;

            if (hit == null)
            {
                continue;
            }

            Transform candidate = ResolveTarget(hit.transform);
            if (!IsTargetVisibleAndLockable(candidate))
            {
                continue;
            }

            Vector3 viewportPosition = targetCamera.WorldToViewportPoint(candidate.position);
            float centerDistance = Vector2.Distance(
                new Vector2(viewportPosition.x, viewportPosition.y),
                viewportCenter
            );
            float worldDistance = Vector3.Distance(lockOnOrigin.position, candidate.position);
            float score = centerDistance * centerScoreWeight + worldDistance * distanceScoreWeight;

            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestTarget = candidate;
        }

        return bestTarget;
    }

    private bool IsTargetStillLockable(Transform target)
    {
        if (target == null || targetCamera == null || lockOnOrigin == null)
        {
            return false;
        }

        if (!IsTargetAlive(target))
        {
            return false;
        }

        float distance = Vector3.Distance(lockOnOrigin.position, target.position);
        if (distance > maxLockOnDistance)
        {
            return false;
        }

        return !requireLineOfSight || HasLineOfSight(target);
    }

    private bool IsTargetVisibleAndLockable(Transform target)
    {
        if (!IsTargetStillLockable(target))
        {
            return false;
        }

        Vector3 viewportPosition = targetCamera.WorldToViewportPoint(target.position);
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
        Vector3 targetPosition = target.position;
        Vector3 direction = targetPosition - origin;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        if (!Physics.Raycast(origin, direction / distance, out RaycastHit hit, distance, lineOfSightBlockers, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.transform == target || hit.transform.IsChildOf(target);
    }

    private bool IsTargetAlive(Transform target)
    {
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

        Monster monster = candidate.GetComponent<Monster>();
        if (monster == null)
        {
            monster = candidate.GetComponentInParent<Monster>();
        }

        if (monster != null)
        {
            return monster.transform;
        }

        return candidate;
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
