using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

// ============================================================================
//  LobbyClient
//  로비 서버(127.0.0.1:9000)와의 TCP 통신만 담당.
//
//  사용:
//    var c = new LobbyClient();
//    c.OnPacketReceived = (type, body) => { ... };
//    c.Connect("127.0.0.1", 9000);
//    c.SendLogin("MyName");
//    매 Update에서 c.Poll() 호출.
// ============================================================================
public class LobbyClient
{
    private TcpClient tcp;
    private NetworkStream stream;
    private byte[] assembly = new byte[8192];
    private int assemblyUsed = 0;

    /// <summary>(packetType, body) 콜백. body는 헤더 제외 본문만.</summary>
    public Action<PacketType, byte[]> OnPacketReceived;
    public Action OnConnected;
    public Action OnDisconnected;

    public bool IsConnected => tcp != null && tcp.Connected;

    public bool Connect(string ip, int port)
    {
        try
        {
            tcp = new TcpClient();
            tcp.NoDelay = true;
            tcp.Connect(ip, port);
            stream = tcp.GetStream();
            assemblyUsed = 0;
            Debug.Log($"<color=green>[Lobby] 연결 성공 {ip}:{port}</color>");
            OnConnected?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] 연결 실패: {e.Message}");
            tcp = null;
            stream = null;
            return false;
        }
    }

    public void Disconnect()
    {
        if (tcp == null) return;
        try { stream?.Close(); tcp.Close(); }
        catch { }
        tcp = null;
        stream = null;
        OnDisconnected?.Invoke();
        Debug.Log("[Lobby] 연결 종료");
    }

    /// <summary>매 프레임 호출. 가용한 데이터를 패킷 단위로 잘라 콜백.</summary>
    public void Poll()
    {
        if (stream == null) return;

        try
        {
            // 가용한 만큼 읽기
            while (stream.DataAvailable)
            {
                int free = assembly.Length - assemblyUsed;
                if (free <= 0)
                {
                    Debug.LogError("[Lobby] 어셈블리 버퍼 가득. 연결 종료.");
                    Disconnect();
                    return;
                }
                int n = stream.Read(assembly, assemblyUsed, free);
                if (n <= 0) break;
                assemblyUsed += n;
            }

            // 패킷 추출
            int headerSize = Marshal.SizeOf<PacketHeader>();
            while (assemblyUsed >= headerSize)
            {
                var header = PacketIO.BytesToStruct<PacketHeader>(assembly, 0);
                if (header.size < 0 || header.size > assembly.Length - headerSize)
                {
                    Debug.LogError($"[Lobby] 비정상 패킷 size={header.size}");
                    Disconnect();
                    return;
                }
                int total = headerSize + header.size;
                if (assemblyUsed < total) break;

                byte[] body = new byte[header.size];
                Buffer.BlockCopy(assembly, headerSize, body, 0, header.size);

                try { OnPacketReceived?.Invoke(header.type, body); }
                catch (Exception ex) { Debug.LogError($"[Lobby] 패킷 핸들러 예외: {ex}"); }

                Buffer.BlockCopy(assembly, total, assembly, 0, assemblyUsed - total);
                assemblyUsed -= total;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Lobby] Poll 오류: {e.Message}");
            Disconnect();
        }
    }

    // ── 송신 헬퍼 ────────────────────────────────────────────────────────

    public void SendLogin(string playerName)
    {
        var req = new LoginRequest { playerName = NameCodec.Encode(playerName ?? "Player", 32) };
        SendStruct(PacketType.LOGIN_REQUEST, req);
    }

    public void SendRoomCreate(string roomName)
    {
        var req = new RoomCreateRequest { roomName = NameCodec.Encode(roomName ?? "Room", 32) };
        SendStruct(PacketType.ROOM_CREATE_REQUEST, req);
    }

    public void SendRoomJoin(int roomId)
    {
        var req = new RoomJoinRequest { roomId = roomId };
        SendStruct(PacketType.ROOM_JOIN_REQUEST, req);
    }

    public void SendSelectTeam(int teamId)
    {
        var req = new RoomSelectTeamRequest { teamId = teamId };
        SendStruct((PacketType)LobbyPacketType.ROOM_SELECT_TEAM_REQUEST, req);
    }

    public void SendRoomLeave()
    {
        var req = new RoomSelectTeamRequest { teamId = 0 };   // 본문은 서버가 무시(연결로 식별)
        SendStruct((PacketType)LobbyPacketType.ROOM_LEAVE_REQUEST, req);
    }

    public void SendRoomList()
    {
        SendBytes(PacketIO.MakeEmptyPacket(PacketType.ROOM_LIST_REQUEST));
    }

    public void SendGameStart()
    {
        SendBytes(PacketIO.MakeEmptyPacket(PacketType.GAME_START_REQUEST));
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
            Debug.LogError($"[Lobby] 송신 오류: {e.Message}");
            Disconnect();
        }
    }
}