using System.Collections.Generic;
using UnityEngine;

/// <summary>Pure presentation: no colliders, damage, inventory writes or network requests.</summary>
public sealed class PooledVfxSpawner : MonoBehaviour
{
    private static PooledVfxSpawner instance;
    private readonly Dictionary<CombatVfxKind, List<PooledCombatEffect>> pools = new();
    public CombatVfxCatalog Catalog;
    public static PooledCombatEffect Play(CombatVfxKind kind, Vector3 position, Quaternion rotation, float scale = 1f, int owner = 0)
    {
        if (!Application.isPlaying) return null;
        var catalog = CombatVfxCatalog.Load();
        if (catalog == null) return null;
        if (instance == null)
        {
            instance = new GameObject("Combat VFX Pool").AddComponent<PooledVfxSpawner>();
            instance.Catalog = catalog;
        }
        if (!instance.pools.TryGetValue(kind, out var pool)) instance.pools[kind] = pool = new List<PooledCombatEffect>();
        PooledCombatEffect effect = null;
        foreach (var candidate in pool) if (candidate != null && !candidate.gameObject.activeSelf) { effect = candidate; break; }
        if (effect == null)
        {
            var prefab = catalog.Find(kind);
            if (prefab == null || pool.Count >= catalog.MaxInstancesPerEffect) return null;
            effect = Instantiate(prefab, instance.transform);
            pool.Add(effect);
        }
        effect.Play(position, rotation, Mathf.Max(0.05f, scale), owner, catalog.ParticleQuality);
        return effect;
    }
    public static void StopOwner(int owner)
    {
        if (instance == null) return;
        foreach (var pool in instance.pools.Values)
            foreach (var effect in pool)
                if (effect != null && effect.gameObject.activeSelf && effect.Owner == owner) effect.Stop();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;
}
