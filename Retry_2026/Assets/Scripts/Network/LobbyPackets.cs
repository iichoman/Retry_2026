using System;
using System.Runtime.InteropServices;

// ============================================================================
//  LobbyPackets.cs
//  서버 Common/PacketProtocol.h 에 대응하는 "추가" 패킷 정의 (클라 측).
//  기존 PacketProtocol.cs 의 PacketType enum 은 건드리지 않고,
//  새 패킷 타입은 여기 정수 상수로 정의 후 (PacketType) 캐스팅해서 사용한다.
//  (enum 값은 서버와 동일: 11~14)
//
//  필드 순서/타입은 서버와 반드시 동일해야 함.
// ============================================================================

// 신규 로비 패킷 타입 (서버 enum 11~14와 동일)
public static class LobbyPacketType
{
    public const int ROOM_LEAVE_REQUEST = 11;  // C→S: 파티 나가기
    public const int ROOM_LEAVE_RESULT = 12;  // S→C: 나가기 결과(본인)
    public const int ROOM_SELECT_TEAM_REQUEST = 13;  // C→S: 팀 슬롯 선택
    public const int ROOM_STATE = 14;  // S→C: 방 멤버/팀 현황 push
}

// 로비 상수 (서버와 동일)
public static class LobbyConst
{
    public const int MAX_TEAMS = 10;   // 5x2 그리드
    public const int TEAM_CAPACITY = 3;    // 한 팀 3명
    public const int TEAM_UNASSIGNED = -1;
    public const int MAX_SESSION_PLAYERS = 30;
    public const int MAX_PLAYER_NAME = 32;
}

// ── 기존에 누락돼 있던 방 목록 구조체 (ROOM_LIST_RESULT 역직렬화용) ──
//    server: RoomListEntry { int roomId, hostClientId, currentPlayers, maxPlayers; char roomName[32]; }
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomListEntry
{
    public int roomId;
    public int hostClientId;
    public int currentPlayers;
    public int maxPlayers;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]   // MAX_ROOM_NAME (UTF-8)
    public byte[] roomName;
}

//    server: RoomListResult { int count; RoomListEntry rooms[50]; }
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomListResult
{
    public int count;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]   // MAX_ROOM_LIST
    public RoomListEntry[] rooms;
}

// ── 신규: 팀 선택 / 나가기 / 방 현황 ─────────────────────────────────────

// C→S: 팀 슬롯 선택 (0..MAX_TEAMS-1, 또는 -1=미배정)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomSelectTeamRequest
{
    public int teamId;
}

// S→C: 나가기 결과 (나간 본인에게만)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomLeaveResult
{
    public int success;
}

// 방 멤버 1명 (ROOM_STATE 항목). server RoomMemberEntry 와 동일.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomMemberEntry
{
    public int clientId;
    public int teamId;       // -1=미배정, 0..MAX_TEAMS-1
    public int isHost;       // 1=방장
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]   // MAX_PLAYER_NAME (UTF-8)
    public byte[] playerName;
}

// S→C: 방(파티) 전체 현황. server RoomStateData 와 동일.
//   count(4) + 30 * (4+4+4+32=44) = 4 + 1320 = 1324... → 헤더 정렬 주의:
//   실제로는 roomId/hostClientId/memberCount(12) + 30*44(1320) = 1332 bytes
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RoomStateData
{
    public int roomId;
    public int hostClientId;
    public int memberCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]   // MAX_SESSION_PLAYERS
    public RoomMemberEntry[] members;
}

// 닉네임/방이름 인코딩 헬퍼.
// 패킷의 이름 필드는 고정 길이 UTF-8 바이트 배열(널 패딩).
// ByValTStr(ANSI)은 OS 코드페이지에 따라 한글이 깨지므로 UTF-8로 통일.
public static class NameCodec
{
    // 문자열 → 고정 길이 UTF-8 바이트 배열. 초과 시 UTF-8 문자 경계에서 안전하게 자름.
    public static byte[] Encode(string s, int size)
    {
        var buf = new byte[size];
        if (string.IsNullOrEmpty(s)) return buf;
        var utf8 = System.Text.Encoding.UTF8.GetBytes(s);
        int n = System.Math.Min(utf8.Length, size - 1);   // 널 종료용 1바이트 예약
        // 멀티바이트 문자가 잘리지 않도록 continuation byte(10xxxxxx)면 경계까지 뒤로 물림
        while (n > 0 && n < utf8.Length && (utf8[n] & 0xC0) == 0x80) n--;
        System.Array.Copy(utf8, buf, n);
        return buf;
    }

    // 고정 길이 UTF-8 바이트 배열 → 문자열 (첫 널 전까지).
    public static string Decode(byte[] buf)
    {
        if (buf == null) return "";
        int n = System.Array.IndexOf(buf, (byte)0);
        if (n < 0) n = buf.Length;
        return System.Text.Encoding.UTF8.GetString(buf, 0, n).Trim();
    }
}