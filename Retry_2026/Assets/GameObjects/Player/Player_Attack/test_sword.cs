using UnityEngine;

[DisallowMultipleComponent]
public class test_sword : Weapon_Sword
{
    [Header("Identity")]
    [SerializeField] private string weaponId = "test_sword";
    [SerializeField] private WeaponGrade grade = WeaponGrade.Common;

    [Header("Stats")]
    [SerializeField, Min(1)] private int attackDamage = 10;

    [Header("Sword Settings")]
    [SerializeField, Min(1)] private int maxComboCount = 4;
    [SerializeField, Min(0.05f)] private float comboInputWindow = 0.75f;

    public override string WeaponId => weaponId;
    public override WeaponGrade Grade => grade;
    public override int AttackDamage => attackDamage;
    protected override int MaxComboCount => maxComboCount;
    protected override float ComboInputWindow => comboInputWindow;
}
