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
    AudioClip = 15,

    // 台本句子库引用
    DialogueLibrary = 16,

    // 商店定义引用
    Shop = 17,

    // 任务定义引用
    Quest = 18,

    // 单位定义(资产本身)引用——跟 Unit(=场景内具体某个单位实例，SceneReference)
    // 是两码事，这个存的是UnitDefinition资产的直接引用(AssetReference)，比如
    // "击杀多少个这种怪"这类不针对场景内具体某个实例、而是针对"这一种单位"的场景。
    UnitAsset = 19
}
