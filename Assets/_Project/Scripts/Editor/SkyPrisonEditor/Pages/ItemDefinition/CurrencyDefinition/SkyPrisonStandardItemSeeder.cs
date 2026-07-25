using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonStandardItemSeeder
{
    private const string StandardItemFolder = "Assets/_Project/Data/Definitions/Standard/Items";

    private struct SeedItem
    {
        public int itemId;
        public string itemKey;
        public string displayName;
        public string description;
        public ItemUsability usability;
        public float cooldown;
        public ItemStatusType statusType;
        public int maxStackCount;
        public ItemCategory category;
        public int itemLevel;
        public int value;
        public int origin;
        public float weightOrQuartz;
        public bool canDiscard;
        public string zhName;
        public string zhDesc;
        public string jaName;
        public string jaDesc;
        public string enName;
        public string enDesc;
    }

    [MenuItem("Tools/Sky Prison/Items/导入或更新标准物品")]
    public static void ImportOrUpdateStandardItems()
    {
        EnsureFolderExists(StandardItemFolder);

        List<SeedItem> seedItems = BuildSeedItems();
        int created = 0;
        int updated = 0;

        for (int i = 0; i < seedItems.Count; i++)
        {
            SeedItem seed = seedItems[i];
            ItemDefinition asset = FindByItemKey(seed.itemKey);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ItemDefinition>();
                string path = AssetDatabase.GenerateUniqueAssetPath($"{StandardItemFolder}/ID_{seed.itemKey}.asset");
                AssetDatabase.CreateAsset(asset, path);
                created++;
            }
            else
            {
                updated++;
            }

            ApplySeed(asset, seed);
            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "标准物品导入完成",
            $"创建：{created}\n更新：{updated}\n\n目录：{StandardItemFolder}",
            "确定"
        );
    }

    private static void ApplySeed(ItemDefinition asset, SeedItem seed)
    {
        asset.itemId = seed.itemId;
        asset.itemKey = seed.itemKey;
        asset.nameKey = $"item_name_{seed.itemKey}";
        asset.descKey = $"item_desc_{seed.itemKey}";
        asset.iconKey = $"icon_{seed.itemKey}";
        asset.displayName = seed.displayName;
        asset.description = seed.description;
        asset.usability = seed.usability;
        asset.cooldown = seed.cooldown;
        asset.statusType = seed.statusType;
        asset.maxStackCount = seed.maxStackCount;
        asset.category = seed.category;
        asset.itemLevel = seed.itemLevel;
        asset.value = seed.value;
        asset.origin = seed.origin;
        asset.weightOrQuartz = seed.weightOrQuartz;
        asset.canDiscard = seed.canDiscard;

        SetLocalizedText(asset.localizedNames, "zh-CN", seed.zhName);
        SetLocalizedText(asset.localizedDescriptions, "zh-CN", seed.zhDesc);
        SetLocalizedText(asset.localizedNames, "ja-JP", seed.jaName);
        SetLocalizedText(asset.localizedDescriptions, "ja-JP", seed.jaDesc);
        SetLocalizedText(asset.localizedNames, "en-US", seed.enName);
        SetLocalizedText(asset.localizedDescriptions, "en-US", seed.enDesc);
    }

    private static void SetLocalizedText(List<LocalizedTextEntry> list, string languageCode, string text)
    {
        if (list == null)
            return;

        LocalizedTextEntry entry = list.Find(x => x != null && x.languageCode == languageCode);
        if (entry == null)
        {
            entry = new LocalizedTextEntry { languageCode = languageCode, text = text ?? "" };
            list.Add(entry);
        }
        else
        {
            entry.text = text ?? "";
        }
    }

    private static ItemDefinition FindByItemKey(string itemKey)
    {
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null && item.itemKey == itemKey)
                return item;
        }

        return null;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static List<SeedItem> BuildSeedItems()
    {
        return new List<SeedItem>
        {
            new SeedItem
            {
                itemId = 10000,
                itemKey = "first_aid_medicine",
                displayName = "急救药物",
                description = "用于快速恢复状态的基础医疗用品。",
                usability = ItemUsability.Usable,
                cooldown = 0f,
                statusType = ItemStatusType.Instant,
                maxStackCount = 20,
                category = ItemCategory.Consumable,
                itemLevel = 1,
                value = 120,
                origin = 0,
                weightOrQuartz = 0.3f,
                canDiscard = true,
                zhName = "急救药物",
                zhDesc = "用于快速恢复状态的基础医疗用品。",
                jaName = "応急医薬品",
                jaDesc = "素早く状態を回復するための基礎医療用品。",
                enName = "First Aid Medicine",
                enDesc = "Basic medical supplies used for quick recovery."
            },
            new SeedItem
            {
                itemId = 20000,
                itemKey = "cooling_agent",
                displayName = "冷却药剂",
                description = "帮助机体或装备快速冷却的药剂。",
                usability = ItemUsability.Usable,
                cooldown = 0f,
                statusType = ItemStatusType.Instant,
                maxStackCount = 20,
                category = ItemCategory.Consumable,
                itemLevel = 1,
                value = 140,
                origin = 0,
                weightOrQuartz = 0.3f,
                canDiscard = true,
                zhName = "冷却药剂",
                zhDesc = "帮助机体或装备快速冷却的药剂。",
                jaName = "冷却薬剤",
                jaDesc = "機体や装備を素早く冷却する薬剤。",
                enName = "Cooling Agent",
                enDesc = "A reagent that rapidly cools the body or equipment."
            },
            new SeedItem
            {
                itemId = 30000,
                itemKey = "scratch_card",
                displayName = "刮刮卡",
                description = "可在指定商店兑换奖励的凭证。",
                usability = ItemUsability.NotUsable,
                cooldown = 0f,
                statusType = ItemStatusType.None,
                maxStackCount = 99,
                category = ItemCategory.Currency,
                itemLevel = 1,
                value = 50,
                origin = 0,
                weightOrQuartz = 0f,
                canDiscard = true,
                zhName = "刮刮卡",
                zhDesc = "可在指定商店兑换奖励的凭证。",
                jaName = "スクラッチカード",
                jaDesc = "指定ショップで報酬と交換できる券。",
                enName = "Scratch Card",
                enDesc = "A voucher redeemable for rewards at certain shops."
            },
            new SeedItem
            {
                itemId = 40000,
                itemKey = "exchange_voucher",
                displayName = "兑换凭证",
                description = "常用于贸易与结算的通用凭证。",
                usability = ItemUsability.NotUsable,
                cooldown = 0f,
                statusType = ItemStatusType.None,
                maxStackCount = 999,
                category = ItemCategory.Currency,
                itemLevel = 1,
                value = 1,
                origin = 0,
                weightOrQuartz = 0f,
                canDiscard = true,
                zhName = "兑换凭证",
                zhDesc = "常用于贸易与结算的通用凭证。",
                jaName = "交換証票",
                jaDesc = "取引や精算によく使われる汎用証票。",
                enName = "Exchange Voucher",
                enDesc = "A general voucher used in trade and settlement."
            },
            new SeedItem
            {
                itemId = 50000,
                itemKey = "compressed_biscuit",
                displayName = "压缩饼干",
                description = "便携耐储存的基础食物。",
                usability = ItemUsability.Usable,
                cooldown = 0f,
                statusType = ItemStatusType.Instant,
                maxStackCount = 20,
                category = ItemCategory.Consumable,
                itemLevel = 1,
                value = 30,
                origin = 0,
                weightOrQuartz = 0.2f,
                canDiscard = true,
                zhName = "压缩饼干",
                zhDesc = "便携耐储存的基础食物。",
                jaName = "圧縮ビスケット",
                jaDesc = "携行しやすく保存性に優れた基本食料。",
                enName = "Compressed Biscuit",
                enDesc = "A compact ration with good shelf life."
            },
            new SeedItem
            {
                itemId = 60000,
                itemKey = "canned_food",
                displayName = "罐头",
                description = "常见的储备食物。",
                usability = ItemUsability.Usable,
                cooldown = 0f,
                statusType = ItemStatusType.Instant,
                maxStackCount = 20,
                category = ItemCategory.Consumable,
                itemLevel = 1,
                value = 45,
                origin = 0,
                weightOrQuartz = 0.5f,
                canDiscard = true,
                zhName = "罐头",
                zhDesc = "常见的储备食物。",
                jaName = "缶詰",
                jaDesc = "一般的な保存食。",
                enName = "Canned Food",
                enDesc = "A common preserved food item."
            },
            new SeedItem
            {
                itemId = 70000,
                itemKey = "factory_parts",
                displayName = "工厂零件",
                description = "可用于制造与维修的基础工业零件。",
                usability = ItemUsability.NotUsable,
                cooldown = 0f,
                statusType = ItemStatusType.None,
                maxStackCount = 99,
                category = ItemCategory.Material,
                itemLevel = 1,
                value = 80,
                origin = 0,
                weightOrQuartz = 0.8f,
                canDiscard = true,
                zhName = "工厂零件",
                zhDesc = "可用于制造与维修的基础工业零件。",
                jaName = "工場部品",
                jaDesc = "製造や修理に使える基礎工業部品。",
                enName = "Factory Parts",
                enDesc = "Basic industrial parts used in crafting and repair."
            },
            new SeedItem
            {
                itemId = 80000,
                itemKey = "dry_battery",
                displayName = "干电池",
                description = "常见能源材料，可为部分设备供电。",
                usability = ItemUsability.NotUsable,
                cooldown = 0f,
                statusType = ItemStatusType.None,
                maxStackCount = 99,
                category = ItemCategory.Material,
                itemLevel = 1,
                value = 25,
                origin = 0,
                weightOrQuartz = 0.2f,
                canDiscard = true,
                zhName = "干电池",
                zhDesc = "常见能源材料，可为部分设备供电。",
                jaName = "乾電池",
                jaDesc = "一般的な電源素材で、一部設備に電力を供給できる。",
                enName = "Dry Battery",
                enDesc = "A common energy material used to power certain devices."
            }
        };
    }
}
