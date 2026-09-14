using UnityEngine;

// ============================================================================
//  PlayerPortrait — 스테이터스 UI 프로필 칸에 들어갈 "살아 있는" 얼굴 사진.
//
//  캐릭터 모델을 미리 찍어 png로 굽지 않는다. 전용 카메라를 머리 앞에 띄워
//  RenderTexture로 실시간 렌더하고, HUD의 RawImage가 그 텍스처를 그린다.
//  장비를 바꾸거나 피격 모션이 나와도 프로필이 그대로 따라 움직인다.
//
//  배경 지우기: 레이어를 새로 파지 않고 far clip(0.9m)으로 자른다.
//  머리보다 0.9m 뒤에 있는 벽·바닥은 카메라에 아예 들어오지 않는다.
// ============================================================================
[DefaultExecutionOrder(200)]
public class PlayerPortrait : MonoBehaviour
{
    public static PlayerPortrait Instance { get; private set; }

    [SerializeField] private int textureSize = 256;
    [SerializeField] private float distance = 0.45f;   // 얼굴에서 카메라까지
    [SerializeField] private float fov = 30f;          // 작을수록 얼굴이 꽉 참
    [SerializeField] private float farClip = 0.9f;     // 이 뒤의 배경은 잘려나감
    [SerializeField] private float heightOffset = 0.04f;

    private Camera cam;
    private RenderTexture rt;
    private Transform head;
    private Transform playerRoot;

    public RenderTexture Texture => rt;
    public bool HasTarget => head != null;

    // HUD가 로컬 플레이어를 찾으면 이걸 불러준다. 같은 대상이면 아무것도 안 한다.
    public static PlayerPortrait Ensure(Transform player)
    {
        if (Instance == null)
        {
            var go = new GameObject("PlayerPortrait");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PlayerPortrait>();
        }
        Instance.SetTarget(player);
        return Instance;
    }

    public void SetTarget(Transform player)
    {
        if (player == null || player == playerRoot) return;
        playerRoot = player;
        head = FindHead(player);
        EnsureCamera();
    }

    // 휴머노이드면 머리 뼈를 바로 얻고, 아니면 이름으로 찾고,
    // 그것도 없으면 루트에서 키만큼 올린 지점을 쓴다.
    private static Transform FindHead(Transform root)
    {
        var anim = root.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            var bone = anim.GetBoneTransform(HumanBodyBones.Head);
            if (bone != null) return bone;
        }
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            string n = t.name.ToLowerInvariant();
            if (n == "head" || n.EndsWith(":head") || n.EndsWith("_head")) return t;
        }
        return root;
    }

    private void EnsureCamera()
    {
        if (rt == null)
        {
            rt = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "PlayerPortraitRT",
                antiAliasing = 2
            };
            rt.Create();
        }

        if (cam != null) return;

        var camGo = new GameObject("PortraitCamera");
        camGo.transform.SetParent(transform, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = farClip;
        cam.targetTexture = rt;
        cam.depth = -50;              // 메인 카메라보다 먼저 그린다
        cam.allowHDR = false;
        cam.allowMSAA = false;

        // 던전이 어두워도 얼굴은 항상 보이게 카메라에 조명을 붙인다.
        var lgt = new GameObject("PortraitLight");
        lgt.transform.SetParent(camGo.transform, false);
        lgt.transform.localPosition = new Vector3(0.15f, 0.2f, 0.05f);
        var L = lgt.AddComponent<Light>();
        L.type = LightType.Point;
        L.range = 2.2f;
        L.intensity = 2.4f;
        L.color = new Color(1f, 0.96f, 0.9f);
        L.shadows = LightShadows.None;
    }

    // 머리 뼈는 애니메이션이 끝난 뒤에 최종 위치가 정해지므로 LateUpdate에서 따라간다.
    private void LateUpdate()
    {
        if (cam == null) return;
        if (head == null || playerRoot == null) { cam.enabled = false; return; }

        cam.enabled = true;
        Vector3 focus = head.position + Vector3.up * heightOffset;
        // 머리 뼈의 축은 리그마다 달라서 못 믿는다. 캐릭터 몸통이 보는 방향을 쓴다.
        cam.transform.position = focus + playerRoot.forward * distance;
        cam.transform.LookAt(focus);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (cam != null) cam.targetTexture = null;
        if (rt != null) { rt.Release(); Destroy(rt); }
    }
}
