using UnityEngine;

/// <summary>
/// 目标显示文字的统一解析——编辑器(SkyPrisonQuestPage)预览和游戏内任务日志都要用
/// 同一套规则：如果目标是"讨伐"/"收集"这种结构化目标(唯一一条完成条件是
/// cond_unit_kill_count_at_least/cond_has_item_count_at_least)，标题按{0}/{1}占位符
/// 模板现场拼出来，名字/数量永远读条件里的真实资产引用/当前数值，不是存好的死文本；
/// 自定义目标(没有识别出这种结构)才直接显示原始文本。放在运行时程序集(不在Editor
/// 文件夹下)，这样游戏内UI也能直接调用，不用另外复制一份。
/// </summary>
public static class QuestObjectiveTextResolver
{
    public readonly struct Progress
    {
        public readonly bool Resolved;
        public readonly int Current;
        public readonly int Target;
        public Progress(bool resolved, int current, int target)
        {
            Resolved = resolved;
            Current = current;
            Target = target;
        }
    }

    /// <summary>讨伐/收集这类结构化目标解析出来的真实资产引用——悬浮查询卡（鼠标停在
    /// 名字上弹出的头像+简讯小窗）用这个拿图标/资产，不是只拿一个名字字符串。</summary>
    public readonly struct AutoInfo
    {
        public readonly bool Resolved;
        public readonly string Name;
        public readonly int Count;
        public readonly UnitDefinition Unit;
        public readonly ItemDefinition Item;
        public readonly Sprite Icon;
        /// <summary>悬浮查询卡的简介文——人物读GetLocalizedDescription，物品同理。
        /// 用户明确要求"至少要有名字/头像/介绍文"，不是只显示进度数字。</summary>
        public readonly string Description;
        /// <summary>"交付"这类同时涉及两个真实资产(物品+收件NPC)的目标才会用到——
        /// Name/Icon/Description永远是主资产(物品)，这个字段是第二个占位符({2})
        /// 要填的收件人名字，纯文本，不额外挂悬浮卡/链接(范围先控制住，物品那边的
        /// 卡片已经够用)。</summary>
        public readonly string SecondaryName;

        public AutoInfo(bool resolved, string name, int count, UnitDefinition unit, ItemDefinition item, Sprite icon, string description = null, string secondaryName = null)
        {
            Resolved = resolved;
            Name = name;
            Count = count;
            Unit = unit;
            Item = item;
            Icon = icon;
            Description = description;
            SecondaryName = secondaryName;
        }

        public static readonly AutoInfo None = new AutoInfo(false, null, 0, null, null, null, null, null);
    }

    /// <summary>counterId 前缀约定——"找发任务人签到/确认进度/交付"这类目标
    /// (NPCDialogueWindowController.CheckInWithGiverForQuest 点"有事情找你"时推进)
    /// 統一用这个前缀起counterId，跟其它用途的cond_counter_at_least区分开，不用
    /// 另外加一个目标类型字段。</summary>
    private const string GiverCheckinCounterPrefix = "quest_giver_checkin_";

    public static string ResolveTitle(QuestObjective objective, UnitDefinition giverNpc = null)
    {
        string template = GetTemplate(objective);

        // 多条件节点(比如"讨伐A×2 且 讨伐B×1")没有唯一的主资产，ResolveAutoInfo
        // 会返回None。之前这里直接退回template原文，而结构化目标的template里满是
        // {0}/{1}占位符——结果玩家在任务日志上看到的就是字面量"收集{0} ×{1}"。
        // 现在改成：作者写了不带占位符的概括标题(比如"清剿据点")就用它；没写、
        // 或者写的还是带占位符的单目标模板，就按每条条件现场拼出完整文字。
        if (IsMultiCondition(objective))
        {
            if (!string.IsNullOrWhiteSpace(template) && !ContainsPlaceholder(template))
                return template;
            return ComposeMultiConditionText(objective, giverNpc, withLinks: false);
        }

        if (string.IsNullOrWhiteSpace(template))
            return "<未命名目标>";

        AutoInfo info = ResolveAutoInfo(objective, giverNpc);
        if (!info.Resolved)
            return template;

        try { return string.Format(template, info.Name, info.Count, info.SecondaryName); }
        catch { return template; } // 模板里的占位符被手滑删掉/写错格式时别崩，退回原文本
    }

    private static bool IsMultiCondition(QuestObjective objective)
        => objective != null && objective.completionConditions != null && objective.completionConditions.Count > 1;

    /// <summary>模板里还留着{0}/{1}/{2}这类占位符——说明它是给"整个目标正好对应一种
    /// 结构化类型"设计的单目标模板，多条件节点下没有主资产可填，不能原样显示。</summary>
    private static bool ContainsPlaceholder(string template)
        => template.Contains("{0}") || template.Contains("{1}") || template.Contains("{2}");

    /// <summary>多条件节点的兜底标题——把每条条件各自的完整文字用顿号连起来，
    /// 比如"讨伐 变异犬 ×2、收集 压缩饼干 ×3"。绝不泄漏占位符。</summary>
    private static string ComposeMultiConditionText(QuestObjective objective, UnitDefinition giverNpc, bool withLinks)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (LogicSentenceInstance c in objective.completionConditions)
        {
            ConditionLine line = ResolveConditionLine(c, null, giverNpc, withLinks);
            if (!string.IsNullOrWhiteSpace(line.Text))
                parts.Add(line.Text);
        }
        return parts.Count > 0 ? string.Join("、", parts) : "<未命名目标>";
    }

    /// <summary>跟 ResolveTitle 结果一致，但名字部分包一层 TMP 链接标签
    /// (link="questobj")，配合 TMP_TextUtilities.FindIntersectingLink 检测鼠标悬停，
    /// 弹出贾维斯式的轻量查询卡。非结构化目标(没有真实资产引用可查)原样返回，不加标签。</summary>
    public static string ResolveTitleWithLink(QuestObjective objective, out AutoInfo info, UnitDefinition giverNpc = null)
    {
        string template = GetTemplate(objective);
        info = ResolveAutoInfo(objective, giverNpc);

        // 跟ResolveTitle同一套多条件处理——这条路径额外把名字包上TMP链接标签。
        if (IsMultiCondition(objective))
        {
            if (!string.IsNullOrWhiteSpace(template) && !ContainsPlaceholder(template))
                return template;
            return ComposeMultiConditionText(objective, giverNpc, withLinks: true);
        }

        if (string.IsNullOrWhiteSpace(template))
            return "<未命名目标>";
        if (!info.Resolved)
            return template;

        try { return string.Format(template, LinkWrap(info.Name), info.Count, info.SecondaryName); }
        catch { return template; }
    }

    /// <summary>目标模板文字(比如"击杀{0} x{1}"/"与{0}对话")——这个方法之前直接取
    /// description列表里第一条非空词条，完全没管当前语言，导致击杀/收集/对话三类
    /// 结构化目标不管切到日文/英文都还是显示中文(只要中文词条排在列表最前面且非空)。
    /// 改成跟quest.GetLocalizedTitle同一套走法，按当前语言查，查不到才退回随便一条
    /// 非空文本兜底。</summary>
    private static string GetTemplate(QuestObjective objective)
    {
        string fallback = null;
        foreach (LocalizedTextEntry t in objective.description)
        {
            if (!string.IsNullOrWhiteSpace(t.text)) { fallback = t.text; break; }
        }
        if (fallback == null) return null;

        return LocalizationRuntime.Instance != null
            ? LocalizationRuntime.Instance.GetText(objective.description, fallback)
            : fallback;
    }

    /// <summary>讨伐/收集这类结构化目标的当前进度(比如已杀3/5)。非结构化目标返回
    /// Resolved=false，调用方应该退回只显示 IsComplete() 的完成/未完成两态。
    /// questIdForBaseline传任务questId时，击杀/对话这两种计数器型进度会扣掉
    /// QuestRuntime.StartQuest()记的起点快照——跟TriggerConditionEvaluator/
    /// QuestDefinition.AreRequiredObjectivesComplete同一套道理，不然接任务之前
    /// (包括接任务这个动作本身)累计的次数会让进度条显示"一接就满"，跟真实的
    /// 完成判定对不上。收集类是看当前实际持有数量，不是累计计数器，不需要扣起点。</summary>
    public static Progress GetProgress(QuestObjective objective, string questIdForBaseline = null)
    {
        if (objective.completionConditions.Count != 1)
            return new Progress(false, 0, 0);

        LogicSentenceInstance c = objective.completionConditions[0];
        if (c == null) return new Progress(false, 0, 0);

        if (c.templateId == "cond_unit_kill_count_at_least")
        {
            UnitDefinition unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (unit != null && !string.IsNullOrWhiteSpace(unit.unitId) && SaveManager.Player != null)
            {
                string counterKey = $"kill_unit_{unit.unitId}";
                int current = SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey);
                return new Progress(true, Mathf.Max(0, current), target);
            }
        }
        else if (c.templateId == "cond_has_item_count_at_least")
        {
            ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
            if (item != null && inv != null)
            {
                int current = inv.CountItem(item);
                return new Progress(true, current, target);
            }
        }
        else if (c.templateId == "cond_has_talked_to_npc")
        {
            UnitDefinition unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
            if (unit != null && !string.IsNullOrWhiteSpace(unit.unitId) && SaveManager.Player != null)
            {
                string counterKey = $"talked_npc_{unit.unitId}";
                int current = SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey);
                return new Progress(true, Mathf.Clamp(current, 0, 1), 1);
            }
        }
        else if (c.templateId == "cond_counter_at_least")
        {
            string counterKey = GetGiverCheckinCounterKey(c);
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (counterKey != null && SaveManager.Player != null)
            {
                int current = SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey);
                return new Progress(true, Mathf.Max(0, current), target);
            }
        }
        else if (c.templateId == "cond_has_delivered_item_to_npc")
        {
            string counterKey = GetDeliveredItemCounterKey(c);
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (counterKey != null && SaveManager.Player != null)
            {
                int current = SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey);
                return new Progress(true, Mathf.Max(0, current), target);
            }
        }

        return new Progress(false, 0, 0);
    }

    /// <summary>跟TriggerConditionEvaluator里的同名key推导逻辑保持一致(两边各自留
    /// 一份，是这个文件一贯的做法——kill_unit_{unitId}/talked_npc_{unitId}也是两边
    /// 各写各的，不共享私有方法)：物品+交付对象这两个真实资产引用没配全就不算
    /// 计数器型条件。</summary>
    private static string GetDeliveredItemCounterKey(LogicSentenceInstance c)
    {
        ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
        UnitDefinition npc = c.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
        if (item == null) return null; // itemId是int(有默认值)，不是string，没有"没填"这个状态，只判空引用
        if (npc == null || string.IsNullOrWhiteSpace(npc.unitId)) return null;
        return $"delivered_item_{item.itemId}_to_{npc.unitId}";
    }

    /// <summary>只认"quest_giver_checkin_"这个前缀开头的counterId——避免把以后可能
    /// 出现的、跟"找发任务人签到"完全无关的其它cond_counter_at_least用途也当成
    /// 这一种结构化目标现场瞎拼文字。</summary>
    private static string GetGiverCheckinCounterKey(LogicSentenceInstance c)
    {
        string counterId = c.GetAssignment("counterId")?.value?.EvaluateString();
        return !string.IsNullOrWhiteSpace(counterId) && counterId.StartsWith(GiverCheckinCounterPrefix)
            ? counterId
            : null;
    }

    private static int GetBaseline(string questIdForBaseline, string counterKey)
    {
        if (questIdForBaseline == null || SaveManager.Player == null) return 0;
        return SaveManager.Player.GetCounterBaseline(questIdForBaseline, counterKey);
    }

    public static AutoInfo ResolveAutoInfo(QuestObjective objective, UnitDefinition giverNpc = null)
    {
        if (objective.completionConditions.Count != 1)
            return AutoInfo.None;

        LogicSentenceInstance c = objective.completionConditions[0];
        if (c == null) return AutoInfo.None;

        if (c.templateId == "cond_unit_kill_count_at_least")
        {
            UnitDefinition unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
            int count = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (unit != null)
                return new AutoInfo(true, GetUnitLabel(unit), count, unit, null, unit.icon, unit.GetLocalizedDescription());
        }
        else if (c.templateId == "cond_has_item_count_at_least")
        {
            ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
            int count = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (item != null)
            {
                // 统一走GetLocalizedDisplayName()，不要直接读displayName原始字段——
                // 跟GetUnitLabel()同一个坑，之前切日文/英文，"收集{0}"里的物品名字
                // 还是中文。
                string label = item.GetLocalizedDisplayName();
                return new AutoInfo(true, label, count, null, item, item.icon, item.GetLocalizedDescription());
            }
        }
        else if (c.templateId == "cond_has_talked_to_npc")
        {
            UnitDefinition unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
            if (unit != null)
                return new AutoInfo(true, GetUnitLabel(unit), 1, unit, null, unit.icon, unit.GetLocalizedDescription());
        }
        else if (c.templateId == "cond_counter_at_least" && giverNpc != null && GetGiverCheckinCounterKey(c) != null)
        {
            // "找发任务人签到"型条件自己不带unit槽位(只有counterId+count)，{0}要填的
            // 名字/头像/简介从调用方传进来的quest.giverNpc取，不是从条件本身解析——
            // 用户明确要求这类"与{0}对话"节点也要保持{0}占位符+真实资产引用的写法，
            // 不能为了区分先后顺序就把名字写死成静态文本。
            return new AutoInfo(true, GetUnitLabel(giverNpc), 1, giverNpc, null, giverNpc.icon, giverNpc.GetLocalizedDescription());
        }
        else if (c.templateId == "cond_has_delivered_item_to_npc")
        {
            ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
            UnitDefinition npc = c.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
            int count = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (item != null && npc != null)
            {
                // 主资产是物品(Name/Icon/Description都走物品的)，收件人只是第二个
                // 占位符({2})的纯文本，不额外挂悬浮卡——跟AutoInfo.SecondaryName的
                // 注释是同一个道理。
                string itemLabel = item.GetLocalizedDisplayName();
                return new AutoInfo(true, itemLabel, count, npc, item, item.icon, item.GetLocalizedDescription(), GetUnitLabel(npc));
            }
        }

        return AutoInfo.None;
    }

    private static string GetUnitLabel(UnitDefinition ud)
    {
        if (ud == null) return "";
        // 之前直接读ud.displayName，切日文/英文的话"击杀{0}"/"与{0}对话"里的名字
        // 部分还是中文——改成走GetLocalizedDisplayName()，跟头顶名字/对话标题同一套。
        string localized = ud.GetLocalizedDisplayName();
        return !string.IsNullOrWhiteSpace(localized) ? localized : ud.name;
    }

    /// <summary>一个目标要求"同时满足好几件事"(completionConditions有不止一条)时，
    /// "当前目标"详情块要把每一条都单独列出来，不能只靠目标本身唯一那一条description
    /// 模板(那套是给"整个目标正好对应一种结构化类型"设计的，一个目标绑死一条模板)。
    /// 这里直接按每条condition自己的templateId现场拼文字+进度，不需要额外的存储字段，
    /// 用的措辞跟GetTemplate/ResolveAutoInfo里那几条内置模板是同一套习惯。</summary>
    public readonly struct ConditionLine
    {
        public readonly string Text;
        public readonly Progress Progress;
        /// <summary>这条子条件指向的真实资产——悬浮查询卡用。单条件目标那边
        /// (ResolveTitleWithLink/_linkableTexts)已经有这个链路，多条件目标之前
        /// 漏了，导致"当前目标"详情块里拆出来的每一行悬停不了。</summary>
        public readonly AutoInfo Info;
        public ConditionLine(string text, Progress progress, AutoInfo info)
        {
            Text = text;
            Progress = progress;
            Info = info;
        }
    }

    /// <param name="withLinks">名字要不要包TMP链接标签。任务日志里逐行显示时要包
    /// (支持悬停查询卡)；被ComposeMultiConditionText拿去拼纯文本标题时不能包，
    /// 否则标签会跟着漏到不支持富文本的地方。</param>
    public static ConditionLine ResolveConditionLine(LogicSentenceInstance c, string questIdForBaseline, UnitDefinition giverNpc = null, bool withLinks = true)
    {
        if (c == null) return new ConditionLine("<未命名条件>", new Progress(false, 0, 0), AutoInfo.None);
        System.Func<string, string> Wrap = withLinks ? (System.Func<string, string>)LinkWrap : (s => s);

        if (c.templateId == "cond_unit_kill_count_at_least")
        {
            UnitDefinition unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (unit != null && !string.IsNullOrWhiteSpace(unit.unitId) && SaveManager.Player != null)
            {
                string counterKey = $"kill_unit_{unit.unitId}";
                int current = Mathf.Max(0, SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey));
                string label = GetUnitLabel(unit);
                var info = new AutoInfo(true, label, target, unit, null, unit.icon, unit.GetLocalizedDescription());
                return new ConditionLine($"讨伐 {Wrap(label)} ×{target}", new Progress(true, current, target), info);
            }
        }
        else if (c.templateId == "cond_has_item_count_at_least")
        {
            ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
            if (item != null && inv != null)
            {
                // 统一走GetLocalizedDisplayName()，不要直接读displayName原始字段——
                // 跟GetUnitLabel()同一个坑，之前切日文/英文，"收集{0}"里的物品名字
                // 还是中文。
                string label = item.GetLocalizedDisplayName();
                int current = inv.CountItem(item);
                var info = new AutoInfo(true, label, target, null, item, item.icon, item.GetLocalizedDescription());
                return new ConditionLine($"收集 {Wrap(label)} ×{target}", new Progress(true, current, target), info);
            }
        }
        else if (c.templateId == "cond_has_talked_to_npc")
        {
            UnitDefinition unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
            if (unit != null && !string.IsNullOrWhiteSpace(unit.unitId) && SaveManager.Player != null)
            {
                string counterKey = $"talked_npc_{unit.unitId}";
                int current = Mathf.Clamp(SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey), 0, 1);
                string label = GetUnitLabel(unit);
                var info = new AutoInfo(true, label, 1, unit, null, unit.icon, unit.GetLocalizedDescription());
                return new ConditionLine($"与 {Wrap(label)} 对话", new Progress(true, current, 1), info);
            }
        }
        else if (c.templateId == "cond_counter_at_least" && giverNpc != null)
        {
            string counterKey = GetGiverCheckinCounterKey(c);
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (counterKey != null)
            {
                int current = Mathf.Max(0, SaveManager.Player != null
                    ? SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey)
                    : 0);
                string label = GetUnitLabel(giverNpc);
                var info = new AutoInfo(true, label, target, giverNpc, null, giverNpc.icon, giverNpc.GetLocalizedDescription());
                return new ConditionLine($"与 {Wrap(label)} 对话", new Progress(true, current, target), info);
            }
        }
        else if (c.templateId == "cond_has_delivered_item_to_npc")
        {
            ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
            UnitDefinition npc = c.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
            int target = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (item != null && npc != null)
            {
                string counterKey = GetDeliveredItemCounterKey(c);
                int current = Mathf.Max(0, counterKey != null && SaveManager.Player != null
                    ? SaveManager.Player.GetCounter(counterKey) - GetBaseline(questIdForBaseline, counterKey)
                    : 0);
                string itemLabel = item.GetLocalizedDisplayName();
                string npcLabel = GetUnitLabel(npc);
                var info = new AutoInfo(true, itemLabel, target, npc, item, item.icon, item.GetLocalizedDescription(), npcLabel);
                return new ConditionLine($"交付 {Wrap(itemLabel)} ×{target} 给 {npcLabel}", new Progress(true, current, target), info);
            }
        }

        return new ConditionLine("<未命名条件>", new Progress(false, 0, 0), AutoInfo.None);
    }

    /// <summary>跟ResolveTitleWithLink同一个包法——TMP链接标签+冷绿色，配合
    /// TMP_TextUtilities.FindIntersectingLink检测悬停，弹出贾维斯式查询卡。</summary>
    private static string LinkWrap(string name) => $"<link=\"questobj\"><color=#6BEBAD>{name}</color></link>";

    /// <summary>整体聚合进度——目标要求同时满足几条里已经满足了几条，节点链上那个
    /// 小圆点的标签用这个显示"2/3"这种整体数字，不需要每条都摆出来(那是"当前目标"
    /// 详情块的活)。单条件目标直接退回GetProgress，行为不变。</summary>
    public static Progress GetAggregateProgress(QuestObjective objective, string questIdForBaseline = null)
    {
        if (objective.completionConditions.Count <= 1)
            return GetProgress(objective, questIdForBaseline);

        int satisfied = 0;
        foreach (LogicSentenceInstance c in objective.completionConditions)
            if (TriggerConditionEvaluator.Evaluate(c, false))
                satisfied++;
        return new Progress(true, satisfied, objective.completionConditions.Count);
    }
}
