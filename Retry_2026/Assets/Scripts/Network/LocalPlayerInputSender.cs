using UnityEngine;

using System.Collections.Generic;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct PositionSyncState {
    public int version,serial;
    public long inputTimestamp;
    public float x,y,z;
    public int teleport;
}

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
    public const int PositionSyncPacket=204;
    readonly Dictionary<long,Vector3> sentPositions=new();
    readonly Queue<long> sentOrder=new();
    long lastSentTimestamp;
    int lastSentFrame=-1;
    int lastPositionSerial;
    public void ResetPrediction(){sentPositions.Clear();sentOrder.Clear();}
    public void SendNow(){
        if(bootstrap==null||bootstrap.Session==null||!bootstrap.Session.IsConnected||bootstrap.IsAwaitingDebugTeleport)return;
        if(lastSentFrame==Time.frameCount)return;
        sendTimer=0;SendInput();
    }
    public void ReceivePosition(byte[] bytes){
        if(bytes==null||bytes.Length!=32||bootstrap==null)return;
        var state=PacketIO.BytesToStruct<PositionSyncState>(bytes,0);
        if(state.version!=1||state.serial<=lastPositionSerial||state.teleport<0||state.teleport>1||!float.IsFinite(state.x)||!float.IsFinite(state.y)||!float.IsFinite(state.z))return;
        lastPositionSerial=state.serial;
        if(bootstrap.IsAwaitingDebugTeleport&&state.teleport==0)return;
        var server=new Vector3(state.x,state.y,state.z);
        Vector3 remaining=Vector3.zero;
        if(state.teleport==0){
            if(!sentPositions.TryGetValue(state.inputTimestamp,out var sent))return;
            remaining=transform.position-sent;
        }else{
            ResetPrediction();
            GetComponent<PlayerDefenseController>()?.CancelDefense();
            GetComponent<Player_Attack>()?.CancelCurrentAttack();
            GetComponent<Player_LockOnSystem>()?.ClearLockOn();
            GetComponent<Player_Movement>()?.ResetVerticalMotion();
            bootstrap.CompleteAuthoritativeTeleport();
        }
        var oldPosition=transform.position;
        var capsule=GetComponent<CharacterController>();bool enabledBefore=capsule!=null&&capsule.enabled;
        if(capsule!=null)capsule.enabled=false;
        transform.position=server;
        if(capsule!=null)capsule.enabled=enabledBefore;
        // Replay only displacement produced after the acknowledged input, through local collision.
        if(enabledBefore&&state.teleport==0)capsule.Move(remaining);
        else if(state.teleport==0)transform.position+=remaining;
        var shift=transform.position-oldPosition;
        var keys=new List<long>(sentPositions.Keys);
        foreach(long key in keys){if(key<=state.inputTimestamp)sentPositions.Remove(key);else sentPositions[key]+=shift;}
        Physics.SyncTransforms();
    }
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
        // Do not send the pre-teleport location back while the server relocates this actor.
        if (bootstrap.IsAwaitingDebugTeleport) { sendTimer = 0f; return; }

        sendTimer += Time.deltaTime;
        if (sendTimer >= SEND_INTERVAL)
        {
            sendTimer = 0f;
            SendInput();
        }
    }

    private void SendInput()
    {
        if(input==null)return;
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
            timestamp = System.Math.Max(System.DateTime.UtcNow.Ticks,lastSentTimestamp+1),
            posX = pos.x,
            posY = pos.y,
            posZ = pos.z,
            rotY = yaw,
        };

        lastSentFrame=Time.frameCount;lastSentTimestamp=packet.timestamp;sentPositions[packet.timestamp]=pos;sentOrder.Enqueue(packet.timestamp);
        while(sentOrder.Count>128)sentPositions.Remove(sentOrder.Dequeue());
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
