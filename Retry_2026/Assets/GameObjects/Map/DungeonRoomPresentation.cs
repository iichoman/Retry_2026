using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Surface decoration only. Does not change shared dungeon/prop layout, collision, or navigation.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(DungeonGenerator_ChunkMesh))]
public sealed class DungeonRoomPresentation : MonoBehaviour
{
    public DungeonPresentationPalette palette;
    public const string RootName = "DungeonRoomPresentation";
    private const float FloorY = 1.012f;
    private static readonly Vector3Int[] Directions = { Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back };
    private readonly List<Mesh> ownedMeshes = new();
    private DungeonGenerator_ChunkMesh generator;
    private Transform generatedRoot;
    private bool reportedMissingPalette;

    private void OnEnable()
    {
        generator = GetComponent<DungeonGenerator_ChunkMesh>();
        generator.DungeonGenerated += OnGenerated;
        if (generator.Rooms.Count > 0) Rebuild();
    }
    private void OnDisable()
    {
        if (generator != null) generator.DungeonGenerated -= OnGenerated;
        Clear();
    }
    private void OnGenerated(DungeonGenerator_ChunkMesh _) => Rebuild();

    public static DungeonRoomStyle StyleFor(DungeonGenerator_ChunkMesh source, Room room)
    {
        if (room.type == RoomType.Boss) return DungeonRoomStyle.Guardian;
        if (room.type == RoomType.Exit) return DungeonRoomStyle.Extraction;
        if (room.type == RoomType.Reward) return DungeonRoomStyle.Treasure;
        // A separate hash leaves all authoritative random sequences untouched.
        uint hash = unchecked((uint)source.seed ^ (uint)room.bounds.xMin * 73856093u ^ (uint)room.bounds.zMin * 19349663u);
        hash ^= hash >> 16;
        return (DungeonRoomStyle)(hash % 3);
    }

    public void Rebuild()
    {
        Clear();
        if (generator == null) generator = GetComponent<DungeonGenerator_ChunkMesh>();
        if (palette == null) palette = DungeonPresentationPalette.Load();
        if (palette == null)
        {
            if (!reportedMissingPalette) { Debug.LogWarning("Dungeon presentation palette missing; using original dungeon surfaces.", this); reportedMissingPalette = true; }
            return;
        }
        generatedRoot = new GameObject(RootName).transform;
        generatedRoot.SetParent(transform, false);
        // The existing offline NavMesh uses render meshes, so excluding only colliders is insufficient.
        generatedRoot.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        foreach (var room in generator.Rooms)
        {
            var style = StyleFor(generator, room);
            var theme = palette.Resolve(style);
            if (theme == null || theme.stone == null || theme.border == null || theme.accent == null) continue;
            var drawing = new SurfaceDrawing(generator, room);
            DrawBorders(drawing, room);
            if(room.type==RoomType.Boss)DrawCenter(drawing, room, style);
            if(room.type==RoomType.Reward)DrawTreasure(drawing, room);
            DrawDoors(drawing, room);
            // Mounted 3D standards are supplied by DungeonSetDressing.
            var mesh = drawing.Build();
            if (mesh == null) continue;
            ownedMeshes.Add(mesh);
            var go = new GameObject($"Room_{room.id}_{style}");
            go.transform.SetParent(generatedRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { theme.stone, theme.border, theme.accent };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }
    }

    public void Clear()
    {
        if (generatedRoot == null) generatedRoot = transform.Find(RootName);
        if (generatedRoot != null)
        {
            generatedRoot.gameObject.SetActive(false);
            Release(generatedRoot.gameObject);
            generatedRoot = null;
        }
        foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh);
        ownedMeshes.Clear();
    }
    private static void Release(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

    private static void DrawBorders(SurfaceDrawing d, Room room)
    {
        // Follow the actual room footprint and inner pillars, rather than drawing across blocked tiles.
        foreach (var tile in d.OrderedTiles)
        {
            if (!d.Walkable(tile)) continue;
            foreach (var direction in Directions)
            {
                if (d.Walkable(tile + direction) || !d.Wall(tile + direction)) continue;
                var c = new Vector2(tile.x + .5f, tile.z + .5f);
                var normal = new Vector2(direction.x, direction.z);
                bool xWall = direction.x != 0;
                d.Rect(c + normal * .20f, xWall ? new Vector2(.38f, .96f) : new Vector2(.96f, .38f), 0);
                d.Rect(c - normal * .08f, xWall ? new Vector2(.08f, .96f) : new Vector2(.96f, .08f), 1);

            }
        }
    }

    private static void DrawCenter(SurfaceDrawing d, Room room, DungeonRoomStyle style)
    {
        float side = Mathf.Min(room.bounds.size.x, room.bounds.size.z);
        if (side < 12) return;
        var center = new Vector2(room.bounds.center.x, room.bounds.center.z);
        if (room.layoutType == RoomLayoutType.CenterBlock)
        {
            // Two smaller panels beside the existing central obstacle leave its silhouette legible.
            bool wide = room.bounds.size.x >= room.bounds.size.z;
            var offset = wide ? new Vector2(room.bounds.size.x * .31f, 0) : new Vector2(0, room.bounds.size.z * .31f);
            DrawPanel(d, center + offset, 3.6f, 4.8f, style);
            DrawPanel(d, center - offset, 3.6f, 4.8f, style);
        }
        else
        {
            float radius = Mathf.Min(side * .20f, style == DungeonRoomStyle.Guardian ? 12 : 8);
            float length = radius * (room.shape == RoomShape.LongTall ? 1.5f : 1.15f);
            float width = radius * (room.shape == RoomShape.LongWide ? 1.5f : 1);
            DrawPanel(d, center, width, length, style);
        }
    }

    private static void DrawTreasure(SurfaceDrawing d, Room room)
    {
        var center = new Vector2(room.bounds.center.x, room.bounds.center.z);
        float halfX = room.bounds.size.x * .38f, halfZ = room.bounds.size.z * .38f;
        DrawPanel(d, center, halfX, halfZ, DungeonRoomStyle.Treasure);
        // Gold joints and garnet insets decorate the floor without adding collision.
        for (int x = room.bounds.xMin + 3; x < room.bounds.xMax - 2; x += 4)
        for (int z = room.bounds.zMin + 3; z < room.bounds.zMax - 2; z += 4)
        {
            var p = new Vector2(x + .5f, z + .5f);
            d.Diamond(p, .35f, 1); d.Diamond(p, .20f, 2);
        }
        var entrance = RoomBlueprint.Entrance(room);
        var axis = new Vector2(entrance.x, entrance.z); var right = new Vector2(-axis.y, axis.x);
        float length = entrance.x != 0 ? halfX : halfZ;
        foreach (int side in new[] { -1, 1 })
            d.Line(center + axis * length + right * side * 1.5f, center + right * side * 1.5f, .12f, 1);
    }

    private static void DrawPanel(SurfaceDrawing d, Vector2 center, float halfX, float halfZ, DungeonRoomStyle style)
    {
        // Tile-sized pieces let odd room footprints clip panels without covering holes or pillars.
        for (int x = Mathf.FloorToInt(center.x - halfX); x < Mathf.CeilToInt(center.x + halfX); x++)
        for (int z = Mathf.FloorToInt(center.y - halfZ); z < Mathf.CeilToInt(center.y + halfZ); z++)
        {
            var p = new Vector2(x + .5f, z + .5f);
            if (Mathf.Abs(p.x - center.x) > halfX || Mathf.Abs(p.y - center.y) > halfZ) continue;
            // Grout remains visible between inset slabs.
            d.Rect(p, new Vector2(.96f, .96f), 0);
        }
        d.Line(center + new Vector2(-halfX, -halfZ), center + new Vector2(halfX, -halfZ), .18f, 1);
        d.Line(center + new Vector2(-halfX, halfZ), center + new Vector2(halfX, halfZ), .18f, 1);
        d.Line(center + new Vector2(-halfX, -halfZ), center + new Vector2(-halfX, halfZ), .18f, 1);
        d.Line(center + new Vector2(halfX, -halfZ), center + new Vector2(halfX, halfZ), .18f, 1);
        foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
            d.Diamond(center + new Vector2(sx * (halfX - .55f), sz * (halfZ - .55f)), .32f, 2);
        float radius = Mathf.Min(halfX, halfZ) * .59f;
        int segments = style == DungeonRoomStyle.Guardian ? 8 : style == DungeonRoomStyle.Storehouse ? 4 : 32;
        d.Ring(center, radius, .15f, segments, 1);
        d.Ring(center, radius * .84f, .07f, segments, 2);
        if (style == DungeonRoomStyle.Extraction)
        {
            for (int i = -1; i <= 1; i++)
            {
                var tip = center + new Vector2(0, i * 1.15f + .65f);
                d.Line(tip + new Vector2(-1, -.8f), tip, .22f, 2);
                d.Line(tip, tip + new Vector2(1, -.8f), .22f, 2);
            }
        }
        else
        {
            d.Diamond(center, radius * .42f, 1);
            d.Diamond(center, radius * .24f, 2);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * .5f;
                var axis = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                d.Line(center + axis * radius * .55f, center + axis * radius * .74f, .20f, 2);
            }
        }
    }

    private static void DrawDoors(SurfaceDrawing d, Room room)
    {
        // doorwayFloorTiles stores doorway centres, not the full ten-metre opening.
        // Derive the full threshold from actual room/corridor adjacency instead.
        foreach (var tile in d.OrderedTiles)
        {
            if (!d.Walkable(tile)) continue;
            foreach (var direction in Directions)
            {
                // Only actual outward edges; corridor may share floor tiles with this room.
                if (room.floorTiles.Contains(tile + direction) || !d.GlobalFloor(tile + direction)) continue;
                var p = new Vector2(tile.x + .5f, tile.z + .5f);
                var inward = new Vector2(-direction.x, -direction.z);
                bool xDoor = direction.x != 0;
                for (int band = 0; band < 2; band++)
                    d.Rect(p + inward * (.1f + band * .45f), xDoor ? new Vector2(.14f, .90f) : new Vector2(.90f, .14f), band == 0 ? 1 : 2);
            }
        }
        foreach (var door in room.doorwayFloorTiles)
        {
            var p = new Vector2(door.x + .5f, door.z + .5f);
            var towardCenter = new Vector2(room.bounds.center.x, room.bounds.center.z) - p;
            var inward = Mathf.Abs(towardCenter.x) > Mathf.Abs(towardCenter.y)
                ? new Vector2(Mathf.Sign(towardCenter.x), 0) : new Vector2(0, Mathf.Sign(towardCenter.y));
            // Short, broken inlay runs identify entrances without claiming to be navigation paths.
            for (int step = 1; step <= 3; step++)
            {
                var size = inward.x != 0 ? new Vector2(.18f, 2.4f) : new Vector2(2.4f, .18f);
                d.Rect(p + inward * (step * 1.5f), size, step == 3 ? 2 : 1);
            }
        }
    }

    private static void DrawStandards(SurfaceDrawing d, Room room, DungeonRoomStyle style)
    {
        // Flat mounted standards, not pass-through scenery that appears to be a physical obstacle.
        var center = new Vector2(room.bounds.center.x, room.bounds.center.z);
        foreach (var direction in new[] { Vector3Int.forward, Vector3Int.right })
        {
            Vector3Int best = default;
            float nearest = float.MaxValue;
            foreach (var tile in d.OrderedTiles)
            {
                if (!d.Walkable(tile) || !d.Wall(tile + direction)) continue;
                if (!d.Wall(tile + direction + new Vector3Int(direction.z, 0, direction.x)) || !d.Wall(tile + direction - new Vector3Int(direction.z, 0, direction.x))) continue;
                float distance = (new Vector2(tile.x + .5f, tile.z + .5f) - center).sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = tile; }
            }
            if (nearest == float.MaxValue) continue;
            Vector3 wallCenter = new Vector3(best.x + .5f, 4.15f, best.z + .5f) + (Vector3)direction * .485f;
            Vector3 across = new Vector3(direction.z, 0, -direction.x);
            d.WallQuad(wallCenter, across, Vector3.up, 1.05f, 1.48f, 0, -(Vector3)direction);
            d.WallQuad(wallCenter - (Vector3)direction * .004f, across, Vector3.up, .93f, 1.32f, 1, -(Vector3)direction);
            d.WallQuad(wallCenter - (Vector3)direction * .008f, across, Vector3.up, .78f, 1.16f, 0, -(Vector3)direction);
            Vector3 symbol = wallCenter - (Vector3)direction * .013f;
            float radius = style == DungeonRoomStyle.Guardian ? .59f : .45f;
            d.WallDiamond(symbol, across, radius, 2, -(Vector3)direction);
            for (int mark = 0; mark <= (int)style; mark++)
                d.WallQuad(symbol + across * ((mark - (int)style * .5f) * .21f) - Vector3.up * .90f, across, Vector3.up, .055f, .12f, 2, -(Vector3)direction);
        }
    }

    private sealed class SurfaceDrawing
    {
        private readonly DungeonGenerator_ChunkMesh generator;
        private readonly Room room;
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uv = new();
        private readonly List<int>[] triangles = { new(), new(), new() };
        public readonly List<Vector3Int> OrderedTiles;
        public SurfaceDrawing(DungeonGenerator_ChunkMesh generator, Room room)
        {
            this.generator = generator; this.room = room;
            OrderedTiles = new List<Vector3Int>(room.floorTiles);
            OrderedTiles.Sort((a,b) => a.x == b.x ? a.z.CompareTo(b.z) : a.x.CompareTo(b.x));
        }
        public bool Walkable(Vector3Int tile) => room.floorTiles.Contains(tile) && !room.blockedTiles.Contains(tile) && !generator.IsWallTile(tile);
        public bool Wall(Vector3Int tile) => generator.IsWallTile(tile);
        public bool GlobalFloor(Vector3Int tile) => generator.IsFloorTile(tile) && !Wall(tile);
        private bool Fits(Vector2 min, Vector2 max)
        {
            for (int x = Mathf.FloorToInt(min.x + .001f); x <= Mathf.FloorToInt(max.x - .001f); x++)
            for (int z = Mathf.FloorToInt(min.y + .001f); z <= Mathf.FloorToInt(max.y - .001f); z++)
                if (!Walkable(new Vector3Int(x,0,z))) return false;
            return true;
        }
        public void Rect(Vector2 center, Vector2 size, int material)
        {
            var min = center - size * .5f; var max = center + size * .5f;
            FloorQuad(min, new Vector2(min.x,max.y), max, new Vector2(max.x,min.y), material);
        }
        public void Diamond(Vector2 center, float radius, int material) => FloorQuad(center + Vector2.down * radius, center + Vector2.left * radius, center + Vector2.up * radius, center + Vector2.right * radius, material);
        public void Line(Vector2 from, Vector2 to, float width, int material)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from,to)));
            var delta = to - from; var normal = new Vector2(-delta.y,delta.x).normalized * width * .5f;
            for (int i=0;i<count;i++)
            {
                var a = Vector2.Lerp(from,to,i/(float)count); var b = Vector2.Lerp(from,to,(i+1)/(float)count);
                FloorQuad(a-normal,a+normal,b+normal,b-normal,material);
            }
        }
        public void Ring(Vector2 center, float radius, float width, int count, int material)
        {
            for(int i=0;i<count;i++)
            {
                float a = i * Mathf.PI * 2 / count, b = (i+1) * Mathf.PI * 2 / count;
                Line(center+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*radius,center+new Vector2(Mathf.Sin(b),Mathf.Cos(b))*radius,width,material);
            }
        }
        private void FloorQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, int material)
        {
            if (!Fits(Vector2.Min(Vector2.Min(a,b),Vector2.Min(c,d)),Vector2.Max(Vector2.Max(a,b),Vector2.Max(c,d)))) return;
            float y = FloorY + material * .006f;
            Quad(new Vector3(a.x,y,a.y),new Vector3(b.x,y,b.y),new Vector3(c.x,y,c.y),new Vector3(d.x,y,d.y),material,Vector3.up);
        }
        public void WallQuad(Vector3 center, Vector3 across, Vector3 up, float halfWidth, float halfHeight, int material, Vector3 normal) => Quad(center-across*halfWidth-up*halfHeight,center-across*halfWidth+up*halfHeight,center+across*halfWidth+up*halfHeight,center+across*halfWidth-up*halfHeight,material,normal);
        public void WallDiamond(Vector3 center, Vector3 across, float radius, int material, Vector3 normal) => Quad(center-Vector3.up*radius,center-across*radius,center+Vector3.up*radius,center+across*radius,material,normal);
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material, Vector3 normal)
        {
            int start = vertices.Count;
            Vector3 offset = generator.WorldOffset;
            vertices.Add(a+offset); vertices.Add(b+offset); vertices.Add(c+offset); vertices.Add(d+offset);
            uv.Add(ProjectUv(a,normal)); uv.Add(ProjectUv(b,normal)); uv.Add(ProjectUv(c,normal)); uv.Add(ProjectUv(d,normal));
            bool reverse = Vector3.Dot(Vector3.Cross(b-a,c-a),normal) < 0;
            var indices = triangles[material];
            indices.Add(start); indices.Add(start+(reverse?2:1)); indices.Add(start+(reverse?1:2));
            indices.Add(start); indices.Add(start+(reverse?3:2)); indices.Add(start+(reverse?2:3));
        }
        private static Vector2 ProjectUv(Vector3 p, Vector3 normal) =>
            (normal.y > .5f ? new Vector2(p.x,p.z) : Mathf.Abs(normal.x) > .5f ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y)) * .5f;
        public Mesh Build()
        {
            if(vertices.Count==0) return null;
            var mesh = new Mesh { name = $"Room_{room.id}_SurfaceInlays", indexFormat = IndexFormat.UInt32, subMeshCount = 3 };
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv);
            for(int i=0;i<3;i++) mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
