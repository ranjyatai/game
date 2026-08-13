using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全部自定义变量的声明清单——变量选择器(编辑器UI)、变量表编辑窗口都读这一份，
/// 跟 QuestDatabase/UILocalizationTable 同一套"Resources 单例数据库"惯例。
/// 资产本体放在 Assets/_Project/Data/Resources/VariableDatabase.asset。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/变量/变量数据库", fileName = "VariableDatabase")]
public class VariableDatabase : ScriptableObject
{
    public List<VariableDeclaration> variables = new List<VariableDeclaration>();

    // 不是 LogicSlotValueType 里所有值都能当变量用——Percent/Position/Enum/
    // ComparisonOperator/Any/Area/ConditionReference/Currency/AudioClip/
    // DialogueLibrary 这些要么是别的槽位专用语义，要么没有对应的存储/求值实现。
    private static readonly LogicSlotValueType[] SupportedTypes =
    {
        LogicSlotValueType.Bool,
        LogicSlotValueType.Int,
        LogicSlotValueType.Float,
        LogicSlotValueType.String,
        LogicSlotValueType.Unit,
        LogicSlotValueType.Item,
    };

    public static IReadOnlyList<LogicSlotValueType> SupportedVariableTypes => SupportedTypes;

    public static bool IsSupportedVariableType(LogicSlotValueType type) => Array.IndexOf(SupportedTypes, type) >= 0;

    // [InspectorName] 对 EditorGUILayout.EnumPopup 这种走反射的API有用，但变量表/
    // 变量选择器这边是手动拼 string[] 传给 Popup、列表行文字也是手动拼字符串，
    // 都不会走那条反射路径，直接用 .ToString() 只会出英文——所以单独给这几个类型
    // 配一份中文映射，两边都用这个，不要各自再拼一份。
    public static string GetTypeLabel(LogicSlotValueType type)
    {
        return type switch
        {
            LogicSlotValueType.Bool => "布尔",
            LogicSlotValueType.Int => "整数",
            LogicSlotValueType.Float => "浮点数",
            LogicSlotValueType.String => "字符串",
            LogicSlotValueType.Unit => "单位",
            LogicSlotValueType.Item => "物品",
            _ => type.ToString()
        };
    }

    private const string ResourcesPath = "VariableDatabase";

    // 不额外做静态缓存——Resources.Load 本身有资源系统自己的缓存，多加一层静态字段
    // 只会在 Domain Reload 边界上引入"缓存没跟着清掉、读到脏引用"的风险，参考
    // QuestRuntime 也是每次直接 Resources.Load，不单独缓存。
    public static VariableDatabase LoadOrNull() => Resources.Load<VariableDatabase>(ResourcesPath);

    public IEnumerable<VariableDeclaration> GetByType(LogicSlotValueType type)
    {
        foreach (VariableDeclaration decl in variables)
            if (decl != null && decl.type == type)
                yield return decl;
    }

    public VariableDeclaration FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return variables.Find(d => d != null && d.variableName == name);
    }
}
