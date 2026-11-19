using UnityEngine;

// Offline prototype only; online poison uses the existing AB status system.
public sealed class MummyPoison : MonoBehaviour
{
    float remaining,tick;
    GameObject source,visual;
    public void Apply(GameObject attacker)
    {
        if(NetworkRuntime.IsOnline)return;
        remaining=5;source=attacker;
        if(visual==null){var prefab=Resources.Load<GameObject>("AB/Poison");if(prefab!=null)visual=Instantiate(prefab,transform);}
    }
    void Update()
    {
        var health=GetComponent<Player_State>();
        if(NetworkRuntime.IsOnline||health==null||health.IsDead){Destroy(this);return;}
        float active=Mathf.Min(Time.deltaTime,remaining);remaining-=active;tick+=active;
        while(tick>=.9999f){tick-=1;health.TakeDamage(2,source);}
        if(remaining<=0)Destroy(this);
    }
    void OnDestroy(){if(visual!=null)Destroy(visual);}
}
