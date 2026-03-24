using UnityEngine;

public enum MonsterType
{
    Normal,
    Elite,
    Boss
}

public enum MonsterElement
{    None
}

[DisallowMultipleComponent]
public class Monster_State : MonoBehaviour, IDamageable
{
    [Header("Monster Info")]
    [SerializeField] private MonsterType monsterType = MonsterType.Normal;
    [SerializeField] private MonsterElement monsterElement = MonsterElement.None;

    [Header("Stats")]
    [SerializeField, Min(1)] private int maxHp = 100;
    [SerializeField, Min(0)] private int attackPower = 10;
    [SerializeField, Min(0)] private int defense = 0;

    public MonsterType Type => monsterType;
    public MonsterElement Element => monsterElement;
    public int MaxHp => maxHp;
    public int CurrentHp { get; private set; }
    public int AttackPower => attackPower;
    public int Defense => defense;
    public bool IsDead { get; private set; }

    private void Awake()
    {
        CurrentHp = maxHp;
        IsDead = false;
    }

    public void TakeDamage(int damage, GameObject attacker)
    {
        if (IsDead)
        {
            return;
        }

        int finalDamage = Mathf.Max(1, damage - defense);
        CurrentHp = Mathf.Max(0, CurrentHp - finalDamage);
        string attackerName = attacker != null ? attacker.name : "Unknown";
        Debug.Log(
            $"[{name}] hit by [{attackerName}] damage={finalDamage}, hp={CurrentHp}/{maxHp}",
            this
        );

        if (CurrentHp <= 0)
        {
            Die(attacker);
        }
    }

    private void Die(GameObject attacker)
    {
        IsDead = true;
    }
}
