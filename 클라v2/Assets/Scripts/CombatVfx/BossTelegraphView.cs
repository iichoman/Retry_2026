using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Packet radius is the exact outer boundary. Charge is the server segment swept by that radius.</summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class BossTelegraphView : MonoBehaviour
{
    public LineRenderer Boundary;
    private Mesh mesh;
    private MeshRenderer fill;
    private MaterialPropertyBlock properties;
    private MaterialPropertyBlock edgeProperties;
    private Color color;
    private bool clipped;
    private Vector4 roomMinMax;
    private const int Segments = 64;
    public void Show(Vector3 origin, Vector3 target, float radius, bool charge, int phase, Material material)
    {
        transform.SetPositionAndRotation(origin + Vector3.up * 0.035f, Quaternion.identity);
        transform.localScale = Vector3.one;
        radius = Mathf.Max(0.01f, radius);
        color = phase >= 2 ? new Color(1f, 0.16f, 0.06f) : new Color(1f, 0.63f, 0.07f);
        if (mesh == null) { mesh = new Mesh { name = "Server Telegraph Boundary" }; GetComponent<MeshFilter>().sharedMesh = mesh; }
        if (fill == null) fill = GetComponent<MeshRenderer>();
        fill.sharedMaterial = material;
        fill.shadowCastingMode = ShadowCastingMode.Off;
        fill.receiveShadows = false;
        if (Boundary == null)
        {
            var go = new GameObject("Exact Damage Boundary"); go.transform.SetParent(transform, false);
            Boundary = go.AddComponent<LineRenderer>(); Boundary.useWorldSpace = false;
            Boundary.shadowCastingMode = ShadowCastingMode.Off; Boundary.receiveShadows = false;
        }
        Boundary.sharedMaterial = material; Boundary.loop = true; Boundary.widthMultiplier = 0.07f;
        var vertices = new Vector3[Segments + 1];
        var colors = new Color[vertices.Length];
        var triangles = new int[Segments * 3];
        var line = new Vector3[Segments];
        Vector3 delta = target - origin; delta.y = 0;
        Vector3 direction = delta.sqrMagnitude > 0.001f ? delta.normalized : Vector3.forward;
        Vector3 right = new Vector3(direction.z, 0, -direction.x);
        vertices[0] = charge ? delta * 0.5f : Vector3.zero; colors[0] = Color.white;
        for (int i = 0; i < Segments; ++i)
        {
            float a = 2f * Mathf.PI * i / Segments;
            Vector3 p = charge
                ? right * (Mathf.Cos(a) * radius) + direction * (Mathf.Sin(a) * radius) + (Mathf.Sin(a) >= 0 ? delta : Vector3.zero)
                : new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
            vertices[i + 1] = p; colors[i + 1] = Color.white; line[i] = p + Vector3.up * 0.005f;
            triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % Segments + 1;
        }
        mesh.Clear(); mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles; mesh.RecalculateBounds();
        Boundary.positionCount = Segments; Boundary.SetPositions(line);
        gameObject.SetActive(true); SetProgress(0);
    }
    public void SetProgress(float t)
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", new Color(color.r, color.g, color.b, Mathf.Lerp(0.12f, 0.42f, t)));
        if (fill != null) fill.SetPropertyBlock(properties);
        if (Boundary != null)
        {
            var edge = Color.Lerp(color, Color.white, Mathf.Clamp01(t) * 0.4f);
            Boundary.startColor = edge; Boundary.endColor = edge;
            if (edgeProperties == null) edgeProperties = new MaterialPropertyBlock();
            edgeProperties.SetFloat("_ClipRoom", clipped ? 1f : 0f); edgeProperties.SetVector("_RoomMinMax", roomMinMax);
            Boundary.SetPropertyBlock(edgeProperties);
        }
    }
    public void SetRoomBounds(Bounds bounds)
    {
        if (properties == null) properties = new MaterialPropertyBlock();
        clipped = true; roomMinMax = new Vector4(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z);
        properties.SetFloat("_ClipRoom", 1f); properties.SetVector("_RoomMinMax",roomMinMax);
        if (fill != null) fill.SetPropertyBlock(properties);
        if (edgeProperties == null) edgeProperties = new MaterialPropertyBlock();
        edgeProperties.SetFloat("_ClipRoom", 1f); edgeProperties.SetVector("_RoomMinMax",roomMinMax);
        if (Boundary != null) Boundary.SetPropertyBlock(edgeProperties);
    }
    public void Hide() => gameObject.SetActive(false);
    private void OnDestroy() { if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
}
