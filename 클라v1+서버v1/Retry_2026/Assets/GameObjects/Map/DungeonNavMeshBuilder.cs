using Unity.AI.Navigation;
using UnityEngine;

[DisallowMultipleComponent]
public class DungeonNavMeshBuilder : MonoBehaviour
{
    public event System.Action<DungeonNavMeshBuilder> NavMeshBuilt;

    [SerializeField] private DungeonGenerator_ChunkMesh dungeonGenerator;
    [SerializeField] private RoomObjectPlacer roomObjectPlacer;
    [SerializeField] private RoomObjectPlacer2 roomObjectPlacer2;
    [SerializeField] private NavMeshSurface navMeshSurface;
    [SerializeField] private bool waitForRoomObjectPlacer = true;

    private bool waitingForRoomObjectPlacers;
    private bool waitingForRoomObjectPlacer;
    private bool waitingForRoomObjectPlacer2;

    private void OnEnable()
    {
        if (dungeonGenerator == null)
        {
            dungeonGenerator = GetComponent<DungeonGenerator_ChunkMesh>();
        }

        if (navMeshSurface == null)
        {
            navMeshSurface = GetComponent<NavMeshSurface>();
        }

        if (roomObjectPlacer == null)
        {
            roomObjectPlacer = GetComponent<RoomObjectPlacer>();
        }

        if (roomObjectPlacer2 == null)
        {
            roomObjectPlacer2 = GetComponent<RoomObjectPlacer2>();
        }

        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated += HandleDungeonGenerated;
        }

        if (roomObjectPlacer != null)
        {
            roomObjectPlacer.RoomObjectsPlaced += HandleRoomObjectsPlaced;
        }

        if (roomObjectPlacer2 != null)
        {
            roomObjectPlacer2.RoomObjectsPlaced += HandleRoomObjectsPlaced2;
        }
    }

    private void OnDisable()
    {
        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated -= HandleDungeonGenerated;
        }

        if (roomObjectPlacer != null)
        {
            roomObjectPlacer.RoomObjectsPlaced -= HandleRoomObjectsPlaced;
        }

        if (roomObjectPlacer2 != null)
        {
            roomObjectPlacer2.RoomObjectsPlaced -= HandleRoomObjectsPlaced2;
        }
    }

    private void HandleDungeonGenerated(DungeonGenerator_ChunkMesh generator)
    {
        waitingForRoomObjectPlacer = ShouldWaitForRoomObjectPlacer();
        waitingForRoomObjectPlacer2 = ShouldWaitForRoomObjectPlacer2();
        waitingForRoomObjectPlacers = waitingForRoomObjectPlacer || waitingForRoomObjectPlacer2;

        if (!waitingForRoomObjectPlacers)
        {
            BuildNavMesh("Dungeon generated");
        }
    }

    private void HandleRoomObjectsPlaced(RoomObjectPlacer placer)
    {
        if (!waitingForRoomObjectPlacers)
        {
            BuildNavMesh("Room objects placed");
            return;
        }

        waitingForRoomObjectPlacer = false;
        TryBuildAfterRoomObjectPlacers("Room objects placed");
    }

    private void HandleRoomObjectsPlaced2(RoomObjectPlacer2 placer)
    {
        if (!waitingForRoomObjectPlacers)
        {
            BuildNavMesh("Room objects 2 placed");
            return;
        }

        waitingForRoomObjectPlacer2 = false;
        TryBuildAfterRoomObjectPlacers("Room objects placed");
    }

    private void TryBuildAfterRoomObjectPlacers(string reason)
    {
        if (waitingForRoomObjectPlacer || waitingForRoomObjectPlacer2)
        {
            return;
        }

        waitingForRoomObjectPlacers = false;
        BuildNavMesh(reason);
    }

    private void BuildNavMesh(string reason)
    {
        if (navMeshSurface == null)
        {
            Debug.LogWarning("DungeonNavMeshBuilder requires a NavMeshSurface reference.", this);
            return;
        }

        Debug.Log($"[DungeonNavMeshBuilder] {reason}. Building NavMesh.", this);
        navMeshSurface.BuildNavMesh();
        Debug.Log("[DungeonNavMeshBuilder] NavMesh build completed.", this);
        NavMeshBuilt?.Invoke(this);
    }

    private bool ShouldWaitForRoomObjectPlacer()
    {
        return waitForRoomObjectPlacer &&
               roomObjectPlacer != null &&
               roomObjectPlacer.isActiveAndEnabled &&
               roomObjectPlacer.PlaceOnDungeonGenerated;
    }

    private bool ShouldWaitForRoomObjectPlacer2()
    {
        return waitForRoomObjectPlacer &&
               roomObjectPlacer2 != null &&
               roomObjectPlacer2.isActiveAndEnabled &&
               roomObjectPlacer2.PlaceOnDungeonGenerated;
    }
}
