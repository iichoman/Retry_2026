using UnityEngine;

// The imported generic rig animates below the gameplay root.
public sealed class KnightAnimationEvents : MonoBehaviour
{
    private Monster_Attack attack;
    private void Awake() => attack = GetComponentInParent<Monster_Attack>();
    public void AnimEvent_BeginSwing()
    {
        if (attack != null && attack.isActiveAndEnabled && attack.GetComponent<KnightHeavyAttack>()?.IsActive!=true) attack.AnimEvent_BeginSwing();
    }
    public void AnimEvent_EndSwing() => attack?.AnimEvent_EndSwing();
}
