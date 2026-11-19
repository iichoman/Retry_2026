using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Monster_State))]
[RequireComponent(typeof(LootContainer))]
public class MonsterLoot : MonoBehaviour
{
    [SerializeField] private Monster_State monsterState;
    [SerializeField] private LootContainer lootContainer;
    [SerializeField] private List<LootDropEntry> drops = new List<LootDropEntry>();
    [SerializeField] private bool clearExistingLootOnDeath = true;
    [SerializeField] private bool openWhenEmpty = true;
    [SerializeField] private int seedOffset = 7000;

    private bool generated;

    private void Awake()
    {
        if (monsterState == null)
        {
            monsterState = GetComponent<Monster_State>();
        }

        if (lootContainer == null)
        {
            lootContainer = GetComponent<LootContainer>();
        }

        if (lootContainer != null)
        {
            lootContainer.SetAvailable(false);
        }
    }

    private void OnEnable()
    {
        if (monsterState != null)
        {
            monsterState.Died += HandleDied;
        }
    }

    private void OnDisable()
    {
        if (monsterState != null)
        {
            monsterState.Died -= HandleDied;
        }
    }

    private void HandleDied(Monster_State state, GameObject attacker)
    {
        GenerateLoot();
    }

    [ContextMenu("Generate Loot")]
    public void GenerateLoot()
    {
        if (generated || lootContainer == null)
        {
            return;
        }

        generated = true;

        if (clearExistingLootOnDeath)
        {
            lootContainer.Clear();
        }

        var random = new System.Random(GetInstanceID() + seedOffset);
        for (int i = 0; i < drops.Count; i++)
        {
            LootDropEntry drop = drops[i];
            if (drop == null || drop.item == null)
            {
                continue;
            }

            if (random.NextDouble() > Mathf.Clamp01(drop.dropChance))
            {
                continue;
            }

            int min = Mathf.Max(1, drop.minAmount);
            int max = Mathf.Max(min, drop.maxAmount);
            int amount = random.Next(min, max + 1);
            lootContainer.TryAdd(drop.item, amount);
        }

        lootContainer.SetAvailable(openWhenEmpty || !lootContainer.IsEmpty());
    }
}

[Serializable]
public class LootDropEntry
{
    public ItemData item;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Range(0f, 1f)] public float dropChance = 1f;
}
