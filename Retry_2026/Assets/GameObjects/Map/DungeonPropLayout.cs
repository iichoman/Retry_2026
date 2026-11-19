using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoritative prop layout contract, mirrored by Session_Manager/Dungeon/DungeonPropLayout.h.
/// Decisions and geometry use integer centimetres; asset bounds never affect server obstacles.
/// </summary>
public static class DungeonPropLayout
{
    public const int Version = 1;
    public const float ActorRadius = .5f;
    public enum Theme { Storage, Study, Rubble }
    public enum Kind { Crate, Table, Chair, Jug, Pot, Rock, Wood, Book, Bottle, Candle }
    public readonly struct Slot
    {
        public readonly int RoomId, GroupId;
        public readonly Theme GroupTheme;
        public readonly Kind PropKind;
        public readonly Vector3 Center, Size;
        public readonly float Yaw;
        public readonly bool Supported;
        public Bounds Bounds => new Bounds(Center, Size);
        public Slot(int roomId, int groupId, Theme theme, Kind kind, Vector3 center, Vector3 size, float yaw, bool supported)
        { RoomId = roomId; GroupId = groupId; GroupTheme = theme; PropKind = kind; Center = center; Size = size; Yaw = yaw; Supported = supported; }
    }
    private readonly struct Part
    {
        public readonly Kind Kind;
        public readonly int U, V, X, Y, Z, Base, Yaw;
        public readonly bool Supported;
        public Part(Kind kind, int u, int v, int x, int y, int z, int bottom = 100, int yaw = 0, bool supported = false)
        { Kind = kind; U = u; V = v; X = x; Y = y; Z = z; Base = bottom; Yaw = yaw; Supported = supported; }
    }
    private readonly struct Rect
    {
        public readonly int X, Z, SX, SZ;
        public Rect(int x, int z, int sx, int sz) { X = x; Z = z; SX = sx; SZ = sz; }
        public bool Overlaps(Rect other, int padding) => Math.Abs(X - other.X) * 2 < SX + other.SX + padding * 2 && Math.Abs(Z - other.Z) * 2 < SZ + other.SZ + padding * 2;
    }
    private static readonly Vector3Int[] Directions = { Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back };
    private static readonly Part[][] Parts =
    {
        new[] {
            new Part(Kind.Crate, 0, 100, 180, 140, 150), new Part(Kind.Crate, -165, 100, 110, 95, 110),
            new Part(Kind.Jug, 165, 100, 70, 110, 70), new Part(Kind.Pot, -170, 220, 80, 65, 80),
            new Part(Kind.Crate, 40, 280, 120, 85, 100), new Part(Kind.Crate, -25, 100, 85, 65, 80, 240, 0, true) },
        new[] {
            new Part(Kind.Table, 0, 120, 260, 105, 130), new Part(Kind.Chair, 30, 270, 70, 125, 80, 100, 180),
            new Part(Kind.Crate, -200, 100, 110, 85, 100), new Part(Kind.Jug, 200, 100, 70, 110, 70),
            new Part(Kind.Pot, 180, 240, 80, 65, 80), new Part(Kind.Book, -60, 120, 50, 12, 40, 205, 0, true),
            new Part(Kind.Bottle, 75, 120, 20, 40, 20, 205, 0, true), new Part(Kind.Candle, 20, 90, 15, 35, 15, 205, 0, true) },
        new[] {
            new Part(Kind.Rock, 0, 100, 220, 120, 160), new Part(Kind.Rock, -180, 130, 100, 65, 100),
            new Part(Kind.Pot, 180, 100, 80, 70, 80), new Part(Kind.Wood, 10, 250, 250, 35, 70),
            new Part(Kind.Jug, -170, 260, 60, 80, 60) }
    };
    private static uint Next(ref uint state)
    {
        unchecked { state += 0x9E3779B9u; uint value = state; value = (value ^ (value >> 16)) * 0x85EBCA6Bu; value = (value ^ (value >> 13)) * 0xC2B2AE35u; return value ^ (value >> 16); }
    }
    private static int FloorDivide(int numerator, int denominator) => numerator >= 0 ? numerator / denominator : -((-numerator + denominator - 1) / denominator);
    public static List<Slot> GenerateRoom(DungeonGenerator_ChunkMesh generator, Room room)
    {
        if (generator == null) return new List<Slot>();
        var result=new List<Slot>();if(room==null)return result;int group=0;
        foreach(var socket in RoomBlueprint.Sockets(generator.seed,room)){
            if(socket.marker=='L'||socket.marker=='N'||!RoomBlueprint.Safe(room,socket.tile,generator.IsWallTile))continue;
            if(room.type==RoomType.Reward&&!RoomBlueprint.Safe(room,RoomBlueprint.Mirror(room,socket.tile),generator.IsWallTile))continue;
            Kind kind=socket.marker=='T'?Kind.Table:socket.marker=='C'?Kind.Crate:socket.marker=='R'?Kind.Rock:socket.marker=='B'?Kind.Book:Kind.Pot;
            var size=RoomBlueprint.Size(socket.marker);var pos=generator.TileToWorldCenter(socket.tile)+Vector3.up*(1+size.y*.5f);
            float yaw=socket.marker=='B'&&socket.tile.z>room.bounds.center.z?180:0;
            if(room.type==RoomType.Reward){var facing=RoomBlueprint.Entrance(room);yaw=Quaternion.LookRotation((Vector3)facing).eulerAngles.y;if(facing.x!=0)size=new Vector3(size.z,size.y,size.x);}
            result.Add(new Slot(room.id,group++,Theme.Storage,kind,pos,size,yaw,false));
            if(kind==Kind.Table){var top=pos+Vector3.up*(size.y*.5f+.06f);result.Add(new Slot(room.id,group-1,Theme.Study,Kind.Book,top+Vector3.left*.5f,new Vector3(.5f,.12f,.4f),0,true));result.Add(new Slot(room.id,group-1,Theme.Study,Kind.Candle,top+Vector3.right*.5f+Vector3.up*.12f,new Vector3(.15f,.35f,.15f),0,true));}
        }
        return result;
    }
    public static List<Slot> Generate(int seed, Room room, Vector3 worldOffset, Func<Vector3Int, bool> isWall)
    {
        var result = new List<Slot>();
        if (room == null || room.type == RoomType.Start || room.floorTiles.Count == 0) return result;
        var routes = ReserveRoutes(room);
        var accepted = new List<Rect>();
        uint roomHash = DungeonSetDressing.Hash(seed,room.bounds.xMin,room.bounds.zMin);
        // Authored storage wall / reading court / broken corners. Only mirror the plan;
        // preserve furniture relationships instead of independently shuffling sockets.
        int[][] plans={new[]{0,1,2,7,10,4,3,5,6,8,9,11},new[]{1,4,7,10,0,3,6,9,2,5,8,11},new[]{0,5,6,11,1,4,7,10,2,3,8,9}};
        var anchors=(int[])plans[roomHash%3].Clone();
        for(int i=0;i<anchors.Length;i++){int wall=anchors[i]/3; if(wall<2&&(roomHash&0x100)!=0)wall=1-wall;if(wall>=2&&(roomHash&0x200)!=0)wall=5-wall;anchors[i]=wall*3+anchors[i]%3;}
        int side = Math.Min(room.bounds.size.x, room.bounds.size.z);
        int budget = Math.Max(2, Math.Min(6, side / 8));
        if (room.type == RoomType.Boss || room.type == RoomType.Exit) budget = 2;
        int groups = 0;
        foreach (int anchor in anchors)
        {
            if (groups == budget) break;
            int wall = anchor / 3, fraction = new[] { 20, 50, 80 }[anchor % 3];
            int ax = room.bounds.xMin * 100, az = room.bounds.zMin * 100;
            if (wall < 2) { ax += room.bounds.size.x * fraction; if (wall == 1) az += room.bounds.size.z * 100; }
            else { az += room.bounds.size.z * fraction; if (wall == 3) ax += room.bounds.size.x * 100; }
            int fx = wall == 2 ? 1 : wall == 3 ? -1 : 0, fz = wall == 0 ? 1 : wall == 1 ? -1 : 0;
            int tx = fz, tz = -fx;
            int yaw = wall == 0 ? 0 : wall == 1 ? 180 : wall == 2 ? 90 : 270;
            Theme theme = room.type == RoomType.Boss || room.type == RoomType.Exit ? Theme.Rubble : (Theme)(DungeonSetDressing.Hash(seed,room.bounds.xMin,room.bounds.zMin) % 3u);
            var pending = new List<Slot>(); var floor = new List<Rect>(); bool valid = true;
            foreach (Part part in Parts[(int)theme])
            {
                int cx = ax + tx * part.U + fx * part.V, cz = az + tz * part.U + fz * part.V;
                int sx = wall < 2 ? part.X : part.Z, sz = wall < 2 ? part.Z : part.X;
                var rect = new Rect(cx, cz, sx, sz);
                if (!part.Supported)
                {
                    if (!ValidRect(room, rect, routes, isWall)) { valid = false; break; }
                    foreach (Rect previous in accepted) if (rect.Overlaps(previous, 25)) { valid = false; break; }
                    if (!valid) break;
                    floor.Add(rect);
                }
                pending.Add(new Slot(room.id, groups, theme, part.Kind,
                    worldOffset + new Vector3(cx * .01f, (part.Base * 2 + part.Y) * .005f, cz * .01f),
                    new Vector3(sx * .01f, part.Y * .01f, sz * .01f), (yaw + part.Yaw) % 360, part.Supported));
            }
            if (!valid) continue;
            result.AddRange(pending); accepted.AddRange(floor); groups++;
        }
        return result;
    }
    private static bool ValidRect(Room room, Rect rect, HashSet<Vector3Int> routes, Func<Vector3Int, bool> isWall)
    {
        // 10cm free rim; doubled cm coordinates preserve odd dimensions without rounding.
        int xMin = FloorDivide(rect.X * 2 - rect.SX - 20, 200), xMax = FloorDivide(rect.X * 2 + rect.SX + 19, 200);
        int zMin = FloorDivide(rect.Z * 2 - rect.SZ - 20, 200), zMax = FloorDivide(rect.Z * 2 + rect.SZ + 19, 200);
        int arena = Math.Min(1600, Math.Min(room.bounds.size.x, room.bounds.size.z) * 30);
        int centerX = (room.bounds.xMin + room.bounds.xMax) * 50, centerZ = (room.bounds.zMin + room.bounds.zMax) * 50;
        for (int x = xMin; x <= xMax; x++) for (int z = zMin; z <= zMax; z++)
        {
            var tile = new Vector3Int(x, 0, z);
            if (!room.floorTiles.Contains(tile) || room.blockedTiles.Contains(tile) || routes.Contains(tile) || (isWall != null && isWall(tile))) return false;
            foreach (var door in room.doorwayFloorTiles) if (Math.Abs(x - door.x) <= 3 && Math.Abs(z - door.z) <= 3) return false;
            long dx = x * 100 + 50 - centerX, dz = z * 100 + 50 - centerZ;
            if (room.type == RoomType.Boss && dx * dx + dz * dz < (long)arena * arena) return false;
        }
        return true;
    }
    private static HashSet<Vector3Int> ReserveRoutes(Room room)
    {
        var reserved = new HashSet<Vector3Int>(); var floor = new List<Vector3Int>(room.floorTiles);
        floor.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
        long best = long.MaxValue; Vector3Int center = default;
        foreach (var tile in floor)
        {
            if (room.blockedTiles.Contains(tile)) continue;
            long dx = tile.x * 2 + 1 - room.bounds.xMin - room.bounds.xMax, dz = tile.z * 2 + 1 - room.bounds.zMin - room.bounds.zMax;
            long distance = dx * dx + dz * dz; if (distance < best) { best = distance; center = tile; }
        }
        if (best == long.MaxValue) return reserved;
        var parents = new Dictionary<Vector3Int, Vector3Int> { [center] = center }; var queue = new Queue<Vector3Int>(); queue.Enqueue(center);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue(); foreach (var direction in Directions)
            { var next = tile + direction; if (!room.floorTiles.Contains(next) || room.blockedTiles.Contains(next) || parents.ContainsKey(next)) continue; parents[next] = tile; queue.Enqueue(next); }
        }
        Reserve(reserved, center, 3);
        foreach (var door in room.doorwayFloorTiles)
        {
            var tile = new Vector3Int(door.x, 0, door.z); int remaining = parents.Count;
            while (parents.TryGetValue(tile, out var parent) && remaining-- > 0) { Reserve(reserved, tile, 2); if (parent == tile) break; tile = parent; }
        }
        return reserved;
    }
    private static void Reserve(HashSet<Vector3Int> reserved, Vector3Int center, int radius)
    { for (int x = -radius; x <= radius; x++) for (int z = -radius; z <= radius; z++) reserved.Add(center + new Vector3Int(x, 0, z)); }
}
