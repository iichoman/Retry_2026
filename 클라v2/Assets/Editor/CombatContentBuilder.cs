using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Idempotent asset generation, executed by the integration build. No imported downloads.</summary>
public static class CombatContentBuilder
{
    private const string Root = "Assets/CombatContent";
    private const string ResourcePath = "Assets/Resources/CombatVfxCatalog.asset";
    private static readonly Color Gold = new Color(1f, 0.64f, 0.12f, 0.88f);
    private static readonly Color Red = new Color(1f, 0.15f, 0.05f, 0.85f);

    [MenuItem("Tools/Retry/Combat/Build All Assets")]
    public static void BuildAll()
    {
        EnsureFolder(Root + "/Materials"); EnsureFolder(Root + "/Meshes"); EnsureFolder(Root + "/Textures");
        EnsureFolder(Root + "/Prefabs"); EnsureFolder("Assets/Resources");
        var catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>(ResourcePath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<CombatVfxCatalog>(); AssetDatabase.CreateAsset(catalog, ResourcePath); }
        Shader fxShader = Shader.Find("Retry/VFX/Combat Unlit");
        if (fxShader == null) throw new InvalidOperationException("Required project URP Sword Trail Unlit shader missing.");
        catalog.GroundMaterial = MaterialAsset("Guardian_Ground", fxShader, Color.white);
        catalog.TrailMaterial = MaterialAsset("Guardian_Trail", fxShader, Color.white);
        catalog.ParticleMaterial = MaterialAsset("Guardian_Particles", fxShader, Color.white);
        catalog.ParticleMaterial.SetTexture("_BaseMap", SoftTexture());
        catalog.GroundMaterial.SetFloat("_Intensity", 1.1f); catalog.TrailMaterial.SetFloat("_Intensity", 1.3f);
        catalog.ParticleMaterial.SetFloat("_Intensity", 1.2f);
        catalog.RingMesh = RingMesh();
        catalog.LootProfiles = new[]
        {
            new LootVisualProfile { Label = "Common", Color = new Color(0.75f,0.78f,0.80f), BeamHeight = 0.55f, ParticleCount = 6 },
            new LootVisualProfile { Label = "Uncommon", Color = new Color(0.22f,0.86f,0.36f), BeamHeight = 0.80f, ParticleCount = 10 },
            new LootVisualProfile { Label = "Rare", Color = new Color(0.15f,0.55f,1f), BeamHeight = 1.1f, ParticleCount = 14 },
            new LootVisualProfile { Label = "Epic", Color = new Color(0.72f,0.27f,1f), BeamHeight = 1.4f, ParticleCount = 20 },
            new LootVisualProfile { Label = "Boss", Color = Gold, BeamHeight = 1.9f, ParticleCount = 28 }
        };
        var entries = new List<VfxEntry>();
        foreach (CombatVfxKind kind in Enum.GetValues(typeof(CombatVfxKind))) entries.Add(new VfxEntry { Kind = kind, Prefab = EffectPrefab(kind, catalog) });
        catalog.Effects = entries.ToArray();
        DuelCombatContentBuilder.ApplyEffects(catalog);
        catalog.TelegraphPrefab = TelegraphPrefab(catalog);
        catalog.LootPrefab = LootPrefab(catalog);
        catalog.ProjectilePrefab = ProjectilePrefab(catalog);
        catalog.BossPrefab = BossPrefab(catalog);
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        ValidateAssets();
        Debug.Log($"COMBAT_CONTENT_BUILD_OK: generated {catalog.Effects.Length} pooled effects, boss, telegraph, loot, projectile and URP materials.");
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    private static Material MaterialAsset(string name, Shader shader, Color color)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader; material.SetColor("_BaseColor", color); material.enableInstancing = true;
        EditorUtility.SetDirty(material); return material;
    }
    private static Texture2D SoftTexture()
    {
        string path = Root + "/Textures/SoftParticle.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture != null) return texture;
        const int size = 64; texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftParticle", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            float alpha = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v)); alpha *= alpha;
            pixels[y * size + x] = new Color(1, 1, 1, alpha);
        }
        texture.SetPixels(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture, path); return texture;
    }
    private static Mesh RingMesh()
    {
        string path = Root + "/Meshes/UnitRadiusRing.asset"; var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        const int n = 64; mesh = new Mesh { name = "Unit Radius Ring (outer radius 1m)" };
        var vertices = new Vector3[n * 2]; var colors = new Color[n * 2]; var indices = new int[n * 6];
        for (int i = 0; i < n; ++i)
        {
            float a = i * 2f * Mathf.PI / n; var p = new Vector3(Mathf.Cos(a), 0.02f, Mathf.Sin(a));
            vertices[i * 2] = p; vertices[i * 2 + 1] = p * 0.94f; colors[i * 2] = colors[i * 2 + 1] = Color.white;
            int j = (i + 1) % n; int at = i * 6;
            indices[at] = i * 2; indices[at + 1] = j * 2; indices[at + 2] = i * 2 + 1;
            indices[at + 3] = i * 2 + 1; indices[at + 4] = j * 2; indices[at + 5] = j * 2 + 1;
        }
        mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = indices; mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    private static PooledCombatEffect EffectPrefab(CombatVfxKind kind, CombatVfxCatalog catalog)
    {
        var go = new GameObject(kind.ToString()); var effect = go.AddComponent<PooledCombatEffect>();
        bool large = kind <= CombatVfxKind.PhaseChange || kind == CombatVfxKind.BossDeath;
        bool ring = large || kind == CombatVfxKind.LootSpawn || kind == CombatVfxKind.LootRemove || kind == CombatVfxKind.Equip;
        effect.Lifetime = large ? 1.25f : kind == CombatVfxKind.Muzzle ? 0.20f : 0.65f;
        effect.Color = kind == CombatVfxKind.PhaseChange ? Red : kind == CombatVfxKind.WallHit ? new Color(0.65f,0.68f,0.72f,0.8f) : Gold;
        if (ring)
        {
            var ringObject = new GameObject("Expanding Ring"); ringObject.transform.SetParent(go.transform, false);
            ringObject.AddComponent<MeshFilter>().sharedMesh = catalog.RingMesh;
            var renderer = ringObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = catalog.GroundMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            effect.ExpandingRing = ringObject.transform; effect.TintedRenderers = new Renderer[] { renderer };
        }
        var sparks = Particle(go.transform, "Sparks", catalog.ParticleMaterial, effect.Color, false, large ? 36 : 16, large ? 0.55f : 0.20f);
        var dust = Particle(go.transform, "Dust", catalog.ParticleMaterial, new Color(0.48f,0.43f,0.36f,0.28f), false, large ? 16 : 5, large ? 0.8f : 0.28f);
        var dustMain = dust.main; dustMain.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.28f); dustMain.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        if (large)
        {
            var debris = Particle(go.transform, "Stone Fragments", catalog.ParticleMaterial, new Color(0.42f,0.39f,0.31f), false, 12, 0.65f);
            var dm = debris.main; dm.startSize = new ParticleSystem.MinMaxCurve(0.02f,0.055f); dm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f,1.4f); dm.gravityModifier = 0.8f;
            var renderer = debris.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
            var tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube); renderer.mesh = tempCube.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(tempCube);
            effect.Particles = new[] { sparks, dust, debris }; effect.BurstCounts = new[] { 32,12,9 };
        }
        else { effect.Particles = new[] { sparks, dust }; effect.BurstCounts = new[] { 12,4 }; }
        effect.AudioHook = go.AddComponent<AudioSource>(); effect.AudioHook.playOnAwake = false; effect.AudioHook.spatialBlend = 1f; effect.AudioHook.maxDistance = 16;
        go.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + kind + ".prefab"); UnityEngine.Object.DestroyImmediate(go);
        return prefab.GetComponent<PooledCombatEffect>();
    }
    private static ParticleSystem Particle(Transform parent, string name, Material material, Color color, bool loop, int max, float lifetime)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = loop; main.duration = 1f; main.maxParticles = max;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime); main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f); main.startColor = color; main.gravityModifier = loop ? 0 : 0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = loop ? max * 0.5f : 0;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 65; shape.radius = loop ? 0.35f : 0.08f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0, 1) }); fade.color = gradient;
        var noise = ps.noise; noise.enabled = loop; noise.strength = 0.12f; noise.frequency = 0.4f; noise.quality = ParticleSystemNoiseQuality.Low;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return ps;
    }
    private static BossTelegraphView TelegraphPrefab(CombatVfxCatalog catalog)
    {
        var go = new GameObject("Boss Telegraph"); var view = go.AddComponent<BossTelegraphView>();
        go.GetComponent<MeshRenderer>().sharedMaterial = catalog.GroundMaterial;
        var edge = new GameObject("Exact Damage Boundary"); edge.transform.SetParent(go.transform, false);
        view.Boundary = edge.AddComponent<LineRenderer>(); view.Boundary.sharedMaterial = catalog.GroundMaterial;
        view.Boundary.useWorldSpace = false; view.Boundary.loop = true; view.Boundary.widthMultiplier = 0.07f;
        view.Boundary.shadowCastingMode = ShadowCastingMode.Off; view.Boundary.receiveShadows = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/BossTelegraph.prefab"); UnityEngine.Object.DestroyImmediate(go);
        return prefab.GetComponent<BossTelegraphView>();
    }
    private static GameObject LootPrefab(CombatVfxCatalog catalog)
    {
        var go = new GameObject("Server Loot Container");
        var box = Primitive(go.transform, "Sealed Cache", PrimitiveType.Cube, new Vector3(0,0.22f,0), new Vector3(0.55f,0.4f,0.42f), MaterialAsset("Loot_Cache", Shader.Find("Universal Render Pipeline/Lit"), new Color(0.16f,0.18f,0.23f)));
        var vfx = go.AddComponent<LootVfxController>(); vfx.Catalog = catalog;
        var beam = new GameObject("Rarity Beam"); beam.transform.SetParent(go.transform, false); vfx.Beam = beam.AddComponent<LineRenderer>();
        vfx.Beam.useWorldSpace = false; vfx.Beam.positionCount = 2; vfx.Beam.SetPosition(0, Vector3.up * 0.1f); vfx.Beam.SetPosition(1, Vector3.up);
        vfx.Beam.sharedMaterial = catalog.TrailMaterial; vfx.Beam.widthMultiplier = 0.1f;
        vfx.Beam.shadowCastingMode = ShadowCastingMode.Off;
        var ring = new GameObject("Loot Rarity Ring"); ring.transform.SetParent(go.transform, false); ring.transform.localScale = Vector3.one * 0.52f;
        ring.AddComponent<MeshFilter>().sharedMesh = catalog.RingMesh; vfx.GroundRing = ring.AddComponent<MeshRenderer>(); vfx.GroundRing.sharedMaterial = catalog.GroundMaterial;
        vfx.GroundRing.shadowCastingMode = ShadowCastingMode.Off;
        vfx.Aura = Particle(go.transform, "Rarity Motes", catalog.ParticleMaterial, Gold, true, 20, 1.1f);
        var auraMain = vfx.Aura.main; auraMain.playOnAwake = true;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/ServerLoot.prefab"); UnityEngine.Object.DestroyImmediate(go); return prefab;
    }
    private static GameObject ProjectilePrefab(CombatVfxCatalog catalog)
    {
        var go = new GameObject("Server Bullet");
        Primitive(go.transform, "Bullet", PrimitiveType.Capsule, Vector3.zero, new Vector3(0.18f,0.42f,0.18f), MaterialAsset("Bullet_Emission", Shader.Find("Universal Render Pipeline/Unlit"), Gold)).transform.localRotation = Quaternion.Euler(90,0,0);
        var trail = go.AddComponent<TrailRenderer>(); trail.sharedMaterial = catalog.TrailMaterial; trail.time = 0.13f;
        trail.startWidth = 0.10f; trail.endWidth = 0.005f; trail.minVertexDistance = 0.08f; trail.emitting = true;
        trail.startColor = Gold; trail.endColor = new Color(1,0.45f,0.08f,0); trail.shadowCastingMode = ShadowCastingMode.Off;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/ServerProjectile.prefab"); UnityEngine.Object.DestroyImmediate(go); return prefab;
    }
    private static GameObject BossPrefab(CombatVfxCatalog catalog)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/Monster/Monster1.prefab");
        if (source == null) throw new InvalidOperationException("Monster1 prefab missing.");
        var boss = UnityEngine.Object.Instantiate(source); boss.name = "Dungeon Guardian"; boss.transform.position = Vector3.zero; boss.transform.localScale = Vector3.one * 1.65f;
        var animator = boss.GetComponent<Animator>(); if (animator != null) animator.applyRootMotion = false;
        Bounds bodyBounds = GroundModel(boss, animator);
        var nav = boss.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (nav != null) nav.enabled = false;
        foreach (var behaviour in boss.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue; string type = behaviour.GetType().Name;
            if (type == "Monster_Skull" || type == "Monster" || type == "Monster_movetest" || type == "MonsterLoot" || type == "LootContainer" || type == "MonsterHitbox" || type == "Monster_Attack") behaviour.enabled = false;
        }
        foreach (var canvas in boss.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
        var controller = boss.AddComponent<BossVfxController>(); controller.Catalog = catalog;
        var body = MaterialAsset("Guardian_Stone", Shader.Find("Retry/Toon/Ramp Lit"), new Color(0.22f,0.24f,0.27f));
        body.SetFloat("_RimStrength", 0.6f); body.SetColor("_RimColor", Gold);
        var core = MaterialAsset("Guardian_Core", Shader.Find("Universal Render Pipeline/Lit"), Gold);
        core.EnableKeyword("_EMISSION"); core.SetColor("_EmissionColor", new Color(2f,1.1f,0.08f));
        core.SetFloat("_Smoothness", 0.65f); core.SetFloat("_Metallic", 0.5f);
        var bodyRenderers = new List<Renderer>();
        foreach (var renderer in boss.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = renderer.sharedMaterials; for (int i = 0; i < mats.Length; ++i) mats[i] = body;
            renderer.sharedMaterials = mats; bodyRenderers.Add(renderer);
        }
        // Keep original skeleton paths intact. Additional pieces move independently of authoritative root.
        var visual = new GameObject("Guardian Armor Visual"); visual.transform.SetParent(boss.transform, false); controller.VisualRoot = visual.transform;
        var cores = new List<Renderer>();
        float height = bodyBounds.size.y;
        Transform chest = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
        if (chest == null && animator != null && animator.isHuman) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        File.AppendAllText("Artifacts/boss-model-bounds.txt", $"Chest anchor: {(chest != null ? chest.name : "bounds fallback")} position {(chest != null ? chest.position : Vector3.zero)} scale {(chest != null ? chest.lossyScale : Vector3.one)}\n");
        Vector3 chestPosition = chest != null ? chest.position : new Vector3(bodyBounds.center.x, bodyBounds.min.y + height * 0.65f, bodyBounds.center.z);
        Vector3 corePosition = chestPosition + boss.transform.forward * Mathf.Max(0.18f, bodyBounds.size.z * 0.40f);
        cores.Add(AttachPrimitive(chest != null ? chest : visual.transform, "Exposed Guardian Core", PrimitiveType.Sphere, corePosition, Vector3.one * height * 0.15f, core));
        for (int side = -1; side <= 1; side += 2)
        {
            Transform shoulder = animator != null && animator.isHuman ? animator.GetBoneTransform(side < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm) : null;
            Vector3 shoulderPosition = shoulder != null ? shoulder.position : chestPosition + boss.transform.right * side * height * 0.27f;
            var plate = AttachPrimitive(shoulder != null ? shoulder : visual.transform, "Stone Shoulder " + side, PrimitiveType.Cube, shoulderPosition, new Vector3(0.19f,0.21f,0.20f) * height, body);
            plate.transform.localRotation = Quaternion.Euler(0,0,side * -18); bodyRenderers.Add(plate);
            cores.Add(AttachPrimitive(plate.transform, "Shoulder Rune " + side, PrimitiveType.Cube, shoulderPosition + boss.transform.forward * height * 0.11f, new Vector3(0.045f,0.13f,0.02f) * height, core));
        }
        for (int i = 0; i < 5; ++i)
        {
            float a = i * Mathf.PI * 2 / 5;
            Vector3 sealPosition = new Vector3(bodyBounds.center.x + Mathf.Cos(a)*height*0.28f, bodyBounds.min.y + height*(0.84f + Mathf.Sin(a)*0.16f), bodyBounds.center.z - height*0.15f);
            var shard = AttachPrimitive(visual.transform, "Floating Seal " + i, PrimitiveType.Cube, sealPosition, new Vector3(0.055f,0.12f,0.055f) * height, core);
            shard.transform.localRotation = Quaternion.Euler(15,0, i*72); cores.Add(shard);
        }
        controller.BodyRenderers = bodyRenderers.ToArray(); controller.CoreRenderers = cores.ToArray();
        controller.ChargeEnergy = Particle(visual.transform, "Charge Energy", catalog.ParticleMaterial, Gold, true, 40, 0.55f);
        controller.ChargeEnergy.transform.localPosition = Vector3.up;
        controller.ChargeDust = Particle(boss.transform, "Charge Ground Dust", catalog.ParticleMaterial, new Color(0.55f,0.46f,0.32f,0.3f), true, 28, 0.6f);
        controller.ChargeDust.transform.localPosition = Vector3.up * 0.08f;
        var chargeDustMain = controller.ChargeDust.main; chargeDustMain.startSize = new ParticleSystem.MinMaxCurve(0.12f,0.22f);
        controller.PhaseAura = Particle(visual.transform, "Phase Two Embers", catalog.ParticleMaterial, Red, true, 24, 0.8f);
        controller.PhaseAura.transform.localPosition = Vector3.up * 0.65f;
        var trailObject = new GameObject("Charge Trail"); trailObject.transform.SetParent(boss.transform, false); trailObject.transform.localPosition = Vector3.up * 0.7f;
        controller.ChargeTrail = trailObject.AddComponent<TrailRenderer>(); controller.ChargeTrail.sharedMaterial = catalog.TrailMaterial;
        controller.ChargeTrail.time = 0.22f; controller.ChargeTrail.minVertexDistance = 0.12f;
        controller.ChargeTrail.startWidth = 0.7f; controller.ChargeTrail.endWidth = 0.02f; controller.ChargeTrail.emitting = false;
        controller.ChargeTrail.startColor = new Color(1,0.55f,0.05f,0.4f); controller.ChargeTrail.endColor = new Color(1,0.2f,0.02f,0);
        controller.ChargeTrail.shadowCastingMode = ShadowCastingMode.Off;
        controller.AudioHook = boss.AddComponent<AudioSource>(); controller.AudioHook.playOnAwake = false; controller.AudioHook.spatialBlend = 1f;
        var prefab = PrefabUtility.SaveAsPrefabAsset(boss, Root + "/Prefabs/DungeonGuardian.prefab"); UnityEngine.Object.DestroyImmediate(boss); return prefab;
    }
    private static Bounds GroundModel(GameObject boss, Animator animator)
    {
        Directory.CreateDirectory("Artifacts"); File.WriteAllText("Artifacts/boss-renderer-diagnostics.txt", "FBX renderer scale diagnostics\n");
        if (animator != null) { animator.Rebind(); animator.Update(0); }
        var renderers = boss.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds bounds = BakedBounds(renderers);
        if (bounds.size.y < 0.5f || bounds.size.y > 8f) throw new InvalidOperationException("Unexpected boss height after FBX scale compensation: " + bounds.size.y + "m; see boss-renderer-diagnostics.txt.");
        float offset = (bounds.min.y - boss.transform.position.y) / boss.transform.lossyScale.y;
        var roots = new HashSet<Transform>();
        foreach (var renderer in renderers)
        {
            Transform t = renderer.transform;
            while (t.parent != null && t.parent != boss.transform) t = t.parent;
            if (t != boss.transform) roots.Add(t);
            if (renderer.rootBone != null)
            {
                t = renderer.rootBone;
                while (t.parent != null && t.parent != boss.transform) t = t.parent;
                if (t != boss.transform) roots.Add(t);
            }
        }
        foreach (var t in roots) t.localPosition -= Vector3.up * offset;
        if (animator != null) { animator.Rebind(); animator.Update(0); }
        bounds = BakedBounds(renderers);
        Directory.CreateDirectory("Artifacts");
        File.WriteAllText("Artifacts/boss-model-bounds.txt", $"Ground offset local: {offset}\nBaked world min: {bounds.min}\nBaked world max: {bounds.max}\nHumanoid: {(animator != null && animator.isHuman)}\nSkeleton/model children adjusted: {roots.Count}\n");
        if (Mathf.Abs(bounds.min.y - boss.transform.position.y) > 0.15f) throw new InvalidOperationException("Boss model grounding failed: " + bounds.min.y);
        return bounds;
    }
    private static Bounds BakedBounds(SkinnedMeshRenderer[] renderers)
    {
        bool any = false; Bounds result = new Bounds();
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
            // Compensate for FBX import scale before applying the renderer transform.
            var mesh = new Mesh(); renderer.BakeMesh(mesh, true);
            File.AppendAllText("Artifacts/boss-renderer-diagnostics.txt", $"{renderer.name}: world={renderer.bounds}, local={renderer.localBounds}, compensatedBake={mesh.bounds}, lossy={renderer.transform.lossyScale}, position={renderer.transform.position}, forceOff={renderer.forceRenderingOff}\n");
            foreach (var vertex in mesh.vertices)
            {
                Vector3 p = renderer.transform.TransformPoint(vertex);
                if (!any) { result = new Bounds(p,Vector3.zero); any = true; } else result.Encapsulate(p);
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        if (!any) throw new InvalidOperationException("Boss source has no visible skinned mesh.");
        return result;
    }
    private static Renderer AttachPrimitive(Transform parent, string name, PrimitiveType kind, Vector3 worldPosition, Vector3 worldScale, Material material)
    {
        var renderer = Primitive(parent,name,kind,Vector3.zero,Vector3.one,material);
        renderer.transform.position = worldPosition;
        var scale = parent.lossyScale;
        renderer.transform.localScale = new Vector3(worldScale.x/Mathf.Max(0.001f,Mathf.Abs(scale.x)),worldScale.y/Mathf.Max(0.001f,Mathf.Abs(scale.y)),worldScale.z/Mathf.Max(0.001f,Mathf.Abs(scale.z)));
        return renderer;
    }
    private static Renderer Primitive(Transform parent, string name, PrimitiveType kind, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(kind); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material; return renderer;
    }
    [MenuItem("Tools/Retry/Combat/Validate Assets")]
    public static void ValidateAssets()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>(ResourcePath);
        if (catalog == null || catalog.BossPrefab == null || catalog.LootPrefab == null || catalog.ProjectilePrefab == null || catalog.TelegraphPrefab == null) throw new InvalidOperationException("Incomplete CombatVfxCatalog.");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid); var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) throw new InvalidOperationException("Missing script: " + path);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in renderer.sharedMaterials)
                    if (mat == null || mat.shader == null || mat.shader.name.Contains("InternalError")) throw new InvalidOperationException("Missing material/shader: " + path + "/" + renderer.name);
        }
        foreach (CombatVfxKind kind in Enum.GetValues(typeof(CombatVfxKind))) if (catalog.Find(kind) == null) throw new InvalidOperationException("Missing VFX " + kind);
        ValidateGeometry(catalog);
        Debug.Log("COMBAT_CONTENT_VALIDATION_OK: prefab scripts/materials/catalog references valid. Audio hooks intentionally contain no clips.");
    }
    private static void ValidateGeometry(CombatVfxCatalog catalog)
    {
        var view = UnityEngine.Object.Instantiate(catalog.TelegraphPrefab);
        try
        {
            const float radius = 3.2f;
            view.Show(Vector3.zero, Vector3.zero, radius, false, 1, catalog.GroundMaterial);
            var points = view.GetComponent<MeshFilter>().sharedMesh.vertices;
            for (int i = 1; i < points.Length; ++i)
                if (Mathf.Abs(points[i].magnitude - radius) > 0.001f) throw new InvalidOperationException("Telegraph circle disagrees with packet radius.");
            var target = new Vector3(4f, 0f, 2f);
            view.Show(Vector3.zero, target, 0.85f, true, 2, catalog.GroundMaterial);
            points = view.GetComponent<MeshFilter>().sharedMesh.vertices;
            for (int i = 1; i < points.Length; ++i)
            {
                float t = Mathf.Clamp01(Vector3.Dot(points[i],target) / target.sqrMagnitude);
                if (Mathf.Abs((points[i] - target * t).magnitude - 0.85f) > 0.001f) throw new InvalidOperationException("Telegraph capsule disagrees with server sweep radius.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
    }
    [MenuItem("Tools/Retry/Combat/Render Preview")]
    public static void RenderPreview()
    {
        // Recover only our exact orphan marker from an interrupted older preview.
        for (int i = UnityEngine.SceneManagement.SceneManager.sceneCount - 1; i >= 0; --i)
        {
            var candidate = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!candidate.isLoaded || !string.IsNullOrEmpty(candidate.path)) continue;
            var roots = candidate.GetRootGameObjects();
            if (roots.Length == 1 && roots[0].name == "Combat Asset Preview")
            {
                UnityEngine.Object.DestroyImmediate(roots[0]); EditorSceneManager.CloseScene(candidate, true);
            }
        }
        var previousActiveScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var previousAmbient = RenderSettings.ambientLight;
        var previousAmbientMode = RenderSettings.ambientMode;
        var catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>(ResourcePath);
        if (catalog == null) { BuildAll(); catalog = AssetDatabase.LoadAssetAtPath<CombatVfxCatalog>(ResourcePath); }
        var scene = EditorSceneManager.NewPreviewScene();
        var stage = new GameObject("Combat Asset Preview"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage, scene);
        try
        {
        var boss = UnityEngine.Object.Instantiate(catalog.BossPrefab, stage.transform); boss.transform.position = new Vector3(0,0,0);
        foreach (var behaviour in boss.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour != null) behaviour.enabled = false;
        var animator = boss.GetComponent<Animator>(); if (animator != null) { animator.Rebind(); animator.Update(0); }
        var floor = Primitive(stage.transform, "Preview Floor", PrimitiveType.Cube, new Vector3(0,-0.12f,0), new Vector3(26,0.2f,26), MaterialAsset("Preview_Floor", Shader.Find("Universal Render Pipeline/Lit"), new Color(0.14f,0.16f,0.20f)));
        var view = UnityEngine.Object.Instantiate(catalog.TelegraphPrefab, stage.transform);
        // Snapshot fixtures mirror the current server tuning; gameplay always reads packet.radius.
        view.Show(Vector3.zero, Vector3.forward * 5f, 4.5f, false, 1, catalog.GroundMaterial); view.SetProgress(0.78f);
        var loot = UnityEngine.Object.Instantiate(catalog.LootPrefab, stage.transform); loot.transform.position = new Vector3(3.2f,0,1.4f);
        loot.GetComponent<LootVfxController>().Configure(Gold, 4, true, true);
        foreach (var ps in loot.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(0.6f, true, true);
        var lightGo = new GameObject("Preview Key Light"); lightGo.transform.SetParent(stage.transform); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f; lightGo.transform.rotation = Quaternion.Euler(48,-35,0);
        var cameraGo = new GameObject("Preview Camera"); cameraGo.transform.SetParent(stage.transform); var camera = cameraGo.AddComponent<Camera>();
        camera.transform.position = new Vector3(-7.0f,6.8f,10.5f); camera.transform.LookAt(new Vector3(0,0.95f,0)); camera.fieldOfView = 43;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f,0.045f,0.07f);
        camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.cullingMask = 1 << 31;
        foreach (var item in stage.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
        RenderSettings.ambientLight = new Color(0.48f,0.48f,0.52f); RenderSettings.ambientMode = AmbientMode.Flat;
        string output = Path.GetFullPath("Artifacts/CombatVfx"); Directory.CreateDirectory(output);
        Render(camera, output + "/Guardian_Slam_Loot.png");
        view.Show(Vector3.zero, new Vector3(-4,0,1.5f), 1.5f, true, 2, catalog.GroundMaterial); view.SetProgress(0.82f);
        var bossController = boss.GetComponent<BossVfxController>();
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", Red); mpb.SetColor("_EmissionColor", Red * 2);
        foreach (var r in bossController.CoreRenderers) if (r != null) r.SetPropertyBlock(mpb);
        var wave = UnityEngine.Object.Instantiate(catalog.Find(CombatVfxKind.Shockwave), stage.transform);
        foreach (var item in wave.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
        wave.Play(new Vector3(0,0.04f,0), Quaternion.identity, 5.2f, 0, 1); wave.SetProgress(0.3f);
        foreach (var ps in wave.Particles) ps.Simulate(0.16f, true, false);
        var trail = bossController.ChargeTrail; trail.Clear(); trail.AddPosition(new Vector3(0,1,0)); trail.AddPosition(new Vector3(1,1,0.5f)); trail.AddPosition(new Vector3(2,1,1));
        Render(camera, output + "/Guardian_Phase2_Charge_Wave.png");
        wave.gameObject.SetActive(false);
        view.Show(Vector3.zero,Vector3.zero,10f,false,2,catalog.GroundMaterial); view.SetProgress(0.82f);
        camera.transform.position = new Vector3(-15f,13f,22f); camera.transform.LookAt(new Vector3(0,0.9f,0));
        Render(camera,output + "/Guardian_ServerShockwave_10m.png");
        Debug.Log("COMBAT_PREVIEW_OK: " + output);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stage); EditorSceneManager.ClosePreviewScene(scene);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActiveScene);
            RenderSettings.ambientLight = previousAmbient; RenderSettings.ambientMode = previousAmbientMode;
            AssetDatabase.SaveAssets();
        }
    }
    private static void Render(Camera camera, string path)
    {
        var rt = new RenderTexture(1440,960,24); var image = new Texture2D(1440,960,TextureFormat.RGB24,false);
        var previous = RenderTexture.active; camera.targetTexture = rt;
        for (int i = 0; i < 3; ++i) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        RenderTexture.active = rt;
        image.ReadPixels(new Rect(0,0,1440,960),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image);
    }
    public static void CaptureCamera(Camera camera, string path) => Render(camera,path);
}
