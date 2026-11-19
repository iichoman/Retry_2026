using UnityEngine;

public static class SimplePropCollider
{
    // Runtime instances and authored prefabs use one smooth, frictionless solid box.
    private static PhysicsMaterial material;
    public static BoxCollider Apply(GameObject root, Vector3 center, Vector3 size, bool solid = true)
    {
        var box = root.GetComponent<BoxCollider>();
        if (box == null) box = root.AddComponent<BoxCollider>();
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = collider == box && solid;
        box.center = center; box.size = size; box.isTrigger = false;
        if (material == null) material = Resources.Load<PhysicsMaterial>("DungeonPropSlide");
        if (material == null) material = new PhysicsMaterial("Dungeon Prop Slide") { dynamicFriction = 0, staticFriction = 0, bounciness = 0, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
        box.sharedMaterial = material;
        return box;
    }
}
