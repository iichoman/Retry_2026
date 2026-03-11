using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_ClassicAttack : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private Transform attackOrigin;

    [Header("Attack Settings")]
    [SerializeField, Min(1)] private int damage = 10;
    [SerializeField, Min(0.1f)] private float attackDistance = 1.8f;
    [SerializeField, Min(0.05f)] private float attackRadius = 0.6f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.25f;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    private readonly HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();
    private bool previousAttackInput;
    private float cooldownTimer;

    private void Awake()
    {
        if (playerInput == null)
        {
            playerInput = GetComponent<Defalult_Input>();
        }

        if (attackOrigin == null)
        {
            attackOrigin = transform;
        }
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }

        bool currentAttackInput = playerInput != null && playerInput.Attack;
        bool attackPressedThisFrame = currentAttackInput && !previousAttackInput;
        previousAttackInput = currentAttackInput;

        if (attackPressedThisFrame)
        {
            TryAttack();
        }
    }

    private void TryAttack()
    {
        if (cooldownTimer > 0f)
        {
            return;
        }

        PerformAttack();
        cooldownTimer = attackCooldown;
    }

    private void PerformAttack()
    {
        damagedTargets.Clear();

        Vector3 start = attackOrigin.position;
        Vector3 end = start + attackOrigin.forward * attackDistance;
        Collider[] hits = Physics.OverlapCapsule(start, end, attackRadius, targetLayers, triggerInteraction);

        for (int i = 0; i < hits.Length; i++)
        {
            if (!hits[i].TryGetComponent<IDamageable>(out var damageable))
            {
                continue;
            }

            if (damagedTargets.Contains(damageable))
            {
                continue;
            }

            damageable.TakeDamage(damage, gameObject);
            damagedTargets.Add(damageable);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = attackOrigin != null ? attackOrigin : transform;
        Vector3 start = origin.position;
        Vector3 end = start + origin.forward * attackDistance;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(start, attackRadius);
        Gizmos.DrawWireSphere(end, attackRadius);
        Gizmos.DrawLine(start + origin.up * attackRadius, end + origin.up * attackRadius);
        Gizmos.DrawLine(start - origin.up * attackRadius, end - origin.up * attackRadius);
        Gizmos.DrawLine(start + origin.right * attackRadius, end + origin.right * attackRadius);
        Gizmos.DrawLine(start - origin.right * attackRadius, end - origin.right * attackRadius);
    }
}
