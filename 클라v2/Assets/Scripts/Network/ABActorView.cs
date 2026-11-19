using UnityEngine;
using System.Collections.Generic;

// Cosmetic state only. Never creates actors or applies damage.
public sealed class ABActorView : MonoBehaviour
{
    public int WeaponHash { get; private set; }
    public float Range { get; private set; } = 1;
    private readonly GameObject[] statuses = new GameObject[4];
    private static readonly string[] Names = { "Burn", "Freeze", "Stun", "Poison" };
    private Animator animator;
    private bool remote;
    private bool weaponInitialized;
    private float animationSpeed = 1;
    private Monster_State monsterState;
    private readonly Dictionary<Transform,Vector3> weaponScales = new Dictionary<Transform,Vector3>();

    public void ScaleWeapon(Transform weapon)
    {
        if(weapon==null)return;
        if(!weaponScales.TryGetValue(weapon,out var original)){
            original=weapon.localScale;weaponScales.Add(weapon,original);
        }
        weapon.localScale=original*Range;
    }
    private void OnDestroy()
    {
        foreach(var pair in weaponScales)if(pair.Key!=null)pair.Key.localScale=pair.Value;
    }

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        monsterState = GetComponent<Monster_State>();
        var player = GetComponent<Player_Attack>();
        remote = player == null || !player.enabled;
    }
    public void Apply(ABActorState state)
    {
        if (remote && (!weaponInitialized || WeaponHash != state.weapon)){
            if(state.weapon==0)GetComponent<Player_Attack>()?.UnequipWeapon();
            else if(ItemCatalog.Load()?.Resolve(state.weapon) is EquipmentItemData item)GetComponent<Player_Attack>()?.EquipWeapon(item.WeaponId);
        }
        (GetComponent<StunReactionView>()??gameObject.AddComponent<StunReactionView>()).SetNetworkStunned((state.statusMask&4)!=0);
        if((state.statusMask&4)!=0) {
            GetComponent<PlayerSkillView>()?.Cancel();
            GetComponent<PlayerDefenseController>()?.CancelDefense();
            GetComponent<Player_Attack>()?.CancelCurrentAttack();
        }
        WeaponHash = state.weapon;
        weaponInitialized=true;
        Range = Mathf.Clamp(state.range, 1, 1.5f);
        animationSpeed = (state.statusMask & 4) != 0 ? 1 : Mathf.Clamp(state.attackSpeed, .1f, 2f);
        for (int i = 0; i < statuses.Length; i++)
        {
            bool show = (state.statusMask & (1 << i)) != 0 || state.entityId < 0 && ((state.statusMask >> 8) & 7) == i + 1;
            if (show && statuses[i] == null)
            {
                var prefab = Resources.Load<GameObject>("AB/" + Names[i]);
                if (prefab != null)
                    statuses[i] = Instantiate(prefab, transform);
            }
            if (statuses[i] != null && statuses[i].activeSelf != show)
                statuses[i].SetActive(show);
        }
    }
    private void LateUpdate()
    {
        if (remote && animator != null && (monsterState == null || !monsterState.IsDead))
            animator.speed = animationSpeed;
    }
}
