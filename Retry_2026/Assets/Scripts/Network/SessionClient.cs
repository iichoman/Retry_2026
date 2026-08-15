using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

// ============================================================================
//  SessionClient
//  세션 서버(SESSION_ASSIGN으로 받은 ip:port)와의 TCP 통신.
//
//  연결 직후 처리:
//   - 8바이트 raw 인증 데이터 (sessionId 4 + clientId 4) 송신
//   - 인증은 서버가 묵시적으로 처리. 응답 없음.
//   - 이후 표준 패킷 송수신.
// ============================================================================
public class SessionClient
{
    private TcpClient tcp;
    private NetworkStream stream;
    private byte[] assembly = new byte[16 * 1024 * 1024];
    private int assemblyUsed = 0;

    public Action<PacketType, byte[]> OnPacketReceived;
    public Action OnConnected;
    public Action OnDisconnected;

    public bool IsConnected => tcp != null && tcp.Connected;

    /// <summary>연결 + 8바이트 raw 인증 송신 (sessionId, clientId).</summary>
    public bool ConnectAndAuth(string ip, int port, int sessionId, int clientId)
    {
        try
        {
            tcp = new TcpClient();
            tcp.NoDelay = true;
            tcp.Connect(ip, port);
            stream = tcp.GetStream();
            assemblyUsed = 0;

            // 8바이트 raw 인증
            byte[] authBuf = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(sessionId), 0, authBuf, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(clientId), 0, authBuf, 4, 4);
            stream.Write(authBuf, 0, 8);

            Debug.Log($"<color=cyan>[Session] 연결+인증 성공 {ip}:{port} sid={sessionId} cid={clientId}</color>");
            OnConnected?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Session] 연결 실패: {e.Message}");
            tcp = null;
            stream = null;
            return false;
        }
    }

    public void Disconnect()
    {
        if (tcp == null) return;
        try { stream?.Close(); tcp.Close(); } catch { }
        tcp = null;
        stream = null;
        OnDisconnected?.Invoke();
        Debug.Log("[Session] 연결 종료");
    }

    public void Poll()
    {
        if (stream == null) return;
        try
        {
            while (stream.DataAvailable)
            {
                int free = assembly.Length - assemblyUsed;
                if (free <= 0)
                {
                    Debug.LogError("[Session] 어셈블리 버퍼 가득.");
                    Disconnect();
                    return;
                }
                int n = stream.Read(assembly, assemblyUsed, free);
                if (n <= 0) break;
                assemblyUsed += n;
            }

            int headerSize = Marshal.SizeOf<PacketHeader>();
            while (assemblyUsed >= headerSize)
            {
                var header = PacketIO.BytesToStruct<PacketHeader>(assembly, 0);
                if (header.size < 0 || header.size > assembly.Length - headerSize)
                {
                    Debug.LogError($"[Session] 비정상 패킷 size={header.size}");
                    Disconnect();
                    return;
                }
                int total = headerSize + header.size;
                if (assemblyUsed < total) break;

                byte[] body = new byte[header.size];
                Buffer.BlockCopy(assembly, headerSize, body, 0, header.size);

                try { OnPacketReceived?.Invoke(header.type, body); }
                catch (Exception ex) { Debug.LogError($"[Session] 핸들러 예외: {ex}"); }

                Buffer.BlockCopy(assembly, total, assembly, 0, assemblyUsed - total);
                assemblyUsed -= total;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Session] Poll 오류: {e.Message}");
            Disconnect();
        }
    }

    // ── 송신 ────────────────────────────────────────────────────────────

    public void SendPlayerInput(PlayerInput input)
    {
        SendStruct(PacketType.PLAYER_INPUT, input);
    }

    public void SendPlayerAttack(PlayerAttackRequest req)
    {
        SendStruct(PacketType.PLAYER_ATTACK_REQUEST, req);
    }

    /// <summary>탈출 요청 송신. 성립 여부는 서버가 판정한다.</summary>
    public void SendExtractionRequest(int extractionPointId)
    {
        var req = new ExtractionRequest { extractionPointId = extractionPointId };
        SendStruct(PacketType.EXTRACTION_REQUEST, req);
    }

    /// <summary>루팅 요청 송신. count 0 이하 = 전량 요청.</summary>
    public void SendItemPickup(int lootId, int itemHash, int count)
    {
        var req = new ItemPickupRequest { lootId = lootId, itemHash = itemHash, count = count };
        SendStruct(PacketType.ITEM_PICKUP_REQUEST, req);
    }

    /// <summary>[치트] 탈출 방 이동 요청. 본문 없음. 배포 시 제거.</summary>
    public void SendDebugTeleportExit()
    {
        SendBytes(PacketIO.MakeEmptyPacket(PacketType.DEBUG_TELEPORT_EXIT));
    }

    private void SendStruct<T>(PacketType type, T body) where T : struct
    {
        SendBytes(PacketIO.MakePacket(type, body));
    }

    private void SendBytes(byte[] data)
    {
        if (stream == null) return;
        try { stream.Write(data, 0, data.Length); }
        catch (Exception e)
        {
            Debug.LogError($"[Session] 송신 오류: {e.Message}");
            Disconnect();
        }
    }
}