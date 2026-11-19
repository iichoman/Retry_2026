using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RoomObjectPlacerPresetBuilder
{
    private const string PropRoot = "Assets/GameObjects/Map/DungeonProps";

    private const int RoomNormal = 1;
    private const int RoomBoss = 1 << 1;
    private const int RoomReward = 1 << 2;
    private const int RoomExit = 1 << 3;
    private const int RoomStory = RoomNormal | RoomReward | RoomExit;
    private const int RoomSetpiece = RoomBoss | RoomReward | RoomExit;
    private const int RoomAll = RoomNormal | RoomBoss | RoomReward | RoomExit | (1 << 4);

    private const int ShapeSmall = 1;
    private const int ShapeNormal = 1 << 1;
    private const int ShapeLarge = 1 << 2;
    private const int ShapeLongWide = 1 << 3;
    private const int ShapeLongTall = 1 << 4;
    private const int ShapeLong = ShapeLongWide | ShapeLongTall;
    private const int ShapePlayable = ShapeNormal | ShapeLarge | ShapeLong;
    private const int ShapeAll = ShapeSmall | ShapeNormal | ShapeLarge | ShapeLongWide | ShapeLongTall;

    private const int LayoutOpen = 1;
    private const int LayoutFourPillars = 1 << 1;
    private const int LayoutCenterBlock = 1 << 2;
    private const int LayoutAll = LayoutOpen | LayoutFourPillars | LayoutCenterBlock;

    private const int DecorSmallClutter = 0;
    private const int DecorLargeObstacle = 1;
    private const int DecorCover = 2;
    private const int DecorCenterpiece = 3;
    private const int DecorCornerSet = 4;
    private const int DecorReward = 5;
    private const int DecorExit = 6;

    private const int DecorRandomOpen = 0;
    private const int DecorAwayFromWalls = 1;
    private const int DecorAlongWall = 3;
    private const int DecorCorner = 4;
    private const int DecorCenterCluster = 5;

    private const int WallTargetRooms = 1;
    private const int WallTargetCorridors = 1 << 2;
    private const int WallSegmentedEven = 2;
    private const int WallAllSides = 15;

    private static readonly DecorPreset[] DecorPresets =
    {
        new DecorPreset(
            label: "01 Small Room/Corner Story Props",
            primaryFolders: Folders("Room/Small/CornerStory"),
            fallbackFolders: Folders("Small"),
            category: DecorCornerSet,
            placementMode: DecorCorner,
            minPerRoom: 0,
            maxPerRoom: 1,
            footprintRadius: 1,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 2,
            roomTypes: RoomNormal,
            roomShapes: ShapeSmall,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 6),

        new DecorPreset(
            label: "01 Small Room/Readable Floor Detail",
            primaryFolders: Folders("Room/Small/FloorDetail"),
            fallbackFolders: Folders("Small"),
            category: DecorSmallClutter,
            placementMode: DecorRandomOpen,
            minPerRoom: 0,
            maxPerRoom: 1,
            footprintRadius: 1,
            wallClearanceTiles: 1,
            extraDoorwayExclusionRadius: 2,
            roomTypes: RoomNormal,
            roomShapes: ShapeSmall,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 6),

        new DecorPreset(
            label: "02 Normal Room/Side Dressing",
            primaryFolders: Folders("Room/Normal/SideDressing"),
            fallbackFolders: Folders("Small"),
            category: DecorSmallClutter,
            placementMode: DecorAlongWall,
            minPerRoom: 0,
            maxPerRoom: 2,
            footprintRadius: 1,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 3,
            roomTypes: RoomNormal,
            roomShapes: ShapeNormal | ShapeLong,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "02 Normal Room/Combat Cover",
            primaryFolders: Folders("Room/Normal/CombatCover"),
            fallbackFolders: Folders("Cover"),
            category: DecorCover,
            placementMode: DecorAwayFromWalls,
            minPerRoom: 0,
            maxPerRoom: 1,
            footprintRadius: 2,
            wallClearanceTiles: 1,
            extraDoorwayExclusionRadius: 4,
            roomTypes: RoomNormal,
            roomShapes: ShapePlayable,
            roomLayouts: LayoutOpen | LayoutFourPillars,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "03 Large Room/Centerpiece",
            primaryFolders: Folders("Room/Large/Centerpiece"),
            fallbackFolders: Folders("Large"),
            category: DecorCenterpiece,
            placementMode: DecorCenterCluster,
            minPerRoom: 0,
            maxPerRoom: 1,
            footprintRadius: 3,
            wallClearanceTiles: 3,
            extraDoorwayExclusionRadius: 5,
            roomTypes: RoomNormal,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutOpen,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 6),

        new DecorPreset(
            label: "03 Large Room/Side Set Dressing",
            primaryFolders: Folders("Room/Large/SideDressing"),
            fallbackFolders: Folders("Large"),
            category: DecorLargeObstacle,
            placementMode: DecorAlongWall,
            minPerRoom: 1,
            maxPerRoom: 2,
            footprintRadius: 2,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 4,
            roomTypes: RoomNormal,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "03 Large Room/Cover Islands",
            primaryFolders: Folders("Room/Large/CoverIslands"),
            fallbackFolders: Folders("Cover"),
            category: DecorCover,
            placementMode: DecorAwayFromWalls,
            minPerRoom: 0,
            maxPerRoom: 2,
            footprintRadius: 2,
            wallClearanceTiles: 2,
            extraDoorwayExclusionRadius: 5,
            roomTypes: RoomNormal,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutOpen | LayoutFourPillars,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "04 Long Room/Side Line Props",
            primaryFolders: Folders("Room/Long/SideLineProps"),
            fallbackFolders: Folders("Small"),
            category: DecorSmallClutter,
            placementMode: DecorAlongWall,
            minPerRoom: 1,
            maxPerRoom: 3,
            footprintRadius: 1,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 3,
            roomTypes: RoomNormal,
            roomShapes: ShapeLong,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "04 Long Room/Path Break Cover",
            primaryFolders: Folders("Room/Long/PathBreakCover"),
            fallbackFolders: Folders("Cover"),
            category: DecorCover,
            placementMode: DecorAwayFromWalls,
            minPerRoom: 0,
            maxPerRoom: 2,
            footprintRadius: 2,
            wallClearanceTiles: 1,
            extraDoorwayExclusionRadius: 4,
            roomTypes: RoomNormal,
            roomShapes: ShapeLong,
            roomLayouts: LayoutOpen | LayoutFourPillars,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "05 Pillar Layout/Corner Accents",
            primaryFolders: Folders("Room/Layout/FourPillars/CornerAccents"),
            fallbackFolders: Folders("Small"),
            category: DecorCornerSet,
            placementMode: DecorCorner,
            minPerRoom: 1,
            maxPerRoom: 2,
            footprintRadius: 1,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 3,
            roomTypes: RoomNormal,
            roomShapes: ShapePlayable,
            roomLayouts: LayoutFourPillars,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "05 Center Block Layout/Perimeter Props",
            primaryFolders: Folders("Room/Layout/CenterBlock/PerimeterProps"),
            fallbackFolders: Folders("Small"),
            category: DecorSmallClutter,
            placementMode: DecorAlongWall,
            minPerRoom: 1,
            maxPerRoom: 3,
            footprintRadius: 1,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 3,
            roomTypes: RoomNormal,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutCenterBlock,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "06 Boss Room/Combat Cover Ring",
            primaryFolders: Folders("Room/Boss/CombatCoverRing"),
            fallbackFolders: Folders("Cover"),
            category: DecorCover,
            placementMode: DecorAwayFromWalls,
            minPerRoom: 2,
            maxPerRoom: 4,
            footprintRadius: 2,
            wallClearanceTiles: 2,
            extraDoorwayExclusionRadius: 5,
            roomTypes: RoomBoss,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutOpen,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 12),

        new DecorPreset(
            label: "06 Boss Room/Scale Setpiece",
            primaryFolders: Folders("Room/Boss/ScaleSetpiece"),
            fallbackFolders: Folders("Large"),
            category: DecorCenterpiece,
            placementMode: DecorCenterCluster,
            minPerRoom: 0,
            maxPerRoom: 1,
            footprintRadius: 3,
            wallClearanceTiles: 3,
            extraDoorwayExclusionRadius: 6,
            roomTypes: RoomBoss,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutOpen,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 6),

        new DecorPreset(
            label: "07 Reward Room/Treasure Focus",
            primaryFolders: Folders("Room/Reward/TreasureFocus"),
            fallbackFolders: Folders("Reward"),
            category: DecorReward,
            placementMode: DecorCenterCluster,
            minPerRoom: 1,
            maxPerRoom: 2,
            footprintRadius: 2,
            wallClearanceTiles: 2,
            extraDoorwayExclusionRadius: 4,
            roomTypes: RoomReward,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 6),

        new DecorPreset(
            label: "07 Reward Room/Side Treasure Detail",
            primaryFolders: Folders("Room/Reward/SideTreasureDetail"),
            fallbackFolders: Folders("Reward", "Small"),
            category: DecorReward,
            placementMode: DecorAlongWall,
            minPerRoom: 1,
            maxPerRoom: 3,
            footprintRadius: 1,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 3,
            roomTypes: RoomReward,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8),

        new DecorPreset(
            label: "08 Exit Room/Portal Frame Dressing",
            primaryFolders: Folders("Room/Exit/PortalFrameDressing"),
            fallbackFolders: Folders("Exit"),
            category: DecorExit,
            placementMode: DecorCenterCluster,
            minPerRoom: 1,
            maxPerRoom: 1,
            footprintRadius: 3,
            wallClearanceTiles: 2,
            extraDoorwayExclusionRadius: 5,
            roomTypes: RoomExit,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: false,
            centerCandidatePoolSize: 6),

        new DecorPreset(
            label: "08 Exit Room/Ceremony Side Props",
            primaryFolders: Folders("Room/Exit/CeremonySideProps"),
            fallbackFolders: Folders("Exit", "Large"),
            category: DecorExit,
            placementMode: DecorAlongWall,
            minPerRoom: 1,
            maxPerRoom: 3,
            footprintRadius: 2,
            wallClearanceTiles: 0,
            extraDoorwayExclusionRadius: 4,
            roomTypes: RoomExit,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            randomYaw: true,
            faceRoomFromNearestWall: true,
            centerCandidatePoolSize: 8)
    };

    private static readonly WallPreset[] WallPresets =
    {
        new WallPreset(
            label: "10 Walls/Room Key Lighting",
            primaryFolders: Folders("Wall/RoomLighting"),
            fallbackFolders: Folders("Lighting"),
            placementTargets: WallTargetRooms,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 14,
            edgePaddingTiles: 4,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.15f,
            minSegmentLengthTiles: 5,
            segmentedDoorwayExclusionRadius: 3,
            corridorJunctionExclusionRadius: 0,
            wallPlacementExclusionRadius: 5,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomStory | RoomBoss,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 1.55f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "10 Walls/Upper Silhouette Decor",
            primaryFolders: Folders("Wall/UpperSilhouetteDecor"),
            fallbackFolders: Folders(),
            placementTargets: WallTargetRooms,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 13,
            edgePaddingTiles: 3,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.12f,
            minSegmentLengthTiles: 5,
            segmentedDoorwayExclusionRadius: 3,
            corridorJunctionExclusionRadius: 0,
            wallPlacementExclusionRadius: 4,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomStory,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 1.9f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "10 Walls/Large Room Banners",
            primaryFolders: Folders("Wall/LargeRoomBanners"),
            fallbackFolders: Folders(),
            placementTargets: WallTargetRooms,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 10,
            edgePaddingTiles: 4,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.1f,
            minSegmentLengthTiles: 6,
            segmentedDoorwayExclusionRadius: 3,
            corridorJunctionExclusionRadius: 0,
            wallPlacementExclusionRadius: 5,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomNormal,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 2.05f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "10 Walls/Reward Room Accents",
            primaryFolders: Folders("Wall/RewardRoomAccents"),
            fallbackFolders: Folders(),
            placementTargets: WallTargetRooms,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 8,
            edgePaddingTiles: 3,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.12f,
            minSegmentLengthTiles: 4,
            segmentedDoorwayExclusionRadius: 2,
            corridorJunctionExclusionRadius: 0,
            wallPlacementExclusionRadius: 4,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomReward,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 1.8f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "10 Walls/Exit Ritual Lights",
            primaryFolders: Folders("Wall/ExitRitualLights"),
            fallbackFolders: Folders(),
            placementTargets: WallTargetRooms,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 8,
            edgePaddingTiles: 3,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.15f,
            minSegmentLengthTiles: 4,
            segmentedDoorwayExclusionRadius: 2,
            corridorJunctionExclusionRadius: 0,
            wallPlacementExclusionRadius: 4,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomExit,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 1.6f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "10 Walls/Boss Scale Decor",
            primaryFolders: Folders("Wall/BossScaleDecor"),
            fallbackFolders: Folders(),
            placementTargets: WallTargetRooms,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 11,
            edgePaddingTiles: 4,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.12f,
            minSegmentLengthTiles: 6,
            segmentedDoorwayExclusionRadius: 4,
            corridorJunctionExclusionRadius: 0,
            wallPlacementExclusionRadius: 5,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomBoss,
            roomShapes: ShapeLarge,
            roomLayouts: LayoutOpen,
            positionOffset: new Vector3(0f, 2f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "11 Corridors/Guiding Lights",
            primaryFolders: Folders("Corridor/GuidingLights"),
            fallbackFolders: Folders("Corridor"),
            placementTargets: WallTargetCorridors,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 16,
            edgePaddingTiles: 4,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.14f,
            minSegmentLengthTiles: 6,
            segmentedDoorwayExclusionRadius: 0,
            corridorJunctionExclusionRadius: 6,
            wallPlacementExclusionRadius: 6,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomAll,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 1.45f, 0f),
            rotationOffset: Vector3.zero),

        new WallPreset(
            label: "11 Corridors/Sparse Wall Detail",
            primaryFolders: Folders("Corridor/SparseWallDetail"),
            fallbackFolders: Folders(),
            placementTargets: WallTargetCorridors,
            placementMode: WallSegmentedEven,
            floorExclusionRadius: 0,
            minPerRoom: 0,
            maxPerRoom: 1,
            spacingTiles: 20,
            edgePaddingTiles: 5,
            wallSides: WallAllSides,
            wallSurfaceOffset: 0.08f,
            minSegmentLengthTiles: 7,
            segmentedDoorwayExclusionRadius: 0,
            corridorJunctionExclusionRadius: 6,
            wallPlacementExclusionRadius: 7,
            placeBothCorridorSides: false,
            centerAlignSegmentPlacements: true,
            roomTypes: RoomAll,
            roomShapes: ShapeAll,
            roomLayouts: LayoutAll,
            positionOffset: new Vector3(0f, 1.65f, 0f),
            rotationOffset: Vector3.zero)
    };

    private static readonly string[] LegacyManagedLabels =
    {
        "Decor/Small Clutter",
        "Decor/Large Props",
        "Decor/Cover Props",
        "Decor/Corner Clutter",
        "Decor/Reward Dressing",
        "Decor/Exit Dressing",
        "Wall/Upper Decoration",
        "Wall/Corridor Decoration",
        "Wall/Room Lighting",
        "Wall/Corridor Lighting"
    };

    [MenuItem("Tools/Retry/Room Object Placer/Create Dungeon Prop Folders")]
    public static void CreateDungeonPropFolders()
    {
        EnsurePropFolders();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Dungeon prop folders are ready under {PropRoot}.");
    }

    [MenuItem("Tools/Retry/Room Object Placer/Create Default Presets")]
    public static void CreateDefaultPresets()
    {
        RoomObjectPlacer[] placers = FindTargetPlacers();
        if (placers.Length == 0)
        {
            Debug.LogWarning("RoomObjectPlacer was not found in the current scene.");
            return;
        }

        EnsurePropFolders();

        foreach (RoomObjectPlacer placer in placers)
        {
            Undo.RecordObject(placer, "Create Curated RoomObjectPlacer Presets");
            SerializedObject serializedPlacer = new SerializedObject(placer);
            RemoveManagedPresets(serializedPlacer);
            AddOrUpdateDecorPresets(serializedPlacer);
            AddOrUpdateWallPresets(serializedPlacer);
            serializedPlacer.ApplyModifiedProperties();
            EditorUtility.SetDirty(placer);
            EditorSceneManager.MarkSceneDirty(placer.gameObject.scene);
        }

        Debug.Log($"Created curated RoomObjectPlacer presets on {placers.Length} placer(s).");
    }

    [MenuItem("Tools/Retry/Room Object Placer/Register Props From Folders")]
    public static void RegisterPropsFromFolders()
    {
        RoomObjectPlacer[] placers = FindTargetPlacers();
        if (placers.Length == 0)
        {
            Debug.LogWarning("RoomObjectPlacer was not found in the current scene.");
            return;
        }

        EnsurePropFolders();
        Dictionary<string, List<GameObject>> prefabsByFolder = LoadPrefabsByFolder();

        foreach (RoomObjectPlacer placer in placers)
        {
            Undo.RecordObject(placer, "Register Dungeon Props");
            SerializedObject serializedPlacer = new SerializedObject(placer);
            RegisterDecorPrefabs(serializedPlacer, prefabsByFolder);
            RegisterWallPrefabs(serializedPlacer, prefabsByFolder);
            serializedPlacer.ApplyModifiedProperties();
            EditorUtility.SetDirty(placer);
            EditorSceneManager.MarkSceneDirty(placer.gameObject.scene);
        }

        Debug.Log($"Registered dungeon props from curated folders on {placers.Length} placer(s).");
    }

    [MenuItem("Tools/Retry/Room Object Placer/Setup Prop Workflow")]
    public static void SetupPropWorkflow()
    {
        CreateDungeonPropFolders();
        CreateDefaultPresets();
        RegisterPropsFromFolders();
    }

    private static string[] Folders(params string[] folders)
    {
        return folders;
    }

    private static RoomObjectPlacer[] FindTargetPlacers()
    {
        var selected = new List<RoomObjectPlacer>();
        foreach (GameObject gameObject in Selection.gameObjects)
        {
            if (gameObject != null && gameObject.TryGetComponent(out RoomObjectPlacer placer))
            {
                selected.Add(placer);
            }
        }

        if (selected.Count > 0)
        {
            return selected.ToArray();
        }

        return Object.FindObjectsByType<RoomObjectPlacer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private static void EnsurePropFolders()
    {
        EnsureFolder("Assets/GameObjects/Map");
        EnsureFolder(PropRoot);

        var folders = new HashSet<string>();
        AddFallbackCompatibilityFolders(folders);

        foreach (DecorPreset preset in DecorPresets)
        {
            AddFolders(folders, preset.PrimaryFolders);
            AddFolders(folders, preset.FallbackFolders);
        }

        foreach (WallPreset preset in WallPresets)
        {
            AddFolders(folders, preset.PrimaryFolders);
            AddFolders(folders, preset.FallbackFolders);
        }

        foreach (string folder in folders)
        {
            EnsureFolder($"{PropRoot}/{folder}");
        }
    }

    private static void AddFallbackCompatibilityFolders(HashSet<string> folders)
    {
        folders.Add("Small");
        folders.Add("Large");
        folders.Add("Cover");
        folders.Add("Wall");
        folders.Add("Reward");
        folders.Add("Exit");
        folders.Add("Corridor");
        folders.Add("Lighting");
        folders.Add("VFX");
    }

    private static void AddFolders(HashSet<string> folders, IEnumerable<string> sourceFolders)
    {
        foreach (string folder in sourceFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                folders.Add(folder);
            }
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static Dictionary<string, List<GameObject>> LoadPrefabsByFolder()
    {
        var result = new Dictionary<string, List<GameObject>>();
        var folders = new HashSet<string>();

        foreach (DecorPreset preset in DecorPresets)
        {
            AddFolders(folders, preset.PrimaryFolders);
            AddFolders(folders, preset.FallbackFolders);
        }

        foreach (WallPreset preset in WallPresets)
        {
            AddFolders(folders, preset.PrimaryFolders);
            AddFolders(folders, preset.FallbackFolders);
        }

        foreach (string folder in folders)
        {
            string folderPath = $"{PropRoot}/{folder}";
            string[] guids = AssetDatabase.IsValidFolder(folderPath)
                ? AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
                : new string[0];
            var prefabs = new List<GameObject>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            result[folder] = prefabs;
        }

        return result;
    }

    private static void RemoveManagedPresets(SerializedObject serializedPlacer)
    {
        RemoveManagedPresets(serializedPlacer.FindProperty("decorRules"), DecorPresets);
        RemoveManagedPresets(serializedPlacer.FindProperty("wallRules"), WallPresets);
    }

    private static void RemoveManagedPresets<TPreset>(SerializedProperty rules, IEnumerable<TPreset> presets)
        where TPreset : PresetBase
    {
        if (rules == null)
        {
            return;
        }

        var labels = new HashSet<string>(LegacyManagedLabels);
        foreach (TPreset preset in presets)
        {
            labels.Add(preset.Label);
        }

        for (int i = rules.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty labelProperty = rules.GetArrayElementAtIndex(i).FindPropertyRelative("label");
            if (labelProperty != null && labels.Contains(labelProperty.stringValue))
            {
                rules.DeleteArrayElementAtIndex(i);
            }
        }
    }

    private static void AddOrUpdateDecorPresets(SerializedObject serializedPlacer)
    {
        SerializedProperty rules = serializedPlacer.FindProperty("decorRules");
        if (rules == null)
        {
            return;
        }

        foreach (DecorPreset preset in DecorPresets)
        {
            SerializedProperty rule = GetOrAddRule(rules, preset.Label);
            ApplyDecorRule(rule, preset);
        }
    }

    private static void AddOrUpdateWallPresets(SerializedObject serializedPlacer)
    {
        SerializedProperty rules = serializedPlacer.FindProperty("wallRules");
        if (rules == null)
        {
            return;
        }

        foreach (WallPreset preset in WallPresets)
        {
            SerializedProperty rule = GetOrAddRule(rules, preset.Label);
            ApplyWallRule(rule, preset);
        }
    }

    private static void RegisterDecorPrefabs(SerializedObject serializedPlacer, Dictionary<string, List<GameObject>> prefabsByFolder)
    {
        SerializedProperty rules = serializedPlacer.FindProperty("decorRules");
        if (rules == null)
        {
            return;
        }

        foreach (DecorPreset preset in DecorPresets)
        {
            RegisterPrefabsForRule(rules, preset, prefabsByFolder);
        }
    }

    private static void RegisterWallPrefabs(SerializedObject serializedPlacer, Dictionary<string, List<GameObject>> prefabsByFolder)
    {
        SerializedProperty rules = serializedPlacer.FindProperty("wallRules");
        if (rules == null)
        {
            return;
        }

        foreach (WallPreset preset in WallPresets)
        {
            RegisterPrefabsForRule(rules, preset, prefabsByFolder);
        }
    }

    private static void RegisterPrefabsForRule(
        SerializedProperty rules,
        PresetBase preset,
        Dictionary<string, List<GameObject>> prefabsByFolder)
    {
        int index = FindRuleByLabel(rules, preset.Label);
        if (index < 0)
        {
            return;
        }

        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        SerializedProperty prefabList = rule.FindPropertyRelative("prefabs");
        if (prefabList == null)
        {
            return;
        }

        List<GameObject> prefabs = GetPrefabsForPreset(preset, prefabsByFolder);
        foreach (GameObject prefab in prefabs)
        {
            AddObjectIfMissing(prefabList, prefab);
        }
    }

    private static List<GameObject> GetPrefabsForPreset(PresetBase preset, Dictionary<string, List<GameObject>> prefabsByFolder)
    {
        List<GameObject> prefabs = CollectPrefabs(preset.PrimaryFolders, prefabsByFolder);
        if (prefabs.Count > 0)
        {
            return prefabs;
        }

        return CollectPrefabs(preset.FallbackFolders, prefabsByFolder);
    }

    private static List<GameObject> CollectPrefabs(IEnumerable<string> folders, Dictionary<string, List<GameObject>> prefabsByFolder)
    {
        var result = new List<GameObject>();
        var seen = new HashSet<GameObject>();

        foreach (string folder in folders)
        {
            if (!prefabsByFolder.TryGetValue(folder, out List<GameObject> prefabs))
            {
                continue;
            }

            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null && seen.Add(prefab))
                {
                    result.Add(prefab);
                }
            }
        }

        return result;
    }

    private static SerializedProperty GetOrAddRule(SerializedProperty rules, string label)
    {
        int existingIndex = FindRuleByLabel(rules, label);
        if (existingIndex >= 0)
        {
            return rules.GetArrayElementAtIndex(existingIndex);
        }

        int index = rules.arraySize;
        rules.InsertArrayElementAtIndex(index);
        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        ClearPrefabList(rule);
        SetString(rule, "label", label);
        return rule;
    }

    private static void ApplyDecorRule(SerializedProperty rule, DecorPreset preset)
    {
        ApplyBaseRule(rule, preset);
        SetInt(rule, "category", preset.Category);
        SetInt(rule, "placementMode", preset.PlacementMode);
        SetInt(rule, "footprintRadius", preset.FootprintRadius);
        SetInt(rule, "wallClearanceTiles", preset.WallClearanceTiles);
        SetInt(rule, "centerCandidatePoolSize", preset.CenterCandidatePoolSize);
        SetBool(rule, "randomYaw", preset.RandomYaw);
        SetBool(rule, "faceRoomFromNearestWall", preset.FaceRoomFromNearestWall);
    }

    private static void ApplyWallRule(SerializedProperty rule, WallPreset preset)
    {
        ApplyBaseRule(rule, preset);
        SetInt(rule, "placementTargets", preset.PlacementTargets);
        SetInt(rule, "placementMode", preset.PlacementMode);
        SetInt(rule, "floorExclusionRadius", preset.FloorExclusionRadius);
        SetInt(rule, "spacingTiles", preset.SpacingTiles);
        SetInt(rule, "edgePaddingTiles", preset.EdgePaddingTiles);
        SetInt(rule, "wallSides", preset.WallSides);
        SetFloat(rule, "wallSurfaceOffset", preset.WallSurfaceOffset);
        SetInt(rule, "minSegmentLengthTiles", preset.MinSegmentLengthTiles);
        SetInt(rule, "segmentedDoorwayExclusionRadius", preset.SegmentedDoorwayExclusionRadius);
        SetInt(rule, "corridorJunctionExclusionRadius", preset.CorridorJunctionExclusionRadius);
        SetInt(rule, "wallPlacementExclusionRadius", preset.WallPlacementExclusionRadius);
        SetBool(rule, "placeBothCorridorSides", preset.PlaceBothCorridorSides);
        SetBool(rule, "centerAlignSegmentPlacements", preset.CenterAlignSegmentPlacements);
    }

    private static void ApplyBaseRule(SerializedProperty rule, PresetBase preset)
    {
        SetString(rule, "label", preset.Label);
        SetBool(rule, "enabled", true);
        SetInt(rule, "minPerRoom", preset.MinPerRoom);
        SetInt(rule, "maxPerRoom", preset.MaxPerRoom);
        SetInt(rule, "roomTypes", preset.RoomTypes);
        SetInt(rule, "roomShapes", preset.RoomShapes);
        SetInt(rule, "roomLayouts", preset.RoomLayouts);
        SetVector3(rule, "positionOffset", preset.PositionOffset);
        SetVector3(rule, "rotationOffset", preset.RotationOffset);
        SetInt(rule, "extraDoorwayExclusionRadius", preset.ExtraDoorwayExclusionRadius);
    }

    private static int FindRuleByLabel(SerializedProperty rules, string label)
    {
        for (int i = 0; i < rules.arraySize; i++)
        {
            SerializedProperty labelProperty = rules.GetArrayElementAtIndex(i).FindPropertyRelative("label");
            if (labelProperty != null && labelProperty.stringValue == label)
            {
                return i;
            }
        }

        return -1;
    }

    private static void AddObjectIfMissing(SerializedProperty list, GameObject value)
    {
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == value)
            {
                return;
            }
        }

        int index = list.arraySize;
        list.InsertArrayElementAtIndex(index);
        list.GetArrayElementAtIndex(index).objectReferenceValue = value;
    }

    private static void ClearPrefabList(SerializedProperty rule)
    {
        SerializedProperty prefabList = rule.FindPropertyRelative("prefabs");
        if (prefabList != null)
        {
            prefabList.ClearArray();
        }
    }

    private static void SetString(SerializedProperty root, string propertyName, string value)
    {
        SerializedProperty property = root.FindPropertyRelative(propertyName);
        if (property != null)
        {
            property.stringValue = value;
        }
    }

    private static void SetBool(SerializedProperty root, string propertyName, bool value)
    {
        SerializedProperty property = root.FindPropertyRelative(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }

    private static void SetInt(SerializedProperty root, string propertyName, int value)
    {
        SerializedProperty property = root.FindPropertyRelative(propertyName);
        if (property != null)
        {
            property.intValue = value;
        }
    }

    private static void SetFloat(SerializedProperty root, string propertyName, float value)
    {
        SerializedProperty property = root.FindPropertyRelative(propertyName);
        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static void SetVector3(SerializedProperty root, string propertyName, Vector3 value)
    {
        SerializedProperty property = root.FindPropertyRelative(propertyName);
        if (property != null)
        {
            property.vector3Value = value;
        }
    }

    private abstract class PresetBase
    {
        protected PresetBase(
            string label,
            string[] primaryFolders,
            string[] fallbackFolders,
            int minPerRoom,
            int maxPerRoom,
            int extraDoorwayExclusionRadius,
            int roomTypes,
            int roomShapes,
            int roomLayouts,
            Vector3 positionOffset,
            Vector3 rotationOffset)
        {
            Label = label;
            PrimaryFolders = primaryFolders;
            FallbackFolders = fallbackFolders;
            MinPerRoom = minPerRoom;
            MaxPerRoom = maxPerRoom;
            ExtraDoorwayExclusionRadius = extraDoorwayExclusionRadius;
            RoomTypes = roomTypes;
            RoomShapes = roomShapes;
            RoomLayouts = roomLayouts;
            PositionOffset = positionOffset;
            RotationOffset = rotationOffset;
        }

        public string Label { get; }
        public string[] PrimaryFolders { get; }
        public string[] FallbackFolders { get; }
        public int MinPerRoom { get; }
        public int MaxPerRoom { get; }
        public int ExtraDoorwayExclusionRadius { get; }
        public int RoomTypes { get; }
        public int RoomShapes { get; }
        public int RoomLayouts { get; }
        public Vector3 PositionOffset { get; }
        public Vector3 RotationOffset { get; }
    }

    private sealed class DecorPreset : PresetBase
    {
        public DecorPreset(
            string label,
            string[] primaryFolders,
            string[] fallbackFolders,
            int category,
            int placementMode,
            int minPerRoom,
            int maxPerRoom,
            int footprintRadius,
            int wallClearanceTiles,
            int extraDoorwayExclusionRadius,
            int roomTypes,
            int roomShapes,
            int roomLayouts,
            bool randomYaw,
            bool faceRoomFromNearestWall,
            int centerCandidatePoolSize)
            : base(label, primaryFolders, fallbackFolders, minPerRoom, maxPerRoom, extraDoorwayExclusionRadius, roomTypes, roomShapes, roomLayouts, Vector3.zero, Vector3.zero)
        {
            Category = category;
            PlacementMode = placementMode;
            FootprintRadius = footprintRadius;
            WallClearanceTiles = wallClearanceTiles;
            RandomYaw = randomYaw;
            FaceRoomFromNearestWall = faceRoomFromNearestWall;
            CenterCandidatePoolSize = centerCandidatePoolSize;
        }

        public int Category { get; }
        public int PlacementMode { get; }
        public int FootprintRadius { get; }
        public int WallClearanceTiles { get; }
        public bool RandomYaw { get; }
        public bool FaceRoomFromNearestWall { get; }
        public int CenterCandidatePoolSize { get; }
    }

    private sealed class WallPreset : PresetBase
    {
        public WallPreset(
            string label,
            string[] primaryFolders,
            string[] fallbackFolders,
            int placementTargets,
            int placementMode,
            int floorExclusionRadius,
            int minPerRoom,
            int maxPerRoom,
            int spacingTiles,
            int edgePaddingTiles,
            int wallSides,
            float wallSurfaceOffset,
            int minSegmentLengthTiles,
            int segmentedDoorwayExclusionRadius,
            int corridorJunctionExclusionRadius,
            int wallPlacementExclusionRadius,
            bool placeBothCorridorSides,
            bool centerAlignSegmentPlacements,
            int roomTypes,
            int roomShapes,
            int roomLayouts,
            Vector3 positionOffset,
            Vector3 rotationOffset)
            : base(label, primaryFolders, fallbackFolders, minPerRoom, maxPerRoom, segmentedDoorwayExclusionRadius, roomTypes, roomShapes, roomLayouts, positionOffset, rotationOffset)
        {
            PlacementTargets = placementTargets;
            PlacementMode = placementMode;
            FloorExclusionRadius = floorExclusionRadius;
            SpacingTiles = spacingTiles;
            EdgePaddingTiles = edgePaddingTiles;
            WallSides = wallSides;
            WallSurfaceOffset = wallSurfaceOffset;
            MinSegmentLengthTiles = minSegmentLengthTiles;
            SegmentedDoorwayExclusionRadius = segmentedDoorwayExclusionRadius;
            CorridorJunctionExclusionRadius = corridorJunctionExclusionRadius;
            WallPlacementExclusionRadius = wallPlacementExclusionRadius;
            PlaceBothCorridorSides = placeBothCorridorSides;
            CenterAlignSegmentPlacements = centerAlignSegmentPlacements;
        }

        public int PlacementTargets { get; }
        public int PlacementMode { get; }
        public int FloorExclusionRadius { get; }
        public int SpacingTiles { get; }
        public int EdgePaddingTiles { get; }
        public int WallSides { get; }
        public float WallSurfaceOffset { get; }
        public int MinSegmentLengthTiles { get; }
        public int SegmentedDoorwayExclusionRadius { get; }
        public int CorridorJunctionExclusionRadius { get; }
        public int WallPlacementExclusionRadius { get; }
        public bool PlaceBothCorridorSides { get; }
        public bool CenterAlignSegmentPlacements { get; }
    }
}
