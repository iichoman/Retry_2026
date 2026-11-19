using UnityEngine;

public sealed class PooledCombatEffect : MonoBehaviour
{
    [Min(0.05f)] public float Lifetime = 0.8f;
    public Transform ExpandingRing;
    public ParticleSystem[] Particles;
    public int[] BurstCounts;
    public Renderer[] TintedRenderers;
    public Color Color = new Color(1f, 0.65f, 0.1f, 0.85f);
    public AudioSource AudioHook;
    public int Owner { get; private set; }
    private float began;
    private Color originalColor;
    private bool initialized;
    private Renderer[] allRenderers;
    private MaterialPropertyBlock roomProperties;
    private MaterialPropertyBlock properties;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public void Play(Vector3 position, Quaternion rotation, float scale, int owner, float quality)
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        if (roomProperties == null) roomProperties = new MaterialPropertyBlock();
        if (!initialized) { originalColor = Color; initialized = true; allRenderers = GetComponentsInChildren<Renderer>(true); }
        Color = originalColor;
        properties.SetFloat("_ClipRoom",0f); roomProperties.SetFloat("_ClipRoom",0f);
        foreach (var renderer in allRenderers) if (renderer != null) renderer.SetPropertyBlock(roomProperties);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = Vector3.one * scale;
        Owner = owner;
        began = Time.unscaledTime;
        gameObject.SetActive(true);
        SetProgress(0f);
        if (Particles != null) for (int i = 0; i < Particles.Length; ++i)
        {
            var ps = Particles[i];
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
            int count = BurstCounts != null && i < BurstCounts.Length ? BurstCounts[i] : 12;
            ps.Emit(Mathf.Max(1, Mathf.RoundToInt(count * quality)));
        }
        if (AudioHook != null && AudioHook.clip != null) AudioHook.Play();
    }
    public void SetProgress(float progress)
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        if (ExpandingRing != null) ExpandingRing.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(progress * 2f));
        var tint = Color; tint.a *= 1f - Mathf.Clamp01(progress);
        properties.SetColor(BaseColor, tint);
        if (TintedRenderers != null) foreach (var r in TintedRenderers) if (r != null) r.SetPropertyBlock(properties);
    }
    public void SetRoomBounds(Bounds bounds)
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        if (roomProperties == null) roomProperties = new MaterialPropertyBlock();
        var rectangle = new Vector4(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z);
        properties.SetFloat("_ClipRoom",1f); properties.SetVector("_RoomMinMax",rectangle);
        roomProperties.SetFloat("_ClipRoom",1f); roomProperties.SetVector("_RoomMinMax",rectangle);
        if (allRenderers != null) foreach (var renderer in allRenderers) if (renderer != null) renderer.SetPropertyBlock(roomProperties);
        if (TintedRenderers != null) foreach (var renderer in TintedRenderers) if (renderer != null) renderer.SetPropertyBlock(properties);
    }
    private void Update()
    {
        float t = (Time.unscaledTime - began) / Lifetime;
        if (t >= 1f) { Stop(); return; }
        SetProgress(t);
    }
    public void Stop() => gameObject.SetActive(false);
    private void OnDisable()
    {
        if (Particles != null) foreach (var ps in Particles) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (AudioHook != null) AudioHook.Stop();
    }
}
