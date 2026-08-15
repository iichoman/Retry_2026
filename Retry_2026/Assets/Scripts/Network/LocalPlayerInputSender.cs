using UnityEngine;

// ============================================================================
//  LocalPlayerInputSender
//
//  본인 캐릭터에 자동으로 붙음 (NetworkBootstrap이 AddComponent).
//  50ms마다 PLAYER_INPUT 패킷을 SessionClient로 송신.
//
//  Phase 1 (현재): 클라가 결정한 본인 위치 (transform.position)를
//                  서버가 그대로 신뢰. CharacterController로 이동/충돌 처리.
//
//  주의: 본 컴포넌트는 본인 캐릭터(LocalPlayer)에만 붙음.
//        RemotePlayer엔 붙이지 말 것.
// ============================================================================
[RequireComponent(typeof(Defalult_Input))]
public class LocalPlayerInputSender : MonoBehaviour
{
    private NetworkBootstrap bootstrap;
    private Defalult_Input input;

    private float sendTimer = 0f;
    private const float SEND_INTERVAL = 0.05f;       // 50ms = 20Hz

    public int SendCount { get; private set; }
    private int debugLogCount = 0;

    public void Initialize(NetworkBootstrap bs)
    {
        bootstrap = bs;
    }

    private void Awake()
    {
        input = GetComponent<Defalult_Input>();
    }

    private void Update()
    {
        if (bootstrap == null) return;
        if (bootstrap.Session == null || !bootstrap.Session.IsConnected) return;

        sendTimer += Time.deltaTime;
        if (sendTimer >= SEND_INTERVAL)
        {
            sendTimer = 0f;
            SendInput();
        }
    }

    private void SendInput()
    {
        Vector2 move = input.Move;
        Vector3 pos = transform.position;
        float yaw = transform.eulerAngles.y;

        var packet = new PlayerInput
        {
            clientId = bootstrap.Identity.LocalClientId,
            moveX = move.x,
            moveY = move.y,
            jump = input.Jump ? 1 : 0,
            sprint = input.Sprint ? 1 : 0,
            timestamp = System.DateTime.UtcNow.Ticks,
            posX = pos.x,
            posY = pos.y,
            posZ = pos.z,
            rotY = yaw,
        };

        bootstrap.Session.SendPlayerInput(packet);
        SendCount++;

        if (debugLogCount < 5)
        {
            Debug.Log($"[Send] PLAYER_INPUT #{SendCount}: move=({move.x:F2},{move.y:F2}) pos={pos} yaw={yaw:F1}");
            debugLogCount++;
        }
        else if (move.sqrMagnitude > 0.01f && SendCount % 40 == 0)
        {
            Debug.Log($"[Send] PLAYER_INPUT #{SendCount}: pos={pos}");
        }
    }
}