using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MonsterHitbox : MonoBehaviour
{
    [SerializeField] private int fallbackDamage = 10;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private Collider hitboxCollider;

    private readonly HashSet<Collider> hitTargets = new HashSet<Collider>();
    private Monster_Attack owner;
    private bool canHit;

    private void Awake()
    {
        if (hitboxCollider == null)
        {
            hitboxCollider = GetComponent<Collider>();
        }

        if (hitboxCollider != null && !hitboxCollider.isTrigger)
        {
            Debug.LogWarning($"{name} MonsterHitbox collider should be set as Trigger.", this);
        }

        SetHitboxActive(false);
    }

    public void SetOwner(Monster_Attack attackOwner)
    {
        owner = attackOwner;
    }

    public void BeginSwing()
    {
        hitTargets.Clear();
        SetHitboxActive(true);
    }

    public void EndSwing()
    {
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

        if (owner != null && other.transform.root == owner.transform.root)
        {
            return;
        }

        if (hitTargets.Contains(other))
        {
            return;
        }

        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null)
        {
            damageable = other.GetComponentInParent<IDamageable>();
        }

        if (damageable == null)
        {
            return;
        }

        int damage = owner != null && owner.CurrentAttackDamage > 0 ? owner.CurrentAttackDamage : fallbackDamage;
        damageable.TakeDamage(damage, owner != null ? owner.gameObject : gameObject);
        hitTargets.Add(other);
    }
}
