using UnityEngine;

[DefaultExecutionOrder(110)]
public sealed class PlayerSkillView : MonoBehaviour
{
    public bool IsCasting=>active && Time.unscaledTime<until;
    public bool IsGreatswordQ => IsCasting && skill == 4;
    public static bool Busy(GameObject actor)=>actor.GetComponent<PlayerSkillView>()?.IsCasting==true;
    public static bool BlocksMovement(GameObject actor){var view=actor.GetComponent<PlayerSkillView>();return view!=null&&view.IsCasting&&view.skill!=5;}
    GreatswordSkillEffect greatEffect;Transform hips;Quaternion hipsBase;bool rotated;
    bool Spin=>skill==3||skill==5;
    Animator animator;int serial=-1,skill;float began,until,windup;bool active;Vector3 direction;SkillEffect warning;
    void Awake(){animator=GetComponentInChildren<Animator>();if(animator!=null&&animator.isHuman)hips=animator.GetBoneTransform(HumanBodyBones.Hips);if(hips==null)hips=GetComponentInChildren<SkinnedMeshRenderer>()?.rootBone;}
    public void Begin(SkillEvent data)
    {
        if(data.serial<=serial)return;Cancel();serial=data.serial;skill=data.skill;
        windup=data.windup>0?data.windup:skill==1?.18f:.75f;
        float total=windup+.45f;began=Time.unscaledTime-(total-data.remaining);until=Time.unscaledTime+data.remaining;
        active=true;direction=new Vector3(data.dx,0,data.dz);
        GetComponent<Player_Attack>()?.CancelCurrentAttack();
        transform.rotation=Quaternion.LookRotation(direction);
        if(skill==4&&data.remaining>.45f)greatEffect=GreatswordSkillEffect.Descent(new Vector3(data.x,data.y,data.z),data.area,data.remaining-.45f,data.weapon);
        if(Spin&&data.remaining>.45f)greatEffect=GreatswordSkillEffect.Spin(transform,data.area,data.remaining-.45f,data.weapon);
        if(skill==2&&data.remaining>.45f)warning=SkillEffect.Charge(transform.position,direction,data.weapon,data.area>0?data.area:1,data.remaining-.45f);
    }
    void Update()
    {
        RestoreSpin();
        var state=GetComponent<Player_State>();
        if(active && (!IsCasting || state!=null&&state.IsDead || GetComponent<PlayerDefenseController>()?.IsBusy==true)){Cancel();return;}
        if(!IsCasting || animator==null)return;
        transform.rotation=Quaternion.LookRotation(direction);
        float age=Time.unscaledTime-began;
        float progress=age<windup?Mathf.Lerp(0,.2f,Mathf.Clamp01(age/windup)):Mathf.Lerp(.2f,.98f,Mathf.Clamp01((age-windup)/.45f));
        if(Spin)progress=.35f;
        string name=skill==1?"SkillE":"SkillQ";
        if(animator.HasState(0,Animator.StringToHash(name))){animator.applyRootMotion=false;animator.Play(name,0,progress);}
        if(warning!=null && age>=windup){Release(warning.gameObject);warning=null;}
    }
    void LateUpdate(){if(IsCasting&&Spin&&hips!=null){hipsBase=hips.localRotation;hips.rotation=Quaternion.AngleAxis(Mathf.Clamp01((Time.unscaledTime-began)/windup)*1080,Vector3.up)*hips.rotation;rotated=true;}}
    void RestoreSpin(){if(rotated&&hips!=null)hips.localRotation=hipsBase;rotated=false;}
    public void Cancel(){
        RestoreSpin();if(greatEffect!=null)Release(greatEffect.gameObject);greatEffect=null;
        if(warning!=null)Release(warning.gameObject);
        if(active&&animator!=null){var info=animator.GetCurrentAnimatorStateInfo(0);if(info.IsName("SkillE")||info.IsName("SkillQ"))animator.CrossFadeInFixedTime("Blend Tree",.08f);}
        active=false;
    }
    static void Release(Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    void OnDisable()=>Cancel();
}
