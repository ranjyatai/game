using System;

public enum StatModifierOp
{
    Add      = 0,   // finalValue += value
    Multiply = 1,   // finalValue *= value
    Override = 2,   // finalValue  = value
}

/// <summary>
/// 单条属性加成，用于装备/组件/科技树等外部来源。
/// 通过 UnitBattleStatRuntime.SetExternalModifiers() 批量注入。
/// </summary>
[Serializable]
public class StatModifier
{
    /// <summary>对应 CoreAttributeDefinition.key，例如 "atk"、"maxHp"。</summary>
    public string       statKey;
    public float        value;
    public StatModifierOp op = StatModifierOp.Add;

    public StatModifier(string statKey, float value, StatModifierOp op = StatModifierOp.Add)
    {
        this.statKey = statKey;
        this.value   = value;
        this.op      = op;
    }
}
