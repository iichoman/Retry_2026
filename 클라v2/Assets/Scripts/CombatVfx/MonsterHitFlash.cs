using UnityEngine;

// Per-renderer material instances preserve the imported shader and original material properties.
public sealed class MonsterHitFlash : MonoBehaviour
{
    private struct Surface { public Material material; public string property; public Color color; }
    private Surface[] surfaces;
    private Material[] owned;
    private float until;
    private bool flashing;
    private void Awake()
    {
        var list=new System.Collections.Generic.List<Surface>();
        var materials=new System.Collections.Generic.List<Material>();
        foreach(var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
            foreach(var material in renderer.materials)
            {
                materials.Add(material);
                string property=material.HasProperty("_BaseColor")?"_BaseColor":material.HasProperty("_Color")?"_Color":null;
                if(property!=null)list.Add(new Surface{material=material,property=property,color=material.GetColor(property)});
            }
        }
        surfaces=list.ToArray();owned=materials.ToArray();
    }
    public static void Play(GameObject actor)
    {
        var view=actor.GetComponent<MonsterHitFlash>() ?? actor.AddComponent<MonsterHitFlash>();
        view.until=Time.unscaledTime+.14f;view.flashing=true;
        foreach(var s in view.surfaces)s.material.SetColor(s.property,new Color(2.5f,2.2f,1.6f,s.color.a));
    }
    private void Update(){if(flashing && Time.unscaledTime>=until)Restore();}
    private void Restore(){if(surfaces!=null)foreach(var s in surfaces)if(s.material!=null)s.material.SetColor(s.property,s.color);flashing=false;}
    private void OnDisable()=>Restore();
    private void OnDestroy(){if(owned!=null)foreach(var m in owned)if(m!=null)Destroy(m);}
}
