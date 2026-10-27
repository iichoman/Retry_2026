public enum BTState
{
    Success,
    Failure,
    Running
}

public abstract class BTNode
{
    public abstract BTState Tick();
}
