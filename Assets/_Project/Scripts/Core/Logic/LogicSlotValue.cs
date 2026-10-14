using UnityEngine;

[System.Serializable]
public class LogicSlotValue
{
    [Header("类型")]
    public LogicSlotValueType valueType = LogicSlotValueType.Float;
    public LogicValueSourceType sourceType = LogicValueSourceType.Constant;

    [Header("固定值")]
    public bool boolValue;
    public int intValue;
    public int intValueMax;
    public float floatValue;
    public float floatValueMax;
    public string stringValue;

    [Header("扩展值")]
    public string enumValue;
    public string enumDisplayName;
    public string variableKey;
    public string contextKey;

    [Header("资源引用")]
    public Object assetReference;

    [Header("场景引用")]
    public string sceneObjectId;
    public string sceneObjectName;

    // 2026-07-17：WE式嵌套四则运算。只有Int/Float槽位会真正用到（sourceType=
    // Expression时），运算数A/B各自是完整的LogicSlotValue——可以是常量、随机范围，
    // 也可以是另一个表达式，靠 EvaluateInt/EvaluateFloat 递归求值。不用
    // [SerializeReference]（那是给"任意多态类型"用的，这里A/B永远是LogicSlotValue
    // 这一个类型，普通嵌套序列化就够，Unity嵌套同类型字段默认支持到7层，
    // 实际写触发器不可能嵌套这么深）。
    [Header("运算表达式（仅Int/Float槽位）")]
    public LogicMathOperator expressionOperator = LogicMathOperator.Add;
    public LogicSlotValue expressionOperandA;
    public LogicSlotValue expressionOperandB;

    public bool IsEmpty()
    {
        return sourceType switch
        {
            LogicValueSourceType.Variable => string.IsNullOrWhiteSpace(variableKey),
            LogicValueSourceType.ContextReference => string.IsNullOrWhiteSpace(contextKey),
            LogicValueSourceType.AssetReference => assetReference == null,
            LogicValueSourceType.SceneReference => string.IsNullOrWhiteSpace(sceneObjectId),
            LogicValueSourceType.Expression => expressionOperandA == null || expressionOperandB == null
                || expressionOperandA.IsEmpty() || expressionOperandB.IsEmpty(),
            _ => false
        };
    }

    /// <summary>把 Int 槽位求值成最终数字——常量/随机范围直接返回，表达式递归求值
    /// 两个运算数再套用运算符，Variable 读 SkyPrisonVariableRuntime 里的自定义变量
    /// (嵌套表达式的运算数本身也是LogicSlotValue，递归调EvaluateInt()时天然支持
    /// "变量A+变量B"这种嵌套，不用额外处理)。ContextReference(玩家货币/属性这类
    /// 内置只读上下文值)还没接，先退回intValue，等做那块的时候再补进这个switch。</summary>
    public int EvaluateInt()
    {
        return sourceType switch
        {
            LogicValueSourceType.RandomRange => UnityEngine.Random.Range(intValue, intValueMax + 1),
            LogicValueSourceType.Expression => EvaluateExpressionInt(),
            LogicValueSourceType.Variable => SkyPrisonVariableRuntime.GetInt(variableKey),
            _ => intValue
        };
    }

    /// <summary>Float版EvaluateInt，逻辑完全对应。</summary>
    public float EvaluateFloat()
    {
        return sourceType switch
        {
            LogicValueSourceType.RandomRange => UnityEngine.Random.Range(floatValue, floatValueMax),
            LogicValueSourceType.Expression => EvaluateExpressionFloat(),
            LogicValueSourceType.Variable => SkyPrisonVariableRuntime.GetFloat(variableKey),
            _ => floatValue
        };
    }

    /// <summary>Bool槽位求值——之前完全没有这个方法，各处直接读 boolValue 原始字段，
    /// Variable/其它来源完全不生效。新补的，调用方要用变量就必须换成调这个方法，
    /// 不能再直接读 .boolValue。</summary>
    public bool EvaluateBool()
    {
        return sourceType switch
        {
            LogicValueSourceType.Variable => SkyPrisonVariableRuntime.GetBool(variableKey),
            _ => boolValue
        };
    }

    /// <summary>String槽位求值，跟EvaluateBool同理——之前也是没有这个方法。</summary>
    public string EvaluateString()
    {
        return sourceType switch
        {
            LogicValueSourceType.Variable => SkyPrisonVariableRuntime.GetString(variableKey),
            _ => stringValue
        };
    }

    private int EvaluateExpressionInt()
    {
        int a = expressionOperandA != null ? expressionOperandA.EvaluateInt() : 0;
        int b = expressionOperandB != null ? expressionOperandB.EvaluateInt() : 0;
        return expressionOperator switch
        {
            LogicMathOperator.Add => a + b,
            LogicMathOperator.Subtract => a - b,
            LogicMathOperator.Multiply => a * b,
            LogicMathOperator.Divide => b != 0 ? a / b : 0,
            _ => a
        };
    }

    private float EvaluateExpressionFloat()
    {
        float a = expressionOperandA != null ? expressionOperandA.EvaluateFloat() : 0f;
        float b = expressionOperandB != null ? expressionOperandB.EvaluateFloat() : 0f;
        return expressionOperator switch
        {
            LogicMathOperator.Add => a + b,
            LogicMathOperator.Subtract => a - b,
            LogicMathOperator.Multiply => a * b,
            LogicMathOperator.Divide => !Mathf.Approximately(b, 0f) ? a / b : 0f,
            _ => a
        };
    }

    public string GetDisplayText()
    {
        return sourceType switch
        {
            LogicValueSourceType.Constant => GetConstantDisplayText(),
            LogicValueSourceType.Variable => string.IsNullOrWhiteSpace(variableKey) ? "<未指定变量>" : variableKey,
            LogicValueSourceType.ContextReference => string.IsNullOrWhiteSpace(contextKey) ? "<未指定上下文>" : contextKey,
            LogicValueSourceType.AssetReference => assetReference != null ? assetReference.name : "<未指定资源>",
            LogicValueSourceType.SceneReference => string.IsNullOrWhiteSpace(sceneObjectName)
                ? "<未指定场景对象>"
                : $"{sceneObjectName} [{sceneObjectId}]",
            LogicValueSourceType.Expression => GetExpressionDisplayText(),
            _ => "<未知来源>"
        };
    }

    private string GetExpressionDisplayText()
    {
        string a = expressionOperandA != null ? expressionOperandA.GetDisplayText() : "<A>";
        string b = expressionOperandB != null ? expressionOperandB.GetDisplayText() : "<B>";
        return $"({a} {GetOperatorSymbol(expressionOperator)} {b})";
    }

    /// <summary>运算符→符号，编辑器UI（LogicExpressionSlotEditorGUI）也用这个，避免
    /// 两处各写一份映射。</summary>
    public static string GetOperatorSymbol(LogicMathOperator op)
    {
        return op switch
        {
            LogicMathOperator.Add => "+",
            LogicMathOperator.Subtract => "-",
            LogicMathOperator.Multiply => "×",
            LogicMathOperator.Divide => "÷",
            _ => "?"
        };
    }

    private string GetConstantDisplayText()
    {
        return valueType switch
        {
            LogicSlotValueType.Bool => boolValue ? "True" : "False",
            LogicSlotValueType.Int => intValue.ToString(),
            LogicSlotValueType.Float => floatValue.ToString("0.##"),
            LogicSlotValueType.Percent => floatValue.ToString("0.##") + "%",
            LogicSlotValueType.String => string.IsNullOrWhiteSpace(stringValue) ? "\"\"" : stringValue,
            LogicSlotValueType.Enum => GetEnumDisplayText(),
            _ => "<常量>"
        };
    }

    private string GetEnumDisplayText()
    {
        if (!string.IsNullOrWhiteSpace(enumDisplayName))
            return enumDisplayName;

        if (!string.IsNullOrWhiteSpace(enumValue))
            return enumValue;

        return "<未指定枚举值>";
    }
}
