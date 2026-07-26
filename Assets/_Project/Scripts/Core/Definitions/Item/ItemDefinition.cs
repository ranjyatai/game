using System;
using System.Collections.Generic;
using Effekseer;
using UnityEngine;

public enum ItemMajorCategory
{
    [InspectorName("一般道具")]
    General = 0,

    [InspectorName("装备")]
    Equipment = 10,
}

public enum ItemCategory
{
    [InspectorName("消耗品")]
    Consumable,

    [InspectorName("材料")]
    Material,

    [InspectorName("任务物品")]
    Quest,

    // Compatibility only.
    // Weapon / Armor / Accessory have moved to ItemMajorCategory.Equipment + EquipmentSlotType.
    // The item editor no longer exposes these options in the old category dropdown.
    [InspectorName("旧-武器（已迁移）")]
    Weapon,

    [InspectorName("旧-护甲（已迁移）")]
    Armor,

    [InspectorName("旧-饰品（已迁移）")]
    Accessory,

    [InspectorName("凭证 / 货币")]
    Currency,

    [InspectorName("特殊")]
    Special
}

public enum MaterialSubCategory
{
    [InspectorName("零件")]   Part       = 0,
    [InspectorName("金属块")] Metal      = 1,
    [InspectorName("液体")]   Liquid     = 2,
    [InspectorName("杂物")]   Misc       = 3,
    [InspectorName("弹药")]   Ammunition = 4,
}

// 口径只分两级，不做"每把枪独立弹药"那种硬核颗粒度——热武器目前只有双枪（打小口径），
// 大口径先留给DEMO之后才会加入的炮这类重武器，框架先搭好，炮的具体数值/模组到时候再配。
public enum AmmoCaliberType
{
    [InspectorName("小口径")] SmallCaliber = 0,
    [InspectorName("大口径")] LargeCaliber = 1,
}

[Serializable]
public class ItemAmmoExtension
{
    [Tooltip("这发弹药的口径——武器装备的 weaponAmmoCaliber 要跟这个一致才能用。")]
    public AmmoCaliberType caliber = AmmoCaliberType.SmallCaliber;
}

public enum ItemRarity
{
    [InspectorName("普通")]   Common    = 0,
    [InspectorName("优质")]   Uncommon  = 1,
    [InspectorName("稀有")]   Rare      = 2,
    [InspectorName("史诗")]   Epic      = 3,
    [InspectorName("传说")]   Legendary = 4,
}

public enum ItemUsability
{
    [InspectorName("不可使用")]
    NotUsable,

    [InspectorName("可使用")]
    Usable
}

public enum ItemStatusType
{
    [InspectorName("无")]
    None = 0,

    [InspectorName("即时效果")]
    Instant = 1,

    [InspectorName("持续效果")]
    Ongoing = 2,

    [InspectorName("特殊效果")]
    Special = 3
}

[Serializable]
public class ItemCurrencyPriceValue
{
    public string currencyId = "";
    public int price = 0;
}

[CreateAssetMenu(menuName = "Sky Prison/Items/Item Definition", fileName = "ID_NewItem")]
public class ItemDefinition : ScriptableObject
{
    [Header("基础信息")]
    public int itemId = 10000;
    public string itemKey = "new_item";
    public string nameKey = "item_name_new_item";
    public string descKey = "item_desc_new_item";
    public string iconKey = "icon_new_item";

    public string displayName = "新道具";
    public string note = "";

    [TextArea(3, 8)]
    public string description = "";

    public Sprite icon;

    [Header("多语言名称")]
    public List<LocalizedTextEntry> localizedNames = new List<LocalizedTextEntry>();

    [Header("多语言描述")]
    public List<LocalizedTextEntry> localizedDescriptions = new List<LocalizedTextEntry>();

    // 之前全项目所有引用物品名字/描述的地方都是直接读 displayName/description 这两个
    // 原始字段，localizedNames/localizedDescriptions 这两份多语言数据填了但完全没人读，
    // UI 上永远显示中文。这两个方法统一走 LocalizationRuntime 解析当前语言，找不到
    // 对应语言词条时退回 displayName/description 原始字段（兼容还没填多语言的旧物品）。
    public string GetLocalizedDisplayName()
    {
        if (LocalizationRuntime.Instance != null)
            return LocalizationRuntime.Instance.GetText(localizedNames, displayName);
        return displayName;
    }

    public string GetLocalizedDescription()
    {
        if (LocalizationRuntime.Instance != null)
            return LocalizationRuntime.Instance.GetText(localizedDescriptions, description);
        return description;
    }

    [Header("规则")]
    public ItemUsability usability = ItemUsability.NotUsable;
    public float cooldown = 0f;
    public ItemStatusType statusType = ItemStatusType.None;

    [Header("分类")]
    public ItemMajorCategory majorCategory = ItemMajorCategory.General;
    public ItemCategory category = ItemCategory.Consumable;
    /// <summary>仅 category == Material 时有效。</summary>
    public MaterialSubCategory materialSubCategory = MaterialSubCategory.Misc;
    public ItemRarity rarity = ItemRarity.Common;
    public int itemLevel = 1;

    [Header("一般道具扩展")]
    public ItemGeneralExtension general = new ItemGeneralExtension();

    // 只有 category == Material 的物品才有意义——强化熔炉只认这些字段判断这个素材
    // 跟装备/其它素材的相性，非材料物品这些字段留着默认值不会被读取。
    [Header("素材炼金扩展（仅材料物品填写）")]
    public ItemMaterialAlchemyExtension materialAlchemy = new ItemMaterialAlchemyExtension();

    // 只有 category == Material 且 materialSubCategory == Ammunition 的物品才有意义——
    // 弹药本身就是一种材料子分类，这个扩展只额外标一下它是哪个口径。
    [Header("弹药扩展（仅弹药物品填写）")]
    public ItemAmmoExtension ammo = new ItemAmmoExtension();

    [Header("装备扩展")]
    public ItemEquipmentExtension equipment = new ItemEquipmentExtension();

    [Header("武器组件扩展（仅组件物品填写）")]
    public ItemModExtension mod = new ItemModExtension();

    [Header("外观扩展")]
    public ItemAppearanceExtension appearance = new ItemAppearanceExtension();

    [Header("持有与价值")]
    public int maxStackCount = 1;
    public int value = 0;
    public int origin = 0;
    public float weightOrQuartz = 0f;
    public bool isKeyItem = false;
    public bool canDiscard = true;
    [Tooltip("能不能卖给商店——任务道具/重要物品这种天生不该卖的关掉，不管去哪家店都不会出现在出售列表里。商店自己还会再按分类过滤一层，两层都过了才能卖。")]
    public bool canSell = true;

    [Header("使用效果")]
    public List<ItemEffectEntry> effects = new List<ItemEffectEntry>();

    [Header("使用 VFX / SFX")]
    [Tooltip("使用这件道具那一刻播放的 Effekseer 特效，播在玩家角色位置。留空则不播放。\n" +
        "背包窗口和快捷道具栏两个入口最终都会走到 ItemEffectExecutor.Execute，在那里统一" +
        "触发，两边都会生效，不用分别配置。")]
    public EffekseerEffectAsset useVFX;
    [Tooltip("使用这件道具那一刻播放的音效，随机取一个片段，每条素材可以单独设自己的音量。\n" +
        "留空则不播放。")]
    public SkillSoundEntry[] useSE = System.Array.Empty<SkillSoundEntry>();
    [Tooltip("使用音效的整体音量倍率，在每条素材自己的音量倍率基础上再乘一次。1=不额外调整。")]
    public float useSEVolume = 1f;

    [Header("价格与货币")]
    public List<ItemCurrencyPriceValue> currencyPrices = new List<ItemCurrencyPriceValue>();

    public bool IsUsable => usability == ItemUsability.Usable;

    public bool IsGeneralItem    => majorCategory == ItemMajorCategory.General;
    public bool IsEquipmentItem  => majorCategory == ItemMajorCategory.Equipment;

    // 强化熔炉的输入校验用——只有材料能丢进去，可使用道具/装备/防具/任务物品/
    // 凭证等一律不行，不管它们是不是又是消耗品又是可堆叠物品什么的，只认这一条。
    public bool IsAlchemyMaterial => majorCategory == ItemMajorCategory.General && category == ItemCategory.Material;

    public void NormalizeMajorCategoryRules()
    {
        if (majorCategory == ItemMajorCategory.General)
        {
            if (category == ItemCategory.Weapon || category == ItemCategory.Armor || category == ItemCategory.Accessory)
                category = ItemCategory.Consumable;
            return;
        }

        usability = ItemUsability.NotUsable;
        cooldown = 0f;
        statusType = ItemStatusType.None;
    }

    private void OnValidate()
    {
        NormalizeMajorCategoryRules();
        if (isKeyItem)
            canDiscard = false;
    }
}
