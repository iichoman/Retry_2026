using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameMinimapUI : MonoBehaviour
{
    [SerializeField] private DungeonGenerator_ChunkMesh dungeon;
    [SerializeField] private Transform player;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

    private readonly HashSet<int> visitedRoomIds = new HashSet<int>();
    private readonly Dictionary<int, RoomView> roomViews = new Dictionary<int, RoomView>();
    private readonly List<Image> markerViews = new List<Image>();

    private RectTransform root;
    private RectTransform mapRoot;
    private Room currentRoom;
    private StartRoom currentStartRoom;
    private Monster[] cachedMonsters = new Monster[0];
    private ExitPortal[] cachedExits = new ExitPortal[0];
    private float nextRefreshTime;
    private float nextActorRefreshTime;
    private int markerUseCount;
    private int mapMinX;
    private int mapMaxX = 1;
    private int mapMinZ;
    private int mapMaxZ = 1;
    private bool hasMapBounds;
    private DungeonGenerator_ChunkMesh subscribedDungeon;

    private static readonly Vector2 PanelSize = new Vector2(252f, 204f);
    private static readonly Vector2 MapSize = new Vector2(232f, 184f);
    private static readonly Color PanelTop = new Color(0.055f, 0.065f, 0.075f, 0.96f);
    private static readonly Color PanelBottom = new Color(0.012f, 0.015f, 0.022f, 0.97f);

    private void Awake()
    {
        ResolveReferences();
        SubscribeToDungeon();
        Build();
        RefreshNow(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToDungeon();
        RefreshNow(true);
    }

    private void OnDisable()
    {
        if (subscribedDungeon != null)
        {
            subscribedDungeon.DungeonGenerated -= HandleDungeonGenerated;
            subscribedDungeon = null;
        }
    }

    private void Update()
    {
        RefreshMapRotation();

        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        RefreshNow(false);
    }

    private void ResolveReferences()
    {
        if (dungeon == null)
        {
            dungeon = FindFirstObjectByType<DungeonGenerator_ChunkMesh>();
        }

        if (player == null)
        {
            Player_State playerState = FindFirstObjectByType<Player_State>();
            if (playerState != null)
            {
                player = playerState.transform;
            }
            else
            {
                Player foundPlayer = FindFirstObjectByType<Player>();
                if (foundPlayer != null)
                {
                    player = foundPlayer.transform;
                }
            }
        }
    }

    private void SubscribeToDungeon()
    {
        if (subscribedDungeon == dungeon)
        {
            return;
        }

        if (subscribedDungeon != null)
        {
            subscribedDungeon.DungeonGenerated -= HandleDungeonGenerated;
        }

        subscribedDungeon = dungeon;
        if (subscribedDungeon != null)
        {
            subscribedDungeon.DungeonGenerated += HandleDungeonGenerated;
        }
    }

    private void HandleDungeonGenerated(DungeonGenerator_ChunkMesh generatedDungeon)
    {
        dungeon = generatedDungeon;
        visitedRoomIds.Clear();
        hasMapBounds = false;
        HideAllRooms();
        RefreshNow(true);
    }

    private void Build()
    {
        if (root != null)
        {
            return;
        }

        root = transform as RectTransform;
        if (root == null)
        {
            return;
        }

        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        GameObject panelObject = CreateUIObject("Minimap Panel", transform);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = Vector2.one;
        panel.anchorMax = Vector2.one;
        panel.pivot = Vector2.one;
        panel.anchoredPosition = new Vector2(-28f, -30f);
        panel.sizeDelta = PanelSize;

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.raycastTarget = false;
        SetGradient(panelImage, PanelTop, PanelBottom);
        AddOutline(panelImage, new Color(1f, 0.72f, 0.28f, 0.54f), new Vector2(2f, -2f));
        AddShadow(panelImage, new Color(0f, 0f, 0f, 0.72f), new Vector2(8f, -8f));

        GameObject mapFrameObject = CreateUIObject("Map Frame", panel);
        RectTransform mapFrame = mapFrameObject.GetComponent<RectTransform>();
        mapFrame.anchorMin = new Vector2(0f, 1f);
        mapFrame.anchorMax = mapFrame.anchorMin;
        mapFrame.pivot = new Vector2(0f, 1f);
        mapFrame.anchoredPosition = new Vector2(10f, -10f);
        mapFrame.sizeDelta = MapSize;

        Image mapFrameImage = mapFrameObject.AddComponent<Image>();
        mapFrameImage.raycastTarget = false;
        SetGradient(mapFrameImage, new Color(0.014f, 0.021f, 0.028f, 0.98f), new Color(0.004f, 0.007f, 0.012f, 0.98f));
        AddOutline(mapFrameImage, new Color(0.72f, 0.56f, 0.3f, 0.36f), new Vector2(1f, -1f));

        RectMask2D mask = mapFrameObject.AddComponent<RectMask2D>();
        mask.padding = Vector4.zero;

        GameObject gridObject = CreateUIObject("Map Grid", mapFrame);
        RectTransform gridRect = gridObject.GetComponent<RectTransform>();
        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.offsetMin = Vector2.zero;
        gridRect.offsetMax = Vector2.zero;
        Image gridImage = gridObject.AddComponent<Image>();
        gridImage.raycastTarget = false;
        gridImage.color = new Color(0.12f, 0.16f, 0.18f, 0.18f);

        mapRoot = CreateUIObject("Map Rooms", mapFrame).GetComponent<RectTransform>();
        mapRoot.anchorMin = new Vector2(0.5f, 0.5f);
        mapRoot.anchorMax = mapRoot.anchorMin;
        mapRoot.pivot = new Vector2(0.5f, 0.5f);
        mapRoot.anchoredPosition = Vector2.zero;
        mapRoot.sizeDelta = MapSize;
    }

    private void RefreshNow(bool forceActors)
    {
        nextRefreshTime = Time.unscaledTime + refreshInterval;
        ResolveReferences();
        SubscribeToDungeon();

        if (dungeon == null || player == null || mapRoot == null)
        {
            return;
        }

        if (!hasMapBounds)
        {
            RebuildMapBounds();
        }

        RefreshCurrentRoom();
        DrawVisitedRooms();
        RefreshMapRotation();

        if (forceActors || Time.unscaledTime >= nextActorRefreshTime)
        {
            cachedMonsters = FindObjectsByType<Monster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            cachedExits = FindObjectsByType<ExitPortal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextActorRefreshTime = Time.unscaledTime + 0.6f;
        }

        DrawCurrentRoomMarkers();
    }

    private void RefreshMapRotation()
    {
        if (mapRoot == null || player == null)
        {
            return;
        }

        mapRoot.localEulerAngles = new Vector3(0f, 0f, player.eulerAngles.y);
    }

    private void RebuildMapBounds()
    {
        bool foundAny = false;
        int minX = 0;
        int maxX = 1;
        int minZ = 0;
        int maxZ = 1;

        IReadOnlyList<Room> rooms = dungeon.Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            IncludeBounds(rooms[i].bounds, ref foundAny, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        IReadOnlyList<StartRoom> startRooms = dungeon.GetAssignedStartRooms();
        for (int i = 0; i < startRooms.Count; i++)
        {
            IncludeBounds(startRooms[i].bounds, ref foundAny, ref minX, ref maxX, ref minZ, ref maxZ);
        }

        mapMinX = minX;
        mapMaxX = Mathf.Max(minX + 1, maxX);
        mapMinZ = minZ;
        mapMaxZ = Mathf.Max(minZ + 1, maxZ);
        hasMapBounds = foundAny;
    }

    private static void IncludeBounds(BoundsInt bounds, ref bool foundAny, ref int minX, ref int maxX, ref int minZ, ref int maxZ)
    {
        if (!foundAny)
        {
            minX = bounds.xMin;
            maxX = bounds.xMax;
            minZ = bounds.zMin;
            maxZ = bounds.zMax;
            foundAny = true;
            return;
        }

        minX = Mathf.Min(minX, bounds.xMin);
        maxX = Mathf.Max(maxX, bounds.xMax);
        minZ = Mathf.Min(minZ, bounds.zMin);
        maxZ = Mathf.Max(maxZ, bounds.zMax);
    }

    private void RefreshCurrentRoom()
    {
        Vector3Int tile = dungeon.WorldToTile(player.position);
        Vector3Int flatTile = new Vector3Int(tile.x, 0, tile.z);

        currentRoom = null;
        currentStartRoom = null;

        IReadOnlyList<Room> rooms = dungeon.Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            Room room = rooms[i];
            if (!RoomContainsTile(room, flatTile))
            {
                continue;
            }

            currentRoom = room;
            visitedRoomIds.Add(room.id);
            return;
        }

        IReadOnlyList<StartRoom> startRooms = dungeon.GetAssignedStartRooms();
        for (int i = 0; i < startRooms.Count; i++)
        {
            StartRoom startRoom = startRooms[i];
            if (!BoundsContainsTile(startRoom.bounds, flatTile))
            {
                continue;
            }

            currentStartRoom = startRoom;
            visitedRoomIds.Add(GetStartRoomId(startRoom));
            return;
        }
    }

    private void DrawVisitedRooms()
    {
        HideAllRooms();

        IReadOnlyList<Room> rooms = dungeon.Rooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            Room room = rooms[i];
            if (!visitedRoomIds.Contains(room.id))
            {
                continue;
            }

            DrawRoom(room.id, room.bounds, room.type, room == currentRoom);
        }

        IReadOnlyList<StartRoom> startRooms = dungeon.GetAssignedStartRooms();
        for (int i = 0; i < startRooms.Count; i++)
        {
            StartRoom startRoom = startRooms[i];
            int id = GetStartRoomId(startRoom);
            if (!visitedRoomIds.Contains(id))
            {
                continue;
            }

            DrawRoom(id, startRoom.bounds, RoomType.Start, startRoom == currentStartRoom);
        }
    }

    private void DrawRoom(int id, BoundsInt bounds, RoomType type, bool isCurrent)
    {
        RoomView view = GetRoomView(id);
        view.Root.SetActive(true);

        Vector2 min = TileToMap(bounds.xMin, bounds.zMin);
        Vector2 max = TileToMap(bounds.xMax, bounds.zMax);
        Vector2 size = new Vector2(Mathf.Max(6f, Mathf.Abs(max.x - min.x)), Mathf.Max(6f, Mathf.Abs(max.y - min.y)));
        view.Rect.anchoredPosition = (min + max) * 0.5f;
        view.Rect.sizeDelta = size;

        Color fillColor = GetRoomColor(type, isCurrent);
        view.Fill.color = fillColor;
        view.Outline.effectColor = isCurrent ? new Color(1f, 0.82f, 0.38f, 0.95f) : new Color(0.08f, 0.1f, 0.11f, 0.9f);
        view.Outline.effectDistance = isCurrent ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
    }

    private RoomView GetRoomView(int id)
    {
        if (roomViews.TryGetValue(id, out RoomView existing))
        {
            return existing;
        }

        GameObject roomObject = CreateUIObject("Visited Room " + id, mapRoot);
        RectTransform rect = roomObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);

        Image fill = roomObject.AddComponent<Image>();
        fill.raycastTarget = false;
        Outline outline = roomObject.AddComponent<Outline>();
        outline.useGraphicAlpha = true;

        RoomView view = new RoomView(roomObject, rect, fill, outline);
        roomViews.Add(id, view);
        return view;
    }

    private void HideAllRooms()
    {
        foreach (KeyValuePair<int, RoomView> pair in roomViews)
        {
            pair.Value.Root.SetActive(false);
        }
    }

    private void DrawCurrentRoomMarkers()
    {
        BeginMarkers();

        if (currentRoom == null)
        {
            DrawMarker(player.position, new Color(0.95f, 0.98f, 1f, 1f), new Vector2(8f, 8f), 0f);
            EndMarkers();
            return;
        }

        DrawMarker(player.position, new Color(0.95f, 0.98f, 1f, 1f), new Vector2(8f, 8f), 0f);

        for (int i = 0; i < cachedMonsters.Length; i++)
        {
            Monster monster = cachedMonsters[i];
            if (monster == null || monster.State == null || monster.State.IsDead || !PositionIsInCurrentRoom(monster.transform.position))
            {
                continue;
            }

            DrawMarker(monster.transform.position, new Color(1f, 0.2f, 0.12f, 1f), new Vector2(9f, 9f), 0f);
        }

        for (int i = 0; i < cachedExits.Length; i++)
        {
            ExitPortal exit = cachedExits[i];
            if (exit == null || !PositionIsInCurrentRoom(exit.transform.position))
            {
                continue;
            }

            DrawMarker(exit.transform.position, new Color(0.15f, 1f, 0.68f, 1f), new Vector2(10f, 10f), 45f);
        }

        EndMarkers();
    }

    private void BeginMarkers()
    {
        markerUseCount = 0;
    }

    private void EndMarkers()
    {
        for (int i = markerUseCount; i < markerViews.Count; i++)
        {
            markerViews[i].gameObject.SetActive(false);
        }
    }

    private void DrawMarker(Vector3 worldPosition, Color color, Vector2 size, float rotation)
    {
        if (dungeon == null)
        {
            return;
        }

        Image marker = GetMarker();
        RectTransform rect = marker.GetComponent<RectTransform>();
        Vector3Int tile = dungeon.WorldToTile(worldPosition);
        rect.anchoredPosition = TileToMap(tile.x + 0.5f, tile.z + 0.5f);
        rect.sizeDelta = size;
        rect.localEulerAngles = new Vector3(0f, 0f, rotation);
        marker.color = color;
        marker.gameObject.SetActive(true);
    }

    private Image GetMarker()
    {
        if (markerUseCount < markerViews.Count)
        {
            Image marker = markerViews[markerUseCount];
            marker.transform.SetAsLastSibling();
            markerUseCount++;
            return marker;
        }

        GameObject markerObject = CreateUIObject("Map Marker", mapRoot);
        RectTransform rect = markerObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);

        Image image = markerObject.AddComponent<Image>();
        image.raycastTarget = false;
        AddOutline(image, new Color(0f, 0f, 0f, 0.7f), new Vector2(1f, -1f));
        markerViews.Add(image);
        image.transform.SetAsLastSibling();
        markerUseCount++;
        return image;
    }

    private bool PositionIsInCurrentRoom(Vector3 position)
    {
        if (currentRoom == null || dungeon == null)
        {
            return false;
        }

        Vector3Int tile = dungeon.WorldToTile(position);
        return RoomContainsTile(currentRoom, new Vector3Int(tile.x, 0, tile.z));
    }

    private bool RoomContainsTile(Room room, Vector3Int tile)
    {
        if (room == null)
        {
            return false;
        }

        if (room.floorTiles != null && room.floorTiles.Contains(tile))
        {
            return true;
        }

        return BoundsContainsTile(room.bounds, tile);
    }

    private static bool BoundsContainsTile(BoundsInt bounds, Vector3Int tile)
    {
        return tile.x >= bounds.xMin
            && tile.x < bounds.xMax
            && tile.z >= bounds.zMin
            && tile.z < bounds.zMax;
    }

    private Vector2 TileToMap(float tileX, float tileZ)
    {
        float width = Mathf.Max(1f, mapMaxX - mapMinX);
        float height = Mathf.Max(1f, mapMaxZ - mapMinZ);
        float scale = Mathf.Min((MapSize.x - 18f) / width, (MapSize.y - 18f) / height);
        float centerX = (mapMinX + mapMaxX) * 0.5f;
        float centerZ = (mapMinZ + mapMaxZ) * 0.5f;
        return new Vector2((tileX - centerX) * scale, (tileZ - centerZ) * scale);
    }

    private static Color GetRoomColor(RoomType type, bool isCurrent)
    {
        if (isCurrent)
        {
            return new Color(0.98f, 0.64f, 0.18f, 0.86f);
        }

        switch (type)
        {
            case RoomType.Start:
                return new Color(0.16f, 0.42f, 0.62f, 0.66f);
            case RoomType.Boss:
                return new Color(0.55f, 0.13f, 0.17f, 0.68f);
            case RoomType.Reward:
                return new Color(0.18f, 0.58f, 0.5f, 0.68f);
            case RoomType.Exit:
                return new Color(0.18f, 0.52f, 0.32f, 0.7f);
            default:
                return new Color(0.18f, 0.22f, 0.24f, 0.7f);
        }
    }

    private static int GetStartRoomId(StartRoom startRoom)
    {
        return -1000 - Mathf.Max(0, startRoom.slotIndex);
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject obj = new GameObject(objectName, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static void SetGradient(Image image, Color top, Color bottom)
    {
        GameUIGradient gradient = image.GetComponent<GameUIGradient>();
        if (gradient == null)
        {
            gradient = image.gameObject.AddComponent<GameUIGradient>();
        }

        gradient.SetColors(top, bottom);
    }

    private static void AddOutline(Graphic graphic, Color color, Vector2 distance)
    {
        Outline outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    private static void AddShadow(Graphic graphic, Color color, Vector2 distance)
    {
        Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private sealed class RoomView
    {
        public RoomView(GameObject root, RectTransform rect, Image fill, Outline outline)
        {
            Root = root;
            Rect = rect;
            Fill = fill;
            Outline = outline;
        }

        public GameObject Root { get; }
        public RectTransform Rect { get; }
        public Image Fill { get; }
        public Outline Outline { get; }
    }
}
