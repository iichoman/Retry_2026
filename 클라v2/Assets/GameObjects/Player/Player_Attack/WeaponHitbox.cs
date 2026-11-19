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
    [Header("Heavy cleave (server box dimensions)")]
    [SerializeField] private bool forwardCleave;
    [SerializeField] private float cleaveLength = 3.5f;
    [SerializeField] private float cleaveWidth = 3f;

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
        SampleCleave();
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
            hitboxCollider.enabled = active && !forwardCleave;
        }
    }

    private void FixedUpdate() => SampleCleave();

    private void SampleCleave()
    {
        if (!forwardCleave || !canHit || owner == null || NetworkRuntime.IsOnline) return;
        var root = owner.transform;
        // Include all targets already in the box when the impact window opens, once per swing.
        foreach (var target in Physics.OverlapBox(root.position + Vector3.up + root.forward * (cleaveLength * .5f),
            new Vector3(cleaveWidth * .5f, 1.25f, cleaveLength * .5f), root.rotation, targetLayers, QueryTriggerInteraction.Ignore))
            OnTriggerEnter(target);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Local swing geometry is presentation; online damage/impact comes from COMBAT_EVENT.
        if (NetworkRuntime.IsOnline) return;
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
        hitTargets.Add(damageable);
        GameObject source = owner != null ? owner.gameObject : gameObject;
        if (damageable is Player_State player)
        {
            if (!player.TryTakeMeleeDamage(damage, source)) return;
        }
        else damageable.TakeDamage(damage, source);
        PlayHitEffect(other);

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
        // The blade is often already inside the collider, where ClosestPoint returns
        // the interior point unchanged. Project from outside and emit toward the attacker.
        Vector3 hitNormal = sourcePosition - other.bounds.center;
        Vector3 resolvedNormal = hitNormal.sqrMagnitude > 0.001f ? hitNormal.normalized : -transform.forward;
        Vector3 hitPoint = other.ClosestPoint(other.bounds.center + resolvedNormal * (other.bounds.size.magnitude + 1f));

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
