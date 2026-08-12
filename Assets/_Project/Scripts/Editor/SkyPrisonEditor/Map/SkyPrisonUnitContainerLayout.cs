using UnityEngine;

/// <summary>
/// 单位 / 孵化器在场景层级里的归属容器——唯一事实来源。
///
/// 存在的理由：放置工具原来把路径硬编码成 "WorldRoot/UnitRoot"，而场景的分类树在根级
/// UnitRoot 下。GetOrCreateParent 找不到就自己建了个同名空壳，于是手摆的单位在分类树里、
/// 工具放的单位在树外面，两边都"看起来正常"，只有去数的时候才发现对不上。
///
/// 这类错误的根源是同一份路径知识散落在多处。放置工具、审计工具、以后任何要往场景里
/// 塞单位的代码都从这里取，改一处就够。
/// </summary>
public static class SkyPrisonUnitContainerLayout
{
    /// <summary>兜底容器。分类树根节点，不是 WorldRoot 下面那个。</summary>
    public const string UnitRootPath = "UnitRoot";

    /// <summary>
    /// 孵化器有自己的位置，不和单位混在一起——它是刷怪配置不是单位，
    /// 混进去会让「这张图摆了哪些怪」没法直接数。
    /// </summary>
    public const string SpawnerParentPath = "UnitRoot/EnemyRoot/EnemySpawnerRoot";

    /// <summary>
    /// 历史遗留的空壳容器。工具建出来的，没有任何东西该待在里面。
    /// 审计时如果发现它们是空的就清掉；非空说明还有对象没归位，先报告不删。
    /// </summary>
    public static readonly string[] StrayContainerPaths =
    {
        "WorldRoot/UnitRoot",
        "WorldRoot/LootRoot",
    };

    /// <summary>
    /// 按单位身份决定它该待在哪个容器。
    ///
    /// NPCRoot 下还有 Follower / Quest / Vendor 三个子类，但 CharacterIdentity 里它们
    /// 都是 Ally，枚举本身区分不出来，所以统一进 NPCFriendlyRoot。真要细分得给
    /// UnitDefinition 加一个 NPC 角色字段。
    /// </summary>
    public static string ResolveUnitParentPath(UnitDefinition definition)
    {
        if (definition == null)
            return UnitRootPath;

        switch (definition.defineType)
        {
            case UnitDefineType.Destructible:
                return "UnitRoot/DestructibleRoot";
            case UnitDefineType.Item:
                return "UnitRoot/LootRoot";
        }

        switch (definition.characterIdentity)
        {
            case CharacterIdentity.Player:         return "UnitRoot/PlayerRoot";
            case CharacterIdentity.Enemy:          return "UnitRoot/EnemyRoot/EnemyMobRoot";
            case CharacterIdentity.Elite:          return "UnitRoot/EnemyRoot/EnemyEliteRoot";
            case CharacterIdentity.Boss:           return "UnitRoot/EnemyRoot/EnemyBossRoot";
            case CharacterIdentity.Ally:           return "UnitRoot/NPCRoot/NPCFriendlyRoot";
            case CharacterIdentity.NeutralHostile: return "UnitRoot/NeutralRoot/NeutralHostileRoot";
            case CharacterIdentity.NeutralPassive: return "UnitRoot/NeutralRoot/NeutralPassiveRoot";
            case CharacterIdentity.Creature:       return "UnitRoot/NeutralRoot/CreatureRoot";
        }

        return UnitRootPath;
    }
}
