#include "DungeonTypes.h"
#include "CSharpRandom.h"

#include <cmath>

bool BSPNode::TrySplit(CSharpRandom& random, int minLeafSize, float aspectBias)
{
    if (!IsLeaf()) return false;

    int width = bounds.size.x;
    int depth = bounds.size.z;
    bool canSplitVertical   = width >= minLeafSize * 2;
    bool canSplitHorizontal = depth >= minLeafSize * 2;
    if (!canSplitVertical && !canSplitHorizontal) return false;

    bool splitVertical;
    float ratio = (float)width / (float)depth;
    if (ratio >= aspectBias)
    {
        splitVertical = true;
    }
    else if (1.f / ratio >= aspectBias)
    {
        splitVertical = false;
    }
    else
    {
        splitVertical = (random.NextDouble() > 0.5);
    }

    if (splitVertical && !canSplitVertical)       splitVertical = false;
    else if (!splitVertical && !canSplitHorizontal) splitVertical = true;

    int maxSize = splitVertical ? width : depth;
    // C#: random.Next(minLeafSize, max - minLeafSize + 1)
    int split = random.Next(minLeafSize, maxSize - minLeafSize + 1);

    if (splitVertical)
    {
        IntBounds leftB(
            bounds.position,
            IntVec3(split, bounds.size.y, bounds.size.z)
        );
        IntBounds rightB(
            IntVec3(bounds.position.x + split, bounds.position.y, bounds.position.z),
            IntVec3(bounds.size.x - split, bounds.size.y, bounds.size.z)
        );
        left  = std::make_unique<BSPNode>(leftB);
        right = std::make_unique<BSPNode>(rightB);
    }
    else
    {
        IntBounds leftB(
            bounds.position,
            IntVec3(bounds.size.x, bounds.size.y, split)
        );
        IntBounds rightB(
            IntVec3(bounds.position.x, bounds.position.y, bounds.position.z + split),
            IntVec3(bounds.size.x, bounds.size.y, bounds.size.z - split)
        );
        left  = std::make_unique<BSPNode>(leftB);
        right = std::make_unique<BSPNode>(rightB);
    }
    return true;
}

void BSPNode::CollectLeaves(std::vector<BSPNode*>& leaves)
{
    if (IsLeaf())
    {
        leaves.push_back(this);
        return;
    }
    if (left)  left->CollectLeaves(leaves);
    if (right) right->CollectLeaves(leaves);
}

IntVec3 BSPNode::GetRoomCenter() const
{
    if (hasRoom)
    {
        // FloorToInt 대응
        Vec3 c = roomBounds.center();
        return IntVec3((int)std::floor(c.x), (int)std::floor(c.y), (int)std::floor(c.z));
    }
    if (left)  return left->GetRoomCenter();
    if (right) return right->GetRoomCenter();
    Vec3 c = bounds.center();
    return IntVec3((int)std::floor(c.x), (int)std::floor(c.y), (int)std::floor(c.z));
}

Room* BSPNode::GetRoomData() const
{
    if (roomData) return roomData;
    if (left)
    {
        Room* r = left->GetRoomData();
        if (r) return r;
    }
    if (right) return right->GetRoomData();
    return nullptr;
}
