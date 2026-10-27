using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// ============================================================================
//  LobbyRoom  —  정지형 로비 디오라마 (HotS / 타르코프 / 와일드리프트 로비 스타일)
//
//  ▶ 구성: 정중앙에 LocalPlayerPrefab 캐릭터를 "고정"(이동/조작 비활성, idle)해서
//          세워두고, 카메라가 정면에서 비추며 완전히 고정. 카메라 방향에만
//          바닥 타일 + 뒷벽 타일.
//
//  ▶ 인스펙터:
//      - Local Player Prefab : LocalPlayerPrefab 드래그 (필수)
//      - Floor / Wall Texture: 바닥/벽 타일 텍스처 (선택, 없으면 단색)
//      - Camera Distance/Height/Look Height/Field Of View : 카메라 구도
//      - Player Yaw : 캐릭터가 등 보이면 180
//
//  ▶ 카메라 격리: 로비용으로 바꾼 메인 카메라 설정(시야각/배경 등)을 백업해뒀다가
//             게임 시작(세션 연결) 시 원복 → 인게임 카메라에 영향 없음.
//
//  ▶ LobbyUI 배경이 투명(이전 적용)이라 이 디오라마가 그대로 보임.
// ============================================================================
[DefaultExecutionOrder(10000)]
public class LobbyRoom : MonoBehaviour
{
    [Header("필수: LocalPlayerPrefab 드래그")]
    [SerializeField] private GameObject localPlayerPrefab;

    [Header("타일 텍스처 (선택)")]
    [SerializeField] private Texture2D floorTexture;
    [SerializeField] private Texture2D wallTexture;

    [Header("카메라 프레이밍")]
    [SerializeField] private float cameraDistance = 3.2f;
    [SerializeField] private float cameraHeight = 1.2f;
    [SerializeField] private float lookHeight = 1.0f;
    [SerializeField] private float fieldOfView = 38f;

    [Header("타이틀 화면 구도 (왼쪽 UI 3 : 캐릭터 7)")]
    [SerializeField] private float titleScreenX = 0.66f;          // 캐릭터가 놓일 가로 위치 0=왼끝 1=오른끝
    [SerializeField] private float titleCameraDistance = 2.6f;    // 작을수록 줌인
    [SerializeField] private float titleCameraHeight = 1.05f;
    [SerializeField] private float titleLookHeight = 0.92f;
    [SerializeField] private float titleFov = 38f;
    [SerializeField] private float viewMoveSeconds = 1.1f;        // 타이틀 → 로비 이동 시간

    [Header("캐릭터")]
    [SerializeField] private float playerYOffset = 0f;     // 발이 바닥에 안 맞으면 조정
    [SerializeField] private float playerYaw = 0f;         // 등이 보이면 180
    [SerializeField] private float dragRotateSpeed = 0.4f; // 캐릭터 꾹 눌러 드래그 회전 감도
    [SerializeField] private float returnRotateSpeed = 12f;// 놓았을 때 제자리(정면) 복귀 속도

    [Header("캐릭터 클릭 = 공격 모션")]
    [SerializeField] private float dragThresholdPx = 8f;   // 이만큼 움직이면 클릭이 아니라 드래그
    [SerializeField] private float clickMaxSeconds = 0.35f;// 이보다 오래 누르면 클릭으로 안 침
    [SerializeField] private float attackLockSeconds = 0.8f;// 모션 도는 동안 재입력 무시

    private NetworkBootstrap bootstrap;
    private GameObject dioramaRoot, lobbyPlayer;
    private Camera cam;
    private bool active;
    private bool draggingPlayer;
    private bool returning;
    private Quaternion homeRot = Quaternion.identity;

    // 클릭/드래그 판별 + 콤보 상태
    private bool pressedOnPlayer;
    private float pressTime, pressTravel;
    private float attackReadyAt;
    private const string AttackState = "sword_attack_1";

    // 기존 카메라 설정 백업 (원복용)
    private bool camStateSaved;
    private CameraClearFlags savedClearFlags;
    private Color savedBg;
    private float savedFov, savedNear;

    // 0 = 타이틀 구도(캐릭터 오른쪽), 1 = 로비 구도(캐릭터 중앙). 그 사이를 천천히 오간다.
    private static LobbyRoom instance;
    private float viewT, viewTarget;

    // LobbyUI가 화면을 바꿀 때 호출한다. 타이틀이면 0, 그 외에는 1로 흘러간다.
    public static void SetTitleView(bool title)
    {
        if (instance != null) instance.viewTarget = title ? 0f : 1f;
    }

    // ========================================================================
    private void Start()
    {
        instance = this;
        bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        EnsureCamera();
        BuildDiorama();
        SpawnFrozenPlayer();
        PositionCamera();
        active = true;
    }

    private void Update()
    {
        var id = bootstrap != null ? bootstrap.Identity : null;
        if (id != null && id.IsConnectedToSession)
        {
            if (active) Teardown();
            return;
        }
        if (!active) return;
        // 타이틀 ↔ 로비 구도 사이를 부드럽게 이동
        viewT = Mathf.MoveTowards(viewT, viewTarget,
                                  Time.unscaledDeltaTime / Mathf.Max(0.05f, viewMoveSeconds));
        PositionCamera();
        HandleDragRotate();
        HandleReturn();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // 캐릭터를 잡고 끌면 좌우 회전, 짧게 톡 치면 공격 모션.
    //
    // 둘을 가르는 기준은 두 가지를 AND로 본다.
    //   · 누른 채 움직인 거리가 dragThresholdPx 미만    (손이 안 흔들렸다)
    //   · 누르고 뗀 시간이 clickMaxSeconds 미만          (꾹 누른 게 아니다)
    // 회전도 거리 기준을 넘긴 뒤에야 시작하므로, 클릭하려다 1~2px 밀려도
    // 캐릭터가 홱 돌지 않는다.
    private void HandleDragRotate()
    {
        if (cam == null || lobbyPlayer == null) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI())
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 50f) && IsPartOfPlayer(hit.collider.transform))
            {
                pressedOnPlayer = true;
                draggingPlayer = false;
                pressTime = Time.unscaledTime;
                pressTravel = 0f;
                returning = false;
            }
        }

        if (pressedOnPlayer && mouse.leftButton.isPressed)
        {
            float dx = mouse.delta.ReadValue().x;
            pressTravel += mouse.delta.ReadValue().magnitude;

            if (!draggingPlayer && pressTravel >= dragThresholdPx)
                draggingPlayer = true;              // 여기서부터 회전으로 인정

            if (draggingPlayer)
                lobbyPlayer.transform.Rotate(0f, -dx * dragRotateSpeed, 0f, Space.World);
        }

        if (mouse.leftButton.wasReleasedThisFrame && pressedOnPlayer)
        {
            bool wasClick = !draggingPlayer
                         && pressTravel < dragThresholdPx
                         && (Time.unscaledTime - pressTime) < clickMaxSeconds;

            if (wasClick) PlayAttack();
            else returning = true;                  // 돌렸으면 정면으로 복귀

            pressedOnPlayer = false;
            draggingPlayer = false;
        }
    }

    // 로비 화면에서만, 그리고 1타만. 시작 화면(viewT<1)에서는 반응하지 않는다.
    private void PlayAttack()
    {
        if (viewT < 0.99f) return;                              // 아직 타이틀 구도
        if (Time.unscaledTime < attackReadyAt) return;          // 모션 끝나기 전 재입력 무시

        var anim = lobbyPlayer != null ? lobbyPlayer.GetComponentInChildren<Animator>() : null;
        if (anim == null || anim.runtimeAnimatorController == null) return;

        int hash = Animator.StringToHash(AttackState);
        if (!anim.HasState(0, hash)) return;                    // 없는 상태면 조용히 무시
        anim.CrossFadeInFixedTime(hash, 0.06f, 0, 0f);
        attackReadyAt = Time.unscaledTime + attackLockSeconds;
    }

    private void HandleReturn()
    {
        if (!returning || lobbyPlayer == null) return;
        lobbyPlayer.transform.rotation =
            Quaternion.Slerp(lobbyPlayer.transform.rotation, homeRot, returnRotateSpeed * Time.deltaTime);
        if (Quaternion.Angle(lobbyPlayer.transform.rotation, homeRot) < 0.8f)
        {
            lobbyPlayer.transform.rotation = homeRot;
            returning = false;
        }
    }

    private bool IsPartOfPlayer(Transform t)
    {
        while (t != null) { if (t == lobbyPlayer.transform) return true; t = t.parent; }
        return false;
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    // ========================================================================
    //  카메라 (고정 + 원복용 백업)
    // ========================================================================
    private void EnsureCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            try { go.tag = "MainCamera"; } catch { }
            cam = go.AddComponent<Camera>();
        }
        else
        {
            savedClearFlags = cam.clearFlags;
            savedBg = cam.backgroundColor;
            savedFov = cam.fieldOfView;
            savedNear = cam.nearClipPlane;
            camStateSaved = true;
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.05f;
    }

    // viewT 0 = 타이틀 구도(캐릭터가 화면 오른쪽), 1 = 로비 구도(정중앙).
    //
    // 핵심: 카메라를 "회전시켜" 캐릭터를 옆으로 보내면 원근이 틀어져 캐릭터가
    // 기울어 보인다. 그래서 LookAt으로 방향을 먼저 정한 뒤 회전은 손대지 않고
    // 오른쪽 축으로 평행 이동만 시킨다(렌즈 시프트와 같은 효과).
    private void PositionCamera()
    {
        if (cam == null) return;

        float s = viewT * viewT * (3f - 2f * viewT);           // smoothstep: 끝에서 부드럽게
        float dist = Mathf.Lerp(titleCameraDistance, cameraDistance, s);
        float hgt = Mathf.Lerp(titleCameraHeight, cameraHeight, s);
        float look = Mathf.Lerp(titleLookHeight, lookHeight, s);
        float fov = Mathf.Lerp(titleFov, fieldOfView, s);
        float sx = Mathf.Lerp(titleScreenX, 0.5f, s);          // 캐릭터의 가로 화면 위치

        cam.fieldOfView = fov;
        cam.transform.position = new Vector3(0f, hgt, dist);
        cam.transform.LookAt(new Vector3(0f, look, 0f));

        // 캐릭터 거리에서의 화면 반폭(월드 단위) → 원하는 화면 비율만큼 옆으로 민다
        float halfH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * dist;
        float halfW = halfH * Mathf.Max(0.1f, cam.aspect);
        cam.transform.position -= cam.transform.right * ((sx - 0.5f) * 2f * halfW);
    }

    // ========================================================================
    //  바닥/벽 타일 + 조명
    // ========================================================================
    private void BuildDiorama()
    {
        dioramaRoot = new GameObject("LobbyDiorama");
        dioramaRoot.transform.SetParent(transform, false);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FloorTile";
        floor.transform.SetParent(dioramaRoot.transform, false);
        // 바닥/벽을 가로·세로 2배로. 타일 반복도 2배로 올려야 타일 한 칸 크기가 유지된다.
        floor.transform.position = new Vector3(0, -0.05f, -1.0f);
        floor.transform.localScale = new Vector3(12f, 0.1f, 12f);
        floor.GetComponent<Renderer>().sharedMaterial =
            MakeLitMat(new Color(0.22f, 0.22f, 0.24f), floorTexture, new Vector2(6, 6));

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallTile";
        wall.transform.SetParent(dioramaRoot.transform, false);
        wall.transform.position = new Vector3(0, 3.2f, -5.6f);
        wall.transform.localScale = new Vector3(12f, 6.8f, 0.2f);
        wall.GetComponent<Renderer>().sharedMaterial =
            MakeLitMat(new Color(0.16f, 0.16f, 0.18f), wallTexture, new Vector2(6, 4));

        var lgt = new GameObject("LobbyKeyLight");
        lgt.transform.SetParent(dioramaRoot.transform, false);
        var L = lgt.AddComponent<Light>();
        L.type = LightType.Point;
        L.transform.position = new Vector3(1.2f, 2.4f, 2.4f);
        L.range = 14f; L.intensity = 1.5f; L.color = new Color(1f, 0.97f, 0.92f);
        RenderSettings.ambientLight = new Color(0.34f, 0.35f, 0.38f);
    }

    // ========================================================================
    //  정지된 캐릭터 = LocalPlayerPrefab (모든 조작 비활성, idle만)
    // ========================================================================
    private void SpawnFrozenPlayer()
    {
        if (localPlayerPrefab == null)
        {
            Debug.LogError("[LobbyRoom] localPlayerPrefab이 비어있음! 인스펙터에 LocalPlayerPrefab을 드래그하세요.");
            return;
        }

        homeRot = Quaternion.Euler(0, playerYaw, 0);
        lobbyPlayer = Instantiate(localPlayerPrefab, new Vector3(0, playerYOffset, 0), homeRot);
        lobbyPlayer.name = "LobbyDisplayPlayer";

        DisableComp(lobbyPlayer, "PlayerInput");
        DisableComp(lobbyPlayer, "Player_Movement");
        DisableComp(lobbyPlayer, "Player_Camera_Controller");
        DisableComp(lobbyPlayer, "Player_Attack");
        DisableComp(lobbyPlayer, "Player_LockOnSystem");
        DisableComp(lobbyPlayer, "PlayerPickupInteractor");
        DisableComp(lobbyPlayer, "LocalPlayerInputSender");
        DisableComp(lobbyPlayer, "LocalPlayerAttackSender");
        var cc = lobbyPlayer.GetComponent<CharacterController>();
        // 클릭(드래그 회전) 감지용 콜라이더 (CharacterController는 비활성이라 레이캐스트 안 됨)
        var pick = lobbyPlayer.AddComponent<CapsuleCollider>();
        if (cc != null) { pick.center = cc.center; pick.height = cc.height; pick.radius = cc.radius; }
        else { pick.center = new Vector3(0, 0.95f, 0); pick.height = 1.9f; pick.radius = 0.4f; }
        if (cc != null) cc.enabled = false;
        // Animator는 그대로 둠 → 기본 idle 재생.
        // 단 루트 모션은 끈다. 켜두면 클릭 공격 모션이 캐릭터를 앞으로 밀어낸다.
        var anim = lobbyPlayer.GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;
    }

    // ========================================================================
    //  정리 + 카메라 원복
    // ========================================================================
    private void Teardown()
    {
        active = false;
        if (camStateSaved && cam != null)
        {
            cam.clearFlags = savedClearFlags;
            cam.backgroundColor = savedBg;
            cam.fieldOfView = savedFov;
            cam.nearClipPlane = savedNear;
        }
        if (dioramaRoot != null) Destroy(dioramaRoot);
        if (lobbyPlayer != null) Destroy(lobbyPlayer);
    }

    // ========================================================================
    //  헬퍼
    // ========================================================================
    private static void DisableComp(GameObject go, string typeName)
    {
        var c = go.GetComponent(typeName) as Behaviour;
        if (c != null) c.enabled = false;
    }

    private static Material MakeLitMat(Color c, Texture tex, Vector2 tiling)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var m = new Material(sh);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling); }
            if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", tiling); }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        }
        else
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
        return m;
    }
}