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

    /// <summary>플레이 시간 측정을 지금부터 다시 시작한다. 던전 입장 시 호출.</summary>
    public void ResetTimer()
    {
        startedAt = Time.time;
        escapeCompleted = false;
        escapedPlayers.Clear();
        monsterKillCount = 0;
        playerKillCount = 0;
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

    // 탈출 결과창의 "로비로" 버튼.
    //
    // 이전 구현은 lobbySceneName("Lobby") 씬을 로드했는데 그런 씬이 프로젝트에
    // 없어서(Build Settings에도 없음) 버튼이 아무 반응도 하지 않았다.
    // 로비 UI는 인게임과 같은 씬 안에 있으므로, PauseMenu와 동일하게
    // "세션 연결 해제 + 현재 씬 새로고침 + 자동 재로그인" 으로 처리한다.
    public void ReturnToLobby()
    {
        var bootstrap = FindFirstObjectByType<NetworkBootstrap>();

        // 닉네임을 남겨두면 씬 재시작 후 자동 로그인 → 로비 화면으로 바로 복귀.
        string nick = (bootstrap != null && bootstrap.Identity != null)
            ? bootstrap.Identity.PlayerName
            : string.Empty;

        int cid = (bootstrap != null && bootstrap.Identity != null)
            ? bootstrap.Identity.LocalClientId : 0;

        PlayerPrefs.SetString(PauseMenu.PrefAutoNick, nick ?? string.Empty);
        PlayerPrefs.SetString(PauseMenu.PrefAutoPw, LobbyUI.LastPassword);
        PlayerPrefs.SetInt(PauseMenu.PrefAutoCid, cid);
        PlayerPrefs.SetInt(PauseMenu.PrefAutoLogin, 1);
        PlayerPrefs.Save();

        // 세션 소켓을 끊어 캐릭터를 서버에서 내보낸다.
        if (bootstrap != null && bootstrap.Session != null)
        {
            bootstrap.Session.Disconnect();
        }

        LoadSceneOrReload(lobbySceneName);
    }

    // 지정한 씬이 Build Settings에 있으면 그 씬을, 없으면 현재 씬을 다시 로드한다.
    // 별도 로비 씬을 나중에 만들더라도 이 코드를 고칠 필요가 없다.
    private void LoadSceneOrReload(string sceneName)
    {
        bool loadable = !string.IsNullOrWhiteSpace(sceneName)
            && Application.CanStreamedLevelBeLoaded(sceneName);

        string target = loadable ? sceneName : SceneManager.GetActiveScene().name;

        if (!loadable && !string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.Log($"[Exit] '{sceneName}' 씬이 Build Settings에 없어 현재 씬을 새로고침한다.");
        }

        try
        {
            SceneManager.LoadScene(target);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Exit] 씬 로드 실패 ({target}): {e.Message}", this);
        }
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

    // "00001" 같은 내부 ID 대신 실제 닉네임을 찾는다.
    //  1) 세션 로컬 플레이어면 로그인 닉네임
    //  2) 원격 플레이어면 오브젝트 이름 "RemotePlayer_3(홍길동)"에서 추출
    //  3) 못 찾으면 PlayerId
    private string ResolveDisplayName(Player player)
    {
        if (player == null) return string.Empty;

        if (cachedBootstrap == null) cachedBootstrap = FindFirstObjectByType<NetworkBootstrap>();
        if (cachedBootstrap != null)
        {
            if (cachedBootstrap.LocalPlayer == player.gameObject
                && cachedBootstrap.Identity != null
                && !string.IsNullOrWhiteSpace(cachedBootstrap.Identity.PlayerName))
            {
                return cachedBootstrap.Identity.PlayerName;
            }
        }

        string goName = player.gameObject.name;
        int open = goName.IndexOf('(');
        int close = goName.LastIndexOf(')');
        if (open >= 0 && close > open + 1) return goName.Substring(open + 1, close - open - 1);

        return player.PlayerId;
    }

    private NetworkBootstrap cachedBootstrap;

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

            string nick = ResolveDisplayName(player);
            if (!string.IsNullOrWhiteSpace(nick)) result.escapedPlayerNames.Add(nick);

            AddInventoryToResult(player, result);
        }

        return result;
    }

    // 서버가 "이건 네가 잡았다"고 알려줄 때 쓰는 경로.
    // 몬스터/플레이어 사망은 전부 서버가 판정하고 MonsterDied/PlayerDied로 내려온다.
    // 로컬 Monster_State.Die()는 네트워크 플레이에서 호출되지 않아 집계가 0이었다.
    public void RegisterNetworkMonsterKill() => monsterKillCount++;
    public void RegisterNetworkPlayerKill() => playerKillCount++;

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

        // 결과 씬도 없을 수 있으므로 동일하게 방어한다.
        LoadSceneOrReload(exitSceneName);
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
    public List<string> escapedPlayerNames = new List<string>();   // 표시용 닉네임
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
