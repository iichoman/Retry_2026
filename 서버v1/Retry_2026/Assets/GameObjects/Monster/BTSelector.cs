using System.Collections.Generic;

public class BTSelector : BTNode
{
    private readonly List<BTNode> children = new List<BTNode>();

    public BTSelector(params BTNode[] nodes)
    {
        children.AddRange(nodes);
    }

    public override BTState Tick()
    {
        for (int i = 0; i < children.Count; i++)
        {
            BTState result = children[i].Tick();
            if (result == BTState.Success || result == BTState.Running)
            {
                return result;
            }
        }

        return BTState.Failure;
    }
}
