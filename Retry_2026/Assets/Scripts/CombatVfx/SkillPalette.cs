using UnityEngine;
public static class SkillPalette
{
    public static Color ForWeapon(int hash)
    {
        var item = ABItemDefinitions.Find(hash);
        int kind = item != null && item.kind == 2 ? item.WeaponElement : 0;
        return kind == 1 ? new Color(1, .34f, .30f)
             : kind == 2 ? new Color(.30f, .75f, 1)
             : kind == 3 ? new Color(.43f, 1, .76f)
             : new Color(.96f, .82f, .39f);
    }
}
