using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class WeaponHitbox : MonoBehaviour
{
    [SerializeField] private int fallbackDamage = 10;
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField] private Collider hitboxCollider;
    [SerializeField] private List<WeaponSwingTrail> swingTrails = new List<WeaponSwingTrail>();
    [SerializeField] private bool ensureKinematicRigidbody = true;

    private readonly HashSet<IDamageable> hitTargets = new HashSet<IDamageable>();
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
            Debug.LogWarning($"{name} WeaponHitbox collider was not Trigger. It has been set automatically.", this);
            hitboxCollider.isTrigger = true;
        }

        EnsureRigidbodyForTriggerEvents();
        CacheSwingTrails();
    }

    private void EnsureRigidbodyForTriggerEvents()
    {
        if (!ensureKinematicRigidbody || GetComponentInParent<Rigidbody>() != null)
        {
            return;
        }

        Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
    }

    public void SetOwner(Player_Attack attackOwner)
    {
        owner = attackOwner;
    }

    public void BeginSwing()
    {
        hitTargets.Clear();
        Debug.Log($"[{name}] Swing started.", this);
        BeginSwingTrails();
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
        EndSwingTrails();
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

        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null)
        {
            damageable = other.GetComponentInParent<IDamageable>();
        }

        if (damageable == null)
        {
            Debug.Log($"Weapon hit ignored: {other.name} has no IDamageable in parent chain.", other);
            return;
        }

        if (hitTargets.Contains(damageable))
        {
            return;
        }

        int damage = owner != null && owner.CurrentAttackDamage > 0 ? owner.CurrentAttackDamage : fallbackDamage;
        damageable.TakeDamage(damage, owner != null ? owner.gameObject : gameObject);
        PlayHitEffect(other);
        hitTargets.Add(damageable);

        Debug.Log($"Weapon hit: name={other.gameObject.name}, id={other.GetInstanceID()}", other);
    }

    private void PlayHitEffect(Collider other)
    {
        HitEffectReceiver effectReceiver = other.GetComponent<HitEffectReceiver>();
        if (effectReceiver == null)
        {
            effectReceiver = other.GetComponentInParent<HitEffectReceiver>();
        }

        Vector3 sourcePosition = owner != null ? owner.transform.position : transform.position;
        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 hitNormal = hitPoint - sourcePosition;
        Vector3 resolvedNormal = hitNormal.sqrMagnitude > 0.001f ? hitNormal.normalized : transform.forward;

        if (effectReceiver != null)
        {
            effectReceiver.Play(HitEffectType.Melee, hitPoint, resolvedNormal);
            return;
        }

        HitEffectReceiver.PlayBuiltInEffect(HitEffectType.Melee, hitPoint + resolvedNormal * 0.02f, Quaternion.LookRotation(resolvedNormal, Vector3.up));
    }

    private void CacheSwingTrails()
    {
        swingTrails.RemoveAll(trail => trail == null);

        if (swingTrails.Count > 0)
        {
            return;
        }

        GetComponentsInChildren(true, swingTrails);
    }

    private void BeginSwingTrails()
    {
        CacheSwingTrails();

        for (int i = 0; i < swingTrails.Count; i++)
        {
            if (swingTrails[i] != null)
            {
                swingTrails[i].BeginTrail();
            }
        }
    }

    private void EndSwingTrails()
    {
        for (int i = 0; i < swingTrails.Count; i++)
        {
            if (swingTrails[i] != null)
            {
                swingTrails[i].EndTrail();
            }
        }
    }
}
