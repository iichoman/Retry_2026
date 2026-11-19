using UnityEngine;

[DisallowMultipleComponent]
public class test_gun : Weapon_Gun
{
    [Header("Identity")]
    [SerializeField] private string weaponId = "test_gun";
    [SerializeField] private WeaponGrade grade = WeaponGrade.Common;

    [Header("Stats")]
    [SerializeField, Min(1)] private int attackDamage = 8;

    [Header("Gun Combo")]
    [SerializeField, Min(1)] private int maxComboCount = 3;
    [SerializeField, Min(0.05f)] private float comboInputWindow = 0.75f;

    public override string WeaponId => weaponId;
    public override WeaponGrade Grade => grade;
    public override int AttackDamage => attackDamage;
    protected override int MaxComboCount => maxComboCount;
    protected override float ComboInputWindow => comboInputWindow;
}
