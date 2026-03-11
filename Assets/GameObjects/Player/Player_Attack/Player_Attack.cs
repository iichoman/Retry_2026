using UnityEngine;

public class Player_Attack : MonoBehaviour
{
    [SerializeField] private WeaponHitbox weaponHitbox;

    private bool swingActive;

    private void Awake()
    {
        if (weaponHitbox == null)
        {
            weaponHitbox = GetComponentInChildren<WeaponHitbox>();
        }
    }

    private void Start()
    {
        if (weaponHitbox != null)
        {
            weaponHitbox.SetOwner(this);
            weaponHitbox.SetHitboxActive(false);
        }
    }

    // Animation Event: call on attack clip at hit start frame.
    public void AnimEvent_BeginSwing()
    {
        if (weaponHitbox == null)
        {
            return;
        }

        if (!swingActive)
        {
            swingActive = true;
            weaponHitbox.BeginSwing();
        }
    }

    // Animation Event: call on attack clip at hit end frame.
    public void AnimEvent_EndSwing()
    {
        if (weaponHitbox == null || !swingActive)
        {
            return;
        }

        swingActive = false;
        weaponHitbox.EndSwing();
    }

    private void OnDisable()
    {
        if (weaponHitbox != null && swingActive)
        {
            swingActive = false;
            weaponHitbox.EndSwing();
        }
    }
}
