using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// ============================================================================
//  PauseMenu  —  Esc 키 메뉴 (로비/인게임 공용)
//
//  ▶ 사용법: 씬의 아무 GameObject(예: NetworkBootstrap 오브젝트)에 컴포넌트 하나 추가.
//            자체 캔버스를 만들어 LobbyUI 위(sortingOrder 200)에 표시.
//
//  ▶ Esc 토글. 상황에 따라 버튼이 달라짐:
//      - 로비   : [시작 화면으로(닉네임 초기화)] [게임 종료] [계속하기]
//      - 세션   : [로비로 나가기] [게임 종료] [계속하기]
//        (설정 버튼은 아직 만들 게 조작키밖에 없어서 보류)
//
//  ▶ 화면 전환(시작 화면으로 / 로비로 나가기)은 씬 새로고침으로 처리 → 인게임 상태가
//     깔끔하게 정리되고 시작 화면부터 다시 시작. (현재 씬이 Build Settings에 포함돼 있어야 함)
//  ▶ 인게임에서 열려도 timeScale은 그대로(1) → 네트워크 동기화/원격 플레이어는 계속 움직임.
//     대신 내 캐릭터의 이동/카메라/공격만 잠그고, 닫으면 복구.
// ============================================================================
public class PauseMenu : MonoBehaviour
{
    static readonly Color ColDim = new Color(0, 0, 0, 0.72f);
    static readonly Color ColPanel = new Color32(0x1B, 0x1F, 0x23, 0xFF);
    static readonly Color ColText = new Color32(0xD7, 0xD2, 0xC7, 0xFF);
    static readonly Color ColTan = new Color32(0xC7, 0xA8, 0x6B, 0xFF);
    static readonly Color ColBtn = new Color32(0x2B, 0x30, 0x2A, 0xFF);
    static readonly Color ColBtnTan = new Color32(0x46, 0x3C, 0x24, 0xFF);
    static readonly Color ColBtnDanger = new Color32(0x4E, 0x2C, 0x28, 0xFF);

    private NetworkBootstrap bootstrap;
    private Font font;
    private GameObject menuRoot, buttonContainer;
    private Text titleText;
    private bool isOpen;

    private void Start()
    {
        bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        font = LoadFont();
        EnsureEventSystem();
        BuildCanvas();
        SetMenu(false);
    }

    private bool lastDead;
    private void Update()
    {
        bool dead = IsLocalPlayerDead();
        if (dead != lastDead)
        {
            lastDead = dead;
            if (dead) SetMenu(true);            // 사망 → 메뉴 자동 오픈(계속하기 없음)
            else if (isOpen) RebuildButtons();  // 부활 등으로 살아나면 버튼 갱신
        }

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (dead) return;                   // 죽으면 Esc로 닫기 불가
            SetMenu(!isOpen);
        }
    }

    private bool IsLocalPlayerDead()
    {
        var p = bootstrap != null ? bootstrap.LocalPlayer : null;
        if (p == null) return false;
        var ps = p.GetComponent<Player_State>();
        return ps != null && ps.IsDead;
    }

    private void OnDisable()
    {
        // 혹시 메뉴 열린 채 비활성화돼도 조작 복구
        if (isOpen) FreezeLocalControl(false);
    }

    // ========================================================================
    private void SetMenu(bool open)
    {
        isOpen = open;
        if (menuRoot != null) menuRoot.SetActive(open);

        if (open)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RebuildButtons();
            FreezeLocalControl(true);   // 내 캐릭터 조작/카메라만 잠금 (timeScale 안 건드림 → 네트워크 동기화 계속)
        }
        else
        {
            FreezeLocalControl(false);
        }
    }

    // 메뉴 동안 내 인게임 캐릭터의 이동/카메라/공격만 비활성. 네트워크 송수신은 그대로 → 원격 플레이어 계속 동기화됨.
    private readonly System.Collections.Generic.List<Behaviour> frozen = new System.Collections.Generic.List<Behaviour>();
    private void FreezeLocalControl(bool freeze)
    {
        if (freeze)
        {
            frozen.Clear();
            var p = bootstrap != null ? bootstrap.LocalPlayer : null;   // 인게임 캐릭터 (로비엔 null)
            if (p != null)
            {
                FreezeOne(p, "Player_Movement");
                FreezeOne(p, "Player_Camera_Controller");
                FreezeOne(p, "Player_Attack");
            }
        }
        else
        {
            foreach (var b in frozen) if (b != null) b.enabled = true;
            frozen.Clear();
        }
    }
    private void FreezeOne(GameObject go, string typeName)
    {
        var c = go.GetComponent(typeName) as Behaviour;
        if (c != null && c.enabled) { c.enabled = false; frozen.Add(c); }
    }

    private bool InSession()
    {
        var id = bootstrap != null ? bootstrap.Identity : null;
        return id != null && id.IsConnectedToSession;
    }

    private void RebuildButtons()
    {
        for (int i = buttonContainer.transform.childCount - 1; i >= 0; --i)
            Destroy(buttonContainer.transform.GetChild(i).gameObject);

        bool session = InSession();
        bool dead = IsLocalPlayerDead();
        titleText.text = dead ? "사망" : (session ? "메뉴 — 게임 중" : "메뉴");

        if (session)
            AddButton("로비로 나가기", ColBtnTan, ReturnToLobby);
        else
            AddButton("시작 화면으로", ColBtnTan, ReloadToTitle);

        AddButton("게임 종료", ColBtnDanger, QuitGame);
        if (!dead) AddButton("계속하기", ColBtn, () => SetMenu(false));   // 사망 시엔 제거
    }

    // ── 동작 ──
    // 시작 화면으로 들어갈 때 LobbyUI가 읽는 자동 재로그인 플래그/닉네임 (PlayerPrefs)
    public const string PrefAutoLogin = "retry_autologin";
    public const string PrefAutoNick = "retry_autologin_nick";

    // 세션 → 로비: 닉네임을 유지한 채 로비로 복귀.
    // 씬을 새로고침해 인게임 상태를 깨끗이 리셋한 뒤, 저장된 닉네임으로 자동 재로그인 → 로비 Main.
    private void ReturnToLobby()
    {
        string nick = (bootstrap != null && bootstrap.Identity != null) ? bootstrap.Identity.PlayerName : "";
        PlayerPrefs.SetString(PrefAutoNick, nick ?? "");
        PlayerPrefs.SetInt(PrefAutoLogin, 1);
        PlayerPrefs.Save();
        LeaveSession();   // 캐릭터를 세션에서 내보냄
        DoReload();
    }

    // 시작(타이틀) 화면으로: 자동 재로그인 해제(닉네임 초기화) 후 새로고침.
    private void ReloadToTitle()
    {
        PlayerPrefs.SetInt(PrefAutoLogin, 0);
        PlayerPrefs.Save();
        LeaveSession();
        DoReload();
    }

    private void DoReload()
    {
        try { SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
        catch (System.Exception e)
        {
            Debug.LogError("[PauseMenu] 씬 새로고침 실패. 현재 씬을 Build Settings에 추가하세요. " + e.Message);
            SetMenu(false);
        }
    }

    // 세션 소켓 종료 → 서버가 내 캐릭터를 세션에서 제거하고 다른 플레이어에게 알림(LEAVE_VIEW).
    private void LeaveSession()
    {
        if (bootstrap != null && bootstrap.Session != null)
            bootstrap.Session.Disconnect();
    }

    private void QuitGame()
    {
        LeaveSession();   // 종료 전에 캐릭터를 세션에서 내보냄
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ========================================================================
    //  UI 빌드
    // ========================================================================
    private void BuildCanvas()
    {
        var canvasGo = new GameObject("PauseMenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // LobbyUI(100) 위
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        menuRoot = New("Root", canvasGo.transform);
        StretchFull(menuRoot);

        var dim = New("Dim", menuRoot.transform);
        StretchFull(dim);
        dim.AddComponent<Image>().color = ColDim;   // 뒤 클릭 차단

        var box = New("Box", menuRoot.transform);
        SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200, -160), new Vector2(200, 160));
        box.AddComponent<Image>().color = ColPanel;

        var titleGo = New("Title", box.transform);
        SetRect(titleGo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -70), new Vector2(-24, -18));
        titleText = AddText(titleGo, "메뉴", 30, ColTan, TextAnchor.MiddleCenter);
        titleText.fontStyle = FontStyle.Bold;

        buttonContainer = New("Buttons", box.transform);
        SetRect(buttonContainer, new Vector2(0, 0), new Vector2(1, 1), new Vector2(28, 28), new Vector2(-28, -84));
        var vlg = buttonContainer.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 14;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
    }

    private void AddButton(string label, Color baseCol, System.Action onClick)
    {
        var go = New("Btn_" + label, buttonContainer.transform);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 56; le.preferredHeight = 56;
        var img = go.AddComponent<Image>(); img.color = Color.white;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = baseCol;
        cb.highlightedColor = Lighten(baseCol, 0.16f);
        cb.pressedColor = Lighten(baseCol, -0.12f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        var t = New("Label", go.transform);
        StretchFull(t);
        AddText(t, label, 22, ColText, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
    }

    // ========================================================================
    //  헬퍼
    // ========================================================================
    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
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

    private static void StretchFull(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void SetRect(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
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

    private static Color Lighten(Color c, float amt)
    {
        return new Color(Mathf.Clamp01(c.r + amt), Mathf.Clamp01(c.g + amt), Mathf.Clamp01(c.b + amt), c.a);
    }
}
