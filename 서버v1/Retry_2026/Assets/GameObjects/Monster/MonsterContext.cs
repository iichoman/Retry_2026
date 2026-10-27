using UnityEngine.AI;

public class MonsterContext
{
    public Monster Monster { get; }
    public Monster_State State { get; }
    public Monster_Attack Attack { get; }
    public NavMeshAgent Agent { get; }

    public MonsterContext(
        Monster monster,
        Monster_State state,
        Monster_Attack attack,
        NavMeshAgent agent
    )
    {
        Monster = monster;
        State = state;
        Attack = attack;
        Agent = agent;
    }
}
