using UnityEngine;

// Repeated network snapshots must not erase the delayed damage portion.
public sealed class HealthTrail
{
    public float Current { get; private set; } = 1;
    public float Delayed { get; private set; } = 1;
    float hold;
    bool initialized;
    public void Set(float value)
    {
        value=Mathf.Clamp01(value);
        if(!initialized||value>Current){Delayed=value;hold=0;}
        else if(value<Current)hold=.4f;
        Current=value;initialized=true;
    }
    public void Advance(float seconds)
    {
        if(hold>0){float held=Mathf.Min(hold,seconds);hold-=held;seconds-=held;}
        if(seconds>0)Delayed=Mathf.MoveTowards(Delayed,Current,seconds*.65f);
    }
}
