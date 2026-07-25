using System.Collections.Generic;

public static class AILogicSentenceTemplateLibrarySystem
{
    public static List<LogicSentenceTemplate> GetTemplates()
    {
        return new List<LogicSentenceTemplate>
        {
            // 事件
            BuildEventItemGained(),
            BuildEventItemLost(),
            BuildEventItemUsed(),

            // 动作
            BuildActionAddItemToBag(),
            BuildActionRemoveItemFromBag(),
            BuildActionAddCurrency(),
            BuildActionPlaySE(),
            BuildActionPlayCustomSE(),
            BuildActionPauseGame(),
            BuildActionResumeGame(),
            BuildActionTriggerAutoSave(),
            BuildActionSetBGMLayer(),
            BuildActionPlayBGMTrackFromStart(),
            BuildActionPlayBGMTrackAtTime()
        };
    }

    // 2026-07-17：指定具体某首BGM曲子播放，跳过曲层轮播——给"这个房间进去必须放
    // 这首歌"这类地图作者想精确控制的场景用。实际播放由
    // MapBGMController.PlaySpecificClip(clip, startTimeSeconds) 执行。
    private static LogicSentenceTemplate BuildActionPlayBGMTrackFromStart()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_play_bgm_track_from_start",
            displayName = "播放指定BGM曲子（从头开始）",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "播放BGM曲子 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "clip" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "（从头开始）" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "clip",
                    displayName = "曲子",
                    valueType = LogicSlotValueType.AudioClip,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.AssetReference }
                }
            }
        };
    }

    private static LogicSentenceTemplate BuildActionPlayBGMTrackAtTime()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_play_bgm_track_at_time",
            displayName = "播放指定BGM曲子（从指定时间点）",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "播放BGM曲子 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "clip" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 从 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "minutes" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 分 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "seconds" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 秒开始" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "clip",
                    displayName = "曲子",
                    valueType = LogicSlotValueType.AudioClip,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.AssetReference }
                },
                BuildIntSlot("minutes", "分"),
                BuildIntSlot("seconds", "秒")
            }
        };
    }

    // 2026-07-17：地图BGM战斗/探索切换——配合 cond_any_enemy_sees_player 条件句型，
    // 触发器包里写"任意敌人看见玩家为真→切换BGM曲层为战斗"这类规则。实际播放由
    // MapBGMController.SetLayer(layerKey) 执行，这里只负责把句型参数转成对那个
    // 方法的调用（见 TriggerActionExecutor.cs 的 act_set_bgm_layer 分支）。
    private static LogicSentenceTemplate BuildActionSetBGMLayer()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_bgm_layer",
            displayName = "切换地图BGM曲层",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "切换地图BGM曲层为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "layer" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "layer",
                    displayName = "曲层",
                    valueType = LogicSlotValueType.Enum,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.Constant },
                    enumOptions = new List<LogicSentenceTemplate.EnumOption>
                    {
                        new LogicSentenceTemplate.EnumOption("explore", "探索"),
                        new LogicSentenceTemplate.EnumOption("combat",  "战斗"),
                    }
                }
            }
        };
    }

    private static LogicSentenceTemplate BuildActionTriggerAutoSave()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_trigger_autosave",
            displayName = "触发自动保存",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "触发自动保存" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildEventItemGained()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_item_gained",
            displayName = "获得物品",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Time },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "获得了 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "itemId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("itemId", "物品")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventItemLost()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_item_lost",
            displayName = "失去物品",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Time },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "失去了 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "itemId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("itemId", "物品")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventItemUsed()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_item_used",
            displayName = "使用物品",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Time },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "使用了 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "itemId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("itemId", "物品")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionAddItemToBag()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_add_item_to_bag",
            displayName = "增加物品到背包",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "增加 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "itemId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "，" },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "amount" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 个进入背包" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("itemId", "物品"),
                BuildIntSlot("amount", "数量")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionRemoveItemFromBag()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_remove_item_from_bag",
            displayName = "扣除物品从背包",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "扣除 " },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "itemId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "，" },
                new LogicSentenceTemplate.TextToken { isSlot = true, slotId = "amount" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 个从背包" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("itemId", "物品"),
                BuildIntSlot("amount", "数量")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionPauseGame()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_pause_game",
            displayName = "暂停游戏",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "暂停游戏" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildActionResumeGame()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_resume_game",
            displayName = "恢复游戏",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "恢复游戏" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildStringSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.String,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.Variable
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildItemSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Item,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.AssetReference
            }
        };
    }

    private static LogicSentenceTemplate BuildActionAddCurrency()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_add_currency",
            displayName = "增加货币",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "增加 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "currencyId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "amount" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 个" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildCurrencySlot("currencyId", "货币"),
                BuildIntSlot("amount", "数量")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionPlaySE()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_play_se",
            displayName = "播放SE音效",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "播放SE " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "seType" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "seType",
                    displayName = "音效类型",
                    valueType = LogicSlotValueType.Enum,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.Constant },
                    enumOptions = new List<LogicSentenceTemplate.EnumOption>
                    {
                        new LogicSentenceTemplate.EnumOption("Confirm",  "确定"),
                        new LogicSentenceTemplate.EnumOption("Cancel",   "取消"),
                        new LogicSentenceTemplate.EnumOption("Forbidden","禁止"),
                        new LogicSentenceTemplate.EnumOption("Switch",   "切换"),
                        new LogicSentenceTemplate.EnumOption("Open",     "打开"),
                        new LogicSentenceTemplate.EnumOption("Close",    "关闭"),
                        new LogicSentenceTemplate.EnumOption("Pickup",   "拾取"),
                        new LogicSentenceTemplate.EnumOption("Equip",    "装备"),
                        new LogicSentenceTemplate.EnumOption("Notice",   "通知"),
                        new LogicSentenceTemplate.EnumOption("Error",    "错误"),
                        new LogicSentenceTemplate.EnumOption("Revive",   "复活"),
                        new LogicSentenceTemplate.EnumOption("Tidy",     "整理"),
                    }
                }
            }
        };
    }

    private static LogicSentenceTemplate BuildActionPlayCustomSE()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_play_custom_se",
            displayName = "播放自定义SE音效",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "播放自定义SE " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "clip" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 音量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "volume" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 音调 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "pitch" },
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "clip",
                    displayName = "音频片段",
                    valueType = LogicSlotValueType.AudioClip,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.AssetReference }
                },
                BuildFloatSlot("volume", "音量（0~1）", 1f),
                BuildFloatSlot("pitch",  "音调（默认1）", 1f),
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildFloatSlot(string slotId, string displayName, float defaultValue = 0f)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Float,
            required = false,
            defaultFloatValue = defaultValue,
            allowedSources = new[] { LogicValueSourceType.Constant }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildCurrencySlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Currency,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.AssetReference
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildIntSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Int,
            required = true,
            allowedSources = new[]
            {
                LogicValueSourceType.Constant,
                LogicValueSourceType.Variable
            }
        };
    }
}
