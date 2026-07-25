using System;

/// <summary>
/// 组件词条的已投掷实例。掉落时生成，鉴定前对玩家隐藏数值。
/// </summary>
[Serializable]
public class RolledModBonus
{
    public string statKey;
    public string displayName;
    public float  value;
    public bool   isPercent;
}
