using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_Attack : MonoBehaviour
{
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private Player_Camera_Action cameraAction;
    [SerializeField] private Player_State playerState;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private string startingWeaponId = "test_sword";
    [SerializeField] private bool manageWeaponObjectVisibility = true;
    [SerializeField] private bool createOffhandGunVisual = true;
    [SerializeField] private Transform offhandGunSocket;
    [SerializeField] private HumanBodyBones offhandGunBone = HumanBodyBones.LeftHand;
    [SerializeField] private Vector3 offhandGunPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 offhandGunRotationOffset = Vector3.zero;
    [SerializeField] private Vector3 offhandGunScaleMultiplier = Vector3.one;

    private readonly List<IPlayerWeapon> weaponComponents = new List<IPlayerWeapon>();
    private readonly Dictionary<string, IPlayerWeapon> weaponsById = new Dictionary<string, IPlayerWeapon>(StringComparer.Ordinal);
    private readonly Dictionary<string, GameObject> offhandGunVisualsByWeaponId = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private IPlayerWeapon equippedWeapon;
    private Animator cachedAnimator;
    private bool warnedMissingOffhandGunParent;
    private bool previousAttackInput;

    public event Action<string> EquippedWeaponChanged;

    public string EquippedWeaponId => equippedWeapon != null ? equippedWeapon.WeaponId : string.Empty;
    public WeaponGrade CurrentWeaponGrade => equippedWeapon != null ? equippedWeapon.Grade : WeaponGrade.Common;
    public int CurrentAttackDamage => equippedWeapon != null ? equippedWeapon.AttackDamage : 0;
    public int ActiveAnimationComboIndex { get; private set; }

    private void Awake()
    {
        if (playerInput == null)
        {
            playerInput = GetComponent<Defalult_Input>();
        }

        if (cameraAction == null)
        {
            cameraAction = GetComponent<Player_Camera_Action>();
        }

        if (playerState == null)
        {
            playerState = GetComponent<Player_State>();
        }

        if (cameraTransform == null)
        {
            Player_Camera_Controller cameraController = GetComponent<Player_Camera_Controller>();
            if (cameraController != null)
            {
                cameraTransform = cameraController.CameraTransform;
            }
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        cachedAnimator = GetComponentInChildren<Animator>();

        CollectWeapons();
        EquipStartingWeapon();
    }

    private void Update()
    {
        if (equippedWeapon == null)
        {
            return;
        }

        equippedWeapon.Tick(Time.deltaTime);

        if (playerState != null && (playerState.IsDead || playerState.IsHit))
        {
            previousAttackInput = playerInput != null && playerInput.Attack;
            return;
        }

        bool currentAttackInput = playerInput != null && playerInput.Attack;
        bool attackPressedThisFrame = currentAttackInput && !previousAttackInput;
        previousAttackInput = currentAttackInput;

        if (attackPressedThisFrame)
        {
            if (ActiveAnimationComboIndex <= 0 && equippedWeapon is not Weapon_Gun)
            {
                FaceCameraForward();
            }

            equippedWeapon.RequestAttack();
        }
    }

    private void FaceCameraForward()
    {
        if (cameraTransform == null)
        {
            return;
        }

        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    public bool EquipWeapon(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return false;
        }

        if (!weaponsById.TryGetValue(weaponId, out IPlayerWeapon weapon))
        {
            return false;
        }

        SetEquippedWeapon(weapon);
        return true;
    }

    public bool TryConsumeAttackAnimationRequest(out int comboIndex)
    {
        comboIndex = 0;
        return equippedWeapon != null && equippedWeapon.TryConsumeAnimationRequest(out comboIndex);
    }

    public bool TryGetAttackAnimationState(int comboIndex, out string stateName)
    {
        stateName = string.Empty;
        return equippedWeapon != null && equippedWeapon.TryGetAnimationStateName(comboIndex, out stateName);
    }

    public void NotifyAttackAnimationStarted(int comboIndex)
    {
        ActiveAnimationComboIndex = comboIndex;
        cameraAction?.BeginComboAttack(comboIndex);
        equippedWeapon?.OnAnimationStarted(comboIndex);
    }

    public void NotifyAttackAnimationCompleted(int comboIndex)
    {
        if (equippedWeapon == null)
        {
            return;
        }

        equippedWeapon.OnAnimationCompleted(comboIndex);

        if (ActiveAnimationComboIndex == comboIndex)
        {
            ActiveAnimationComboIndex = 0;
        }

        cameraAction?.EndComboAttack(comboIndex);
    }

    public void CancelCurrentAttack()
    {
        ActiveAnimationComboIndex = 0;
        cameraAction?.CancelAction();
        equippedWeapon?.CancelAttack();
    }

    public void AnimEvent_BeginSwing()
    {
        if (equippedWeapon == null || ActiveAnimationComboIndex <= 0)
        {
            return;
        }

        cameraAction?.PlayImpact();
        equippedWeapon.OnAttackWindowOpened(ActiveAnimationComboIndex);
    }

    public void AnimEvent_EndSwing()
    {
        if (equippedWeapon == null || ActiveAnimationComboIndex <= 0)
        {
            return;
        }

        equippedWeapon.OnAttackWindowClosed(ActiveAnimationComboIndex);
    }

    public void AnimEvent_ComboWindowOpen()
    {
        if (equippedWeapon == null || ActiveAnimationComboIndex <= 0)
        {
            return;
        }

        equippedWeapon.OnComboWindowOpened(ActiveAnimationComboIndex);
    }

    public void AnimEvent_ComboCommit()
    {
        if (equippedWeapon == null || ActiveAnimationComboIndex <= 0)
        {
            return;
        }

        equippedWeapon.OnComboWindowCommitted(ActiveAnimationComboIndex);
    }

    public void AnimEvent_ComboWindowClose()
    {
        if (equippedWeapon == null || ActiveAnimationComboIndex <= 0)
        {
            return;
        }

        equippedWeapon.OnComboWindowClosed(ActiveAnimationComboIndex);
    }

    private void OnDisable()
    {
        previousAttackInput = false;
        ActiveAnimationComboIndex = 0;
        cameraAction?.CancelAction();
        equippedWeapon?.CancelAttack();
    }

    private void CollectWeapons()
    {
        weaponComponents.Clear();
        weaponsById.Clear();

        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not IPlayerWeapon weapon)
            {
                continue;
            }

            weapon.Initialize(this);
            weaponComponents.Add(weapon);
            RegisterWeapon(weapon);
        }

        if (weaponComponents.Count == 0)
        {
            Debug.LogWarning("No weapon implementing IPlayerWeapon was found on the player.", this);
        }
    }

    private void RegisterWeapon(IPlayerWeapon weapon)
    {
        if (string.IsNullOrWhiteSpace(weapon.WeaponId))
        {
            Debug.LogWarning("Weapon id is empty. This weapon cannot be equipped by id.", weapon as UnityEngine.Object);
            return;
        }

        if (weaponsById.ContainsKey(weapon.WeaponId))
        {
            Debug.LogWarning($"Duplicate weapon id detected: {weapon.WeaponId}. This weapon will be ignored during id lookup.", weapon as UnityEngine.Object);
            return;
        }

        weaponsById.Add(weapon.WeaponId, weapon);
    }

    private void EquipStartingWeapon()
    {
        if (!string.IsNullOrWhiteSpace(startingWeaponId) && EquipWeapon(startingWeaponId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(startingWeaponId) && weaponComponents.Count > 0)
        {
            Debug.LogWarning($"Starting weapon id '{startingWeaponId}' was not found. The first collected weapon will be equipped instead.", this);
        }

        if (weaponComponents.Count > 0)
        {
            SetEquippedWeapon(weaponComponents[0]);
        }
    }

    private void RefreshWeaponVisuals()
    {
        if (manageWeaponObjectVisibility)
        {
            for (int i = 0; i < weaponComponents.Count; i++)
            {
                if (weaponComponents[i] is not MonoBehaviour weaponBehaviour || weaponBehaviour.gameObject == gameObject)
                {
                    continue;
                }

                bool shouldBeActive = ReferenceEquals(weaponComponents[i], equippedWeapon);
                if (weaponBehaviour.gameObject.activeSelf != shouldBeActive)
                {
                    weaponBehaviour.gameObject.SetActive(shouldBeActive);
                }
            }
        }

        RefreshOffhandGunVisual();
    }

    private void RefreshOffhandGunVisual()
    {
        HideOffhandGunVisuals();

        if (!createOffhandGunVisual || equippedWeapon is not Weapon_Gun || string.IsNullOrWhiteSpace(EquippedWeaponId))
        {
            return;
        }

        GameObject visual = GetOrCreateOffhandGunVisual(EquippedWeaponId, equippedWeapon);
        if (visual != null && !visual.activeSelf)
        {
            visual.SetActive(true);
        }
    }

    private void HideOffhandGunVisuals()
    {
        foreach (KeyValuePair<string, GameObject> pair in offhandGunVisualsByWeaponId)
        {
            if (pair.Value != null && pair.Value.activeSelf)
            {
                pair.Value.SetActive(false);
            }
        }
    }

    private GameObject GetOrCreateOffhandGunVisual(string weaponId, IPlayerWeapon weapon)
    {
        if (offhandGunVisualsByWeaponId.TryGetValue(weaponId, out GameObject existingVisual) && existingVisual != null)
        {
            if (weapon is MonoBehaviour existingWeaponBehaviour)
            {
                ApplyOffhandGunTransform(existingWeaponBehaviour.transform, existingVisual.transform);
            }

            return existingVisual;
        }

        if (weapon is not MonoBehaviour weaponBehaviour)
        {
            return null;
        }

        Transform parent = ResolveOffhandGunParent();
        if (parent == null)
        {
            WarnMissingOffhandGunParent();
            return null;
        }

        GameObject visual = Instantiate(weaponBehaviour.gameObject, parent);
        visual.name = $"{weaponBehaviour.gameObject.name}_OffhandVisual";
        visual.SetActive(false);

        StripInteractiveComponents(visual);
        ApplyOffhandGunTransform(weaponBehaviour.transform, visual.transform);

        offhandGunVisualsByWeaponId[weaponId] = visual;
        return visual;
    }

    private Transform ResolveOffhandGunParent()
    {
        if (offhandGunSocket != null)
        {
            return offhandGunSocket;
        }

        if (cachedAnimator == null)
        {
            cachedAnimator = GetComponentInChildren<Animator>();
        }

        if (cachedAnimator != null && cachedAnimator.isHuman)
        {
            Transform bone = cachedAnimator.GetBoneTransform(offhandGunBone);
            if (bone != null)
            {
                return bone;
            }
        }

        return FindChildByNamePart(transform, "LeftHand") ?? FindChildByNamePart(transform, "Left Hand");
    }

    private static Transform FindChildByNamePart(Transform root, string namePart)
    {
        if (root == null || string.IsNullOrWhiteSpace(namePart))
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return child;
            }

            Transform match = FindChildByNamePart(child, namePart);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private void WarnMissingOffhandGunParent()
    {
        if (warnedMissingOffhandGunParent)
        {
            return;
        }

        warnedMissingOffhandGunParent = true;
        Debug.LogWarning("Offhand gun visual could not be created. Assign Offhand Gun Socket or use a Humanoid avatar with a left hand bone.", this);
    }

    private void ApplyOffhandGunTransform(Transform source, Transform target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.localPosition = source.localPosition + offhandGunPositionOffset;
        target.localRotation = source.localRotation * Quaternion.Euler(offhandGunRotationOffset);
        target.localScale = Vector3.Scale(source.localScale, offhandGunScaleMultiplier);
    }

    private void StripInteractiveComponents(GameObject visual)
    {
        MonoBehaviour[] behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Destroy(behaviours[i]);
        }

        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Destroy(colliders[i]);
        }

        Rigidbody[] rigidbodies = visual.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Destroy(rigidbodies[i]);
        }
    }

    private void SetEquippedWeapon(IPlayerWeapon weapon)
    {
        if (ReferenceEquals(equippedWeapon, weapon))
        {
            RefreshWeaponVisuals();
            return;
        }

        equippedWeapon?.CancelAttack();
        equippedWeapon = weapon;
        ActiveAnimationComboIndex = 0;
        RefreshWeaponVisuals();
        EquippedWeaponChanged?.Invoke(EquippedWeaponId);
    }
}
