using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(400)]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WeaponSwingTrail : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform bladeBase;
    [SerializeField] private Transform bladeTip;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private ParticleSystem sparkParticles;
    [SerializeField] private Material trailMaterial;

    [Header("Shape")]
    [SerializeField, Min(0.02f)] private float trailDuration = 0.22f;
    [SerializeField, Range(4, 64)] private int maxSamples = 24;
    [SerializeField, Min(0.002f)] private float sampleInterval = 0.005f;
    [SerializeField, Range(1, 4)] private int smoothingSteps = 3;
    [SerializeField, Range(0.1f, 1.4f)] private float bladeWidthScale = 1f;
    [SerializeField] private AnimationCurve alphaOverLifetime = new AnimationCurve(
        new Keyframe(0f, 0.72f),
        new Keyframe(0.45f, 0.5f),
        new Keyframe(0.78f, 0.18f),
        new Keyframe(1f, 0f)
    );

    [Header("Color")]
    [SerializeField] private Color baseColor = new Color(1f, 0.86f, 0.22f, 0.78f);
    [SerializeField] private Color edgeColor = new Color(1f, 0.46f, 0.06f, 0.24f);

    private readonly List<BladeSample> samples = new List<BladeSample>(32);
    private readonly List<BladeSample> smoothedSamples = new List<BladeSample>(256);
    private readonly List<Vector3> vertices = new List<Vector3>(512);
    private readonly List<Vector2> uvs = new List<Vector2>(512);
    private readonly List<Color32> colors = new List<Color32>(512);
    private readonly List<int> triangles = new List<int>(1536);
    private Mesh trailMesh;
    private float nextSampleTime;
    private bool emitting;
    private bool serverSwing;
    private float serverEndTime;
    private float serverStartTime;
    private bool serverStarted;

    private void Awake()
    {
        Configure();
        ClearTrail();
    }

    private void OnEnable()
    {
        if(elementalFx!=null)elementalFx.SetActive(true);
        Configure();
    }

    private void OnValidate()
    {
        Configure();
        BuildMesh();
    }

    private GameObject elementalFx;
    private int elementHash;
    private ABActorView abActor;
    private Transform abWeapon;
    private bool abScaleKnown;
    private Renderer[] abWeaponRenderers;
    private bool[] abRendererEnabled;
    private Material[][] originalWeaponMaterials;
    private bool iceTrail;
    private MaterialPropertyBlock iceTint;
    private void UpdateElement(){
        if(abActor==null)abActor=GetComponentInParent<ABActorView>();
        int hash=abActor!=null ? abActor.WeaponHash : 0;
        if(hash==0&&!NetworkRuntime.IsOnline){var item=GetComponentInParent<PlayerEquipment>()?.GetEquippedItem(EquipmentSlotType.Weapon);if(item!=null)hash=ItemHash.Of(item.ItemId);}
        if(!abScaleKnown && GetComponentInParent<Weapon_Sword>() is var sword && sword!=null){
            abWeapon=sword.transform;abScaleKnown=true;
            abWeaponRenderers=abWeapon.GetComponentsInChildren<Renderer>(true);abRendererEnabled=new bool[abWeaponRenderers.Length];
            originalWeaponMaterials=new Material[abWeaponRenderers.Length][];
            for(int i=0;i<abWeaponRenderers.Length;i++){abRendererEnabled[i]=abWeaponRenderers[i].enabled;originalWeaponMaterials[i]=abWeaponRenderers[i].sharedMaterials;}
        }
        if(abScaleKnown && abActor!=null)abActor.ScaleWeapon(abWeapon);
        if(hash!=elementHash){
            string skinId=ABItemDefinitions.Find(hash)?.id;
            var skin=skinId==null?null:Resources.Load<Material>("AB/WeaponSkins/"+skinId);
            if(abWeaponRenderers!=null)for(int i=0;i<abWeaponRenderers.Length;i++){
                var renderer=abWeaponRenderers[i];
                if(renderer==null||renderer==meshRenderer||!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))continue;
                var materials=(Material[])originalWeaponMaterials[i].Clone();
                if(skin!=null)for(int j=0;j<materials.Length;j++)materials[j]=skin;
                renderer.sharedMaterials=materials;
            }
            if(elementalFx!=null)Destroy(elementalFx);elementHash=hash;
            if(abWeaponRenderers!=null)for(int i=0;i<abWeaponRenderers.Length;i++){
                var r=abWeaponRenderers[i];if(r!=null && r!=meshRenderer && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))r.enabled=hash!=ItemHash.Of("beam_sword") && abRendererEnabled[i];
            }
            int element = ABItemDefinitions.Find(hash)?.WeaponElement ?? 0;
            iceTrail = element == 2;
            if(meshRenderer!=null){
                if(iceTrail){
                    if(iceTint==null)iceTint=new MaterialPropertyBlock();
                    float opacity=trailMaterial!=null&&trailMaterial.HasProperty("_BaseColor")?trailMaterial.GetColor("_BaseColor").a:1;
                    iceTint.SetColor("_BaseColor",new Color(1,1,1,opacity));
                }
                meshRenderer.SetPropertyBlock(iceTrail?iceTint:null);
            }
            if(iceTrail)StopParticles(); // The ice prefab supplies matching frost particles.
            string resource = element == 1 ? "Flame" : element == 2 ? "Ice" : element == 3 ? "Beam" : null;
            if(resource!=null){var prefab=Resources.Load<GameObject>("AB/"+resource);if(prefab!=null)elementalFx=Instantiate(prefab,abActor!=null?abActor.transform:transform.root);}
        }
        if(elementalFx!=null && bladeBase!=null && bladeTip!=null){
            elementalFx.transform.SetPositionAndRotation(bladeBase.position,Quaternion.LookRotation((bladeTip.position-bladeBase.position).normalized));
            var line=elementalFx.GetComponent<LineRenderer>();line.SetPosition(0,bladeBase.position);line.SetPosition(1,bladeTip.position);
            var core=elementalFx.transform.Find("Beam Core");
            if(core!=null){float length=Vector3.Distance(bladeBase.position,bladeTip.position);core.localPosition=new Vector3(0,0,length*.5f);core.localScale=new Vector3(.035f,length*.5f,.035f);}
            var particles=elementalFx.GetComponent<ParticleSystem>();
            if(particles!=null){var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;float length=Vector3.Distance(bladeBase.position,bladeTip.position);shape.position=new Vector3(0,0,length*.5f);shape.scale=new Vector3(.04f,.04f,length);}
        }
    }
    private void LateUpdate()
    {
        UpdateElement();
        if (serverSwing && !serverStarted && Time.unscaledTime >= serverStartTime && Time.unscaledTime < serverEndTime)
        {
            serverStarted = true;
            BeginTrail();
        }
        if (serverSwing && Time.unscaledTime >= serverEndTime)
        {
            serverSwing = false;
            emitting = false;
            StopParticles();
        }
        UpdateSamples();

        if (emitting && Time.time >= nextSampleTime)
        {
            AddSample();
            nextSampleTime = Time.time + sampleInterval;
        }

        BuildMesh();
    }

    public void BeginTrail()
    {
        // Swing ribbon is cosmetic prediction. Online hit/damage remains server-owned.
        Configure();
        ClearTrail();
        emitting = true;
        nextSampleTime = 0f;
        AddSample();
        PlayParticles();
    }

    public void CancelSwing(){serverSwing=false;serverStarted=false;EndTrail();ClearTrail();}
    public void EndTrail()
    {
        if (NetworkRuntime.IsOnline && serverSwing) return;
        emitting = false;
        StopParticles();
    }

    /// <summary>Only call in response to an accepted PLAYER_ATTACK_BROADCAST.</summary>
    public void PlayServerSwing(float duration = 0.28f, float delay = 0f)
    {
        serverSwing = true;
        serverStarted = false;
        emitting = false;
        StopParticles();
        ClearTrail();
        serverStartTime = Time.unscaledTime + Mathf.Max(0f, delay);
        serverEndTime = serverStartTime + Mathf.Clamp(duration, 0.05f, 1.0f);
    }

    private void OnDisable()
    {
        if(elementalFx!=null)elementalFx.SetActive(false);
        serverSwing = false;
        emitting = false;
        StopParticles();
        ClearTrail();
    }

    private void OnDestroy()
    {
        if(elementalFx!=null)Destroy(elementalFx);
        if (trailMesh != null) Destroy(trailMesh);
    }

    private void Configure()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (trailMesh == null)
        {
            trailMesh = new Mesh
            {
                name = "Sword Swing Trail Mesh"
            };
            trailMesh.MarkDynamic();
        }

        if (meshFilter != null)
        {
            meshFilter.sharedMesh = trailMesh;
        }

        if (meshRenderer != null)
        {
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = samples.Count > 1;

            if (trailMaterial != null)
            {
                meshRenderer.sharedMaterial = trailMaterial;
            }
        }
    }

    private void UpdateSamples()
    {
        if (samples.Count == 0)
        {
            return;
        }

        float deltaTime = Application.isPlaying ? Time.deltaTime : 0f;
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            BladeSample sample = samples[i];
            sample.Age += deltaTime;

            if (sample.Age >= trailDuration)
            {
                samples.RemoveAt(i);
                continue;
            }

            samples[i] = sample;
        }
    }

    private void AddSample()
    {
        if (bladeBase == null || bladeTip == null)
        {
            return;
        }

        Vector3 basePosition = bladeBase.position;
        Vector3 tipPosition = bladeTip.position;
        Vector3 center = (basePosition + tipPosition) * 0.5f;
        Vector3 halfSpan = (tipPosition - basePosition) * 0.5f * bladeWidthScale;

        samples.Add(new BladeSample(center - halfSpan, center + halfSpan, 0f));

        while (samples.Count > maxSamples)
        {
            samples.RemoveAt(0);
        }
    }

    private void BuildMesh()
    {
        if (trailMesh == null)
        {
            return;
        }

        if (samples.Count < 2)
        {
            trailMesh.Clear();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }

            return;
        }

        List<BladeSample> renderSamples = BuildRenderSamples();
        if (renderSamples.Count < 2)
        {
            trailMesh.Clear();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }

            return;
        }

        vertices.Clear(); uvs.Clear(); colors.Clear(); triangles.Clear();

        for (int i = 0; i < renderSamples.Count; i++)
        {
            float normalizedAge = Mathf.Clamp01(renderSamples[i].Age / Mathf.Max(0.001f, trailDuration));
            float alpha = Mathf.Clamp01(alphaOverLifetime.Evaluate(normalizedAge));
            float u = i / (float)(renderSamples.Count - 1);
            vertices.Add(transform.InverseTransformPoint(renderSamples[i].BasePosition));
            vertices.Add(transform.InverseTransformPoint(renderSamples[i].TipPosition));
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));

            Color root = edgeColor;
            Color tip = baseColor;
            if(iceTrail){var tint=SkillPalette.ForWeapon(elementHash);root=new Color(tint.r,tint.g,tint.b,edgeColor.a);tip=new Color(tint.r,tint.g,tint.b,baseColor.a);}
            root.a *= alpha;
            tip.a *= alpha;
            colors.Add(root);
            colors.Add(tip);
        }

        for (int i = 0; i < renderSamples.Count - 1; i++)
        {
            int current = i * 2;
            int next = current + 2;

            triangles.Add(current); triangles.Add(next); triangles.Add(current + 1);
            triangles.Add(current + 1); triangles.Add(next); triangles.Add(next + 1);
        }

        trailMesh.Clear();
        trailMesh.SetVertices(vertices);
        trailMesh.SetUVs(0, uvs);
        trailMesh.SetColors(colors);
        trailMesh.SetTriangles(triangles, 0);
        trailMesh.RecalculateBounds();

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }
    }

    private void ClearTrail()
    {
        samples.Clear();

        if (trailMesh != null)
        {
            trailMesh.Clear();
        }

        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }
    }

    private List<BladeSample> BuildRenderSamples()
    {
        if (samples.Count < 3 || smoothingSteps <= 1)
        {
            return samples;
        }

        var renderSamples = smoothedSamples;
        renderSamples.Clear();
        for (int i = 0; i < samples.Count - 1; i++)
        {
            BladeSample previous = samples[Mathf.Max(0, i - 1)];
            BladeSample current = samples[i];
            BladeSample next = samples[i + 1];
            BladeSample nextNext = samples[Mathf.Min(samples.Count - 1, i + 2)];

            for (int step = 0; step < smoothingSteps; step++)
            {
                float t = step / (float)smoothingSteps;
                renderSamples.Add(new BladeSample(
                    CatmullRom(previous.BasePosition, current.BasePosition, next.BasePosition, nextNext.BasePosition, t),
                    CatmullRom(previous.TipPosition, current.TipPosition, next.TipPosition, nextNext.TipPosition, t),
                    Mathf.Lerp(current.Age, next.Age, t)
                ));
            }
        }

        renderSamples.Add(samples[samples.Count - 1]);
        return renderSamples;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void PlayParticles()
    {
        if (sparkParticles == null || iceTrail)
        {
            return;
        }

        sparkParticles.Clear(true);
        sparkParticles.Play(true);
    }

    private void StopParticles()
    {
        if (sparkParticles == null)
        {
            return;
        }

        sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private struct BladeSample
    {
        public BladeSample(Vector3 basePosition, Vector3 tipPosition, float age)
        {
            BasePosition = basePosition;
            TipPosition = tipPosition;
            Age = age;
        }

        public Vector3 BasePosition { get; }
        public Vector3 TipPosition { get; }
        public float Age { get; set; }
    }
}
