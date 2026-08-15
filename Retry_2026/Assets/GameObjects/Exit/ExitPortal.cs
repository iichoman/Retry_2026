using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using System;

[DisallowMultipleComponent]
public class ExitPortal : MonoBehaviour
{
    [SerializeField] private DungeonExitManager exitManager;
    [SerializeField] private bool findManagerOnStart = true;
    [SerializeField] private bool requirePlayerComponent = true;
    [SerializeField] private bool disableAfterFirstEscape;

    [Header("Interaction")]
    [SerializeField, Min(0.1f)] private float holdDuration = 7f;
    [SerializeField] private ItemData requiredKey;
    [SerializeField] private bool consumeRequiredKey;

    [Header("Events")]
    [SerializeField] private UnityEvent<Player, float> holdProgressChanged;
    [SerializeField] private UnityEvent<Player> holdCanceled;
    [SerializeField] private UnityEvent<Player> missingRequiredKey;

    private bool used;

    // ── 서버 권위 탈출 ────────────────────────────────────────
    // 네트워크 세션에 연결된 경우 로컬에서 탈출을 확정하지 않는다.
    // 홀드가 끝나면 서버에 요청만 보내고 EXTRACTION_RESULT를 기다린다.
    [Header("Network")]
    [SerializeField] private int extractionPointId;
    private NetworkBootstrap bootstrap;
    private Player awaitingPlayer;      // 서버 응답 대기 중인 플레이어
    private readonly Dictionary<Player, HoldState> holdStates = new Dictionary<Player, HoldState>();
    private readonly List<Player> playersToRemove = new List<Player>();

    public float HoldDuration => holdDuration;
    public event Action<Player, float> HoldProgressChanged;
    public event Action<Player> HoldCanceled;
    public event Action<Player> MissingRequiredKey;

    private void Awake()
    {
        if (exitManager == null && findManagerOnStart)
        {
            exitManager = FindFirstObjectByType<DungeonExitManager>();
        }

        bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        if (bootstrap != null)
        {
            bootstrap.ExtractionResultReceived += OnExtractionResult;
        }
    }

    private void OnDestroy()
    {
        if (bootstrap != null)
        {
            bootstrap.ExtractionResultReceived -= OnExtractionResult;
        }
    }

    // 서버가 판정한 탈출 결과. 성공일 때만 실제 탈출 처리.
    private void OnExtractionResult(ExtractionResult result)
    {
        Player player = awaitingPlayer;
        awaitingPlayer = null;
        if (player == null) return;

        if (result.success != 1)
        {
            // 거부됨 → 홀드 리셋. 계속 서 있으면 재시도된다.
            InvokeHoldProgressChanged(player, 0f);
            InvokeHoldCanceled(player);
            return;
        }

        CompleteEscape(player);
    }

    // 탈출 확정 (오프라인이거나 서버 승인 후).
    private void CompleteEscape(Player player)
    {
        if (exitManager == null) return;

        exitManager.NotifyPlayerEnteredExit(player);

        if (disableAfterFirstEscape && exitManager.EscapeCompleted)
        {
            used = true;
            gameObject.SetActive(false);
        }
    }

    // 본인 캐릭터인지 (원격 플레이어가 포탈에 들어와도 요청하지 않도록).
    private bool IsLocalPlayer(Player player)
    {
        if (bootstrap == null || bootstrap.LocalPlayer == null) return false;
        return player != null && player.gameObject == bootstrap.LocalPlayer;
    }

    private void Reset()
    {
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleTriggerEnter(other);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleTriggerStay(other);
    }

    private void OnTriggerExit(Collider other)
    {
        HandleTriggerExit(other);
    }

    public void HandleTriggerEnter(Collider other)
    {
        TrySetCurrentPlayer(other);
    }

    public void HandleTriggerStay(Collider other)
    {
        TrySetCurrentPlayer(other);
    }

    public void HandleTriggerExit(Collider other)
    {
        Player player = FindPlayer(other);
        if (player == null)
        {
            return;
        }

        RemovePlayer(player, true);
    }

    private void Update()
    {
        if (used || holdStates.Count == 0)
        {
            return;
        }

        playersToRemove.Clear();

        foreach (KeyValuePair<Player, HoldState> pair in holdStates)
        {
            Player player = pair.Key;
            HoldState state = pair.Value;

            if (player == null || state.input == null)
            {
                playersToRemove.Add(player);
                continue;
            }

            if (!state.input.Interact)
            {
                if (state.timer > 0f)
                {
                    state.timer = 0f;
                    state.nextCountdownSecond = 0;
                    InvokeHoldProgressChanged(player, 0f);
                    InvokeHoldCanceled(player);
                }

                continue;
            }

            if (!HasRequiredKey(player))
            {
                InvokeMissingRequiredKey(player);
                state.timer = 0f;
                state.nextCountdownSecond = 0;
                InvokeHoldProgressChanged(player, 0f);
                InvokeHoldCanceled(player);
                continue;
            }

            state.timer += Time.deltaTime;
            InvokeHoldProgressChanged(player, GetProgress(state.timer));

            if (state.timer >= holdDuration)
            {
                playersToRemove.Add(player);
                TryEscape(player);
            }
        }

        for (int i = 0; i < playersToRemove.Count; i++)
        {
            RemovePlayer(playersToRemove[i], false);
        }
    }

    private void TrySetCurrentPlayer(Collider other)
    {
        if (used || other == null)
        {
            return;
        }

        Player player = FindPlayer(other);
        if (player == null || holdStates.ContainsKey(player))
        {
            return;
        }

        Defalult_Input input = player.GetComponent<Defalult_Input>();
        if (input == null)
        {
            input = player.GetComponentInParent<Defalult_Input>();
        }

        if (input == null)
        {
            return;
        }

        holdStates.Add(player, new HoldState(input));
        InvokeHoldProgressChanged(player, 0f);
    }

    private void TryEscape(Player player)
    {
        if (used || player == null || exitManager == null)
        {
            return;
        }

        if (!TryConsumeRequiredKey(player))
        {
            InvokeMissingRequiredKey(player);
            RemovePlayer(player, true);
            return;
        }

        // 네트워크 세션이면 서버 판정에 맡긴다 (위치/체류시간 재검증).
        if (bootstrap != null && bootstrap.Session != null && bootstrap.Session.IsConnected)
        {
            if (!IsLocalPlayer(player)) return;   // 원격 캐릭터는 요청하지 않음
            awaitingPlayer = player;
            bootstrap.RequestExtraction(extractionPointId);
            return;
        }

        // 오프라인(단독 테스트) 경로: 기존대로 즉시 확정.
        CompleteEscape(player);
    }

    private Player FindPlayer(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player == null)
        {
            player = other.GetComponentInParent<Player>();
        }

        if (player == null && !requirePlayerComponent)
        {
            player = FindPlayerFromCollider(other);
        }

        return player;
    }

    private bool HasRequiredKey(Player player)
    {
        if (requiredKey == null)
        {
            return true;
        }

        PlayerInventory inventory = FindInventory(player);
        return inventory != null && inventory.Count(requiredKey) > 0;
    }

    private bool TryConsumeRequiredKey(Player player)
    {
        if (requiredKey == null)
        {
            return true;
        }

        PlayerInventory inventory = FindInventory(player);
        if (inventory == null || inventory.Count(requiredKey) <= 0)
        {
            return false;
        }

        return !consumeRequiredKey || inventory.TryRemove(requiredKey);
    }

    private static PlayerInventory FindInventory(Player player)
    {
        if (player == null)
        {
            return null;
        }

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            inventory = player.GetComponentInParent<PlayerInventory>();
        }

        return inventory;
    }

    private void RemovePlayer(Player player, bool notifyCanceled)
    {
        if (player != null && holdStates.ContainsKey(player))
        {
            InvokeHoldProgressChanged(player, 0f);
            if (notifyCanceled)
            {
                InvokeHoldCanceled(player);
            }
        }

        if (player != null)
        {
            holdStates.Remove(player);
        }
    }

    private float GetProgress(float timer)
    {
        return holdDuration <= 0f ? 1f : Mathf.Clamp01(timer / holdDuration);
    }

    private void InvokeHoldProgressChanged(Player player, float progress)
    {
        holdProgressChanged?.Invoke(player, progress);
        HoldProgressChanged?.Invoke(player, progress);
    }

    private void InvokeHoldCanceled(Player player)
    {
        holdCanceled?.Invoke(player);
        HoldCanceled?.Invoke(player);
    }

    private void InvokeMissingRequiredKey(Player player)
    {
        missingRequiredKey?.Invoke(player);
        MissingRequiredKey?.Invoke(player);
    }

    private static Player FindPlayerFromCollider(Collider other)
    {
        Player_State state = other.GetComponent<Player_State>();
        if (state == null)
        {
            state = other.GetComponentInParent<Player_State>();
        }

        return state != null ? state.GetComponent<Player>() : null;
    }

    private sealed class HoldState
    {
        public readonly Defalult_Input input;
        public float timer;
        public int nextCountdownSecond = -1;

        public HoldState(Defalult_Input input)
        {
            this.input = input;
        }
    }
}