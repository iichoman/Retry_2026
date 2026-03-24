using UnityEngine;

public interface IDamageable
{
    void TakeDamage(int damage, GameObject attacker);
}
// 몬스터 이외에도 공격 가능한 오브젝트 등에 적용