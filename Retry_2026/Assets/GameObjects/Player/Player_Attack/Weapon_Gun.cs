using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class Weapon_Gun : MonoBehaviour, IPlayerWeapon
{
    [Header("References")]
    [SerializeField] private GunProjectile projectilePrefab;
    [SerializeField] private Transform muzzleTransform;
    [SerializeField] private string attackStateMachinePath = "Base Layer.test";

    [Header("Fire Settings")]
    [SerializeField, Min(0f)] private float fireCooldown = 0.25f;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 35f;
    [SerializeField, Min(0.1f)] private float projectileLifetime = 2f;
    [SerializeField, Min(0.1f)] private float projectileMaxDistance = 45f;
    [SerializeField] private List<GunFirePattern> firePatterns = new List<GunFirePattern>
    {
        new GunFirePattern()
    };

    private bool attackInProgress;
    private bool comboWindowOpen;
    private bool queuedNextAttack;
    private bool hasPendingAnimationRequest;
    private int currentComboIndex;
    private int pendingAnimationComboIndex;
    private float comboTimer;
    private float cooldownTimer;

    private bool burstActive;
    private int burstComboIndex;
    private int burstShotsRemaining;
    private float burstTimer;
    private Vector3 burstDirection;

    protected Player_Attack Owner { get; private set; }
    protected virtual string AttackAnimationStatePrefix => "gun";
    protected abstract int MaxComboCount { get; }
    protected abstract float ComboInputWindow { get; }

    public abstract string WeaponId { get; }
    public abstract WeaponGrade Grade { get; }
    public abstract int AttackDamage { get; }

    public virtual void Initialize(Player_Attack owner)
    {
        Owner = owner;
        CancelAttack();
        ResolveReferences();
    }

    public virtual void Tick(float deltaTime)
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);
        }

        if (comboTimer > 0f)
        {
            comboTimer = Mathf.Max(0f, comboTimer - deltaTime);
        }

        TickBurst(deltaTime);

        if (comboTimer <= 0f && !attackInProgress && !hasPendingAnimationRequest && !queuedNextAttack)
        {
            ResetComboState();
        }
    }

    public virtual void RequestAttack()
    {
        if (hasPendingAnimationRequest)
        {
            return;
        }

        if (attackInProgress)
        {
            if (comboWindowOpen && comboTimer > 0f && currentComboIndex < ResolvedMaxComboCount)
            {
                queuedNextAttack = true;
            }

            return;
        }

        if (cooldownTimer > 0f && currentComboIndex <= 0)
        {
            return;
        }

        if (currentComboIndex <= 0)
        {
            StartComboStep(1);
            return;
        }

        if (currentComboIndex >= ResolvedMaxComboCount)
        {
            return;
        }

        if (comboTimer > 0f)
        {
            StartComboStep(currentComboIndex + 1);
            return;
        }

        ResetComboState();
        StartComboStep(1);
    }

    public virtual bool TryConsumeAnimationRequest(out int comboIndex)
    {
        comboIndex = 0;

        if (!hasPendingAnimationRequest)
        {
            return false;
        }

        comboIndex = pendingAnimationComboIndex;
        pendingAnimationComboIndex = 0;
        hasPendingAnimationRequest = false;
        return true;
    }

    public virtual bool TryGetAnimationStateName(int comboIndex, out string stateName)
    {
        if (comboIndex < 1 || comboIndex > ResolvedMaxComboCount)
        {
            stateName = string.Empty;
            return false;
        }

        stateName = $"{attackStateMachinePath}.{AttackAnimationStatePrefix}_attack_{comboIndex}";
        return true;
    }

    public virtual void OnAnimationStarted(int comboIndex)
    {
        currentComboIndex = comboIndex;
        attackInProgress = true;
        comboWindowOpen = false;
        comboTimer = ResolvedComboInputWindow;
    }

    public virtual void OnAnimationCompleted(int comboIndex)
    {
        attackInProgress = false;
        comboWindowOpen = false;

        if (currentComboIndex >= ResolvedMaxComboCount || comboTimer <= 0f)
        {
            ResetComboState();
        }
    }

    public virtual void OnAttackWindowOpened(int comboIndex)
    {
        StartBurst(comboIndex);
    }

    public virtual void OnAttackWindowClosed(int comboIndex)
    {
    }

    public virtual void OnComboWindowOpened(int comboIndex)
    {
        if (!attackInProgress || currentComboIndex != comboIndex)
        {
            return;
        }

        comboWindowOpen = true;
    }

    public virtual void OnComboWindowCommitted(int comboIndex)
    {
        if (!attackInProgress || currentComboIndex != comboIndex)
        {
            return;
        }

        comboWindowOpen = false;

        if (!queuedNextAttack || currentComboIndex >= ResolvedMaxComboCount)
        {
            return;
        }

        queuedNextAttack = false;
        attackInProgress = false;
        StartComboStep(currentComboIndex + 1);
    }

    public virtual void OnComboWindowClosed(int comboIndex)
    {
        if (currentComboIndex != comboIndex)
        {
            return;
        }

        comboWindowOpen = false;
    }

    public virtual void CancelAttack()
    {
        ResetComboState();
        StopBurst();
    }

    private void ResolveReferences()
    {
        if (muzzleTransform == null)
        {
            muzzleTransform = transform;
        }

    }

    private void StartComboStep(int comboIndex)
    {
        currentComboIndex = comboIndex;
        pendingAnimationComboIndex = comboIndex;
        hasPendingAnimationRequest = true;
        comboTimer = ResolvedComboInputWindow;
    }

    private void ResetComboState()
    {
        attackInProgress = false;
        comboWindowOpen = false;
        queuedNextAttack = false;
        hasPendingAnimationRequest = false;
        currentComboIndex = 0;
        pendingAnimationComboIndex = 0;
        comboTimer = 0f;
    }

    private void StartBurst(int comboIndex)
    {
        GunFirePattern pattern = GetFirePattern(comboIndex);
        int shotCount = Mathf.Max(1, pattern.shotCount);
        Vector3 direction = ResolveFireDirection();
        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        burstActive = true;
        burstComboIndex = comboIndex;
        burstShotsRemaining = shotCount;
        burstTimer = 0f;
        burstDirection = direction;

        FireBurstShot(pattern);
    }

    private void TickBurst(float deltaTime)
    {
        if (!burstActive)
        {
            return;
        }

        GunFirePattern pattern = GetFirePattern(burstComboIndex);
        if (burstShotsRemaining <= 0)
        {
            StopBurst();
            return;
        }

        burstTimer -= deltaTime;
        if (burstTimer > 0f)
        {
            return;
        }

        FireBurstShot(pattern);
    }

    private void FireBurstShot(GunFirePattern pattern)
    {
        if (burstShotsRemaining <= 0)
        {
            StopBurst();
            return;
        }

        FireProjectiles(pattern);
        burstShotsRemaining--;
        burstTimer = Mathf.Max(0.01f, pattern.shotInterval);

        if (burstShotsRemaining <= 0)
        {
            StopBurst();
        }
    }

    private void StopBurst()
    {
        burstActive = false;
        burstComboIndex = 0;
        burstShotsRemaining = 0;
        burstTimer = 0f;
        burstDirection = Vector3.zero;
    }

    private void FireProjectiles(GunFirePattern pattern)
    {
        if (projectilePrefab == null || muzzleTransform == null || Owner == null)
        {
            return;
        }

        Vector3 direction = burstDirection.sqrMagnitude > 0.001f ? burstDirection : ResolveFireDirection();
        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        int pellets = Mathf.Max(1, pattern.pelletCount);
        float spread = Mathf.Max(0f, pattern.spreadAngle);
        for (int i = 0; i < pellets; i++)
        {
            Vector3 shotDirection = ApplyHorizontalSpread(direction, spread, pellets, i);
            Quaternion rotation = Quaternion.LookRotation(shotDirection, Vector3.up);
            GunProjectile projectile = Instantiate(projectilePrefab, muzzleTransform.position, rotation);
            projectile.Launch(
                Owner.gameObject,
                AttackDamage,
                shotDirection,
                projectileSpeed,
                projectileLifetime,
                projectileMaxDistance
            );
        }

        cooldownTimer = Mathf.Max(cooldownTimer, fireCooldown);
    }

    private Vector3 ResolveFireDirection()
    {
        return Owner != null ? FlattenDirection(Owner.transform.forward) : FlattenDirection(transform.forward);
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
        {
            return Vector3.zero;
        }

        return direction.normalized;
    }

    private static Vector3 ApplyHorizontalSpread(Vector3 direction, float spreadAngle, int count, int index)
    {
        if (spreadAngle <= 0f || count <= 1)
        {
            return direction;
        }

        float t = count == 1 ? 0f : index / (float)(count - 1);
        float angle = Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, t);
        return Quaternion.AngleAxis(angle, Vector3.up) * direction;
    }

    private GunFirePattern GetFirePattern(int comboIndex)
    {
        int index = Mathf.Clamp(comboIndex - 1, 0, Mathf.Max(0, firePatterns.Count - 1));
        if (firePatterns.Count == 0 || firePatterns[index] == null)
        {
            return GunFirePattern.Default;
        }

        return firePatterns[index];
    }

    private int ResolvedMaxComboCount => Mathf.Max(1, MaxComboCount);
    private float ResolvedComboInputWindow => Mathf.Max(0.05f, ComboInputWindow);

    [Serializable]
    private sealed class GunFirePattern
    {
        [Min(1)] public int shotCount = 1;
        [Min(0.01f)] public float shotInterval = 0.08f;
        [Min(1)] public int pelletCount = 1;
        [Min(0f)] public float spreadAngle = 0f;

        public static GunFirePattern Default => new GunFirePattern();
    }
}
