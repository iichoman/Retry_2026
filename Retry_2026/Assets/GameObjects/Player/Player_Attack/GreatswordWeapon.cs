using UnityEngine;

/// <summary>Heavy three-hit combo. Its own Animator state owns wind-up, swing and recovery.</summary>
public sealed class GreatswordWeapon : Weapon_Sword
{
    public const string Id = "greatsword";
    public const float AttackSeconds = 1.6f;
    public const float SwingStart = .56f;
    public const float SwingDuration = .288f;

    public static float SwingStartFor(int combo)=>combo==3?.72f:SwingStart;
    public static float SwingDurationFor(int combo)=>combo==3?.32f:SwingDuration;

    [SerializeField, Min(1)] private int damage = 45;
    [SerializeField] private bool bladeFacingCorrected;
    private void Awake() => CorrectBladeFacing();

    public void CorrectBladeFacing()
    {
        if (bladeFacingCorrected) return;
        var blade = GetComponentInChildren<WeaponHitbox>(true);
        if (blade == null) return;
        // The imported greatsword points opposite the ordinary sword in the hand socket.
        blade.transform.localRotation *= Quaternion.Euler(180, 0, 0);
        bladeFacingCorrected = true;
    }
    public override string WeaponId => Id;
    public override WeaponGrade Grade => WeaponGrade.Rare;
    public override int AttackDamage => damage;
    protected override string AttackAnimationStatePrefix => "greatsword";
    protected override int MaxComboCount => 3;
    protected override float ComboInputWindow => AttackSeconds + .5f;
}
