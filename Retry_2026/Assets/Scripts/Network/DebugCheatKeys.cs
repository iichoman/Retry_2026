using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Development shortcuts: F9 approaches the exit room; F10 approaches the boss room.
// Online movement always remains an existing server request. Neither key extracts,
// kills the boss, nor changes the server's debug-permission setting.
public class DebugCheatKeys : MonoBehaviour
{
    [SerializeField] private bool enableCheats = true;

    private const float FloorBlockHeight = 1f;
    private const float LandingClearance = .08f;
    private const float CapsuleClearance = .03f;
    private const float RoomApproachDistance = 6f;
    private readonly Collider[] overlaps = new Collider[32];
    private NetworkBootstrap bootstrap;
    private DungeonGenerator_ChunkMesh dungeon;
    private Transform localPlayer;
    private bool onlinePermissionNoticeShown;
    private int rewardTeleportCursor=-1;
    private string notice;
    private float noticeUntil, awaitingUntil;
    private Font noticeFont;
    private GUIStyle noticeStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterScenes()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureOfflineHandler();
    public static void EnsureOfflineHandler()
    {
        if(FindFirstObjectByType<DebugCheatKeys>()!=null || FindFirstObjectByType<DungeonGenerator_ChunkMesh>()==null) return;
        new GameObject("Dungeon Debug Keys (F9 Exit, F10 Boss)").AddComponent<DebugCheatKeys>();
    }

    public void Initialize(NetworkBootstrap bs)
    {
        bootstrap = bs;
        enableCheats = true;
        enabled = true;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if(awaitingUntil>0 && Time.unscaledTime>=awaitingUntil)
        {
            awaitingUntil=0; ShowNotice("이동 응답이 없습니다. 서버의 순간이동 허용 여부를 확인하세요.");
        }
        if (!enableCheats || Time.timeScale <= 0 || ChatUI.IsTyping) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f9Key.wasPressedThisFrame) RequestTeleport(RoomType.Exit);
        else if (keyboard.f10Key.wasPressedThisFrame) RequestTeleport(RoomType.Boss);
        else if(keyboard.f8Key.wasPressedThisFrame) {
            var economy=EconomyClient.Active;
            if(bootstrap!=null && !bootstrap.SessionGameStarted && (bootstrap.Session==null || !bootstrap.Session.IsConnected) && economy!=null && economy.Supported && economy.State.inRun==0) {
                economy.Request(5);ShowNotice("로비 데모 골드 +1,000 G 요청");
            } else ShowNotice("F8 골드는 로비에서만 지급됩니다.");
        }
        else if(keyboard.f7Key.wasPressedThisFrame) RequestDemo(9,"자기장 다음 단계 · 전체 적용");
        else if(keyboard.f6Key.wasPressedThisFrame) RequestDemo(10,"다음 남은 상자로 이동");
        else if(keyboard.f5Key.wasPressedThisFrame) RequestDemo(11,"다음 함정 발판 2.5m 앞 이동");
        else if(keyboard.f3Key.wasPressedThisFrame) RequestTeleport(RoomType.Reward);
        else if(keyboard.f4Key.wasPressedThisFrame) RequestDemo(8,"스태미나 무제한 전환 요청");
    }

    private void RequestDemo(int action,string label) {
        if(bootstrap==null || !bootstrap.SessionGameStarted || ABClient.Active==null || !ABClient.Active.Supported || !CanUseGameplayKeys()) {ShowNotice("게임 세션에서 사용할 수 있습니다.");return;}
        ABClient.Active.Request(action,0);ShowNotice(label);
    }

    /// <returns>True when an online request was sent or an offline move completed.</returns>
    public bool RequestTeleport(RoomType roomType)
    {
        if (roomType != RoomType.Exit && roomType != RoomType.Boss && roomType != RoomType.Reward) return false;
        if (!enableCheats || !isActiveAndEnabled || !CanUseGameplayKeys()) return false;
        if (NetworkRuntime.IsOnline || (bootstrap != null && bootstrap.Session != null && bootstrap.Session.IsConnected))
        {
            if (bootstrap == null || bootstrap.Session == null || !bootstrap.Session.IsConnected
                || !bootstrap.SessionGameStarted) {ShowNotice("게임 세션에 접속한 뒤 사용할 수 있습니다."); return false;}

            if (!onlinePermissionNoticeShown)
            {
                onlinePermissionNoticeShown = true;
                Debug.Log("[DebugKeys] F9/F10은 서버에 이동을 요청합니다. 서버의 DEBUG_ALLOW_TELEPORT 허용이 필요하며, 허용되지 않으면 이동하지 않습니다.", this);
            }
            if(roomType==RoomType.Reward){RequestDemo(ABWire.RewardTeleport,"다음 보상방으로 이동 요청");return true;}
            if (roomType == RoomType.Exit) bootstrap.RequestDebugTeleportExit();
            else bootstrap.RequestDebugTeleportBoss();
            awaitingUntil=Time.unscaledTime+4;
            ShowNotice((roomType==RoomType.Exit?"F9 탈출방":"F10 보스방")+" 이동 요청 · 서버 응답 대기");
            return true;
        }

        bool moved = TryTeleportOffline(roomType);
        ShowNotice(moved?(roomType==RoomType.Reward?"다음 보상방으로 이동했습니다.":roomType==RoomType.Exit?"탈출방으로 이동했습니다.":"보스방으로 이동했습니다."):"목적지 방이나 안전한 착지 지점을 찾지 못했습니다.");
        if (!moved) Debug.LogWarning("[DebugKeys] 해당 방에 충돌 없이 설 수 있는 생성된 바닥이 없습니다.", this);
        return moved;
    }

    // Never falls back from a connected session to local movement, even during
    // an online-mode transition. Public for offline practice/editor validation.
    public bool TryTeleportOffline(RoomType roomType)
    {
        if (NetworkRuntime.IsOnline || (bootstrap != null && bootstrap.Session != null && bootstrap.Session.IsConnected)) return false;
        if (roomType != RoomType.Exit && roomType != RoomType.Boss && roomType != RoomType.Reward) return false;
        if (!enableCheats || !isActiveAndEnabled || !CanUseGameplayKeys()) return false;
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator_ChunkMesh>();
        if (dungeon == null || !dungeon.isActiveAndEnabled) return false;
        CharacterController capsule = localPlayer.GetComponent<CharacterController>();
        if (capsule == null || !capsule.enabled || Vector3.Dot(localPlayer.up, Vector3.up) < .99f) return false;

        Physics.SyncTransforms();
        var destinations=new List<Room>();foreach(var room in dungeon.Rooms)if(room.type==roomType)destinations.Add(room);
        if(roomType==RoomType.Reward)destinations.Sort((a,b)=>{int groupA=a.id>rewardTeleportCursor?0:1,groupB=b.id>rewardTeleportCursor?0:1;return groupA!=groupB?groupA.CompareTo(groupB):a.id.CompareTo(b.id);});
        foreach (Room room in destinations)
        {
            if (room.type != roomType) continue;
            if (!TryFindLanding(room, capsule, out Vector3 landing)) continue;
            localPlayer.GetComponent<PlayerDefenseController>()?.CancelDefense();
            localPlayer.GetComponent<Player_Attack>()?.CancelCurrentAttack();
            localPlayer.GetComponent<Player_LockOnSystem>()?.ClearLockOn();
            localPlayer.GetComponent<Player_Movement>()?.ResetVerticalMotion();
            capsule.enabled = false;
            localPlayer.position = landing;
            capsule.enabled = true;
            Physics.SyncTransforms();
            if(roomType==RoomType.Reward)rewardTeleportCursor=room.id;
            return true;
        }
        return false;
    }

    private bool CanUseGameplayKeys()
    {
        if (Time.timeScale <= 0 || ChatUI.IsTyping) return false;
        if (bootstrap != null && bootstrap.LocalPlayer != null) localPlayer = bootstrap.LocalPlayer.transform;
        if (localPlayer == null || !localPlayer.gameObject.activeInHierarchy ||
            localPlayer.GetComponent<Defalult_Input>()==null || !localPlayer.GetComponent<Defalult_Input>().isActiveAndEnabled)
        {
            // Search only on a key request, never on every frame. Offline players
            // carry the same local input component as the network player prefab.
            localPlayer=null;
            foreach(var found in FindObjectsByType<Defalult_Input>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
            {
                var candidateMovement=found.GetComponent<Player_Movement>();
                var candidateState=found.GetComponent<Player_State>();
                if(!found.isActiveAndEnabled || !found.IsGameplayInputEnabled || candidateMovement==null || !candidateMovement.isActiveAndEnabled || candidateState!=null && candidateState.IsDead) continue;
                localPlayer=found.transform; break;
            }
        }
        if (localPlayer == null || !localPlayer.gameObject.activeInHierarchy) return false;
        var input = localPlayer.GetComponent<Defalult_Input>();
        var state = localPlayer.GetComponent<Player_State>();
        var movement = localPlayer.GetComponent<Player_Movement>();
        return input != null && input.isActiveAndEnabled && input.IsGameplayInputEnabled
            && movement != null && movement.isActiveAndEnabled && (state == null || !state.IsDead);
    }

    public void NotifyServerTeleportApplied()
    {
        awaitingUntil=0; ShowNotice("서버가 보낸 이동 위치를 적용했습니다.");
    }
    public static bool MatchesRoom(DungeonGenerator_ChunkMesh generator, RoomType type, Vector3 position)
    {
        if (generator == null || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)) return false;
        var tile = generator.WorldToTile(position);
        foreach (var room in generator.Rooms)
            if (room.type == type && tile.x >= room.bounds.xMin && tile.x < room.bounds.xMax
                && tile.z >= room.bounds.zMin && tile.z < room.bounds.zMax) return true;
        return false;
    }
    private void ShowNotice(string text) {notice=text; noticeUntil=Time.unscaledTime+4;}
    private void OnGUI()
    {
        bool infinite=PlayerStamina.Local!=null && PlayerStamina.Local.DemoInfinite;
        if((string.IsNullOrEmpty(notice) || Time.unscaledTime>noticeUntil) && !infinite) return;
        if(noticeStyle==null)
        {
            noticeFont=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},17);
            noticeStyle=new GUIStyle(GUI.skin.box){font=noticeFont,fontSize=17,alignment=TextAnchor.MiddleCenter};
        }
        float width=Mathf.Min(640,Screen.width-24);
        GUI.Box(new Rect((Screen.width-width)*.5f,Screen.height-150,width,44),Time.unscaledTime<=noticeUntil?notice:"데모 · 스태미나 무제한 ON · F4로 해제",noticeStyle);
    }
    private void OnDestroy() {if(noticeFont!=null) Destroy(noticeFont);}

    private bool TryFindLanding(Room room, CharacterController capsule, out Vector3 landing)
    {
        landing = default;
        Vector3 approach = room.bounds.center + Vector3.back * RoomApproachDistance;
        var candidates = new List<Vector3Int>(room.floorTiles);
        candidates.Sort((a, b) =>
        {
            float da = (new Vector2(a.x + .5f, a.z + .5f) - new Vector2(approach.x, approach.z)).sqrMagnitude;
            float db = (new Vector2(b.x + .5f, b.z + .5f) - new Vector2(approach.x, approach.z)).sqrMagnitude;
            int distance = da.CompareTo(db);
            if (distance != 0) return distance;
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.z.CompareTo(b.z);
        });

        Vector3 scale = localPlayer.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfHeight = Mathf.Max(radius, capsule.height * Mathf.Abs(scale.y) * .5f);
        Vector3 centerOffset = localPlayer.TransformVector(capsule.center);
        foreach (Vector3Int tile in candidates)
        {
            if (room.blockedTiles.Contains(tile) || !dungeon.IsFloorTile(tile)) continue;
            Vector3 surface = dungeon.TileToWorldCenter(tile) + Vector3.up * FloorBlockHeight;
            // A short ray must hit the generated floor height; prop tops are not
            // eligible landing surfaces and triggers never count as solid ground.
            if (!Physics.Raycast(surface + Vector3.up * .15f, Vector3.down, out RaycastHit floor,
                .3f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < .9f
                || Mathf.Abs(floor.point.y - surface.y) > .1f) continue;

            Vector3 position = surface;
            position.y = floor.point.y + halfHeight - centerOffset.y + LandingClearance;
            Vector3 center = position + centerOffset;
            Vector3 bottom = center - Vector3.up * (halfHeight - radius);
            Vector3 top = center + Vector3.up * (halfHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + CapsuleClearance,
                overlaps, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = count >= overlaps.Length;
            for (int i = 0; i < count && !blocked; i++)
                if (overlaps[i] != null && !overlaps[i].transform.IsChildOf(localPlayer)) blocked = true;
            if (blocked) continue;
            landing = position;
            return true;
        }
        return false;
    }
}
