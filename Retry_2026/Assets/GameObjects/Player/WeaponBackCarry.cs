using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves the equipped model between its authored grip and the actor's back.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(300)]
public sealed class WeaponBackCarry : MonoBehaviour
{
    sealed class Grip
    {
        public Weapon_Sword weapon;
        public Vector3 position;
        public Quaternion rotation;
        public Quaternion backRotation;
        public Transform bladeBase, bladeTip;
    }
    readonly List<Grip> grips = new();
    Player_Attack attack;
    Player_State health;
    Animator animator;
    Transform chest;
    bool bound;
    public bool IsCarried { get; private set; }

    public void Bind()
    {
        if (bound) return;
        attack = GetComponent<Player_Attack>(); health = GetComponent<Player_State>();
        animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        foreach (var weapon in GetComponentsInChildren<Weapon_Sword>(true))
        {
            if (weapon is GreatswordWeapon great) great.CorrectBladeFacing();
            var grip = new Grip { weapon = weapon, position = weapon.transform.localPosition, rotation = weapon.transform.localRotation };
            foreach (var part in weapon.GetComponentsInChildren<Transform>(true))
            {
                if (part.name == "Blade Trail Base") grip.bladeBase = part;
                if (part.name == "Blade Trail Tip") grip.bladeTip = part;
            }
            if (grip.bladeBase != null && grip.bladeTip != null)
            {
                var direction = grip.bladeTip.position - grip.bladeBase.position;
                var desired = (-transform.up - transform.right * .42f).normalized;
                grip.backRotation = Quaternion.Inverse(transform.rotation) * Quaternion.FromToRotation(direction.normalized, desired) * weapon.transform.rotation;
            }
            grips.Add(grip);
        }
        bound = true;
    }
    void Awake() => Bind();
    void Update() => RestoreGrip();
    void LateUpdate() => ApplyPose(ShouldDraw());
    void OnDisable() => RestoreGrip();
    public void RestoreGrip()
    {
        foreach (var grip in grips) if (grip.weapon != null)
        { grip.weapon.transform.localPosition = grip.position; grip.weapon.transform.localRotation = grip.rotation; }
        IsCarried = false;
    }
    bool ShouldDraw()
    {
        if (health != null && health.IsDead) return false;
        if (attack != null && attack.ActiveAnimationComboIndex > 0 || PlayerSkillView.Busy(gameObject)
            || GetComponent<PlayerDefenseController>()?.IsParrying == true) return true;
        if (animator == null || !animator.isActiveAndEnabled) return false;
        return DrawnState(animator.GetCurrentAnimatorStateInfo(0))
            || animator.IsInTransition(0) && DrawnState(animator.GetNextAnimatorStateInfo(0));
    }
    static bool DrawnState(AnimatorStateInfo state)
    {
        if (state.IsName("SkillE") || state.IsName("SkillQ")) return true;
        for (int i = 1; i <= 4; i++) if (state.shortNameHash == Animator.StringToHash("sword_attack_" + i)) return true;
        for(int i=1;i<=3;i++)if(state.IsName("greatsword_attack_"+i))return true;return false;
    }
    public void ApplyPose(bool drawn)
    {
        Bind(); RestoreGrip();
        if (drawn || attack == null || !attack.IsSwordEquipped) return;
        foreach (var grip in grips)
        {
            if (grip.weapon == null || !grip.weapon.gameObject.activeInHierarchy || grip.weapon.WeaponId != attack.EquippedWeaponId
                || grip.bladeBase == null || grip.bladeTip == null) continue;
            Vector3 direction = grip.bladeTip.position - grip.bladeBase.position;
            if (direction.sqrMagnitude < .0001f) continue;
            var root = grip.weapon.transform;
            // Hilt above the right shoulder, blade diagonally down the back. No extra hitbox or duplicate model.
            root.rotation = transform.rotation * grip.backRotation;
            float scale = GetComponent<CharacterController>()?.height * .5f ?? 1f;
            var shoulder = chest != null ? chest.position : transform.position + transform.up * 1.35f * scale;
            var anchor = shoulder + (transform.up * .4f + transform.right * .28f - transform.forward * .30f) * scale;
            root.position += anchor - grip.bladeBase.position;
            IsCarried = true;
        }
    }
}
