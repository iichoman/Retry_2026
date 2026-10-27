public interface IPlayerWeapon
{
    string WeaponId { get; }
    WeaponGrade Grade { get; }
    int AttackDamage { get; }

    void Initialize(Player_Attack owner);
    void Tick(float deltaTime);
    void RequestAttack();
    bool TryConsumeAnimationRequest(out int comboIndex);
    bool TryGetAnimationStateName(int comboIndex, out string stateName);
    void OnAnimationStarted(int comboIndex);
    void OnAnimationCompleted(int comboIndex);
    void OnAttackWindowOpened(int comboIndex);
    void OnAttackWindowClosed(int comboIndex);
    void OnComboWindowOpened(int comboIndex);
    void OnComboWindowCommitted(int comboIndex);
    void OnComboWindowClosed(int comboIndex);
    void CancelAttack();
}
