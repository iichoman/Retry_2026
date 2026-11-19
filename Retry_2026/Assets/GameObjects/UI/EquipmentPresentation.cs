using UnityEngine;

public static class EquipmentPresentation
{
    public static Color Accent(ItemData item)
    {
        var weapon = item != null ? ABItemDefinitions.Find(ItemHash.Of(item.ItemId)) : null;
        if (weapon == null || weapon.kind != 2) return item != null ? item.RarityColor : ExpeditionTheme.Muted;
        return weapon.WeaponElement switch
        {
            1 => new Color(1f, .32f, .10f), 2 => new Color(.30f, .75f, 1f),
            3 => new Color(.32f, 1f, .48f), _ => new Color(.70f, .76f, .82f)
        };
    }
    public static string WeaponDescription(ABItemDefinitions.Item weapon) => weapon?.description ?? "";
    public static ABSnapshot Preview(int weaponHash, int itemHash)
    {
        var weapon = ABItemDefinitions.Find(weaponHash); var item = ABItemDefinitions.Find(itemHash);
        int effect = weapon?.effect ?? 0;
        var value = new ABSnapshot { level = 1, hp = 100, maxHp = 100, stamina = 100, maxStamina = 100,
            damage = weapon?.IsGreatsword == true ? 45 : effect == 3 ? 16 : effect == 4 ? 10 : 20,
            attackSpeed = effect == 3 ? 1.65f : 1, movement = 1, range = 1 };
        if (item?.kind == 1 && item.effect == 4) value.hp = value.maxHp += 5 + 5 * item.tier;
        if (item?.kind == 1 && item.effect == 5) value.stamina = value.maxStamina += 5 + 10 * item.tier;
        return value;
    }
    public static string[] Stats(ABSnapshot state) => new[]
    {
        $"최대 체력  {state.maxHp:N0}", $"공격력  {state.damage:0.#}", $"공격 속도  {state.attackSpeed * 100:0.#}%",
        $"최대 스태미나  {state.maxStamina:0}", $"이동 속도  {state.movement * 100:0.#}%", $"무기 크기  {state.range * 100:0.#}%"
    };
}
