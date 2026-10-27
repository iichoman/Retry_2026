using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameUIGradient : BaseMeshEffect
{
    [SerializeField] private Color topColor = Color.white;
    [SerializeField] private Color bottomColor = Color.white;

    public void SetColors(Color top, Color bottom)
    {
        topColor = top;
        bottomColor = bottom;

        if (graphic != null)
        {
            graphic.SetVerticesDirty();
        }
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0)
        {
            return;
        }

        UIVertex vertex = default;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            minY = Mathf.Min(minY, vertex.position.y);
            maxY = Mathf.Max(maxY, vertex.position.y);
        }

        float height = Mathf.Max(0.0001f, maxY - minY);
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            float t = Mathf.Clamp01((vertex.position.y - minY) / height);
            vertex.color *= Color.Lerp(bottomColor, topColor, t);
            vertexHelper.SetUIVertex(vertex, i);
        }
    }
}
