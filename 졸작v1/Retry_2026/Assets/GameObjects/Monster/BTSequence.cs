using System.Collections.Generic;

public class BTSequence : BTNode
{
    private readonly List<BTNode> children = new List<BTNode>();

    public BTSequence(params BTNode[] nodes)
    {
        children.AddRange(nodes);
    }

    public override BTState Tick()
    {
        for (int i = 0; i < children.Count; i++)
        {
            BTState result = children[i].Tick();
            if (result == BTState.Failure)
            {
                return BTState.Failure;
            }

            if (result == BTState.Running)
            {
                return BTState.Running;
            }
        }

        return BTState.Success;
    }
}
