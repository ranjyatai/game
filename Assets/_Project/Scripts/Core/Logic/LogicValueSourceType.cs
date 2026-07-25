public enum LogicValueSourceType
{
    Constant = 0,
    Variable = 1,
    ContextReference = 2,
    AssetReference = 3,
    SceneReference = 4,
    RandomRange = 5,
    // 2026-07-17：WE式嵌套四则运算——只对 Int/Float 槽位开放（LogicIntSlotHandler/
    // LogicFloatSlotHandler 里才会用到），本身又是一个完整的 LogicSlotValue（运算数
    // A/B 各自可以再是常量/变量/另一个表达式），靠 LogicSlotValue.expressionOperandA/B
    // 递归求值。
    Expression = 6
}