using UnityEngine;

[DisallowMultipleComponent]
public abstract class Weapon_Sword : MonoBehaviour, IPlayerWeapon
{
    [Header("References")]
    [SerializeField] private WeaponHitbox weaponHitbox;

    private bool attackInProgress;
    private bool queuedNextAttack;
    private bool hasPendingAnimationRequest;
    private bool swingActive;
    private int currentComboIndex;
    private int pendingAnimationComboIndex;
    private float comboTimer;

    protected Player_Attack Owner { get; private set; }
    protected virtual string AttackAnimationStatePrefix => "sword";
    protected abstract int MaxComboCount { get; }
    protected abstract float ComboInputWindow { get; }

    public abstract string WeaponId { get; }
    public abstract WeaponGrade Grade { get; }
    public abstract int AttackDamage { get; }

    public void Initialize(Player_Attack owner)
    {
        Owner = owner;
        CancelAttack();

        if (weaponHitbox == null)
        {
            weaponHitbox = GetComponentInChildren<WeaponHitbox>();
        }

        if (weaponHitbox != null)
        {
            weaponHitbox.SetOwner(owner);
            weaponHitbox.SetHitboxActive(false);
        }
    }

    public void Tick(float deltaTime)
    {
        if (comboTimer > 0f)
        {
            comboTimer = Mathf.Max(0f, comboTimer - deltaTime);
        }

        if (comboTimer <= 0f && !attackInProgress && !hasPendingAnimationRequest && !queuedNextAttack)
        {
            ResetComboState();
        }
    }

    public void RequestAttack()
    {
        if (hasPendingAnimationRequest)
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

        if (attackInProgress)
        {
            if (comboTimer > 0f)
            {
                queuedNextAttack = true;
            }

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

    public bool TryConsumeAnimationRequest(out int comboIndex)
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

    public bool TryGetAnimationStateName(int comboIndex, out string stateName)
    {
        if (comboIndex < 1 || comboIndex > ResolvedMaxComboCount)
        {
            stateName = string.Empty;
            return false;
        }

        stateName = $"Base Layer.{AttackAnimationStatePrefix}_attack_{comboIndex}";
        return true;
    }

    public void OnAnimationStarted(int comboIndex)
    {
        currentComboIndex = comboIndex;
        attackInProgress = true;
        comboTimer = ResolvedComboInputWindow;
        Debug.Log($"[{WeaponId}] Combo attack {comboIndex} started.", this);
    }

    public void OnAnimationCompleted(int comboIndex)
    {
        attackInProgress = false;
        CloseSwing();

        if (queuedNextAttack && currentComboIndex < ResolvedMaxComboCount)
        {
            queuedNextAttack = false;
            StartComboStep(currentComboIndex + 1);
            return;
        }

        if (currentComboIndex >= ResolvedMaxComboCount || comboTimer <= 0f)
        {
            ResetComboState();
        }
    }

    public void OnAttackWindowOpened(int comboIndex)
    {
        if (weaponHitbox == null || swingActive)
        {
            return;
        }

        swingActive = true;
        weaponHitbox.BeginSwing();
    }

    public void OnAttackWindowClosed(int comboIndex)
    {
        CloseSwing();
    }

    public void CancelAttack()
    {
        CloseSwing();
        ResetComboState();
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
        queuedNextAttack = false;
        hasPendingAnimationRequest = false;
        currentComboIndex = 0;
        pendingAnimationComboIndex = 0;
        comboTimer = 0f;
    }

    private void CloseSwing()
    {
        if (weaponHitbox == null || !swingActive)
        {
            return;
        }

        swingActive = false;
        weaponHitbox.EndSwing();
    }

    private int ResolvedMaxComboCount => Mathf.Max(1, MaxComboCount);
    private float ResolvedComboInputWindow => Mathf.Max(0.05f, ComboInputWindow);
}
