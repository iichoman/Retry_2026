using UnityEngine;

[DisallowMultipleComponent]
public class StylizedTorchFlame : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform flameRoot;
    [SerializeField] private ParticleSystem flameCore;
    [SerializeField] private ParticleSystem flameGlow;
    [SerializeField] private ParticleSystem sparks;
    [SerializeField] private ParticleSystem smoke;
    [SerializeField] private Light torchLight;

    [Header("Materials")]
    [SerializeField] private Material flameMaterial;
    [SerializeField] private Material smokeMaterial;

    [Header("Placement")]
    [SerializeField] private Vector3 flameLocalPosition = new Vector3(0f, 1.72f, -1.91f);
    [SerializeField] private Vector3 lightLocalPosition = new Vector3(0f, 2.2f, -1.75f);

    [Header("Light")]
    [SerializeField] private Color lightColor = new Color(1f, 0.54f, 0.18f, 1f);
    [SerializeField, Min(0f)] private float baseIntensity = 2.2f;
    [SerializeField, Min(0f)] private float flickerIntensity = 0.55f;
    [SerializeField, Min(0.1f)] private float lightRange = 9f;
    [SerializeField, Min(0f)] private float flickerSpeed = 9f;

    [Header("Motion")]
    [SerializeField, Min(0f)] private float flameScalePulse = 0.08f;
    [SerializeField, Min(0f)] private float flameSway = 0.035f;

    private Vector3 baseFlameScale = Vector3.one;
    private float seed;
    private static Material defaultFlameMaterial;
    private static Material defaultSmokeMaterial;

    private void Awake()
    {
        seed = Random.value * 100f;
        EnsureEffectObjects();
        ConfigureEffect();
    }

    private void Update()
    {
        if (torchLight == null)
        {
            return;
        }

        float time = Time.time * flickerSpeed + seed;
        float noise = Mathf.PerlinNoise(time, seed) - 0.5f;
        float wave = Mathf.Sin(time * 1.7f) * 0.5f;
        float flicker = noise + wave * 0.25f;

        torchLight.intensity = Mathf.Max(0f, baseIntensity + flicker * flickerIntensity);

        if (flameRoot != null)
        {
            float pulse = 1f + flicker * flameScalePulse;
            flameRoot.localScale = new Vector3(baseFlameScale.x * (1f - flameSway * flicker), baseFlameScale.y * pulse, baseFlameScale.z);
            flameRoot.localPosition = flameLocalPosition + new Vector3(flicker * flameSway, 0f, 0f);
        }
    }

    [ContextMenu("Rebuild Stylized Torch Effect")]
    public void RebuildEffect()
    {
        EnsureEffectObjects();
        ConfigureEffect();
    }

    public void RebuildEffect(bool clearExistingGeneratedObjects)
    {
        if (clearExistingGeneratedObjects)
        {
            ClearGeneratedObjects();
        }

        RebuildEffect();
    }

    public void SetMaterials(Material newFlameMaterial, Material newSmokeMaterial)
    {
        flameMaterial = newFlameMaterial;
        smokeMaterial = newSmokeMaterial;
        ConfigureEffect();
    }

    private void ClearGeneratedObjects()
    {
        Transform existing = transform.Find("Stylized Flame");
        if (existing != null)
        {
            DestroyGeneratedObject(existing.gameObject);
        }

        flameRoot = null;
        flameCore = null;
        flameGlow = null;
        sparks = null;
        smoke = null;
    }

    private static void DestroyGeneratedObject(Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private void EnsureEffectObjects()
    {
        if (flameRoot == null)
        {
            Transform existing = transform.Find("Stylized Flame");
            flameRoot = existing != null ? existing : CreateChild("Stylized Flame").transform;
        }

        if (flameRoot.parent != transform)
        {
            flameRoot.SetParent(transform, false);
        }

        flameRoot.localPosition = flameLocalPosition;
        flameRoot.localRotation = Quaternion.identity;
        flameRoot.localScale = Vector3.one;
        baseFlameScale = flameRoot.localScale;

        flameCore = EnsureParticleSystem(flameRoot, flameCore, "Flame Core");
        flameGlow = EnsureParticleSystem(flameRoot, flameGlow, "Flame Glow");
        sparks = EnsureParticleSystem(flameRoot, sparks, "Sparks");
        smoke = EnsureParticleSystem(flameRoot, smoke, "Soft Smoke");

        if (torchLight == null)
        {
            torchLight = GetComponentInChildren<Light>(true);
        }

        if (torchLight == null)
        {
            GameObject lightObject = CreateChild("Stylized Torch Light");
            lightObject.transform.SetParent(transform, false);
            torchLight = lightObject.AddComponent<Light>();
        }

        torchLight.transform.localPosition = lightLocalPosition;
        torchLight.transform.localRotation = Quaternion.identity;
    }

    private void ConfigureEffect()
    {
        ConfigureCore(flameCore);
        ConfigureGlow(flameGlow);
        ConfigureSparks(sparks);
        ConfigureSmoke(smoke);
        ConfigureLight();

        PlayIfConfigured(flameCore);
        PlayIfConfigured(flameGlow);
        PlayIfConfigured(sparks);
        PlayIfConfigured(smoke);
    }

    private void ConfigureLight()
    {
        if (torchLight == null)
        {
            return;
        }

        torchLight.type = LightType.Point;
        torchLight.color = lightColor;
        torchLight.intensity = baseIntensity;
        torchLight.range = lightRange;
        torchLight.shadows = LightShadows.None;
    }

    private static ParticleSystem EnsureParticleSystem(Transform parent, ParticleSystem current, string name)
    {
        if (current != null)
        {
            return current;
        }

        Transform existing = parent.Find(name);
        if (existing != null && existing.TryGetComponent(out ParticleSystem existingSystem))
        {
            return existingSystem;
        }

        GameObject obj = new GameObject(name);
        obj.SetActive(false);
        obj.transform.SetParent(parent, false);
        return obj.AddComponent<ParticleSystem>();
    }

    private static GameObject CreateChild(string name)
    {
        return new GameObject(name);
    }

    private void ConfigureCore(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        StopForConfiguration(system);

        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.58f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.35f, 1f), new Color(1f, 0.48f, 0.08f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 80;

        var emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 45f;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.06f;
        shape.position = Vector3.zero;

        var color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildGradient(
            new Color(1f, 1f, 0.52f, 1f),
            new Color(1f, 0.52f, 0.08f, 0.82f),
            new Color(1f, 0.12f, 0f, 0f)
        ));

        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f, 0.08f)
        ));

        ConfigureRenderer(system, 20, flameMaterial != null ? flameMaterial : GetDefaultFlameMaterial());
    }

    private void ConfigureGlow(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        StopForConfiguration(system);

        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.42f, 0.72f);
        main.startColor = new Color(1f, 0.42f, 0.06f, 0.42f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 45;

        var emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 20f;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.08f;

        var color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildGradient(
            new Color(1f, 0.55f, 0.1f, 0.35f),
            new Color(1f, 0.22f, 0.02f, 0.18f),
            new Color(1f, 0.08f, 0f, 0f)
        ));

        ConfigureRenderer(system, 10, flameMaterial != null ? flameMaterial : GetDefaultFlameMaterial());
    }

    private void ConfigureSparks(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        StopForConfiguration(system);

        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.25f, 1f), new Color(1f, 0.35f, 0.05f, 1f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 7f;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 24f;
        shape.radius = 0.04f;

        var color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildGradient(
            new Color(1f, 0.9f, 0.25f, 1f),
            new Color(1f, 0.45f, 0.05f, 0.7f),
            new Color(1f, 0.2f, 0f, 0f)
        ));

        ConfigureRenderer(system, 30, flameMaterial != null ? flameMaterial : GetDefaultFlameMaterial());
    }

    private void ConfigureSmoke(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        StopForConfiguration(system);

        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.startColor = new Color(0.22f, 0.18f, 0.16f, 0.16f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 30;

        var emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 4f;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.05f;

        var color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildGradient(
            new Color(0.18f, 0.14f, 0.12f, 0.08f),
            new Color(0.28f, 0.24f, 0.22f, 0.14f),
            new Color(0.28f, 0.24f, 0.22f, 0f)
        ));

        ConfigureRenderer(system, 0, smokeMaterial != null ? smokeMaterial : GetDefaultSmokeMaterial());
    }

    private static void ConfigureRenderer(ParticleSystem system, int sortingOrder, Material material)
    {
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = sortingOrder;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
    }

    private static void StopForConfiguration(ParticleSystem system)
    {
        system.gameObject.SetActive(false);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static void PlayIfConfigured(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        system.gameObject.SetActive(true);
        system.Play(true);
    }

    private static Gradient BuildGradient(Color start, Color middle, Color end)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(middle, 0.45f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(middle.a, 0.55f),
                new GradientAlphaKey(end.a, 1f)
            }
        );
        return gradient;
    }

    private static Material GetDefaultFlameMaterial()
    {
        if (defaultFlameMaterial != null)
        {
            return defaultFlameMaterial;
        }

        defaultFlameMaterial = CreateDefaultParticleMaterial("Generated Torch Flame Material", new Color(1f, 0.48f, 0.08f, 1f));
        return defaultFlameMaterial;
    }

    private static Material GetDefaultSmokeMaterial()
    {
        if (defaultSmokeMaterial != null)
        {
            return defaultSmokeMaterial;
        }

        defaultSmokeMaterial = CreateDefaultParticleMaterial("Generated Torch Smoke Material", new Color(0.22f, 0.18f, 0.16f, 0.35f));
        return defaultSmokeMaterial;
    }

    private static Material CreateDefaultParticleMaterial(string name, Color color)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        var material = new Material(shader)
        {
            name = name,
            color = color
        };

        material.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        return material;
    }
}
