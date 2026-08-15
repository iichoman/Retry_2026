using System.Collections.Generic;
using UnityEngine;

public class StartRoom
{
    public int slotIndex;
    public BoundsInt bounds;
    public Vector3 teamAnchorPosition;
    public Quaternion spawnRotation;
    public bool occupied;
    public int teamId;
    public List<Vector3> playerSpawnPositions = new();

    public Vector3 GetSpawnPositionForMember(int memberIndex)
    {
        if (playerSpawnPositions == null || playerSpawnPositions.Count == 0)
        {
            return teamAnchorPosition;
        }

        int safeIndex = Mathf.Clamp(memberIndex, 0, playerSpawnPositions.Count - 1);
        return playerSpawnPositions[safeIndex];
    }
}
