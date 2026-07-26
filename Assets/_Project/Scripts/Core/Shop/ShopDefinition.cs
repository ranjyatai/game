using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixed=固定货架，items 里配几个就一直卖几个（补给站这种）。
/// RandomPool=随机商店（古董店这种）：items 当"物品池"用，每次刷新从当前解锁等级
/// 能买到的物品里随机抽 randomDisplayCount 件上架，价格如果配了随机区间也会一起
/// 重新抽。
/// </summary>
public enum ShopStockMode
{
    [InspectorName("固定商店")]
    Fixed = 0,
    [InspectorName("随机商店")]
    RandomPool = 1,
}

/// <summary>
/// 商店定义 ScriptableObject。
/// 补给站/古物街各建一个 asset，配好货架即可。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/商店/商店定义", fileName = "ShopDefinition")]
public class ShopDefinition : ScriptableObject
{
    [Header("基本信息")]
    public string shopId = "supply_post";
    public string displayName = "补给站"; // 兜底文字——本地化表查不到 displayNameKey 时用这个

    [Tooltip("商店名字的本地化 Key，实际多语言文字存在 UILocalizationTable 里（跟界面设置窗口的\"商店\"页联动编辑）")]
    public string displayNameKey = "";

    [Header("默认货币（商品未单独配置时使用）")]
    public string defaultCurrencyId = "token";

    [Tooltip("商店当前解锁等级——类似基地设施的科技等级，以后会由中枢等级等外部系统驱动提升，" +
             "现在先自成一体：货架只展示 ShopItemEntry.unlockLevel <= 这个值的商品。")]
    public int currentLevel = 1;

    [Header("商店模式")]
    public ShopStockMode stockMode = ShopStockMode.Fixed;

    [Tooltip("随机商店模式下，每次刷新从解锁的物品池里随机抽几件上架（比如20件池子抽5件展示）")]
    public int randomDisplayCount = 5;

    [Header("货架（Fixed模式=固定卖这些；RandomPool模式=当物品池用）")]
    public List<ShopItemEntry> items = new List<ShopItemEntry>();

    [Header("库存刷新（章节开始时重置库存，随机商店模式下同时重新抽选品/价格）")]
    public bool refreshStockOnChapterStart = true;
}
