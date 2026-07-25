using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 监听 TechTreeRuntime.OnNodeLevelUp，把所有已解锁节点的 rewards
/// 汇总为 StatModifier 注入玩家的 UnitBattleStatRuntime。
/// 挂在玩家 GameObject 上，或由 UnitDefinitionRuntimeApplier 自动添加。
/// </summary>
[DisallowMultipleComponent]
public sealed class TechTreeStatApplier : MonoBehaviour
{
    private const string SourceTag = "tech_tree";

    private UnitBattleStatRuntime _statRuntime;

    private void Awake()
    {
        _statRuntime = GetComponent<UnitBattleStatRuntime>();
    }

    private void OnEnable()
    {
        TechTreeRuntime.OnNodeLevelUp += HandleNodeLevelUp;
        SaveManager.OnSaveLoaded      += Rebuild; // 读档后重建（已有解锁历史）
    }

    private void OnDisable()
    {
        TechTreeRuntime.OnNodeLevelUp -= HandleNodeLevelUp;
        SaveManager.OnSaveLoaded      -= Rebuild;
    }

    private void HandleNodeLevelUp(string _, int __) => Rebuild();

    private void Rebuild()
    {
        // 同 EquipmentStatApplier 的坑：Awake() 里第一次取的时候 UnitBattleStatRuntime
        // 可能还没被另一个脚本挂上去，取到 null 就再也不会重试，导致科技树加成静默
        // 不生效。
        if (_statRuntime == null)
            _statRuntime = GetComponent<UnitBattleStatRuntime>();
        if (_statRuntime == null) return;
        var tech = TechTreeRuntime.Instance;
        if (tech == null) return;

        var rewards = tech.GetAllRewards(); // Dictionary<string, float>
        var mods = new List<StatModifier>(rewards.Count);

        foreach (var kv in rewards)
            mods.Add(new StatModifier(kv.Key, kv.Value));

        _statRuntime.SetExternalModifiers(SourceTag, mods);
    }
}
