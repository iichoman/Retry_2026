using System.Text;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class DungeonResultUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Text elapsedTimeText;
    [SerializeField] private Text escapedPlayersText;
    [SerializeField] private Text playerKillsText;
    [SerializeField] private Text monsterKillsText;
    [SerializeField] private Text goldText;
    [SerializeField] private Text itemsText;
    [SerializeField] private Button lobbyButton;

    private DungeonExitManager exitManager;
    private bool visibilityRequested;
    private ScrollRect itemScroll;
    private readonly List<ResultItemRow> itemRows = new List<ResultItemRow>();
    private const float ItemRowHeight = 48f;

    private sealed class ResultItemRow
    {
        public RectTransform Root;
        public Image Icon;
        public Image Rarity;
        public Text Label;
    }

    private void Awake()
    {
        if (root == null)
        {
            root = gameObject;
        }

        if (lobbyButton != null)
        {
            lobbyButton.onClick.AddListener(HandleLobbyButtonClicked);
        }

        if (root != null && !visibilityRequested)
        {
            root.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (lobbyButton != null)
        {
            lobbyButton.onClick.RemoveListener(HandleLobbyButtonClicked);
        }
    }

    public void Show(DungeonExitResult result, DungeonExitManager manager)
    {
        exitManager = manager;
        visibilityRequested = true;
        if (root == null) root = gameObject;

        if (root != null)
        {
            root.SetActive(true);
        }

        if (result == null)
        {
            if (elapsedTimeText != null) elapsedTimeText.text = string.Empty;
            if (escapedPlayersText != null) escapedPlayersText.text = string.Empty;
            if (playerKillsText != null) playerKillsText.text = string.Empty;
            if (monsterKillsText != null) monsterKillsText.text = string.Empty;
            if (goldText != null) goldText.text = string.Empty;
            if (itemsText != null) itemsText.text = "결과 정보가 없습니다.";
            if (itemScroll != null) itemScroll.gameObject.SetActive(false);
            return;
        }

        if (elapsedTimeText != null)
        {
            elapsedTimeText.text = FormatElapsedTime(result);
        }

        if (escapedPlayersText != null)
        {
            escapedPlayersText.text = FormatEscapedPlayers(result);
        }

        if (playerKillsText != null)
        {
            playerKillsText.text = $"잡은 플레이어: {result.playerKillCount}";
        }

        if (monsterKillsText != null)
        {
            monsterKillsText.text = $"잡은 몬스터: {result.monsterKillCount}";
        }

        if (goldText != null)
        {
            goldText.gameObject.SetActive(true);
            goldText.text = result.snapshotUnavailable ? "서버 보상 내역을 수신하지 못했습니다." :
                $"획득 골드: {result.gold:N0} · 전체 획득 가치: {result.totalValue:N0}\n던전 보스 처치: {result.bossKillCount}";
        }
        if (itemsText != null)
        {
            itemsText.gameObject.SetActive(true);
            RefreshItemRows(result);
        }
    }

    private void RefreshItemRows(DungeonExitResult result)
    {
        bool hasItems = !result.snapshotUnavailable && result.items != null && result.items.Count > 0;
        if (!hasItems)
        {
            itemsText.text = result.snapshotUnavailable ? "확인되지 않은 보상은 표시하지 않습니다." : "획득 아이템: 없음";
            if (itemScroll != null) itemScroll.gameObject.SetActive(false);
            return;
        }

        EnsureItemScroll();
        itemsText.text = result.serverConfirmedInventory ? "서버 확정 획득 아이템 (시작 지급품 제외)" : "획득 아이템";
        itemScroll.gameObject.SetActive(true);
        var catalog = ItemCatalog.Load();
        while (itemRows.Count < result.items.Count) itemRows.Add(CreateItemRow(itemRows.Count));
        for (int i = 0; i < itemRows.Count; i++)
        {
            var row = itemRows[i];
            row.Root.gameObject.SetActive(i < result.items.Count);
            if (i >= result.items.Count) continue;
            var entry = result.items[i];
            var item = catalog != null ? catalog.Resolve(entry.itemId) : null;
            ItemIconPresentation.Apply(row.Icon, item);
            row.Rarity.color = item != null ? item.RarityColor : Color.gray;
            string displayName = string.IsNullOrWhiteSpace(entry.displayName) ? entry.itemId : entry.displayName;
            string rarity = item != null ? $" · {item.RarityName}" : string.Empty;
            // The accepted extraction snapshot owns count and value; the catalog only supplies presentation.
            row.Label.text = $"{displayName} ×{entry.count}{rarity}\n개당 {entry.unitValue:N0} · 총 가치 {entry.TotalValue:N0}";
        }
        itemScroll.content.sizeDelta = new Vector2(0f, result.items.Count * ItemRowHeight);
        itemScroll.StopMovement();
        itemScroll.verticalNormalizedPosition = 1f;
    }

    private void EnsureItemScroll()
    {
        if (itemScroll != null) return;
        var viewport = CreateRect("Result Item Viewport", itemsText.transform);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero; viewport.offsetMax = new Vector2(0f, -32f);
        var background = viewport.gameObject.AddComponent<Image>();
        background.color = new Color(0.04f, 0.05f, 0.06f, 0.45f);
        viewport.gameObject.AddComponent<RectMask2D>();
        itemScroll = viewport.gameObject.AddComponent<ScrollRect>();
        itemScroll.viewport = viewport;
        itemScroll.horizontal = false; itemScroll.vertical = true;
        itemScroll.movementType = ScrollRect.MovementType.Clamped;
        itemScroll.scrollSensitivity = 32f;
        var content = CreateRect("Result Item Rows", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        itemScroll.content = content;
    }

    private ResultItemRow CreateItemRow(int index)
    {
        var rect = CreateRect("Result Item " + index, itemScroll.content);
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f); rect.sizeDelta = new Vector2(0f, ItemRowHeight);
        rect.anchoredPosition = new Vector2(0f, -index * ItemRowHeight);
        var strip = CreateRect("Rarity", rect);
        strip.anchorMin = new Vector2(0f, 0.15f); strip.anchorMax = new Vector2(0f, 0.85f);
        strip.pivot = new Vector2(0f, 0.5f); strip.sizeDelta = new Vector2(3f, 0f);
        strip.anchoredPosition = new Vector2(4f, 0f);
        var rarity = strip.gameObject.AddComponent<Image>(); rarity.raycastTarget = false;
        var iconRect = CreateRect("Item Icon", rect);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f); iconRect.sizeDelta = new Vector2(40f, 40f);
        iconRect.anchoredPosition = new Vector2(13f, 0f);
        var icon = iconRect.gameObject.AddComponent<Image>();
        var textRect = CreateRect("Item Summary", rect);
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(64f, 0f); textRect.offsetMax = new Vector2(-8f, 0f);
        var text = textRect.gameObject.AddComponent<Text>();
        text.font = itemsText.font; text.fontSize = 16; text.color = itemsText.color;
        text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false; text.raycastTarget = false;
        return new ResultItemRow { Root = rect, Icon = icon, Rarity = rarity, Label = text };
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private void HandleLobbyButtonClicked()
    {
        DungeonExitManager manager = exitManager != null
            ? exitManager
            : DungeonExitManager.Instance;

        if (manager != null)
        {
            manager.ReturnToLobby();
        }
    }

    private static string FormatElapsedTime(DungeonExitResult result)
    {
        return $"진행 시간: {result.ElapsedTime:hh\\:mm\\:ss}";
    }

    private static string FormatEscapedPlayers(DungeonExitResult result)
    {
        // 내부 ID("00001")가 아니라 닉네임을 보여준다.
        if (result.escapedPlayerNames != null && result.escapedPlayerNames.Count > 0)
        {
            return $"탈출 플레이어: {string.Join(", ", result.escapedPlayerNames)}";
        }

        if (result.escapedPlayerIds.Count == 0)
        {
            return $"탈출 플레이어: {result.escapedPlayerCount}";
        }

        return $"탈출 플레이어: {string.Join(", ", result.escapedPlayerIds)}";
    }

}
