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
            BuildActionPlayBGMTrackAtTime(),

            // 任务标记——NPC对话变体条件、剧情/章节推进都走这一套
            BuildConditionHasQuestFlag(),
            BuildActionSetQuestFlag(),

            // 台词字幕——触发器直接播一句台本句子，屏幕血条上方居中显示+读语音，
            // 不打开完整的NPC对话窗口(没有选项，纯展示)。
            BuildActionShowDialogueLine(),

            // 任务目标推进——击杀数/探索点数这类累加型计数器，物品数量判定(丢失会
            // 自动回退，不用额外维护)，到达指定场景位置。
            BuildConditionCounterAtLeast(),
            BuildActionIncrementCounter(),
            BuildConditionHasItemCountAtLeast(),
            BuildConditionPlayerNearSceneObject(),
            BuildConditionPlayerInRegion(),

            // 区域出入事件——动机本身带"区域"槽位，直接选具体是哪个区域；槽位留空
            // 则任意区域出入都算(TriggerPackageRuntime.HasMotive 里做的过滤)。
            BuildEventPlayerEnterRegion(),
            BuildEventPlayerLeaveRegion(),

            // 自定义变量赋值——按类型分开4个，"目标变量"槽位限死只能来源=变量(不给
            // 常量选项，写入目标本来就该是个变量引用)，"值"槽位是正常的同类型槽，
            // 支持常量/表达式/引用别的变量，"变量2=变量1+变量3"这种嵌套天然就通。
            BuildActionSetVariableBool(),
            BuildActionSetVariableInt(),
            BuildActionSetVariableFloat(),
            BuildActionSetVariableString(),

            // 自定义变量做条件——跟"设置变量"对称的读那一半。任务目标/触发器条件
            // 直接拿变量当前值判定，不用先转存成计数器/任务标记才能用。
            BuildConditionVariableBoolEquals(),
            BuildConditionVariableIntCompare(),
            BuildConditionVariableFloatCompare(),
            BuildConditionVariableStringEquals(),

            // 商店——触发器直接开店，以及"这家店这件货还有没有"、"完成了一笔交易"。
            BuildActionOpenShop(),
            BuildEventShopPurchaseCompleted(),
            BuildConditionShopHasStock(),

            // 击杀数达到至少(按具体单位定义资产判定，不是字符串拼的counterId)——
            // 任务编辑器"讨伐目标"快捷模板用这个，unit槽位存的是真正的UnitDefinition
            // 资产引用，不是一串手打的字符串，改单位名字/在Project窗口定位单位都不会
            // 断。内部照样读QuestRuntime已经在维护的kill_unit_{unitId}计数器，不是
            // 另开一套跟踪逻辑。
            BuildConditionUnitKillCountAtLeast(),
            BuildConditionHasTalkedToNpc(),

            // 交付——跟"讨伐/收集"同一挂档次的第三种结构化任务目标快捷模板：把指定
            // 数量的物品交给指定NPC，确认交付那一刻真的从背包扣掉物品(不是"拥有"就算，
            // 是"给出去"才算)，靠NPCDialogueWindowController弹出的专属交付窗口触发，
            // 不走通用"打开对话就算"的cond_has_talked_to_npc那一套。
            BuildConditionHasDeliveredItemToNpc(),

            // 任务前置——主线/支线之间串前置("上一条主线已完成才解锁下一条")之前只能靠
            // "拥有任务标记为"+手动记住"任务的flag名就是questId"这个隐藏约定，不直观也
            // 容易记错。这条直接给一个任务选择器+已接取/已完成/未接取三态，不用绕flag。
            // 没有"失败"这个状态——任务是基地发放的，局内失败不等于任务本身失败。
            BuildConditionQuestStateIs(),

            // 状态层数/剩余时间——跟已有的"拥有/没有状态"(布尔判定)互补，Boss层数
            // 攒到多少、DEBUFF还剩多久这类更细的判定。
            BuildConditionUnitStatusStackCountCompare(),
            BuildConditionUnitStatusRemainingDurationCompare(),

            // 装备——EquipmentRuntime 明确是玩家专属的单例，这几个都不带"单位"槽位。
            BuildActionEquipItem(),
            BuildActionUnequipItem(),
            BuildActionCycleActiveWeapon(),
            BuildConditionHasEquippedSlot(),
            BuildEventItemEquipped(),
            BuildEventItemUnequipped(),

            // 货币——act_add_currency 只有"加"这一个方向，补上"判断余额"和"扣款"。
            BuildConditionCurrencyAmountCompare(),
            BuildActionSpendCurrency(),

            // 音频——PlayLoop 已经在用(濒死脉冲)，StopLoop 之前没有对应句型。
            BuildActionStopLoopingSE(),

            // 存档/章节
            BuildConditionSaveSlotExists(),
            BuildConditionCurrentChapterIs(),

            // 本地化
            BuildActionSetLanguage(),
            BuildActionSetVoiceLanguage(),

            // 镜头
            BuildActionCameraShake(),

            // 天气——只做开关(用已配置好的这张地图天气)，不做类型切换：粒子范围/
            // 密度/下落速度那套是照具体资产手调的，运行时切类型风险太高，不在这次
            // 范围内。
            BuildActionSetWeatherActive(),

            // RAID 入口：把某个入口（旧工厂昼 / 夜、学校昼 / 夜、黄金乡昼 / 夜、废病院昼 / 夜、塔）设为可进入 / 不可进入
            BuildActionSetRaidEntry(),

            // 战术研修：随主线让一批科技树节点出现在研修窗口里
            BuildActionRevealTechGroup()
        };
    }

    // 2026-07-27：触发器演出用的轻量台词展示——跟 NPCDialogueWindowController 那种带
    // 选项的完整交互窗口是两回事，这个只是"念一句台本句子"，给地图触发器的一次性
    // 演出用(比如踏入某处自动播一句旁白)。实际显示由 DialogueSubtitleHUD.ShowLine 执行。
    private static LogicSentenceTemplate BuildActionShowDialogueLine()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_show_dialogue_line",
            displayName = "显示台词字幕",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "显示台词 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "line" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                // 句子库+句子编号是同一个槽——选完句子库，编号就是这个库里现成的
                // 下拉菜单，不用手打，见 LogicDialogueLibrarySlotHandler。
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "line",
                    displayName = "台词",
                    valueType = LogicSlotValueType.DialogueLibrary,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.AssetReference }
                }
            }
        };
    }

    // 2026-07-27：任务标记（quest flag）判定/设置——NPC对话按不同任务状态显示不同
    // 对话变体、对话选项推进剧情，跟地图触发器共用同一套 PlayerSaveData 标记存储
    // (SaveManager.Player.HasQuestFlag/SetQuestFlag)，不是给对话另开一条平行小路。
    private static LogicSentenceTemplate BuildConditionHasQuestFlag()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_has_quest_flag",
            displayName = "拥有任务标记为",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "任务标记 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "flag" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 已达成为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("flag", "标记Key"),
                BuildBoolSlot("expected", "是否成立")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetQuestFlag()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_quest_flag",
            displayName = "设置任务标记",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置任务标记 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "flag" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 为已达成" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("flag", "标记Key")
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildBoolSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Bool,
            required = true,
            allowedSources = new[] { LogicValueSourceType.Constant }
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

    // 2026-07-29：任务系统四类进度推进方式——对话（复用现成的flag）之外的三类，
    // 之前完全没有对应的条件/动作句型，任务目标写了也判定不出来。

    private static LogicSentenceTemplate BuildConditionCounterAtLeast()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_counter_at_least",
            displayName = "计数器达到至少",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "计数器 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "counterId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 达到至少 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "count" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("counterId", "计数器Key"),
                BuildIntSlot("count", "至少数量")
            }
        };
    }

    // 击杀类任务目标用这个手动加，不过大部分击杀计数已经由 QuestRuntime 监听
    // UnitDeathController.OnAnyEnemyDied 自动记账(kill_any / kill_unit_xxx /
    // kill_identity_xxx 三个粒度)，这条主要给"探索点数"之类非击杀的累加需求用。
    private static LogicSentenceTemplate BuildActionIncrementCounter()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_increment_counter",
            displayName = "增加计数器",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "计数器 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "counterId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 增加 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "amount" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("counterId", "计数器Key"),
                BuildIntSlot("amount", "增加数量")
            }
        };
    }

    // 判定用当前背包实际数量，不额外维护"收集进度"——物品丢了/用了，判定自然
    // 跟着变回不成立，不用专门处理"进度回退"。
    private static LogicSentenceTemplate BuildConditionHasItemCountAtLeast()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_has_item_count_at_least",
            displayName = "拥有物品数量达到至少",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "拥有物品 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "item" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 数量达到至少 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "count" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("item", "物品"),
                BuildIntSlot("count", "至少数量")
            }
        };
    }

    // target 不要求是真的单位——场景里随便放一个挂 SkyPrisonSceneUnitMarker 的
    // 空物体当"地点标记"就行，跟单位引用走同一套场景引用解析，不用另建一套
    // "地点标记"系统。
    private static LogicSentenceTemplate BuildConditionPlayerNearSceneObject()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_player_near_scene_object",
            displayName = "玩家靠近场景位置",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "玩家距离 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 不超过 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "radius" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildSceneUnitSlot("target", "目标位置（场景标记）"),
                BuildFloatSlot("radius", "半径", 2f)
            }
        };
    }

    // 用放置工具建出来的命名区域(SkyPrisonSceneRegionMarker + BoxCollider)判定玩家
    // 在不在里面——跟"到达指定地点"(cond_player_near_scene_object)的区别是这个用
    // 真实的方形范围而不是一个点+半径，WC3 WE式的"区域"任务/剧情条件用这个。
    private static LogicSentenceTemplate BuildConditionPlayerInRegion()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_player_in_region",
            displayName = "玩家在区域内",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "玩家在区域 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "region" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 内为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildAreaSlot("region", "区域"),
                BuildBoolSlot("expected", "是否成立")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventPlayerEnterRegion()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_player_enter_region",
            displayName = "玩家进入区域",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "玩家进入区域 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "region" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildAreaSlot("region", "区域(留空=任意区域)", required: false)
            }
        };
    }

    private static LogicSentenceTemplate BuildEventPlayerLeaveRegion()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_player_leave_region",
            displayName = "玩家离开区域",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "玩家离开区域 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "region" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildAreaSlot("region", "区域(留空=任意区域)", required: false)
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildAreaSlot(string slotId, string displayName, bool required = true)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Area,
            required = required,
            allowedSources = new[] { LogicValueSourceType.SceneReference }
        };
    }

    // 跟 AILogicSentenceTemplateLibraryCore/Combat 里的同名 BuildUnitSlot 逻辑
    // 一样，但那两处是各自文件的private方法，跨文件访问不到——按这个代码库已有
    // 的惯例(TriggerConditionEvaluator/TriggerActionExecutor里同名小helper也是
    // 故意各自维护一份)，这里本地建一份，不为了复用几行代码建跨类耦合。
    private static LogicSentenceTemplate.SlotDefinition BuildSceneUnitSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Unit,
            required = true,
            allowedSources = new[] { LogicValueSourceType.SceneReference }
        };
    }

    private static LogicSentenceTemplate BuildActionSetVariableBool()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_variable_bool",
            displayName = "设置变量(布尔)",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置变量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " = " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "目标变量", LogicSlotValueType.Bool),
                BuildBoolSlot("value", "值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetVariableInt()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_variable_int",
            displayName = "设置变量(整数)",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置变量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " = " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "目标变量", LogicSlotValueType.Int),
                BuildIntSlot("value", "值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetVariableFloat()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_variable_float",
            displayName = "设置变量(浮点数)",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置变量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " = " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "目标变量", LogicSlotValueType.Float),
                BuildFloatSlot("value", "值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetVariableString()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_variable_string",
            displayName = "设置变量(字符串)",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置变量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " = " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "目标变量", LogicSlotValueType.String),
                BuildStringSlot("value", "值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionVariableBoolEquals()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_variable_bool_equals",
            displayName = "变量(布尔)等于",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 等于 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "变量", LogicSlotValueType.Bool),
                BuildBoolSlot("expected", "期望值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionVariableIntCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_variable_int_compare",
            displayName = "变量(整数)比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "变量", LogicSlotValueType.Int),
                BuildLocalComparisonSlot("op", "比较符"),
                BuildIntSlot("value", "值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionVariableFloatCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_variable_float_compare",
            displayName = "变量(浮点数)比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "变量", LogicSlotValueType.Float),
                BuildLocalComparisonSlot("op", "比较符"),
                BuildFloatSlot("value", "值")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionVariableStringEquals()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_variable_string_equals",
            displayName = "变量(字符串)等于",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "variable" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 等于 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildVariableTargetSlot("variable", "变量", LogicSlotValueType.String),
                BuildStringSlot("value", "值")
            }
        };
    }

    // =========================
    // 商店
    // =========================

    private static LogicSentenceTemplate BuildActionOpenShop()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_open_shop",
            displayName = "打开商店",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "打开商店 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "shop" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildShopSlot("shop", "商店")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventShopPurchaseCompleted()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_shop_purchase_completed",
            displayName = "商店完成一笔交易",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "商店完成一笔交易(购买或出售)" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildConditionShopHasStock()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_shop_has_stock",
            displayName = "商店还有库存",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "shop" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 里 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "item" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 还有库存" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildShopSlot("shop", "商店"),
                BuildItemSlot("item", "物品")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitKillCountAtLeast()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_kill_count_at_least",
            displayName = "击杀单位数量达到至少",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "击杀 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 数量达到至少 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "count" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitAssetSlot("unit", "单位"),
                BuildIntSlot("count", "至少数量")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionHasTalkedToNpc()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_has_talked_to_npc",
            displayName = "已经与NPC对话过",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "已经与 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "unit" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 对话过" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildUnitAssetSlot("unit", "NPC")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionHasDeliveredItemToNpc()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_has_delivered_item_to_npc",
            displayName = "已经交付物品给NPC",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "已经交付 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "item" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 数量达到至少 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "count" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 给 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "npc" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("item", "物品"),
                BuildIntSlot("count", "至少数量"),
                BuildUnitAssetSlot("npc", "交付对象")
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildUnitAssetSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.UnitAsset,
            required = true,
            allowedSources = new[] { LogicValueSourceType.AssetReference }
        };
    }

    private static LogicSentenceTemplate BuildConditionQuestStateIs()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_quest_state_is",
            displayName = "任务状态是",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "任务 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "quest" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 状态是 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "state" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildQuestSlot("quest", "任务"),
                new LogicSentenceTemplate.SlotDefinition
                {
                    slotId = "state",
                    displayName = "状态",
                    valueType = LogicSlotValueType.Enum,
                    required = true,
                    allowedSources = new[] { LogicValueSourceType.Constant },
                    enumOptions = new List<LogicSentenceTemplate.EnumOption>
                    {
                        new LogicSentenceTemplate.EnumOption("NotStarted", "未接取"),
                        new LogicSentenceTemplate.EnumOption("Active", "已接取(进行中)"),
                        new LogicSentenceTemplate.EnumOption("Completed", "已完成"),
                    }
                }
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildQuestSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Quest,
            required = true,
            allowedSources = new[] { LogicValueSourceType.AssetReference }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildShopSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Shop,
            required = true,
            allowedSources = new[] { LogicValueSourceType.AssetReference }
        };
    }

    // =========================
    // 状态层数 / 剩余时间
    // =========================

    private static LogicSentenceTemplate BuildConditionUnitStatusStackCountCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_status_stack_count_compare",
            displayName = "单位状态层数比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的状态 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "statusId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 层数 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildSceneUnitSlot("target", "目标单位"),
                BuildStringSlot("statusId", "状态Key"),
                BuildLocalComparisonSlot("op", "比较符"),
                BuildIntSlot("value", "层数")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionUnitStatusRemainingDurationCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_unit_status_remaining_duration_compare",
            displayName = "单位状态剩余时间比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 的状态 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "statusId" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 剩余时间(秒) " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildSceneUnitSlot("target", "目标单位"),
                BuildStringSlot("statusId", "状态Key"),
                BuildLocalComparisonSlot("op", "比较符"),
                BuildFloatSlot("value", "秒数")
            }
        };
    }

    // =========================
    // 装备——EquipmentRuntime 是玩家专属单例，这几个都不需要"单位"槽位。
    // =========================

    private static LogicSentenceTemplate BuildActionEquipItem()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_equip_item",
            displayName = "装备物品",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "装备物品 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "item" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildItemSlot("item", "物品(须已在背包中)")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionUnequipItem()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_unequip_item",
            displayName = "卸下装备",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "卸下装备槽位 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "slot" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildEquipmentSlotSlot("slot", "槽位")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionCycleActiveWeapon()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_cycle_active_weapon",
            displayName = "切换当前生效武器",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "切换当前生效武器(主/副)" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildConditionHasEquippedSlot()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_has_equipped_slot",
            displayName = "装备槽位已装备",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "装备槽位 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "slot" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 已装备 为=" },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "expected" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildEquipmentSlotSlot("slot", "槽位"),
                BuildBoolSlot("expected", "期望值")
            }
        };
    }

    private static LogicSentenceTemplate BuildEventItemEquipped()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_item_equipped",
            displayName = "玩家装备了物品",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "玩家装备了物品(任意槽位)" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate BuildEventItemUnequipped()
    {
        return new LogicSentenceTemplate
        {
            templateId = "event_item_unequipped",
            displayName = "玩家卸下了物品",
            category = LogicSentenceCategory.Motive,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "玩家卸下了物品(任意槽位)" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildEquipmentSlotSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Enum,
            required = true,
            allowedSources = new[] { LogicValueSourceType.Constant },
            enumOptions = new List<LogicSentenceTemplate.EnumOption>
            {
                new LogicSentenceTemplate.EnumOption("Weapon", "武器"),
                new LogicSentenceTemplate.EnumOption("WeaponSecondary", "副武器"),
                new LogicSentenceTemplate.EnumOption("Head", "头部"),
                new LogicSentenceTemplate.EnumOption("UpperBody", "上装"),
                new LogicSentenceTemplate.EnumOption("LowerBody", "下装"),
                new LogicSentenceTemplate.EnumOption("Hands", "手部"),
                new LogicSentenceTemplate.EnumOption("Shoes", "鞋子"),
            }
        };
    }

    // =========================
    // 货币——act_add_currency 只有"加"这一个方向。
    // =========================

    private static LogicSentenceTemplate BuildConditionCurrencyAmountCompare()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_currency_amount_compare",
            displayName = "货币数量比较",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI, LogicTemplateTag.Math },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "currency" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 当前数量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "op" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "value" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildCurrencySlot("currency", "货币"),
                BuildLocalComparisonSlot("op", "比较符"),
                BuildIntSlot("value", "数值")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSpendCurrency()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_spend_currency",
            displayName = "扣除货币",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "扣除货币 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "currency" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 数量 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "amount" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildCurrencySlot("currency", "货币"),
                BuildIntSlot("amount", "数量")
            }
        };
    }

    // =========================
    // 音频
    // =========================

    private static LogicSentenceTemplate BuildActionStopLoopingSE()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_stop_looping_se",
            displayName = "停止循环SE音效",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "停止循环SE音效(比如濒死脉冲)" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>()
        };
    }

    // =========================
    // 存档 / 章节
    // =========================

    private static LogicSentenceTemplate BuildConditionSaveSlotExists()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_save_slot_exists",
            displayName = "存档槽位存在",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "存档槽位 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "slot" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 存在" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildIntSlot("slot", "槽位编号")
            }
        };
    }

    private static LogicSentenceTemplate BuildConditionCurrentChapterIs()
    {
        return new LogicSentenceTemplate
        {
            templateId = "cond_current_chapter_is",
            displayName = "当前章节是",
            category = LogicSentenceCategory.Condition,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "当前章节是 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "chapterId" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("chapterId", "章节ID")
            }
        };
    }

    // =========================
    // 本地化
    // =========================

    private static LogicSentenceTemplate BuildActionSetLanguage()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_language",
            displayName = "设置文字语言",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置文字语言为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "languageCode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("languageCode", "语言代码(如zh-CN)")
            }
        };
    }

    private static LogicSentenceTemplate BuildActionSetVoiceLanguage()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_voice_language",
            displayName = "设置语音语言",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置语音语言为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "languageCode" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildStringSlot("languageCode", "语言代码(如zh-CN)")
            }
        };
    }

    // =========================
    // 镜头
    // =========================

    private static LogicSentenceTemplate BuildActionCameraShake()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_camera_shake",
            displayName = "镜头震动",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "镜头震动 强度 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "intensity" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 持续 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "duration" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 秒" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildFloatSlot("intensity", "强度", 0.3f),
                BuildFloatSlot("duration", "持续秒数", 0.3f)
            }
        };
    }

    // =========================
    // 天气——只做开关，见 GetTemplates() 里的说明。
    // =========================

    private static LogicSentenceTemplate BuildActionSetRaidEntry()
    {
        var targets = new List<LogicSentenceTemplate.EnumOption>();
        foreach (var t in SkyPrisonRaidEntry.Targets) targets.Add(new LogicSentenceTemplate.EnumOption(t[0], t[1]));
        return new LogicSentenceTemplate
        {
            templateId = "act_set_raid_entry",
            displayName = "设置RAID入口状态",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "设置 RAID入口 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "target" },
                new LogicSentenceTemplate.TextToken { isSlot = false, text = " 为 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "state" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildLocalEnumSlot("target", "入口", targets),
                BuildLocalEnumSlot("state", "状态", new List<LogicSentenceTemplate.EnumOption>
                {
                    new LogicSentenceTemplate.EnumOption("enabled",  "可进入"),
                    new LogicSentenceTemplate.EnumOption("disabled", "不可进入"),
                })
            }
        };
    }

    private static LogicSentenceTemplate BuildActionRevealTechGroup()
    {
        var groups = new List<LogicSentenceTemplate.EnumOption>();
        foreach (var g in TechTreeReveal.Groups) groups.Add(new LogicSentenceTemplate.EnumOption(g[0], g[1]));
        return new LogicSentenceTemplate
        {
            templateId = "act_reveal_tech_group",
            displayName = "显现研修节点组",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "让战术研修显现节点组 " },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "group" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildLocalEnumSlot("group", "节点组", groups)
            }
        };
    }

    private static LogicSentenceTemplate.SlotDefinition BuildLocalEnumSlot(string slotId, string displayName, List<LogicSentenceTemplate.EnumOption> options)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.Enum,
            required = true,
            allowedSources = new[] { LogicValueSourceType.Constant },
            enumOptions = options
        };
    }

    private static LogicSentenceTemplate BuildActionSetWeatherActive()
    {
        return new LogicSentenceTemplate
        {
            templateId = "act_set_weather_active",
            displayName = "设置天气开关",
            category = LogicSentenceCategory.Action,
            nodeKind = LogicNodeKind.Leaf,
            tags = new List<LogicTemplateTag> { LogicTemplateTag.AI },
            tokens = new List<LogicSentenceTemplate.TextToken>
            {
                new LogicSentenceTemplate.TextToken { isSlot = false, text = "当前地图天气开启 为=" },
                new LogicSentenceTemplate.TextToken { isSlot = true,  slotId = "active" }
            },
            slots = new List<LogicSentenceTemplate.SlotDefinition>
            {
                BuildBoolSlot("active", "开启")
            }
        };
    }

    // 跟 AILogicSentenceTemplateLibraryCore 里的同名 BuildComparisonSlot 逻辑一样，
    // 但那边是private，跨文件访问不到，按老规矩本地建一份。
    private static LogicSentenceTemplate.SlotDefinition BuildLocalComparisonSlot(string slotId, string displayName)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = LogicSlotValueType.ComparisonOperator,
            required = true,
            allowedSources = new[] { LogicValueSourceType.Constant }
        };
    }

    // 目标变量槽位——来源固定只能是"变量"(不给常量/表达式选项)，因为这个槽位
    // 表达的是"要写进哪个变量"，不是一个可以求值的量。复用现成的
    // LogicXxxSlotHandler(Bool/Int/Float/String)，它们的"来源=变量"分支已经在
    // 这次改成 LogicVariablePickerGUI 的类型过滤下拉框，天然只会列出同类型变量。
    private static LogicSentenceTemplate.SlotDefinition BuildVariableTargetSlot(string slotId, string displayName, LogicSlotValueType type)
    {
        return new LogicSentenceTemplate.SlotDefinition
        {
            slotId = slotId,
            displayName = displayName,
            valueType = type,
            required = true,
            allowedSources = new[] { LogicValueSourceType.Variable }
        };
    }
}
