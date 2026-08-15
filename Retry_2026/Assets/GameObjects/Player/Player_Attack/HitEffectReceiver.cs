using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HitEffectReceiver : MonoBehaviour
{
    [SerializeField] private List<HitEffectEntry> effects = new List<HitEffectEntry>
    {
        new HitEffectEntry { type = HitEffectType.Generic }
    };
    [SerializeField] private float surfaceOffset = 0.02f;
    [SerializeField] private bool parentToReceiver;
    [SerializeField] private bool useBuiltInFallback = true;

    public void Play(HitEffectType type, Vector3 position, Vector3 normal)
    {
        Vector3 resolvedNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : transform.forward;
        Vector3 spawnPosition = position + resolvedNormal * surfaceOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(resolvedNormal, Vector3.up);
        Transform parent = parentToReceiver ? transform : null;

        GameObject prefab = ResolvePrefab(type);
        if (prefab != null)
        {
            Instantiate(prefab, spawnPosition, spawnRotation, parent);
            return;
        }

        if (useBuiltInFallback)
        {
            PlayBuiltInEffect(type, spawnPosition, spawnRotation, parent);
        }
    }

    public static void PlayBuiltInEffect(HitEffectType type, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        GameObject effectRoot = new GameObject($"{type} Hit Effect");
        effectRoot.transform.SetPositionAndRotation(position, rotation);

        if (parent != null)
        {
            effectRoot.transform.SetParent(parent, true);
        }

        effectRoot.AddComponent<HitEffectAutoDestroy>();
        CreateImpactFlash(effectRoot.transform, type);
        CreateSparkBurst(effectRoot.transform, type);
        CreateDustPuff(effectRoot.transform, type);
    }

    private static void CreateImpactFlash(Transform parent, HitEffectType type)
    {
        GameObject flashObject = new GameObject("Impact Flash");
        flashObject.transform.SetParent(parent, false);

        ParticleSystem particles = CreateStoppedParticleSystem(flashObject);
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.08f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.045f, 0.08f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startSize = type == HitEffectType.Bullet
            ? new ParticleSystem.MinMaxCurve(0.12f, 0.22f)
            : new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
        main.startColor = GetFlashColor(type);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 8;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(3f, 5f))
        });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.015f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildAlphaFadeGradient(
            new Color(1f, 0.92f, 0.42f, 0.78f),
            new Color(1f, 0.42f, 0.08f, 0f)
        ));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.15f),
            new Keyframe(0.18f, 1f),
            new Keyframe(1f, 0.25f)
        ));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingOrder = 40;
        renderer.sharedMaterial = GetSparkParticleMaterial();

        particles.Play(true);
    }

    private GameObject ResolvePrefab(HitEffectType type)
    {
        GameObject fallback = null;

        for (int i = 0; i < effects.Count; i++)
        {
            HitEffectEntry entry = effects[i];
            if (entry == null || entry.prefab == null)
            {
                continue;
            }

            if (entry.type == type)
            {
                return entry.prefab;
            }

            if (entry.type == HitEffectType.Generic)
            {
                fallback = entry.prefab;
            }
        }

        return fallback;
    }

    private static void CreateSparkBurst(Transform parent, HitEffectType type)
    {
        GameObject sparkObject = new GameObject("Impact Sparks");
        sparkObject.transform.SetParent(parent, false);

        ParticleSystem particles = CreateStoppedParticleSystem(sparkObject);
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.18f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
        main.startColor = GetSparkColor(type);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = type == HitEffectType.Bullet ? 0.1f : 0.18f;
        main.maxParticles = 48;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(14f, 26f))
        });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 42f;
        shape.radius = 0.025f;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildAlphaFadeGradient(
            new Color(1f, 0.86f, 0.22f, 0.95f),
            new Color(1f, 0.35f, 0.05f, 0f)
        ));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.65f),
            new Keyframe(0.25f, 1f),
            new Keyframe(1f, 0.08f)
        ));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 2.6f;
        renderer.minParticleSize = 0.005f;
        renderer.maxParticleSize = 0.08f;
        renderer.sortingOrder = 30;
        renderer.sharedMaterial = GetSparkParticleMaterial();

        particles.Play(true);
    }

    private static void CreateDustPuff(Transform parent, HitEffectType type)
    {
        GameObject dustObject = new GameObject("Impact Puff");
        dustObject.transform.SetParent(parent, false);

        ParticleSystem particles = CreateStoppedParticleSystem(dustObject);
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.34f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.42f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.36f);
        main.startColor = GetPuffColor(type);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 24;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(8f, 14f))
        });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 65f;
        shape.radius = 0.06f;

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        noise.frequency = 0.55f;
        noise.scrollSpeed = 0.35f;

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildAlphaFadeGradient(
            new Color(0.75f, 0.58f, 0.34f, 0.26f),
            new Color(0.38f, 0.3f, 0.24f, 0f)
        ));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingOrder = 20;
        renderer.sharedMaterial = GetDustParticleMaterial();

        particles.Play(true);
    }

    private static ParticleSystem CreateStoppedParticleSystem(GameObject target)
    {
        ParticleSystem particles = target.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static ParticleSystem.MinMaxGradient GetSparkColor(HitEffectType type)
    {
        return type == HitEffectType.Bullet
            ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.45f, 0.95f), new Color(1f, 0.45f, 0.08f, 0.75f))
            : new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.28f, 0.95f), new Color(1f, 0.52f, 0.08f, 0.72f));
    }

    private static ParticleSystem.MinMaxGradient GetFlashColor(HitEffectType type)
    {
        return type == HitEffectType.Bullet
            ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.96f, 0.56f, 0.82f), new Color(1f, 0.62f, 0.16f, 0.48f))
            : new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.38f, 0.78f), new Color(1f, 0.36f, 0.06f, 0.42f));
    }

    private static ParticleSystem.MinMaxGradient GetPuffColor(HitEffectType type)
    {
        return type == HitEffectType.Bullet
            ? new ParticleSystem.MinMaxGradient(new Color(0.58f, 0.52f, 0.42f, 0.24f), new Color(0.32f, 0.28f, 0.22f, 0.12f))
            : new ParticleSystem.MinMaxGradient(new Color(0.82f, 0.62f, 0.34f, 0.22f), new Color(0.46f, 0.34f, 0.24f, 0.1f));
    }

    private static Gradient BuildAlphaFadeGradient(Color start, Color end)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(start, 0f),
                new GradientColorKey(end, 1f)
            },
            new[]
            {
                new GradientAlphaKey(start.a, 0f),
                new GradientAlphaKey(end.a, 1f)
            }
        );
        return gradient;
    }

    private static Material sparkParticleMaterial;
    private static Material dustParticleMaterial;
    private static Texture2D softParticleTexture;

    private static Material GetSparkParticleMaterial()
    {
        if (sparkParticleMaterial != null)
        {
            return sparkParticleMaterial;
        }

        sparkParticleMaterial = CreateParticleMaterial("Generated Hit Effect Spark Material", true);
        return sparkParticleMaterial;
    }

    private static Material GetDustParticleMaterial()
    {
        if (dustParticleMaterial != null)
        {
            return dustParticleMaterial;
        }

        dustParticleMaterial = CreateParticleMaterial("Generated Hit Effect Dust Material", false);
        return dustParticleMaterial;
    }

    private static Material CreateParticleMaterial(string materialName, bool additive)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Transparent") ??
            Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = materialName,
            color = Color.white,
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };

        Texture2D texture = GetSoftParticleTexture();
        SetTextureIfAvailable(material, "_BaseMap", texture);
        SetTextureIfAvailable(material, "_MainTex", texture);
        SetColorIfAvailable(material, "_BaseColor", Color.white);
        SetColorIfAvailable(material, "_Color", Color.white);

        if (additive)
        {
            SetFloatIfAvailable(material, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIfAvailable(material, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        }
        else
        {
            SetFloatIfAvailable(material, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIfAvailable(material, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        SetFloatIfAvailable(material, "_ZWrite", 0f);
        SetFloatIfAvailable(material, "_Surface", 1f);
        SetFloatIfAvailable(material, "_Blend", additive ? 2f : 0f);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        return material;
    }

    private static void SetTextureIfAvailable(Material material, string propertyName, Texture texture)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetTexture(propertyName, texture);
        }
    }

    private static void SetColorIfAvailable(Material material, string propertyName, Color color)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }

    private static void SetFloatIfAvailable(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static Texture2D GetSoftParticleTexture()
    {
        if (softParticleTexture != null)
        {
            return softParticleTexture;
        }

        const int size = 64;
        softParticleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Generated Soft Hit Particle Texture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float distance = Mathf.Sqrt(u * u + v * v);
                float alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.18f, 1f, distance));
                alpha *= 0.75f + 0.25f * Mathf.Sin((u * 17f + v * 11f) * Mathf.PI);
                alpha = Mathf.Clamp01(alpha);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        softParticleTexture.SetPixels(pixels);
        softParticleTexture.Apply(false, true);
        return softParticleTexture;
    }

    [Serializable]
    private sealed class HitEffectEntry
    {
        public HitEffectType type;
        public GameObject prefab;
    }
}
