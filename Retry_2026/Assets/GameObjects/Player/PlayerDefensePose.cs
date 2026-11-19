using UnityEngine;

/// <summary>
/// Temporary, visual-only defense poses. The authoritative root and collider are never modified.
/// Replace this layer with authored clips later without changing movement or defense rules.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class PlayerDefensePose : MonoBehaviour
{
    [SerializeField] private Animator rigAnimator;
    [SerializeField] private Transform hips;
    [SerializeField] private Transform chest;
    [SerializeField] private Transform rightUpperArm, rightLowerArm, rightHand;
    [SerializeField] private Transform leftUpperArm, leftLowerArm, leftHand;
    [SerializeField] private Transform rightUpperLeg, rightLowerLeg, rightFoot;
    [SerializeField] private Transform leftUpperLeg, leftLowerLeg, leftFoot;
    [SerializeField] private Transform bladeBase, bladeTip;
    [SerializeField, Range(0f, 35f)] private float dashLeanDegrees = 24f;
    [SerializeField, Range(0f, 0.12f)] private float dashLowerHeightRatio = 0.065f;
    [SerializeField, Min(1f)] private float poseBlendSpeed = 14f;

    private PlayerDefenseController defense;
    private Player_Attack attack;
    private Player_State state;
    private PlayerDefenseAnimation authoredAnimation;
    private BonePose[] bones;
    private float bodyHeight = 2f;
    private float dashWeight, parryWeight;
    private bool applied;

    private struct BonePose
    {
        public Transform Bone;
        public Vector3 Position, WrittenPosition;
        public Quaternion Rotation, WrittenRotation;
    }

    public bool HasRequiredBones => hips != null && chest != null &&
        rightUpperArm != null && rightLowerArm != null && rightHand != null &&
        leftUpperArm != null && leftLowerArm != null && leftHand != null &&
        rightUpperLeg != null && rightLowerLeg != null && rightFoot != null &&
        leftUpperLeg != null && leftLowerLeg != null && leftFoot != null &&
        bladeBase != null && bladeTip != null;

    private void Awake()
    {
        defense = GetComponent<PlayerDefenseController>();
        attack = GetComponent<Player_Attack>();
        state = GetComponent<Player_State>();
        authoredAnimation = GetComponent<PlayerDefenseAnimation>();
        ConfigureReferences();
    }

    private void OnEnable()
    {
        if (attack == null) attack = GetComponent<Player_Attack>();
        if (attack != null) attack.EquippedWeaponChanged += OnWeaponChanged;
    }

    private void OnDisable()
    {
        if (attack != null) attack.EquippedWeaponChanged -= OnWeaponChanged;
        ResetPose();
    }

    private void OnWeaponChanged(string _) => ResetPose();

    // Remove our last contribution before Animator evaluates the new frame. This also handles
    // culling/disabled animators where no new animation pose is written at all.
    private void Update() => RestoreContribution();

    private void LateUpdate()
    {
        if (authoredAnimation != null && authoredAnimation.IsPlaying) { ResetPose(); return; }
        if (defense == null || !defense.isActiveAndEnabled || state != null && (state.IsDead || state.IsHit))
        {
            ResetPose();
            return;
        }
        float dashTarget = defense.IsDashing ? Envelope(defense.ActionProgress) : 0f;
        float parryTarget = defense.IsParrying && attack != null && attack.IsSwordEquipped
            ? Envelope(defense.ActionProgress) : 0f;
        dashWeight = Mathf.MoveTowards(dashWeight, dashTarget, poseBlendSpeed * Time.deltaTime);
        parryWeight = Mathf.MoveTowards(parryWeight, parryTarget, poseBlendSpeed * Time.deltaTime);
        ApplyPose(dashWeight, parryWeight, defense.DashDirection);
    }

    private static float Envelope(float progress)
    {
        progress = Mathf.Clamp01(progress);
        return Mathf.SmoothStep(0f, 1f, progress / 0.12f) * Mathf.SmoothStep(0f, 1f, (1f - progress) / 0.25f);
    }

    /// <summary>One-time binding, also used by the content builder to serialize actual references.</summary>
    public bool ConfigureReferences()
    {
        RestoreContribution();
        if (rigAnimator == null) rigAnimator = GetComponentInChildren<Animator>(true);
        var hierarchy = GetComponentsInChildren<Transform>(true);
        hips = Resolve(hips, HumanBodyBones.Hips, "Hips", hierarchy);
        chest = Resolve(chest, HumanBodyBones.Chest, "Spine1", hierarchy);
        rightUpperArm = Resolve(rightUpperArm, HumanBodyBones.RightUpperArm, "RightArm", hierarchy);
        rightLowerArm = Resolve(rightLowerArm, HumanBodyBones.RightLowerArm, "RightForeArm", hierarchy);
        rightHand = Resolve(rightHand, HumanBodyBones.RightHand, "RightHand", hierarchy);
        leftUpperArm = Resolve(leftUpperArm, HumanBodyBones.LeftUpperArm, "LeftArm", hierarchy);
        leftLowerArm = Resolve(leftLowerArm, HumanBodyBones.LeftLowerArm, "LeftForeArm", hierarchy);
        leftHand = Resolve(leftHand, HumanBodyBones.LeftHand, "LeftHand", hierarchy);
        rightUpperLeg = Resolve(rightUpperLeg, HumanBodyBones.RightUpperLeg, "RightUpLeg", hierarchy);
        rightLowerLeg = Resolve(rightLowerLeg, HumanBodyBones.RightLowerLeg, "RightLeg", hierarchy);
        rightFoot = Resolve(rightFoot, HumanBodyBones.RightFoot, "RightFoot", hierarchy);
        leftUpperLeg = Resolve(leftUpperLeg, HumanBodyBones.LeftUpperLeg, "LeftUpLeg", hierarchy);
        leftLowerLeg = Resolve(leftLowerLeg, HumanBodyBones.LeftLowerLeg, "LeftLeg", hierarchy);
        leftFoot = Resolve(leftFoot, HumanBodyBones.LeftFoot, "LeftFoot", hierarchy);
        if (bladeBase == null) bladeBase = FindNamed(hierarchy, "Blade Trail Base");
        if (bladeTip == null) bladeTip = FindNamed(hierarchy, "Blade Trail Tip");

        Transform[] references = { hips, chest, rightUpperArm, rightLowerArm, rightHand,
            leftUpperArm, leftLowerArm, leftHand, rightUpperLeg, rightLowerLeg, rightFoot,
            leftUpperLeg, leftLowerLeg, leftFoot };
        bones = new BonePose[references.Length];
        for (int i = 0; i < references.Length; i++) bones[i].Bone = references[i];
        var capsule = GetComponent<CharacterController>();
        bodyHeight = capsule != null ? capsule.height * Mathf.Abs(transform.lossyScale.y) : 2f;
        bodyHeight = Mathf.Max(0.5f, bodyHeight);
        return HasRequiredBones;
    }

    private Transform Resolve(Transform current, HumanBodyBones id, string fallback, Transform[] hierarchy)
    {
        if (current != null && current != transform && current.IsChildOf(transform)) return current;
        Transform resolved = null;
        if (rigAnimator != null && rigAnimator.avatar != null && rigAnimator.avatar.isValid && rigAnimator.isHuman)
            resolved = rigAnimator.GetBoneTransform(id);
        return resolved != null && resolved != transform ? resolved : FindNamed(hierarchy, fallback);
    }

    private static Transform FindNamed(Transform[] hierarchy, string name)
    {
        foreach (var bone in hierarchy)
            if (bone.name == name || bone.name.EndsWith(":" + name, System.StringComparison.Ordinal)) return bone;
        return null;
    }

    public void ResetPose()
    {
        RestoreContribution();
        dashWeight = parryWeight = 0f;
    }

    private void RestoreContribution()
    {
        if (!applied || bones == null) return;
        for (int i = 0; i < bones.Length; i++)
        {
            var sample = bones[i];
            if (sample.Bone == null) continue;
            // An Animator, hit reaction, or another pose layer may already have replaced it.
            // Only restore values that are still ours; never overwrite a newer pose.
            if ((sample.Bone.localPosition - sample.WrittenPosition).sqrMagnitude < 0.00000001f)
                sample.Bone.localPosition = sample.Position;
            if (Quaternion.Angle(sample.Bone.localRotation, sample.WrittenRotation) < 0.01f)
                sample.Bone.localRotation = sample.Rotation;
        }
        applied = false;
    }

    private void ApplyPose(float dash, float parry, Vector3 direction)
    {
        RestoreContribution();
        if (bones == null || hips == null || chest == null || Mathf.Max(dash, parry) < 0.001f) return;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].Bone == null) continue;
            bones[i].Position = bones[i].Bone.localPosition;
            bones[i].Rotation = bones[i].Bone.localRotation;
        }

        Vector3 up = transform.up;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        Vector3 right = transform.right;
        direction = Vector3.ProjectOnPlane(direction, up);
        if (direction.sqrMagnitude < 0.001f) direction = -forward;
        direction.Normalize();

        Vector3 rightFootPosition = rightFoot != null ? rightFoot.position : Vector3.zero;
        Vector3 leftFootPosition = leftFoot != null ? leftFoot.position : Vector3.zero;
        Quaternion rightFootRotation = rightFoot != null ? rightFoot.rotation : Quaternion.identity;
        Quaternion leftFootRotation = leftFoot != null ? leftFoot.rotation : Quaternion.identity;
        hips.position -= up * bodyHeight * (dashLowerHeightRatio * dash + 0.02f * parry);
        chest.rotation = Quaternion.AngleAxis(dashLeanDegrees * dash, Vector3.Cross(up, direction)) * chest.rotation;
        chest.rotation = Quaternion.AngleAxis(6f * parry, right) * chest.rotation;

        // Keep both animated foot positions while lowering the visual pelvis.
        SolveLimb(rightUpperLeg, rightLowerLeg, rightFoot, rightFootPosition, hips.position + forward * bodyHeight);
        SolveLimb(leftUpperLeg, leftLowerLeg, leftFoot, leftFootPosition, hips.position + forward * bodyHeight);
        if (rightFoot != null) rightFoot.rotation = rightFootRotation;
        if (leftFoot != null) leftFoot.rotation = leftFootRotation;

        if (parry > 0f && rightHand != null && leftHand != null)
        {
            Vector3 guard = chest.position + forward * bodyHeight * 0.18f - up * bodyHeight * 0.035f;
            Vector3 rightTarget = guard + right * bodyHeight * 0.07f;
            Vector3 leftTarget = guard - right * bodyHeight * 0.075f - up * bodyHeight * 0.02f;
            SolveLimb(rightUpperArm, rightLowerArm, rightHand, Vector3.Lerp(rightHand.position, rightTarget, parry),
                chest.position + right * bodyHeight * 0.32f - up * bodyHeight * 0.25f);
            SolveLimb(leftUpperArm, leftLowerArm, leftHand, Vector3.Lerp(leftHand.position, leftTarget, parry),
                chest.position - right * bodyHeight * 0.30f - up * bodyHeight * 0.25f);
            if (bladeBase != null && bladeTip != null)
            {
                Vector3 bladeDirection = bladeTip.position - bladeBase.position;
                if (bladeDirection.sqrMagnitude > 0.0001f)
                {
                    Vector3 guardDirection = (up + right * 0.16f + forward * 0.10f).normalized;
                    Quaternion upright = Quaternion.FromToRotation(bladeDirection, guardDirection) * rightHand.rotation;
                    rightHand.rotation = Quaternion.Slerp(rightHand.rotation, upright, parry);
                }
            }
        }
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].Bone == null) continue;
            bones[i].WrittenPosition = bones[i].Bone.localPosition;
            bones[i].WrittenRotation = bones[i].Bone.localRotation;
        }
        applied = true;
    }

    private static void SolveLimb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        if (upper == null || lower == null || end == null) return;
        Vector3 start = upper.position;
        float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, end.position);
        Vector3 reach = target - start;
        if (a < 0.0001f || b < 0.0001f || reach.sqrMagnitude < 0.000001f) return;
        float distance = Mathf.Clamp(reach.magnitude, Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);
        Vector3 axis = reach.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole - start, axis).normalized;
        if (bend.sqrMagnitude < 0.001f) bend = Vector3.ProjectOnPlane(lower.position - start, axis).normalized;
        if (bend.sqrMagnitude < 0.001f) return;
        float along = (a * a + distance * distance - b * b) / (2f * distance);
        float outward = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 elbow = start + axis * along + bend * outward;
        upper.rotation = Quaternion.FromToRotation(lower.position - start, elbow - start) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, target - lower.position) * lower.rotation;
    }

#if UNITY_EDITOR
    // Explicit preview only; this never starts an action, grants immunity, or moves the controller.
    public void PreviewPose(float dash, float parry, Vector3 direction)
    {
        if (bones == null) ConfigureReferences();
        ApplyPose(Mathf.Clamp01(dash), Mathf.Clamp01(parry), direction);
    }
#endif
}
