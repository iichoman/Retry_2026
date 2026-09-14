using System;
using System.Collections.Generic;
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
    // 어두운 갈색 버튼은 배경과 붙어 검게 보였다. 주요 액션은 밝은 색으로 분리한다.
    static readonly Color ColBtnStart = new Color32(0xD8, 0xA2, 0x3C, 0xFF);   // 파티 시작(금색)
    static readonly Color ColBtnWait = new Color32(0x3E, 0x82, 0xD0, 0xFF);   // 준비(파란색)
    static readonly Color ColBtnDone = new Color32(0x4F, 0xB8, 0x63, 0xFF);   // 준비 완료(초록)
    static readonly Color ColOnBright = new Color32(0x14, 0x14, 0x10, 0xFF);   // 밝은 버튼 위 글자
    static readonly Color ColError = new Color32(0xFF, 0x5B, 0x50, 0xFF);   // 입력 오류 문구
    // 입력칸 배경. 타이틀에 뒤판이 없어 디오라마 위에 바로 올라가므로
    // 살짝 더 진하게 해서 글자가 배경에 묻히지 않게 한다.
    static readonly Color ColInput = new Color32(0x16, 0x1A, 0x1E, 0xF2);

    // 타이틀 화면 가로 분할. 왼쪽 UI 3 : 오른쪽 캐릭터 7.
    // LobbyRoom.titleScreenX(기본 0.66)와 짝을 이룬다. 한쪽만 바꾸면 어긋난다.
    private const float UiSplit = 0.30f;

    private Font font;
    private Canvas canvas;
    private Screen screen = Screen.Connecting;

    private GameObject connectingPanel, mainPanel, createPanel, findPanel, waitingPanel, guidePanel, titlePanel;
    private InputField titleNicknameInput;
    private InputField titlePasswordInput;
    private Text  nickError, pwError;
    private string pendingPassword = "";          // 세션 복귀 시 자동 재로그인용
    /// <summary>씬을 새로고침해도 다시 로그인할 수 있도록 마지막 비밀번호를 들고 있는다.</summary>
    public static string LastPassword = "";

    // 회원가입 / 비밀번호 찾기 모달
    private GameObject accountModal;
    private Text       accountTitle;
    private InputField acNick, acPw1, acPw2;
    private Text       acNickError, acPwError, acShowPwLabel, acSubmitLabel;
    private Button     acShowPwBtn, acSubmitBtn;
    private bool       accountRegisterMode = true;
    private bool       showPassword;
    private string     lastValidModalNick = "";

    // 플레이 전적
    private GameObject statsPanel;
    private Text       statsBody;

    // 빨간 안내 문구의 자동 삭제 시각
    private readonly Dictionary<Text, float> errorClearAt = new Dictionary<Text, float>();
    private Text  nickLabel;
    private float nickWarnUntil;
    private string lastValidNick = "";

    private const string NickLabelNormal = "닉네임";
    private const string NickLabelWarn   = "한글 6글자, 영어 12글자 제한입니다.";
    // 표시 폭 한도. 한글 1자를 2폭으로 센다 → 한글 6자 = 영문 12자.
    private const int NickWidthLimit = 12;
    private GameObject listContent, gridContainer;
    private Text statusText, statusBarText, nameLabel;
    private Text waitTitle, profileLabel;
    // 팀 슬롯/준비 표시 색
    private static readonly Color ColSlotEmpty  = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color ColSlotFilled = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color ColSlotMine   = new Color(0.85f, 0.70f, 0.35f, 0.22f);
    private static readonly Color ColReady      = new Color(0.55f, 1.00f, 0.60f);

    private Button startBtn;      // 방장: 파티 시작 / 파티원: 준비
    private Text   startBtnLabel;
    private Button leaveBtn;
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
        // 인게임에서 돌아온 경우 예전 clientId를 같이 보낸다. 서버가 잠가 둔
        // 내 닉네임을 이걸로 풀어준다. (없으면 0 = 새 로그인)
        int reconnectCid = auto ? PlayerPrefs.GetInt(PauseMenu.PrefAutoCid, 0) : 0;
        PlayerPrefs.SetInt(PauseMenu.PrefAutoCid, 0);   // 1회성

        string pw = auto ? PlayerPrefs.GetString(PauseMenu.PrefAutoPw, "") : "";
        if (auto && !string.IsNullOrWhiteSpace(nick) && bootstrap != null
            && bootstrap.BeginLogin(nick, pw, reconnectCid))
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
        UpdateNickWarning();
        UpdateErrors();

        var id = bootstrap.Identity;
        if (id != null && id.IsConnectedToSession)
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            // 오버레이는 서버의 SESSION_START_GAME(전원 입장)까지 유지한다.
            // NetworkBootstrap이 그 신호를 받아 Hide()를 호출한다.
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
                        string why = NameCodec.Decode(r.failReason);
                        if (string.IsNullOrEmpty(why)) why = "로그인에 실패했습니다";
                        // 비밀번호 문제면 비밀번호 칸 옆에, 그 외엔 닉네임 칸 옆에.
                        ShowError(why.Contains("비밀번호") ? pwError : nickError, why);
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
                        Toast("파티가 생성되었습니다");
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
                        Toast("파티에 참가했습니다");
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
            case (int)PacketType.REGISTER_RESULT:
            case (int)PacketType.RESET_PW_RESULT:
                {
                    var a = PacketIO.BytesToStruct<AccountResult>(body, 0);
                    bool isRegister = (t == (int)PacketType.REGISTER_RESULT);
                    if (a.success != 0)
                    {
                        // 성공하면 모달을 닫고 방금 만든 닉네임을 시작 화면에 채워 준다.
                        string nick = acNick != null ? acNick.text : "";
                        string pw = acPw1 != null ? acPw1.text : "";
                        HideAccountModal();
                        if (titleNicknameInput != null) titleNicknameInput.text = nick;
                        if (titlePasswordInput != null) titlePasswordInput.text = pw;
                        Toast(isRegister ? "회원 가입이 완료되었습니다" : "비밀번호가 변경되었습니다");
                    }
                    else
                    {
                        string why = NameCodec.Decode(a.failReason);
                        if (string.IsNullOrEmpty(why)) why = "처리에 실패했습니다";
                        ShowError(why.Contains("비밀번호") ? acPwError : acNickError, why);
                    }
                    break;
                }

            case (int)PacketType.PLAYER_STATS_RESULT:
                {
                    var s = PacketIO.BytesToStruct<PlayerStatsResult>(body, 0);
                    ShowStats(s);
                    break;
                }

            case (int)PacketType.GAME_START_NOTICE:
                {
                    // 방장이 시작을 눌러 서버가 던전 생성을 시작했다. 파티원 전원에게 표시.
                    if (myRoomId != 0) LoadingOverlay.Show("던전 생성 중..");
                    break;
                }
            case (int)PacketType.SESSION_ASSIGN:
                {
                    // 공지를 놓쳤어도 여기서 확실히 띄운다. (클라 던전 생성이 제일 오래 걸림)
                    LoadingOverlay.Show("던전 생성 중..");
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
        ClearErrors();   // 화면이 바뀌면 빨간 안내 문구도 지운다
        if (guidePanel != null) guidePanel.SetActive(false);   // 화면 전환 시 가이드 닫기
        titlePanel.SetActive(s == Screen.Title);
        connectingPanel.SetActive(s == Screen.Connecting);
        mainPanel.SetActive(s == Screen.Main);
        createPanel.SetActive(s == Screen.Create);
        findPanel.SetActive(s == Screen.Find);
        waitingPanel.SetActive(s == Screen.Waiting);

        // 타이틀이면 카메라를 왼쪽으로(캐릭터 오른쪽), 벗어나면 중앙으로 천천히 이동
        LobbyRoom.SetTitleView(s == Screen.Title);

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

    // ── 닉네임 길이 검사 ────────────────────────────────────────
    //  한글은 팀 슬롯에서 영문의 두 배 폭을 차지한다. 그래서 글자 수가 아니라
    //  '표시 폭'으로 제한한다. 한도를 넘으면 입력을 되돌리고 라벨로 알려준다.
    private static int DisplayWidth(string s)
    {
        int w = 0;
        for (int i = 0; i < s.Length; i++) w += IsWide(s[i]) ? 2 : 1;
        return w;
    }

    private static bool IsWide(char c)
    {
        // 한글(음절/자모) + CJK + 전각 구간
        return (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x1100 && c <= 0x11FF)
            || (c >= 0x3130 && c <= 0x318F) || (c >= 0x4E00 && c <= 0x9FFF)
            || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFF60);
    }

    private void OnNicknameChanged(string value)
    {
        if (DisplayWidth(value) <= NickWidthLimit)
        {
            lastValidNick = value;
            return;
        }

        // 한도 초과: 마지막 유효값으로 되돌리고 라벨을 빨갛게 잠깐 바꾼다.
        titleNicknameInput.text = lastValidNick;
        titleNicknameInput.caretPosition = lastValidNick.Length;
        ShowNickWarning();
    }

    private void ShowNickWarning()
    {
        if (nickLabel == null) return;
        nickLabel.text = NickLabelWarn;
        nickLabel.color = ColBtnDanger;
        nickLabel.fontSize = 14;
        nickWarnUntil = Time.unscaledTime + 1.8f;
    }

    private void UpdateNickWarning()
    {
        if (nickLabel == null || nickWarnUntil <= 0f) return;
        if (Time.unscaledTime < nickWarnUntil) return;

        nickWarnUntil = 0f;
        nickLabel.text = NickLabelNormal;
        nickLabel.color = ColTextDim;
        nickLabel.fontSize = 16;
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
    // 방장이면 시작 요청, 파티원이면 준비 토글.
    // 준비가 덜 됐을 때의 [시스템] 안내는 서버가 방장에게 직접 보낸다.
    private void OnClickStartOrReady()
    {
        if (amHost)
        {
            bootstrap.Lobby?.SendGameStart();
            // 전원 준비된 경우에만 로딩 화면을 띄운다.
            // (미준비 시엔 서버가 [시스템] 거절 메시지만 보내므로 오버레이가 남으면 안 된다)
            if (AllMembersReadyLocal()) LoadingOverlay.Show("던전 생성 중..");
        }
        else { bootstrap.Lobby?.SendReadyToggle(); }
    }

    // 방장을 제외한 전원이 준비됐는지. 서버 판정과 같은 규칙.
    private bool AllMembersReadyLocal()
    {
        int host = -1;
        for (int i = 0; i < rosterCount; i++) if (roster[i].isHost != 0) { host = roster[i].clientId; break; }
        for (int i = 0; i < rosterCount; i++)
        {
            if (roster[i].clientId == host) continue;
            if (roster[i].isReady == 0) return false;
        }
        return true;
    }
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

    // 내 준비 상태. 방장은 항상 준비된 것으로 본다.
    private bool AmIReady()
    {
        if (amHost) return true;
        int me = bootstrap.Identity.LocalClientId;
        for (int i = 0; i < rosterCount; i++)
            if (roster[i].clientId == me) return roster[i].isReady != 0;
        return false;
    }

    private void RefreshWaiting()
    {
        if (waitTitle == null) return;

        waitTitle.text = string.IsNullOrEmpty(myRoomName)
            ? "파티 대기실"
            : myRoomName;

        myTeam = GetMyTeam();
        string teamStr = (myTeam < 0) ? "팀 미선택" : $"팀 {myTeam + 1}";
        profileLabel.text = $"{bootstrap.Identity.PlayerName} (나)    ·    {teamStr}    ·    파티 {rosterCount}/{memberMax}명";
        // 버튼은 항상 두 개 보이고, 오른쪽 라벨만 역할에 따라 바뀐다.
        bool iAmReady = AmIReady();
        if (startBtnLabel != null)
        {
            startBtnLabel.text = amHost ? "파티 시작" : (iAmReady ? "준비 완료" : "준비");
        }
        // Button은 ColorBlock.normalColor를 매 프레임 targetGraphic에 덮어쓴다.
        // 그래서 Image.color만 바꾸면 색이 안 변한다. ColorBlock을 직접 갱신해야 한다.
        Color startCol = amHost ? ColBtnStart : (iAmReady ? ColBtnDone : ColBtnWait);
        SetButtonColor(startBtn, startCol);
        if (startBtnLabel != null) startBtnLabel.color = ColOnBright;

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

        // 닉네임이 들어갈 직사각형 슬롯 3개. 비어 있어도 칸은 그대로 그린다.
        int shown = 0;
        for (int s = 0; s < LobbyConst.TEAM_CAPACITY; s++)
        {
            float yTop = 0.80f - s * 0.175f;
            var slot = New($"Slot{s}", cell.transform);
            Frac(slot, 0.06f, yTop - 0.155f, 0.94f, yTop);

            // 이 슬롯에 들어갈 멤버 찾기
            int found = -1;
            int seen = 0;
            for (int i = 0; i < rosterCount; i++)
            {
                if (roster[i].teamId != teamId) continue;
                if (seen == s) { found = i; break; }
                seen++;
            }

            if (found < 0)
            {
                AddImage(slot, ColSlotEmpty);
                var et = New("T", slot.transform);
                Frac(et, 0f, 0f, 1f, 1f);
                AddText(et, "비어 있음", 12, ColTextDim, TextAnchor.MiddleCenter);
                continue;
            }

            bool me = roster[found].clientId == bootstrap.Identity.LocalClientId;
            bool host = roster[found].isHost != 0;
            bool ready = roster[found].isReady != 0;

            AddImage(slot, me ? ColSlotMine : ColSlotFilled);

            string nm = NameCodec.Decode(roster[found].playerName);
            if (string.IsNullOrEmpty(nm)) nm = $"#{roster[found].clientId}";
            if (host) nm = "★ " + nm;
            if (me) nm += " (나)";

            var nameGo = New("T", slot.transform);
            Frac(nameGo, 0.05f, 0f, 0.80f, 1f);
            var nameTxt = AddText(nameGo, nm, 12, me ? ColTan : ColText, TextAnchor.MiddleLeft);
            // 한글 6자(=전각 6자)는 Wrap이면 두 줄로 접힌다. 폭이 좁아도 한 줄 유지.
            nameTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameTxt.verticalOverflow = VerticalWrapMode.Truncate;

            // 준비 표시. 방장은 준비 개념이 없어 표시하지 않는다.
            var markGo = New("R", slot.transform);
            Frac(markGo, 0.80f, 0f, 0.97f, 1f);
            AddText(markGo, host ? "" : (ready ? "●" : "○"), 13,
                    ready ? ColReady : ColTextDim, TextAnchor.MiddleCenter);
            shown++;
        }

        // 선택 버튼
        string label = mine ? "현재 팀" : (full ? "가득참" : "선택");
        var btn = MakeButton(cell.transform, label,
            new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.26f), Vector2.zero, Vector2.zero,
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

    // ========================================================================
    //  타이틀 화면  —  로고 + 로그인 + 회원가입/비밀번호 찾기
    // ========================================================================
    private void BuildTitle()
    {
        titlePanel = New("TitlePanel", canvas.transform);
        StretchFull(titlePanel);

        // 왼쪽 3 : 오른쪽 7. 오른쪽은 아무것도 덮지 않는다.
        // 덮으면 LobbyRoom 캐릭터가 안 보이고 드래그 회전도 UI에 막힌다.
        var col = New("SideColumn", titlePanel.transform);
        Frac(col, 0f, 0f, UiSplit, 1f);
        AddImage(col, new Color(0.02f, 0.03f, 0.04f, 0.94f));    // 여긴 클릭 차단(의도적)

        // 경계가 칼로 자른 듯 보이지 않게 오른쪽으로 갈수록 옅어지는 띠. 클릭은 통과.
        float[] fade = { 0.70f, 0.44f, 0.24f, 0.10f };
        for (int i = 0; i < fade.Length; i++)
        {
            var f = New("Fade" + i, titlePanel.transform);
            Frac(f, UiSplit + 0.032f * i, 0f, UiSplit + 0.032f * (i + 1), 1f);
            AddImage(f, new Color(0.02f, 0.03f, 0.04f, fade[i])).raycastTarget = false;
        }

        // 왼쪽 열의 가로 한가운데를 기준점으로 잡는다. 비율 앵커라 해상도가
        // 바뀌어도 3:7이 유지된다. 아래 숫자들은 전부 이 점에서의 거리다.
        var uiA = new Vector2(UiSplit * 0.5f, 0.5f);

        // ── 로고 (Retry.png) ─────────────────────────────────
        // AspectRatioFitter(FitInParent)는 앵커를 부모 전체로 덮어써서 좌표를
        // 무시한다. 쓰지 않고 텍스처 비율로 폭을 직접 계산한다.
        const float LogoH = 400f;          // 로고 높이. 이 값만 바꾸면 크기 조절
        const float LogoBottom = -60f;     // 세로 중앙에서 로고 아래변까지
        var logo = New("Logo", titlePanel.transform);
        var raw = logo.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var tex = Resources.Load<Texture2D>("Retry");
        float logoW = LogoH;
        if (tex != null)
        {
            raw.texture = tex;
            if (tex.height > 0) logoW = LogoH * tex.width / tex.height;
        }
        else raw.color = new Color(1, 1, 1, 0);
        SetRect(logo, uiA, uiA,
                new Vector2(-logoW * 0.5f, LogoBottom),
                new Vector2(logoW * 0.5f, LogoBottom + LogoH));

        // ── 닉네임 ───────────────────────────────────────────
        titleNicknameInput = MakeInputField(titlePanel.transform, uiA, uiA,
            new Vector2(-210, -130), new Vector2(210, -80),
            "닉네임", "", 20);
        titleNicknameInput.characterLimit = 0;                 // 폭 기준으로 직접 검사
        titleNicknameInput.onValueChanged.AddListener(OnNicknameChanged);
        nickError = MakeInlineError(titleNicknameInput);

        // ── 비밀번호 ─────────────────────────────────────────
        titlePasswordInput = MakeInputField(titlePanel.transform, uiA, uiA,
            new Vector2(-210, -192), new Vector2(210, -142),
            "비밀번호", "", 20);
        titlePasswordInput.contentType = InputField.ContentType.Password;
        titlePasswordInput.characterLimit = 32;
        pwError = MakeInlineError(titlePasswordInput);

        // ── 시작 ─────────────────────────────────────────────
        MakeButton(titlePanel.transform, "게임 시작", uiA, uiA,
            new Vector2(-210, -274), new Vector2(210, -212),
            ColBtnStart, 24, OnClickStartGame).GetComponentInChildren<Text>().color = ColOnBright;

        // ── 회원가입 / 비밀번호 찾기 (입력칸 폭의 절반씩) ────
        MakeButton(titlePanel.transform, "회원 가입", uiA, uiA,
            new Vector2(-210, -336), new Vector2(-6, -288),
            ColBtnTan, 17, () => ShowAccountModal(true));

        MakeButton(titlePanel.transform, "비밀번호 찾기", uiA, uiA,
            new Vector2(6, -336), new Vector2(210, -288),
            ColBtnTan, 17, () => ShowAccountModal(false));

        // 엔터로 다음 칸 / 로그인
        titleNicknameInput.onEndEdit.AddListener(_ =>
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                titlePasswordInput?.ActivateInputField();
        });
        titlePasswordInput.onEndEdit.AddListener(_ =>
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnClickStartGame();
        });

        BuildAccountModal();
    }

    // 입력칸 "안쪽" 오른쪽에 뜨는 빨간 안내 문구. 평소엔 빈 문자열이라
    // 자리를 차지하지 않고, 화면 밖으로 삐져나오지도 않는다.
    private Text MakeInlineError(InputField field)
    {
        var go = New("Err", field.transform);
        SetRect(go, Vector2.zero, Vector2.one, new Vector2(120, 0), new Vector2(-12, 0));
        var t = AddText(go, "", 14, ColError, TextAnchor.MiddleRight);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;          // 클릭이 입력칸으로 그대로 통과하게
        return t;
    }

    // 잠깐 보였다가 사라지는 빨간 문구.
    private void ShowError(Text label, string msg)
    {
        if (label == null) return;
        label.text = msg;
        label.color = ColError;
        errorClearAt[label] = Time.unscaledTime + 3.0f;
    }

    private void ClearErrors()
    {
        foreach (var kv in new List<Text>(errorClearAt.Keys))
            if (kv != null) kv.text = "";
        errorClearAt.Clear();
    }

    private void UpdateErrors()
    {
        if (errorClearAt.Count == 0) return;
        List<Text> done = null;
        foreach (var kv in errorClearAt)
        {
            if (Time.unscaledTime < kv.Value) continue;
            (done ??= new List<Text>()).Add(kv.Key);
        }
        if (done == null) return;
        foreach (var t in done) { if (t != null) t.text = ""; errorClearAt.Remove(t); }
    }

    private void OnClickStartGame()
    {
        string nick = (titleNicknameInput != null ? titleNicknameInput.text : "").Trim();
        string pw = (titlePasswordInput != null ? titlePasswordInput.text : "");

        if (string.IsNullOrEmpty(nick)) { ShowError(nickError, "닉네임을 입력하세요"); return; }
        if (string.IsNullOrEmpty(pw)) { ShowError(pwError, "비밀번호를 입력하세요"); return; }
        if (bootstrap == null) { ShowError(nickError, "연결 준비 중..."); return; }

        pendingPassword = pw;      // 세션 후 로비 복귀 시 자동 재로그인용
        LastPassword = pw;
        if (bootstrap.BeginLogin(nick, pw)) GoTo(Screen.Connecting);
        else ShowError(nickError, "로비 서버에 연결할 수 없습니다");
    }

    // ========================================================================
    //  회원가입 / 비밀번호 찾기 모달 (두 화면이 구조가 같아 하나로 만든다)
    // ========================================================================
    private void BuildAccountModal()
    {
        accountModal = New("AccountModal", canvas.transform);
        StretchFull(accountModal);
        AddImage(accountModal, new Color(0, 0, 0, 0.75f));      // 뒤쪽 클릭 차단

        var box = New("Box", accountModal.transform);
        SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-280, -210), new Vector2(280, 210));
        AddImage(box, ColPanel);

        var edge = New("Edge", box.transform);
        SetRect(edge, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -3), new Vector2(0, 0));
        AddImage(edge, new Color(0.78f, 0.64f, 0.36f, 0.8f));   // 상단 금색 라인

        var titleGo = New("T", box.transform);
        SetRect(titleGo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -64), new Vector2(-70, -18));
        accountTitle = AddText(titleGo, "회원 가입", 26, ColTan, TextAnchor.MiddleLeft);
        accountTitle.fontStyle = FontStyle.Bold;

        // 우상단 X
        MakeButton(box.transform, "X",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-56, -60), new Vector2(-14, -18),
            ColBtnDanger, 18, HideAccountModal);

        // 닉네임
        MakeFieldLabel(box.transform, "닉네임", -96);
        acNick = MakeInputField(box.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(140, -120), new Vector2(400, -80), "닉네임", "", 18);
        acNick.characterLimit = 0;
        acNick.onValueChanged.AddListener(OnModalNickChanged);

        // 새 비밀번호
        MakeFieldLabel(box.transform, "새 비밀번호", -148);
        acPw1 = MakeInputField(box.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(140, -172), new Vector2(400, -132), "새 비밀번호", "", 18);
        acPw1.contentType = InputField.ContentType.Password;
        acPw1.characterLimit = 32;

        // 새 비밀번호 확인 + 보기 체크박스
        MakeFieldLabel(box.transform, "비밀번호 확인", -200);
        acPw2 = MakeInputField(box.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(140, -224), new Vector2(400, -184), "한 번 더 입력", "", 18);
        acPw2.contentType = InputField.ContentType.Password;
        acPw2.characterLimit = 32;

        acShowPwBtn = MakeButton(box.transform, "☐ 표시",
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(410, -224), new Vector2(524, -184),
            ColBtn, 15, ToggleShowPassword);
        acShowPwLabel = acShowPwBtn.GetComponentInChildren<Text>();

        // 오류 문구는 비밀번호 확인 칸 아래 빈 공간에 모아서 띄운다.
        // 닉네임/비밀번호 어느 쪽 오류든 여기 한 곳에 나온다.
        var errGo = New("Err", box.transform);
        SetRect(errGo, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(28, -272), new Vector2(-28, -236));
        acPwError = AddText(errGo, "", 15, ColError, TextAnchor.MiddleCenter);
        acPwError.horizontalOverflow = HorizontalWrapMode.Overflow;
        acNickError = acPwError;        // 같은 자리를 공유

        // 확인 버튼
        acSubmitBtn = MakeButton(box.transform, "생성",
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-130, 24), new Vector2(130, 78),
            ColBtnStart, 22, OnClickAccountSubmit);
        acSubmitLabel = acSubmitBtn.GetComponentInChildren<Text>();
        acSubmitLabel.color = ColOnBright;

        accountModal.SetActive(false);
    }

    private void MakeFieldLabel(Transform parent, string s, float yTop)
    {
        var go = New("L", parent);
        SetRect(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, yTop - 24), new Vector2(136, yTop + 16));
        AddText(go, s, 16, ColTextDim, TextAnchor.MiddleLeft);
    }

    private void ToggleShowPassword()
    {
        showPassword = !showPassword;
        var ct = showPassword ? InputField.ContentType.Standard : InputField.ContentType.Password;
        if (acPw1 != null) { acPw1.contentType = ct; acPw1.ForceLabelUpdate(); }
        if (acPw2 != null) { acPw2.contentType = ct; acPw2.ForceLabelUpdate(); }
        if (acShowPwLabel != null) acShowPwLabel.text = showPassword ? "☑ 표시" : "☐ 표시";
    }

    private void ShowAccountModal(bool register)
    {
        accountRegisterMode = register;
        accountTitle.text = register ? "회원 가입" : "비밀번호 찾기";
        acSubmitLabel.text = register ? "생성" : "저장";

        acNick.text = ""; acPw1.text = ""; acPw2.text = "";
        acNickError.text = ""; acPwError.text = "";
        showPassword = false;
        if (acShowPwLabel != null) acShowPwLabel.text = "☐ 표시";
        acPw1.contentType = InputField.ContentType.Password;
        acPw2.contentType = InputField.ContentType.Password;

        accountModal.SetActive(true);
        acNick.ActivateInputField();
    }

    private void HideAccountModal()
    {
        if (accountModal != null) accountModal.SetActive(false);
    }

    // 모달 안 닉네임도 시작 화면과 같은 글자수 규칙을 쓴다.
    private void OnModalNickChanged(string value)
    {
        if (DisplayWidth(value) <= NickWidthLimit) { lastValidModalNick = value; return; }
        acNick.text = lastValidModalNick;
        ShowError(acNickError, NickLabelWarn);
    }

    private void OnClickAccountSubmit()
    {
        string nick = (acNick.text ?? "").Trim();
        string p1 = acPw1.text ?? "";
        string p2 = acPw2.text ?? "";

        if (string.IsNullOrEmpty(nick)) { ShowError(acNickError, "닉네임을 입력하세요"); return; }
        if (string.IsNullOrEmpty(p1)) { ShowError(acPwError, "새 비밀번호를 입력하세요"); return; }
        if (p1 != p2) { ShowError(acPwError, "비밀번호가 일치하지 않습니다"); return; }
        if (bootstrap == null || bootstrap.Lobby == null || !bootstrap.Lobby.IsConnected)
        {
            // 타이틀에서는 아직 로비에 붙기 전이다. 계정 작업만 하려고 연결한다.
            if (bootstrap == null || !bootstrap.ConnectLobbyOnly())
            {
                ShowError(acNickError, "로비 서버에 연결할 수 없습니다");
                return;
            }
        }

        if (accountRegisterMode) bootstrap.Lobby.SendRegister(nick, p1);
        else bootstrap.Lobby.SendResetPassword(nick, p1);
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
        MakeButton(mainPanel.transform, "플레이 전적",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-660, -86), new Vector2(-458, -28),
            ColBtn, 20, OnClickStats);
        MakeButton(mainPanel.transform, "파티 찾기",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-446, -86), new Vector2(-244, -28),
            ColBtn, 20, OnClickFind);
        MakeButton(mainPanel.transform, "파티 만들기",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-232, -86), new Vector2(-28, -28),
            ColBtnTan, 20, OnClickOpenCreate);

        // 중앙 안내(캐릭터 자리는 비움)
        var hint = New("CenterHint", mainPanel.transform);
        SetRect(hint, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 96), new Vector2(0, 132));
        // (안내 문구 삭제: 색이 너무 어두워 잘 보이지 않았음)
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
        SetRect(gridContainer, new Vector2(0, 0), new Vector2(1, 1), new Vector2(20, 280), new Vector2(-20, -110));
        var grid = gridContainer.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.spacing = new Vector2(10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.cellSize = new Vector2(170, 150);

        BuildPartyChat(box.transform);

        // 좌: 파티 나가기 / 우: 파티 시작(방장) 또는 준비(파티원). 비슷한 크기의 정사각형.
        leaveBtn = MakeButton(box.transform, "파티 나가기",
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-134, 12), new Vector2(-14, 112),
            ColBtnDanger, 18, OnClickLeave);

        startBtn = MakeButton(box.transform, "파티 시작",
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(14, 12), new Vector2(134, 112),
            ColBtnTan, 18, OnClickStartOrReady);
        startBtnLabel = startBtn.GetComponentInChildren<Text>();
    }

    // 파티 대기실 전용 채팅창. 위젯만 만들고 동작은 ChatUI가 맡는다.
    private void BuildPartyChat(Transform parent)
    {
        var panel = New("PartyChat", parent);
        // 높이 148 - (아래 입력칸 38 + 위 여백 8) = 뷰포트 102px ≈ 6줄
        SetRect(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(28, 124), new Vector2(-28, 272));
        AddImage(panel, ColPanel2);

        // 스크롤 영역. 줄이 늘어나도 잘리지 않고 아래로 밀린다.
        var viewport = New("Viewport", panel.transform);
        SetRect(viewport, new Vector2(0, 0), new Vector2(1, 1), new Vector2(10, 38), new Vector2(-10, -8));
        AddImage(viewport, new Color(0, 0, 0, 0.001f));       // Mask용 최소 그래픽
        viewport.AddComponent<RectMask2D>();

        var content = New("Content", viewport.transform);
        var crt = RT(content);
        crt.anchorMin = new Vector2(0, 0);
        crt.anchorMax = new Vector2(1, 0);
        crt.pivot = new Vector2(0.5f, 0f);
        crt.offsetMin = new Vector2(0, 0);
        crt.offsetMax = new Vector2(0, 0);

        var log = AddText(content, "", 14, ColText, TextAnchor.LowerLeft);
        log.supportRichText = true;
        var fit = content.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = panel.AddComponent<ScrollRect>();
        scroll.content = crt;
        scroll.viewport = RT(viewport);
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;

        // 입력칸 윗변(38)을 뷰포트 아랫변(38)에 딱 맞춰 붙인다.
        var fieldGo = New("Field", panel.transform);
        SetRect(fieldGo, new Vector2(0, 0), new Vector2(1, 0), new Vector2(10, 4), new Vector2(-10, 38));
        AddImage(fieldGo, new Color(1f, 1f, 1f, 0.06f));

        var fieldTxtGo = New("Txt", fieldGo.transform);
        SetRect(fieldTxtGo, Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0));
        var fieldTxt = AddText(fieldTxtGo, "", 14, ColText, TextAnchor.MiddleLeft);
        fieldTxt.supportRichText = false;

        var field = fieldGo.AddComponent<InputField>();
        field.textComponent = fieldTxt;
        field.lineType = InputField.LineType.SingleLine;
        field.characterLimit = 40;

        bootstrap.Chat?.BindPartyPanel(panel, log, field);
    }

    // ========================================================================
    //  헬퍼
    // ========================================================================
    // ========================================================================
    //  플레이 전적
    // ========================================================================
    private void BuildStatsPanel()
    {
        statsPanel = New("StatsPanel", canvas.transform);
        StretchFull(statsPanel);
        AddImage(statsPanel, new Color(0, 0, 0, 0.75f));

        var box = New("Box", statsPanel.transform);
        SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-260, -190), new Vector2(260, 190));
        AddImage(box, ColPanel);

        var edge = New("Edge", box.transform);
        SetRect(edge, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -3), new Vector2(0, 0));
        AddImage(edge, new Color(0.78f, 0.64f, 0.36f, 0.8f));

        var t = New("T", box.transform);
        SetRect(t, new Vector2(0, 1), new Vector2(1, 1), new Vector2(28, -64), new Vector2(-70, -18));
        AddText(t, "플레이 전적", 26, ColTan, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;

        MakeButton(box.transform, "X",
            new Vector2(1, 1), new Vector2(1, 1), new Vector2(-56, -60), new Vector2(-14, -18),
            ColBtnDanger, 18, () => statsPanel.SetActive(false));

        var bodyGo = New("Body", box.transform);
        SetRect(bodyGo, new Vector2(0, 0), new Vector2(1, 1), new Vector2(30, 24), new Vector2(-30, -76));
        statsBody = AddText(bodyGo, "불러오는 중...", 19, ColText, TextAnchor.UpperLeft);
        statsBody.lineSpacing = 1.6f;

        statsPanel.SetActive(false);
    }

    private void OnClickStats()
    {
        if (statsPanel == null) BuildStatsPanel();
        statsBody.text = "불러오는 중...";
        statsPanel.SetActive(true);
        bootstrap.Lobby?.SendStatsRequest();
    }

    private void ShowStats(PlayerStatsResult s)
    {
        if (statsPanel == null) BuildStatsPanel();
        statsPanel.SetActive(true);

        if (s.success == 0)
        {
            statsBody.text = "전적을 불러오지 못했습니다.\n(DB에 연결되어 있지 않을 수 있습니다)";
            return;
        }

        float rate = s.totalRuns > 0 ? 100f * s.totalExtractions / s.totalRuns : 0f;
        int m = s.bestSeconds / 60, sec = s.bestSeconds % 60;

        statsBody.text =
            $"총 플레이        {s.totalRuns} 판\n" +
            $"탈출 성공        {s.totalExtractions} 회   ({rate:F1}%)\n" +
            $"처치한 몬스터    {s.monsterKills}\n" +
            $"처치한 플레이어  {s.playerKills}\n" +
            $"최장 생존        {m:00}:{sec:00}";
    }

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
"- 방장이 [파티 시작]을 누르면 모든 팀원이 함께 던전으로 투입됩니다.\n" +
"\n" +
"[ 기본 조작 ]\n" +
"- 이동: W A S D  또는 방향키\n" +
"- 카메라: 마우스 이동\n" +
"- 공격: 마우스 좌클릭 (연타하면 4타까지 이어집니다)\n" +
"- 달리기: Shift\n" +
"- 점프: Space\n" +
"- 대상 고정: R\n" +
"- 줍기 / 상호작용: F\n" +
"- 인벤토리 & 장비창: I\n" +
"- 채팅: Enter로 입력창 열기, 던전에서는 Tab으로 [파티] / [팀] 전환, Esc로 취소\n" +
"- 메뉴: Esc\n" +
"\n" +
"[ 스태미나 ]\n" +
"- 최대 100. 공격 1회에 2, 달리는 동안 0.2초마다 1이 줄어듭니다.\n" +
"- 소모를 멈추고 1초가 지나면 0.1초마다 1씩 회복됩니다. 걷는 것은 회복을 막지 않습니다.\n" +
"- 바닥나면 달릴 수도, 공격할 수도 없습니다. 보스전은 한 사람당 스태미나를 한 바퀴 넘게 쓰도록 맞춰져 있으니 치고 빠지며 회복 타이밍을 나누세요.\n" +
"\n" +
"[ 전투 팁 ]\n" +
"- 몬스터는 일반 / 정예 / 보스로 나뉘며 체력과 공격력이 크게 다릅니다. 보스는 몇 대만 맞아도 쓰러지니 정면으로 버티지 마세요.\n" +
"- 다른 팀과 마주치면 교전할지 피할지 빠르게 판단하세요. 전투 중 제3의 팀이 끼어들 수 있습니다.\n" +
"- 체력이 부족하면 욕심내지 말고 탈출 지점으로 향하는 것이 이득일 때가 많습니다.\n" +
"\n" +
"[ 탈출 ]\n" +
"- 던전 어딘가의 탈출 포탈 안에서 3초를 버티면 탈출에 성공합니다.\n" +
"- 버티는 동안 포탈 밖으로 나가면 처음부터 다시 채워야 하고, 그 사이 다른 팀에게 노출되기 쉬우니 주변을 먼저 정리하세요.\n" +
"- 탈출하거나 사망하면 진행 시간과 처치한 몬스터 / 플레이어 수가 기록으로 남습니다.\n" +
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

    // 버튼 색을 런타임에 바꾼다. ColorBlock 전체를 갱신해야 실제로 적용된다.
    private static void SetButtonColor(Button btn, Color c)
    {
        if (btn == null) return;
        var cb = btn.colors;
        if (cb.normalColor == c) return;
        cb.normalColor = c;
        cb.highlightedColor = Lighten(c, 0.16f);
        cb.pressedColor = Lighten(c, -0.12f);
        cb.selectedColor = c;
        cb.disabledColor = new Color(c.r, c.g, c.b, 0.35f);
        btn.colors = cb;
        if (btn.targetGraphic != null) btn.targetGraphic.canvasRenderer.SetColor(c);
    }

    private InputField MakeInputField(Transform parent,
        Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax,
        string placeholder, string initial, int fontSize)
    {
        var go = New("Input", parent);
        SetRect(go, aMin, aMax, oMin, oMax);
        var bg = go.AddComponent<Image>(); bg.color = ColInput;
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