using System.Collections.Generic;
using UnityEngine;

public class BSPNode
{
    public BoundsInt bounds;
    public BSPNode left;
    public BSPNode right;
    public BoundsInt roomBounds;
    public bool hasRoom;
    public Room roomData;

    public BSPNode(BoundsInt bounds)
    {
        this.bounds = bounds;
    }

    public bool IsLeaf => left == null && right == null;

    public bool TrySplit(System.Random random, int minLeafSize, float aspectBias = 1.25f)
    {
        if (!IsLeaf)
        {
            return false;
        }

        int width = bounds.size.x;
        int depth = bounds.size.z;
        bool canSplitVertical = width >= minLeafSize * 2;
        bool canSplitHorizontal = depth >= minLeafSize * 2;
        if (!canSplitVertical && !canSplitHorizontal)
        {
            return false;
        }

        bool splitVertical;
        float ratio = (float)width / depth;
        if (ratio >= aspectBias)
        {
            splitVertical = true;
        }
        else if (1f / ratio >= aspectBias)
        {
            splitVertical = false;
        }
        else
        {
            splitVertical = random.NextDouble() > 0.5;
        }

        if (splitVertical && !canSplitVertical)
        {
            splitVertical = false;
        }
        else if (!splitVertical && !canSplitHorizontal)
        {
            splitVertical = true;
        }

        int max = splitVertical ? width : depth;
        int split = random.Next(minLeafSize, max - minLeafSize + 1);

        if (splitVertical)
        {
            var leftBounds = new BoundsInt(
                bounds.position,
                new Vector3Int(split, bounds.size.y, bounds.size.z)
            );
            var rightBounds = new BoundsInt(
                new Vector3Int(bounds.position.x + split, bounds.position.y, bounds.position.z),
                new Vector3Int(bounds.size.x - split, bounds.size.y, bounds.size.z)
            );
            left = new BSPNode(leftBounds);
            right = new BSPNode(rightBounds);
        }
        else
        {
            var leftBounds = new BoundsInt(
                bounds.position,
                new Vector3Int(bounds.size.x, bounds.size.y, split)
            );
            var rightBounds = new BoundsInt(
                new Vector3Int(bounds.position.x, bounds.position.y, bounds.position.z + split),
                new Vector3Int(bounds.size.x, bounds.size.y, bounds.size.z - split)
            );
            left = new BSPNode(leftBounds);
            right = new BSPNode(rightBounds);
        }

        return true;
    }

    public void CollectLeaves(List<BSPNode> leaves)
    {
        if (IsLeaf)
        {
            leaves.Add(this);
            return;
        }

        left?.CollectLeaves(leaves);
        right?.CollectLeaves(leaves);
    }

    public Vector3Int GetRoomCenter()
    {
        if (hasRoom)
        {
            return Vector3Int.FloorToInt(roomBounds.center);
        }

        if (left != null)
        {
            return left.GetRoomCenter();
        }

        if (right != null)
        {
            return right.GetRoomCenter();
        }

        return Vector3Int.FloorToInt(bounds.center);
    }

    public Room GetRoomData()
    {
        if (roomData != null)
        {
            return roomData;
        }

        Room leftRoom = left?.GetRoomData();
        if (leftRoom != null)
        {
            return leftRoom;
        }

        return right?.GetRoomData();
    }
}
