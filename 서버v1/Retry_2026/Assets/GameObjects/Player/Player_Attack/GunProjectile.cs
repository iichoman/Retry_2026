using UnityEngine;

[RequireComponent(typeof(Collider))]
public class GunProjectile : MonoBehaviour
{
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private bool destroyOnAnyHit = true;
    [SerializeField, Min(0f)] private float sweepRadius = 0.05f;

    private GameObject owner;
    private int damage;
    private Vector3 direction;
    private float speed;
    private float lifetime;
    private float maxDistance;
    private float age;
    private float traveledDistance;
    private bool launched;
    private readonly RaycastHit[] sweepHits = new RaycastHit[8];

    private void Awake()
    {
        Collider projectileCollider = GetComponent<Collider>();
        if (projectileCollider != null && !projectileCollider.isTrigger)
        {
            projectileCollider.isTrigger = true;
        }
    }

    public void Launch(
        GameObject projectileOwner,
        int projectileDamage,
        Vector3 fireDirection,
        float projectileSpeed,
        float projectileLifetime,
        float projectileMaxDistance
    )
    {
        owner = projectileOwner;
        damage = Mathf.Max(1, projectileDamage);
        direction = ResolveFlatDirection(fireDirection);
        speed = Mathf.Max(0.1f, projectileSpeed);
        lifetime = Mathf.Max(0.01f, projectileLifetime);
        maxDistance = Mathf.Max(0.1f, projectileMaxDistance);
        age = 0f;
        traveledDistance = 0f;
        launched = true;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    private void Update()
    {
        if (!launched)
        {
            return;
        }

        float distance = speed * Time.deltaTime;
        if (TrySweep(distance))
        {
            return;
        }

        transform.position += direction * distance;
        traveledDistance += distance;
        age += Time.deltaTime;

        if (age >= lifetime || traveledDistance >= maxDistance)
        {
            Destroy(gameObject);
        }
    }

    private Vector3 ResolveFlatDirection(Vector3 requestedDirection)
    {
        Vector3 flat = requestedDirection;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
        {
            return flat.normalized;
        }

        flat = transform.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
        {
            return flat.normalized;
        }

        return Vector3.forward;
    }

    private bool TrySweep(float distance)
    {
        if (distance <= 0f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            transform.position,
            sweepRadius,
            direction,
            sweepHits,
            distance,
            hitLayers,
            triggerInteraction
        );

        int bestIndex = -1;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = sweepHits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            if (owner != null && hitCollider.transform.root == owner.transform.root)
            {
                continue;
            }

            if (sweepHits[i].distance >= bestDistance)
            {
                continue;
            }

            bestDistance = sweepHits[i].distance;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        RaycastHit hit = sweepHits[bestIndex];
        transform.position += direction * Mathf.Max(0f, hit.distance);
        HandleHit(hit.collider);
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!launched || other == null)
        {
            return;
        }

        if (owner != null && other.transform.root == owner.transform.root)
        {
            return;
        }

        if ((hitLayers.value & (1 << other.gameObject.layer)) == 0)
        {
            return;
        }

        HandleHit(other);
    }

    private void HandleHit(Collider other)
    {
        if (other == null)
        {
            return;
        }

        IDamageable damageable = other.GetComponent<IDamageable>();
        if (damageable == null)
        {
            damageable = other.GetComponentInParent<IDamageable>();
        }

        if (damageable != null)
        {
            damageable.TakeDamage(damage, owner != null ? owner : gameObject);
            PlayHitEffect(other);
            Destroy(gameObject);
            return;
        }

        if (destroyOnAnyHit)
        {
            PlayHitEffect(other);
            Destroy(gameObject);
        }
    }

    private void PlayHitEffect(Collider other)
    {
        HitEffectReceiver effectReceiver = other.GetComponent<HitEffectReceiver>();
        if (effectReceiver == null)
        {
            effectReceiver = other.GetComponentInParent<HitEffectReceiver>();
        }

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 hitNormal = direction.sqrMagnitude > 0.001f ? -direction.normalized : -transform.forward;

        if (effectReceiver != null)
        {
            effectReceiver.Play(HitEffectType.Bullet, hitPoint, hitNormal);
            return;
        }

        HitEffectReceiver.PlayBuiltInEffect(HitEffectType.Bullet, hitPoint + hitNormal * 0.02f, Quaternion.LookRotation(hitNormal, Vector3.up));
    }
}
