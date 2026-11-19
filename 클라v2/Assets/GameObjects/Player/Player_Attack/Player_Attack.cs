using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_Attack : MonoBehaviour
{
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private Player_Camera_Action cameraAction;
    [SerializeField] private Player_State playerState;
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
    private PlayerDefenseController defense;
    private Player_LockOnSystem lockOn;

    public event Action<string> EquippedWeaponChanged;
    public event Action<int> AttackAnimationStarted;

    public string EquippedWeaponId => equippedWeapon != null ? equippedWeapon.WeaponId : string.Empty;
    public WeaponGrade CurrentWeaponGrade => equippedWeapon != null ? equippedWeapon.Grade : WeaponGrade.Common;
    public int CurrentAttackDamage => equippedWeapon != null ? equippedWeapon.AttackDamage : 0;
    public int ActiveAnimationComboIndex { get; private set; }
    public bool IsSwordEquipped => equippedWeapon is Weapon_Sword;
    public bool IsGreatswordEquipped => equippedWeapon is GreatswordWeapon;

    private void Awake()
    {
        defense = GetComponent<PlayerDefenseController>();
        lockOn = GetComponent<Player_LockOnSystem>();
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

        cachedAnimator = GetComponentInChildren<Animator>();

        CollectWeapons();
        EquipStartingWeapon();
        if (GetComponent<WeaponBackCarry>() == null) gameObject.AddComponent<WeaponBackCarry>();
    }

    private void Update()
    {
        if (equippedWeapon == null)
        {
            return;
        }

        equippedWeapon.Tick(Time.deltaTime);

        if (playerInput == null || !playerInput.isActiveAndEnabled || !playerInput.IsGameplayInputEnabled
            || PlayerSkillView.Busy(gameObject) || (defense != null && defense.IsBusy) || (playerState != null && (playerState.IsDead || playerState.IsHit)))
        {
            previousAttackInput = playerInput != null && playerInput.Attack;
            CancelCurrentAttack();
            return;
        }

        bool currentAttackInput = playerInput != null && playerInput.Attack;
        bool attackPressedThisFrame = currentAttackInput && !previousAttackInput;
        previousAttackInput = currentAttackInput;

        if (attackPressedThisFrame)
        {
            // 스태미나가 모자라면 입력 자체를 무시한다(콤보 예약도 안 걸린다).
            // 실제 차감은 모션이 시작될 때(NotifyAttackAnimationStarted) 한 번만 한다.
            // 여기서 깎으면 쿨다운/콤보창 때문에 씹힌 입력까지 전부 깎여
            // 4타 치는 데 30 가까이 날아간다.
            if (PlayerStamina.Local != null && !PlayerStamina.Local.CanAttack) return;
            if(ABClient.Active!=null && ABClient.Active.Supported && (ABClient.Active.State.statusMask&4)!=0)return;

            if (ActiveAnimationComboIndex == 0) AlignAttackToLockOn();
            equippedWeapon.RequestAttack();
        }
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
        if ((defense != null && defense.IsBusy) || playerInput == null || !playerInput.IsGameplayInputEnabled) return false;
        return equippedWeapon != null && equippedWeapon.TryConsumeAnimationRequest(out comboIndex);
    }

    public bool TryGetAttackAnimationState(int comboIndex, out string stateName)
    {
        stateName = string.Empty;
        return equippedWeapon != null && equippedWeapon.TryGetAnimationStateName(comboIndex, out stateName);
    }

    public void NotifyAttackAnimationStarted(int comboIndex)
    {
        GetComponent<WeaponBackCarry>()?.RestoreGrip();
        AlignAttackToLockOn();
        // 한 번 휘두를 때마다 정확히 1회. 콤보로 이어진 2·3·4타도 여기를 지난다.
        PlayerStamina.Local?.SpendAttack();

        ActiveAnimationComboIndex = comboIndex;
        cameraAction?.BeginComboAttack(comboIndex);
        equippedWeapon?.OnAnimationStarted(comboIndex);
        AttackAnimationStarted?.Invoke(comboIndex);
    }

    // Commit facing at each swing start. Do not chase a moving target mid-swing or rotate a roll.
    // The network sender uses this same root direction, without changing the packet protocol.
    public void AlignAttackToLockOn()
    {
        if (!IsSwordEquipped || lockOn == null || !lockOn.IsLockedOn
            || PlayerSkillView.Busy(gameObject) || (defense != null && defense.IsBusy)
            || playerInput == null || !playerInput.isActiveAndEnabled || !playerInput.IsGameplayInputEnabled
            || (playerState != null && (playerState.IsDead || playerState.IsHit))) return;
        // Actor positions update immediately; collider bounds may still be at the previous physics tick.
        Vector3 direction = Vector3.ProjectOnPlane(lockOn.CurrentTarget.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(direction);
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

    public void UnequipWeapon(){CancelCurrentAttack();SetEquippedWeapon(null);}
    private void SetEquippedWeapon(IPlayerWeapon weapon)
    {
        if (ReferenceEquals(equippedWeapon, weapon))
        {
            RefreshWeaponVisuals();
            return;
        }

        CancelCurrentAttack();
        equippedWeapon = weapon;
        ActiveAnimationComboIndex = 0;
        RefreshWeaponVisuals();
        EquippedWeaponChanged?.Invoke(EquippedWeaponId);
    }
}
