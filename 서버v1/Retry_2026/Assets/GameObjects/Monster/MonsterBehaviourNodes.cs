using UnityEngine;
using UnityEngine.AI;

public class MonsterHasTargetNode : BTNode
{
    private readonly MonsterContext context;

    public MonsterHasTargetNode(MonsterContext context)
    {
        this.context = context;
    }

    public override BTState Tick()
    {
        return context.Monster != null && context.Monster.HasTarget
            ? BTState.Success
            : BTState.Failure;
    }
}

public class MonsterInAttackRangeNode : BTNode
{
    private readonly MonsterContext context;

    public MonsterInAttackRangeNode(MonsterContext context)
    {
        this.context = context;
    }

    public override BTState Tick()
    {
        if (context.Monster == null || !context.Monster.HasTarget)
        {
            return BTState.Failure;
        }

        float distance = Vector3.Distance(
            context.Monster.transform.position,
            context.Monster.Target.position
        );

        return distance <= context.Monster.AttackRange
            ? BTState.Success
            : BTState.Failure;
    }
}

public class MonsterAttackNode : BTNode
{
    private readonly MonsterContext context;

    public MonsterAttackNode(MonsterContext context)
    {
        this.context = context;
    }

    public override BTState Tick()
    {
        if (context.Attack == null || (context.State != null && context.State.IsHit))
        {
            StopAgent();
            return BTState.Failure;
        }

        if (context.Attack.IsAttacking)
        {
            StopAgent();
            return BTState.Running;
        }

        if (!context.Attack.RequestAttack())
        {
            return BTState.Failure;
        }

        StopAgent();
        return BTState.Running;
    }

    private void StopAgent()
    {
        if (context.Agent == null || !context.Agent.enabled || !context.Agent.isOnNavMesh)
        {
            return;
        }

        context.Agent.isStopped = true;
        context.Agent.ResetPath();
    }
}

public class MonsterChaseTargetNode : BTNode
{
    private readonly MonsterContext context;

    public MonsterChaseTargetNode(MonsterContext context)
    {
        this.context = context;
    }

    public override BTState Tick()
    {
        if (context.Monster == null || !context.Monster.HasTarget || context.Agent == null)
        {
            return BTState.Failure;
        }

        if ((context.State != null && context.State.IsHit) || (context.Attack != null && context.Attack.IsAttacking))
        {
            if (context.Agent.enabled && context.Agent.isOnNavMesh)
            {
                context.Agent.isStopped = true;
                context.Agent.ResetPath();
            }

            return BTState.Running;
        }

        if (!context.Agent.enabled || !context.Agent.isOnNavMesh)
        {
            return BTState.Failure;
        }

        context.Agent.isStopped = false;
        context.Agent.SetDestination(context.Monster.Target.position);
        return BTState.Running;
    }
}

public class MonsterIdleNode : BTNode
{
    private readonly MonsterContext context;

    public MonsterIdleNode(MonsterContext context)
    {
        this.context = context;
    }

    public override BTState Tick()
    {
        if (context.Agent != null && context.Agent.enabled && context.Agent.isOnNavMesh)
        {
            context.Agent.isStopped = true;
            context.Agent.ResetPath();
        }

        return BTState.Running;
    }
}

public class MonsterWanderNode : BTNode
{
    private readonly MonsterContext context;
    private float idleTimer;

    public MonsterWanderNode(MonsterContext context)
    {
        this.context = context;
        idleTimer = context.Monster != null ? context.Monster.WanderIdleDelay : 0f;
    }

    public override BTState Tick()
    {
        if (context.Monster == null || context.Agent == null)
        {
            return BTState.Failure;
        }

        if (context.State != null && context.State.IsHit)
        {
            StopAgent();
            return BTState.Running;
        }

        if (context.Monster.HasTarget)
        {
            return BTState.Failure;
        }

        if (context.Attack != null && context.Attack.IsAttacking)
        {
            StopAgent();
            return BTState.Running;
        }

        if (!context.Agent.enabled || !context.Agent.isOnNavMesh)
        {
            return BTState.Failure;
        }

        context.Agent.isStopped = false;

        if (context.Agent.pathPending)
        {
            return BTState.Running;
        }

        if (context.Agent.hasPath && context.Agent.remainingDistance > context.Agent.stoppingDistance)
        {
            return BTState.Running;
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer > 0f)
        {
            return BTState.Running;
        }

        if (!TryGetRandomNavMeshPoint(out Vector3 destination))
        {
            idleTimer = context.Monster.WanderIdleDelay;
            return BTState.Failure;
        }

        context.Agent.SetDestination(destination);
        idleTimer = context.Monster.WanderIdleDelay;
        return BTState.Running;
    }

    private bool TryGetRandomNavMeshPoint(out Vector3 destination)
    {
        float radius = context.Monster.WanderRadius;
        int attempts = context.Monster.MaxWanderSampleAttempts;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * radius;
            Vector3 candidate = context.Monster.transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                destination = hit.position;
                return true;
            }
        }

        destination = context.Monster.transform.position;
        return false;
    }

    private void StopAgent()
    {
        if (context.Agent == null || !context.Agent.enabled || !context.Agent.isOnNavMesh)
        {
            return;
        }

        context.Agent.isStopped = true;
        context.Agent.ResetPath();
    }
}
