using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Targeted content update: preserve animation clips, boss content and user scene composition.
public static class DuelCombatContentBuilder
{
    [MenuItem("Retry/Player/Build Duel Combat Effects")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode");
        var catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>("Assets/Resources/CombatVfxCatalog.asset");
        if (catalog == null || catalog.ParticleMaterial == null || !catalog.ParticleMaterial.shader.isSupported)
            throw new InvalidOperationException("Missing supported combat particle material");
        ApplyEffects(catalog);
        foreach (string path in new[] { "Assets/Prefabs/LocalPlayerPrefab.prefab", "Assets/Prefabs/RemotePlayerPrefab.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { TuneTrails(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        const string scenePath = "Assets/Scenes/SampleScene.unity";
        var scene = SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.isLoaded;
        if (!opened && scene.isDirty) throw new InvalidOperationException("Save SampleScene first");
        if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects()) TuneTrails(root);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/DuelCombat");
        File.WriteAllText("Artifacts/DuelCombat/assets.txt", "PASS: pooled DodgeDust and SwordHit particle prefabs, existing URP material, catalog entries, local/remote/scene sword trails connected. No animation clips or server files changed.\n");
    }

    public static void ApplyEffects(CombatVfxCatalog catalog)
    {
        var entries = new List<VfxEntry>(catalog.Effects);
        SetEffect(entries, CombatVfxKind.DodgeDust, BuildDust(catalog));
        SetEffect(entries, CombatVfxKind.SwordHit, BuildImpact(catalog));
        catalog.Effects = entries.ToArray(); EditorUtility.SetDirty(catalog);
    }
    public static void BuildSwordImpact()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>("Assets/Resources/CombatVfxCatalog.asset");
        if(catalog==null || catalog.ParticleMaterial==null) throw new InvalidOperationException("Missing VFX catalog/material");
        var entries=new List<VfxEntry>(catalog.Effects);
        SetEffect(entries,CombatVfxKind.SwordHit,BuildImpact(catalog));
        catalog.Effects=entries.ToArray(); EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
    }

    static void SetEffect(List<VfxEntry> entries, CombatVfxKind kind, PooledCombatEffect prefab)
    {
        entries.RemoveAll(e => e.Kind == kind);
        entries.Add(new VfxEntry { Kind = kind, Prefab = prefab });
    }
    static PooledCombatEffect BuildDust(CombatVfxCatalog catalog)
    {
        var root = new GameObject("DodgeDust");
        var effect = root.AddComponent<PooledCombatEffect>(); effect.Lifetime = .65f;
        var dust = Particle(root, "Low ground dust", catalog.ParticleMaterial, new Color(.62f,.56f,.46f,.4f), .55f, .32f, .9f, 20);
        dust.transform.localRotation = Quaternion.Euler(-65,180,0);
        var shape = dust.shape; shape.radius = .24f; shape.angle = 38;
        var noise = dust.noise; noise.enabled = true; noise.strength = .12f; noise.frequency = .5f; noise.quality = ParticleSystemNoiseQuality.Low;
        var flecks = Particle(root, "Ground flecks", catalog.ParticleMaterial, new Color(.73f,.66f,.54f,.55f), .35f, .045f, 1.5f, 12);
        flecks.transform.localRotation = Quaternion.Euler(-65,180,0);
        var main = flecks.main; main.gravityModifier = .7f;
        effect.Particles = new[] { dust, flecks }; effect.BurstCounts = new[] { 14, 7 };
        return Save(root, "DodgeDust");
    }
    static PooledCombatEffect BuildImpact(CombatVfxCatalog catalog)
    {
        // Reuse the existing asset GUID so offline receivers and server-confirmed impacts both get the update.
        var root = new GameObject("SwordHit");
        var effect = root.AddComponent<PooledCombatEffect>(); effect.Lifetime = .48f;
        var sparks = Particle(root,"Short gold sparks",catalog.ParticleMaterial,new Color(1,.73f,.25f,.95f),.27f,.045f,3.6f,24);
        var render = sparks.GetComponent<ParticleSystemRenderer>(); render.renderMode = ParticleSystemRenderMode.Stretch;
        render.lengthScale = 2.5f; render.velocityScale = .05f;
        var flash = Particle(root,"Contact flash",catalog.ParticleMaterial,new Color(1,.92f,.68f,1f),.20f,.65f,.05f,3);
        var dust = Particle(root,"Contact dust",catalog.ParticleMaterial,new Color(.55f,.46f,.36f,.26f),.4f,.22f,.45f,8);
        effect.Particles = new[] { sparks, flash, dust }; effect.BurstCounts = new[] { 18, 1, 5 };
        return Save(root, "SwordHit");
    }
    static ParticleSystem Particle(GameObject parent, string name, Material material, Color color, float life, float size, float speed, int max)
    {
        var go = new GameObject(name); go.transform.SetParent(parent.transform,false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake=false; main.loop=false; main.duration=.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life*.65f,life);
        main.startSize = new ParticleSystem.MinMaxCurve(size*.55f,size);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed*.35f,speed);
        main.startColor=color; main.simulationSpace=ParticleSystemSimulationSpace.World; main.maxParticles=max;
        var emission = ps.emission; emission.rateOverTime=0; emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
        var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=65; shape.radius=.06f;
        var fade=ps.colorOverLifetime; fade.enabled=true; var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.75f,.25f),new GradientAlphaKey(0,1)});
        fade.color=gradient;
        var shrink=ps.sizeOverLifetime; shrink.enabled=true; shrink.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,.3f));
        var renderer=ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        return ps;
    }
    static PooledCombatEffect Save(GameObject root,string name)
    {
        root.SetActive(false);
        try { return PrefabUtility.SaveAsPrefabAsset(root,"Assets/CombatContent/Prefabs/"+name+".prefab").GetComponent<PooledCombatEffect>(); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void TuneTrails(GameObject root)
    {
        foreach(var trail in root.GetComponentsInChildren<WeaponSwingTrail>(true))
        {
            var so=new SerializedObject(trail);
            so.FindProperty("trailDuration").floatValue=.16f;
            so.FindProperty("baseColor").colorValue=new Color(1,.93f,.66f,.86f);
            so.FindProperty("edgeColor").colorValue=new Color(1,.53f,.12f,.12f);
            so.ApplyModifiedPropertiesWithoutUndo();
            if(so.FindProperty("bladeBase").objectReferenceValue==null || so.FindProperty("bladeTip").objectReferenceValue==null
                || so.FindProperty("trailMaterial").objectReferenceValue==null) throw new InvalidOperationException("Unwired sword trail: "+root.name);
        }
    }
}
