using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class DungeonExitManager : MonoBehaviour
{
    [Header("Scene Flow")]
    [SerializeField] private bool loadExitSceneOnEscape;
    [SerializeField] private string exitSceneName = "ExitResult";
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField, Min(0f)] private float exitSceneLoadDelay = 0.5f;

    [Header("Rules")]
    [SerializeField] private bool requireAllPlayersToEscape;
    [SerializeField] private bool registerScenePlayersOnStart = true;
    [SerializeField] private List<Player> registeredPlayers = new List<Player>();

    [Header("Completion")]
    [SerializeField] private bool disablePlayerControlOnEscape = true;
    [SerializeField] private bool deactivatePlayerGameObjectsOnEscape;
    [SerializeField] private bool showResultPanelOnEscape = true;
    [SerializeField] private DungeonResultUI resultUI;
    [SerializeField] private GameObject resultPanelRoot;

    [Header("Events")]
    [SerializeField] private UnityEvent<DungeonExitResult> escaped;
    [SerializeField] private UnityEvent<Player> playerEscaped;

    private readonly HashSet<Player> escapedPlayers = new HashSet<Player>();
    private float startedAt;
    private bool escapeCompleted;
    private float exitSceneTimer = -1f;
    private int monsterKillCount;
    private int playerKillCount;

    public static DungeonExitManager Instance { get; private set; }
    public static DungeonExitResult LastResult { get; private set; }

    public event Action<DungeonExitResult> Escaped;
    public event Action<Player> PlayerEscaped;

    public float ElapsedTime => Mathf.Max(0f, Time.time - startedAt);
    public bool EscapeCompleted => escapeCompleted;
    public IReadOnlyCollection<Player> EscapedPlayers => escapedPlayers;

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }

        startedAt = Time.time;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (resultPanelRoot != null)
        {
            resultPanelRoot.SetActive(false);
        }

        if (!registerScenePlayersOnStart)
        {
            return;
        }

        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            RegisterPlayer(player);
        }
    }

    private void Update()
    {
        if (exitSceneTimer < 0f)
        {
            return;
        }

        exitSceneTimer -= Time.deltaTime;
        if (exitSceneTimer <= 0f)
        {
            exitSceneTimer = -1f;
            LoadExitScene();
        }
    }

    public void RegisterPlayer(Player player)
    {
        if (player == null || registeredPlayers.Contains(player))
        {
            return;
        }

        registeredPlayers.Add(player);
    }

    public void UnregisterPlayer(Player player)
    {
        if (player == null)
        {
            return;
        }

        registeredPlayers.Remove(player);
        escapedPlayers.Remove(player);
    }

    public void NotifyPlayerEnteredExit(Player player)
    {
        if (escapeCompleted || player == null)
        {
            return;
        }

        RegisterPlayer(player);

        if (!escapedPlayers.Add(player))
        {
            return;
        }

        playerEscaped?.Invoke(player);
        PlayerEscaped?.Invoke(player);

        if (!ShouldCompleteEscape())
        {
            return;
        }

        CompleteEscape(player);
    }

    public void ReturnToLobby()
    {
        if (string.IsNullOrWhiteSpace(lobbySceneName))
        {
            Debug.LogWarning("Lobby scene name is empty.", this);
            return;
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    private bool ShouldCompleteEscape()
    {
        if (!requireAllPlayersToEscape)
        {
            return true;
        }

        RemoveMissingPlayers();
        return registeredPlayers.Count > 0 && escapedPlayers.Count >= registeredPlayers.Count;
    }

    private void CompleteEscape(Player finalPlayer)
    {
        escapeCompleted = true;
        DungeonExitResult result = BuildResult(finalPlayer);
        LastResult = result;

        escaped?.Invoke(result);
        Escaped?.Invoke(result);

        if (disablePlayerControlOnEscape || deactivatePlayerGameObjectsOnEscape)
        {
            DisablePlayersAfterEscape();
        }

        if (showResultPanelOnEscape)
        {
            ShowResultPanel(result);
        }

        if (loadExitSceneOnEscape)
        {
            exitSceneTimer = exitSceneLoadDelay;
        }
    }

    private DungeonExitResult BuildResult(Player finalPlayer)
    {
        var result = new DungeonExitResult
        {
            elapsedSeconds = ElapsedTime,
            finalPlayerId = finalPlayer != null ? finalPlayer.PlayerId : string.Empty,
            escapedPlayerCount = escapedPlayers.Count,
            monsterKillCount = monsterKillCount,
            playerKillCount = playerKillCount
        };

        foreach (Player player in escapedPlayers)
        {
            if (player != null && !string.IsNullOrWhiteSpace(player.PlayerId))
            {
                result.escapedPlayerIds.Add(player.PlayerId);
            }

            AddInventoryToResult(player, result);
        }

        return result;
    }

    public void RegisterMonsterKill(GameObject attacker)
    {
        if (FindPlayerFromObject(attacker) == null)
        {
            return;
        }

        monsterKillCount++;
    }

    public void RegisterPlayerKill(Player victim, GameObject attacker)
    {
        Player killer = FindPlayerFromObject(attacker);
        if (killer == null || killer == victim)
        {
            return;
        }

        playerKillCount++;
    }

    private static void AddInventoryToResult(Player player, DungeonExitResult result)
    {
        if (player == null)
        {
            return;
        }

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            inventory = player.GetComponentInParent<PlayerInventory>();
        }

        if (inventory == null)
        {
            return;
        }

        foreach (InventorySlot slot in inventory.Slots)
        {
            if (slot == null || slot.IsEmpty)
            {
                continue;
            }

            if (slot.item.ItemType == ItemType.Currency)
            {
                result.gold += slot.count;
            }

            result.AddItem(slot.item, slot.count);
        }
    }

    private void LoadExitScene()
    {
        if (string.IsNullOrWhiteSpace(exitSceneName))
        {
            Debug.LogWarning("Exit scene name is empty.", this);
            return;
        }

        SceneManager.LoadScene(exitSceneName);
    }

    private void RemoveMissingPlayers()
    {
        for (int i = registeredPlayers.Count - 1; i >= 0; i--)
        {
            if (registeredPlayers[i] != null)
            {
                continue;
            }

            registeredPlayers.RemoveAt(i);
        }
    }

    private void DisablePlayersAfterEscape()
    {
        RemoveMissingPlayers();

        List<Player> targets = registeredPlayers.Count > 0
            ? registeredPlayers
            : new List<Player>(escapedPlayers);

        for (int i = 0; i < targets.Count; i++)
        {
            DisablePlayer(targets[i]);
        }
    }

    private void DisablePlayer(Player player)
    {
        if (player == null)
        {
            return;
        }

        if (deactivatePlayerGameObjectsOnEscape)
        {
            player.gameObject.SetActive(false);
            return;
        }

        Defalult_Input input = player.GetComponent<Defalult_Input>();
        if (input != null)
        {
            input.enabled = false;
        }

        Player_Movement movement = player.GetComponent<Player_Movement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        Player_Attack attack = player.GetComponent<Player_Attack>();
        if (attack != null)
        {
            attack.enabled = false;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }
    }

    private void ShowResultPanel(DungeonExitResult result)
    {
        if (resultPanelRoot != null)
        {
            resultPanelRoot.SetActive(true);
        }

        if (resultUI == null && resultPanelRoot != null)
        {
            resultUI = resultPanelRoot.GetComponentInChildren<DungeonResultUI>(true);
        }

        if (resultUI == null)
        {
            resultUI = FindFirstObjectByType<DungeonResultUI>(FindObjectsInactive.Include);
        }

        if (resultUI != null)
        {
            resultUI.Show(result, this);
        }
    }

    private static Player FindPlayerFromObject(GameObject source)
    {
        if (source == null)
        {
            return null;
        }

        Player player = source.GetComponent<Player>();
        if (player == null)
        {
            player = source.GetComponentInParent<Player>();
        }

        return player;
    }
}

[Serializable]
public class DungeonExitResult
{
    public float elapsedSeconds;
    public string finalPlayerId;
    public int escapedPlayerCount;
    public int monsterKillCount;
    public int playerKillCount;
    public int gold;
    public List<string> escapedPlayerIds = new List<string>();
    public List<DungeonExitItemSummary> items = new List<DungeonExitItemSummary>();

    public TimeSpan ElapsedTime => TimeSpan.FromSeconds(Mathf.Max(0f, elapsedSeconds));

    public void AddItem(ItemData item, int count)
    {
        if (item == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].itemId != item.ItemId)
            {
                continue;
            }

            items[i].count += count;
            return;
        }

        items.Add(new DungeonExitItemSummary
        {
            itemId = item.ItemId,
            displayName = item.DisplayName,
            itemType = item.ItemType,
            count = count
        });
    }
}

[Serializable]
public class DungeonExitItemSummary
{
    public string itemId;
    public string displayName;
    public ItemType itemType;
    public int count;
}
