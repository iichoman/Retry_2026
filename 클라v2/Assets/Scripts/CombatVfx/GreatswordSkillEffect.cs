using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Presentation only: damage and cast timing are owned by the session server.
public sealed class GreatswordSkillEffect : MonoBehaviour
{
    readonly List<LineRenderer> rings=new();
    readonly List<Transform> sparks=new();
    Material glow,steel,gold,element;
    Color elementColor;Mesh bladeMesh;Transform blade;
    float began,duration,radius;int mode;
    static GreatswordSkillEffect Create(Vector3 point,float radius,int mode,float duration,int weapon=0){
        var fx=new GameObject("Greatsword - "+(mode==0?"target":mode==1?"judgement descent":mode==2?"judgement impact":"whirlwind")).AddComponent<GreatswordSkillEffect>();
        fx.transform.position=point;fx.radius=radius;fx.mode=mode;fx.duration=duration;fx.began=Time.unscaledTime;
        fx.glow=new Material(Resources.Load<Shader>("SkillHologram"));fx.glow.SetColor("_BaseColor",mode<2?new Color(1,.08f,.04f):new Color(1,.68f,.14f));
        fx.elementColor=SkillPalette.ForWeapon(weapon);fx.element=new Material(fx.glow);fx.element.SetColor("_BaseColor",fx.elementColor);
        fx.Ring(radius,.075f,0);fx.Ring(radius*.94f,.025f,.015f);
        if(mode==1||mode==2){
            fx.MakeSword();fx.Ring(radius*.6f,.045f,.025f);
            for(int strand=0;strand<2;strand++){var points=new Vector3[65];for(int j=0;j<65;j++){float a=j*.18f+strand*Mathf.PI;points[j]=new Vector3(Mathf.Cos(a)*.9f,j*.115f,Mathf.Sin(a)*.9f);}var ribbon=fx.Line(points,.065f);ribbon.sharedMaterial=fx.element;ribbon.transform.SetParent(fx.blade,false);}
            for(int i=0;i<12;i++){
                float a=i*Mathf.PI/6;var dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                fx.Line(new[]{dir*(radius*.86f),dir*(radius*1.04f)},.045f);
            }
        }
        if(mode==2){
            fx.Ring(radius*.45f,.24f,.08f);
            for(int i=0;i<24;i++){
                float a=i*2.399963f;var dir=new Vector3(Mathf.Cos(a),.6f+(i%5)*.28f,Mathf.Sin(a));
                var line=fx.Line(new[]{Vector3.zero,dir.normalized*.35f},.05f);fx.sparks.Add(line.transform);line.transform.localPosition=dir;
            }
            fx.glow.SetColor("_BaseColor",fx.elementColor);
            foreach(var kind in new[]{CombatVfxKind.SlamImpact,CombatVfxKind.Shockwave}){var burst=PooledVfxSpawner.Play(kind,point,Quaternion.identity,radius*.65f);if(burst!=null){burst.Color=fx.elementColor;burst.SetProgress(0);
                if(burst.Particles!=null&&burst.Particles.Length>0&&burst.Particles[0]!=null){var ps=burst.Particles[0];var particles=new ParticleSystem.Particle[ps.main.maxParticles];int count=ps.GetParticles(particles);for(int i=0;i<count;i++)particles[i].startColor=fx.elementColor;ps.SetParticles(particles,count);}}}
        }
        if(mode==3){fx.glow.SetColor("_BaseColor",fx.elementColor);fx.Ring(radius*.8f,.11f,.8f);fx.Ring(radius,.055f,1.1f);}
        fx.Sample(0);return fx;
    }
    public static GreatswordSkillEffect Marker(Vector3 point,float radius)=>Create(point,radius,0,float.PositiveInfinity);
    public static GreatswordSkillEffect Descent(Vector3 point,float area,float remaining,int weapon=0)=>Create(point-Vector3.up*.01f,3.5f*area,1,remaining,weapon);
    public static GreatswordSkillEffect Impact(Vector3 point,float area,int weapon=0)=>Create(point-Vector3.up*.01f,3.5f*area,2,1.25f,weapon);
    public static GreatswordSkillEffect Spin(Transform actor,float area,float remaining,int weapon=0){var fx=Create(actor.position+Vector3.up*.02f,3.5f*area,3,remaining,weapon);fx.transform.SetParent(actor,true);return fx;}
    LineRenderer Line(Vector3[] points,float width){
        var line=new GameObject("Energy ribbon").AddComponent<LineRenderer>();line.transform.SetParent(transform,false);
        line.useWorldSpace=false;line.positionCount=points.Length;line.SetPositions(points);line.widthMultiplier=width;
        line.sharedMaterial=glow;line.startColor=line.endColor=Color.white;line.numCapVertices=2;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;return line;
    }
    void Ring(float size,float width,float y){var points=new Vector3[97];for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2/96;points[i]=new Vector3(Mathf.Cos(a)*size,y,Mathf.Sin(a)*size);}rings.Add(Line(points,width));}
    void MakeSword(){
        blade=new GameObject("Celestial greatsword").transform;blade.SetParent(transform,false);
        steel=new Material(Shader.Find("Universal Render Pipeline/Lit"));steel.SetColor("_BaseColor",elementColor);steel.SetFloat("_Metallic",.05f);steel.SetFloat("_Smoothness",.8f);steel.EnableKeyword("_EMISSION");steel.SetColor("_EmissionColor",elementColor*2f);
        gold=new Material(steel);gold.SetColor("_BaseColor",elementColor);gold.SetColor("_EmissionColor",elementColor*2f);
        bladeMesh=new Mesh{name="Faceted colossal blade"};
        bladeMesh.vertices=new[]{Vector3.zero,new Vector3(-.65f,1.7f,0),new Vector3(.65f,1.7f,0),new Vector3(-.65f,6.4f,0),new Vector3(.65f,6.4f,0),new Vector3(0,1.7f,.24f),new Vector3(0,6.4f,.24f),new Vector3(0,1.7f,-.24f),new Vector3(0,6.4f,-.24f)};
        bladeMesh.triangles=new[]{0,1,5,0,5,2,1,3,6,1,6,5,5,6,4,5,4,2,0,7,1,0,2,7,1,8,3,1,7,8,7,4,8,7,2,4,3,8,6,4,6,8};bladeMesh.RecalculateNormals();bladeMesh.RecalculateBounds();
        blade.gameObject.AddComponent<MeshFilter>().sharedMesh=bladeMesh;blade.gameObject.AddComponent<MeshRenderer>().sharedMaterial=steel;
        Piece(new Vector3(0,6.4f,0),new Vector3(2.6f,.32f,.55f));Piece(new Vector3(0,7.2f,0),new Vector3(.3f,1.4f,.3f));Piece(new Vector3(0,7.95f,0),Vector3.one*.5f);
        var edge=Line(new[]{Vector3.zero,new Vector3(-.65f,1.7f,0),new Vector3(-.65f,6.4f,0),new Vector3(.65f,6.4f,0),new Vector3(.65f,1.7f,0),Vector3.zero},.08f);edge.sharedMaterial=gold;edge.transform.SetParent(blade,false);
    }
    void Piece(Vector3 position,Vector3 size){var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name="Sword fitting";part.transform.SetParent(blade,false);part.transform.localPosition=position;part.transform.localScale=size;var collider=part.GetComponent<Collider>();collider.enabled=false;if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);part.GetComponent<Renderer>().sharedMaterial=gold;}
    public void Sample(float age){
        float t=mode==0?0:Mathf.Clamp01(age/duration);
        if(mode==0){glow.SetFloat("_Opacity",.7f+.2f*Mathf.Sin(age*6));return;}
        if(mode==1){blade.localPosition=Vector3.up*(12*(1-Mathf.Pow(Mathf.Clamp01((t-.55f)/.45f),2)));blade.localScale=Vector3.one*Mathf.Lerp(.7f,1,t);glow.SetFloat("_Opacity",.55f+.45f*t);}
        if(mode==2){blade.localPosition=Vector3.down*.25f;blade.localScale=Vector3.one*(1-.2f*t);glow.SetFloat("_Opacity",1-t);for(int i=0;i<rings.Count;i++)rings[i].transform.localScale=Vector3.one*(1+t*(1+i*.3f));for(int i=0;i<sparks.Count;i++){float a=i*2.399963f;float speed=2+i%5;sparks[i].localPosition=new Vector3(Mathf.Cos(a)*speed*age,(3+i%5)*age-4*age*age,Mathf.Sin(a)*speed*age);}}
        if(mode==3){glow.SetFloat("_Opacity",.45f+.25f*Mathf.Sin(age*20));foreach(var ring in rings)ring.transform.localRotation=Quaternion.Euler(6*Mathf.Sin(age*7),age*600,6*Mathf.Cos(age*7));}
    }
    void Update(){float age=Time.unscaledTime-began;if(age>=duration){Destroy(gameObject);return;}Sample(age);}
    void OnDestroy(){Release(glow);Release(steel);Release(gold);Release(element);Release(bladeMesh);}
    static void Release(Object o){if(o==null)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
}
