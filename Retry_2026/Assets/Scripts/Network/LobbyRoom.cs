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

    [Header("캐릭터")]
    [SerializeField] private float playerYOffset = 0f;     // 발이 바닥에 안 맞으면 조정
    [SerializeField] private float playerYaw = 0f;         // 등이 보이면 180
    [SerializeField] private float dragRotateSpeed = 0.4f; // 캐릭터 꾹 눌러 드래그 회전 감도
    [SerializeField] private float returnRotateSpeed = 12f;// 놓았을 때 제자리(정면) 복귀 속도

    private NetworkBootstrap bootstrap;
    private GameObject dioramaRoot, lobbyPlayer;
    private Camera cam;
    private bool active;
    private bool draggingPlayer;
    private bool returning;
    private Quaternion homeRot = Quaternion.identity;

    // 기존 카메라 설정 백업 (원복용)
    private bool camStateSaved;
    private CameraClearFlags savedClearFlags;
    private Color savedBg;
    private float savedFov, savedNear;

    // ========================================================================
    private void Start()
    {
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
        HandleDragRotate();
        HandleReturn();
    }

    // 중앙 캐릭터를 마우스로 꾹 눌러 드래그하면 좌우 회전 → 놓으면 정면(homeRot)으로 복귀
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
                draggingPlayer = true;
                returning = false;
            }
        }
        if (draggingPlayer && mouse.leftButton.isPressed)
        {
            float dx = mouse.delta.ReadValue().x;
            lobbyPlayer.transform.Rotate(0f, -dx * dragRotateSpeed, 0f, Space.World);
        }
        if (mouse.leftButton.wasReleasedThisFrame && draggingPlayer)
        {
            draggingPlayer = false;
            returning = true;   // 놓으면 제자리로 복귀 시작
        }
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

    private void PositionCamera()
    {
        if (cam == null) return;
        Vector3 p = Vector3.zero;
        cam.transform.position = p + new Vector3(0f, cameraHeight, cameraDistance);
        cam.transform.LookAt(p + new Vector3(0f, lookHeight, 0f));
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
        floor.transform.position = new Vector3(0, -0.05f, -0.5f);
        floor.transform.localScale = new Vector3(6f, 0.1f, 6f);
        floor.GetComponent<Renderer>().sharedMaterial =
            MakeLitMat(new Color(0.22f, 0.22f, 0.24f), floorTexture, new Vector2(3, 3));

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "WallTile";
        wall.transform.SetParent(dioramaRoot.transform, false);
        wall.transform.position = new Vector3(0, 1.6f, -2.8f);
        wall.transform.localScale = new Vector3(6f, 3.4f, 0.2f);
        wall.GetComponent<Renderer>().sharedMaterial =
            MakeLitMat(new Color(0.16f, 0.16f, 0.18f), wallTexture, new Vector2(3, 2));

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
        // Animator는 그대로 둠 → 기본 idle 재생
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