using System.Collections.Generic;
using UnityEngine;

public enum DropPoolMode
{
    [InspectorName("独立判定")]
    IndependentRolls,
    [InspectorName("权重抽选")]
    WeightedPick
}

[CreateAssetMenu(menuName = "Game/Loot/Drop Profile", fileName = "DP_NewDropProfile")]
public class DropProfile : ScriptableObject
{
    [System.Serializable]
    public class DropEntry
    {
        [Header("启用")]
        public bool enabled = true;

        [Header("掉落物品")]
        public ScriptableObject itemDefinition;

        [Header("独立判定参数")]
        [Range(0f, 1f)]
        public float dropChance = 0.5f;

        [Header("抽选参数")]
        [Min(0f)]
        public float weight = 1f;

        [Header("数量")]
        public int minCount = 1;
        public int maxCount = 1;

        [TextArea(1, 3)]
        public string note;

        public bool IsValid()
        {
            return enabled && itemDefinition != null;
        }
    }

    [System.Serializable]
    public class DropPool
    {
        [Header("基础信息")]
        public bool enabled = true;
        public string poolName = "新掉落池";

        [TextArea(2, 4)]
        public string poolNote;

        [Header("池模式")]
        public DropPoolMode poolMode = DropPoolMode.IndependentRolls;

        [Header("抽选设置")]
        [Min(1)]
        public int pickCount = 1;

        [Header("掉落项")]
        public List<DropEntry> entries = new();

        public List<DropEntry> GetValidEntries()
        {
            List<DropEntry> result = new();

            for (int i = 0; i < entries.Count; i++)
            {
                DropEntry e = entries[i];
                if (e == null)
                    continue;

                if (!e.IsValid())
                    continue;

                result.Add(e);
            }

            return result;
        }
    }

    [Header("基础信息")]
    public string profileId;
    public string displayName;

    [TextArea(2, 5)]
    public string note;

    [Header("物品掉落池")]
    public List<DropPool> pools = new();

    public List<DropPool> GetValidPools()
    {
        List<DropPool> result = new();

        for (int i = 0; i < pools.Count; i++)
        {
            DropPool p = pools[i];
            if (p == null)
                continue;

            if (!p.enabled)
                continue;

            if (p.GetValidEntries().Count <= 0)
                continue;

            result.Add(p);
        }

        return result;
    }
}
