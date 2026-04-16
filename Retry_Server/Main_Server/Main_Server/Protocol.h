#pragma once

#pragma pack(push, 1) // C#의 Pack=1 과 동일하게 빈 공간 없이 압축

// 패킷 타입 정의
enum class PacketType : int {
    LOGIN = 1,
    ROOM_CREATE = 2,
    ROOM_JOIN = 3,
    ROOM_LIST = 4,
    SESSION_ASSIGN = 5 // 클라이언트에게 세션 서버 정보를 줄 때 사용
};

// 기본 데이터 구조
struct PlayerData {
    int id;
    float x, y, z;
    float rotY;
    float speed;
    int state;
};

// 방 정보 구조체
struct RoomInfo {
    int roomId;
    int hostId;
    int currentPlayers;
    char roomName[32];
};

// 패킷 헤더
struct PacketHeader {
    PacketType type;
    int size;
};

#pragma pack(pop) // 압축 설정 해제