using System;

/// <summary>
/// 变量表里的一条声明——名字+类型。类型直接复用 LogicSlotValueType（不是所有枚举值
/// 都合法，只有 Bool/Int/Float/String/Unit/Item 这几个能当变量用，见
/// VariableDatabase.SupportedVariableTypes），这样声明的类型跟槽位的valueType
/// 是同一套枚举，判定"这个变量能不能填进这个槽位"直接比较枚举值就行，不用另外
/// 写一套类型映射。
/// </summary>
[Serializable]
public class VariableDeclaration
{
    public string variableName = "";
    public LogicSlotValueType type = LogicSlotValueType.Int;
}
