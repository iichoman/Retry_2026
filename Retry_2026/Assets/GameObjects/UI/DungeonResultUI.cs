using System.Text;
using UnityEngine;
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

        if (root != null)
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

        if (root != null)
        {
            root.SetActive(true);
        }

        if (result == null)
        {
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
            goldText.text = $"골드: {result.gold}";
        }

        if (itemsText != null)
        {
            itemsText.text = FormatItems(result);
        }
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
        if (result.escapedPlayerIds.Count == 0)
        {
            return $"탈출 플레이어: {result.escapedPlayerCount}";
        }

        return $"탈출 플레이어: {string.Join(", ", result.escapedPlayerIds)}";
    }

    private static string FormatItems(DungeonExitResult result)
    {
        if (result.items.Count == 0)
        {
            return "획득 아이템: 없음";
        }

        var builder = new StringBuilder();
        builder.AppendLine("획득 아이템");

        for (int i = 0; i < result.items.Count; i++)
        {
            DungeonExitItemSummary item = result.items[i];
            string itemName = string.IsNullOrWhiteSpace(item.displayName)
                ? item.itemId
                : item.displayName;

            builder.Append("- ");
            builder.Append(itemName);
            builder.Append(" x");
            builder.Append(item.count);

            if (i < result.items.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
