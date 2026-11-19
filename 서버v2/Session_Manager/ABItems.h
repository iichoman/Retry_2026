#pragma once
#include "../Common/ItemHash.h"
namespace AB
{
struct Item
{
    const char *id;
    int effect, tier, value, kind;
};
inline const Item Items[] = {
    {"health_potion", 0, 1, 30, 0},
    {"health_potion_t2", 0, 2, 60, 0},
    {"health_potion_t3", 0, 3, 90, 0},
    {"vigor_potion_t1", 1, 1, 30, 0},
    {"vigor_potion_t2", 1, 2, 60, 0},
    {"vigor_potion_t3", 1, 3, 90, 0},
    {"cleanse_potion_t1", 2, 1, 30, 0},
    {"cleanse_potion_t2", 2, 2, 60, 0},
    {"cleanse_potion_t3", 2, 3, 90, 0},
    {"sprint_potion_t1", 3, 1, 30, 0},
    {"sprint_potion_t2", 3, 2, 60, 0},
    {"sprint_potion_t3", 3, 3, 90, 0},
    {"life_amulet_t1", 4, 1, 100, 1},
    {"life_amulet_t2", 4, 2, 200, 1},
    {"life_amulet_t3", 4, 3, 300, 1},
    {"vigor_ring_t1", 5, 1, 100, 1},
    {"vigor_ring_t2", 5, 2, 200, 1},
    {"vigor_ring_t3", 5, 3, 300, 1},
    {"regen_seed_t1", 6, 1, 100, 1},
    {"regen_seed_t2", 6, 2, 200, 1},
    {"regen_seed_t3", 6, 3, 300, 1},
    {"vampire_fang_t1", 7, 1, 100, 1},
    {"vampire_fang_t2", 7, 2, 200, 1},
    {"vampire_fang_t3", 7, 3, 300, 1},
    {"hunter_crest_t1", 8, 1, 100, 1},
    {"hunter_crest_t2", 8, 2, 200, 1},
    {"hunter_crest_t3", 8, 3, 300, 1},
    {"duelist_knot_t1", 9, 1, 100, 1},
    {"duelist_knot_t2", 9, 2, 200, 1},
    {"duelist_knot_t3", 9, 3, 300, 1},
    {"ember_charm_t1", 10, 1, 100, 1},
    {"ember_charm_t2", 10, 2, 200, 1},
    {"ember_charm_t3", 10, 3, 300, 1},
    {"frost_crystal_t1", 11, 1, 100, 1},
    {"frost_crystal_t2", 11, 2, 200, 1},
    {"frost_crystal_t3", 11, 3, 300, 1},
    {"poison_thorn_t1", 12, 1, 100, 1},
    {"poison_thorn_t2", 12, 2, 200, 1},
    {"poison_thorn_t3", 12, 3, 300, 1},
    {"thunder_stone_t1", 13, 1, 100, 1},
    {"thunder_stone_t2", 13, 2, 200, 1},
    {"thunder_stone_t3", 13, 3, 300, 1},
    {"explorer_map_t1", 14, 1, 100, 1},
    {"explorer_map_t2", 14, 2, 200, 1},
    {"explorer_map_t3", 14, 3, 300, 1},
    {"guardian_sigil_t1", 15, 1, 100, 1},
    {"guardian_sigil_t2", 15, 2, 200, 1},
    {"guardian_sigil_t3", 15, 3, 300, 1},
    {"chronometer_t1", 16, 1, 100, 1},
    {"chronometer_t2", 16, 2, 200, 1},
    {"chronometer_t3", 16, 3, 300, 1},
    {"runic_focus_t1", 17, 1, 100, 1},
    {"runic_focus_t2", 17, 2, 200, 1},
    {"runic_focus_t3", 17, 3, 300, 1},
    {"echo_orb_t1", 18, 1, 100, 1},
    {"echo_orb_t2", 18, 2, 200, 1},
    {"echo_orb_t3", 18, 3, 300, 1},
    {"arc_prism_t1", 19, 1, 100, 1},
    {"arc_prism_t2", 19, 2, 200, 1},
    {"arc_prism_t3", 19, 3, 300, 1},
    {"reset_tonic_t1", 20, 1, 80, 0},
    {"reset_tonic_t2", 20, 2, 160, 0},
    {"reset_tonic_t3", 20, 3, 240, 0},
    {"focus_tonic_t1", 21, 1, 60, 0},
    {"focus_tonic_t2", 21, 2, 120, 0},
    {"focus_tonic_t3", 21, 3, 180, 0},
    {"sword_001", 0, 1, 100, 2},
    {"flame_sword", 1, 1, 200, 2},
    {"frost_sword", 2, 1, 300, 2},
    {"beam_sword", 3, 1, 400, 2},
    {"gun_001", 4, 1, 350, 2},
    {"greatsword_001", 5, 1, 450, 2},
    {"flame_greatsword", 6, 1, 550, 2},
    {"frost_greatsword", 7, 1, 650, 2},
};
inline const Item *Find(int hash)
{
    for (const auto &i : Items)
        if (ItemHash::Of(i.id) == hash)
            return &i;
    return nullptr;
}
inline int StackLimit(int hash)
{
    if (auto *item = Find(hash))
        return item->kind == 0 ? 16 : 1;
    if (hash == ItemHash::Of("gold_coin"))
        return 999;
    if (hash == ItemHash::Of("iron_ore"))
        return 99;
    if (hash == ItemHash::Of("rare_gem"))
        return 20;
    if (hash == ItemHash::Of("boss_core"))
        return 10;
    if (hash == ItemHash::Of("gun_001"))
        return 1;
    return 0;
}
inline int Value(int hash)
{
    if (auto *item = Find(hash))
        return item->value;
    if (hash == ItemHash::Of("gold_coin"))
        return 1;
    if (hash == ItemHash::Of("iron_ore"))
        return 10;
    if (hash == ItemHash::Of("rare_gem"))
        return 150;
    if (hash == ItemHash::Of("boss_core"))
        return 1000;
    if (hash == ItemHash::Of("gun_001"))
        return 350;
    return 0;
}
} // namespace AB
