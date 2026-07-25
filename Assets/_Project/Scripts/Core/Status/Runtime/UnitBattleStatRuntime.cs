using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UnitBattleStatRuntime : MonoBehaviour
{
    [SerializeField] private UnitDefinition ownerDefinition;
    [SerializeField] private UnitStatusController statusController;
    [SerializeField] private UnitHealthController healthController;
    [SerializeField] private bool debugLogs = false;

    private readonly Dictionary<string, float> baseValueCache = new Dictionary<string, float>();
    private readonly Dictionary<string, float> finalValueCache = new Dictionary<string, float>();

    // ── 外部加成层（装备/组件/科技树，不走 StatusController）─────────────
    // key = 来源标签（如 "equip_weapon"、"tech_node_atk1"），value = 该来源的属性加成列表
    private readonly Dictionary<string, List<StatModifier>> _externalModifiers
        = new Dictionary<string, List<StatModifier>>();

    // RebuildFinalValues 之前每次调用都新建3个Dictionary+1个HashSet——只要单位身上
    // 有持续跳的状态/DOT，这个方法就会频繁触发，复用同一批缓冲区，只Clear不重建。
    private readonly Dictionary<string, float> _addMapCache = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _mulMapCache = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _overrideMapCache = new Dictionary<string, float>();
    private readonly HashSet<string> _keysCache = new HashSet<string>();

    public event Action StatsRebuilt;

    public UnitDefinition OwnerDefinition => ownerDefinition;

    public static UnitBattleStatRuntime EnsureOnRoot(GameObject unitRoot)
    {
        if (unitRoot == null)
            return null;

        UnitBattleStatRuntime runtime = unitRoot.GetComponent<UnitBattleStatRuntime>();
        if (runtime == null)
            runtime = unitRoot.AddComponent<UnitBattleStatRuntime>();

        runtime.AutoSetup();
        return runtime;
    }

    private void Awake()
    {
        AutoSetup();
        RebuildAll();
    }

    private void OnEnable()
    {
        SubscribeStatusEvents(true);
    }

    private void OnDisable()
    {
        SubscribeStatusEvents(false);
    }

    public void EnsureInitialized(UnitDefinition definition)
    {
        ownerDefinition = definition;
        AutoSetup();
        RebuildAll();
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (statusController == null)
            statusController = GetComponent<UnitStatusController>();

        if (healthController == null)
            healthController = GetComponent<UnitHealthController>();

        SubscribeStatusEvents(false);
        SubscribeStatusEvents(true);
    }

    // ── 外部加成接口 ──────────────────────────────────────────────────────

    /// <summary>
    /// 注册一组外部加成（装备/组件/科技树）。
    /// sourceTag 唯一标识来源，重复调用会替换旧值。
    /// 调用后自动触发 RebuildAll。
    /// </summary>
    public void SetExternalModifiers(string sourceTag, List<StatModifier> modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
            _externalModifiers.Remove(sourceTag);
        else
            _externalModifiers[sourceTag] = modifiers;
        RebuildAll();
    }

    /// <summary>移除指定来源的全部外部加成并重算。</summary>
    public void ClearExternalModifiers(string sourceTag)
    {
        if (_externalModifiers.Remove(sourceTag))
            RebuildAll();
    }

    /// <summary>移除所有外部加成并重算（换场景/角色重置时用）。</summary>
    public void ClearAllExternalModifiers()
    {
        if (_externalModifiers.Count == 0) return;
        _externalModifiers.Clear();
        RebuildAll();
    }

    public float GetBaseValue(string key, float fallback = 0f)
    {
        if (string.IsNullOrWhiteSpace(key))
            return fallback;

        float value;
        return baseValueCache.TryGetValue(key, out value) ? value : fallback;
    }

    public float GetFinalValue(string key, float fallback = 0f)
    {
        if (string.IsNullOrWhiteSpace(key))
            return fallback;

        float value;
        return finalValueCache.TryGetValue(key, out value) ? value : fallback;
    }

    public void RebuildAll()
    {
        RebuildBaseValues();
        RebuildFinalValues();
        ApplyDerivedResources();
        StatsRebuilt?.Invoke();

        if (debugLogs)
            Debug.Log($"[UnitBattleStatRuntime] RebuildAll -> {name}", this);
    }

    private void RebuildBaseValues()
    {
        baseValueCache.Clear();

        if (ownerDefinition == null || ownerDefinition.parameterValues == null)
            return;

        for (int i = 0; i < ownerDefinition.parameterValues.Count; i++)
        {
            UnitParameterValue item = ownerDefinition.parameterValues[i];
            if (item == null || string.IsNullOrWhiteSpace(item.parameterKey))
                continue;

            baseValueCache[item.parameterKey] = item.value;
        }
    }

    private void RebuildFinalValues()
    {
        finalValueCache.Clear();

        foreach (KeyValuePair<string, float> pair in baseValueCache)
            finalValueCache[pair.Key] = pair.Value;

        Dictionary<string, float> addMap = _addMapCache; addMap.Clear();
        Dictionary<string, float> mulMap = _mulMapCache; mulMap.Clear();
        Dictionary<string, float> overrideMap = _overrideMapCache; overrideMap.Clear();

        if (statusController != null)
        {
            IReadOnlyList<RuntimeStatusEntry> statuses = statusController.ActiveStatuses;
            for (int i = 0; i < statuses.Count; i++)
            {
                RuntimeStatusEntry entry = statuses[i];
                if (entry == null || entry.definition == null)
                    continue;

                List<StatusAttributeModifierDefinition> modifiers = entry.definition.attributeModifiers;
                if (modifiers == null)
                    continue;

                for (int m = 0; m < modifiers.Count; m++)
                {
                    StatusAttributeModifierDefinition mod = modifiers[m];
                    if (mod == null || !mod.enabled || string.IsNullOrWhiteSpace(mod.attributeKey))
                        continue;

                    float scaledValue = EvaluateScaledModifierValue(mod, entry.stackCount);

                    switch (mod.attributeOperator)
                    {
                        case StatusAttributeOperator.Add:
                            Accumulate(addMap, mod.attributeKey, scaledValue);
                            break;

                        case StatusAttributeOperator.Multiply:
                            if (!mulMap.ContainsKey(mod.attributeKey))
                                mulMap[mod.attributeKey] = 1f;
                            mulMap[mod.attributeKey] *= scaledValue;
                            break;

                        case StatusAttributeOperator.Override:
                            overrideMap[mod.attributeKey] = scaledValue;
                            break;
                    }
                }
            }
        }

        // 外部加成层（装备/组件/科技树）
        foreach (var source in _externalModifiers.Values)
        {
            foreach (var mod in source)
            {
                if (string.IsNullOrEmpty(mod.statKey)) continue;
                switch (mod.op)
                {
                    case StatModifierOp.Add:
                        Accumulate(addMap, mod.statKey, mod.value);
                        break;
                    case StatModifierOp.Multiply:
                        if (!mulMap.ContainsKey(mod.statKey)) mulMap[mod.statKey] = 1f;
                        mulMap[mod.statKey] *= mod.value;
                        break;
                    case StatModifierOp.Override:
                        overrideMap[mod.statKey] = mod.value;
                        break;
                }
            }
        }

        HashSet<string> keys = _keysCache; keys.Clear();
        foreach (var pair in finalValueCache) keys.Add(pair.Key);
        foreach (var pair in addMap) keys.Add(pair.Key);
        foreach (var pair in mulMap) keys.Add(pair.Key);
        foreach (var pair in overrideMap) keys.Add(pair.Key);

        foreach (string key in keys)
        {
            float baseValue = GetDictionaryValue(baseValueCache, key, 0f);
            float addValue = GetDictionaryValue(addMap, key, 0f);
            float mulValue = GetDictionaryValue(mulMap, key, 1f);

            float value = (baseValue + addValue) * mulValue;
            if (overrideMap.ContainsKey(key))
                value = overrideMap[key];

            finalValueCache[key] = value;
        }
    }

    private void ApplyDerivedResources()
    {
        if (healthController == null)
            return;

        float maxHp = GetFinalValue("maxHp", healthController.MaxHealth);
        maxHp = Mathf.Max(1f, maxHp);
        healthController.SetMaxHealthFromStatus(maxHp);

        // maxLp 先只算缓存，不直接写，因为你还没给 LP 控制器文件。
    }

    private float EvaluateScaledModifierValue(StatusAttributeModifierDefinition mod, int stackCount)
    {
        int safeStacks = Mathf.Max(1, stackCount);
        float baseValue = mod.value;

        switch (mod.stackScaling)
        {
            case StatusAttributeStackScalingMode.None:
                return baseValue;

            case StatusAttributeStackScalingMode.Linear:
                return baseValue * safeStacks;

            case StatusAttributeStackScalingMode.MultiplyByStack:
                if (mod.attributeOperator == StatusAttributeOperator.Multiply)
                    return Mathf.Pow(baseValue, safeStacks);
                return baseValue * safeStacks;

            default:
                return baseValue;
        }
    }

    private void SubscribeStatusEvents(bool subscribe)
    {
        if (statusController == null)
            return;

        if (subscribe)
            statusController.StatusesChanged += HandleStatusesChanged;
        else
            statusController.StatusesChanged -= HandleStatusesChanged;
    }

    private void HandleStatusesChanged()
    {
        RebuildAll();
    }

    private void Accumulate(Dictionary<string, float> map, string key, float value)
    {
        float current;
        map.TryGetValue(key, out current);
        map[key] = current + value;
    }

    private float GetDictionaryValue(Dictionary<string, float> map, string key, float fallback)
    {
        float value;
        return map.TryGetValue(key, out value) ? value : fallback;
    }
}
