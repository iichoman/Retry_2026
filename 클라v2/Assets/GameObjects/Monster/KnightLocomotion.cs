using UnityEngine;
using UnityEngine.AI;

// The source walk is in-place. Its planted-foot travel supplies the missing root motion.
// Offline: consume that motion through NavMesh. Online: match the gait to server travel.
[DefaultExecutionOrder(120)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class KnightLocomotion : MonoBehaviour
{
    [SerializeField] private AnimationCurve rootDistance;
    [SerializeField, Min(.01f)] private float cycleSeconds = 3.625f;
    private Animator animator;
    private Monster monster;
    private Monster_State health;
    private NavMeshAgent agent;
    private Transform actor;
    private Vector3 previousPosition;
    private float speed, previousCycle;
    private bool hasCycle, ownsNavigation, previousUpdatePosition;
    private static readonly int SpeedHash = Animator.StringToHash("Mspeed");
    private static readonly int RateHash = Animator.StringToHash("WalkRate");

    public float CycleDistance => rootDistance == null || rootDistance.length < 2 ? 0 : rootDistance.Evaluate(1);
    public float WorldCycleDistance => CycleDistance * Mathf.Abs(transform.lossyScale.z);
    public void Configure(AnimationCurve distance, float duration) { rootDistance = distance; cycleSeconds = duration; }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        monster = GetComponentInParent<Monster>();
        health = GetComponentInParent<Monster_State>();
        agent = GetComponentInParent<NavMeshAgent>();
        actor = monster != null ? monster.transform : transform.parent;
        if (actor != null) previousPosition = actor.position;
    }

    private void Update()
    {
        if (actor == null || WorldCycleDistance <= .01f) return;
        bool local = agent != null && agent.enabled && agent.isOnNavMesh;
        SetNavigationOwnership(local);
        var delta = Vector3.ProjectOnPlane(actor.position - previousPosition, Vector3.up);
        previousPosition = actor.position;
        float measured = local ? (agent.isStopped ? 0 : agent.desiredVelocity.magnitude)
            : delta.magnitude / Mathf.Max(Time.deltaTime, .0001f);
        // A view-entry correction or teleport is not a footstep.
        if (delta.sqrMagnitude > 4f) measured = 0;
        bool blocked = health != null && (health.IsDead || health.IsHit);
        speed = blocked ? 0 : Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-18f * Time.deltaTime));
        if (measured < .015f && speed < .03f) speed = 0;
        animator.SetFloat(SpeedHash, speed);
        animator.SetFloat(RateHash, speed > .015f ? Mathf.Clamp(speed * cycleSeconds / WorldCycleDistance, .05f, 12f) : 1f);
    }

    private void SetNavigationOwnership(bool value)
    {
        if (ownsNavigation == value) return;
        if (value) { previousUpdatePosition = agent.updatePosition; agent.updatePosition = false; }
        else if (agent != null) agent.updatePosition = previousUpdatePosition;
        ownsNavigation = value;
        hasCycle = false;
    }

    private void OnAnimatorMove()
    {
        // Never add animation displacement to a server-authoritative transform.
        if (!ownsNavigation || agent == null || !agent.enabled || !agent.isOnNavMesh) { hasCycle = false; return; }
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool walking = state.IsName("Locomotion") && !animator.IsInTransition(0)
            && !agent.isStopped && speed > .015f && health != null && !health.IsDead && !health.IsHit;
        if (!walking) { hasCycle = false; agent.nextPosition = actor.position; return; }
        float current = state.normalizedTime;
        float travel = hasCycle && current >= previousCycle && current - previousCycle < .5f
            ? (DistanceAt(current) - DistanceAt(previousCycle)) * Mathf.Abs(transform.lossyScale.z) : 0;
        previousCycle = current; hasCycle = true;
        var direction = Vector3.ProjectOnPlane(agent.desiredVelocity, Vector3.up);
        if (direction.sqrMagnitude < .0001f) { agent.nextPosition = actor.position; return; }
        travel = Mathf.Min(travel, Mathf.Max(0, agent.remainingDistance - agent.stoppingDistance));
        agent.nextPosition = actor.position;
        agent.Move(direction.normalized * Mathf.Max(0, travel));
        actor.position = agent.nextPosition;
    }

    private float DistanceAt(float cycle) => Mathf.Floor(cycle) * CycleDistance + rootDistance.Evaluate(Mathf.Repeat(cycle, 1f));
    private void OnDisable() { SetNavigationOwnership(false); hasCycle = false; speed = 0; }
    private void OnEnable() { if (actor != null) previousPosition = actor.position; }
}
