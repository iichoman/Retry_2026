using System;
using UnityEngine;

public enum CombatVfxKind { BossEntrance, SlamImpact, ChargeStop, Shockwave, PhaseChange, BossHit, BossDeath, LootSpawn, LootRemove, Pickup, SwordHit, Muzzle, BulletHit, WallHit, Equip, DodgeDust }

[CreateAssetMenu(menuName = "Retry/Combat VFX Catalog")]
public sealed class CombatVfxCatalog : ScriptableObject
{
    public GameObject BossPrefab;
    public GameObject LootPrefab;
    public GameObject ProjectilePrefab;
    public BossTelegraphView TelegraphPrefab;
    public Material GroundMaterial;
    public Material ParticleMaterial;
    public Material TrailMaterial;
    public Mesh RingMesh;
    public LootVisualProfile[] LootProfiles;
    public VfxEntry[] Effects;
    [Range(0.2f, 1f)] public float ParticleQuality = 1f;
    [Range(1, 32)] public int MaxInstancesPerEffect = 12;
    private static CombatVfxCatalog loaded;
    public static CombatVfxCatalog Load() => loaded != null ? loaded : loaded = Resources.Load<CombatVfxCatalog>("CombatVfxCatalog");
    public PooledCombatEffect Find(CombatVfxKind kind)
    {
        if (Effects != null) foreach (var entry in Effects) if (entry.Kind == kind) return entry.Prefab;
        return null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => loaded = null;
}

[Serializable] public struct VfxEntry { public CombatVfxKind Kind; public PooledCombatEffect Prefab; }
[Serializable] public struct LootVisualProfile
{
    public string Label;
    public Color Color;
    public float BeamHeight;
    public int ParticleCount;
}
