using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Online-only loot view. Buttons send requests; only server broadcasts change rows.</summary>
[DisallowMultipleComponent]
public sealed class ServerLootUI : MonoBehaviour
{
    [SerializeField] private Font font;
    
    private GameObject panel;
    private Text title, footer, toast, hint;
    private Button takeAll;
    private readonly Text[] labels = new Text[LootConst.MAX_LOOT_ENTRIES];
    private readonly Image[] icons = new Image[LootConst.MAX_LOOT_ENTRIES];
    private readonly Button[] buttons = new Button[LootConst.MAX_LOOT_ENTRIES];
    private readonly int[] rowHashes = new int[LootConst.MAX_LOOT_ENTRIES];
    private Action<int> take;
    private Action all;
    private Action closed;
    private float toastUntil;
    public static ServerLootUI Active { get; private set; }
    public bool IsOpen => panel != null && panel.activeSelf;
    private void Awake() => EnsureBuilt();
    public void EnsureBuilt()
    {
        if (panel != null) return;
        if (font == null) font = ExpeditionTheme.Font;
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 180;
        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        panel = Rect("Server Loot Panel", transform, new Vector2(720, 760), Vector2.zero);
        var surface=panel.AddComponent<Image>();surface.sprite=Resources.Load<Sprite>("ExpeditionUI/Panel");surface.type=Image.Type.Sliced;surface.color=ExpeditionTheme.Ink;
        ExpeditionTheme.Box("Rule",panel.transform,0,286,650,1,ExpeditionTheme.Gold);
        title = Label("Title", panel.transform, new Vector2(650, 56), new Vector2(0, 318), 28);
        for (int i = 0; i < labels.Length; i++)
        {
            int row = i;
            float y = 250 - i * 63;
            var icon = Rect("Icon " + i, panel.transform, new Vector2(42, 42), new Vector2(-312, y));
            icons[i] = icon.AddComponent<Image>(); icons[i].raycastTarget = false;
            labels[i] = Label("Item " + i, panel.transform, new Vector2(460, 58), new Vector2(-38, y), 17);
            buttons[i] = Button("획득", panel.transform, new Vector2(88, 42), new Vector2(295, y), () => take?.Invoke(rowHashes[row]));
        }
        footer = Label("Feedback", panel.transform, new Vector2(650, 56), new Vector2(0, -290), 17);
        takeAll = Button("전체 획득", panel.transform, new Vector2(240, 44), new Vector2(-150, -345), () => all?.Invoke());
        Button("닫기 [F / Esc]", panel.transform, new Vector2(240, 44), new Vector2(150, -345), Close);
        toast = Label("Pickup Feedback", transform, new Vector2(900, 70), new Vector2(0, 390), 23);
        toast.alignment = TextAnchor.MiddleCenter; toast.gameObject.SetActive(false);
        hint = Label("Loot Hint", transform, new Vector2(650, 45), new Vector2(0, -390), 23);
        hint.alignment = TextAnchor.MiddleCenter; hint.gameObject.SetActive(false);
        panel.SetActive(false);
    }
    public void Open(Action<int> requestItem, Action requestAll, Action onClosed)
    {
        EnsureBuilt(); take = requestItem; all = requestAll; closed = onClosed;
        Active = this;
        panel.SetActive(true); panel.transform.SetAsLastSibling(); footer.text = "";
    }
    public void Close()
    {
        if (!IsOpen) return;
        panel.SetActive(false);
        if (Active == this) Active = null;
        var callback = closed; closed = null; callback?.Invoke();
    }
    public void Refresh(LootSpawnData data, ItemCatalog catalog, bool inRange, Func<int, bool> pending)
    {
        if (!IsOpen) return;
        int n = data.entries == null ? 0 : Mathf.Clamp(data.entryCount, 0, Mathf.Min(data.entries.Length, labels.Length));
        long value = 0;
        for (int i = 0; i < labels.Length; i++)
        {
            bool visible = i < n && data.entries[i].count > 0;
            labels[i].gameObject.SetActive(visible); icons[i].gameObject.SetActive(visible); buttons[i].gameObject.SetActive(visible);
            if (!visible) continue;
            var entry = data.entries[i]; var item = catalog != null ? catalog.Resolve(entry.itemHash) : null;
            rowHashes[i] = entry.itemHash;
            buttons[i].interactable = inRange && item != null && !pending(entry.itemHash);
            ItemIconPresentation.Apply(icons[i], item);
            if (item == null)
            {
                labels[i].text = $"알 수 없는 아이템 ×{entry.count}";
                labels[i].color = Color.gray;
                continue;
            }
            long total = (long)item.ExtractionValue * entry.count; value += total;
            labels[i].text = $"{item.DisplayName} ×{entry.count} \n{item.RarityName}   ·   개당 {item.ExtractionValue:N0}   ·   합계 {total:N0}";
            labels[i].color = item.RarityColor;
        }
        string label=data.sourceMonsterId<EnvironmentClient.RewardChestSource?"보물 상자":data.sourceMonsterId<EnvironmentClient.ChestSource?"상자":"전리품";
        title.text = $"{label} · 총 가치 {value:N0}";
        takeAll.interactable = inRange && n > 0;
        if (!inRange) footer.text = "거리 초과";
    }
    public void Feedback(string message, bool success)
    {
        EnsureBuilt();
        if (IsOpen) { footer.text = message; return; }
        toast.text = message; toast.color = success ? new Color(0.5f, 1f, 0.65f) : new Color(1f, 0.7f, 0.5f);
        toastUntil = Time.unscaledTime + 3f; toast.gameObject.SetActive(true); toast.transform.SetAsLastSibling();
    }
    public void SetHint(bool visible, string text)
    {
        EnsureBuilt(); hint.gameObject.SetActive(visible && !IsOpen); if (visible) hint.text = text;
    }
    private void Update() { if (toast != null && toast.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toast.gameObject.SetActive(false); }
    private void OnDestroy() { if (Active == this) Active = null; }
    private GameObject Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.sizeDelta = size; rect.anchoredPosition = position;
        return go;
    }
    private Text Label(string name, Transform parent, Vector2 size, Vector2 position, int fontSize)
    {
        var t = Rect(name, parent, size, position).AddComponent<Text>(); t.font = font; t.fontSize = fontSize;
        t.color = ExpeditionTheme.White; t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false; t.supportRichText = false;
        return t;
    }
    private Button Button(string name, Transform parent, Vector2 size, Vector2 position, Action action)
    {
        var go = Rect(name, parent, size, position); var image = go.AddComponent<Image>(); image.sprite=Resources.Load<Sprite>("ExpeditionUI/Panel");image.type=Image.Type.Sliced;image.color = ExpeditionTheme.Card;
        var button = go.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => action());
        var label = Label(name + " Text", go.transform, size, Vector2.zero, 18); label.text = name; label.alignment = TextAnchor.MiddleCenter;
        return button;
    }
}
