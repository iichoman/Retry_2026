using UnityEngine;

[DisallowMultipleComponent]
public abstract class Weapon_Gun : MonoBehaviour, IPlayerWeapon
{
    protected Player_Attack Owner { get; private set; }

    public abstract string WeaponId { get; }
    public abstract WeaponGrade Grade { get; }
    public abstract int AttackDamage { get; }

    public virtual void Initialize(Player_Attack owner)
    {
        Owner = owner;
        CancelAttack();
    }

    public virtual void Tick(float deltaTime)
    {
    }

    public abstract void RequestAttack();
    public abstract bool TryConsumeAnimationRequest(out int comboIndex);

    public virtual bool TryGetAnimationStateName(int comboIndex, out string stateName)
    {
        stateName = string.Empty;
        return false;
    }

    public virtual void OnAnimationStarted(int comboIndex)
    {
    }

    public virtual void OnAnimationCompleted(int comboIndex)
    {
    }

    public virtual void OnAttackWindowOpened(int comboIndex)
    {
    }

    public virtual void OnAttackWindowClosed(int comboIndex)
    {
    }

    public virtual void OnComboWindowOpened(int comboIndex)
    {
    }

    public virtual void OnComboWindowCommitted(int comboIndex)
    {
    }

    public virtual void OnComboWindowClosed(int comboIndex)
    {
    }

    public virtual void CancelAttack()
    {
    }
}
