using System;
using System.Collections.Generic;
using UnityEngine;

public class StartRoomManager
{
    public const int PlayersPerTeam = 3;

    public List<StartRoom> BuildStartRoomCandidates(
        int mapSize,
        int roomSize,
        int edgeMargin,
        int thickness
    )
    {
        var candidates = new List<StartRoom>(16);

        int usable = mapSize - 2 * edgeMargin;
        int totalRooms = 4 * roomSize;
        int gap = (usable - totalRooms) / 5;

        int[] slots = new int[4];
        for (int i = 0; i < 4; i++)
        {
            slots[i] = edgeMargin + gap * (i + 1) + roomSize * i;
        }

        // South (z = edgeMargin)
        for (int i = 0; i < 4; i++)
        {
            candidates.Add(CreateStartRoom(
                i,
                new Vector3Int(slots[i], 0, edgeMargin),
                roomSize,
                thickness,
                Quaternion.Euler(0f, 0f, 0f)
            ));
        }

        // North (z = mapSize - edgeMargin - roomSize)
        int northZ = mapSize - edgeMargin - roomSize;
        for (int i = 0; i < 4; i++)
        {
            candidates.Add(CreateStartRoom(
                4 + i,
                new Vector3Int(slots[i], 0, northZ),
                roomSize,
                thickness,
                Quaternion.Euler(0f, 180f, 0f)
            ));
        }

        // West (x = edgeMargin)
        for (int i = 0; i < 4; i++)
        {
            candidates.Add(CreateStartRoom(
                8 + i,
                new Vector3Int(edgeMargin, 0, slots[i]),
                roomSize,
                thickness,
                Quaternion.Euler(0f, 90f, 0f)
            ));
        }

        // East (x = mapSize - edgeMargin - roomSize)
        int eastX = mapSize - edgeMargin - roomSize;
        for (int i = 0; i < 4; i++)
        {
            candidates.Add(CreateStartRoom(
                12 + i,
                new Vector3Int(eastX, 0, slots[i]),
                roomSize,
                thickness,
                Quaternion.Euler(0f, -90f, 0f)
            ));
        }

        return candidates;
    }

    public List<int> PickStartSlots(int teamCount, int seed)
    {
        int count = Mathf.Clamp(teamCount, 1, 16);
        var random = new System.Random(seed);
        var indices = new List<int>(16);
        for (int i = 0; i < 16; i++)
        {
            indices.Add(i);
        }

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            int temp = indices[i];
            indices[i] = indices[j];
            indices[j] = temp;
        }

        return indices.GetRange(0, count);
    }

    public List<StartRoom> AssignTeams(
        List<StartRoom> candidates,
        int teamCount,
        int seed
    )
    {
        var picked = PickStartSlots(teamCount, seed);
        var results = new List<StartRoom>(picked.Count);

        for (int i = 0; i < picked.Count; i++)
        {
            int index = picked[i];
            StartRoom room = candidates[index];
            room.occupied = true;
            room.teamId = i;
            results.Add(room);
        }

        return results;
    }

    private static StartRoom CreateStartRoom(
        int slotIndex,
        Vector3Int position,
        int roomSize,
        int thickness,
        Quaternion rotation
    )
    {
        var bounds = new BoundsInt(
            position,
            new Vector3Int(roomSize, thickness, roomSize)
        );
        float spawnY = bounds.yMax + 0.5f;
        Vector3 teamAnchorPosition = new Vector3(bounds.center.x, spawnY, bounds.center.z);
        List<Vector3> playerSpawnPositions = BuildPlayerSpawnPositions(teamAnchorPosition, rotation, roomSize);

        return new StartRoom
        {
            slotIndex = slotIndex,
            bounds = bounds,
            teamAnchorPosition = teamAnchorPosition,
            spawnRotation = rotation,
            occupied = false,
            teamId = -1,
            playerSpawnPositions = playerSpawnPositions
        };
    }

    private static List<Vector3> BuildPlayerSpawnPositions(
        Vector3 anchor,
        Quaternion rotation,
        int roomSize
    )
    {
        float spacing = Mathf.Clamp(roomSize * 0.18f, 1.5f, 3.5f);
        Vector3 right = rotation * Vector3.right;

        return new List<Vector3>(PlayersPerTeam)
        {
            anchor - right * spacing,
            anchor,
            anchor + right * spacing
        };
    }
}
