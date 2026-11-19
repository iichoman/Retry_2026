#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
namespace AB
{
constexpr int Version = 1, CommandPacket = 200, StatePacket = 201, ActorPacket = 202;
constexpr int PresentationPacket = 206;
constexpr float ParryWindow = .35f, ParryStun = .75f;
constexpr float DashCost = 20.f, ParryCost = 5.f, AttackCost = 2.f;
constexpr float RecoveryDashExtraCost = 10.f, HitRecoveryDuration = .45f;
constexpr int InventoryLimit = 30, HitRecoveryMask = 32;
constexpr int UpgradeCount = 21, MaxLevel = 144;
constexpr int UpgradeCaps[UpgradeCount] = {10,10,10,10,10,10,10,10,6,6,5,5,5,5,5,1,5,5,5,5,5};
inline bool IsGreatsword(int type) { return type >= 5 && type <= 7; }
inline int WeaponElement(int type) { return type >= 6 && type <= 7 ? type - 5 : type; }
struct HitTraits
{
    int weaponType = 0, passiveType = -1, tier = 1;
    float duration = 1;
};
enum Action
{
    Equip = 1,
    Use = 2,
    Choose = 3,
    Dash = 4,
    Parry = 5,
    RecoveryDash = 6,
    Drop = 7,
    Abandon = 12,
    RewardTeleport = 13
};
#pragma pack(push, 1)
struct Presentation
{
    int version=1, entityId=0, team=-1, action=0, serial=0;
    float remaining=0, duration=0, dx=0, dz=0;
};
struct Command
{
    int version, sequence, action, itemHash, offer;
};
struct Snapshot
{
    int version = Version, sequence = 0, result = 0, weapon = 0, passive = 0, hp = 100, maxHp = 100, level = 1, xp = 0,
        required = 50, offer = 0, choice0 = 0, choice1 = 1, choice2 = 2;
    float stamina = 100, maxStamina = 100, damage = 20, attackSpeed = 1, range = 1, movement = 1;
    int statusMask = 0;
};
// Positive entity ids are players; negative ids are monsters, as in CombatEvent.
struct ActorState
{
    int version = Version, entityId = 0, weapon = 0, statusMask = 0, parrySerial = 0;
    float range = 1, attackSpeed = 1;
};
// Mirrors the client's existing EXTRACTION_LOOT_RESULT (68); starter quantities excluded.
struct ExtractionEntry
{
    int itemHash = 0, count = 0, unitValue = 0;
};
struct Extraction
{
    int entryCount = 0, totalCount = 0, bossKillCount = 0;
    std::int64_t totalValue = 0;
    ExtractionEntry entries[32];
};
#pragma pack(pop)
static_assert(sizeof(Command) == 20 && sizeof(Snapshot) == 84, "AB wire layout");
static_assert(sizeof(ActorState) == 28, "AB actor wire layout");
static_assert(sizeof(Extraction) == 404, "Extraction wire layout");
struct Status
{
    float left = 0, tick = 0;
    int power = 0, source = 0;
};
struct Effects
{
    std::array<Status, 4> statuses{};
    float stunImmunity = 0;
    void Apply(int kind, int power, int source, float duration = 1.f)
    {
        if (kind < 0 || kind >= 4 || power <= 0)
            return;
        if (kind == 2 && stunImmunity > 0)
            return;
        if (kind == 2)
            stunImmunity = 2.f;
        auto &s = statuses[kind];
        s.left = kind == 0 ? 3.f : kind == 3 ? 10.f : kind == 2 ? .3f : 2.f;
        s.left *= duration;
        s.power = (std::max)(s.power, power);
        s.source = source;
    }
    bool Stunned() const
    {
        return statuses[2].left > 0;
    }
    float Slow() const
    {
        return statuses[1].left > 0 ? .7f : 1.f;
    }
    int Mask() const
    {
        int result = 0;
        for (int i = 0; i < 4; i++)
            if (statuses[i].left > 0)
                result |= 1 << i;
        return result;
    }
    void Clear()
    {
        statuses = {};
    }
    template <class Hit> void Step(float dt, Hit hit)
    {
        stunImmunity = (std::max)(0.f, stunImmunity - dt);
        for (int i = 0; i < 4; i++)
        {
            auto &s = statuses[i];
            if (s.left <= 0)
                continue;
            float active = (std::min)(dt, s.left);
            s.left -= active;
            s.tick += active;
            if (i == 0 || i == 3)
                while (s.tick >= .9999f)
                {
                    s.tick -= 1.f;
                    hit(s.power, s.source);
                }
            if (s.left <= 0)
                s = {};
        }
    }
};
struct Progress
{
    bool demoInfiniteStamina = false;
    int demoChestCursor = 0, demoTrapCursor = 0;
    unsigned randomState = 12345;
    Snapshot view;
    Effects effects;
    std::array<int, UpgradeCount> upgrades{};
    int weaponType = 0, passiveType = -1, tier = 1, pending = 0, lastSequence = 0;
    int defenseAction = 0, defenseSerial = 0;
    float defenseLeft = 0, defenseDuration = 0, defenseX = 0, defenseZ = 0;
    float sync = 0, procCooldown = 0, parry = 0, counter = 0, immune = 0;
    int hits = 0, parrySerial = 0, bossKills = 0;
    float regenDelay = 0;
    float lifestealRemainder = 0, dashCooldown = 0, parryCooldown = 0, lastSprintInput = 0, shieldReduction = 0;
    bool initialized = false;
    float useCooldown = 0, sprintFree = 0, shield = 0, idle = 0, regen = 0;
    float skillBuffLeft = 0, skillBuffPower = 0, skillResetLock = 0, echoCooldown = 0;
    float hitRecoveryLeft = 0, recentHitLeft = 0, recoveryDashCooldown = 0, dropCooldown = 0;
    int observedHp = 100;
    void ObserveHealth(int hp, int maximum) {
        if(hp>0 && hp<std::min(observedHp,maximum)) {
            hitRecoveryLeft=HitRecoveryDuration;recentHitLeft=HitRecoveryDuration+.3f;
            defenseLeft=0;parry=0;
        }
        observedHp=hp;
    }
    HitTraits Traits() const { return {weaponType, passiveType, tier, 1 + .1f * upgrades[6]}; }
    void Recompute()
    {
        view.maxHp = 100 + upgrades[0] * 10 + (passiveType == 4 ? 5 + 5 * tier : 0);
        view.maxStamina = 100 + upgrades[1] * 10 + (passiveType == 5 ? 5 + 10 * tier : 0);
        view.damage = (IsGreatsword(weaponType) ? 45.f : weaponType == 4 ? 10.f : weaponType == 3 ? 16.f : 20.f) * (1.f + .08f * upgrades[2]);
        if(IsGreatsword(weaponType))view.damage*=1+.08f*upgrades[14];
        view.attackSpeed = std::min(2.f, (weaponType == 3 ? 1.65f : 1.f) * (1.f + .05f * upgrades[3]) * effects.Slow());
        view.movement = effects.Stunned() ? 0 : effects.Slow();
        view.statusMask = (demoInfiniteStamina ? 64 : 0) | effects.Mask() | (sprintFree > 0 ? 16 : 0) | (hitRecoveryLeft > 0 ? HitRecoveryMask : 0);
        if(demoInfiniteStamina) view.stamina=view.maxStamina;
        view.stamina = (std::max)(0.f, (std::min)(view.stamina, view.maxStamina));
        view.range = 1.f + .05f * upgrades[4];
        view.required = 50 + (view.level - 1) * 25;
    }
    void Offer()
    {
        if (pending <= 0)
        {
            view.offer = 0;
            return;
        }
        ++offerSerial;
        view.offer = offerSerial;
        randomState = randomState * 1664525u + 1013904223u;
        int offset = (randomState >> 16) % UpgradeCount;
        int choices[3] = {-1, -1, -1}, count = 0;
        for (int i = 0; i < UpgradeCount && count < 3; i++)
        {
            int k = (offset + i * 5) % UpgradeCount;
            if ((k<14||IsGreatsword(weaponType)) && upgrades[k] < UpgradeCaps[k])
                choices[count++] = k;
        }
        // Include a skill path while one remains, without repeating a choice.
        if (count == 3 && choices[0] < 8 && choices[1] < 8 && choices[2] < 8)
            for (int i = 0; i < 6; ++i) {
                int k = 8 + (offset + i) % 6;
                if (upgrades[k] < UpgradeCaps[k]) { choices[2] = k; break; }
            }
        view.choice0 = choices[0];
        view.choice1 = choices[1];
        view.choice2 = choices[2];
        if (count == 0)
        {
            view.offer = 0;
            pending = 0;
        }
    }
    void Award(int amount)
    {
        view.xp += amount + (amount * upgrades[7] / 10);
        while (view.xp >= view.required && view.level < MaxLevel)
        {
            view.xp -= view.required;
            ++view.level;
            ++pending;
            Recompute();
        }
        if (pending > 0 && view.offer == 0)
            Offer();
    }
    bool Select(int offer, int choice)
    {
        if (offer != view.offer || offer == 0 || pending <= 0 || choice < 0 || choice >= UpgradeCount)
            return false;
        if (choice != view.choice0 && choice != view.choice1 && choice != view.choice2)
            return false;
        if (upgrades[choice] >= UpgradeCaps[choice])
            return false;
        ++upgrades[choice];
        --pending;
        Recompute();
        Offer();
        return true;
    }

  private:
    int offerSerial = 0;
};
} // namespace AB
