using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
//  ChatUI
//
//  채팅창을 문맥별로 나눠 관리한다. NetworkBootstrap이 자식으로 자동 생성.
//
//  창 구성
//   로비 창 : 화면 좌하단. 스스로 캔버스를 만들어 그린다.
//             로비에서는 [전체] 고정, 세션에서는 [파티]/[팀].
//   파티 창 : 파티 대기실 UI 안. LobbyUI가 위젯을 만들어 BindPartyPanel로 넘긴다.
//             [파티] 고정.
//
//  조작
//   Enter : 입력창 켜기. 파티에 들어와 있으면 파티창이, 아니면 로비창이 열린다.
//           켜진 상태에서 Enter → 내용이 있으면 전송 후 유지, 비어 있으면 닫기.
//   Tab   : 세션에서만 [파티] ↔ [팀] 전환. 로비는 창이 나뉘어 있어 필요 없다.
//   Esc   : 입력 취소하고 닫기
//   파티 입력창을 마우스로 클릭해도 타이핑 상태가 된다.
//
//  로그인 전에는 아무 입력도 받지 않는다.
//  닉네임 입력창의 Enter가 채팅창을 여는 것을 막기 위해서다.
//
//  입력 차단
//   타이핑 중 IsTyping이 true가 되고, 이동/공격/루팅/치트키가 이를 보고 멈춘다.
// ============================================================================
[DisallowMultipleComponent]
public class ChatUI : MonoBehaviour
{
    /// <summary>채팅 입력 중인가. 다른 입력 컴포넌트가 이 값을 보고 자기 입력을 무시한다.</summary>
    public static bool IsTyping { get; private set; }

    [Header("표시")]
    // 화면에 보이는 줄 수는 로그창 높이가 정한다. 이건 "보관"하는 줄 수.
    [SerializeField, Min(1)] private int maxLines = 300;
    // 파티창은 6줄만 보이고(뷰포트 높이) 넘친 건 스크롤로 본다.
    // 여기 값은 "보관"하는 최대 줄 수라 넉넉하게 둔다.
    [SerializeField, Min(1)] private int maxPartyLines = 300;
    [SerializeField, Min(1f)] private float fadeAfterSec = 12f; // 유휴 시 흐려지기까지
    [SerializeField] private int fontSize = 15;

    private NetworkBootstrap bootstrap;

    // 로비/세션 창 (자체 생성)
    private Text logText;
    private Text channelLabel;
    private InputField inputField;
    private GameObject inputRow;
    private GameObject logGo;
    private bool inputRowWanted;
    private CanvasGroup logGroup;
    private ScrollRect logScroll;

    // 파티 창 (LobbyUI가 넘겨줌)
    private GameObject partyPanelRoot;
    private Text partyLogText;
    private InputField partyInputField;

    private readonly List<string> lines = new List<string>();
    private readonly List<string> partyLines = new List<string>();

    private ChatChannel channel = ChatChannel.All;
    private bool wasInSession;            // 세션 진입 순간을 잡아 로비 로그를 비우는 용도
    private float lastMessageTime;

    private enum Target { None, Lobby, Party }
    private Target typing = Target.None;

    private static readonly Color ColAll = new Color(0.90f, 0.90f, 0.92f);
    private static readonly Color ColParty = new Color(0.45f, 0.85f, 1.00f);
    private static readonly Color ColTeam = new Color(0.55f, 1.00f, 0.60f);
    private static readonly Color ColSystem = new Color(1.00f, 0.83f, 0.47f);
    private static readonly Color ColPanel = new Color(0f, 0f, 0f, 0.55f);

    // ── 문맥 판정 ────────────────────────────────────────────────

    private bool InSession =>
        bootstrap != null && bootstrap.Session != null && bootstrap.Session.IsConnected;

    // 로그인 전(타이틀/닉네임 화면)에는 채팅 자체를 잠근다.
    private bool CanChat =>
        bootstrap != null && bootstrap.Identity != null && bootstrap.Identity.IsAuthenticated;

    /// <summary>로비에서 파티(방)에 들어와 있는지. 로비 패킷으로 직접 추적한다.</summary>
    public bool InRoom { get; private set; }

    private bool PartyAvailable =>
        !InSession && InRoom && partyPanelRoot != null
        && partyPanelRoot.activeInHierarchy && partyInputField != null;

    // ── 초기화 ───────────────────────────────────────────────────

    public void Initialize(NetworkBootstrap bs)
    {
        bootstrap = bs;
        if (bootstrap != null)
        {
            bootstrap.ChatMessageReceived += OnChatMessage;
            bootstrap.LobbyPacket += OnLobbyPacketForRoomState;
        }
        BuildUI();
        ValidateChannel();
    }

    private void OnDestroy()
    {
        if (bootstrap != null)
        {
            bootstrap.ChatMessageReceived -= OnChatMessage;
            bootstrap.LobbyPacket -= OnLobbyPacketForRoomState;
        }
        IsTyping = false;
    }

    /// <summary>파티 대기실 안의 채팅 위젯을 넘겨받는다. LobbyUI가 호출.</summary>
    public void BindPartyPanel(GameObject root, Text log, InputField field)
    {
        partyPanelRoot = root;
        partyLogText = log;
        partyInputField = field;
        partyScroll = root != null ? root.GetComponentInChildren<ScrollRect>(true) : null;

        if (partyInputField != null)
        {
            partyInputField.lineType = InputField.LineType.SingleLine;
            partyInputField.characterLimit = 40;
        }
        RedrawParty();
    }

    // 방 입장/퇴장 추적. [파티] 창을 쓸 수 있는지 판단하는 근거다.
    private void OnLobbyPacketForRoomState(PacketType type, byte[] body)
    {
        switch ((int)type)
        {
            case (int)PacketType.ROOM_CREATE_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomCreateResult>(body, 0);
                    if (r.success != 0) { InRoom = true; partyLines.Clear(); RedrawParty(); }
                    break;
                }
            case (int)PacketType.ROOM_JOIN_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomJoinResult>(body, 0);
                    if (r.success != 0) { InRoom = true; partyLines.Clear(); RedrawParty(); }
                    break;
                }
            case LobbyPacketType.ROOM_STATE:
                InRoom = true;
                break;
            case LobbyPacketType.ROOM_LEAVE_RESULT:
                InRoom = false;
                partyLines.Clear();
                RedrawParty();
                break;
        }
    }

    // ── 채널 규칙 ────────────────────────────────────────────────

    // Tab 전환은 세션에서만. 로비는 창이 채널별로 나뉘어 있다.
    private void CycleChannel()
    {
        if (!InSession) return;
        channel = (channel == ChatChannel.Party) ? ChatChannel.Team : ChatChannel.Party;
        ApplyChannelToLabel();
    }

    private void ValidateChannel()
    {
        if (InSession)
        {
            if (channel != ChatChannel.Party && channel != ChatChannel.Team)
            {
                channel = ChatChannel.Party;
                ApplyChannelToLabel();
            }
        }
        else if (channel != ChatChannel.All)
        {
            channel = ChatChannel.All;
            ApplyChannelToLabel();
        }
    }

    private static string ChannelTag(ChatChannel c)
    {
        switch (c)
        {
            case ChatChannel.Team: return "[팀]";
            case ChatChannel.Party: return "[파티]";
            case ChatChannel.System: return "[시스템]";
            default: return "[전체]";
        }
    }

    private static Color ChannelColor(ChatChannel c)
    {
        switch (c)
        {
            case ChatChannel.Team: return ColTeam;
            case ChatChannel.Party: return ColParty;
            case ChatChannel.System: return ColSystem;
            default: return ColAll;
        }
    }

    // ── 입력 ─────────────────────────────────────────────────────

    private void Update()
    {
        if (bootstrap == null) return;

        ValidateChannel();

        // 던전에 들어가는 순간, 로비에서 받아 둔 [전체] 방송을 비운다.
        if (InSession != wasInSession)
        {
            wasInSession = InSession;
            if (wasInSession)
            {
                lines.Clear();
                if (logText != null) logText.text = string.Empty;
            }
        }

        // 로그인 전에는 어떤 입력도 받지 않는다.
        if (!CanChat)
        {
            if (typing != Target.None) StopTyping();
            UpdateLogVisibility();
            return;
        }

        if (typing != Target.None)
        {
            InputField f = (typing == Target.Party) ? partyInputField : inputField;
            if (f == null) { StopTyping(); return; }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                f.text = string.Empty;
                StopTyping();
                UpdateLogVisibility();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab) && typing == Target.Lobby && InSession)
            {
                CycleChannel();
                FocusInput(f);
                return;
            }

            if (SubmitPressed())
            {
                string body = (f.text ?? string.Empty).Trim();
                if (body.Length > 0)
                {
                    Send(body, typing);
                    f.text = string.Empty;
                    FocusInput(f);            // 전송 후에도 입력창 유지
                }
                else
                {
                    StopTyping();             // 빈 입력이면 닫기
                }
            }
        }
        else
        {
            // 마우스로 파티 입력창을 클릭한 경우도 타이핑으로 인정한다.
            if (PartyAvailable && partyInputField.isFocused)
            {
                BeginTyping(Target.Party);
            }
            else if (SubmitPressed() && !IsOtherUIOpen())
            {
                // 파티에 들어와 있으면 Enter는 파티창을 연다.
                BeginTyping(PartyAvailable ? Target.Party : Target.Lobby);
            }
        }

        UpdateLogVisibility();
    }

    private static bool SubmitPressed()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    private bool IsOtherUIOpen()
    {
        var input = FindPlayerInput();
        return input != null && !input.IsGameplayInputEnabled;
    }

    private Defalult_Input FindPlayerInput()
    {
        if (bootstrap == null || bootstrap.LocalPlayer == null) return null;
        return bootstrap.LocalPlayer.GetComponent<Defalult_Input>();
    }

    private void BeginTyping(Target t)
    {
        typing = t;
        IsTyping = true;

        if (t == Target.Lobby)
        {
            inputRowWanted = true;
            inputRow.SetActive(true);
            ApplyChannelToLabel();
            FocusInput(inputField);
        }
        else
        {
            FocusInput(partyInputField);
        }

        // 인게임에서는 이동/시점/공격을 막는다. 로비엔 캐릭터가 없어 null.
        var input = FindPlayerInput();
        if (input != null) input.SetInputMode(PlayerInputMode.UI);
    }

    private void StopTyping()
    {
        typing = Target.None;
        IsTyping = false;

        inputRowWanted = false;
        if (inputRow != null) inputRow.SetActive(false);
        if (inputField != null) inputField.DeactivateInputField();
        if (partyInputField != null) partyInputField.DeactivateInputField();

        var input = FindPlayerInput();
        if (input != null) input.SetInputMode(PlayerInputMode.Gameplay);

        lastMessageTime = Time.unscaledTime;
    }

    private static void FocusInput(InputField f)
    {
        if (f == null) return;
        f.ActivateInputField();
        f.Select();
        f.caretPosition = f.text.Length;
    }

    private void Send(string body, Target from)
    {
        if (InSession)
        {
            ChatChannel c = (channel == ChatChannel.Team) ? ChatChannel.Team : ChatChannel.Party;
            bootstrap.Session.SendChat(c, body);
            return;
        }

        if (bootstrap.Lobby == null || !bootstrap.Lobby.IsConnected) return;

        // 파티창에서 보내면 [파티], 로비창에서 보내면 [전체].
        ChatChannel lobbyCh = (from == Target.Party && InRoom) ? ChatChannel.Party : ChatChannel.All;
        bootstrap.Lobby.SendChat(lobbyCh, body);
    }

    // ── 수신 ─────────────────────────────────────────────────────

    private void OnChatMessage(ChatMessageData msg)
    {
        string tag = ChannelTag(msg.Channel);
        string hex = ColorUtility.ToHtmlStringRGB(ChannelColor(msg.Channel));

        string line;
        if (msg.Channel == ChatChannel.System)
        {
            line = $"<color=#{hex}>{tag} {msg.Text}</color>";
        }
        else
        {
            string name = msg.SenderName;
            if (string.IsNullOrEmpty(name)) name = $"#{msg.senderId}";

            string who = (msg.Channel == ChatChannel.Team && msg.senderTeam >= 0)
                ? $"{name}(팀{msg.senderTeam + 1})"
                : name;

            line = $"<color=#{hex}>{tag} {who}</color> : {msg.Text}";
        }

        // 로비에서는 채널별로 창을 나눈다. 세션에서는 한 창만 쓴다.
        bool toParty = !InSession &&
                       (msg.Channel == ChatChannel.Party || msg.Channel == ChatChannel.System);

        if (toParty) AppendParty(line);
        else AppendLobby(line);
    }

    /// <summary>클라가 직접 만드는 [시스템] 줄.</summary>
    public void AppendSystem(string text)
    {
        string hex = ColorUtility.ToHtmlStringRGB(ColSystem);
        string line = $"<color=#{hex}>[시스템] {text}</color>";
        if (!InSession && InRoom) AppendParty(line);
        else AppendLobby(line);
    }

    private void AppendLobby(string line)
    {
        lines.Add(line);
        while (lines.Count > maxLines) lines.RemoveAt(0);
        if (logText != null) logText.text = string.Join("\n", lines);
        lastMessageTime = Time.unscaledTime;
        if (logGroup != null) logGroup.alpha = 1f;
        if (isActiveAndEnabled) StartCoroutine(ScrollLobbyToBottom());
    }

    // 레이아웃이 갱신된 다음 프레임에 내려야 맨 아래로 정확히 붙는다.
    private System.Collections.IEnumerator ScrollLobbyToBottom()
    {
        yield return null;
        if (logScroll != null) logScroll.verticalNormalizedPosition = 0f;
    }

    private void AppendParty(string line)
    {
        partyLines.Add(line);
        while (partyLines.Count > maxPartyLines) partyLines.RemoveAt(0);
        RedrawParty();
    }

    private ScrollRect partyScroll;

    private void RedrawParty()
    {
        if (partyLogText == null) return;
        partyLogText.text = string.Join("\n", partyLines);
        if (partyScroll != null) StartCoroutine(ScrollPartyToBottom());
    }

    // ContentSizeFitter가 높이를 갱신한 다음 프레임에 맨 아래로 붙인다.
    private System.Collections.IEnumerator ScrollPartyToBottom()
    {
        yield return null;
        if (partyScroll != null) partyScroll.verticalNormalizedPosition = 0f;
    }

    private void UpdateLogVisibility()
    {
        if (logGroup == null) return;

        // 로그인 전(시작 화면)과 파티 대기실에서는 [전체] 로그/입력줄을 통째로 끈다.
        //  · 시작 화면 : 켜 두면 투명해도 레이캐스트를 먹어 비번 칸·시작 버튼이 안 눌린다
        //  · 대기실    : 방 위에 겹쳐 그려 가린다. 대기실 안에서는 파티 채팅을 쓴다
        bool hide = !CanChat || PartyAvailable;
        if (logGo != null && logGo.activeSelf == hide) logGo.SetActive(!hide);
        bool wantRow = !hide && inputRowWanted;
        if (inputRow != null && inputRow.activeSelf != wantRow) inputRow.SetActive(wantRow);
        if (hide) return;

        // 클릭/휠은 채팅을 연 동안에만 가로챈다.
        // 상시 켜 두면 로그창 넓이(760x170)만큼 뒤쪽 버튼이 통째로 죽는다.
        // 스크롤로 지난 대화를 보려면 Enter로 채팅을 열면 된다.
        logGroup.blocksRaycasts = (typing == Target.Lobby);

        if (typing == Target.Lobby) { logGroup.alpha = 1f; return; }
        if (lines.Count == 0) { logGroup.alpha = 0f; return; }

        float idle = Time.unscaledTime - lastMessageTime;
        logGroup.alpha = idle < fadeAfterSec ? 1f : Mathf.Max(0f, 1f - (idle - fadeAfterSec));
    }

    private void ApplyChannelToLabel()
    {
        if (channelLabel == null) return;
        channelLabel.text = ChannelTag(channel);
        channelLabel.color = ChannelColor(channel);
    }

    // ── 로비/세션 창 생성 (씬 작업 없이 코드로) ──────────────────

    private void BuildUI()
    {
        var canvasGo = new GameObject("ChatCanvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;                  // 로비 UI 위에 그린다

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // 로그 영역 (좌하단)
        logGo = new GameObject("ChatLog");
        logGo.transform.SetParent(canvasGo.transform, false);
        var logRt = logGo.AddComponent<RectTransform>();
        logRt.anchorMin = Vector2.zero;
        logRt.anchorMax = Vector2.zero;
        logRt.pivot = Vector2.zero;
        // 로그창과 입력줄은 한 덩어리다. ChatBottom 하나만 고치면 통째로 움직인다.
        // (좌하단 HP/스태미나 HUD 위로 충분히 띄워 둔 값)
        const float ChatX = 24f;
        // 스테이터스 UI(좌하단 패널)는 y 28~154를 쓴다. 그 위로 한 뼘(26px) 띄운다.
        const float ChatBottom = 180f;   // 입력줄 아래변
        const float ChatW = 760f;
        const float RowH = 40f;          // 입력줄 높이
        const float LogH = 170f;         // 로그창 높이

        logRt.anchoredPosition = new Vector2(ChatX, ChatBottom + RowH);   // 입력줄 바로 위 = 딱 붙음
        logRt.sizeDelta = new Vector2(ChatW, LogH);
        logGroup = logGo.AddComponent<CanvasGroup>();
        logGroup.interactable = false;
        logGroup.blocksRaycasts = false;       // 채팅을 연 동안에만 켠다
        logGo.AddComponent<Image>().color = ColPanel;

        // 지난 대화를 휠로 되짚어 볼 수 있게 스크롤 영역을 만든다.
        // 구조: ChatLog(ScrollRect) > Viewport(RectMask2D) > Content(자동 높이) > Text
        var vp = new GameObject("Viewport", typeof(RectTransform));
        vp.transform.SetParent(logGo.transform, false);
        var vpRt = vp.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = new Vector2(10f, 8f);
        vpRt.offsetMax = new Vector2(-10f, -8f);
        vp.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);   // 마스크용 최소 그래픽
        vp.AddComponent<RectMask2D>();

        // ContentSizeFitter는 자기 오브젝트의 Text 크기를 읽는다.
        // Text를 자식으로 두면 높이가 0이라 스크롤이 안 먹어서, Text 자체를 content로 쓴다.
        logText = MakeText(vp.transform, fontSize, ColAll, TextAnchor.LowerLeft);
        var cRt = logText.rectTransform;
        cRt.anchorMin = new Vector2(0f, 0f);
        cRt.anchorMax = new Vector2(1f, 0f);
        cRt.pivot = new Vector2(0.5f, 0f);
        cRt.offsetMin = Vector2.zero;
        cRt.offsetMax = Vector2.zero;
        logText.verticalOverflow = VerticalWrapMode.Overflow;
        logText.raycastTarget = false;
        var fit = logText.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        logScroll = logGo.AddComponent<ScrollRect>();
        logScroll.content = cRt;
        logScroll.viewport = vpRt;
        logScroll.horizontal = false;
        logScroll.vertical = true;
        logScroll.movementType = ScrollRect.MovementType.Clamped;
        logScroll.scrollSensitivity = 18f;

        // 입력 줄
        inputRow = new GameObject("ChatInputRow");
        inputRow.transform.SetParent(canvasGo.transform, false);
        var rowRt = inputRow.AddComponent<RectTransform>();
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.zero;
        rowRt.pivot = Vector2.zero;
        rowRt.anchoredPosition = new Vector2(ChatX, ChatBottom);
        rowRt.sizeDelta = new Vector2(ChatW, RowH);
        inputRow.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        var chGo = new GameObject("Channel");
        chGo.transform.SetParent(inputRow.transform, false);
        var chRt = chGo.AddComponent<RectTransform>();
        chRt.anchorMin = new Vector2(0f, 0f);
        chRt.anchorMax = new Vector2(0f, 1f);
        chRt.pivot = new Vector2(0f, 0.5f);
        chRt.anchoredPosition = new Vector2(10f, 0f);
        chRt.sizeDelta = new Vector2(84f, 0f);
        channelLabel = MakeText(chGo.transform, fontSize, ColAll, TextAnchor.MiddleLeft);

        var fieldGo = new GameObject("Field");
        fieldGo.transform.SetParent(inputRow.transform, false);
        var fieldRt = fieldGo.AddComponent<RectTransform>();
        fieldRt.anchorMin = Vector2.zero;
        fieldRt.anchorMax = Vector2.one;
        fieldRt.offsetMin = new Vector2(98f, 4f);
        fieldRt.offsetMax = new Vector2(-10f, -4f);
        fieldGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

        var fieldText = MakeText(fieldGo.transform, fontSize, ColAll, TextAnchor.MiddleLeft);
        fieldText.rectTransform.offsetMin = new Vector2(8f, 0f);
        fieldText.rectTransform.offsetMax = new Vector2(-8f, 0f);
        fieldText.supportRichText = false;          // 사용자가 태그를 넣지 못하게

        inputField = fieldGo.AddComponent<InputField>();
        inputField.textComponent = fieldText;
        inputField.lineType = InputField.LineType.SingleLine;
        inputField.characterLimit = 40;             // UTF-8 128바이트에 들어가는 안전값
        inputField.text = string.Empty;

        inputRow.SetActive(false);
        ApplyChannelToLabel();
    }

    private Text MakeText(Transform parent, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        return t;
    }
}
