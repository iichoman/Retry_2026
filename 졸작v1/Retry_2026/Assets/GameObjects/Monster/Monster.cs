using UnityEngine;

public enum MonsterMovementType
{
    Ground,
    Flying
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Monster_State))]
public class Monster : MonoBehaviour
{
    [Header("Monster Base")]
    [SerializeField] private string monsterId = "Monster";
    [SerializeField] private MonsterMovementType movementType = MonsterMovementType.Ground;

    [Header("Combat Base")]
    [SerializeField, Min(0f)] protected float detectRange = 8f;
    [SerializeField, Min(0f)] protected float attackRange = 1.5f;
    [SerializeField, Min(0f)] protected float moveSpeed = 2f;

    [Header("Wander")]
    [SerializeField, Min(0.5f)] private float wanderRadius = 8f;
    [SerializeField, Min(0f)] private float wanderIdleDelay = 1.5f;
    [SerializeField, Min(1)] private int maxWanderSampleAttempts = 8;

    [Header("Target Detection")]
    [SerializeField] private LayerMask playerLayerMask = 0;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool showAlertOnDetect = true;
    [SerializeField] private MonsterAlertEffect alertEffect;

    [Header("Body Collider")]
    [SerializeField] private bool ensureBodyCollider = true;
    [SerializeField] private Vector3 bodyColliderCenter = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0.01f)] private float bodyColliderRadius = 0.5f;
    [SerializeField, Min(0.01f)] private float bodyColliderHeight = 2f;
    [SerializeField] private bool bodyColliderIsTrigger = false;

    protected Monster_State state;
    protected Transform target;

    private readonly Collider[] detectResults = new Collider[16];

    public string MonsterId => monsterId;
    public MonsterMovementType MovementType => movementType;
    public Monster_State State => state;
    public bool HasTarget => target != null;
    public Transform Target => target;
    public float DetectRange => detectRange;
    public float AttackRange => attackRange;
    public float MoveSpeed => moveSpeed;
    public float WanderRadius => wanderRadius;
    public float WanderIdleDelay => wanderIdleDelay;
    public int MaxWanderSampleAttempts => maxWanderSampleAttempts;

    protected virtual void Awake()
    {
        state = GetComponent<Monster_State>();
        EnsureBodyCollider();
        ResolveAlertEffect();
    }

    private void EnsureBodyCollider()
    {
        if (!ensureBodyCollider || GetComponent<Collider>() != null)
        {
            return;
        }

        CapsuleCollider bodyCollider = gameObject.AddComponent<CapsuleCollider>();
        bodyCollider.center = bodyColliderCenter;
        bodyCollider.radius = bodyColliderRadius;
        bodyCollider.height = bodyColliderHeight;
        bodyCollider.isTrigger = bodyColliderIsTrigger;
    }

    protected virtual void Update()
    {
        if (state == null || state.IsDead || state.IsHit)
        {
            if (state != null && state.IsDead)
            {
                alertEffect?.Hide();
            }

            return;
        }

        UpdateTargetDetection();
        Tick();
    }

    public virtual void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    protected virtual void Tick()
    {
    }

    protected virtual void UpdateTargetDetection()
    {
        Transform detectedTarget = FindClosestTargetInLayer();

        if (detectedTarget == null)
        {
            detectedTarget = FindClosestTargetByTag();
        }

        Transform previousTarget = target;
        SetTarget(detectedTarget);

        if (showAlertOnDetect && previousTarget == null && target != null)
        {
            ResolveAlertEffect();
            alertEffect?.Show();
        }
    }

    private void ResolveAlertEffect()
    {
        if (!showAlertOnDetect)
        {
            return;
        }

        if (alertEffect == null)
        {
            alertEffect = GetComponent<MonsterAlertEffect>();
        }

        if (alertEffect == null)
        {
            alertEffect = gameObject.AddComponent<MonsterAlertEffect>();
        }
    }

    private Transform FindClosestTargetInLayer()
    {
        if (playerLayerMask.value == 0)
        {
            return null;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectRange,
            detectResults,
            playerLayerMask
        );

        Transform closestTarget = null;
        float closestDistanceSqr = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = detectResults[i];
            detectResults[i] = null;

            if (hit == null)
            {
                continue;
            }

            Transform candidate = ResolveTargetTransform(hit.transform);
            if (candidate == null)
            {
                continue;
            }

            float distanceSqr = (candidate.position - transform.position).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closestTarget = candidate;
            }
        }

        return closestTarget;
    }

    private Transform FindClosestTargetByTag()
    {
        if (string.IsNullOrWhiteSpace(playerTag))
        {
            return null;
        }

        GameObject[] taggedPlayers = GameObject.FindGameObjectsWithTag(playerTag);
        Transform closestTarget = null;
        float closestDistanceSqr = detectRange * detectRange;

        for (int i = 0; i < taggedPlayers.Length; i++)
        {
            GameObject taggedPlayer = taggedPlayers[i];
            if (taggedPlayer == null)
            {
                continue;
            }

            Transform candidate = ResolveTargetTransform(taggedPlayer.transform);
            if (candidate == null)
            {
                continue;
            }

            float distanceSqr = (candidate.position - transform.position).sqrMagnitude;
            if (distanceSqr > closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = distanceSqr;
            closestTarget = candidate;
        }

        return closestTarget;
    }

    private Transform ResolveTargetTransform(Transform candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        Player_State playerState = candidate.GetComponent<Player_State>();
        if (playerState == null)
        {
            playerState = candidate.GetComponentInParent<Player_State>();
        }

        if (playerState != null)
        {
            if (playerState.IsDead)
            {
                return null;
            }

            return playerState.transform;
        }

        return candidate;
    }
}
