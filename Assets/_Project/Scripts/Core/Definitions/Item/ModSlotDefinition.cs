using System;

/// <summary>
/// 武器/防具上的一个改装槽位定义。
/// 例：剑 → [{key:"hilt", displayName:"剑柄", slotTypeKey:"sword_hilt"}, {key:"blade", displayName:"刃", slotTypeKey:"sword_blade"}]
/// </summary>
[Serializable]
public class ModSlotDefinition
{
    /// <summary>槽位实例键，同一武器内唯一（如 "blade_1"）。</summary>
    public string slotKey;

    /// <summary>UI 显示名。</summary>
    public string displayName;

    /// <summary>
    /// 槽位类型键，决定哪些组件可以装入。
    /// 组件的 compatibleSlotTypeKeys 包含此值才允许安装。
    /// 例："sword_blade"、"gun_barrel"
    /// </summary>
    public string slotTypeKey;
}
