using UnityEngine;

public enum MonsterMovementType 
{
    Ground,
    Flying
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Monster_State))]
public class Monster : MonoBehaviour
{
    [Header("Monster Base")]
    [SerializeField] private string monsterId = "Monster";
    [SerializeField] private MonsterMovementType movementType = MonsterMovementType.Ground;

    [Header("Combat Base")]
    [SerializeField, Min(0f)] protected float detectRange = 8f;
    [SerializeField, Min(0f)] protected float attackRange = 1.5f;
    [SerializeField, Min(0f)] protected float moveSpeed = 2f;

    protected Monster_State state;
    protected Transform target;

    public string MonsterId => monsterId;
    public MonsterMovementType MovementType => movementType; //public일 이유 없나
    public Monster_State State => state;
    public bool HasTarget => target != null;

    protected virtual void Awake()
    {
        state = GetComponent<Monster_State>();
    }

    protected virtual void Update()
    {
        if (state == null || state.IsDead)
        {
            return;
        }

        Tick();
    }

    public virtual void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    protected virtual void Tick()
    {
        // Child monster classes (Goblin, Knight, etc.) override this.
    }
}
