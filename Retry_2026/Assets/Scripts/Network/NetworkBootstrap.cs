using UnityEngine;

// ============================================================================
//  NetworkBootstrap
//  씬에 빈 GameObject 1개 + 본 컴포넌트만 붙이면 전체 시스템이 동작.
//
//  Inspector 설정:
//   - lobbyServerIP (기본 127.0.0.1)
//   - lobbyServerPort (기본 9000)
//   - localPlayerPrefab (본인 캐릭터 prefab)
//   - remotePlayerPrefab (다른 플레이어 prefab)
//   - monsterPrefab (몬스터 prefab)
//   - dungeonGenerator (씬에 있는 클라 측 던전 생성기 GameObject 참조)
//   - playerName (본인 이름)
// ============================================================================
public class NetworkBootstrap : MonoBehaviour
{
    [Header("Lobby Server")]
    [SerializeField] private string lobbyServerIP = "127.0.0.1";
    [SerializeField] private int lobbyServerPort = 9000;

    [Header("Prefabs")]
    [SerializeField] private GameObject localPlayerPrefab;
    [SerializeField] private GameObject remotePlayerPrefab;
    [SerializeField] private GameObject monsterPrefab;
    [SerializeField] private GameObject projectilePrefab;   // 활/총 투사체(직육면체). 비우면 기본 Cube 생성.

    [Header("Dungeon")]
    [Tooltip("클라 측 던전 생성기를 가진 GameObject. 비워두면 자동 탐색 시도.")]
    [SerializeField] private GameObject dungeonGeneratorObject;

    [Header("Player Settings")]
    [SerializeField] private string playerName = "Player";

    public LocalIdentity Identity { get; private set; }
    public LobbyClient Lobby { get; private set; }
    public SessionClient Session { get; private set; }
    public RemotePlayerRegistry RemotePlayers { get; private set; }
    public RemoteMonsterRegistry RemoteMonsters { get; private set; }
    public RemoteProjectileRegistry RemoteProjectiles { get; private set; }
    public DebugLobbyUI DebugUI { get; private set; }

    public GameObject LocalPlayer { get; private set; }
    public event System.Action<PacketType, byte[]> LobbyPacket; // LobbyUI가 구독하는 로비 패킷 이벤트

    // 탈출 관련 서버 통보. ExitPortal / DungeonExitManager가 구독.
    public event System.Action<ExtractionResult> ExtractionResultReceived;
    public event System.Action<PlayerExtracted> PlayerExtractedReceived;
    public event System.Action<SessionEnded> SessionEndedReceived;

    // 아이템/인벤토리 서버 통보. ServerInventoryBridge / 루팅 UI가 구독.
    public event System.Action<InventorySyncData> InventorySyncReceived;
    public event System.Action<ItemPickupResult> PickupResultReceived;
    public event System.Action<LootSpawnData> LootSpawnReceived;
    public event System.Action<LootRemovedData> LootRemovedReceived;

    public ServerInventoryBridge Inventory { get; private set; }
    public RemoteLootRegistry RemoteLoot { get; private set; }
    public DebugCheatKeys Cheats { get; private set; }   // [테스트 전용]

    /// <summary>[치트] 탈출 방으로 이동 요청. 실제 이동은 서버가 수행한다.</summary>
    public void RequestDebugTeleportExit()
    {
        if (Session == null || !Session.IsConnected) return;
        pendingSelfTeleport = true;    // 다음 본인 PLAYER_MOVE를 위치에 반영
        Session.SendDebugTeleportExit();
    }

    /// <summary>루팅 요청 송신. 거리/재고/여유는 서버가 검증한다.</summary>
    public void RequestItemPickup(int lootId, int itemHash, int count = 0)
    {
        if (Session == null || !Session.IsConnected) return;
        Session.SendItemPickup(lootId, itemHash, count);
    }

    /// <summary>탈출 요청 송신. 실제 성립 여부는 서버가 판정해 EXTRACTION_RESULT로 통보.</summary>
    public void RequestExtraction(int extractionPointId = 0)
    {
        if (Session == null || !Session.IsConnected) return;
        Session.SendExtractionRequest(extractionPointId);
    }

    private bool sessionConnectScheduled;
    private SessionAssignData pendingAssign;
    private bool firstSelfMoveApplied = false;     // 본인 시작 위치 한 번만 적용
    private bool pendingSelfTeleport = false;      // [치트] 서버 이동 통보 대기 중

    private void Awake()
    {
        Identity = EnsureChild<LocalIdentity>("LocalIdentity");
        DebugUI = EnsureChild<DebugLobbyUI>("DebugLobbyUI");
        RemotePlayers = EnsureChild<RemotePlayerRegistry>("RemotePlayerRegistry");
        RemoteMonsters = EnsureChild<RemoteMonsterRegistry>("RemoteMonsterRegistry");
        RemoteProjectiles = EnsureChild<RemoteProjectileRegistry>("RemoteProjectileRegistry");
        Inventory = EnsureChild<ServerInventoryBridge>("ServerInventoryBridge");
        RemoteLoot = EnsureChild<RemoteLootRegistry>("RemoteLootRegistry");
        RemoteLoot.Initialize(this);

        // [테스트 전용] F9 즉시 탈출. 배포 시 이 두 줄과 DebugCheatKeys.cs 삭제.
        Cheats = EnsureChild<DebugCheatKeys>("DebugCheatKeys");
        Cheats.Initialize(this);

        Identity.SetPlayerName(playerName);
        RemotePlayers.RemotePlayerPrefab = remotePlayerPrefab;
        RemoteMonsters.MonsterPrefab = monsterPrefab;
        RemoteProjectiles.ProjectilePrefab = projectilePrefab;
        RemoteProjectiles.LocalIdentity = Identity;

        Lobby = new LobbyClient();
        Session = new SessionClient();

        Lobby.OnPacketReceived = OnLobbyPacket;
        Session.OnPacketReceived = OnSessionPacket;
    }

    private void Start()
    {
        // 로그인은 타이틀 화면에서 닉네임 입력 후 BeginLogin() 호출로 진행
    }

    // 타이틀 화면의 "시작" 버튼이 호출. 연결 + 로그인.
    public bool BeginLogin(string nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            playerName = nickname;
            Identity.SetPlayerName(nickname);
        }
        if (!Lobby.IsConnected)
        {
            if (!Lobby.Connect(lobbyServerIP, lobbyServerPort)) return false;
            Identity.IsConnectedToLobby = true;
        }
        Lobby.SendLogin(playerName);
        return true;
    }

    private void Update()
    {
        Lobby?.Poll();
        Session?.Poll();

        if (sessionConnectScheduled)
        {
            sessionConnectScheduled = false;
            DoSessionConnect();
        }
    }

    private void OnDestroy()
    {
        Lobby?.Disconnect();
        Session?.Disconnect();
    }

    // ── 로비 패킷 처리 ───────────────────────────────────────────────

    private void OnLobbyPacket(PacketType type, byte[] body)
    {
        LobbyPacket?.Invoke(type, body);
        switch (type)
        {
            case PacketType.LOGIN_RESULT:
                {
                    var r = PacketIO.BytesToStruct<LoginResult>(body, 0);
                    if (r.success != 0)
                    {
                        Identity.SetLocalClientId(r.clientId);
                        Debug.Log($"[Lobby] 로그인 성공 cid={r.clientId}");
                    }
                    else
                    {
                        Debug.LogError($"[Lobby] 로그인 실패: {r.failReason}");
                    }
                    break;
                }
            case PacketType.ROOM_CREATE_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomCreateResult>(body, 0);
                    Debug.Log($"[Lobby] 방 생성 응답: success={r.success} roomId={r.roomId}");
                    DebugUI?.OnRoomResult(r.success != 0, r.roomId);
                    break;
                }
            case PacketType.ROOM_JOIN_RESULT:
                {
                    var r = PacketIO.BytesToStruct<RoomJoinResult>(body, 0);
                    Debug.Log($"[Lobby] 방 참가 응답: success={r.success} roomId={r.roomId}");
                    DebugUI?.OnRoomResult(r.success != 0, r.roomId);
                    break;
                }
            case PacketType.SESSION_ASSIGN:
                {
                    pendingAssign = PacketIO.BytesToStruct<SessionAssignData>(body, 0);
                    sessionConnectScheduled = true;
                    Debug.Log($"<color=cyan>[Lobby] SESSION_ASSIGN: sid={pendingAssign.sessionId} seed={pendingAssign.mapSeed}</color>");
                    break;
                }
        }
    }

    private void DoSessionConnect()
    {
        Lobby.Disconnect();
        Identity.IsConnectedToLobby = false;

        // 1. 클라 측 던전 생성 (서버와 같은 시드로!)
        GenerateClientDungeon(pendingAssign.mapSeed);

        // 2. 로컬 플레이어 spawn
        SpawnLocalPlayer();

        // 3. 세션 연결 + 인증
        if (Session.ConnectAndAuth(
            lobbyServerIP,
            pendingAssign.sessionServerPort,
            pendingAssign.sessionId,
            Identity.LocalClientId))
        {
            Identity.IsConnectedToSession = true;
            AttachLocalPlayerSenders();
        }
    }

    /// <summary>
    /// 클라 측 던전 생성기에 서버 mapSeed 전달.
    /// 추가로:
    ///  - randomizeSeedOnGenerate를 false로 강제 (켜져 있으면 자체 시드로 덮어씀)
    ///  - generateOnStart도 false로 (혹시라도 다시 호출되는 것 방지)
    ///  - MonsterSpawner 컴포넌트 비활성화 (서버가 몬스터 관장하므로 중복 방지)
    /// </summary>
    private void GenerateClientDungeon(int seed)
    {
        var target = dungeonGeneratorObject;
        if (target == null)
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb.GetType().Name.Contains("DungeonGenerator"))
                {
                    target = mb.gameObject;
                    break;
                }
            }
        }
        if (target == null)
        {
            Debug.LogError("[Bootstrap] 던전 생성기를 찾지 못함!");
            return;
        }

        // ─── MonsterSpawner 비활성화 (서버가 몬스터 처리) ───
        var spawner = target.GetComponent("MonsterSpawner") as MonoBehaviour;
        if (spawner != null)
        {
            spawner.enabled = false;
            Debug.Log("[Bootstrap] MonsterSpawner 비활성화됨");
        }

        var dgComponents = target.GetComponents<MonoBehaviour>();
        foreach (var c in dgComponents)
        {
            var t = c.GetType();
            if (!t.Name.Contains("DungeonGenerator")) continue;

            var BF = System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic;

            // ─── randomizeSeedOnGenerate를 false로 강제 ───
            // (인스펙터 체크돼 있어도 우리가 보낸 seed가 살아남도록)
            var randomizeField = t.GetField("randomizeSeedOnGenerate", BF);
            if (randomizeField != null && randomizeField.FieldType == typeof(bool))
            {
                randomizeField.SetValue(c, false);
                Debug.Log("[Bootstrap] randomizeSeedOnGenerate=false 강제");
            }

            // ─── generateOnStart도 false (다시 호출 방지) ───
            var genOnStartField = t.GetField("generateOnStart", BF);
            if (genOnStartField != null && genOnStartField.FieldType == typeof(bool))
            {
                genOnStartField.SetValue(c, false);
            }

            // ─── seed 필드 설정 ───
            var seedField = t.GetField("seed", BF);
            if (seedField == null) seedField = t.GetField("mapSeed", BF);
            if (seedField != null && seedField.FieldType == typeof(int))
            {
                seedField.SetValue(c, seed);
                Debug.Log($"[Bootstrap] 던전 시드 설정: {seed}");
            }

            // ─── Generate() 호출 ───
            var methodNames = new[] { "Generate", "GenerateDungeon", "Build", "BuildDungeon", "Regenerate" };
            foreach (var mname in methodNames)
            {
                var m = t.GetMethod(mname,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(c, null);
                    Debug.Log($"<color=yellow>[Bootstrap] 던전 생성 호출: {t.Name}.{mname}() seed={seed}</color>");
                    return;
                }
            }
            Debug.LogWarning($"[Bootstrap] {t.Name}에 Generate() 류 메서드 없음.");
            return;
        }
        Debug.LogError($"[Bootstrap] {target.name}에 DungeonGenerator 류 컴포넌트 없음.");
    }

    private void SpawnLocalPlayer()
    {
        if (localPlayerPrefab == null)
        {
            Debug.LogError("[Bootstrap] localPlayerPrefab이 비어있음!");
            return;
        }
        // 임시 spawn 위치: 던전 위쪽으로 멀리 (y=100). 첫 PLAYER_MOVE 받으면 즉시 시작방으로 텔레포트.
        LocalPlayer = Instantiate(localPlayerPrefab, new Vector3(0f, 100f, 0f), Quaternion.identity);
        LocalPlayer.name = $"LocalPlayer_{Identity.LocalClientId}";

        // ── 추락 방지: 첫 PLAYER_MOVE 받기 전엔 중력 비활성 ──
        var cc = LocalPlayer.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        var pm = LocalPlayer.GetComponent("Player_Movement") as MonoBehaviour;
        if (pm != null) pm.enabled = false;

        Debug.Log($"[Bootstrap] 본인 캐릭터 임시 spawn (시작 위치 대기 중...)");
    }

    private void AttachLocalPlayerSenders()
    {
        if (LocalPlayer == null) return;

        var inputSender = LocalPlayer.GetComponent<LocalPlayerInputSender>();
        if (inputSender == null) inputSender = LocalPlayer.AddComponent<LocalPlayerInputSender>();
        inputSender.Initialize(this);

        var attackSender = LocalPlayer.GetComponent<LocalPlayerAttackSender>();
        if (attackSender == null) attackSender = LocalPlayer.AddComponent<LocalPlayerAttackSender>();
        attackSender.Initialize(this);

        // 서버 권위 인벤토리 연결. 이후 로컬 인벤토리는 표시용 사본이 된다.
        var playerInv = LocalPlayer.GetComponent<PlayerInventory>();
        if (playerInv == null) playerInv = LocalPlayer.GetComponentInChildren<PlayerInventory>();
        if (Inventory != null) Inventory.Initialize(this, playerInv);
    }

    // ── 세션 패킷 처리 ───────────────────────────────────────────────

    private void OnSessionPacket(PacketType type, byte[] body)
    {
        switch (type)
        {
            case PacketType.PLAYER_ENTER_VIEW:
                {
                    var p = PacketIO.BytesToStruct<PlayerEnterView>(body, 0);
                    if (p.clientId == Identity.LocalClientId) break;
                    RemotePlayers.OnEnterView(p);
                    break;
                }
            case PacketType.PLAYER_LEAVE_VIEW:
                {
                    var p = PacketIO.BytesToStruct<PlayerLeaveView>(body, 0);
                    RemotePlayers.OnLeaveView(p.clientId);
                    break;
                }
            case PacketType.PLAYER_MOVE:
                {
                    var p = PacketIO.BytesToStruct<PlayerMove>(body, 0);
                    if (p.clientId == Identity.LocalClientId)
                    {
                        // ── 보완2: 본인 첫 PLAYER_MOVE → 시작 위치 적용 ──
                        // [치트] 서버가 옮긴 경우: 시작 위치와 무관하게 다시 스냅
                        if (pendingSelfTeleport && firstSelfMoveApplied && LocalPlayer != null)
                        {
                            pendingSelfTeleport = false;
                            ApplySelfPosition(p);
                            Debug.Log($"<color=yellow>[치트] 탈출 방 근처로 이동: ({p.posX:F1}, {p.posY:F1}, {p.posZ:F1})</color>");
                            break;
                        }

                        if (!firstSelfMoveApplied && LocalPlayer != null)
                        {
                            // 위치 텔레포트 (CharacterController 비활성 상태여야 즉시 적용됨)
                            LocalPlayer.transform.position = new Vector3(p.posX, p.posY, p.posZ);
                            LocalPlayer.transform.eulerAngles = new Vector3(0f, p.rotY, 0f);
                            firstSelfMoveApplied = true;

                            // 이제 정상 동작 컴포넌트들 재활성화
                            var cc = LocalPlayer.GetComponent<CharacterController>();
                            if (cc != null) cc.enabled = true;
                            var pmv = LocalPlayer.GetComponent("Player_Movement") as MonoBehaviour;
                            if (pmv != null) pmv.enabled = true;

                            Debug.Log($"<color=lime>[Bootstrap] 시작 위치 적용: ({p.posX:F1}, {p.posY:F1}, {p.posZ:F1}) rotY={p.rotY:F1}</color>");
                        }
                        break;
                    }
                    RemotePlayers.OnMove(p);
                    break;
                }
            case PacketType.MONSTER_ENTER_VIEW:
                {
                    var m = PacketIO.BytesToStruct<MonsterEnterView>(body, 0);
                    RemoteMonsters.OnEnterView(m);
                    break;
                }
            case PacketType.MONSTER_LEAVE_VIEW:
                {
                    var m = PacketIO.BytesToStruct<MonsterLeaveView>(body, 0);
                    RemoteMonsters.OnLeaveView(m.monsterId);
                    break;
                }
            case PacketType.MONSTER_MOVE:
                {
                    var m = PacketIO.BytesToStruct<MonsterMove>(body, 0);
                    RemoteMonsters.OnMove(m);
                    break;
                }
            case PacketType.MONSTER_ATTACK_EVENT:
                {
                    var ev = PacketIO.BytesToStruct<MonsterAttackEvent>(body, 0);
                    if (ev.damage <= 0)
                    {
                        // 공격 시작(준비동작) → 애니메이션만 재생
                        RemoteMonsters.OnMonsterAttack(ev);
                    }
                    else
                    {
                        // 타격 확정(준비동작 후) → 데미지만 반영, 애니 재생 안 함
                        if (ev.victimClientId == Identity.LocalClientId)
                            ApplyHpToLocalPlayer(ev.victimHpAfter);
                    }
                    break;
                }
            case PacketType.MONSTER_DIED:
                {
                    var d = PacketIO.BytesToStruct<MonsterDied>(body, 0);
                    RemoteMonsters.OnDied(d.monsterId);
                    break;
                }
            case PacketType.PLAYER_DIED:
                {
                    var d = PacketIO.BytesToStruct<PlayerDied>(body, 0);
                    Debug.Log($"[Session] 사망: victim={d.victimId} killer={d.killerId}");
                    // 본인 사망 시 처리(추후: 사망 UI, 리스폰)
                    if (d.victimId == Identity.LocalClientId)
                    {
                        Debug.Log("<color=red>[Session] 본인 사망!</color>");
                    }
                    else
                    {
                        RemotePlayers.OnPlayerDied(d.victimId);
                    }
                    break;
                }
            case PacketType.HP_CHANGED:
                {
                    var h = PacketIO.BytesToStruct<HpChanged>(body, 0);
                    if (h.targetId == Identity.LocalClientId) ApplyHpToLocalPlayer(h.hp);
                    break;
                }
            case PacketType.PLAYER_ATTACK_BROADCAST:
                {
                    // 다른 플레이어의 공격 액션. 본인은 본 패킷 받지 않음 (서버가 exceptClientId=attackerId).
                    var pab = PacketIO.BytesToStruct<PlayerAttackBroadcast>(body, 0);
                    RemotePlayers.OnAttackBroadcast(pab);
                    break;
                }
            case PacketType.COMBAT_EVENT:
                {
                    // 공격 명중 + 데미지. HP 변화 알림.
                    var ce = PacketIO.BytesToStruct<CombatEvent>(body, 0);
                    // 타겟이 본인이면 본인 HP 갱신
                    if (ce.targetId == Identity.LocalClientId)
                    {
                        ApplyHpToLocalPlayer(ce.targetHpAfter);
                    }
                    else if (ce.targetId > 0)
                    {
                        // 다른 플레이어 HP 갱신
                        RemotePlayers.OnHpChanged(ce.targetId, ce.targetHpAfter);
                    }
                    else if (ce.targetId < 0)
                    {
                        // 몬스터 HP 갱신
                        RemoteMonsters.OnHpChanged(-ce.targetId, ce.targetHpAfter);
                    }
                    break;
                }
            case PacketType.PROJECTILE_SPAWN:
                {
                    // 활/총 투사체 생성. 클라는 직육면체를 월드에 띄운다.
                    var sp = PacketIO.BytesToStruct<ProjectileSpawn>(body, 0);
                    RemoteProjectiles.OnSpawn(sp);
                    break;
                }
            case PacketType.PROJECTILE_MOVE:
                {
                    var mv = PacketIO.BytesToStruct<ProjectileMove>(body, 0);
                    RemoteProjectiles.OnMove(mv);
                    break;
                }
            case PacketType.PROJECTILE_DESPAWN:
                {
                    // 벽/대상 명중 또는 수명. 직육면체 제거 후 미쿠 손에 초기화는
                    // 발사자 본인 클라에서 처리(ownerId 비교).
                    var dp = PacketIO.BytesToStruct<ProjectileDespawn>(body, 0);
                    RemoteProjectiles.OnDespawn(dp);
                    break;
                }
            case PacketType.EXTRACTION_RESULT:
                {
                    // 서버 탈출 판정 결과. 성공해야만 실제 탈출 처리.
                    var r = PacketIO.BytesToStruct<ExtractionResult>(body, 0);
                    ExtractionResultReceived?.Invoke(r);
                    if (r.success == 1)
                        Debug.Log($"<color=lime>[Extract] 탈출 성공 (체류 {r.heldSec:F1}s, 아이템 {r.itemCount})</color>");
                    else
                        Debug.LogWarning($"[Extract] 탈출 거부: {(ExtractionFailReason)r.failReason} (체류 {r.heldSec:F1}s)");
                    break;
                }
            case PacketType.PLAYER_EXTRACTED:
                {
                    // 다른 플레이어 탈출. 해당 캐릭터는 시야에서도 제거된다.
                    var e = PacketIO.BytesToStruct<PlayerExtracted>(body, 0);
                    RemotePlayers.OnLeaveView(e.clientId);
                    PlayerExtractedReceived?.Invoke(e);
                    Debug.Log($"[Extract] cid={e.clientId} 탈출 (남은 인원 {e.remainingPlayers})");
                    break;
                }
            case PacketType.INVENTORY_SYNC:
                {
                    // 서버 권위 인벤토리. 로컬 상태를 이걸로 덮어쓴다.
                    var inv = PacketIO.BytesToStruct<InventorySyncData>(body, 0);
                    InventorySyncReceived?.Invoke(inv);
                    break;
                }
            case PacketType.ITEM_PICKUP_RESULT:
                {
                    var r = PacketIO.BytesToStruct<ItemPickupResult>(body, 0);
                    PickupResultReceived?.Invoke(r);
                    break;
                }
            case PacketType.LOOT_SPAWN:
                {
                    var sp = PacketIO.BytesToStruct<LootSpawnData>(body, 0);
                    LootSpawnReceived?.Invoke(sp);
                    break;
                }
            case PacketType.LOOT_REMOVED:
                {
                    var rm = PacketIO.BytesToStruct<LootRemovedData>(body, 0);
                    LootRemovedReceived?.Invoke(rm);
                    break;
                }
            case PacketType.SESSION_ENDED:
                {
                    var s = PacketIO.BytesToStruct<SessionEnded>(body, 0);
                    SessionEndedReceived?.Invoke(s);
                    Debug.Log($"[Session] 세션 종료 reason={s.reason}");
                    break;
                }
        }
    }

    // 본인 캐릭터를 서버 좌표로 스냅.
    // CharacterController가 켜져 있으면 transform 직접 대입이 무시되므로 잠깐 끈다.
    private void ApplySelfPosition(PlayerMove p)
    {
        if (LocalPlayer == null) return;

        var cc = LocalPlayer.GetComponent<CharacterController>();
        bool wasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;

        LocalPlayer.transform.position = new Vector3(p.posX, p.posY, p.posZ);

        if (cc != null) cc.enabled = wasEnabled;
    }

    private void ApplyHpToLocalPlayer(int newHp)
    {
        if (LocalPlayer == null) return;
        // backing field 직접 설정 대신 정상 경로 → HP바 갱신 + 피격 애니 + 사망 시 IsDead 설정
        var ps = LocalPlayer.GetComponent<Player_State>();
        if (ps != null) ps.NetworkApplyHp(newHp);
    }

    private T EnsureChild<T>(string name) where T : Component
    {
        var existing = GetComponentInChildren<T>(true);
        if (existing != null) return existing;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.AddComponent<T>();
    }
}