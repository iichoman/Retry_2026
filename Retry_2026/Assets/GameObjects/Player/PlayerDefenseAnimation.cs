using UnityEngine;

// Presentation only: controller movement, stamina and server authority remain unchanged.
[DefaultExecutionOrder(100), DisallowMultipleComponent]
public sealed class PlayerDefenseAnimation : MonoBehaviour
{
    public const string RollState = "DefenseRoll", ParryState = "DefenseParry", GreatswordParryState = "GreatswordParry";
    private Animator animator;
    private PlayerDefenseController defense;
    private Transform hips;
    private Transform leftArm, rightArm;
    private Vector3 beforePosition, writtenPosition, clipForwardLocal;
    private bool captureForward;
    private Quaternion before, written;
    private bool rotated;
    private string active;
    private int parryLayer=-1;
    public bool IsPlaying => active != null;
    private void Awake()
    {
        animator=GetComponent<Animator>(); defense=GetComponent<PlayerDefenseController>();
        if(animator!=null) parryLayer=animator.GetLayerIndex("Defense Upper Body");
        if(animator!=null && animator.isHuman)
        {
            hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            leftArm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
    }
    private void Restore()
    {
        if(rotated && hips!=null && Quaternion.Angle(hips.rotation,written)<.01f) hips.rotation=before;
        if(rotated && hips!=null && (hips.position-writtenPosition).sqrMagnitude<.00000001f) hips.position=beforePosition;
        rotated=false;
    }
    private void Update()
    {
        Restore();
        if(animator==null || defense==null) return;
        // Ordinary sword retains its procedural guard; greatsword uses its authored support-hand clip.
        string next=defense.IsDashing ? RollState : defense.IsParrying && GetComponent<Player_Attack>()?.IsGreatswordEquipped==true ? GreatswordParryState : null;
        int layer=next==ParryState || next==GreatswordParryState ? parryLayer : 0;
        if(next!=null && (layer<0 || !animator.HasState(layer,Animator.StringToHash(next)))) next=null;
        if(parryLayer>=0) animator.SetLayerWeight(parryLayer,(next==ParryState||next==GreatswordParryState)?1:0);
        if(next!=null)
        {
            if(active!=RollState) captureForward=true;
            animator.applyRootMotion=false;
            // Sample against actual defense duration, including cancellation. No animation events deal damage.
            animator.Play(next,layer,next==GreatswordParryState ? Mathf.Min(.65f,defense.ActionProgress/.3f*.65f) : Mathf.Min(.999f,defense.ActionProgress));
        }
        else if(active!=null && animator.GetCurrentAnimatorStateInfo(0).IsName(active))
            animator.CrossFadeInFixedTime("Blend Tree",.08f);
        active=next;
    }
    private void LateUpdate()
    {
        if(active!=RollState || hips==null) return;
        Restore();
        // The retargeted clip's forward can differ from the gameplay root's forward.
        // Measure its initial shoulder axis once, before the somersault turns it over.
        if(captureForward)
        {
            Vector3 forward=leftArm!=null && rightArm!=null
                ? Vector3.Cross(rightArm.position-leftArm.position,Vector3.up) : transform.forward;
            forward=Vector3.ProjectOnPlane(forward,Vector3.up);
            clipForwardLocal=transform.InverseTransformDirection(forward.sqrMagnitude>.0001f?forward.normalized:transform.forward);
            captureForward=false;
        }
        Vector3 direction=Vector3.ProjectOnPlane(defense.DashDirection,Vector3.up);
        if(direction.sqrMagnitude<.0001f) return;
        float yaw=Vector3.SignedAngle(transform.TransformDirection(clipForwardLocal),direction,Vector3.up);
        var correction=Quaternion.AngleAxis(yaw,Vector3.up);
        before=hips.rotation; written=correction*before;
        beforePosition=hips.position;
        // Rotate the entire visual skeleton about the actor, not just the pelvis orientation.
        writtenPosition=transform.position+correction*(beforePosition-transform.position);
        hips.position=writtenPosition;
        hips.rotation=written; rotated=true;
    }
    private void OnDisable() { Restore(); if(animator!=null && parryLayer>=0) animator.SetLayerWeight(parryLayer,0); active=null; }
}
