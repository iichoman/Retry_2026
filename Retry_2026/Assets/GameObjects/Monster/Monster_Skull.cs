using UnityEngine;

public class Monster_Skull : Monster
{
    private Monster_Attack monsterAttack;

    protected override void Awake()
    {
        base.Awake();
        monsterAttack = GetComponent<Monster_Attack>();
    }

    protected override void Tick()
    {
        if (!HasTarget || monsterAttack == null)
        {
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, Target.position);
        if (distanceToTarget <= AttackRange)
        {
            monsterAttack.RequestAttack();
        }
    }
}
