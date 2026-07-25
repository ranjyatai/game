using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UnitDeathDropController : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private UnitDeathController deathController;
    [SerializeField] private Transform dropSpawnRoot;

    [Header("掉落配置")]
    [Tooltip("关闭后死亡不触发任何掉落（玩家单位建议关闭）。")]
    [SerializeField] private bool enableDrop = true;
    [SerializeField] private DropProfile dropProfile;
    [SerializeField] private List<DropProfile> extraDropProfiles = new List<DropProfile>();
    [SerializeField] private bool overrideProfilePoolMode = false;
    [SerializeField] private DropPoolMode overridePoolMode = DropPoolMode.IndependentRolls;

    [Header("掉落物预制体")]
    [SerializeField] private LootDropWorldObject lootDropPrefab;

    [Header("掉落位置")]
    [SerializeField] private Vector3 dropOffset = Vector3.zero;
    [SerializeField] private float randomRadius = 0.35f;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    private void Reset()
    {
        AutoSetup();
    }

    private void Awake()
    {
        AutoSetup();
    }

    private void OnEnable()
    {
        if (deathController != null)
            deathController.OnDeathStarted += HandleDeathStarted;
    }

    private void OnDisable()
    {
        if (deathController != null)
            deathController.OnDeathStarted -= HandleDeathStarted;
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();

        if (dropSpawnRoot == null)
            dropSpawnRoot = transform;
    }

    public void SetDropProfile(DropProfile profile)
    {
        dropProfile = profile;
        overrideProfilePoolMode = false;
    }

    public void SetDropProfile(DropProfile profile, DropPoolMode poolMode)
    {
        dropProfile = profile;
        overrideProfilePoolMode = true;
        overridePoolMode = poolMode;
    }

    public void SetDropProfiles(List<DropProfile> profiles)
    {
        dropProfile = null;
        extraDropProfiles.Clear();
        if (profiles == null || profiles.Count == 0) return;
        dropProfile = profiles[0];
        for (int i = 1; i < profiles.Count; i++)
            if (profiles[i] != null) extraDropProfiles.Add(profiles[i]);
        overrideProfilePoolMode = false;
    }

    public void ClearDropProfile()
    {
        dropProfile = null;
        extraDropProfiles.Clear();
        overrideProfilePoolMode = false;
    }

    private void HandleDeathStarted(UnitDeathController controller)
    {
        if (!enableDrop) return;
        SpawnDrops();
    }

    [ContextMenu("Spawn Drops")]
    public void SpawnDrops()
    {
        SpawnDropsFromProfile(dropProfile);
        for (int e = 0; e < extraDropProfiles.Count; e++)
            SpawnDropsFromProfile(extraDropProfiles[e]);
    }

    private void SpawnDropsFromProfile(DropProfile profile)
    {
        if (profile == null) return;

        List<DropProfile.DropPool> pools = profile.GetValidPools();
        if (pools == null || pools.Count == 0)
            return;

        for (int i = 0; i < pools.Count; i++)
        {
            DropProfile.DropPool pool = pools[i];
            if (pool == null || !pool.enabled)
                continue;

            DropPoolMode mode = overrideProfilePoolMode ? overridePoolMode : pool.poolMode;

            switch (mode)
            {
                case DropPoolMode.IndependentRolls:
                    SpawnIndependentPool(pool);
                    break;

                case DropPoolMode.WeightedPick:
                    SpawnWeightedPool(pool);
                    break;
            }
        }
    }

    private void SpawnIndependentPool(DropProfile.DropPool pool)
    {
        for (int i = 0; i < pool.entries.Count; i++)
        {
            DropProfile.DropEntry entry = pool.entries[i];
            if (entry == null || !entry.IsValid())
                continue;

            float effectiveChance = Mathf.Min(100f, entry.dropChance * DropRateBoostRuntime.CurrentMultiplier);
            float roll = Random.Range(0f, 100f);
            if (roll > effectiveChance)
                continue;

            int count = Random.Range(
                Mathf.Min(entry.minCount, entry.maxCount),
                Mathf.Max(entry.minCount, entry.maxCount) + 1
            );

            if (count <= 0)
                continue;

            SpawnLoot(entry.itemDefinition, count);
        }
    }

    private void SpawnWeightedPool(DropProfile.DropPool pool)
    {
        List<DropProfile.DropEntry> valid = pool.GetValidEntries();
        if (valid.Count == 0)
            return;

        int pickCount = Mathf.Max(1, pool.pickCount);

        for (int p = 0; p < pickCount; p++)
        {
            float totalWeight = 0f;
            for (int i = 0; i < valid.Count; i++)
                totalWeight += Mathf.Max(0f, valid[i].weight);

            if (totalWeight <= 0f)
                return;

            float roll = Random.Range(0f, totalWeight);
            float sum = 0f;
            DropProfile.DropEntry selected = null;

            for (int i = 0; i < valid.Count; i++)
            {
                sum += Mathf.Max(0f, valid[i].weight);
                if (roll <= sum)
                {
                    selected = valid[i];
                    break;
                }
            }

            if (selected == null)
                selected = valid[valid.Count - 1];

            int count = Random.Range(
                Mathf.Min(selected.minCount, selected.maxCount),
                Mathf.Max(selected.minCount, selected.maxCount) + 1
            );

            if (count <= 0)
                continue;

            SpawnLoot(selected.itemDefinition, count);
        }
    }

    private void SpawnLoot(ScriptableObject itemDefinition, int count)
    {
        if (itemDefinition == null || count <= 0)
            return;

        Vector3 center = (dropSpawnRoot != null ? dropSpawnRoot.position : transform.position) + dropOffset;
        Vector2 random2D = Random.insideUnitCircle * randomRadius;
        Vector3 spawnPos = center + new Vector3(random2D.x, 0f, random2D.y);

        if (lootDropPrefab != null)
        {
            LootDropWorldObject obj = Instantiate(lootDropPrefab, spawnPos, Quaternion.identity);
            obj.SetLoot(itemDefinition, count);
        }
        else if (itemDefinition is ItemDefinition itemDef)
        {
            LootDropWorldObject.SpawnDrop(itemDef, count, spawnPos);
        }

        if (debugLogs)
            Debug.Log($"[UnitDeathDropController] {name}: drop {itemDefinition.name} x{count}", this);
    }
}
