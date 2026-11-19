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
    [SerializeField] private GameObject localBossPrefab;
    [SerializeField] private GameObject knightElitePrefab;
    [SerializeField] private GameObject mummyPrefab;

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
        if(NetworkRuntime.IsOnline) return;
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
            if(room.type==RoomType.Boss)
            {
                SpawnBoss(room);
                continue;
            }
            if (KnightEncounter.IsKnightRoom(room))
            {
                SpawnKnight(room);
                continue;
            }
            if (room.type != RoomType.Normal)
            {
                continue;
            }

            SpawnMonstersInRoom(room, random);
        }
        SpawnCorridorMonsters(random);
    }

    static uint CorridorHash(int seed,Vector3Int tile)
    {
        unchecked{uint h=(uint)(seed^0x434D4F4E)^((uint)tile.x*73856093u)^((uint)tile.z*19349663u);h^=h>>16;h*=0x85ebca6bu;return h^(h>>13);}
    }
    void SpawnCorridorMonsters(System.Random random)
    {
        var unique=new HashSet<Vector3Int>();foreach(var corridor in dungeonGenerator.Corridors)unique.UnionWith(corridor.floorTiles);
        var tiles=new List<Vector3Int>(unique);
        tiles.Sort((a,b)=>{int order=CorridorHash(dungeonGenerator.seed,a).CompareTo(CorridorHash(dungeonGenerator.seed,b));return order!=0?order:a.x!=b.x?a.x.CompareTo(b.x):a.z.CompareTo(b.z);});
        int count=0;
        foreach(var tile in tiles){
            if(count>=24)break;bool safe=tile.y==0;
            foreach(var room in dungeonGenerator.Rooms){if(room.floorTiles.Contains(tile))safe=false;foreach(var door in room.doorwayFloorTiles)if(Mathf.Abs(door.x-tile.x)+Mathf.Abs(door.z-tile.z)<5)safe=false;}
            foreach(var start in dungeonGenerator.GetAssignedStartRooms())if(tile.x>=start.bounds.xMin-12&&tile.x<start.bounds.xMax+12&&tile.z>=start.bounds.zMin-12&&tile.z<start.bounds.zMax+12)safe=false;
            if(!safe)continue;
            var point=dungeonGenerator.TileToWorldCenter(tile)+Vector3.up;
            if(!NavMesh.SamplePosition(point,out var hit,.5f,NavMesh.AllAreas)||spawnedMonsters.Exists(m=>m!=null&&(m.transform.position-hit.position).sqrMagnitude<100))continue;
            if(Physics.CheckSphere(hit.position+Vector3.up,.65f,LayerMask.GetMask("Wall","Default"),QueryTriggerInteraction.Ignore))continue;
            var prefab=PickMonsterPrefab(random);if(prefab==null)continue;
            var monster=Instantiate(prefab,hit.position,Quaternion.identity);monster.name="Corridor Monster";spawnedMonsters.Add(monster);count++;
        }
    }

    private void SpawnKnight(Room room)
    {
        for (int i = 0; i < RoomBlueprint.GuardCount(dungeonGenerator.seed, room); i++) SpawnKnight(room, i);
    }

    private void SpawnKnight(Room room, int index)
    {
        if (knightElitePrefab == null) knightElitePrefab = Resources.Load<GameObject>("KnightElite");
        if (knightElitePrefab == null) { Debug.LogError("Missing KnightElite prefab", this); return; }
        // Exhaust the sorted, unblocked floor list: a random retry budget can silently omit a guardian.
        var tiles = new List<Vector3Int>(room.floorTiles);
        Vector3 center = RoomBlueprint.GuardTile(dungeonGenerator.seed, room, index);
        tiles.Sort((a,b) => {
            int distance = (a-center).sqrMagnitude.CompareTo((b-center).sqrMagnitude);
            if (distance != 0) return distance;
            return a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z);
        });
        foreach (var tile in tiles)
        {
            if (room.blockedTiles.Contains(tile)) continue;
            // Floor voxels are one metre high; TileToWorldCenter only centers X/Z.
            var candidate = dungeonGenerator.TileToWorldCenter(tile) + Vector3.up;
            if (!NavMesh.SamplePosition(candidate, out var hit, .6f, NavMesh.AllAreas)) continue;
            var landed = dungeonGenerator.WorldToTile(hit.position); landed.y = 0;
            if (!room.floorTiles.Contains(landed) || room.blockedTiles.Contains(landed)) continue;
            if (spawnedMonsters.Exists(m => m != null && (m.transform.position-hit.position).sqrMagnitude < 9)) continue;
            var knight = Instantiate(knightElitePrefab, hit.position, Quaternion.identity);
            knight.name = "KnightElite_Room_" + room.id;
            spawnedMonsters.Add(knight);
            return;
        }
        Debug.LogError($"No navigable tile for knight in reward room {room.id}", this);
    }

    private void SpawnBoss(Room room)
    {
        if(localBossPrefab==null || room.floorTiles.Count==0) return;
        Vector3 center=dungeonGenerator.TileToWorld(room.bounds.position)+new Vector3(room.bounds.size.x*.5f,0,room.bounds.size.z*.5f);
        Vector3 best=Vector3.zero; float distance=float.MaxValue;
        foreach(var tile in room.floorTiles)
        {
            if(room.blockedTiles.Contains(tile)) continue;
            Vector3 candidate=dungeonGenerator.TileToWorldCenter(tile);
            if(!NavMesh.SamplePosition(candidate,out var hit,.6f,NavMesh.AllAreas)) continue;
            float value=(hit.position-center).sqrMagnitude;
            if(value<distance) {distance=value; best=hit.position;}
        }
        if(distance==float.MaxValue) {Debug.LogWarning("[LocalBoss] No navigable boss room spawn."); return;}
        var boss=Instantiate(localBossPrefab,best,Quaternion.identity);
        boss.name="LocalBoss_Room_"+room.id;
        boss.GetComponent<LocalBossController>().Configure(dungeonGenerator,room);
        spawnedMonsters.Add(boss);
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
            if(MummyMonster.SpawnsInRoom(dungeonGenerator.seed,room,i)) {
                if(mummyPrefab==null)mummyPrefab=Resources.Load<GameObject>("Mummy");
                if(mummyPrefab!=null)prefab=mummyPrefab;
            }
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
