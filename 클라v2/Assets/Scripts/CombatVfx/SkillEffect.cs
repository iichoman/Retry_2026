using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Depth-tested, unlit holographic ribbons; gameplay shape comes from the server.</summary>
public sealed class SkillEffect : MonoBehaviour
{
    Mesh mesh; Material material; Vector3 direction, target;
    float expires, received, began, lifetime, area = 1;
    int weapon, mode; bool configured;
    static SkillEffect Create(string name, int mode, int weapon, float area, float duration)
    {
        var e = new GameObject(name).AddComponent<SkillEffect>();
        e.mode = mode; e.began = Time.unscaledTime; e.lifetime = duration; e.expires = e.began + duration;
        e.mesh = new Mesh { name = name + " ribbons" };
        e.gameObject.AddComponent<MeshFilter>().sharedMesh = e.mesh;
        e.material = new Material(Resources.Load<Shader>("SkillHologram"));
        var renderer = e.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = e.material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        e.Configure(weapon, area); return e;
    }
    public static SkillEffect CreateWave() => Create("E · spectral blade", 0, 0, 1, 1.5f);
    public void Flight(SkillEvent data)
    {
        Configure(data.weapon, data.area > 0 ? data.area : 1);
        target = new Vector3(data.x, data.y + 1.1f, data.z); direction = new Vector3(data.dx, 0, data.dz);
        received = Time.unscaledTime; expires = received + Mathf.Max(.15f, data.remaining);
        transform.SetPositionAndRotation(target, Quaternion.LookRotation(direction));
    }
    public static SkillEffect Charge(Vector3 position, Vector3 direction, int weapon = 0, float area = 1, float windup = .75f)
    {
        var e = Create("Q · gathering arc", 1, weapon, area, windup);
        e.transform.SetPositionAndRotation(position + Vector3.up * .08f, Quaternion.LookRotation(direction)); return e;
    }
    public static SkillEffect Slash(Vector3 position, Vector3 direction, int weapon = 0, float area = 1)
    {
        var e = Create("Q · spectral sweep", 2, weapon, area, .42f);
        e.transform.SetPositionAndRotation(position + Vector3.up * .85f, Quaternion.LookRotation(direction)); return e;
    }
    public void SetTint(Color tint){if(material!=null)material.SetColor("_BaseColor",tint);}
    void Configure(int hash, float scale)
    {
        if (configured && weapon == hash && Mathf.Approximately(area, scale)) return;
        configured = true; weapon = hash; area = scale;
        material.SetColor("_BaseColor", SkillPalette.ForWeapon(hash)); BuildMesh();
    }
    void BuildMesh()
    {
        var vertices = new List<Vector3>(); var colors = new List<Color>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        void Ribbon(System.Func<float,Vector3> curve, Vector3 width, float alpha, bool taper = true)
        {
            int start = vertices.Count; const int segments = 48;
            // Four rows give the translucent body a bright, fine edge without a solid fill.
            for (int i = 0; i <= segments; i++) {
                float t = i / (float)segments, envelope = taper ? Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.PI * t)), .55f) : 1;
                Vector3 p = curve(t); Vector3 w = width * Mathf.Max(.03f, envelope);
                for(int row=0;row<4;row++) {
                    float v=row/3f;vertices.Add(p+w*(v-.5f));uv.Add(new Vector2(t,v));
                    colors.Add(new Color(v,v,v,alpha*envelope));
                }
                if (i == segments) continue;
                for(int row=0;row<3;row++) {
                    int n=start+i*4+row;triangles.AddRange(new[]{n,n+1,n+4,n+4,n+1,n+5});
                }
            }
        }
        Vector3 Arc(float t, float radius, float height = 0) {
            float angle = Mathf.Lerp(-60,60,t) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle)*radius,height,Mathf.Cos(angle)*radius);
        }
        if (mode == 0) {
            Vector3 Blade(float t) => new Vector3(Mathf.Lerp(-1.08f,1.08f,t)*area,Mathf.Sin(t*Mathf.PI)*.22f,(Mathf.Sin(t*Mathf.PI)*.55f-.25f)*area);
            Ribbon(Blade,new Vector3(0,.12f,.4f)*area,.28f);
            Ribbon(t=>Blade(t)+Vector3.forward*.18f*area,Vector3.up*.045f,.85f);
            for(int j=1;j<=3;j++) { int index=j;
                Ribbon(t=>Blade(t)-Vector3.forward*(.24f+index*.22f)*area,new Vector3(0,.035f,.12f)*area,.20f/index);
            }
            for(int j=0;j<7;j++) { float t=(j+.5f)/7;
                Ribbon(u=>Blade(t)+new Vector3(0,.07f*Mathf.Sin(j*2),-.5f-u*.24f),Vector3.up*.025f,.28f,false);
            }
        } else if(mode == 1) {
            // A true sector fill (not an oval) keeps the visible warning inside the hit area.
            int start=vertices.Count;const int count=48;
            for(int i=0;i<=count;i++) {
                float t=i/(float)count;vertices.Add(Vector3.zero);vertices.Add(Arc(t,6*area));
                colors.Add(new Color(0,0,0,.025f));colors.Add(new Color(0,0,0,.10f));
                uv.Add(new Vector2(t,.5f));uv.Add(new Vector2(t,.5f));
                if(i<count){int n=start+i*2;triangles.AddRange(new[]{n,n+1,n+3});}
            }
            Ribbon(t=>Arc(t,6*area),Vector3.forward*.055f,.58f,false);
            Ribbon(t=>Arc(t,5.8f*area),Vector3.forward*.018f,.20f,false);
            for(int side=0;side<2;side++) { int edge=side;
                Ribbon(t=>Arc(edge,Mathf.Lerp(.35f,6*area,t)),Vector3.right*.045f,.32f);
            }
            for(int j=0;j<=12;j++) { float t=j/12f;
                Ribbon(u=>Arc(t,(5.82f+u*.18f)*area),Vector3.right*.025f,.40f,false);
            }
        } else {
            Ribbon(t=>Arc(t,5.55f*area,.2f*Mathf.Sin(t*Mathf.PI)),Vector3.forward*(.85f*area),.28f);
            Ribbon(t=>Arc(t,5.96f*area,.2f*Mathf.Sin(t*Mathf.PI)),Vector3.up*.075f,.85f);
            Ribbon(t=>Arc(t,5.25f*area),Vector3.up*.025f,.32f);
            Ribbon(t=>Arc(t,4.7f*area,-.14f),Vector3.forward*.22f,.12f);
            for(int j=0;j<11;j++) { float t=(j+.5f)/11;
                Ribbon(u=>Arc(t,(5.3f+u*.8f)*area,.04f+(j%3)*.07f),Vector3.up*.035f,.35f);
            }
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
    }
    // Used by isolated editor renders as well; no physics or gameplay side effects.
    public void SampleVisual(float progress)
    {
        progress = Mathf.Clamp01(progress);
        if(mode==1)material.SetFloat("_Opacity",Mathf.Lerp(.45f,1,progress));
        else if(mode==2) {
            transform.localScale=Vector3.one*Mathf.Lerp(.84f,1,Mathf.Sin(progress*Mathf.PI*.5f));
            material.SetFloat("_Opacity",Mathf.Pow(1-progress,1.2f));
        }
    }
    void Update()
    {
        if(Time.unscaledTime>=expires){Destroy(gameObject);return;}
        if(mode==0)transform.position=target+direction*Mathf.Min(Time.unscaledTime-received,.1f)*22;
        else SampleVisual((Time.unscaledTime-began)/lifetime);
    }
    void OnDestroy()
    {
        if(Application.isPlaying){if(mesh!=null)Destroy(mesh);if(material!=null)Destroy(material);}
        else {if(mesh!=null)DestroyImmediate(mesh);if(material!=null)DestroyImmediate(material);}
    }
}
