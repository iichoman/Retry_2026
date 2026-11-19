using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MonsterAlertEffect : MonoBehaviour
{
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.55f, 0f);
    [SerializeField, Min(0.1f)] private float visibleDuration = 1.2f;
    [SerializeField, Min(0f)] private float cooldown = 1f;
    [SerializeField, Min(0.001f)] private float worldScale = 0.006f;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Camera targetCamera;

    private RectTransform rootRect;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private float visibleTimer;
    private float cooldownTimer;
    private Vector3 resolvedLocalOffset;

    private void Awake()
    {
        EnsureView();
        resolvedLocalOffset = ResolveLocalOffset();
        SetVisible(false);
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }

        if (visibleTimer <= 0f)
        {
            return;
        }

        visibleTimer -= Time.deltaTime;
        float normalized = Mathf.Clamp01(visibleTimer / visibleDuration);
        float appear = 1f - normalized;
        float pulse = 1f + Mathf.Sin(Time.time * 16f) * 0.06f;
        float bob = Mathf.Sin(appear * Mathf.PI) * 0.35f;

        rootRect.localPosition = resolvedLocalOffset + Vector3.up * bob;
        rootRect.localScale = Vector3.one * (worldScale * Mathf.Lerp(1.25f, 0.95f, appear) * pulse);
        canvasGroup.alpha = Mathf.Clamp01(normalized * 1.8f);

        if (faceCamera)
        {
            FaceCamera();
        }

        if (visibleTimer <= 0f)
        {
            SetVisible(false);
        }
    }

    public void Show()
    {
        if (cooldownTimer > 0f)
        {
            return;
        }

        EnsureView();
        resolvedLocalOffset = ResolveLocalOffset();
        visibleTimer = visibleDuration;
        cooldownTimer = visibleDuration + cooldown;
        rootRect.localPosition = resolvedLocalOffset;
        rootRect.localScale = Vector3.one * (worldScale * 1.2f);
        canvasGroup.alpha = 1f;
        SetVisible(true);
        FaceCamera();
    }

    public void Hide()
    {
        visibleTimer = 0f;
        SetVisible(false);
    }

    private void EnsureView()
    {
        if (rootRect != null)
        {
            return;
        }

        GameObject root = new GameObject("Detection Alert", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);

        rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(120f, 130f);
        rootRect.localScale = Vector3.one * worldScale;

        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 120;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 24f;

        canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        GameObject bubble = new GameObject("Alert Bubble", typeof(RectTransform), typeof(Image));
        bubble.transform.SetParent(root.transform, false);
        RectTransform bubbleRect = bubble.GetComponent<RectTransform>();
        bubbleRect.anchorMin = new Vector2(0.5f, 0.5f);
        bubbleRect.anchorMax = new Vector2(0.5f, 0.5f);
        bubbleRect.sizeDelta = new Vector2(72f, 72f);
        bubbleRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

        Image bubbleImage = bubble.GetComponent<Image>();
        bubbleImage.raycastTarget = false;
        bubbleImage.color = new Color(1f, 0.27f, 0.08f, 0.92f);
        GameUIGradient gradient = bubble.AddComponent<GameUIGradient>();
        gradient.SetColors(new Color(1f, 0.58f, 0.16f, 0.96f), new Color(0.62f, 0.04f, 0.02f, 0.96f));

        Outline bubbleOutline = bubble.AddComponent<Outline>();
        bubbleOutline.effectColor = new Color(1f, 0.9f, 0.35f, 0.9f);
        bubbleOutline.effectDistance = new Vector2(3f, -3f);

        Shadow bubbleShadow = bubble.AddComponent<Shadow>();
        bubbleShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        bubbleShadow.effectDistance = new Vector2(5f, -5f);

        // Two solid strokes stay crisp in world space without a dynamic font atlas.
        CreateMarkStroke(root.transform, "Alert Stem", new Vector2(0, 11), new Vector2(11, 34));
        CreateMarkStroke(root.transform, "Alert Dot", new Vector2(0, -18), new Vector2(11, 11));
    }

    private static void CreateMarkStroke(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var mark = new GameObject(name, typeof(RectTransform), typeof(Image));
        mark.transform.SetParent(parent, false);
        var rect = mark.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = mark.GetComponent<Image>(); image.color = Color.white; image.raycastTarget = false;
        var shadow = mark.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .65f); shadow.effectDistance = new Vector2(2, -2);
    }

    private Vector3 ResolveLocalOffset()
    {
        Collider bodyCollider = GetComponent<Collider>();
        if (bodyCollider is CapsuleCollider capsuleCollider)
        {
            return new Vector3(localOffset.x, capsuleCollider.center.y + capsuleCollider.height * 0.5f + 0.35f, localOffset.z);
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return localOffset;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 top = transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
        return new Vector3(localOffset.x, Mathf.Max(localOffset.y, top.y + 0.35f), localOffset.z);
    }

    private void FaceCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null || rootRect == null)
        {
            return;
        }

        rootRect.rotation = Quaternion.LookRotation(rootRect.position - targetCamera.transform.position, Vector3.up);
        canvas.worldCamera = targetCamera;
    }

    private void SetVisible(bool visible)
    {
        if (canvas != null)
        {
            canvas.enabled = visible;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? canvasGroup.alpha : 0f;
        }
    }
}
