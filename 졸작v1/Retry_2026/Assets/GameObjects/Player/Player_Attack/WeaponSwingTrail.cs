using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
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
    private Mesh trailMesh;
    private float nextSampleTime;
    private bool emitting;

    private void Awake()
    {
        Configure();
        ClearTrail();
    }

    private void OnEnable()
    {
        Configure();
    }

    private void OnValidate()
    {
        Configure();
        BuildMesh();
    }

    private void LateUpdate()
    {
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
        Configure();
        ClearTrail();
        emitting = true;
        nextSampleTime = 0f;
        AddSample();
        PlayParticles();
    }

    public void EndTrail()
    {
        emitting = false;
        StopParticles();
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

            if (!emitting && sample.Age >= trailDuration)
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

        int vertexCount = renderSamples.Count * 2;
        var vertices = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var colors = new Color32[vertexCount];
        var triangles = new int[(renderSamples.Count - 1) * 6];

        for (int i = 0; i < renderSamples.Count; i++)
        {
            float normalizedAge = Mathf.Clamp01(renderSamples[i].Age / Mathf.Max(0.001f, trailDuration));
            float alpha = Mathf.Clamp01(alphaOverLifetime.Evaluate(normalizedAge));
            float u = i / (float)(renderSamples.Count - 1);
            int baseIndex = i * 2;

            vertices[baseIndex] = transform.InverseTransformPoint(renderSamples[i].BasePosition);
            vertices[baseIndex + 1] = transform.InverseTransformPoint(renderSamples[i].TipPosition);
            uvs[baseIndex] = new Vector2(u, 0f);
            uvs[baseIndex + 1] = new Vector2(u, 1f);

            Color root = edgeColor;
            Color tip = baseColor;
            root.a *= alpha;
            tip.a *= alpha;
            colors[baseIndex] = root;
            colors[baseIndex + 1] = tip;
        }

        int triangleIndex = 0;
        for (int i = 0; i < renderSamples.Count - 1; i++)
        {
            int current = i * 2;
            int next = current + 2;

            triangles[triangleIndex++] = current;
            triangles[triangleIndex++] = next;
            triangles[triangleIndex++] = current + 1;
            triangles[triangleIndex++] = current + 1;
            triangles[triangleIndex++] = next;
            triangles[triangleIndex++] = next + 1;
        }

        trailMesh.Clear();
        trailMesh.vertices = vertices;
        trailMesh.uv = uvs;
        trailMesh.colors32 = colors;
        trailMesh.triangles = triangles;
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

        var renderSamples = new List<BladeSample>(samples.Count * smoothingSteps);
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
        if (sparkParticles == null)
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
