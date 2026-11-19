using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Executes native Unity 3D physics queries and CharacterController.Move against copies of
/// the generated solid BoxColliders in an isolated physics scene. It does not validate server
/// collision code, AI navigation, or latency. Call in Play Mode with generated prop fixtures.
/// </summary>
public static class DungeonPropPhysicsValidation
{
    public static AsyncOperation PendingSceneUnload { get; private set; }
    private const float PlayerRadius = .5f;
    private const float PlayerHeight = 2f;
    private const float PlayerCenterY = 1f;
    private const float PlayerSkinWidth = .08f;
    private const float PlayerStepOffset = .3f;
    private const float ProjectileRadius = .12f;
    private const float FeetClearance = .05f;
    private const float ApproachGap = 2.5f;
    private const float MoveStep = .1f;

    private static readonly DungeonPropLayout.Kind[] RequiredKinds =
    {
        DungeonPropLayout.Kind.Crate, DungeonPropLayout.Kind.Table, DungeonPropLayout.Kind.Rock
    };

    /// <summary>
    /// Returns the number of successful assertions. The supplied stage must contain a floor
    /// crate, table, and rock, each with its real enabled non-trigger Wall-layer BoxCollider.
    /// Throws on missing fixtures, unavailable native physics, or any failed collision check.
    /// Never reports a passing run when a native physics operation could not be executed.
    /// </summary>
    public static int Run(GameObject stage)
    {
        if (stage == null) throw new ArgumentNullException(nameof(stage));
        if (!Application.isPlaying)
            throw new NotSupportedException("Unity creates isolated Physics3D scenes only in Play Mode. " +
                "Use DungeonPropPhysicsRuntimeValidation.Start() and inspect its asynchronous result.");
        PendingSceneUnload = null;
        var sources = SelectSources(stage);
        int checks = 0;
        int wallLayer = LayerMask.NameToLayer("Wall");
        int playerLayer = LayerMask.NameToLayer("Player");
        Require(wallLayer >= 0 && playerLayer >= 0, "Wall/Player layers are not configured.", ref checks);
        Require(!Physics.GetIgnoreLayerCollision(playerLayer, wallLayer),
            "Player and Wall layers ignore one another in the physics collision matrix.", ref checks);

        var previousActiveScene = SceneManager.GetActiveScene();
        Scene isolatedScene = default;
        var temporaryRoots = new List<GameObject>();
        try
        {
            // Preview scenes need not have a usable local physics world. Always create a
            // separate Physics3D scene and address its PhysicsScene explicitly.
            isolatedScene = SceneManager.CreateScene("DungeonPropPhysics_" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            PhysicsScene physics = isolatedScene.GetPhysicsScene();
            Require(physics.IsValid(), "Unity did not create an isolated 3D physics scene.", ref checks);

            foreach (var kind in RequiredKinds)
            {
                var source = sources[kind];
                ValidateOne(kind, source, isolatedScene, physics, wallLayer, playerLayer, temporaryRoots, ref checks);
                // Remove each fixture before the next native query; fixtures must never
                // accidentally provide one another's collision result.
                DestroyTemporaryRoots(temporaryRoots);
                Physics.SyncTransforms();
            }

            Debug.Log($"DUNGEON_PROP_PHYSICS_PASS: {checks} assertions; Crate/Table/Rock; " +
                "native isolated PhysicsScene capsule casts, CharacterController.Move, Wall raycasts, " +
                "0.12m projectile sphere casts, and clear-lane negative controls. " +
                "Actual Play Mode physics fixtures; not a live client/server or NavMesh test.");
            return checks;
        }
        finally
        {
            DestroyTemporaryRoots(temporaryRoots);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            if (isolatedScene.IsValid() && isolatedScene.isLoaded)
            {
                if (Application.isPlaying) PendingSceneUnload = SceneManager.UnloadSceneAsync(isolatedScene);
                else EditorSceneManager.CloseScene(isolatedScene, true);
            }
        }
    }

    private static Dictionary<DungeonPropLayout.Kind, BoxCollider> SelectSources(GameObject stage)
    {
        var selected = new Dictionary<DungeonPropLayout.Kind, BoxCollider>();
        foreach (var prop in stage.GetComponentsInChildren<DungeonPropObstacle>(true))
        {
            if (prop == null || prop.Supported || !prop.gameObject.activeInHierarchy) continue;
            if (Array.IndexOf(RequiredKinds, prop.Kind) < 0) continue;
            var collider = prop.GetComponent<BoxCollider>();
            if (collider == null || !collider.enabled || collider.isTrigger) continue;
            // Prefer the larger representative floor prop, avoiding a stacked decorative box.
            if (!selected.TryGetValue(prop.Kind, out var prior) ||
                collider.bounds.size.y > prior.bounds.size.y) selected[prop.Kind] = collider;
        }
        foreach (var kind in RequiredKinds)
            if (!selected.ContainsKey(kind))
                throw new InvalidOperationException("Dungeon prop physics fixture is missing a solid floor " + kind +
                    ". Call Run after a generated composition contains Crate, Table, and Rock together.");
        return selected;
    }

    private static void ValidateOne(DungeonPropLayout.Kind kind, BoxCollider source, Scene scene,
        PhysicsScene physics, int wallLayer, int playerLayer, List<GameObject> temporaryRoots, ref int checks)
    {
        string label = kind + " (" + source.name + ")";
        Require(source.gameObject.layer == wallLayer, label + " is not on the Wall layer.", ref checks);
        Require(source.bounds.size.y > PlayerStepOffset + PlayerSkinWidth,
            label + " is too low to be a meaningful standing-player obstruction fixture.", ref checks);

        // Copy collider geometry and its effective world transform, rather than reconstructing
        // a synthetic obstacle from the planner's nominal dimensions. Translate the source's
        // floor to y=0 for isolation; original scene objects and layers remain untouched.
        Bounds originalBounds = source.bounds;
        Vector3 offset = new Vector3(originalBounds.center.x, originalBounds.min.y, originalBounds.center.z);
        var obstacleObject = CreateObject(label, scene, temporaryRoots);
        obstacleObject.layer = source.gameObject.layer;
        obstacleObject.transform.SetPositionAndRotation(source.transform.position - offset, source.transform.rotation);
        obstacleObject.transform.localScale = source.transform.lossyScale;
        var obstacle = obstacleObject.AddComponent<BoxCollider>();
        obstacle.center = source.center;
        obstacle.size = source.size;
        obstacle.isTrigger = source.isTrigger;
        obstacle.includeLayers = source.includeLayers;
        obstacle.excludeLayers = source.excludeLayers;
        obstacle.layerOverridePriority = source.layerOverridePriority;
        Physics.SyncTransforms();

        Bounds bounds = obstacle.bounds;
        Require(Vector3.Distance(bounds.size, originalBounds.size) < .002f &&
            Vector3.Distance(bounds.center, originalBounds.center - offset) < .002f,
            label + " collider-copy transform disagrees with the actual generated collider.", ref checks);

        int mask = 1 << wallLayer;
        Vector3 direction = Vector3.forward;
        Vector3 feet = new Vector3(bounds.center.x, bounds.min.y + FeetClearance, bounds.min.z - ApproachGap);
        float distance = bounds.size.z + ApproachGap * 2f;
        Vector3 lowerCenter = feet + Vector3.up * PlayerRadius;
        Vector3 upperCenter = feet + Vector3.up * (PlayerHeight - PlayerRadius);

        bool capsuleHit = physics.CapsuleCast(lowerCenter, upperCenter, PlayerRadius, direction,
            out RaycastHit capsule, distance, mask, QueryTriggerInteraction.Ignore);
        Require(capsuleHit && capsule.collider == obstacle && capsule.distance > 0 && capsule.distance < distance,
            label + " did not block the native player-sized capsule sweep.", ref checks);
        Require(capsule.normal.z < -.5f, label + " capsule did not hit the approached face.", ref checks);

        // The controller skin permits small contact penetration. Also test the conservative
        // inner capsule so a nominal-radius-only touch cannot be mistaken for a solid block.
        bool innerHit = physics.CapsuleCast(lowerCenter, upperCenter, PlayerRadius - PlayerSkinWidth, direction,
            out RaycastHit inner, distance, mask, QueryTriggerInteraction.Ignore);
        Require(innerHit && inner.collider == obstacle, label + " failed the skin-adjusted capsule sweep.", ref checks);

        Vector3 shot = new Vector3(bounds.center.x, bounds.center.y, feet.z);
        bool rayHit = physics.Raycast(shot, direction, out RaycastHit ray, distance, mask, QueryTriggerInteraction.Ignore);
        Require(rayHit && ray.collider == obstacle, label + " is invisible to a Wall-layer projectile ray.", ref checks);
        bool sphereHit = physics.SphereCast(shot, ProjectileRadius, direction, out RaycastHit sphere,
            distance, mask, QueryTriggerInteraction.Ignore);
        Require(sphereHit && sphere.collider == obstacle && sphere.distance <= ray.distance,
            label + " did not block the 0.12m projectile sphere sweep.", ref checks);

        Vector3 clearOffset = Vector3.right * (bounds.extents.x + PlayerRadius + 1f);
        Require(!physics.CapsuleCast(lowerCenter + clearOffset, upperCenter + clearOffset, PlayerRadius,
            direction, out _, distance, mask, QueryTriggerInteraction.Ignore),
            label + " incorrectly blocked the control lane outside the prop.", ref checks);
        Require(!physics.SphereCast(shot + clearOffset, ProjectileRadius, direction, out _, distance,
            mask, QueryTriggerInteraction.Ignore), label + " projectile control lane was not clear.", ref checks);

        // Native CharacterController.Move is exercised, not inferred from capsule-cast results.
        // A support floor is excluded from the preceding Wall-only queries.
        var floorObject = CreateObject("Controller support floor", scene, temporaryRoots);
        floorObject.transform.position = new Vector3(0, bounds.min.y - .1f, 0);
        var floor = floorObject.AddComponent<BoxCollider>();
        floor.size = new Vector3(40, .2f, 40);

        var playerObject = CreateObject("Player controller collision probe", scene, temporaryRoots);
        playerObject.layer = playerLayer;
        playerObject.transform.position = feet;
        var controller = playerObject.AddComponent<CharacterController>();
        controller.radius = PlayerRadius;
        controller.height = PlayerHeight;
        controller.center = Vector3.up * PlayerCenterY;
        controller.skinWidth = PlayerSkinWidth;
        controller.stepOffset = PlayerStepOffset;
        controller.slopeLimit = 45f;
        controller.minMoveDistance = .001f;
        Physics.SyncTransforms();

        bool touchedSide = false;
        int steps = Mathf.CeilToInt(distance / MoveStep);
        for (int i = 0; i < steps; i++)
        {
            CollisionFlags flags = controller.Move(direction * MoveStep + Vector3.down * .02f);
            touchedSide |= (flags & CollisionFlags.Sides) != 0;
        }
        float actualZ = playerObject.transform.position.z;
        Require(actualZ > feet.z + .5f,
            label + " CharacterController.Move did not advance in this Editor physics scene; runtime validation is required.", ref checks);
        Require(touchedSide, label + " CharacterController.Move never reported side contact.", ref checks);
        Require(actualZ <= bounds.min.z - PlayerRadius + PlayerSkinWidth * 2f + .03f,
            label + " CharacterController.Move crossed the solid prop.", ref checks);
        Require(playerObject.transform.position.y < bounds.max.y - .1f,
            label + " controller climbed the fixture rather than proving horizontal blockage.", ref checks);
    }

    private static GameObject CreateObject(string name, Scene scene, List<GameObject> roots)
    {
        var result = new GameObject(name) { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(result, scene);
        roots.Add(result);
        return result;
    }

    private static void DestroyTemporaryRoots(List<GameObject> roots)
    {
        for (int i = roots.Count - 1; i >= 0; i--)
            if (roots[i] != null) UnityEngine.Object.DestroyImmediate(roots[i]);
        roots.Clear();
    }

    private static void Require(bool value, string message, ref int checks)
    {
        checks++;
        if (!value) throw new InvalidOperationException("Dungeon prop native physics: " + message);
    }
}
