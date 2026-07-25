public enum LogicSlotValueType
{
    Bool = 0,
    Int = 1,
    Float = 2,
    Percent = 3,
    Unit = 4,
    Position = 5,
    AIBehaviorPackage = 6,
    Enum = 7,
    String = 8,
    ComparisonOperator = 9,

    // 通用任意值（用于通用比较）
    Any = 10,

    // 区域
    Area = 11,

    // 条件引用（用于 If / While）
    ConditionReference = 12,

    // 物品定义引用
    Item = 13,

    // 货币定义引用
    Currency = 14,

    // AudioClip 资源引用
    AudioClip = 15
}
