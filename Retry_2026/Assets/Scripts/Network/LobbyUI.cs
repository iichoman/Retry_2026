using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;   // 새 Input System용 UI 입력 모듈

// ============================================================================
//  LobbyUI  —  런타임 생성 uGUI 로비 (타르코프식 파티 찾기 / 만들기 / 팀 편성)
//
//  ▶ 사용법: 로비 씬의 아무 GameObject(예: NetworkBootstrap 오브젝트)에 이 컴포넌트
//            하나만 추가. Canvas/EventSystem/패널/스크롤/그리드까지 전부 코드로 생성.
//
//  ▶ 자동 처리: 씬에서 NetworkBootstrap 자동 탐색 → LobbyPacket 이벤트 구독.
//               임시 DebugLobbyUI(OnGUI)가 있으면 자동 비활성화.
//
//  ▶ 화면 흐름:
//       Connecting ─(로그인)→ Main
//          Main : (좌상단 닉네임) (우상단 [파티 찾기][파티 만들기]) (중앙=캐릭터 자리)
//            ├ Create  : 파티 이름 입력 → 생성
//            ├ Find    : 열린 파티 목록(ROOM_LIST 2초 폴링) + [참가]
//            └ Waiting : 5x2 팀 그리드(팀당 3명) + 본인 프로필 + 팀 선택 + [레이드 시작]/[나가기]
//       SESSION_ASSIGN 수신 → 캔버스 숨김(게임 시작).
//
//  ▶ 서버 연동: ROOM_STATE(14)로 멤버/팀 현황을 push 받아 그리드 갱신.
//               팀 선택=ROOM_SELECT_TEAM_REQUEST(13), 나가기=ROOM_LEAVE_REQUEST(11).
//               NetworkBootstrap에 LobbyPacket 이벤트 2줄 추가 필요(이미 적용함).
// ============================================================================
public class LobbyUI : MonoBehaviour
{
    private enum Screen { Title, Connecting, Main, Create, Find, Waiting }

    [Header("자동 연결 (비워두면 씬에서 자동 탐색)")]
    [SerializeField] private NetworkBootstrap bootstrap;

    private const float LIST_POLL_INTERVAL = 2f;

    // ── 색상 팔레트 (타르코프 톤) ──
    static readonly Color ColBg = new Color32(0x12, 0x14, 0x16, 0xFF);
    static readonly Color ColPanel = new Color32(0x1B, 0x1F, 0x23, 0xFF);
    static readonly Color ColPanel2 = new Color32(0x23, 0x28, 0x2D, 0xFF);
    static readonly Color ColRow = new Color32(0x20, 0x25, 0x2A, 0xFF);
    static readonly Color ColRowAlt = new Color32(0x1C, 0x21, 0x25, 0xFF);
    static readonly Color ColTan = new Color32(0xC7, 0xA8, 0x6B, 0xFF);
    static readonly Color ColText = new Color32(0xD7, 0xD2, 0xC7, 0xFF);
    static readonly Color ColTextDim = new Color32(0x8C, 0x88, 0x7E, 0xFF);
    static readonly Color ColBtn = new Color32(0x2B, 0x30, 0x2A, 0xFF);
    static readonly Color ColBtnTan = new Color32(0x46, 0x3C, 0x24, 0xFF);
    static readonly Color ColBtnDanger = new Color32(0x4E, 0x2C, 0x28, 0xFF);

    private Font font;
    private Canvas canvas;
    private Screen screen = Screen.Connecting;

    private GameObject connectingPanel, mainPanel, createPanel, findPanel, waitingPanel, guidePanel, titlePanel;
    private InputField titleNicknameInput;
    private GameObject listContent, gridContainer;
    private Text statusText, statusBarText, nameLabel;
    private Text waitTitle, profileLabel;
    private Button startBtn;
    private InputField partyNameInput;
    private float statusUntil, nextPollTime;

    // 대기실 상태
    private int myRoomId;
    private bool amHost;
    private string myRoomName = "";
    private int memberMax = LobbyConst.MAX_SESSION_PLAYERS;
    private int myTeam = LobbyConst.TEAM_UNASSIGNED;

    // 최신 ROOM_STATE 명단
    private RoomMemberEntry[] roster = Array.Empty<RoomMemberEntry>();
    private int rosterCount = 0;

    private bool subscribed;

    // ========================================================================
    //  생명주기
    // ========================================================================
    private void Start()
    {
        if (bootstrap == null) bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        var dbg = FindFirstObjectByType<DebugLobbyUI>();
        if (dbg != null) dbg.enabled = false;

        font = LoadFont();
        EnsureEventSystem();
        BuildCanvas();
        BuildAllScreens();
        TrySubscribe();
        TryAutoLogin();
    }

    // PauseMenu의 [로비로 나가기]로 새로고침된 경우: 저장된 닉네임으로 자동 재로그인 → 로비 Main.
    // 그 외에는 타이틀 화면.
    private void TryAutoLogin()
    {
        bool auto = PlayerPrefs.GetInt("retry_autologin", 0) == 1;
        PlayerPrefs.SetInt("retry_autologin", 0);   // 1회성: 즉시 소비
        PlayerPrefs.Save();

        string nick = auto ? PlayerPrefs.GetString("retry_autologin_nick", "") : "";
        if (auto && !string.IsNullOrWhiteSpace(nick) && bootstrap != null && bootstrap.BeginLogin(nick))
        {
            if (titleNicknameInput != null) titleNicknameInput.text = nick;
            GoTo(Screen.Connecting);   // IsAuthenticated 되면 Update에서 Main으로
        }
        else
        {
            GoTo(Screen.Title);
        }
    }

    private void OnDestroy()
    {
        if (subscribed && bootstrap != null) bootstrap.LobbyPacket -= OnLobbyPacket;
    }

    private void TrySubscribe()
    {
        if (subscribed || bootstrap == null) return;
        bootstrap.LobbyPacket += OnLobbyPacket;
        subscribed = true;
    }

    private void Update()
    {
        if (bootstrap == null) { bootstrap = FindFirstObjectByType<NetworkBootstrap>(); return; }
        TrySubscribe();

        var id = bootstrap.Identity;
        if (id != null && id.IsConnectedToSession)
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            return;
        }

        if (screen == Screen.Connecting && id != null && id.IsAuthenticated)
            GoTo(Screen.Main);

        if (screen == Screen.Find && Time.unscaledTime >= nextPollTime)
        {
            nextPollTime = Time.unscaledTime + LIST_POLL_INTERVAL;
            bootstrap.Lobby?.SendRoomList();
        }

        if (statusText != null && statusUntil > 0f && Time.unscaledTime > statusUntil)
        {
            statusText.text = ""; statusUntil = 0f;
        }
    }

    private void LateUpdate()
    {
        if (bootstrap == null) return;
        var id = bootstrap.Identity;
        if (nameLabel != null) nameLabel.text = (id != null ? id.PlayerName : "PLAYER");
        if (statusBarText != null)
            statusBarText.text = (id == null) ? "" :
                $"ID #{id.LocalClientId}  ·  LOBBY {(id.IsConnectedToLobby ? "ON" : "OFF")}";
    }

    // ========================================================================
    //  로비 패킷 수신
    // ========================================================================
    private void OnLobbyPacket(PacketType type, byte[] body)
    {
        int t = (int)type;
        switch (t)
        {
            // 로그인 결과: 실패(중복 닉네임 등)면 타이틀로 돌려보내고 안내.
            // 성공은 NetworkBootstrap이 IsAuthenticated 처리 → Update에서 Main 전환.
            case (int)PacketType.LOGIN_RESULT:
                {
                    var r = PacketIO.BytesToStruct<LoginResult>(body, 0);
                    if (r.success == 0)
                    {
                        GoTo(Screen.Title);
                        Toast("이미 사용 중인 닉네임입니다");
                    }
                    break;
                }

            case (int)PacketType.ROOM_CREATE_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomCreateResult>(body, 0);
                    if (r.success != 0)
                    {
                        myRoomId = r.roomId; amHost = true;
                        GoTo(Screen.Waiting);
                        Toast($"파티 생성됨 (코드 {r.roomId})");
                    }
                    else Toast("파티 생성 실패");
                    break;
                }
            case (int)PacketType.ROOM_JOIN_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomJoinResult>(body, 0);
                    if (r.success != 0)
                    {
                        myRoomId = r.roomId;
                        amHost = (r.hostClientId == bootstrap.Identity.LocalClientId);
                        memberMax = r.maxPlayers > 0 ? r.maxPlayers : LobbyConst.MAX_SESSION_PLAYERS;
                        GoTo(Screen.Waiting);
                        Toast($"파티 참가 (코드 {r.roomId})");
                    }
                    else Toast($"참가 실패: {Clean(r.failReason)}");
                    break;
                }
            case (int)PacketType.ROOM_LIST_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomListResult>(body, 0);
                    if (screen == Screen.Find) RebuildRoomList(r);
                    break;
                }
            case LobbyPacketType.ROOM_STATE:
                {
                    var st = PacketIO.BytesToStruct<RoomStateData>(body, 0);
                    myRoomId = st.roomId;
                    amHost = (st.hostClientId == bootstrap.Identity.LocalClientId);
                    roster = st.members ?? Array.Empty<RoomMemberEntry>();
                    rosterCount = Mathf.Clamp(st.memberCount, 0, roster.Length);
                    if (screen != Screen.Waiting) GoTo(Screen.Waiting);
                    else RefreshWaiting();
                    break;
                }
            case LobbyPacketType.ROOM_LEAVE_RESULT:
                {
                    myRoomId = 0; rosterCount = 0;
                    if (screen == Screen.Waiting) GoTo(Screen.Main);
                    break;
                }
            case (int)PacketType.SESSION_ASSIGN:
                {
                    if (canvas != null) canvas.gameObject.SetActive(false);
                    break;
                }
        }
    }

    // ========================================================================
    //  화면 전환
    // ========================================================================
    private void GoTo(Screen s)
    {
        screen = s;
        if (guidePanel != null) guidePanel.SetActive(false);   // 화면 전환 시 가이드 닫기
        titlePanel.SetActive(s == Screen.Title);
        connectingPanel.SetActive(s == Screen.Connecting);
        mainPanel.SetActive(s == Screen.Main);
        createPanel.SetActive(s == Screen.Create);
        findPanel.SetActive(s == Screen.Find);
        waitingPanel.SetActive(s == Screen.Waiting);

        if (s == Screen.Find)
        {
            nextPollTime = 0f;
            bootstrap.Lobby?.SendRoomList();
        }
        else if (s == Screen.Waiting)
        {
            RefreshWaiting();
        }
    }

    // ── 버튼 동작 ──
    private void OnClickFind() { GoTo(Screen.Find); }
    private void OnClickOpenCreate()
    {
        if (partyNameInput != null) partyNameInput.text = $"{bootstrap.Identity.PlayerName}의 파티";
        GoTo(Screen.Create);
    }
    private void OnClickConfirmCreate()
    {
        string nm = (partyNameInput != null ? partyNameInput.text : "").Trim();
        if (string.IsNullOrEmpty(nm)) nm = $"{bootstrap.Identity.PlayerName}의 파티";
        myRoomName = nm;
        bootstrap.Lobby?.SendRoomCreate(nm);
        Toast("파티 생성 요청...");
    }
    private void OnClickJoin(int roomId) { bootstrap.Lobby?.SendRoomJoin(roomId); }
    private void OnClickSelectTeam(int team) { bootstrap.Lobby?.SendSelectTeam(team); }
    private void OnClickStartRaid() { bootstrap.Lobby?.SendGameStart(); Toast("파티 시작 요청..."); }
    private void OnClickLeave()
    {
        bootstrap.Lobby?.SendRoomLeave();
        myRoomId = 0; rosterCount = 0;
        GoTo(Screen.Main);   // 즉시 복귀(서버는 ROOM_LEAVE_RESULT로 확인)
    }

    // ========================================================================
    //  Find: 방 목록
    // ========================================================================
    private void RebuildRoomList(RoomListResult r)
    {
        for (int i = listContent.transform.childCount - 1; i >= 0; --i)
            Destroy(listContent.transform.GetChild(i).gameObject);

        int count = Mathf.Clamp(r.count, 0, r.rooms != null ? r.rooms.Length : 0);
        if (count == 0)
        {
            var empty = New("Empty", listContent.transform);
            var le = empty.AddComponent<LayoutElement>(); le.minHeight = 80;
            AddText(empty, "열린 파티가 없습니다.\n새로고침하거나 파티를 만드세요.", 18, ColTextDim, TextAnchor.MiddleCenter);
            return;
        }
        for (int i = 0; i < count; ++i) BuildRoomRow(r.rooms[i], i);
    }

    private void BuildRoomRow(RoomListEntry e, int index)
    {
        var row = New($"Room_{e.roomId}", listContent.transform);
        AddImage(row, (index % 2 == 0) ? ColRow : ColRowAlt);
        var le = row.AddComponent<LayoutElement>(); le.minHeight = 64; le.preferredHeight = 64;

        var nameGo = New("Name", row.transform);
        SetRect(nameGo, new Vector2(0, 0), new Vector2(1, 1), new Vector2(18, 0), new Vector2(-360, 0));
        AddText(nameGo, NameCodec.Decode(e.roomName), 20, ColText, TextAnchor.MiddleLeft);

        var hostGo = New("Host", row.transform);
        SetRect(hostGo, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-360, 0), new Vector2(-210, 0));
        AddText(hostGo, $"방장 #{e.hostClientId}", 15, ColTextDim, TextAnchor.MiddleLeft);

        var popGo = New("Pop", row.transform);
        SetRect(popGo, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-210, 0), new Vector2(-120, 0));
        bool full = e.currentPlayers >= e.maxPlayers;
        AddText(popGo, $"{e.currentPlayers}/{e.maxPlayers}", 18, full ? ColBtnDanger : ColTan, TextAnchor.MiddleCenter);

        int rid = e.roomId;
        var joinBtn = MakeButton(row.transform, full ? "가득참" : "참가",
            new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            new Vector2(-108, -20), new Vector2(-12, 20),
            ColBtnTan, 17, () => OnClickJoin(rid));
        joinBtn.interactable = !full;
    }

    // ========================================================================
    //  Waiting: 5x2 팀 그리드
    // ========================================================================
    private int CountTeam(int teamId)
    {
        int c = 0;
        for (int i = 0; i < rosterCount; i++) if (roster[i].teamId == teamId) c++;
        return c;
    }
    private int GetMyTeam()
    {
        int me = bootstrap.Identity.LocalClientId;
        for (int i = 0; i < rosterCount; i++) if (roster[i].clientId == me) return roster[i].teamId;
        return LobbyConst.TEAM_UNASSIGNED;
    }

    private void RefreshWaiting()
    {
        if (waitTitle == null) return;

        waitTitle.text = string.IsNullOrEmpty(myRoomName)
            ? $"파티 대기실 — 코드 {myRoomId}"
            : $"{myRoomName} — 코드 {myRoomId}";

        myTeam = GetMyTeam();
        string teamStr = (myTeam < 0) ? "팀 미선택" : $"팀 {myTeam + 1}";
        profileLabel.text = $"{bootstrap.Identity.PlayerName} (나)    ·    {teamStr}    ·    파티 {rosterCount}/{memberMax}명";
        startBtn.gameObject.SetActive(amHost);

        if (gridContainer == null) return;
        var grid = gridContainer.GetComponent<GridLayoutGroup>();
        var grt = RT(gridContainer);
        float w = grt.rect.width, h = grt.rect.height;
        if (w > 50 && h > 50)
        {
            float cellW = (w - 16f - 10f * 4f) / 5f;
            float cellH = (h - 16f - 10f) / 2f;
            grid.cellSize = new Vector2(cellW, Mathf.Min(cellH, 170f));
        }

        for (int i = gridContainer.transform.childCount - 1; i >= 0; --i)
            Destroy(gridContainer.transform.GetChild(i).gameObject);
        for (int team = 0; team < LobbyConst.MAX_TEAMS; team++) BuildTeamCell(team);
    }

    private void BuildTeamCell(int teamId)
    {
        var cell = New($"Team_{teamId}", gridContainer.transform);
        AddImage(cell, ColRow);

        int cnt = CountTeam(teamId);
        bool mine = (myTeam == teamId);
        bool full = cnt >= LobbyConst.TEAM_CAPACITY;

        // 헤더
        var hd = New("Hd", cell.transform);
        Frac(hd, 0.04f, 0.80f, 0.96f, 0.985f);
        AddText(hd, $"팀 {teamId + 1}   {cnt}/{LobbyConst.TEAM_CAPACITY}", 15,
                mine ? ColTan : ColText, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;

        // 멤버 이름 (최대 3)
        int shown = 0;
        for (int i = 0; i < rosterCount && shown < LobbyConst.TEAM_CAPACITY; i++)
        {
            if (roster[i].teamId != teamId) continue;
            var row = New($"M{shown}", cell.transform);
            float yTop = 0.78f - shown * 0.165f;
            Frac(row, 0.06f, yTop - 0.15f, 0.94f, yTop);
            bool me = roster[i].clientId == bootstrap.Identity.LocalClientId;
            string nm = NameCodec.Decode(roster[i].playerName);
            if (string.IsNullOrEmpty(nm)) nm = $"#{roster[i].clientId}";
            if (roster[i].isHost != 0) nm = "★ " + nm;
            if (me) nm += " (나)";
            AddText(row, nm, 13, me ? ColTan : ColText, TextAnchor.MiddleCenter);
            shown++;
        }
        for (int s = shown; s < LobbyConst.TEAM_CAPACITY; s++)
        {
            var row = New($"E{s}", cell.transform);
            float yTop = 0.78f - s * 0.165f;
            Frac(row, 0.06f, yTop - 0.15f, 0.94f, yTop);
            AddText(row, "—", 13, ColTextDim, TextAnchor.MiddleCenter);
        }

        // 선택 버튼
        string label = mine ? "현재 팀" : (full ? "가득참" : "선택");
        var btn = MakeButton(cell.transform, label,
            new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.26f), Vector2.zero, Vector2.zero,
            mine ? ColBtn : ColBtnTan, 14, () => OnClickSelectTeam(teamId));
        btn.interactable = !mine && !full;
    }

    // ========================================================================
    //  UI 빌드
    // ========================================================================
    private void BuildAllScreens()
    {
        var bg = New("BG", canvas.transform);
        StretchFull(bg);
        var bgImg = AddImage(bg, new Color(0, 0, 0, 0f));   // 투명: 뒤의 3D 로비룸이 보이도록
        bgImg.raycastTarget = false;                         // 중앙 클릭이 월드(분필 글씨)로 통과

        // 하단 좌측 상태바
        var statusBar = New("StatusBar", canvas.transform);
        SetRect(statusBar, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 12), new Vector2(420, 38));
        statusBarText = AddText(statusBar, "", 13, ColTextDim, TextAnchor.MiddleLeft);

        // 하단 중앙 토스트
        var toast = New("Toast", canvas.transform);
        SetRect(toast, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 44), new Vector2(0, 80));
        statusText = AddText(toast, "", 18, ColTan, TextAnchor.MiddleCenter);

        BuildConnecting();
        BuildTitle();
        BuildMain();
        BuildCreate();
        BuildFind();
        BuildWaiting();
        BuildGuide();
    }

    private void BuildTitle()
    {
        titlePanel = New("TitlePanel", canvas.transform);
        StretchFull(titlePanel);

        var dim = New("Dim", titlePanel.transform);
        StretchFull(dim);
        dim.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);

        // 큰 RETRY 로고
        var logo = New("Logo", titlePanel.transform);
        SetRect(logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420, 90), new Vector2(420, 230));
        AddText(logo, "R E T R Y", 86, ColTan, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
        var sub = New("Sub", titlePanel.transform);
        SetRect(sub, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420, 56), new Vector2(420, 92));
        AddText(sub, "EXTRACTION  ·  PvPvE", 18, ColTextDim, TextAnchor.MiddleCenter);

        // 닉네임 라벨 + 입력칸
        var lbl = New("Lbl", titlePanel.transform);
        SetRect(lbl, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200, 4), new Vector2(200, 34));
        AddText(lbl, "닉네임", 16, ColTextDim, TextAnchor.MiddleCenter);

        titleNicknameInput = MakeInputField(titlePanel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200, -48), new Vector2(200, -4),
            "닉네임 입력", "", 22);
        titleNicknameInput.characterLimit = 12;

        MakeButton(titlePanel.transform, "시작",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200, -112), new Vector2(200, -60),
            ColBtnTan, 24, OnClickStartGame);
    }

    private void OnClickStartGame()
    {
        string nick = (titleNicknameInput != null ? titleNicknameInput.text : "").Trim();
        if (string.IsNullOrEmpty(nick)) { Toast("닉네임을 입력하세요"); return; }
        if (bootstrap == null) { Toast("연결 준비 중..."); return; }
        bool ok = bootstrap.BeginLogin(nick);
        if (ok) GoTo(Screen.Connecting);
        else Toast("로비 서버 연결 실패");
    }

    private void BuildConnecting()
    {
        connectingPanel = New("ConnectingPanel", canvas.transform);
        StretchFull(connectingPanel);
        var t = New("Txt", connectingPanel.transform);
        StretchFull(t);
        AddText(t, "서버 연결 중...", 26, ColTextDim, TextAnchor.MiddleCenter);
    }

    private void BuildMain()
    {
        mainPanel = New("MainPanel", canvas.transform);
        StretchFull(mainPanel);

        // 좌상단 닉네임
        var nameBox = New("NameBox", mainPanel.transform);
        SetRect(nameBox, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -86), new Vector2(360, -28));
        AddImage(nameBox, ColPanel);
        var nick = New("Nick", nameBox.transform);
        SetRect(nick, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0));
        nameLabel = AddText(nick, "PLAYER", 22, ColText, TextAnchor.MiddleLeft);
        nameLabel.fontStyle = FontStyle.Bold;

        // 프로필 옆 "게임 가이드" 버튼
        MakeButton(mainPanel.transform, "게임 가이드",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(372, -86), new Vector2(548, -28),
            ColBtn, 18, () => { if (guidePanel != null) guidePanel.SetActive(true); });

        // 상단 중앙 워드마크
        var wm = New("Wordmark", mainPanel.transform);
        SetRect(wm, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-170, -78), new Vector2(170, -26));
        AddText(wm, "R E T R Y", 30, ColTan, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;

        // 우상단 버튼
        MakeButton(mainPanel.transform, "파티 찾기",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-446, -86), new Vector2(-244, -28),
            ColBtn, 20, OnClickFind);
        MakeButton(mainPanel.transform, "파티 만들기",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-232, -86), new Vector2(-28, -28),
            ColBtnTan, 20, OnClickOpenCreate);

        // 중앙 안내(캐릭터 자리는 비움)
        var hint = New("CenterHint", mainPanel.transform);
        SetRect(hint, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 96), new Vector2(0, 132));
        AddText(hint, "파티 찾기 또는 파티 만들기로 파티에 참가하세요", 16, ColTextDim, TextAnchor.MiddleCenter);
    }

    private void BuildCreate()
    {
        createPanel = New("CreatePanel", canvas.transform);
        StretchFull(createPanel);
        var dim = New("Dim", createPanel.transform);
        StretchFull(dim);
        var dimImg = dim.AddComponent<Image>(); dimImg.color = new Color(0, 0, 0, 0.5f);

        var box = New("Box", createPanel.transform);
        SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, -130), new Vector2(260, 130));
        AddImage(box, ColPanel);

        var head = New("Head", box.transform);
        SetRect(head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -58), new Vector2(-24, -16));
        AddText(head, "파티 만들기", 24, ColTan, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;

        var lbl = New("Lbl", box.transform);
        SetRect(lbl, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -96), new Vector2(-24, -66));
        AddText(lbl, "파티 이름", 15, ColTextDim, TextAnchor.MiddleLeft);

        partyNameInput = MakeInputField(box.transform,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -152), new Vector2(-24, -102),
            "파티 이름 입력", "", 18);

        MakeButton(box.transform, "만들기",
            new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(24, 24), new Vector2(-8, 76),
            ColBtnTan, 20, OnClickConfirmCreate);
        MakeButton(box.transform, "취소",
            new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(8, 24), new Vector2(-24, 76),
            ColBtnDanger, 20, () => GoTo(Screen.Main));
    }

    private void BuildFind()
    {
        findPanel = New("FindPanel", canvas.transform);
        StretchFull(findPanel);

        var fdim = New("Dim", findPanel.transform);   // 3D 룸 위에 어둡게
        StretchFull(fdim);
        fdim.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);

        var box = New("Box", findPanel.transform);
        SetRect(box, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(-440, 90), new Vector2(440, -150));
        AddImage(box, ColPanel);

        var header = New("Header", box.transform);
        SetRect(header, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -58), new Vector2(-20, -10));
        AddText(header, "파티 찾기", 26, ColTan, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;

        MakeButton(box.transform, "새로고침",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-260, -56), new Vector2(-140, -14),
            ColBtn, 16, () => bootstrap.Lobby?.SendRoomList());
        MakeButton(box.transform, "뒤로",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128, -56), new Vector2(-20, -14),
            ColBtnDanger, 16, () => GoTo(Screen.Main));

        var colHead = New("ColHead", box.transform);
        SetRect(colHead, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -88), new Vector2(-20, -64));
        AddText(colHead, "파티", 14, ColTextDim, TextAnchor.MiddleLeft);
        var colHead2 = New("ColHead2", box.transform);
        SetRect(colHead2, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-230, -88), new Vector2(-120, -64));
        AddText(colHead2, "인원", 14, ColTextDim, TextAnchor.MiddleCenter);

        BuildScrollList(box.transform);
    }

    private void BuildScrollList(Transform parent)
    {
        var scrollGo = New("Scroll", parent);
        SetRect(scrollGo, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 16), new Vector2(-16, -96));
        AddImage(scrollGo, ColPanel2);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.scrollSensitivity = 24f;

        var viewport = New("Viewport", scrollGo.transform);
        StretchFull(viewport);
        var vpImg = viewport.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = RT(viewport);

        listContent = New("Content", viewport.transform);
        var cRt = RT(listContent);
        cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1); cRt.pivot = new Vector2(0.5f, 1f);
        cRt.offsetMin = Vector2.zero; cRt.offsetMax = Vector2.zero;
        var vlg = listContent.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4; vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = listContent.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = cRt;
    }

    private void BuildWaiting()
    {
        waitingPanel = New("WaitingPanel", canvas.transform);
        StretchFull(waitingPanel);

        var wdim = New("Dim", waitingPanel.transform);   // 3D 룸 위에 어둡게
        StretchFull(wdim);
        wdim.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);

        var box = New("Box", waitingPanel.transform);
        SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-500, -360), new Vector2(500, 360));
        AddImage(box, ColPanel);

        var titleGo = New("WTitle", box.transform);
        SetRect(titleGo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -58), new Vector2(-28, -14));
        waitTitle = AddText(titleGo, "파티 대기실", 24, ColTan, TextAnchor.MiddleLeft);
        waitTitle.fontStyle = FontStyle.Bold;

        var profGo = New("Profile", box.transform);
        SetRect(profGo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -102), new Vector2(-28, -66));
        AddImage(profGo, ColPanel2);
        var profTxt = New("ProfTxt", profGo.transform);
        SetRect(profTxt, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
        profileLabel = AddText(profTxt, "", 17, ColText, TextAnchor.MiddleLeft);

        gridContainer = New("Grid", box.transform);
        SetRect(gridContainer, new Vector2(0, 0), new Vector2(1, 1), new Vector2(20, 96), new Vector2(-20, -110));
        var grid = gridContainer.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.spacing = new Vector2(10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.cellSize = new Vector2(170, 150);

        startBtn = MakeButton(box.transform, "파티 시작",
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 48), new Vector2(-28, 86),
            ColBtnTan, 22, OnClickStartRaid);
        MakeButton(box.transform, "파티 나가기",
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 10), new Vector2(-28, 44),
            ColBtnDanger, 15, OnClickLeave);
    }

    // ========================================================================
    //  헬퍼
    // ========================================================================
    private void BuildGuide()
    {
        guidePanel = New("GuidePanel", canvas.transform);
        StretchFull(guidePanel);

        var dim = New("Dim", guidePanel.transform);
        StretchFull(dim);
        dim.AddComponent<Image>().color = new Color(0, 0, 0, 0.82f);

        var box = New("Box", guidePanel.transform);
        SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-460, -340), new Vector2(460, 340));
        AddImage(box, ColPanel);

        var head = New("Head", box.transform);
        SetRect(head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -64), new Vector2(-28, -14));
        AddText(head, "RETRY 게임 가이드", 26, ColTan, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;

        MakeButton(box.transform, "닫기",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128, -62), new Vector2(-24, -16),
            ColBtnDanger, 16, () => guidePanel.SetActive(false));

        // 스크롤 영역
        var scrollGo = New("Scroll", box.transform);
        SetRect(scrollGo, new Vector2(0, 0), new Vector2(1, 1), new Vector2(20, 20), new Vector2(-20, -76));
        AddImage(scrollGo, ColPanel2);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.scrollSensitivity = 28f;

        var viewport = New("Viewport", scrollGo.transform);
        StretchFull(viewport);
        var vpImg = viewport.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = RT(viewport);

        var content = New("Content", viewport.transform);
        var cRt = RT(content);
        cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1); cRt.pivot = new Vector2(0.5f, 1f);
        cRt.offsetMin = Vector2.zero; cRt.offsetMax = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 20, 24); vlg.spacing = 6;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = content.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = cRt;

        // 본문 텍스트 (한 덩어리, 자동 줄바꿈 + 높이 맞춤)
        var bodyGo = New("Body", content.transform);
        var bt = bodyGo.AddComponent<Text>();
        bt.font = font; bt.fontSize = 18; bt.color = ColText; bt.alignment = TextAnchor.UpperLeft;
        bt.horizontalOverflow = HorizontalWrapMode.Wrap;
        bt.verticalOverflow = VerticalWrapMode.Overflow;
        bt.supportRichText = false;
        bt.lineSpacing = 1.15f;
        bt.text = GuideText();

        guidePanel.SetActive(false);
    }

    private static string GuideText()
    {
        return
"[ RETRY 란? ]\n" +
"RETRY는 1~3인 협동을 기반으로 한 익스트랙션(탈출) 액션 RPG입니다. 3인칭 시점으로 무작위 생성된 던전을 탐험하고, 전리품을 모은 뒤 살아서 탈출하는 것이 목표입니다.\n" +
"한 세션에는 최대 10개 팀(팀당 1~3명)이 동시에 들어가며, 다른 플레이어(PvP)와 몬스터(PvE)가 함께 존재하는 PvPvE 구조입니다.\n" +
"\n" +
"[ 파티 / 팀 ]\n" +
"- 파티 만들기: 새 파티(세션)를 직접 개설하고 방장이 됩니다.\n" +
"- 파티 찾기: 열려 있는 파티 목록에서 골라 참가합니다.\n" +
"- 대기실의 5x2 팀 슬롯에서 원하는 팀(최대 3명)을 선택할 수 있습니다. 같은 팀끼리 같은 시작 방에서 출발합니다.\n" +
"- 방장이 '파티 시작'을 누르면 모든 팀원이 함께 던전으로 투입됩니다.\n" +
"\n" +
"[ 기본 조작 ]\n" +
"- 이동: W A S D\n" +
"- 카메라: 마우스 이동\n" +
"- 공격: 마우스 좌클릭\n" +
"- 달리기: Shift\n" +
"\n" +
"[ 전투 팁 ]\n" +
"- 몬스터는 종류마다 공격 패턴과 체력이 다릅니다. 무리하게 어그로를 끌지 말고 하나씩 정리하세요.\n" +
"- 다른 팀과 마주치면 교전할지 피할지 빠르게 판단하세요. 전투 중 제3의 팀이 끼어들 수 있습니다.\n" +
"- 체력이 부족하면 욕심내지 말고 탈출 지점으로 향하는 것이 이득일 때가 많습니다.\n" +
"\n" +
"[ 시작해볼까요? ]\n" +
"오른쪽 위의 [파티 찾기]로 열린 파티에 합류하거나, [파티 만들기]로 직접 파티를 만들어 게임을 시작해보세요. 행운을 빕니다!\n";
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject("LobbyCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private static Font LoadFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }

    private GameObject New(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static RectTransform RT(GameObject go) => go.GetComponent<RectTransform>();

    private static void StretchFull(GameObject go)
    {
        var rt = RT(go);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void SetRect(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var rt = RT(go);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
    }

    private static void Frac(GameObject go, float x0, float y0, float x1, float y1)
    {
        var rt = RT(go);
        rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private Image AddImage(GameObject go, Color c)
    {
        var img = go.AddComponent<Image>(); img.color = c; return img;
    }

    private Text AddText(GameObject go, string s, int size, Color c, TextAnchor anchor)
    {
        var t = go.AddComponent<Text>();
        t.font = font; t.text = s; t.fontSize = size; t.color = c; t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        return t;
    }

    private Button MakeButton(Transform parent, string label,
        Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax,
        Color baseCol, int fontSize, Action onClick)
    {
        var go = New("Btn_" + label, parent);
        SetRect(go, aMin, aMax, oMin, oMax);
        var img = go.AddComponent<Image>(); img.color = Color.white;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = baseCol;
        cb.highlightedColor = Lighten(baseCol, 0.16f);
        cb.pressedColor = Lighten(baseCol, -0.12f);
        cb.selectedColor = baseCol;
        cb.disabledColor = new Color(baseCol.r, baseCol.g, baseCol.b, 0.35f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        var txtGo = New("Label", go.transform);
        StretchFull(txtGo);
        var t = AddText(txtGo, label, fontSize, ColText, TextAnchor.MiddleCenter);
        t.fontStyle = FontStyle.Bold;
        return btn;
    }

    private InputField MakeInputField(Transform parent,
        Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax,
        string placeholder, string initial, int fontSize)
    {
        var go = New("Input", parent);
        SetRect(go, aMin, aMax, oMin, oMax);
        var bg = go.AddComponent<Image>(); bg.color = ColPanel2;
        var inp = go.AddComponent<InputField>();

        var ph = New("Placeholder", go.transform);
        SetRect(ph, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0));
        var phT = AddText(ph, placeholder, fontSize, ColTextDim, TextAnchor.MiddleLeft);
        phT.fontStyle = FontStyle.Italic;

        var txt = New("Text", go.transform);
        SetRect(txt, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0));
        var tT = AddText(txt, "", fontSize, ColText, TextAnchor.MiddleLeft);

        inp.textComponent = tT;
        inp.placeholder = phT;
        inp.lineType = InputField.LineType.SingleLine;
        inp.characterLimit = 24;
        inp.text = initial ?? "";
        return inp;
    }

    private static Color Lighten(Color c, float amt)
    {
        return new Color(Mathf.Clamp01(c.r + amt), Mathf.Clamp01(c.g + amt), Mathf.Clamp01(c.b + amt), c.a);
    }

    private void Toast(string msg)
    {
        if (statusText == null) return;
        statusText.text = msg; statusUntil = Time.unscaledTime + 3f;
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int z = s.IndexOf('\0');
        return (z >= 0 ? s.Substring(0, z) : s).Trim();
    }
}