using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct MummyBreathState
{
    public int version,monsterId,serial,phase;
    public float x,y,z,dx,dz,elapsed;
}

// Presentation only. All networked damage/poison comes from Session_Manager.
public sealed class MummyBreathView : MonoBehaviour
{
    public const float ChargeWindup=.6f,ChargeDuration=.8f,ChargeSpeed=7.5f;
    public const float Windup=.9f,Lifetime=1.8f,Speed=2.7f,TrailLength=1.5f;
    [SerializeField] Material gasMaterial;
    [SerializeField] Transform mouth;
    ParticleSystem cloud;
    readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[48];
    MummyBreathState state;
    float receivedAt;
    int lastSerial=-1;
    public int Phase=>state.phase;
    public float Age=>state.elapsed+(Application.isPlaying?Time.unscaledTime-receivedAt:0);
    public Vector3 Origin=>new Vector3(state.x,state.y,state.z);
    public Vector3 Direction=>new Vector3(state.dx,0,state.dz);
    public static float Front(float age)=>.6f+Speed*age;
    public static float Radius(float age)=>.45f+.35f*age;
    public static bool Contains(Vector3 origin,Vector3 direction,float age,Vector3 point)
    {
        if(Mathf.Abs(point.y-origin.y)>2)return false;
        var offset=point-origin;offset.y=0;float along=Vector3.Dot(offset,direction);
        float front=Front(age),rear=Mathf.Max(.25f,front-TrailLength);
        return along>=0&&(offset-direction*Mathf.Clamp(along,rear,front)).sqrMagnitude<=Mathf.Pow(Radius(age)+.3f,2);
    }
    public void Apply(MummyBreathState value)
    {
        if(value.serial<lastSerial)return;
        bool begin=value.phase>0&&value.serial!=lastSerial;
        int previousPhase=state.phase;
        state=value;receivedAt=Time.unscaledTime;lastSerial=value.serial;
        if(begin) {
            GetComponent<Monster_Attack>()?.NetworkPlayAttack();
            if(value.phase==2){var animator=GetComponentInChildren<Animator>();animator?.Play("Attack",0,Mathf.Clamp01((Windup+value.elapsed)/(Windup+Lifetime)));}
        }
        if(value.phase>=3&&(begin||previousPhase!=value.phase)){
            var animator=GetComponentInChildren<Animator>();
            animator?.Play(value.phase==3?"ChargeWindup":"Charge",0,value.phase==3?0:Mathf.Clamp01(value.elapsed/ChargeDuration));
        }
        if(value.phase==0&&previousPhase>=3){GetComponent<Monster_Attack>()?.CancelAttack();var animator=GetComponentInChildren<Animator>();if(GetComponent<Monster_State>()?.IsDead!=true)animator?.CrossFadeInFixedTime("Locomotion",.08f);}
        Render();
    }
    public void Stop(){state.phase=0;if(cloud!=null)cloud.Clear();}
    void Update()
    {
        if(GetComponent<Monster_State>()?.IsDead==true){Stop();return;}
        if((state.phase==2&&Age>=Lifetime)||(state.phase==4&&Age>=ChargeDuration+.3f)){Stop();return;}
        Render();
    }
    void OnDisable()=>Stop();
    public void Render()
    {
        if(state.phase==0||state.phase>=3){if(cloud!=null)cloud.Clear();return;}
        if(cloud==null) {
            var go=new GameObject("Mummy poison breath");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,gameObject.scene);go.transform.SetParent(transform,false);cloud=go.AddComponent<ParticleSystem>();
            cloud.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=cloud.main;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=48;main.startSpeed=0;
            var emission=cloud.emission;emission.enabled=false;var shape=cloud.shape;shape.enabled=false;
            var renderer=cloud.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=gasMaterial;renderer.sortMode=ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            cloud.Play();cloud.Pause();
        }
        float age=Age;int count=state.phase==1?12:48;
        var right=Vector3.Cross(Vector3.up,Direction);
        for(int i=0;i<count;i++) {
            float angle=i*2.39996f+age*.7f;
            Vector3 position;float size,alpha;
            if(state.phase==1) {
                position=(mouth!=null?mouth.position:Origin+Vector3.up*1.55f+Direction*.2f)+right*Mathf.Sin(angle)*.13f+Vector3.up*Mathf.Cos(angle)*.10f;
                size=.16f+Mathf.Clamp01(age/Windup)*.19f;alpha=.35f;
            } else {
                float fraction=i/(float)(count-1),front=Front(age),rear=Mathf.Max(.25f,front-TrailLength),radius=Radius(age);
                position=Origin+Direction*Mathf.Lerp(rear,front,fraction)+Vector3.up*(1.25f+Mathf.Cos(angle)*radius*.42f)+right*Mathf.Sin(angle)*radius*.55f;
                size=radius*(.9f+.5f*Mathf.Sin(i*7.3f)*Mathf.Sin(i*7.3f));
                alpha=.38f*Mathf.Clamp01((Lifetime-age)/.3f);
            }
            particles[i]=new ParticleSystem.Particle {position=position,startSize=size,startColor=Color.Lerp(new Color(.28f,.48f,.08f,alpha),new Color(.73f,.90f,.22f,alpha),i%3*.5f),startLifetime=10,remainingLifetime=10,randomSeed=(uint)(i+1),rotation=i*47+age*20};
        }
        cloud.SetParticles(particles,count);cloud.Pause();
    }
}
