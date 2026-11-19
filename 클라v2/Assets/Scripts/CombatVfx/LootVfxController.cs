using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class LootVfxController : MonoBehaviour
{
    public CombatVfxCatalog Catalog;
    public LineRenderer Beam;
    public MeshRenderer GroundRing;
    public ParticleSystem Aura;
    public bool RewardChest;
    private MaterialPropertyBlock properties;
    private Color tint = Color.gray;
    private float height = 0.6f;
    private bool inRange, created;
    private void Awake() { EnsureVisuals(); }
    private void EnsureVisuals()
    {
        if (created) return;
        if (Catalog == null) Catalog = CombatVfxCatalog.Load();
        if (Catalog == null) return;
        created = true;
        if (Beam == null)
        {
            var go = new GameObject("Rarity Beam"); go.transform.SetParent(transform, false);
            Beam = go.AddComponent<LineRenderer>(); Beam.useWorldSpace = false; Beam.positionCount = 2;
            Beam.sharedMaterial = Catalog.TrailMaterial; Beam.widthMultiplier = 0.10f;
            Beam.shadowCastingMode = ShadowCastingMode.Off; Beam.receiveShadows = false;
        }
        if (GroundRing == null)
        {
            var go = new GameObject("Loot Rarity Ring"); go.transform.SetParent(transform, false); go.transform.localPosition = Vector3.up * 0.04f;
            go.transform.localScale = Vector3.one * 0.52f;
            go.AddComponent<MeshFilter>().sharedMesh = Catalog.RingMesh; GroundRing = go.AddComponent<MeshRenderer>();
            GroundRing.sharedMaterial = Catalog.GroundMaterial; GroundRing.shadowCastingMode = ShadowCastingMode.Off; GroundRing.receiveShadows = false;
        }
        PooledVfxSpawner.Play(CombatVfxKind.LootSpawn, transform.position, Quaternion.identity, 0.8f);
    }
    public void Configure(Color rarityColor, int rarity, bool containsWeapon, bool containsBossItem)
    {
        EnsureVisuals(); tint = rarityColor;
        if (Aura == null) Aura = GetComponentInChildren<ParticleSystem>();
        bool hasProfiles = Catalog != null && Catalog.LootProfiles != null && Catalog.LootProfiles.Length > 0;
        int index = Mathf.Clamp(rarity, 0, hasProfiles ? Catalog.LootProfiles.Length - 1 : 4);
        height = 0.55f + 0.28f * index + (containsWeapon ? 0.45f : 0) + (containsBossItem ? 0.45f : 0);
        if (containsBossItem) tint = new Color(1f, 0.56f, 0.05f);
        if (hasProfiles)
            height = Catalog.LootProfiles[index].BeamHeight + (containsWeapon ? 0.45f : 0f);
        if (RewardChest) { tint = new Color(1f,.68f,.2f); height = 2.8f; }
        if (GroundRing != null && RewardChest) GroundRing.transform.localScale = Vector3.one * .85f;
        if (Aura != null)
        {
            var main = Aura.main; main.startColor = tint; main.maxParticles = RewardChest ? 18 : containsBossItem ? 28 : containsWeapon ? 20 : 10;
            var emission = Aura.emission; emission.rateOverTime = Mathf.Max(2, main.maxParticles / 3) * (Catalog != null ? Catalog.ParticleQuality : 1f);
        }
        Apply();
    }
    public void SetInRange(bool value) { if (value == inRange) return; inRange = value; Apply(); }
    public void PlayRemoval() => PooledVfxSpawner.Play(CombatVfxKind.LootRemove, transform.position, Quaternion.identity, 0.55f);
    public static void PlayPickupSuccess(Vector3 position) => PooledVfxSpawner.Play(CombatVfxKind.Pickup, position + Vector3.up * 0.6f, Quaternion.identity, 0.6f);
    private void Apply()
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        if (Beam != null)
        {
            Beam.SetPosition(0, Vector3.up * 0.16f); Beam.SetPosition(1, Vector3.up * height);
            Beam.startColor = new Color(tint.r, tint.g, tint.b, inRange ? 0.8f : 0.40f);
            Beam.endColor = new Color(tint.r, tint.g, tint.b, 0.02f);
            Beam.widthMultiplier = inRange ? 0.15f : 0.10f;
        }
        properties.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, inRange ? 0.85f : 0.38f));
        if (GroundRing != null) GroundRing.SetPropertyBlock(properties);
    }
    private void OnDisable() { if (Aura != null) Aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
}
