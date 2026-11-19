using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 생성 대기용 전체화면 로딩 오버레이.
/// 로비 캔버스가 SESSION_ASSIGN에서 꺼져도 남아 있어야 하므로 자체 캔버스를 갖는다.
/// 사용법: LoadingOverlay.Show("던전 생성 중..") / LoadingOverlay.Hide()
/// </summary>
public class LoadingOverlay : MonoBehaviour
{
    private const float TIMEOUT_SEC = 90f;      // 안전장치: 응답이 없어도 영구히 막히지 않게
    private const float DOT_INTERVAL = 0.55f;   // 점 하나 늘어나는 간격
    private const string RES_IMAGE = "Retry";   // Assets/Resources/Retry.png

    private static LoadingOverlay inst;

    private Canvas canvas;
    private Text captionText;
    private string caption = "";
    private float shownAt;
    private float nextDotTime;
    private int dotCount = 0;

    public static bool IsShowing => inst != null && inst.canvas != null && inst.canvas.enabled;

    public static void Show(string text)
    {
        Ensure();
        if (inst == null) return;
        inst.caption = text ?? "";
        inst.shownAt = Time.unscaledTime;
        inst.nextDotTime = 0f;
        inst.dotCount = 0;
        inst.canvas.enabled = true;
    }

    public static void Hide()
    {
        if (inst == null || inst.canvas == null) return;
        inst.canvas.enabled = false;
    }

    private static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("LoadingOverlay");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<LoadingOverlay>();
        inst.Build();
    }

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;                  // 채팅(500)보다 위
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();  // 뒤쪽 버튼 클릭 차단

        // 배경 암전
        var dim = NewRect("Dim", transform, Vector2.zero, Vector2.one);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0.02f, 0.02f, 0.03f, 0.94f);
        dimImg.raycastTarget = true;

        // 로고 이미지. Sprite 임포트 설정에 의존하지 않도록 RawImage + Texture2D.
        var imgGo = NewRect("Logo", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        var irt = imgGo.GetComponent<RectTransform>();
        irt.anchoredPosition = new Vector2(0f, 90f);
        irt.sizeDelta = new Vector2(640f, 520f);
        var raw = imgGo.AddComponent<RawImage>();
        var tex = Resources.Load<Texture2D>(RES_IMAGE);
        if (tex != null)
        {
            raw.texture = tex;
            var fitter = imgGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = tex.height > 0 ? (float)tex.width / tex.height : 1.6f;
        }
        else
        {
            raw.color = new Color(1f, 1f, 1f, 0f);
            Debug.LogWarning("[LoadingOverlay] Resources/Retry 텍스처를 찾지 못했습니다.");
        }

        // 캡션. 어두운 배경 띠를 깔아 가시성을 확보한다.
        var band = NewRect("Band", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        var brt = band.GetComponent<RectTransform>();
        brt.anchoredPosition = new Vector2(0f, -252f);
        brt.sizeDelta = new Vector2(820f, 104f);
        band.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var capGo = NewRect("Caption", band.transform, Vector2.zero, Vector2.one);
        captionText = capGo.AddComponent<Text>();
        captionText.font = LoadFont();
        captionText.fontSize = 46;
        captionText.fontStyle = FontStyle.Bold;
        captionText.color = new Color(1f, 0.93f, 0.74f, 1f);
        captionText.alignment = TextAnchor.MiddleCenter;
        capGo.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

        canvas.enabled = false;
    }

    private static Font LoadFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }

    private static GameObject NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    private void Update()
    {
        if (canvas == null || !canvas.enabled) return;

        // . -> .. -> ... -> . ... 계속 순환. 화면이 멈춰도 값은 unscaledTime 기준.
        if (Time.unscaledTime >= nextDotTime)
        {
            nextDotTime = Time.unscaledTime + DOT_INTERVAL;
            dotCount = (dotCount % 3) + 1;
            if (captionText != null)
                captionText.text = caption.TrimEnd('.') + new string('.', dotCount);
        }

        if (Time.unscaledTime - shownAt > TIMEOUT_SEC)
        {
            Debug.LogWarning("[LoadingOverlay] 타임아웃으로 강제 종료");
            Hide();
        }
    }
}
