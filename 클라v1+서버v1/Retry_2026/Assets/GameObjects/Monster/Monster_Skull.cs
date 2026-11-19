using UnityEngine;
using UnityEngine.AI;

public class Monster_Skull : Monster
{
    private BTNode behaviourTree;
    private MonsterContext context;

    protected override void Awake()
    {
        base.Awake();
        
        context = new MonsterContext(
            this,
            GetComponent<Monster_State>(),
            GetComponent<Monster_Attack>(),
            GetComponent<NavMeshAgent>()
        );

        behaviourTree = new BTSelector(
            new BTSequence(
                new MonsterHasTargetNode(context),
                new MonsterInAttackRangeNode(context),
                new MonsterAttackNode(context)
            ),
            new BTSequence(
                new MonsterHasTargetNode(context),
                new MonsterChaseTargetNode(context)
            ),
            new MonsterWanderNode(context),
            new MonsterIdleNode(context)
        );
    }

    protected override void Tick()
    {
        if (behaviourTree == null)
        {
            return;
        }

        behaviourTree.Tick();
    }
}
