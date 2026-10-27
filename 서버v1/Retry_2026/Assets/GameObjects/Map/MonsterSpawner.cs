using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class MonsterSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonGenerator_ChunkMesh dungeonGenerator;
    [SerializeField] private DungeonNavMeshBuilder navMeshBuilder;

    [Header("Monster Pool")]
    [SerializeField] private List<GameObject> monsterPrefabs = new List<GameObject>();

    [Header("Spawn Rules")]
    [SerializeField, Min(0)] private int monstersPerNormalRoom = 3;
    [SerializeField, Min(0.5f)] private float navMeshSampleRadius = 5f;
    [SerializeField, Min(1)] private int maxSpawnPositionAttempts = 16;
    [SerializeField] private int spawnSeedOffset = 3000;
    [SerializeField] private bool randomizeRotation = true;

    private readonly List<GameObject> spawnedMonsters = new List<GameObject>();

    private void OnEnable()
    {
        if (dungeonGenerator == null)
        {
            dungeonGenerator = GetComponent<DungeonGenerator_ChunkMesh>();
        }

        if (navMeshBuilder == null)
        {
            navMeshBuilder = GetComponent<DungeonNavMeshBuilder>();
        }

        if (navMeshBuilder != null)
        {
            navMeshBuilder.NavMeshBuilt += HandleNavMeshBuilt;
        }
    }

    private void OnDisable()
    {
        if (navMeshBuilder != null)
        {
            navMeshBuilder.NavMeshBuilt -= HandleNavMeshBuilt;
        }
    }

    [ContextMenu("Spawn Monsters")]
    public void SpawnMonsters()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogWarning("MonsterSpawner requires a DungeonGenerator_ChunkMesh reference.", this);
            return;
        }

        if (monsterPrefabs.Count == 0)
        {
            Debug.LogWarning("MonsterSpawner requires at least one monster prefab.", this);
            return;
        }

        ClearSpawnedMonsters();

        var random = new System.Random(dungeonGenerator.seed + spawnSeedOffset);
        IReadOnlyList<Room> rooms = dungeonGenerator.Rooms;
        foreach (Room room in rooms)
        {
            if (room.type != RoomType.Normal)
            {
                continue;
            }

            SpawnMonstersInRoom(room, random);
        }
    }

    [ContextMenu("Clear Spawned Monsters")]
    public void ClearSpawnedMonsters()
    {
        for (int i = spawnedMonsters.Count - 1; i >= 0; i--)
        {
            GameObject monster = spawnedMonsters[i];
            if (monster == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(monster);
            }
            else
            {
                DestroyImmediate(monster);
            }
        }

        spawnedMonsters.Clear();
    }

    private void HandleNavMeshBuilt(DungeonNavMeshBuilder builder)
    {
        SpawnMonsters();
    }

    private void SpawnMonstersInRoom(Room room, System.Random random)
    {
        for (int i = 0; i < monstersPerNormalRoom; i++)
        {
            GameObject prefab = PickMonsterPrefab(random);
            if (prefab == null)
            {
                continue;
            }

            if (!TryGetSpawnPosition(room, random, out Vector3 spawnPosition))
            {
                continue;
            }

            Quaternion rotation = randomizeRotation
                ? Quaternion.Euler(0f, (float)(random.NextDouble() * 360f), 0f)
                : Quaternion.identity;

            GameObject monster = Instantiate(prefab, spawnPosition, rotation);
            spawnedMonsters.Add(monster);
        }
    }

    private GameObject PickMonsterPrefab(System.Random random)
    {
        if (monsterPrefabs.Count == 0)
        {
            return null;
        }

        return monsterPrefabs[random.Next(0, monsterPrefabs.Count)];
    }

    private bool TryGetSpawnPosition(Room room, System.Random random, out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        if (room.floorTiles == null || room.floorTiles.Count == 0)
        {
            return false;
        }

        var floorTiles = new List<Vector3Int>(room.floorTiles);
        for (int i = 0; i < maxSpawnPositionAttempts; i++)
        {
            Vector3Int tile = floorTiles[random.Next(0, floorTiles.Count)];
            Vector3 candidate = dungeonGenerator.TileToWorldCenter(tile);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            spawnPosition = hit.position;
            return true;
        }

        return false;
    }
}
