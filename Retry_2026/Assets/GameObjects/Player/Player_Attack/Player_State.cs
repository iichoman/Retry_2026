using UnityEngine;

public class Player_State : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int defense = 0;

    public int CurrentHp { get; private set; }
    public bool IsDead { get; private set; }

    private void Awake()
    {
        CurrentHp = maxHp;
    }

    public void TakeDamage(int damage, GameObject attacker)
    {
        if (IsDead) return;

        int finalDamage = Mathf.Max(1, damage - defense);
        CurrentHp = Mathf.Max(0, CurrentHp - finalDamage);

        if (CurrentHp == 0)
        {
            Die();
        }
    }

    private void Die()
    {
        IsDead = true;
    }
}
