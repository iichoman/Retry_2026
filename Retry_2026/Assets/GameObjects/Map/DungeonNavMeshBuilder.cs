using Unity.AI.Navigation;
using UnityEngine;

[DisallowMultipleComponent]
public class DungeonNavMeshBuilder : MonoBehaviour
{
    public event System.Action<DungeonNavMeshBuilder> NavMeshBuilt;

    [SerializeField] private DungeonGenerator_ChunkMesh dungeonGenerator;
    [SerializeField] private NavMeshSurface navMeshSurface;

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
        if (navMeshSurface == null)
        {
            Debug.LogWarning("DungeonNavMeshBuilder requires a NavMeshSurface reference.", this);
            return;
        }

        Debug.Log("[DungeonNavMeshBuilder] Dungeon generated. Building NavMesh.", this);
        navMeshSurface.BuildNavMesh();
        Debug.Log("[DungeonNavMeshBuilder] NavMesh build completed after dungeon generation.", this);
        NavMeshBuilt?.Invoke(this);
    }
}
