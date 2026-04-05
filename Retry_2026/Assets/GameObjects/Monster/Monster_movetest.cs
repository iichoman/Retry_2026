using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class Monster_movetest : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Monster monster;
    [SerializeField] private Monster_State monsterState;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private DungeonNavMeshBuilder navMeshBuilder;

    [Header("Wander")]
    [SerializeField, Min(0.5f)] private float wanderRadius = 12f;
    [SerializeField, Min(0f)] private float idleDelay = 1.5f;
    [SerializeField, Min(1)] private int maxSampleAttempts = 8;
    [SerializeField, Min(0.5f)] private float spawnToNavMeshRadius = 32f;

    private float idleTimer;
    private bool isNavigationReady;

    private void Awake()
    {
        if (monster == null)
        {
            monster = GetComponent<Monster>();
        }

        if (monsterState == null)
        {
            monsterState = GetComponent<Monster_State>();
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (navMeshBuilder == null)
        {
            navMeshBuilder = FindFirstObjectByType<DungeonNavMeshBuilder>();
        }

        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    private void OnEnable()
    {
        idleTimer = idleDelay;

        if (navMeshBuilder != null)
        {
            navMeshBuilder.NavMeshBuilt += HandleNavMeshBuilt;
        }
    }

    private void OnDisable()
    {
        if (navMeshBuilder != null)
        {
            navMeshBuilder.NavMeshBuilt -= HandleNavMeshBuilt;
        }
    }

    private void Update()
    {
        if (agent == null || !isNavigationReady || !agent.enabled)
        {
            return;
        }

        if (monsterState != null && monsterState.IsDead)
        {
            if (agent.enabled)
            {
                agent.ResetPath();
            }

            return;
        }

        if (!TryEnsureAgentOnNavMesh())
        {
            return;
        }

        if (monster != null && monster.HasTarget)
        {
            agent.ResetPath();
            return;
        }

        if (agent.pathPending)
        {
            return;
        }

        if (agent.remainingDistance > agent.stoppingDistance)
        {
            return;
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer > 0f)
        {
            return;
        }

        if (TryGetRandomNavMeshPoint(out Vector3 destination))
        {
            agent.SetDestination(destination);
        }

        idleTimer = idleDelay;
    }

    private bool TryEnsureAgentOnNavMesh()
    {
        if (!agent.enabled)
        {
            return false;
        }

        if (agent.isOnNavMesh)
        {
            return true;
        }

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, spawnToNavMeshRadius, NavMesh.AllAreas))
        {
            return false;
        }

        return agent.Warp(hit.position);
    }

    private bool TryGetRandomNavMeshPoint(out Vector3 destination)
    {
        for (int i = 0; i < maxSampleAttempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                destination = hit.position;
                return true;
            }
        }

        destination = transform.position;
        return false;
    }

    private void HandleNavMeshBuilt(DungeonNavMeshBuilder builder)
    {
        TryActivateNavigation();
    }

    private void TryActivateNavigation()
    {
        if (agent == null)
        {
            return;
        }

        if (!agent.enabled)
        {
            agent.enabled = true;
        }

        isNavigationReady = TryEnsureAgentOnNavMesh();

        if (!isNavigationReady && agent.enabled)
        {
            agent.enabled = false;
        }
    }
}
