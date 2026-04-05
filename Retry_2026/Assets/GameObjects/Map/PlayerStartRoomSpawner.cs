using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerStartRoomSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonGenerator_ChunkMesh dungeonGenerator;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform existingPlayer;

    [Header("Spawn")]
    [SerializeField] private bool spawnOnDungeonGenerated = true;
    [SerializeField] private bool useAssignedStartRoomsOnly = true;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private int spawnSeedOffset = 1000;
    [SerializeField] private int localTeamId;
    [SerializeField] private int localTeamMemberIndex;

    private Transform spawnedPlayer;
    private bool hasSpawned;

    private void OnEnable()
    {
        if (dungeonGenerator == null)
        {
            dungeonGenerator = GetComponent<DungeonGenerator_ChunkMesh>();
        }

        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated += HandleDungeonGenerated;
        }
    }

    private void Start()
    {
        ResolveExistingPlayer();

        if (!spawnOnDungeonGenerated || dungeonGenerator == null)
        {
            return;
        }

        IReadOnlyList<StartRoom> assignedRooms = dungeonGenerator.GetAssignedStartRooms();
        if (assignedRooms.Count > 0)
        {
            SpawnPlayerAtRandomStartRoom();
        }
    }

    private void OnDisable()
    {
        if (dungeonGenerator != null)
        {
            dungeonGenerator.DungeonGenerated -= HandleDungeonGenerated;
        }
    }

    [ContextMenu("Spawn Player At Random Start Room")]
    public void SpawnPlayerAtRandomStartRoom()
    {
        if (hasSpawned)
        {
            return;
        }

        if (dungeonGenerator == null)
        {
            Debug.LogWarning("PlayerStartRoomSpawner requires a DungeonGenerator_ChunkMesh reference.", this);
            return;
        }

        if (useAssignedStartRoomsOnly &&
            dungeonGenerator.TryGetTeamSpawnPosition(localTeamId, localTeamMemberIndex, out Vector3 teamSpawnPosition, out Quaternion teamSpawnRotation))
        {
            SpawnPlayer(teamSpawnPosition, teamSpawnRotation);
            return;
        }

        IReadOnlyList<StartRoom> sourceRooms = useAssignedStartRoomsOnly
            ? dungeonGenerator.GetAssignedStartRooms()
            : dungeonGenerator.GetAllStartRooms();

        if (sourceRooms.Count == 0)
        {
            Debug.LogWarning("No start rooms are available for player spawning.", this);
            return;
        }

        StartRoom selectedRoom = PickRandomStartRoom(sourceRooms);
        SpawnPlayer(selectedRoom.GetSpawnPositionForMember(localTeamMemberIndex), selectedRoom.spawnRotation);
    }

    private void HandleDungeonGenerated(DungeonGenerator_ChunkMesh generator)
    {
        if (!spawnOnDungeonGenerated)
        {
            return;
        }

        SpawnPlayerAtRandomStartRoom();
    }

    private StartRoom PickRandomStartRoom(IReadOnlyList<StartRoom> rooms)
    {
        int seed = dungeonGenerator.seed + spawnSeedOffset;
        var random = new System.Random(seed);
        int index = random.Next(0, rooms.Count);
        return rooms[index];
    }

    private void SpawnPlayer(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        Transform playerTransform = GetOrCreatePlayerTransform();
        if (playerTransform == null)
        {
            Debug.LogWarning("No player instance or prefab is available for spawning.", this);
            return;
        }

        playerTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
        hasSpawned = true;
    }

    private Transform GetOrCreatePlayerTransform()
    {
        ResolveExistingPlayer();

        if (existingPlayer != null)
        {
            return existingPlayer;
        }

        if (spawnedPlayer != null)
        {
            return spawnedPlayer;
        }

        if (playerPrefab == null)
        {
            return null;
        }

        GameObject instance = Instantiate(playerPrefab);
        spawnedPlayer = instance.transform;
        existingPlayer = spawnedPlayer;
        return spawnedPlayer;
    }

    private void ResolveExistingPlayer()
    {
        if (existingPlayer != null)
        {
            return;
        }

        GameObject taggedPlayer = GameObject.FindGameObjectWithTag(playerTag);
        if (taggedPlayer != null)
        {
            existingPlayer = taggedPlayer.transform;
        }
    }
}
