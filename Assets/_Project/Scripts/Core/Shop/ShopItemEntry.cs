using System;
using UnityEngine;

[Serializable]
public class ShopItemEntry
{
    [Tooltip("售卖的物品")]
    public ItemDefinition item;

    [Tooltip("每次购买的固定数量（0 = 玩家自选数量）")]
    public int fixedCount = 0;

    [Tooltip("最大库存（-1 = 无限）")]
    public int stock = -1;

    [Tooltip("货币 ID，留空则使用商店默认货币")]
    public string currencyOverride = "";

    [Tooltip("价格覆盖（0 = 读 ItemDefinition.currencyPrices）")]
    public int priceOverride = 0;

    [Tooltip("解锁所需商店等级——ShopDefinition.currentLevel < 这个值时，货架不显示这件商品")]
    public int unlockLevel = 1;

    [Header("随机价格区间（仅 ShopDefinition.stockMode=RandomPool 时生效，都填>0且Max>=Min才启用；" +
             "留空则这件商品价格照常走 priceOverride/物品定价，不参与随机）")]
    public int randomPriceMin = 0;
    public int randomPriceMax = 0;

    [NonSerialized] public int remainingStock = -1; // 运行时剩余库存
    // 随机商店模式下每次刷新抽到的价格——-1表示这次没抽/不用随机价，优先级比
    // priceOverride更高（毕竟是"这次进货刚好抽到的价"，理应盖过静态配置的价格）。
    [NonSerialized] public int rolledPrice = -1;

    public bool IsOutOfStock => stock >= 0 && remainingStock <= 0;

    /// <summary>是否启用了随机价格区间。</summary>
    public bool HasRandomPriceRange => randomPriceMax > 0 && randomPriceMax >= randomPriceMin;

    /// <summary>返回该条目实际货币 ID，优先使用 currencyOverride，再从物品定义读第一条。</summary>
    public string ResolveCurrencyId(string shopDefaultCurrency)
    {
        if (!string.IsNullOrEmpty(currencyOverride)) return currencyOverride;
        if (!string.IsNullOrEmpty(shopDefaultCurrency)) return shopDefaultCurrency;
        if (item != null && item.currencyPrices.Count > 0) return item.currencyPrices[0].currencyId;
        return "";
    }

    /// <summary>返回单价。随机商店本次抽到的价格 > priceOverride > 物品定义定价。</summary>
    public int ResolvePrice(string resolvedCurrencyId)
    {
        if (rolledPrice >= 0) return rolledPrice;
        if (priceOverride > 0) return priceOverride;
        if (item == null) return 0;
        foreach (var p in item.currencyPrices)
            if (p.currencyId == resolvedCurrencyId) return p.price;
        return 0;
    }
}
