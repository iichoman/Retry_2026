using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class WeaponHitbox : MonoBehaviour
{
    [SerializeField] private int damage = 10;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private Collider hitboxCollider;

    private readonly HashSet<Collider> hitTargets = new HashSet<Collider>();
    private Player_Attack owner;
    private bool canHit;

    private void Awake()
    {
        if (hitboxCollider == null)
        {
            hitboxCollider = GetComponent<Collider>();
        }

        if (hitboxCollider != null && !hitboxCollider.isTrigger)
        {
            Debug.LogWarning($"{name} WeaponHitbox collider should be set as Trigger.", this);
        }
    }

    public void SetOwner(Player_Attack attackOwner)
    {
        owner = attackOwner;
    }

    public void BeginSwing()
    {
        hitTargets.Clear();
        Debug.Log($"[{name}] Swing started.", this);
        SetHitboxActive(true);
    }

    public void EndSwing()
    {
        Debug.Log(
            hitTargets.Count > 0
                ? $"[{name}] Swing result: HIT ({hitTargets.Count} target(s))."
                : $"[{name}] Swing result: MISS (no collision).",
            this
        );
        SetHitboxActive(false);
        hitTargets.Clear();
    }

    public void SetHitboxActive(bool active)
    {
        canHit = active;

        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = active;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!canHit)
        {
            return;
        }

        if ((targetLayers.value & (1 << other.gameObject.layer)) == 0)
        {
            return;
        }

        if (hitTargets.Contains(other))
        {
            return;
        }

        if (!other.TryGetComponent<IDamageable>(out var damageable))
        {
            return;
        }

        damageable.TakeDamage(damage, owner != null ? owner.gameObject : gameObject);
        hitTargets.Add(other);
    }
}
