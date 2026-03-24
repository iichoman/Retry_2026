using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_Attack : MonoBehaviour
{
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField] private string startingWeaponId = "test_sword";

    private readonly List<IPlayerWeapon> weaponComponents = new List<IPlayerWeapon>();
    private readonly Dictionary<string, IPlayerWeapon> weaponsById = new Dictionary<string, IPlayerWeapon>(StringComparer.Ordinal);
    private IPlayerWeapon equippedWeapon;
    private bool previousAttackInput;

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

        bool currentAttackInput = playerInput != null && playerInput.Attack;
        bool attackPressedThisFrame = currentAttackInput && !previousAttackInput;
        previousAttackInput = currentAttackInput;

        if (attackPressedThisFrame)
        {
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
    }

    public void AnimEvent_BeginSwing()
    {
        if (equippedWeapon == null || ActiveAnimationComboIndex <= 0)
        {
            return;
        }

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

    private void OnDisable()
    {
        previousAttackInput = false;
        ActiveAnimationComboIndex = 0;
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

    private void SetEquippedWeapon(IPlayerWeapon weapon)
    {
        if (ReferenceEquals(equippedWeapon, weapon))
        {
            return;
        }

        equippedWeapon?.CancelAttack();
        equippedWeapon = weapon;
        ActiveAnimationComboIndex = 0;
    }
}
