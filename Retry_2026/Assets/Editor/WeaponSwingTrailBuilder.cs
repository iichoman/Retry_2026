using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WeaponSwingTrailBuilder
{
    private const string TrailObjectName = "Sword Swing Trail";
    private const string BladeBaseName = "Blade Trail Base";
    private const string BladeTipName = "Blade Trail Tip";
    private const string SparkObjectName = "Gold Sparks";
    private const string MaterialFolder = "Assets/GameObjects/Player/Player_Attack/Materials";
    private const string TrailMaterialPath = MaterialFolder + "/Sword_Swing_Trail.mat";
    private const string SparkMaterialPath = MaterialFolder + "/Sword_Swing_Sparks.mat";

    [MenuItem("Tools/Retry/Weapons/Add Sword Swing Trail To Selected")]
    public static void AddSwordSwingTrailToSelected()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("Select a weapon object or WeaponHitbox first.");
            return;
        }

        WeaponHitbox hitbox = selected.GetComponent<WeaponHitbox>();
        if (hitbox == null)
        {
            hitbox = selected.GetComponentInChildren<WeaponHitbox>(true);
        }

        if (hitbox == null)
        {
            Debug.LogWarning("No WeaponHitbox was found on the selected object or its children.", selected);
            return;
        }

        Material trailMaterial = CreateOrUpdateTrailMaterial();
        Material sparkMaterial = CreateOrUpdateSparkMaterial();
        WeaponSwingTrail swingTrail = FindOrCreateTrail(hitbox, trailMaterial, sparkMaterial);
        RegisterTrail(hitbox, swingTrail);

        Selection.activeGameObject = swingTrail.gameObject;
        EditorUtility.SetDirty(hitbox);
        EditorUtility.SetDirty(swingTrail);
        EditorSceneManager.MarkSceneDirty(hitbox.gameObject.scene);

        Debug.Log("Sword swing trail was added. Move the 'Sword Swing Trail' child to the blade tip/edge position.", swingTrail);
    }

    [MenuItem("Tools/Retry/Weapons/Add Sword Swing Trail To Selected", true)]
    private static bool ValidateAddSwordSwingTrailToSelected()
    {
        return Selection.activeGameObject != null;
    }

    private static WeaponSwingTrail FindOrCreateTrail(WeaponHitbox hitbox, Material trailMaterial, Material sparkMaterial)
    {
        Transform existing = hitbox.transform.Find(TrailObjectName);
        GameObject trailObject;
        if (existing != null)
        {
            trailObject = existing.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(trailObject, "Update Sword Swing Trail");
        }
        else
        {
            trailObject = new GameObject(TrailObjectName);
            Undo.RegisterCreatedObjectUndo(trailObject, "Create Sword Swing Trail");
            trailObject.transform.SetParent(hitbox.transform, false);
            trailObject.transform.localPosition = Vector3.zero;
            trailObject.transform.localRotation = Quaternion.identity;
            trailObject.transform.localScale = Vector3.one;
        }

        DisableLegacyTrailRenderer(trailObject);

        MeshFilter meshFilter = trailObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = trailObject.AddComponent<MeshFilter>();
        }

        MeshRenderer meshRenderer = trailObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = trailObject.AddComponent<MeshRenderer>();
        }

        ConfigureRenderer(meshRenderer, trailMaterial);

        Transform bladeBase = FindOrCreateChild(trailObject.transform, BladeBaseName, new Vector3(0f, 0f, 0f));
        Transform bladeTip = FindOrCreateChild(trailObject.transform, BladeTipName, new Vector3(0f, 0f, 0.85f));
        ParticleSystem sparks = FindOrCreateSparks(trailObject.transform, sparkMaterial);

        WeaponSwingTrail swingTrail = trailObject.GetComponent<WeaponSwingTrail>();
        if (swingTrail == null)
        {
            swingTrail = trailObject.AddComponent<WeaponSwingTrail>();
        }

        AssignObject(swingTrail, "bladeBase", bladeBase);
        AssignObject(swingTrail, "bladeTip", bladeTip);
        AssignObject(swingTrail, "meshFilter", meshFilter);
        AssignObject(swingTrail, "meshRenderer", meshRenderer);
        AssignObject(swingTrail, "sparkParticles", sparks);
        AssignObject(swingTrail, "trailMaterial", trailMaterial);
        return swingTrail;
    }

    private static Transform FindOrCreateChild(Transform parent, string name, Vector3 localPosition)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            return existing;
        }

        GameObject child = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(child, $"Create {name}");
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return child.transform;
    }

    private static void DisableLegacyTrailRenderer(GameObject trailObject)
    {
        TrailRenderer legacyRenderer = trailObject.GetComponent<TrailRenderer>();
        if (legacyRenderer == null)
        {
            return;
        }

        legacyRenderer.emitting = false;
        legacyRenderer.enabled = false;
        legacyRenderer.Clear();
    }

    private static void ConfigureRenderer(MeshRenderer renderer, Material material)
    {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
    }

    private static ParticleSystem FindOrCreateSparks(Transform parent, Material material)
    {
        Transform existing = parent.Find(SparkObjectName);
        GameObject sparkObject;
        if (existing != null)
        {
            sparkObject = existing.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(sparkObject, "Update Sword Swing Sparks");
        }
        else
        {
            sparkObject = new GameObject(SparkObjectName);
            Undo.RegisterCreatedObjectUndo(sparkObject, "Create Sword Swing Sparks");
            sparkObject.transform.SetParent(parent, false);
            sparkObject.transform.localPosition = new Vector3(0f, 0f, 0.72f);
            sparkObject.transform.localRotation = Quaternion.identity;
            sparkObject.transform.localScale = Vector3.one;
        }

        ParticleSystem particles = sparkObject.GetComponent<ParticleSystem>();
        if (particles == null)
        {
            particles = sparkObject.AddComponent<ParticleSystem>();
        }

        ConfigureSparkParticles(particles, material);
        return particles;
    }

    private static void ConfigureSparkParticles(ParticleSystem particles, Material material)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 0.45f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.88f, 0.28f, 0.85f),
            new Color(1f, 0.48f, 0.08f, 0.45f)
        );
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 18f;

        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.025f;

        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.88f, 0.22f), 0f),
                new GradientColorKey(new Color(1f, 0.4f, 0.04f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0.35f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.65f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f, 0.15f)
        ));

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingOrder = 40;
        renderer.sharedMaterial = material;
    }

    private static void RegisterTrail(WeaponHitbox hitbox, WeaponSwingTrail swingTrail)
    {
        SerializedObject serializedHitbox = new SerializedObject(hitbox);
        SerializedProperty trails = serializedHitbox.FindProperty("swingTrails");
        if (trails == null)
        {
            return;
        }

        for (int i = 0; i < trails.arraySize; i++)
        {
            if (trails.GetArrayElementAtIndex(i).objectReferenceValue == swingTrail)
            {
                serializedHitbox.ApplyModifiedProperties();
                return;
            }
        }

        int index = trails.arraySize;
        trails.InsertArrayElementAtIndex(index);
        trails.GetArrayElementAtIndex(index).objectReferenceValue = swingTrail;
        serializedHitbox.ApplyModifiedProperties();
    }

    private static Material CreateOrUpdateTrailMaterial()
    {
        EnsureMaterialFolder();

        Material material = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
        if (material == null)
        {
            material = new Material(FindTrailShader())
            {
                name = "Sword_Swing_Trail"
            };
            AssetDatabase.CreateAsset(material, TrailMaterialPath);
        }

        material.shader = FindTrailShader();
        ApplyTransparentMaterialSettings(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static Material CreateOrUpdateSparkMaterial()
    {
        EnsureMaterialFolder();

        Material material = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath);
        if (material == null)
        {
            material = new Material(FindTrailShader())
            {
                name = "Sword_Swing_Sparks"
            };
            AssetDatabase.CreateAsset(material, SparkMaterialPath);
        }

        material.shader = FindTrailShader();
        ApplySparkMaterialSettings(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static Shader FindTrailShader()
    {
        return
            Shader.Find("Retry/VFX/Sword Trail Unlit") ??
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color");
    }

    private static void ApplyTransparentMaterialSettings(Material material)
    {
        Color color = new Color(1f, 0.78f, 0.18f, 0.68f);

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Intensity"))
        {
            material.SetFloat("_Intensity", 1.35f);
        }

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.enableInstancing = true;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private static void ApplySparkMaterialSettings(Material material)
    {
        Color color = new Color(1f, 0.74f, 0.18f, 0.85f);

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 1f);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Intensity"))
        {
            material.SetFloat("_Intensity", 1.65f);
        }

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.enableInstancing = true;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private static void EnsureMaterialFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialFolder))
        {
            return;
        }

        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
    }

    private static void AssignObject(Object target, string fieldName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property != null)
        {
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
