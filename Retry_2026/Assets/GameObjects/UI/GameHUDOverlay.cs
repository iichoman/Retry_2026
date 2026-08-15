using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameHUDOverlay : MonoBehaviour
{
    [SerializeField] private Player_State playerState;
    [SerializeField] private Player player;
    [SerializeField, Min(1)] private int maxStamina = 100;
    [SerializeField, Min(0)] private int currentStamina = 100;
    // 스태미나는 표시만 한다(소모/회복 로직 없음, 항상 100%). 사용 규칙이 생기면 값만 갱신하면 됨.
    [SerializeField] private bool showStaminaBar = true;
    private NetworkBootstrap bootstrap;
    [SerializeField] private bool hideLegacyPlayerHud = true;

    private RectTransform root;
    private Text timerText;
    private Text playerNameText;
    private Text hpText;
    private Text staminaText;
    private Image hpFill;
    private Image staminaFill;
    private Sprite portraitSprite;
    private static Font cachedDefaultFont;

    private static readonly Color PanelTop = new Color(0.13f, 0.105f, 0.075f, 0.96f);
    private static readonly Color PanelBottom = new Color(0.018f, 0.022f, 0.032f, 0.96f);
    private static readonly Color GoldTop = new Color(1f, 0.78f, 0.32f, 1f);
    private static readonly Color GoldBottom = new Color(0.55f, 0.27f, 0.06f, 1f);
    private static readonly Color HpTop = new Color(1f, 0.18f, 0.14f, 1f);
    private static readonly Color HpBottom = new Color(0.42f, 0.015f, 0.012f, 1f);
    private static readonly Color StaminaTop = new Color(1f, 0.88f, 0.18f, 1f);
    private static readonly Color StaminaBottom = new Color(0.82f, 0.46f, 0.02f, 1f);
    private static readonly Color TextMain = new Color(0.96f, 0.92f, 0.8f, 1f);
    private static readonly Color TextMuted = new Color(0.76f, 0.68f, 0.48f, 1f);

    private void Awake()
    {
        ResolveReferences();
        Build();
        HideLegacyHud();
        RefreshAll();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (playerState != null)
        {
            playerState.HpChanged += RefreshHp;
            RefreshHp(playerState.CurrentHp, playerState.MaxHp);
        }
    }

    private void OnDisable()
    {
        if (playerState != null)
        {
            playerState.HpChanged -= RefreshHp;
        }
    }

    private void Update()
    {
        SyncLocalPlayer();
        RefreshTimer();
    }

    // 로컬 플레이어는 세션 접속 시 런타임에 생성된다. 그래서 OnEnable 시점엔 없을 수 있고,
    // FindFirstObjectByType은 로비 디오라마/원격 플레이어를 잘못 잡을 수 있다.
    // → 진짜 로컬 플레이어가 생기거나 바뀌면 HP 구독을 그쪽으로 옮긴다.
    private void SyncLocalPlayer()
    {
        Player_State real = FindLocalPlayerState();
        if (real == null || real == playerState) return;

        if (playerState != null)
        {
            playerState.HpChanged -= RefreshHp;
        }

        playerState = real;
        player = playerState.GetComponent<Player>();
        playerState.HpChanged += RefreshHp;
        RefreshHp(playerState.CurrentHp, playerState.MaxHp);
        RefreshPlayerName();
    }

    private Player_State FindLocalPlayerState()
    {
        if (bootstrap == null)
        {
            bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        }
        if (bootstrap != null && bootstrap.LocalPlayer != null)
        {
            return bootstrap.LocalPlayer.GetComponent<Player_State>();
        }
        return null;
    }

    private void ResolveReferences()
    {
        Player_State real = FindLocalPlayerState();
        if (real != null)
        {
            playerState = real;
        }
        else if (playerState == null)
        {
            playerState = FindFirstObjectByType<Player_State>();
        }

        if (player == null)
        {
            player = playerState != null
                ? playerState.GetComponent<Player>()
                : FindFirstObjectByType<Player>();
        }
    }

    private void Build()
    {
        if (root != null)
        {
            return;
        }

        RectTransform parent = transform as RectTransform;
        if (parent == null)
        {
            return;
        }

        GameObject rootObject = CreateUIObject("Premium HUD", transform);
        root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.SetAsLastSibling();

        BuildPlayerPanel(root);
        BuildTimerPanel(root);
    }

    private void BuildPlayerPanel(Transform parent)
    {
        GameObject panel = CreatePanel("Player Command Panel", parent, new Vector2(32f, 28f), new Vector2(430f, 126f), Vector2.zero, Vector2.zero);

        GameObject portraitFrame = CreatePanel("Portrait Frame", panel.transform, new Vector2(19f, -18f), new Vector2(112f, 112f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        Image portraitFrameImage = portraitFrame.GetComponent<Image>();
        SetGradient(portraitFrameImage, GoldTop, GoldBottom);

        GameObject portraitObject = CreateUIObject("Player Portrait", portraitFrame.transform);
        RectTransform portraitRect = portraitObject.GetComponent<RectTransform>();
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(8f, 8f);
        portraitRect.offsetMax = new Vector2(-8f, -8f);
        Image portraitImage = portraitObject.AddComponent<Image>();
        portraitImage.raycastTarget = false;
        portraitImage.sprite = null;
        portraitImage.type = Image.Type.Simple;
        portraitImage.color = new Color(0.015f, 0.018f, 0.024f, 0.92f);

        playerNameText = CreateText("Player Name", panel.transform, new Vector2(150f, -18f), new Vector2(230f, 30f), "PLAYER", 24, FontStyle.Bold, TextAnchor.MiddleLeft, GoldTop);

        hpFill = CreateBar(panel.transform, "HP", new Vector2(152f, -57f), new Vector2(230f, 24f), HpTop, HpBottom);
        hpText = CreateText("HP Value", panel.transform, new Vector2(152f, -57f), new Vector2(230f, 24f), "100 / 100", 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);

        staminaFill = CreateBar(panel.transform, "Stamina", new Vector2(152f, -89f), new Vector2(230f, 22f), StaminaTop, StaminaBottom);
        staminaText = CreateText("Stamina Value", panel.transform, new Vector2(152f, -89f), new Vector2(230f, 22f), "ST 100 / 100", 13, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.62f, 1f));

        if (!showStaminaBar)
        {
            // 프레임(부모)까지 통째로 숨긴다.
            if (staminaFill != null && staminaFill.transform.parent != null)
                staminaFill.transform.parent.gameObject.SetActive(false);
            if (staminaText != null) staminaText.gameObject.SetActive(false);
        }
    }

    private void BuildTimerPanel(Transform parent)
    {
        GameObject panel = CreatePanel("Run Timer Panel", parent, new Vector2(0f, -24f), new Vector2(300f, 58f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.pivot = new Vector2(0.5f, 1f);

        CreateText("Timer Label", panel.transform, new Vector2(0f, -7f), new Vector2(260f, 18f), "진행 시간", 13, FontStyle.Bold, TextAnchor.MiddleCenter, TextMuted);
        GameObject labelCover = CreateUIObject("Timer Label Cover", panel.transform);
        RectTransform labelCoverRect = labelCover.GetComponent<RectTransform>();
        labelCoverRect.anchorMin = new Vector2(0.5f, 1f);
        labelCoverRect.anchorMax = labelCoverRect.anchorMin;
        labelCoverRect.pivot = new Vector2(0.5f, 1f);
        labelCoverRect.anchoredPosition = new Vector2(0f, -7f);
        labelCoverRect.sizeDelta = new Vector2(260f, 18f);
        Image labelCoverImage = labelCover.AddComponent<Image>();
        labelCoverImage.raycastTarget = false;
        labelCoverImage.color = PanelTop;

        CreateText("Timer Label Fixed", panel.transform, new Vector2(0f, -7f), new Vector2(260f, 18f), "진행 시간", 13, FontStyle.Bold, TextAnchor.MiddleCenter, TextMuted);
        timerText = CreateText("Timer Text", panel.transform, new Vector2(0f, -24f), new Vector2(260f, 30f), "00:00", 26, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter, GoldTop);
    }

    private GameObject CreatePanel(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 size, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject panel = CreateUIObject(objectName, parent);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(
            anchorMin.x <= 0f ? 0f : anchorMin.x >= 1f ? 1f : 0.5f,
            anchorMin.y <= 0f ? 0f : anchorMin.y >= 1f ? 1f : 0.5f
        );
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = panel.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.white;
        SetGradient(image, PanelTop, PanelBottom);
        AddOutline(image, new Color(0.92f, 0.67f, 0.22f, 0.58f), new Vector2(2f, -2f));
        AddShadow(image, new Color(0f, 0f, 0f, 0.68f), new Vector2(8f, -8f));
        return panel;
    }

    private Image CreateBar(Transform parent, string label, Vector2 anchoredPosition, Vector2 size, Color top, Color bottom)
    {
        GameObject frame = CreatePanel(label + " Bar Frame", parent, anchoredPosition, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
        Image frameImage = frame.GetComponent<Image>();
        SetGradient(frameImage, new Color(0.055f, 0.047f, 0.04f, 0.95f), new Color(0.012f, 0.014f, 0.02f, 0.95f));

        GameObject fillObject = CreateUIObject(label + " Bar Fill", frame.transform);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        // 왼쪽 고정 + anchorMax.x로 너비를 조절하는 방식(= 바가 줄어듦).
        // Image.Type.Filled의 fillAmount는 GameUIGradient(BaseMeshEffect)와 충돌해
        // 색만 칠해지고 바가 안 줄어들던 문제 때문에 스케일 방식으로 변경.
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);
        Image fill = fillObject.AddComponent<Image>();
        fill.raycastTarget = false;
        fill.type = Image.Type.Simple;
        fill.color = Color.white;
        SetGradient(fill, top, bottom);

        Text labelText = CreateText(label + " Label", frame.transform, new Vector2(8f, 0f), new Vector2(54f, size.y), label.ToUpperInvariant(), 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.98f, 0.84f, 0.45f, 0.88f));
        labelText.raycastTarget = false;

        return fill;
    }

    private void CreateStatChip(RectTransform parent, string value, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject chip = CreatePanel(value + " Chip", parent, anchoredPosition, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
        Image chipImage = chip.GetComponent<Image>();
        SetGradient(chipImage, new Color(0.2f, 0.15f, 0.08f, 0.98f), new Color(0.045f, 0.036f, 0.028f, 0.98f));
        CreateText(value + " Text", chip.transform, Vector2.zero, size, value, 11, FontStyle.Bold, TextAnchor.MiddleCenter, SoftGold());
    }

    private Text CreateText(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 size, string value, int fontSize, FontStyle style, TextAnchor anchor, Color color)
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(objectName.Contains("Right") ? 1f : objectName.Contains("Timer") ? 0.5f : 0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(rect.anchorMin.x, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = textObject.AddComponent<Text>();
        text.raycastTarget = false;
        text.text = value;
        text.font = GetDefaultFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.supportRichText = true;
        AddShadow(text, new Color(0f, 0f, 0f, 0.78f), new Vector2(2f, -2f));
        return text;
    }

    private void RefreshAll()
    {
        RefreshPlayerName();
        if (playerState != null)
        {
            RefreshHp(playerState.CurrentHp, playerState.MaxHp);
        }
        else
        {
            RefreshHp(100, 100);
        }

        RefreshStamina();
        RefreshTimer();
    }

    private void RefreshPlayerName()
    {
        if (playerNameText == null)
        {
            return;
        }

        string playerId = player != null && !string.IsNullOrWhiteSpace(player.PlayerId)
            ? player.PlayerId
            : "PLAYER";
        playerNameText.text = playerId.ToUpperInvariant();
    }

    private void RefreshHp(int currentHp, int maxHp)
    {
        int safeMax = Mathf.Max(1, maxHp);
        int safeCurrent = Mathf.Clamp(currentHp, 0, safeMax);

        if (hpFill != null)
        {
            SetBarFill(hpFill, safeCurrent / (float)safeMax);
        }

        if (hpText != null)
        {
            hpText.text = $"{safeCurrent} / {safeMax}";
        }
    }

    // 바 너비를 ratio(0~1)로 조절. 왼쪽 고정, anchorMax.x만 변경 → 그라디언트 유지한 채 줄어듦.
    private static void SetBarFill(Image fill, float ratio)
    {
        if (fill == null) return;
        ratio = Mathf.Clamp01(ratio);
        RectTransform rt = fill.rectTransform;
        Vector2 max = rt.anchorMax;
        max.x = ratio;
        rt.anchorMax = max;
        // 오른쪽 안쪽 여백(-4) 유지: 꽉 찼을 때만 4px 안쪽, 줄어들면 0으로.
        Vector2 off = rt.offsetMax;
        off.x = (ratio >= 0.999f) ? -4f : 0f;
        rt.offsetMax = off;
    }

    private void RefreshStamina()
    {
        int safeMax = Mathf.Max(1, maxStamina);
        int safeCurrent = Mathf.Clamp(currentStamina, 0, safeMax);

        if (staminaFill != null)
        {
            SetBarFill(staminaFill, safeCurrent / (float)safeMax);
        }

        if (staminaText != null)
        {
            staminaText.text = $"ST {safeCurrent} / {safeMax}";
        }
    }

    private void RefreshTimer()
    {
        if (timerText == null)
        {
            return;
        }

        // 던전(세션) 안에서만 시간이 흐르고, 나가면 0으로 초기화된다.
        // 세션 진입 시점을 기준으로 경과 시간을 직접 잰다.
        bool inSession = bootstrap != null && bootstrap.Identity != null
                         && bootstrap.Identity.IsConnectedToSession;

        if (inSession)
        {
            if (!timerRunning)
            {
                timerRunning = true;
                runStartTime = Time.time;   // 입장 순간부터 재개(0부터 시작)
            }
        }
        else
        {
            timerRunning = false;           // 던전에서 나가면 정지 + 다음 입장 때 초기화
        }

        float seconds = timerRunning ? (Time.time - runStartTime) : 0f;

        TimeSpan elapsed = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
        timerText.text = elapsed.TotalHours >= 1d
            ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private bool timerRunning;
    private float runStartTime;

    private void HideLegacyHud()
    {
        if (!hideLegacyPlayerHud)
        {
            return;
        }

        PlayerHudUI[] legacyHuds = FindObjectsByType<PlayerHudUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < legacyHuds.Length; i++)
        {
            PlayerHudUI legacyHud = legacyHuds[i];
            if (legacyHud == null || legacyHud.transform.IsChildOf(transform))
            {
                continue;
            }

            legacyHud.gameObject.SetActive(false);
        }
    }

    private Sprite CreatePortraitSprite()
    {
        if (portraitSprite != null)
        {
            return portraitSprite;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Generated Player Portrait",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float v = y / (float)(size - 1);
                Color color = Color.Lerp(new Color(0.025f, 0.045f, 0.055f, 1f), new Color(0.08f, 0.12f, 0.12f, 1f), v);
                float vignette = Mathf.Clamp01(Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.52f)) * 1.35f);
                color = Color.Lerp(color, new Color(0.005f, 0.007f, 0.012f, 1f), vignette * 0.75f);
                pixels[y * size + x] = color;
            }
        }

        FillEllipse(pixels, size, 64, 50, 42, 38, new Color(0.08f, 0.48f, 0.52f, 1f));
        FillEllipse(pixels, size, 45, 62, 25, 46, new Color(0.03f, 0.32f, 0.38f, 1f));
        FillEllipse(pixels, size, 84, 62, 25, 46, new Color(0.03f, 0.32f, 0.38f, 1f));
        FillEllipse(pixels, size, 64, 62, 31, 34, new Color(0.96f, 0.78f, 0.62f, 1f));
        FillEllipse(pixels, size, 64, 104, 42, 24, new Color(0.07f, 0.11f, 0.14f, 1f));
        FillEllipse(pixels, size, 50, 61, 5, 4, new Color(0.08f, 0.06f, 0.05f, 1f));
        FillEllipse(pixels, size, 78, 61, 5, 4, new Color(0.08f, 0.06f, 0.05f, 1f));
        FillEllipse(pixels, size, 64, 80, 12, 4, new Color(0.74f, 0.35f, 0.32f, 1f));
        FillEllipse(pixels, size, 64, 34, 34, 16, new Color(0.08f, 0.58f, 0.62f, 1f));
        DrawLine(pixels, size, 28, 104, 100, 104, 4, new Color(0.95f, 0.68f, 0.22f, 1f));

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        portraitSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        portraitSprite.name = "Generated Player Portrait Sprite";
        return portraitSprite;
    }

    private static void FillEllipse(Color[] pixels, int size, int centerX, int centerY, int radiusX, int radiusY, Color color)
    {
        int minX = Mathf.Max(0, centerX - radiusX);
        int maxX = Mathf.Min(size - 1, centerX + radiusX);
        int minY = Mathf.Max(0, centerY - radiusY);
        int maxY = Mathf.Min(size - 1, centerY + radiusY);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x - centerX) / (float)Mathf.Max(1, radiusX);
                float dy = (y - centerY) / (float)Mathf.Max(1, radiusY);
                if (dx * dx + dy * dy > 1f)
                {
                    continue;
                }

                int index = y * size + x;
                pixels[index] = Color.Lerp(pixels[index], color, color.a);
            }
        }
    }

    private static void DrawLine(Color[] pixels, int size, int x0, int y0, int x1, int y1, int thickness, Color color)
    {
        Vector2 start = new Vector2(x0, y0);
        Vector2 end = new Vector2(x1, y1);
        Vector2 line = end - start;
        float lengthSqr = Mathf.Max(0.001f, line.sqrMagnitude);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x, y);
                float t = Mathf.Clamp01(Vector2.Dot(point - start, line) / lengthSqr);
                Vector2 closest = start + line * t;
                if (Vector2.Distance(point, closest) > thickness)
                {
                    continue;
                }

                pixels[y * size + x] = color;
            }
        }
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject obj = new GameObject(objectName, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static void SetGradient(Image image, Color top, Color bottom)
    {
        GameUIGradient gradient = image.GetComponent<GameUIGradient>();
        if (gradient == null)
        {
            gradient = image.gameObject.AddComponent<GameUIGradient>();
        }

        gradient.SetColors(top, bottom);
    }

    private static void AddOutline(Graphic graphic, Color color, Vector2 distance)
    {
        Outline outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    private static void AddShadow(Graphic graphic, Color color, Vector2 distance)
    {
        Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static Color SoftGold()
    {
        return new Color(1f, 0.86f, 0.5f, 1f);
    }

    private static Font GetDefaultFont()
    {
        if (cachedDefaultFont != null)
        {
            return cachedDefaultFont;
        }

        cachedDefaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (cachedDefaultFont == null)
        {
            cachedDefaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return cachedDefaultFont;
    }
}