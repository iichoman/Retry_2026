using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class RoomObjectPlacer2 : MonoBehaviour
{
    public event Action<RoomObjectPlacer2> RoomObjectsPlaced;

    [SerializeField] private DungeonGenerator_ChunkMesh dungeonGenerator;

    [SerializeField] private bool placeOnDungeonGenerated = true;
    [SerializeField] private int placementSeedOffset = 81023;
    [SerializeField] private bool createDefaultTemplatesIfEmpty = true;
    [SerializeField, Range(0.5f, 2f)] private float floorDecorationDensity = 1.15f;
    [SerializeField, Range(0.5f, 2f)] private float wallDecorationDensity = 1.25f;
    [SerializeField, Range(0.5f, 2f)] private float ceilingDecorationDensity = 1.2f;

    [SerializeField] private float floorSurfaceHeight = 1f;
    [SerializeField] private bool alignFloorObjectBottomToSurface = true;
    [SerializeField] private float defaultWallSurfaceOffset = 0.12f;
    [SerializeField] private float wallLightingHeightOffset = 0.3f;
    [SerializeField] private float wallLightingYawOffset = 180f;
    [SerializeField] private float defaultCeilingHangOffset = 0.35f;

    [SerializeField] private PropLibrary propLibrary = new PropLibrary();

    [SerializeField] private List<RoomTemplate> roomTemplates = new List<RoomTemplate>();

    [SerializeField] private List<WallSlot> corridorWallSlots = new List<WallSlot>();

    private const string RootName = "RoomObjects2";

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly HashSet<Vector3Int> occupiedFloorTiles = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> occupiedWallTiles = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> occupiedCeilingTiles = new HashSet<Vector3Int>();

    private static readonly Vector3Int[] HorizontalDirections =
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.forward,
        Vector3Int.back
    };

    public bool PlaceOnDungeonGenerated => placeOnDungeonGenerated;

    private void Reset()
    {
        dungeonGenerator = GetComponent<DungeonGenerator_ChunkMesh>();
        if (createDefaultTemplatesIfEmpty && roomTemplates.Count == 0 && corridorWallSlots.Count == 0)
        {
            CreateDefaultTemplates();
        }
        else if (createDefaultTemplatesIfEmpty)
        {
            EnsureDefaultStartRoomTemplate();
        }
    }

    private void OnEnable()
    {
        if (dungeonGenerator == null)
        {
            dungeonGenerator = GetComponent<DungeonGenerator_ChunkMesh>();
        }

        if (createDefaultTemplatesIfEmpty && roomTemplates.Count == 0 && corridorWallSlots.Count == 0)
        {
            CreateDefaultTemplates();
        }
        else if (createDefaultTemplatesIfEmpty)
        {
            EnsureDefaultStartRoomTemplate();
        }

        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated += HandleDungeonGenerated;
        }
    }

    private void OnDisable()
    {
        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated -= HandleDungeonGenerated;
        }
    }

    private void HandleDungeonGenerated(DungeonGenerator_ChunkMesh generator)
    {
        if (placeOnDungeonGenerated)
        {
            PlaceRoomObjects();
        }
    }

    [ContextMenu("Place Room Objects 2")]
    public void PlaceRoomObjects()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogWarning("RoomObjectPlacer2 requires a DungeonGenerator_ChunkMesh reference.", this);
            return;
        }

        ClearPlacedObjects();

        var random = new System.Random(dungeonGenerator.seed + placementSeedOffset);
        Transform root = GetOrCreateRoot();

        foreach (Room room in dungeonGenerator.Rooms)
        {
            RoomTemplate template = PickTemplate(room, random);
            if (template == null)
            {
                continue;
            }

            PlaceFloorSlots(room, template, random, root);
            PlaceWallSlots(room, template, random, root);
            PlaceCeilingSlots(room, template, random, root);
        }

        PlaceStartRooms(random, root);
        PlaceCorridorWallSlots(random, root);
        RoomObjectsPlaced?.Invoke(this);
    }

    [ContextMenu("Clear Room Objects 2")]
    public void ClearPlacedObjects()
    {
        spawnedObjects.Clear();
        occupiedFloorTiles.Clear();
        occupiedWallTiles.Clear();
        occupiedCeilingTiles.Clear();

        Transform root = transform.Find(RootName);
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyObject(root.GetChild(i).gameObject);
        }
    }

    [ContextMenu("Create Default Room Templates")]
    private void CreateDefaultTemplatesFromContext()
    {
        CreateDefaultTemplates();
    }

    private void CreateDefaultTemplates()
    {
        roomTemplates = new List<RoomTemplate>
        {
            CreateSmallRoomTemplate(),
            CreateNormalRoomTemplate(),
            CreateLargeHallTemplate(),
            CreateLongGalleryTemplate(),
            CreateFourPillarTemplate(),
            CreateCenterBlockTemplate(),
            CreateBossArenaTemplate(),
            CreateRewardVaultTemplate(),
            CreateExitRitualTemplate(),
            CreateStartRoomTemplate()
        };

        corridorWallSlots = new List<WallSlot>
        {
            new WallSlot
            {
                label = "복도 - 길잡이 조명 리듬",
                propCategories = Categories(PropCategory.벽조명),
                pattern = WallPlacementPattern.EvenRhythm,
                prefabs = new List<GameObject>(),
                minCount = 0,
                maxCount = 99,
                spacingTiles = 16,
                edgePaddingTiles = 5,
                wallHeight = 1.45f,
                wallSurfaceOffset = 0.14f,
                wallOccupyRadius = 6,
                doorwayClearanceTiles = 6,
                placeBothSides = false
            },
            new WallSlot
            {
                label = "복도 - 드문드문 벽 장식",
                propCategories = Categories(PropCategory.벽장식, PropCategory.벽배너),
                pattern = WallPlacementPattern.EvenRhythm,
                prefabs = new List<GameObject>(),
                minCount = 0,
                maxCount = 99,
                spacingTiles = 18,
                edgePaddingTiles = 6,
                wallHeight = 1.75f,
                wallSurfaceOffset = 0.08f,
                wallOccupyRadius = 8,
                doorwayClearanceTiles = 6,
                placeBothSides = false
            }
        };
    }

    private void EnsureDefaultStartRoomTemplate()
    {
        foreach (RoomTemplate template in roomTemplates)
        {
            if (template != null && (template.roomTypes & RoomTypeMask.Start) != 0)
            {
                return;
            }
        }

        roomTemplates.Add(CreateStartRoomTemplate());
    }

    private static List<PropCategory> Categories(params PropCategory[] categories)
    {
        return new List<PropCategory>(categories);
    }

    private static RoomTemplate CreateSmallRoomTemplate()
    {
        return new RoomTemplate
        {
            label = "작은 방 - 동선형 루팅 구석",
            weight = 8,
            roomTypes = RoomTypeMask.Normal,
            roomShapes = RoomShapeMask.Small,
            roomLayouts = RoomLayoutMask.All,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "Corner story props",
                    propCategories = Categories(PropCategory.작은잡동사니),
                    pattern = FloorPlacementPattern.CornerClusters,
                    minCount = 1,
                    maxCount = 3,
                    footprintRadius = 1,
                    doorwayClearanceTiles = 3,
                    roomFacingOffset = 0.15f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "작은 바닥 디테일: 흩어진 소품",
                    propCategories = Categories(PropCategory.작은잡동사니),
                    pattern = FloorPlacementPattern.RandomOpen,
                    minCount = 0,
                    maxCount = 2,
                    footprintRadius = 1,
                    wallClearanceTiles = 1,
                    doorwayClearanceTiles = 2
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("단일 벽 조명 또는 램프", WallPlacementPattern.LongestWallCenter, 1, 1, 1.45f, 0.14f),
                CreateWallDecorSlot("작은 벽 장식: 액자, 사슬, 명판", WallPlacementPattern.RandomSegmentCenter, 1, 2, 1.85f)
            },
            ceilingSlots = new List<CeilingSlot>()
        };
    }

    private static RoomTemplate CreateNormalRoomTemplate()
    {
        return new RoomTemplate
        {
            label = "Normal Room - Combat Dressing",
            weight = 10,
            roomTypes = RoomTypeMask.Normal,
            roomShapes = RoomShapeMask.Normal | RoomShapeMask.LongWide | RoomShapeMask.LongTall,
            roomLayouts = RoomLayoutMask.Open,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "벽가 연출: 책장, 책상, 상자 더미",
                    propCategories = Categories(PropCategory.책장, PropCategory.책상테이블, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 1,
                    maxCount = 3,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.3f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "전투 엄폐물: 바위, 잔해, 낮은 상자",
                    propCategories = Categories(PropCategory.전투엄폐물, PropCategory.바위잔해, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.CombatCoverRing,
                    minCount = 0,
                    maxCount = 3,
                    footprintRadius = 2,
                    wallClearanceTiles = 2,
                    doorwayClearanceTiles = 5
                },
                new FloorSlot
                {
                    label = "동선용 소품: 책, 병, 부서진 소품",
                    propCategories = Categories(PropCategory.작은잡동사니),
                    pattern = FloorPlacementPattern.CornerClusters,
                    minCount = 1,
                    maxCount = 3,
                    footprintRadius = 1,
                    doorwayClearanceTiles = 3,
                    roomFacingOffset = 0.15f,
                    faceNearestWall = true
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("벽 조명 리듬", WallPlacementPattern.EvenRhythm, 1, 99, 1.45f, 0.14f),
                CreateWallDecorSlot("상단 벽 장식: 배너, 방패, 사슬", WallPlacementPattern.PairOnLongestWall, 1, 3, 1.95f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("선택 중앙 샹들리에", CeilingPlacementPattern.Center, 0, 1, 0.5f)
            }
        };
    }

    private static RoomTemplate CreateLargeHallTemplate()
    {
        return new RoomTemplate
        {
            label = "큰 방 - 세트피스 홀",
            weight = 10,
            roomTypes = RoomTypeMask.Normal,
            roomShapes = RoomShapeMask.Large,
            roomLayouts = RoomLayoutMask.Open,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "중앙 장식: 석상, 제단, 큰 바위, 분수",
                    propCategories = Categories(PropCategory.대형중앙장식, PropCategory.바위잔해),
                    pattern = FloorPlacementPattern.Centerpiece,
                    minCount = 1,
                    maxCount = 1,
                    footprintRadius = 4,
                    wallClearanceTiles = 4,
                    doorwayClearanceTiles = 6,
                    candidatePoolSize = 12
                },
                new FloorSlot
                {
                    label = "벽가 세트 연출: 책장, 테이블, 상자",
                    propCategories = Categories(PropCategory.책장, PropCategory.책상테이블, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 2,
                    maxCount = 5,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 5,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "엄폐 및 큰 바위, 부서진 기둥, 상자 더미",
                    propCategories = Categories(PropCategory.전투엄폐물, PropCategory.바위잔해, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.CombatCoverRing,
                    minCount = 2,
                    maxCount = 4,
                    footprintRadius = 2,
                    wallClearanceTiles = 3,
                    doorwayClearanceTiles = 6
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("큰 방 균등 조명", WallPlacementPattern.EvenRhythm, 1, 99, 1.5f, 0.14f),
                CreateWallDecorSlot("대형 배너와 높은 벽 장식", WallPlacementPattern.PairOnLongestWall, 2, 4, 2.05f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("중앙 샹들리에", CeilingPlacementPattern.Center, 0, 1, 0.6f),
                CreateCeilingSlot("선택 천장 깃발", CeilingPlacementPattern.CenterLine, 1, 3, 0.2f, PropCategory.천장깃발천장식)
            }
        };
    }

    private static RoomTemplate CreateLongGalleryTemplate()
    {
        return new RoomTemplate
        {
            label = "긴 방 - 갤러리 통로",
            weight = 10,
            roomTypes = RoomTypeMask.Normal,
            roomShapes = RoomShapeMask.LongWide | RoomShapeMask.LongTall,
            roomLayouts = RoomLayoutMask.All,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "측면 라인 소품: 선반, 상자, 잔해",
                    propCategories = Categories(PropCategory.책장, PropCategory.책상테이블, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 2,
                    maxCount = 5,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "Path breaker props",
                    propCategories = Categories(PropCategory.전투엄폐물, PropCategory.바위잔해, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.PathBreakers,
                    minCount = 0,
                    maxCount = 3,
                    footprintRadius = 2,
                    wallClearanceTiles = 2,
                    doorwayClearanceTiles = 5
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("갤러리 길잡이 조명", WallPlacementPattern.EvenRhythm, 1, 99, 1.45f, 0.14f),
                CreateWallDecorSlot("갤러리 벽 배너", WallPlacementPattern.EvenRhythm, 1, 99, 1.9f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("천장 라인 배너 또는 샹들리에", CeilingPlacementPattern.CenterLine, 1, 3, 0.4f, PropCategory.천장깃발천장식, PropCategory.샹들리에천장조명)
            }
        };
    }

    private static RoomTemplate CreateFourPillarTemplate()
    {
        return new RoomTemplate
        {
            label = "Layout - Four Pillar Chamber",
            weight = 14,
            roomTypes = RoomTypeMask.Normal,
            roomShapes = RoomShapeMask.Normal | RoomShapeMask.Large | RoomShapeMask.LongWide | RoomShapeMask.LongTall,
            roomLayouts = RoomLayoutMask.FourPillars,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "Pillar corner accents",
                    propCategories = Categories(PropCategory.작은잡동사니),
                    pattern = FloorPlacementPattern.CornerClusters,
                    minCount = 2,
                    maxCount = 5,
                    footprintRadius = 1,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.15f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "내부 측면 소품: 테이블, 상자, 선반",
                    propCategories = Categories(PropCategory.책장, PropCategory.책상테이블, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 1,
                    maxCount = 4,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("기둥 및 벽 조명", WallPlacementPattern.EvenRhythm, 1, 99, 1.5f, 0.14f),
                CreateWallDecorSlot("기둥 방 높은 벽 장식", WallPlacementPattern.PairOnLongestWall, 1, 3, 1.95f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("기둥 사이 중앙 샹들리에", CeilingPlacementPattern.Center, 1, 1, 0.55f)
            }
        };
    }

    private static RoomTemplate CreateCenterBlockTemplate()
    {
        return new RoomTemplate
        {
            label = "레이아웃 - 중앙 블록 내부",
            weight = 14,
            roomTypes = RoomTypeMask.Normal,
            roomShapes = RoomShapeMask.Large,
            roomLayouts = RoomLayoutMask.CenterBlock,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "중앙 블록 주변 내부 소품",
                    propCategories = Categories(PropCategory.책장, PropCategory.책상테이블, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 2,
                    maxCount = 5,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "Outer combat cover",
                    propCategories = Categories(PropCategory.전투엄폐물, PropCategory.바위잔해, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.PathBreakers,
                    minCount = 1,
                    maxCount = 3,
                    footprintRadius = 2,
                    wallClearanceTiles = 2,
                    doorwayClearanceTiles = 5
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("내부 벽 조명", WallPlacementPattern.EvenRhythm, 1, 99, 1.5f, 0.14f),
                CreateWallDecorSlot("내부 높은 벽 장식", WallPlacementPattern.PairOnLongestWall, 1, 3, 1.9f)
            },
            ceilingSlots = new List<CeilingSlot>()
        };
    }

    private static RoomTemplate CreateBossArenaTemplate()
    {
        return new RoomTemplate
        {
            label = "Boss Room - Arena Dressing",
            weight = 20,
            roomTypes = RoomTypeMask.Boss,
            roomShapes = RoomShapeMask.Large,
            roomLayouts = RoomLayoutMask.Open,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "Boss arena scale setpiece",
                    propCategories = Categories(PropCategory.보스방소품, PropCategory.대형중앙장식, PropCategory.바위잔해),
                    pattern = FloorPlacementPattern.Centerpiece,
                    minCount = 1,
                    maxCount = 1,
                    footprintRadius = 4,
                    wallClearanceTiles = 5,
                    doorwayClearanceTiles = 8,
                    candidatePoolSize = 10
                },
                new FloorSlot
                {
                    label = "Boss combat cover ring",
                    propCategories = Categories(PropCategory.전투엄폐물, PropCategory.바위잔해),
                    pattern = FloorPlacementPattern.CombatCoverRing,
                    minCount = 4,
                    maxCount = 6,
                    footprintRadius = 2,
                    wallClearanceTiles = 3,
                    doorwayClearanceTiles = 7
                },
                new FloorSlot
                {
                    label = "보스방 측면 위협 소품",
                    propCategories = Categories(PropCategory.보스방소품, PropCategory.바위잔해, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 3,
                    maxCount = 5,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 6,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("보스 아레나 강한 조명", WallPlacementPattern.EvenRhythm, 2, 99, 1.55f, 0.16f),
                CreateWallDecorSlot("보스방 대형 배너, 사슬, 트로피 장식", WallPlacementPattern.EvenRhythm, 2, 99, 2.1f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("보스 아레나 샹들리에 또는 매달린 장식", CeilingPlacementPattern.Center, 1, 1, 0.7f)
            }
        };
    }

    private static RoomTemplate CreateRewardVaultTemplate()
    {
        return new RoomTemplate
        {
            label = "보상방 - 보물 금고",
            weight = 20,
            roomTypes = RoomTypeMask.Reward,
            roomShapes = RoomShapeMask.All,
            roomLayouts = RoomLayoutMask.All,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "보물 중심 장식: 상자, 금화 더미, 고급 아이템 받침대",
                    propCategories = Categories(PropCategory.보물보상),
                    pattern = FloorPlacementPattern.Centerpiece,
                    minCount = 1,
                    maxCount = 2,
                    footprintRadius = 2,
                    wallClearanceTiles = 2,
                    doorwayClearanceTiles = 5,
                    candidatePoolSize = 8
                },
                new FloorSlot
                {
                    label = "측면 보물 디테일: 작은 상자, 자루, 잡동사니",
                    propCategories = Categories(PropCategory.보물보상, PropCategory.작은잡동사니),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 2,
                    maxCount = 5,
                    footprintRadius = 1,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.2f,
                    faceNearestWall = true
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("보상방 따뜻한 조명", WallPlacementPattern.PairOnLongestWall, 1, 2, 1.45f, 0.14f),
                CreateWallDecorSlot("보상방 벽 장식", WallPlacementPattern.PairOnLongestWall, 1, 3, 1.8f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("보상방 샹들리에", CeilingPlacementPattern.Center, 1, 1, 0.55f)
            }
        };
    }

    private static RoomTemplate CreateExitRitualTemplate()
    {
        return new RoomTemplate
        {
            label = "탈출방 - 의식 포탈",
            weight = 20,
            roomTypes = RoomTypeMask.Exit,
            roomShapes = RoomShapeMask.All,
            roomLayouts = RoomLayoutMask.All,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "포탈 아레나 연출: 포탈 받침, 의식용 제단",
                    propCategories = Categories(PropCategory.탈출의식소품, PropCategory.대형중앙장식),
                    pattern = FloorPlacementPattern.Centerpiece,
                    minCount = 1,
                    maxCount = 1,
                    footprintRadius = 4,
                    wallClearanceTiles = 3,
                    doorwayClearanceTiles = 6,
                    candidatePoolSize = 8
                },
                new FloorSlot
                {
                    label = "의식 측면 소품: 수정, 촛불, 석상",
                    propCategories = Categories(PropCategory.탈출의식소품, PropCategory.대형중앙장식, PropCategory.작은잡동사니),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 3,
                    maxCount = 5,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 5,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("탈출방 의식 조명", WallPlacementPattern.EvenRhythm, 2, 99, 1.5f, 0.16f),
                CreateWallDecorSlot("탈출방 의식 배너와 문양", WallPlacementPattern.PairOnLongestWall, 2, 4, 1.9f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("탈출방 중앙 천장 장식", CeilingPlacementPattern.Center, 1, 1, 0.65f)
            }
        };
    }

    private static RoomTemplate CreateStartRoomTemplate()
    {
        return new RoomTemplate
        {
            label = "시작방 - 모험 준비 거점",
            weight = 30,
            roomTypes = RoomTypeMask.Start,
            roomShapes = RoomShapeMask.All,
            roomLayouts = RoomLayoutMask.All,
            floorSlots = new List<FloorSlot>
            {
                new FloorSlot
                {
                    label = "시작방 중앙 분위기 소품: 탁자, 제단, 캠프 장식",
                    propCategories = Categories(PropCategory.책상테이블, PropCategory.대형중앙장식, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.Centerpiece,
                    minCount = 1,
                    maxCount = 1,
                    footprintRadius = 3,
                    wallClearanceTiles = 4,
                    doorwayClearanceTiles = 5,
                    candidatePoolSize = 10
                },
                new FloorSlot
                {
                    label = "시작방 벽가 준비물: 책장, 상자, 보급품",
                    propCategories = Categories(PropCategory.책장, PropCategory.책상테이블, PropCategory.상자크레이트),
                    pattern = FloorPlacementPattern.SideDressing,
                    minCount = 3,
                    maxCount = 5,
                    footprintRadius = 2,
                    doorwayClearanceTiles = 5,
                    roomFacingOffset = 0.35f,
                    faceNearestWall = true
                },
                new FloorSlot
                {
                    label = "시작방 작은 생활감 소품",
                    propCategories = Categories(PropCategory.작은잡동사니, PropCategory.바위잔해),
                    pattern = FloorPlacementPattern.CornerClusters,
                    minCount = 2,
                    maxCount = 4,
                    footprintRadius = 1,
                    doorwayClearanceTiles = 4,
                    roomFacingOffset = 0.15f,
                    faceNearestWall = true
                }
            },
            wallSlots = new List<WallSlot>
            {
                CreateRoomTorchSlot("시작방 따뜻한 벽 조명", WallPlacementPattern.EvenRhythm, 2, 99, 1.55f, 0.14f),
                CreateWallDecorSlot("시작방 안내 배너와 벽 장식", WallPlacementPattern.PairOnLongestWall, 2, 4, 1.95f)
            },
            ceilingSlots = new List<CeilingSlot>
            {
                CreateCeilingSlot("시작방 중앙 샹들리에", CeilingPlacementPattern.Center, 1, 1, 0.55f),
                CreateCeilingSlot("시작방 천장 깃발", CeilingPlacementPattern.CenterLine, 0, 2, 0.3f, PropCategory.천장깃발천장식)
            }
        };
    }

    private static WallSlot CreateRoomTorchSlot(string label, WallPlacementPattern pattern, int minCount, int maxCount, float height, float surfaceOffset)
    {
        return new WallSlot
        {
            label = label,
            propCategories = Categories(PropCategory.벽조명),
            pattern = pattern,
            prefabs = new List<GameObject>(),
            minCount = minCount,
            maxCount = maxCount,
            spacingTiles = 10,
            edgePaddingTiles = 3,
            wallHeight = height,
            wallSurfaceOffset = surfaceOffset,
            wallOccupyRadius = 5,
            doorwayClearanceTiles = 4,
            placeBothSides = false
        };
    }

    private static WallSlot CreateWallDecorSlot(string label, WallPlacementPattern pattern, int minCount, int maxCount, float height)
    {
        return new WallSlot
        {
            label = label,
            propCategories = Categories(PropCategory.벽장식, PropCategory.벽배너),
            pattern = pattern,
            prefabs = new List<GameObject>(),
            minCount = minCount,
            maxCount = maxCount,
            spacingTiles = 10,
            edgePaddingTiles = 3,
            wallHeight = height,
            wallSurfaceOffset = 0.1f,
            wallOccupyRadius = 4,
            doorwayClearanceTiles = 4,
            placeBothSides = false
        };
    }

    private static CeilingSlot CreateCeilingSlot(
        string label,
        CeilingPlacementPattern pattern,
        int minCount,
        int maxCount,
        float hangOffset,
        params PropCategory[] categories)
    {
        return new CeilingSlot
        {
            label = label,
            propCategories = categories.Length > 0
                ? Categories(categories)
                : Categories(PropCategory.샹들리에천장조명),
            pattern = pattern,
            prefabs = new List<GameObject>(),
            minCount = minCount,
            maxCount = maxCount,
            footprintRadius = 3,
            doorwayClearanceTiles = 5,
            edgeInsetTiles = 3,
            hangOffset = hangOffset,
            randomYaw = true
        };
    }

    private RoomTemplate PickTemplate(Room room, System.Random random)
    {
        var candidates = new List<RoomTemplate>();
        int totalWeight = 0;

        foreach (RoomTemplate template in roomTemplates)
        {
            if (!template.CanPlaceIn(room))
            {
                continue;
            }

            candidates.Add(template);
            totalWeight += Mathf.Max(1, template.weight);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        int roll = random.Next(0, totalWeight);
        foreach (RoomTemplate template in candidates)
        {
            roll -= Mathf.Max(1, template.weight);
            if (roll < 0)
            {
                return template;
            }
        }

        return candidates[0];
    }

    private void PlaceFloorSlots(Room room, RoomTemplate template, System.Random random, Transform root)
    {
        foreach (FloorSlot slot in template.floorSlots)
        {
            if (!slot.CanPlace(random, propLibrary))
            {
                continue;
            }

            List<FloorCandidate> candidates = BuildFloorCandidates(room, slot, random);
            int targetCount = ResolveTargetCount(slot.GetCount(random), candidates.Count, GetFloorDecorationDensity(slot));
            int placedCount = 0;

            foreach (FloorCandidate candidate in candidates)
            {
                if (placedCount >= targetCount)
                {
                    break;
                }

                if (!IsValidFloorCandidate(room, candidate.tile, slot))
                {
                    continue;
                }

                GameObject prefab = slot.PickPrefab(random, propLibrary);
                if (prefab == null)
                {
                    break;
                }

                Quaternion rotation = GetFloorRotation(candidate, slot, random);
                Vector3 position = GetFloorSurfaceCenter(candidate.tile) +
                                   candidate.facingDirection * slot.roomFacingOffset +
                                   slot.positionOffset;
                GameObject spawnedObject = Spawn(prefab, position, rotation, root);
                AlignFloorObjectBottomToSurface(spawnedObject, position.y);
                OccupyFloorArea(candidate.tile, slot.footprintRadius);
                placedCount++;
            }
        }
    }

    private void PlaceWallSlots(Room room, RoomTemplate template, System.Random random, Transform root)
    {
        foreach (WallSlot slot in template.wallSlots)
        {
            if (!slot.CanPlace(random, propLibrary))
            {
                continue;
            }

            List<WallCandidate> candidates = BuildRoomWallCandidates(room, slot, random);
            int targetCount = ResolveTargetCount(slot.GetCount(random), candidates.Count, wallDecorationDensity);
            int placedCount = 0;

            foreach (WallCandidate candidate in candidates)
            {
                if (placedCount >= targetCount)
                {
                    break;
                }

                if (!IsValidWallCandidate(room, candidate, slot))
                {
                    continue;
                }

                GameObject prefab = slot.PickPrefab(random, propLibrary);
                if (prefab == null)
                {
                    break;
                }

                Spawn(prefab, GetWallPosition(candidate, slot), GetWallRotation(candidate, slot), root);
                OccupyWallArea(candidate.wallTile, slot.wallOccupyRadius);
                placedCount++;
            }
        }
    }

    private void PlaceCeilingSlots(Room room, RoomTemplate template, System.Random random, Transform root)
    {
        foreach (CeilingSlot slot in template.ceilingSlots)
        {
            if (!slot.CanPlace(random, propLibrary))
            {
                continue;
            }

            List<CeilingCandidate> candidates = BuildCeilingCandidates(room, slot, random);
            int targetCount = ResolveTargetCount(slot.GetCount(random), candidates.Count, GetCeilingDecorationDensity(slot));
            int placedCount = 0;

            foreach (CeilingCandidate candidate in candidates)
            {
                if (placedCount >= targetCount)
                {
                    break;
                }

                if (!IsValidCeilingCandidate(room, candidate.tile, slot))
                {
                    continue;
                }

                GameObject prefab = slot.PickPrefab(random, propLibrary);
                if (prefab == null)
                {
                    break;
                }

                Vector3 position = GetCeilingPosition(candidate.tile, slot);
                Quaternion rotation = slot.randomYaw
                    ? Quaternion.Euler(0f, (float)(random.NextDouble() * 360f), 0f) * Quaternion.Euler(slot.rotationOffset)
                    : Quaternion.Euler(slot.rotationOffset);

                Spawn(prefab, position, rotation, root);
                OccupyCeilingArea(candidate.tile, slot.footprintRadius);
                placedCount++;
            }
        }
    }

    private void PlaceStartRooms(System.Random random, Transform root)
    {
        IReadOnlyList<StartRoom> startRooms = dungeonGenerator.GetAssignedStartRooms();
        if (startRooms == null || startRooms.Count == 0)
        {
            return;
        }

        foreach (StartRoom startRoom in startRooms)
        {
            Room room = CreateStartRoomContext(startRoom);
            RoomTemplate template = PickTemplate(room, random);
            if (template == null)
            {
                continue;
            }

            PlaceFloorSlots(room, template, random, root);
            PlaceWallSlots(room, template, random, root);
            PlaceCeilingSlots(room, template, random, root);
        }
    }

    private void PlaceCorridorWallSlots(System.Random random, Transform root)
    {
        foreach (WallSlot slot in corridorWallSlots)
        {
            if (!slot.CanPlace(random, propLibrary))
            {
                continue;
            }

            List<WallCandidate> candidates = BuildCorridorWallCandidates(slot, random);
            int targetCount = ResolveTargetCount(slot.GetCount(random), candidates.Count, wallDecorationDensity);
            int placedCount = 0;

            foreach (WallCandidate candidate in candidates)
            {
                if (placedCount >= targetCount)
                {
                    break;
                }

                if (!IsValidCorridorWallCandidate(candidate, slot))
                {
                    continue;
                }

                GameObject prefab = slot.PickPrefab(random, propLibrary);
                if (prefab == null)
                {
                    break;
                }

                Spawn(prefab, GetWallPosition(candidate, slot), GetWallRotation(candidate, slot), root);
                OccupyWallArea(candidate.wallTile, slot.wallOccupyRadius);
                placedCount++;
            }
        }
    }

    private Room CreateStartRoomContext(StartRoom startRoom)
    {
        var room = new Room
        {
            id = startRoom.slotIndex,
            type = RoomType.Start,
            shape = GetShapeFromBounds(startRoom.bounds),
            layoutType = RoomLayoutType.Open,
            bounds = startRoom.bounds
        };

        for (int x = startRoom.bounds.xMin; x < startRoom.bounds.xMax; x++)
        {
            for (int z = startRoom.bounds.zMin; z < startRoom.bounds.zMax; z++)
            {
                Vector3Int tile = new Vector3Int(x, startRoom.bounds.yMin, z);
                room.floorTiles.Add(tile);

                if (IsStartRoomDoorwayTile(startRoom.bounds, tile))
                {
                    room.doorwayFloorTiles.Add(tile);
                }
            }
        }

        AddBlockedWorldArea(room.blockedTiles, startRoom.teamAnchorPosition, 3);
        for (int i = 0; i < startRoom.playerSpawnPositions.Count; i++)
        {
            AddBlockedWorldArea(room.blockedTiles, startRoom.playerSpawnPositions[i], 2);
        }

        return room;
    }

    private bool IsStartRoomDoorwayTile(BoundsInt bounds, Vector3Int tile)
    {
        bool isEdge =
            tile.x == bounds.xMin ||
            tile.x == bounds.xMax - 1 ||
            tile.z == bounds.zMin ||
            tile.z == bounds.zMax - 1;

        if (!isEdge)
        {
            return false;
        }

        foreach (Vector3Int direction in HorizontalDirections)
        {
            Vector3Int neighbor = tile + direction;
            if (bounds.Contains(neighbor))
            {
                continue;
            }

            if (dungeonGenerator.IsFloorTile(neighbor))
            {
                return true;
            }
        }

        return false;
    }

    private void AddBlockedWorldArea(HashSet<Vector3Int> blockedTiles, Vector3 worldPosition, int radius)
    {
        Vector3Int center = dungeonGenerator.WorldToTile(worldPosition);
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) > radius)
                {
                    continue;
                }

                blockedTiles.Add(center + new Vector3Int(x, 0, z));
            }
        }
    }

    private static RoomShape GetShapeFromBounds(BoundsInt bounds)
    {
        int width = bounds.size.x;
        int depth = bounds.size.z;
        float longSide = Mathf.Max(width, depth);
        float shortSide = Mathf.Max(1, Mathf.Min(width, depth));
        float ratio = longSide / shortSide;

        if (ratio >= 1.45f)
        {
            return width >= depth ? RoomShape.LongWide : RoomShape.LongTall;
        }

        int area = width * depth;
        if (area <= 1200)
        {
            return RoomShape.Small;
        }

        return area >= 3600 ? RoomShape.Large : RoomShape.Normal;
    }

    private int ResolveTargetCount(int requestedCount, int candidateCount, float density)
    {
        if (requestedCount <= 0 || candidateCount <= 0)
        {
            return 0;
        }

        float safeDensity = Mathf.Max(0.01f, density);
        int targetCount = safeDensity >= 1f
            ? Mathf.CeilToInt(requestedCount * safeDensity)
            : Mathf.FloorToInt(requestedCount * safeDensity);

        return Mathf.Clamp(Mathf.Max(1, targetCount), 0, candidateCount);
    }

    private float GetFloorDecorationDensity(FloorSlot slot)
    {
        if (slot.pattern == FloorPlacementPattern.Centerpiece || slot.footprintRadius >= 4)
        {
            return 1f;
        }

        return floorDecorationDensity;
    }

    private float GetCeilingDecorationDensity(CeilingSlot slot)
    {
        if (slot.pattern == CeilingPlacementPattern.Center && slot.maxCount <= 1)
        {
            return 1f;
        }

        return ceilingDecorationDensity;
    }

    private List<FloorCandidate> BuildFloorCandidates(Room room, FloorSlot slot, System.Random random)
    {
        switch (slot.pattern)
        {
            case FloorPlacementPattern.Centerpiece:
                return BuildCenterFloorCandidates(room, slot, random);
            case FloorPlacementPattern.CornerClusters:
                return BuildCornerFloorCandidates(room, slot, random);
            case FloorPlacementPattern.SideDressing:
                return BuildSideFloorCandidates(room, slot, random);
            case FloorPlacementPattern.CombatCoverRing:
                return BuildAnchorFloorCandidates(room, slot, random, GetRingAnchors());
            case FloorPlacementPattern.PathBreakers:
                return BuildAnchorFloorCandidates(room, slot, random, GetPathBreakerAnchors(room));
            default:
                return BuildRandomFloorCandidates(room, slot, random);
        }
    }

    private List<FloorCandidate> BuildRandomFloorCandidates(Room room, FloorSlot slot, System.Random random)
    {
        var candidates = new List<FloorCandidate>();
        foreach (Vector3Int tile in room.floorTiles)
        {
            if (IsValidFloorCandidate(room, tile, slot))
            {
                candidates.Add(new FloorCandidate(tile, GetNearestWallFacingDirection(tile)));
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<FloorCandidate> BuildCenterFloorCandidates(Room room, FloorSlot slot, System.Random random)
    {
        List<FloorCandidate> candidates = BuildRandomFloorCandidates(room, slot, random);
        Vector2 center = GetRoomCenter2D(room);
        candidates.Sort((a, b) =>
        {
            float distanceA = DistanceTo(a.tile, center);
            float distanceB = DistanceTo(b.tile, center);
            return distanceA.CompareTo(distanceB);
        });

        int take = Mathf.Min(candidates.Count, Mathf.Max(1, slot.candidatePoolSize));
        if (candidates.Count > take)
        {
            candidates.RemoveRange(take, candidates.Count - take);
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<FloorCandidate> BuildCornerFloorCandidates(Room room, FloorSlot slot, System.Random random)
    {
        List<Vector2> corners = GetRoomCornerTargets(room, slot.edgeInsetTiles);
        var candidates = new List<FloorCandidate>();

        foreach (Vector2 corner in corners)
        {
            if (TryFindNearestFloorCandidate(room, slot, corner, out FloorCandidate candidate))
            {
                candidates.Add(candidate);
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<FloorCandidate> BuildSideFloorCandidates(Room room, FloorSlot slot, System.Random random)
    {
        var candidates = new List<FloorCandidate>();
        foreach (Vector3Int tile in room.floorTiles)
        {
            if (!IsValidFloorCandidate(room, tile, slot))
            {
                continue;
            }

            int adjacentWallCount = CountAdjacentWalls(tile);
            if (adjacentWallCount == 1)
            {
                candidates.Add(new FloorCandidate(tile, GetNearestWallFacingDirection(tile)));
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<FloorCandidate> BuildAnchorFloorCandidates(Room room, FloorSlot slot, System.Random random, List<Vector2> anchors)
    {
        var candidates = new List<FloorCandidate>();
        foreach (Vector2 anchor in anchors)
        {
            Vector2 target = GetRoomNormalizedPoint(room, anchor);
            if (TryFindNearestFloorCandidate(room, slot, target, out FloorCandidate candidate))
            {
                candidates.Add(candidate);
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private bool TryFindNearestFloorCandidate(Room room, FloorSlot slot, Vector2 target, out FloorCandidate selected)
    {
        selected = default;
        float bestDistance = float.MaxValue;
        bool found = false;

        foreach (Vector3Int tile in room.floorTiles)
        {
            if (!IsValidFloorCandidate(room, tile, slot))
            {
                continue;
            }

            float distance = DistanceTo(tile, target);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            selected = new FloorCandidate(tile, GetNearestWallFacingDirection(tile));
            found = true;
        }

        return found;
    }

    private List<WallCandidate> BuildRoomWallCandidates(Room room, WallSlot slot, System.Random random)
    {
        var candidates = new List<WallCandidate>();
        List<WallSegment> segments = GetRoomSegments(room, slot);

        switch (slot.pattern)
        {
            case WallPlacementPattern.LongestWallCenter:
                AddCenterCandidateFromLongestSegment(segments, candidates);
                break;
            case WallPlacementPattern.PairOnLongestWall:
                AddPairCandidatesFromLongestSegment(segments, candidates, slot.edgePaddingTiles);
                break;
            case WallPlacementPattern.RandomSegmentCenter:
                AddSegmentCenters(segments, candidates);
                Shuffle(candidates, random);
                break;
            case WallPlacementPattern.CornerAccents:
                AddCornerWallCandidates(segments, candidates, slot.edgePaddingTiles);
                Shuffle(candidates, random);
                break;
            default:
                AddEvenWallCandidates(segments, candidates, slot);
                break;
        }

        return candidates;
    }

    private List<WallCandidate> BuildCorridorWallCandidates(WallSlot slot, System.Random random)
    {
        var candidates = new List<WallCandidate>();
        foreach (WallSegment segment in dungeonGenerator.WallSegments)
        {
            if (segment.ownerType != WallSegmentOwnerType.Corridor)
            {
                continue;
            }

            if (!slot.MatchesSide(segment.side))
            {
                continue;
            }

            if (!slot.placeBothSides && (segment.side == WallSide.South || segment.side == WallSide.West))
            {
                continue;
            }

            AddEvenWallCandidates(segment, candidates, slot);
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<WallSegment> GetRoomSegments(Room room, WallSlot slot)
    {
        var segments = new List<WallSegment>();
        WallSegmentOwnerType ownerType = GetWallSegmentOwnerType(room);
        foreach (WallSegment segment in dungeonGenerator.WallSegments)
        {
            if (segment.ownerType != ownerType || segment.ownerId != room.id)
            {
                continue;
            }

            if (!slot.MatchesSide(segment.side))
            {
                continue;
            }

            if (segment.LengthTiles < slot.minSegmentLengthTiles)
            {
                continue;
            }

            segments.Add(segment);
        }

        return segments;
    }

    private static WallSegmentOwnerType GetWallSegmentOwnerType(Room room)
    {
        return room.type == RoomType.Start ? WallSegmentOwnerType.StartRoom : WallSegmentOwnerType.Room;
    }

    private static void AddCenterCandidateFromLongestSegment(List<WallSegment> segments, List<WallCandidate> candidates)
    {
        WallSegment segment = GetLongestSegment(segments);
        if (segment == null)
        {
            return;
        }

        AddCandidateAtRatio(segment, candidates, 0.5f);
    }

    private static void AddPairCandidatesFromLongestSegment(List<WallSegment> segments, List<WallCandidate> candidates, int edgePadding)
    {
        WallSegment segment = GetLongestSegment(segments);
        if (segment == null)
        {
            return;
        }

        AddCandidateAtRatio(segment, candidates, 0.33f, edgePadding);
        AddCandidateAtRatio(segment, candidates, 0.67f, edgePadding);
    }

    private static void AddSegmentCenters(List<WallSegment> segments, List<WallCandidate> candidates)
    {
        foreach (WallSegment segment in segments)
        {
            AddCandidateAtRatio(segment, candidates, 0.5f);
        }
    }

    private static void AddCornerWallCandidates(List<WallSegment> segments, List<WallCandidate> candidates, int edgePadding)
    {
        foreach (WallSegment segment in segments)
        {
            if (segment.LengthTiles <= edgePadding * 2)
            {
                continue;
            }

            AddWallCandidate(segment, edgePadding, candidates);
            AddWallCandidate(segment, segment.LengthTiles - 1 - edgePadding, candidates);
        }
    }

    private static void AddEvenWallCandidates(List<WallSegment> segments, List<WallCandidate> candidates, WallSlot slot)
    {
        foreach (WallSegment segment in segments)
        {
            AddEvenWallCandidates(segment, candidates, slot);
        }
    }

    private static void AddEvenWallCandidates(WallSegment segment, List<WallCandidate> candidates, WallSlot slot)
    {
        int start = Mathf.Clamp(slot.edgePaddingTiles, 0, Mathf.Max(0, segment.LengthTiles - 1));
        int end = Mathf.Max(start, segment.LengthTiles - 1 - slot.edgePaddingTiles);

        for (int i = start; i <= end; i += Mathf.Max(1, slot.spacingTiles))
        {
            AddWallCandidate(segment, i, candidates);
        }
    }

    private static WallSegment GetLongestSegment(List<WallSegment> segments)
    {
        WallSegment longest = null;
        foreach (WallSegment segment in segments)
        {
            if (longest == null || segment.LengthTiles > longest.LengthTiles)
            {
                longest = segment;
            }
        }

        return longest;
    }

    private static void AddCandidateAtRatio(WallSegment segment, List<WallCandidate> candidates, float ratio, int edgePadding = 0)
    {
        if (segment.LengthTiles == 0)
        {
            return;
        }

        int index = Mathf.RoundToInt((segment.LengthTiles - 1) * Mathf.Clamp01(ratio));
        index = Mathf.Clamp(index, edgePadding, Mathf.Max(edgePadding, segment.LengthTiles - 1 - edgePadding));
        AddWallCandidate(segment, index, candidates);
    }

    private static void AddWallCandidate(WallSegment segment, int index, List<WallCandidate> candidates)
    {
        if (index < 0 || index >= segment.wallTiles.Count || index >= segment.floorTiles.Count)
        {
            return;
        }

        candidates.Add(new WallCandidate(segment.floorTiles[index], segment.wallTiles[index], segment.roomFacingDirection));
    }

    private List<CeilingCandidate> BuildCeilingCandidates(Room room, CeilingSlot slot, System.Random random)
    {
        switch (slot.pattern)
        {
            case CeilingPlacementPattern.CenterLine:
                return BuildCeilingAnchorCandidates(room, slot, random, GetPathBreakerAnchors(room));
            case CeilingPlacementPattern.CornerDrops:
                return BuildCeilingCornerCandidates(room, slot, random);
            case CeilingPlacementPattern.RandomOpen:
                return BuildRandomCeilingCandidates(room, slot, random);
            default:
                return BuildCeilingAnchorCandidates(room, slot, random, new List<Vector2> { new Vector2(0.5f, 0.5f) });
        }
    }

    private List<CeilingCandidate> BuildRandomCeilingCandidates(Room room, CeilingSlot slot, System.Random random)
    {
        var candidates = new List<CeilingCandidate>();
        foreach (Vector3Int floorTile in room.floorTiles)
        {
            Vector3Int tile = new Vector3Int(floorTile.x, Mathf.Max(1, dungeonGenerator.mapHeight), floorTile.z);
            if (IsValidCeilingCandidate(room, tile, slot))
            {
                candidates.Add(new CeilingCandidate(tile));
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<CeilingCandidate> BuildCeilingAnchorCandidates(Room room, CeilingSlot slot, System.Random random, List<Vector2> anchors)
    {
        var candidates = new List<CeilingCandidate>();
        foreach (Vector2 anchor in anchors)
        {
            Vector2 target = GetRoomNormalizedPoint(room, anchor);
            if (TryFindNearestCeilingCandidate(room, slot, target, out CeilingCandidate candidate))
            {
                candidates.Add(candidate);
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private List<CeilingCandidate> BuildCeilingCornerCandidates(Room room, CeilingSlot slot, System.Random random)
    {
        var candidates = new List<CeilingCandidate>();
        foreach (Vector2 corner in GetRoomCornerTargets(room, slot.edgeInsetTiles))
        {
            if (TryFindNearestCeilingCandidate(room, slot, corner, out CeilingCandidate candidate))
            {
                candidates.Add(candidate);
            }
        }

        Shuffle(candidates, random);
        return candidates;
    }

    private bool TryFindNearestCeilingCandidate(Room room, CeilingSlot slot, Vector2 target, out CeilingCandidate selected)
    {
        selected = default;
        float bestDistance = float.MaxValue;
        bool found = false;

        foreach (Vector3Int floorTile in room.floorTiles)
        {
            Vector3Int tile = new Vector3Int(floorTile.x, Mathf.Max(1, dungeonGenerator.mapHeight), floorTile.z);
            if (!IsValidCeilingCandidate(room, tile, slot))
            {
                continue;
            }

            float distance = DistanceTo(floorTile, target);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            selected = new CeilingCandidate(tile);
            found = true;
        }

        return found;
    }

    private bool IsValidFloorCandidate(Room room, Vector3Int tile, FloorSlot slot)
    {
        if (!room.floorTiles.Contains(tile))
        {
            return false;
        }

        if (room.blockedTiles.Contains(tile))
        {
            return false;
        }

        if (IsNearDoorway(room, tile, slot.doorwayClearanceTiles))
        {
            return false;
        }

        if (slot.wallClearanceTiles > 0 && IsNearWall(tile, slot.wallClearanceTiles))
        {
            return false;
        }

        if (slot.edgeInsetTiles > 0 && IsNearRoomEdge(room, tile, slot.edgeInsetTiles))
        {
            return false;
        }

        return !IsFloorAreaOccupied(tile, slot.footprintRadius);
    }

    private bool IsValidWallCandidate(Room room, WallCandidate candidate, WallSlot slot)
    {
        if (IsWallAreaOccupied(candidate.wallTile, slot.wallOccupyRadius))
        {
            return false;
        }

        if (IsNearDoorway(room, candidate.floorTile, slot.doorwayClearanceTiles))
        {
            return false;
        }

        return dungeonGenerator.IsWallTile(candidate.wallTile);
    }

    private bool IsValidCorridorWallCandidate(WallCandidate candidate, WallSlot slot)
    {
        if (IsWallAreaOccupied(candidate.wallTile, slot.wallOccupyRadius))
        {
            return false;
        }

        if (IsNearAnyRoomDoorway(candidate.floorTile, slot.doorwayClearanceTiles))
        {
            return false;
        }

        return dungeonGenerator.IsWallTile(candidate.wallTile);
    }

    private bool IsValidCeilingCandidate(Room room, Vector3Int ceilingTile, CeilingSlot slot)
    {
        Vector3Int floorTile = new Vector3Int(ceilingTile.x, 0, ceilingTile.z);
        if (!room.floorTiles.Contains(floorTile))
        {
            return false;
        }

        if (IsNearDoorway(room, floorTile, slot.doorwayClearanceTiles))
        {
            return false;
        }

        if (slot.edgeInsetTiles > 0 && IsNearRoomEdge(room, floorTile, slot.edgeInsetTiles))
        {
            return false;
        }

        return !IsCeilingAreaOccupied(ceilingTile, slot.footprintRadius);
    }

    private bool IsNearDoorway(Room room, Vector3Int tile, int radius)
    {
        if (radius <= 0)
        {
            return false;
        }

        foreach (Vector3Int doorwayTile in room.doorwayFloorTiles)
        {
            if (Mathf.Abs(doorwayTile.x - tile.x) <= radius && Mathf.Abs(doorwayTile.z - tile.z) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsNearAnyRoomDoorway(Vector3Int tile, int radius)
    {
        if (radius <= 0)
        {
            return false;
        }

        foreach (Room room in dungeonGenerator.Rooms)
        {
            if (IsNearDoorway(room, tile, radius))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsNearWall(Vector3Int tile, int radius)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                if (Mathf.Abs(x) + Mathf.Abs(z) > radius)
                {
                    continue;
                }

                if (dungeonGenerator.IsWallTile(tile + new Vector3Int(x, 0, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsNearRoomEdge(Room room, Vector3Int tile, int inset)
    {
        return tile.x < room.bounds.xMin + inset ||
               tile.x >= room.bounds.xMax - inset ||
               tile.z < room.bounds.zMin + inset ||
               tile.z >= room.bounds.zMax - inset;
    }

    private int CountAdjacentWalls(Vector3Int tile)
    {
        int count = 0;
        foreach (Vector3Int direction in HorizontalDirections)
        {
            if (dungeonGenerator.IsWallTile(tile + direction))
            {
                count++;
            }
        }

        return count;
    }

    private Vector3 GetNearestWallFacingDirection(Vector3Int tile)
    {
        foreach (Vector3Int direction in HorizontalDirections)
        {
            if (dungeonGenerator.IsWallTile(tile + direction))
            {
                return new Vector3(-direction.x, 0f, -direction.z);
            }
        }

        return Vector3.forward;
    }

    private Quaternion GetFloorRotation(FloorCandidate candidate, FloorSlot slot, System.Random random)
    {
        Quaternion baseRotation;
        if (slot.faceNearestWall)
        {
            baseRotation = Quaternion.LookRotation(candidate.facingDirection, Vector3.up);
        }
        else if (slot.randomYaw)
        {
            baseRotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360f), 0f);
        }
        else
        {
            baseRotation = Quaternion.identity;
        }

        return baseRotation * Quaternion.Euler(slot.rotationOffset);
    }

    private Quaternion GetWallRotation(WallCandidate candidate, WallSlot slot)
    {
        Vector3 categoryRotationOffset = slot.UsesCategory(PropCategory.벽조명)
            ? new Vector3(0f, wallLightingYawOffset, 0f)
            : Vector3.zero;

        return Quaternion.LookRotation(candidate.roomFacingDirection, Vector3.up) *
               Quaternion.Euler(slot.rotationOffset + categoryRotationOffset);
    }

    private Vector3 GetFloorSurfaceCenter(Vector3Int tile)
    {
        Vector3 center = dungeonGenerator.TileToWorldCenter(tile);
        center.y += floorSurfaceHeight;
        return center;
    }

    private Vector3 GetWallPosition(WallCandidate candidate, WallSlot slot)
    {
        float surfaceOffset = slot.wallSurfaceOffset >= 0f ? slot.wallSurfaceOffset : defaultWallSurfaceOffset;
        float heightOffset = slot.UsesCategory(PropCategory.벽조명) ? wallLightingHeightOffset : 0f;
        Vector3 wallCenter = dungeonGenerator.TileToWorldCenter(candidate.wallTile);
        return wallCenter +
               candidate.roomFacingDirection * (0.5f + surfaceOffset) +
               Vector3.up * (slot.wallHeight + heightOffset) +
               slot.positionOffset;
    }

    private Vector3 GetCeilingPosition(Vector3Int tile, CeilingSlot slot)
    {
        float hangOffset = slot.hangOffset >= 0f ? slot.hangOffset : defaultCeilingHangOffset;
        Vector3 position = dungeonGenerator.TileToWorldCenter(tile);
        position.y -= hangOffset;
        return position + slot.positionOffset;
    }

    private Vector2 GetRoomCenter2D(Room room)
    {
        return new Vector2(room.bounds.center.x, room.bounds.center.z);
    }

    private Vector2 GetRoomNormalizedPoint(Room room, Vector2 normalized)
    {
        int inset = 2;
        float minX = room.bounds.xMin + inset;
        float maxX = room.bounds.xMax - 1 - inset;
        float minZ = room.bounds.zMin + inset;
        float maxZ = room.bounds.zMax - 1 - inset;

        return new Vector2(
            Mathf.Lerp(minX, maxX, Mathf.Clamp01(normalized.x)),
            Mathf.Lerp(minZ, maxZ, Mathf.Clamp01(normalized.y))
        );
    }

    private List<Vector2> GetRoomCornerTargets(Room room, int inset)
    {
        int safeInset = Mathf.Max(2, inset);
        return new List<Vector2>
        {
            new Vector2(room.bounds.xMin + safeInset, room.bounds.zMin + safeInset),
            new Vector2(room.bounds.xMax - 1 - safeInset, room.bounds.zMin + safeInset),
            new Vector2(room.bounds.xMin + safeInset, room.bounds.zMax - 1 - safeInset),
            new Vector2(room.bounds.xMax - 1 - safeInset, room.bounds.zMax - 1 - safeInset)
        };
    }

    private static List<Vector2> GetRingAnchors()
    {
        return new List<Vector2>
        {
            new Vector2(0.35f, 0.5f),
            new Vector2(0.65f, 0.5f),
            new Vector2(0.5f, 0.35f),
            new Vector2(0.5f, 0.65f),
            new Vector2(0.38f, 0.38f),
            new Vector2(0.62f, 0.62f)
        };
    }

    private static List<Vector2> GetPathBreakerAnchors(Room room)
    {
        bool isWide = room.bounds.size.x >= room.bounds.size.z;
        return isWide
            ? new List<Vector2> { new Vector2(0.35f, 0.5f), new Vector2(0.65f, 0.5f) }
            : new List<Vector2> { new Vector2(0.5f, 0.35f), new Vector2(0.5f, 0.65f) };
    }

    private static float DistanceTo(Vector3Int tile, Vector2 target)
    {
        float dx = tile.x + 0.5f - target.x;
        float dz = tile.z + 0.5f - target.y;
        return dx * dx + dz * dz;
    }

    private bool IsFloorAreaOccupied(Vector3Int center, int radius)
    {
        return IsAreaOccupied(occupiedFloorTiles, center, radius);
    }

    private bool IsWallAreaOccupied(Vector3Int center, int radius)
    {
        return IsAreaOccupied(occupiedWallTiles, new Vector3Int(center.x, 0, center.z), radius);
    }

    private bool IsCeilingAreaOccupied(Vector3Int center, int radius)
    {
        return IsAreaOccupied(occupiedCeilingTiles, center, radius);
    }

    private static bool IsAreaOccupied(HashSet<Vector3Int> occupied, Vector3Int center, int radius)
    {
        if (radius <= 0)
        {
            return occupied.Contains(center);
        }

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                if (occupied.Contains(new Vector3Int(x, center.y, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OccupyFloorArea(Vector3Int center, int radius)
    {
        OccupyArea(occupiedFloorTiles, center, radius);
    }

    private void OccupyWallArea(Vector3Int center, int radius)
    {
        OccupyArea(occupiedWallTiles, new Vector3Int(center.x, 0, center.z), radius);
    }

    private void OccupyCeilingArea(Vector3Int center, int radius)
    {
        OccupyArea(occupiedCeilingTiles, center, radius);
    }

    private static void OccupyArea(HashSet<Vector3Int> occupied, Vector3Int center, int radius)
    {
        if (radius <= 0)
        {
            occupied.Add(center);
            return;
        }

        for (int x = center.x - radius; x <= center.x + radius; x++)
        {
            for (int z = center.z - radius; z <= center.z + radius; z++)
            {
                occupied.Add(new Vector3Int(x, center.y, z));
            }
        }
    }

    private GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform root)
    {
        GameObject spawnedObject = Instantiate(prefab, position, rotation, root);
        spawnedObjects.Add(spawnedObject);
        return spawnedObject;
    }

    private void AlignFloorObjectBottomToSurface(GameObject spawnedObject, float surfaceY)
    {
        if (!alignFloorObjectBottomToSurface || spawnedObject == null)
        {
            return;
        }

        if (!TryGetPlacementBounds(spawnedObject, out Bounds bounds))
        {
            return;
        }

        float yOffset = surfaceY - bounds.min.y;
        if (Mathf.Abs(yOffset) <= 0.001f)
        {
            return;
        }

        spawnedObject.transform.position += Vector3.up * yOffset;
    }

    private static bool TryGetPlacementBounds(GameObject target, out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private Transform GetOrCreateRoot()
    {
        Transform root = transform.Find(RootName);
        if (root != null)
        {
            return root;
        }

        var rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(transform);
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        return rootObject.transform;
    }

    private static void DestroyObject(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private readonly struct FloorCandidate
    {
        public readonly Vector3Int tile;
        public readonly Vector3 facingDirection;

        public FloorCandidate(Vector3Int tile, Vector3 facingDirection)
        {
            this.tile = tile;
            this.facingDirection = facingDirection;
        }
    }

    private readonly struct WallCandidate
    {
        public readonly Vector3Int floorTile;
        public readonly Vector3Int wallTile;
        public readonly Vector3 roomFacingDirection;

        public WallCandidate(Vector3Int floorTile, Vector3Int wallTile, Vector3 roomFacingDirection)
        {
            this.floorTile = floorTile;
            this.wallTile = wallTile;
            this.roomFacingDirection = roomFacingDirection;
        }
    }

    private readonly struct CeilingCandidate
    {
        public readonly Vector3Int tile;

        public CeilingCandidate(Vector3Int tile)
        {
            this.tile = tile;
        }
    }

    [Serializable]
    private sealed class PropLibrary
    {
        [FormerlySerializedAs("smallClutter")] public List<GameObject> 작은잡동사니목록 = new List<GameObject>();
        [FormerlySerializedAs("bookshelves")] public List<GameObject> 책장목록 = new List<GameObject>();
        [FormerlySerializedAs("tables")] public List<GameObject> 책상테이블목록 = new List<GameObject>();
        [FormerlySerializedAs("cratesAndBoxes")] public List<GameObject> 상자크레이트목록 = new List<GameObject>();
        [FormerlySerializedAs("rocksAndRubble")] public List<GameObject> 바위잔해목록 = new List<GameObject>();
        [FormerlySerializedAs("combatCover")] public List<GameObject> 전투엄폐물목록 = new List<GameObject>();
        [FormerlySerializedAs("largeCenterpieces")] public List<GameObject> 대형중앙장식목록 = new List<GameObject>();
        [FormerlySerializedAs("treasureAndReward")] public List<GameObject> 보물보상목록 = new List<GameObject>();
        [FormerlySerializedAs("exitRitualProps")] public List<GameObject> 탈출의식소품목록 = new List<GameObject>();
        [FormerlySerializedAs("bossRoomProps")] public List<GameObject> 보스방소품목록 = new List<GameObject>();
        [FormerlySerializedAs("wallLighting")] public List<GameObject> 벽조명목록 = new List<GameObject>();
        [FormerlySerializedAs("wallDecor")] public List<GameObject> 벽장식목록 = new List<GameObject>();
        [FormerlySerializedAs("wallBanners")] public List<GameObject> 벽배너목록 = new List<GameObject>();
        [FormerlySerializedAs("ceilingBanners")] public List<GameObject> 천장깃발천장식목록 = new List<GameObject>();
        [FormerlySerializedAs("chandeliers")] public List<GameObject> 샹들리에천장조명목록 = new List<GameObject>();

        public IEnumerable<GameObject> GetPrefabs(PropCategory category)
        {
            return category switch
            {
                PropCategory.작은잡동사니 => 작은잡동사니목록,
                PropCategory.책장 => 책장목록,
                PropCategory.책상테이블 => 책상테이블목록,
                PropCategory.상자크레이트 => 상자크레이트목록,
                PropCategory.바위잔해 => 바위잔해목록,
                PropCategory.전투엄폐물 => 전투엄폐물목록,
                PropCategory.대형중앙장식 => 대형중앙장식목록,
                PropCategory.보물보상 => 보물보상목록,
                PropCategory.탈출의식소품 => 탈출의식소품목록,
                PropCategory.보스방소품 => 보스방소품목록,
                PropCategory.벽조명 => 벽조명목록,
                PropCategory.벽장식 => 벽장식목록,
                PropCategory.벽배너 => 벽배너목록,
                PropCategory.천장깃발천장식 => 천장깃발천장식목록,
                PropCategory.샹들리에천장조명 => 샹들리에천장조명목록,
                _ => Array.Empty<GameObject>()
            };
        }
    }

    [Serializable]
    private sealed class RoomTemplate
    {
        public string label = "Room Template";
        public bool enabled = true;
        [Min(1)] public int weight = 1;
        public RoomTypeMask roomTypes = RoomTypeMask.Normal;
        public RoomShapeMask roomShapes = RoomShapeMask.All;
        public RoomLayoutMask roomLayouts = RoomLayoutMask.All;
        public List<FloorSlot> floorSlots = new List<FloorSlot>();
        public List<WallSlot> wallSlots = new List<WallSlot>();
        public List<CeilingSlot> ceilingSlots = new List<CeilingSlot>();

        public bool CanPlaceIn(Room room)
        {
            return enabled &&
                   (roomTypes & ToMask(room.type)) != 0 &&
                   (roomShapes & ToMask(room.shape)) != 0 &&
                   (roomLayouts & ToMask(room.layoutType)) != 0;
        }

        private static RoomTypeMask ToMask(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Normal => RoomTypeMask.Normal,
                RoomType.Boss => RoomTypeMask.Boss,
                RoomType.Reward => RoomTypeMask.Reward,
                RoomType.Exit => RoomTypeMask.Exit,
                RoomType.Start => RoomTypeMask.Start,
                _ => RoomTypeMask.None
            };
        }

        private static RoomShapeMask ToMask(RoomShape roomShape)
        {
            return roomShape switch
            {
                RoomShape.Small => RoomShapeMask.Small,
                RoomShape.Normal => RoomShapeMask.Normal,
                RoomShape.Large => RoomShapeMask.Large,
                RoomShape.LongWide => RoomShapeMask.LongWide,
                RoomShape.LongTall => RoomShapeMask.LongTall,
                _ => RoomShapeMask.None
            };
        }

        private static RoomLayoutMask ToMask(RoomLayoutType roomLayout)
        {
            return roomLayout switch
            {
                RoomLayoutType.Open => RoomLayoutMask.Open,
                RoomLayoutType.FourPillars => RoomLayoutMask.FourPillars,
                RoomLayoutType.CenterBlock => RoomLayoutMask.CenterBlock,
                _ => RoomLayoutMask.None
            };
        }
    }

    [Serializable]
    private abstract class PlacementSlot
    {
        public string label = "배치 슬롯";
        public bool enabled = true;
        [Range(0f, 1f)] public float chance = 1f;
        public List<PropCategory> propCategories = new List<PropCategory>();
        [Tooltip("비워두면 프롭 라이브러리에서 카테고리에 맞는 프리팹을 사용합니다.")]
        public List<GameObject> prefabs = new List<GameObject>();
        [Min(0)] public int minCount = 0;
        [Min(0)] public int maxCount = 1;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;

        public bool CanPlace(System.Random random, PropLibrary library)
        {
            return enabled &&
                   HasAnyPrefab(library) &&
                   maxCount > 0 &&
                   random.NextDouble() <= chance;
        }

        public int GetCount(System.Random random)
        {
            int min = Mathf.Max(0, minCount);
            int max = Mathf.Max(min, maxCount);
            return random.Next(min, max + 1);
        }

        public GameObject PickPrefab(System.Random random, PropLibrary library)
        {
            List<GameObject> pool = BuildPrefabPool(library);
            if (pool.Count == 0)
            {
                return null;
            }

            return pool[random.Next(0, pool.Count)];
        }

        public bool UsesCategory(PropCategory category)
        {
            return propCategories != null && propCategories.Contains(category);
        }

        private bool HasAnyPrefab(PropLibrary library)
        {
            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null)
                {
                    return true;
                }
            }

            if (library == null)
            {
                return false;
            }

            foreach (PropCategory category in propCategories)
            {
                foreach (GameObject prefab in library.GetPrefabs(category))
                {
                    if (prefab != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private List<GameObject> BuildPrefabPool(PropLibrary library)
        {
            var pool = new List<GameObject>();

            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null)
                {
                    pool.Add(prefab);
                }
            }

            if (pool.Count > 0 || library == null)
            {
                return pool;
            }

            foreach (PropCategory category in propCategories)
            {
                foreach (GameObject prefab in library.GetPrefabs(category))
                {
                    if (prefab != null)
                    {
                        pool.Add(prefab);
                    }
                }
            }

            return pool;
        }
    }

    [Serializable]
    private sealed class FloorSlot : PlacementSlot
    {
        public FloorPlacementPattern pattern = FloorPlacementPattern.RandomOpen;
        [Min(0)] public int footprintRadius = 1;
        [Min(0)] public int wallClearanceTiles = 0;
        [Min(0)] public int doorwayClearanceTiles = 3;
        [Min(0)] public int edgeInsetTiles = 0;
        [Min(1)] public int candidatePoolSize = 8;
        [Min(0f)] public float roomFacingOffset = 0f;
        public bool randomYaw = true;
        public bool faceNearestWall = false;
    }

    [Serializable]
    private sealed class WallSlot : PlacementSlot
    {
        public WallPlacementPattern pattern = WallPlacementPattern.EvenRhythm;
        public WallSideMask wallSides = WallSideMask.All;
        [Min(1)] public int spacingTiles = 12;
        [Min(0)] public int edgePaddingTiles = 4;
        [Min(1)] public int minSegmentLengthTiles = 4;
        [Min(0)] public int doorwayClearanceTiles = 4;
        [Min(0)] public int wallOccupyRadius = 4;
        public float wallHeight = 1.5f;
        public float wallSurfaceOffset = -1f;
        public bool placeBothSides = false;

        public bool MatchesSide(WallSide side)
        {
            return (wallSides & ToMask(side)) != 0;
        }

        private static WallSideMask ToMask(WallSide side)
        {
            return side switch
            {
                WallSide.North => WallSideMask.North,
                WallSide.South => WallSideMask.South,
                WallSide.East => WallSideMask.East,
                WallSide.West => WallSideMask.West,
                _ => WallSideMask.None
            };
        }
    }

    [Serializable]
    private sealed class CeilingSlot : PlacementSlot
    {
        public CeilingPlacementPattern pattern = CeilingPlacementPattern.Center;
        [Min(0)] public int footprintRadius = 3;
        [Min(0)] public int doorwayClearanceTiles = 5;
        [Min(0)] public int edgeInsetTiles = 3;
        public float hangOffset = -1f;
        public bool randomYaw = true;
    }

    private enum FloorPlacementPattern
    {
        RandomOpen,
        Centerpiece,
        CornerClusters,
        SideDressing,
        CombatCoverRing,
        PathBreakers
    }

    private enum WallPlacementPattern
    {
        EvenRhythm,
        LongestWallCenter,
        PairOnLongestWall,
        RandomSegmentCenter,
        CornerAccents
    }

    private enum CeilingPlacementPattern
    {
        Center,
        CenterLine,
        CornerDrops,
        RandomOpen
    }

    private enum PropCategory
    {
        작은잡동사니,
        책장,
        책상테이블,
        상자크레이트,
        바위잔해,
        전투엄폐물,
        대형중앙장식,
        보물보상,
        탈출의식소품,
        보스방소품,
        벽조명,
        벽장식,
        벽배너,
        천장깃발천장식,
        샹들리에천장조명
    }

    [Flags]
    private enum RoomTypeMask
    {
        None = 0,
        Normal = 1 << 0,
        Boss = 1 << 1,
        Reward = 1 << 2,
        Exit = 1 << 3,
        Start = 1 << 4,
        All = Normal | Boss | Reward | Exit | Start
    }

    [Flags]
    private enum RoomShapeMask
    {
        None = 0,
        Small = 1 << 0,
        Normal = 1 << 1,
        Large = 1 << 2,
        LongWide = 1 << 3,
        LongTall = 1 << 4,
        All = Small | Normal | Large | LongWide | LongTall
    }

    [Flags]
    private enum RoomLayoutMask
    {
        None = 0,
        Open = 1 << 0,
        FourPillars = 1 << 1,
        CenterBlock = 1 << 2,
        All = Open | FourPillars | CenterBlock
    }

    [Flags]
    private enum WallSideMask
    {
        None = 0,
        North = 1 << 0,
        South = 1 << 1,
        East = 1 << 2,
        West = 1 << 3,
        All = North | South | East | West
    }
}

