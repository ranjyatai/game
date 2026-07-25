using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SkyPrisonSkillInspectorPanel
{
    private readonly SkyPrisonSkillPage page;
    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "动画", true },
        { "判定框", true },
        { "远程弹幕", false },
        { "蓄力", false },
        { "消耗", true },
        { "伤害", true },
        { "击退 & 硬直", false },
        { "帧数据", true },
        { "连段", false },
        { "特效 (VFX)", false },
        { "音效 (SE)", false },
        { "多语言", false },
    };

    public SkyPrisonSkillInspectorPanel(SkyPrisonSkillPage page) { this.page = page; }

    public void Draw()
    {
        SerializedObject   so    = page.SelectedSO;
        SkillDefinition    skill = page.SelectedSkill;
        if (so == null || skill == null) return;

        // 顶部标题行
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(string.IsNullOrWhiteSpace(skill.displayName) ? skill.name : skill.displayName,
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("在 Project 中定位", GUILayout.Height(22), GUILayout.Width(120)))
        { Selection.activeObject = skill; EditorGUIUtility.PingObject(skill); }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // ── 基础信息 ─────────────────────────────────────────────────────
        Section("基础信息", () =>
        {
            Row("技能 Key",   so.FindProperty("skillKey"));
            Row("显示名称", so.FindProperty("displayName"));
            Row("描述",     so.FindProperty("description"), multiline: true);
            Row("分类",     so.FindProperty("category"));
        });

        // ── 动画 ─────────────────────────────────────────────────────────
        Section("动画", () =>
        {
            Row("Spine 动画 Key",    so.FindProperty("spineAnimationKey"));
            Row("Spine 事件 Key",    so.FindProperty("spineEventKey"));
            EditorGUILayout.HelpBox(
                "在 Spine 编辑器中于出拳接触帧插入同名 Event，运行时收到该事件时触发 hitbox 判定。",
                MessageType.None);
        });

        // ── 判定框 ───────────────────────────────────────────────────────
        Section("判定框", () =>
        {
            SerializedProperty hitbox = so.FindProperty("hitbox");
            SerializedProperty shape  = hitbox.FindPropertyRelative("shape");
            Row("形状",   shape);
            Row("偏移",   hitbox.FindPropertyRelative("offset"));
            if (shape.enumValueIndex == (int)SkillHitboxShape.Circle)
                Row("半径", hitbox.FindPropertyRelative("radius"));
            else
                Row("尺寸", hitbox.FindPropertyRelative("size"));
            Row("Z 深度", hitbox.FindPropertyRelative("zDepth"));

            DrawHitboxPreview(skill);
        });

        // ── 远程弹幕 ─────────────────────────────────────────────────────
        Section("远程弹幕", () =>
        {
            SerializedProperty isProjectileProp = so.FindProperty("isProjectileSkill");
            Row("是远程弹幕技能", isProjectileProp);

            if (!isProjectileProp.boolValue)
            {
                EditorGUILayout.HelpBox("非弹幕技能，上面的判定框正常生效，下面这组参数不生效。", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                "勾选后 hit_start 不再打开上面的近战判定框，改成生成一发真的会飞的抛射物，" +
                "飞行中检测碰撞，命中/存活时间到了才消失。",
                MessageType.None);

            SerializedProperty projectile = so.FindProperty("projectile");
            Row("飞行速度(米/秒)", projectile.FindPropertyRelative("speed"));
            Row("最长存活时间(秒)", projectile.FindPropertyRelative("maxLifetimeSeconds"));
            Row("朝下偏转角度(度)", projectile.FindPropertyRelative("launchAngleDownDegrees"));
            EditorGUILayout.HelpBox("跟角色朝向镜像联动：面朝右时朝右下偏转，面朝左时朝左下偏转。", MessageType.None);
            Row("命中判定半径", projectile.FindPropertyRelative("hitRadius"));
            Row("命中后销毁", projectile.FindPropertyRelative("destroyOnHit"));
            Row("弹幕特效", projectile.FindPropertyRelative("projectileVfx"));
            Row("弹幕特效缩放", projectile.FindPropertyRelative("projectileVfxScale"));
            Row("命中特效", projectile.FindPropertyRelative("impactVfx"));
        });

        // ── 蓄力 ─────────────────────────────────────────────────────────
        Section("蓄力", () =>
        {
            SerializedProperty isChargeProp = so.FindProperty("isChargeSkill");
            Row("是蓄力技能", isChargeProp);

            if (!isChargeProp.boolValue)
            {
                EditorGUILayout.HelpBox("非蓄力技能，下面这组参数不生效。", MessageType.None);
                return;
            }

            SerializedProperty charge = so.FindProperty("charge");
            EditorGUILayout.LabelField("定格 & 释放", EditorStyles.boldLabel);
            Row("蓄力事件 Key",   charge.FindPropertyRelative("chargeHoldEventKey"));
            Row("释放播放倍速",   charge.FindPropertyRelative("releaseTimeScale"));
            EditorGUILayout.HelpBox(
                "在Spine编辑器里于蓄力定格帧插入同名Event，动画播到这一帧会自动冻结，" +
                "等玩家松开蓄力键才继续播完剩下的部分（比如刺出去）。",
                MessageType.None);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("蓄满伤害加成", EditorStyles.boldLabel);
            Row("蓄满所需秒数", charge.FindPropertyRelative("fullChargeHoldSeconds"));
            Row("蓄满伤害倍率", charge.FindPropertyRelative("fullChargeDamageMultiplier"));
            Row("蓄满提示 SE",  charge.FindPropertyRelative("fullChargeReachedSE"));
            EditorGUILayout.HelpBox("蓄力刚好达到蓄满秒数的那一刻播放一次，随机取一个片段。留空则不提示。", MessageType.None);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("持续LP消耗", EditorStyles.boldLabel);
            Row("每秒消耗LP", charge.FindPropertyRelative("holdLpCostPerSecond"));
            EditorGUILayout.HelpBox("蓄力定格期间按秒持续扣，不是按键那一刻一次性扣。0=不消耗。", MessageType.None);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("释放冲刺", EditorStyles.boldLabel);
            SerializedProperty dashOnRelease = charge.FindPropertyRelative("dashOnRelease");
            Row("松手时前冲", dashOnRelease);
            if (dashOnRelease.boolValue)
            {
                Row("冲刺距离(米)",     charge.FindPropertyRelative("dashDistance"));
                Row("冲刺距离按蓄力比例缩放", charge.FindPropertyRelative("dashDistanceScalesWithChargeRatio"));
                Row("冲刺时长备用值(秒)", charge.FindPropertyRelative("dashFallbackDurationSeconds"));
                Row("冲刺 VFX", charge.FindPropertyRelative("dashVfx"));
                SerializedProperty dashVfxAnchor = charge.FindPropertyRelative("dashVfxAnchor");
                Row("冲刺特效锚点", dashVfxAnchor);
                if (dashVfxAnchor.enumValueIndex == (int)ChargeDashVfxAnchorMode.Character)
                    Row("锚点偏移量(角色本地空间)", charge.FindPropertyRelative("dashVfxCharacterAnchorOffset"));
                EditorGUILayout.HelpBox(
                    "冲刺实际跑多久是按释放瞬间动画剩余播放时长自动算的，备用值只在拿不到" +
                    "动画时长时兜底用。",
                    MessageType.None);
            }
        });

        // ── 消耗 ─────────────────────────────────────────────────────────
        Section("消耗", () =>
        {
            if (skill.isChargeSkill)
            {
                EditorGUILayout.HelpBox(
                    "这是蓄力技能，LP消耗改由上面「蓄力」分组里的「每秒消耗LP」控制，这里的" +
                    "一次性消耗不会生效。",
                    MessageType.Info);
                return;
            }

            Row("LP 消耗", so.FindProperty("lpCost"));
            if (skill.lpCost > 0f)
                EditorGUILayout.HelpBox($"释放时消耗 {skill.lpCost} LP，LP 不足时无法释放。", MessageType.None);
            else
                EditorGUILayout.HelpBox("LP 消耗为 0，无消耗限制。", MessageType.None);
        });

        // ── 伤害 ─────────────────────────────────────────────────────────
        Section("伤害", () =>
        {
            BattleParameterDatabase db = LoadBattleParamDB();

            // 物理伤害
            EditorGUILayout.LabelField("物理伤害", EditorStyles.boldLabel);
            Row("伤害倍率", so.FindProperty("damageMultiplier"));
            DrawDamageTypeDropdown(so, db);
            EditorGUILayout.Space(6);

            // 属性伤害
            EditorGUILayout.LabelField("属性伤害", EditorStyles.boldLabel);
            DrawAttributeHitList(so, db, skill);
        });

        // ── 击退 & 硬直 ──────────────────────────────────────────────────
        Section("击退 & 硬直", () =>
        {
            Row("击退力", so.FindProperty("knockbackForce"));
            Row("硬直时长", so.FindProperty("stunDuration"));
        });

        // ── 帧数据 ───────────────────────────────────────────────────────
        Section("帧数据 (秒)", () =>
        {
            Row("前摇 startupTime",  so.FindProperty("startupTime"));
            Row("判定 activeTime",   so.FindProperty("activeTime"));
            Row("后摇 recoveryTime", so.FindProperty("recoveryTime"));

            float total = skill.TotalDuration;
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("合计", GUILayout.Width(140));
            EditorGUILayout.FloatField(total);
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            DrawTimingBar(skill);
        });

        // ── 连段 ─────────────────────────────────────────────────────────
        Section("连段", () =>
        {
            Row("连段下一技能 Key", so.FindProperty("nextComboSkillKey"));
        });

        // ── 特效 (VFX) ────────────────────────────────────────────────────
        Section("特效 (VFX)", () =>
        {
            Row("出招 VFX", so.FindProperty("swingVFX"));
            EditorGUILayout.HelpBox(
                "攻击发出时播放的 Effekseer 特效（比如挥砍轨迹）。留空则这个技能不播放专属特效——" +
                "不同技能可以配不同特效，不再是同武器类别共用同一个。",
                MessageType.None);
            Row("VFX 额外偏移(本地空间)", so.FindProperty("swingVfxOffset"));
            EditorGUILayout.HelpBox(
                "挥砍特效的锚点是所有挥砍技能共用的，如果这个技能的动作姿势特殊导致共用锚点位置" +
                "对不上，不想连累其它技能改共用偏移量，就用这个字段单独给这一个技能叠加修正。" +
                "跟着角色朝向镜像自动翻转方向，0=不额外偏移。",
                MessageType.None);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("位移特效（跟随位移的軌跡特效）", EditorStyles.boldLabel);
            Row("位移 VFX", so.FindProperty("travelVfx"));
            SerializedProperty travelVfxAnchor = so.FindProperty("travelVfxAnchor");
            Row("位移特效锚点", travelVfxAnchor);
            if (travelVfxAnchor.enumValueIndex == (int)ChargeDashVfxAnchorMode.Character)
                Row("锚点偏移量(角色本地空间)", so.FindProperty("travelVfxCharacterAnchorOffset"));
            EditorGUILayout.HelpBox(
                "出招瞬间播一次的 swingVFX 不一样，这个是軌跡/Trail 类型特效，会在这个技能触发的" +
                "位移过程中每帧跟随角色位置更新（比如闪避接突刺的冲刺飘带）。跟是否蓄力技无关，" +
                "留空则不播放。",
                MessageType.None);
        });

        // ── 音效 (SE) ─────────────────────────────────────────────────────
        Section("音效 (SE)", () =>
        {
            DrawSoundEntryList("出招 SE",  so.FindProperty("swingSE"), so);
            Row("出招 SE 音量倍率", so.FindProperty("swingSEVolume"));
            EditorGUILayout.HelpBox("素材本身偏轻时用这个单独调响出招SE，不会影响命中/打空SE的音量。", MessageType.None);
            DrawSoundEntryList("命中 SE",  so.FindProperty("hitSE"), so);
            DrawSoundEntryList("打空 SE",  so.FindProperty("whiffSE"), so);
            Row("音量倍率", so.FindProperty("seVolume"));
            EditorGUILayout.HelpBox(
                "出招 SE：攻击发出时播放（挥拳/出刀音）。\n命中 SE：命中目标时播放（撞击音）。\n打空 SE：判定窗口结束但未命中任何目标时播放（破风声）。\n三组各随机取一个片段。",
                MessageType.None);
        });

        // ── 多语言 ───────────────────────────────────────────────────────
        Section("多语言", () =>
        {
            GUILayout.Label("多语言名称", EditorStyles.boldLabel);
            DrawLocalizedRichTextList(so, so.FindProperty("localizedNames"), "name", skill);
            GUILayout.Space(6f);
            GUILayout.Label("多语言描述", EditorStyles.boldLabel);
            DrawLocalizedRichTextList(so, so.FindProperty("localizedDescriptions"), "description", skill);
        });
    }

    // ── 帧数据可视化条 ────────────────────────────────────────────────────

    private void DrawTimingBar(SkillDefinition skill)
    {
        float total = skill.TotalDuration;
        if (total <= 0f) return;

        Rect barRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
        barRect = new Rect(barRect.x + 144f, barRect.y, barRect.width - 144f - 4f, barRect.height);

        float startupW  = barRect.width * (skill.startupTime  / total);
        float activeW   = barRect.width * (skill.activeTime   / total);
        float recoveryW = barRect.width * (skill.recoveryTime / total);

        EditorGUI.DrawRect(new Rect(barRect.x,                     barRect.y, startupW,  barRect.height), new Color(0.3f, 0.3f, 0.6f, 0.7f));
        EditorGUI.DrawRect(new Rect(barRect.x + startupW,          barRect.y, activeW,   barRect.height), new Color(0.8f, 0.2f, 0.2f, 0.8f));
        EditorGUI.DrawRect(new Rect(barRect.x + startupW + activeW,barRect.y, recoveryW, barRect.height), new Color(0.3f, 0.5f, 0.3f, 0.7f));

        var tiny = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };
        if (startupW  > 24f) GUI.Label(new Rect(barRect.x,                      barRect.y, startupW,  barRect.height), "前摇", tiny);
        if (activeW   > 24f) GUI.Label(new Rect(barRect.x + startupW,           barRect.y, activeW,   barRect.height), "判定", tiny);
        if (recoveryW > 24f) GUI.Label(new Rect(barRect.x + startupW + activeW, barRect.y, recoveryW, barRect.height), "后摇", tiny);
    }

    // ── hitbox 预览 ───────────────────────────────────────────────────────

    private void DrawHitboxPreview(SkillDefinition skill)
    {
        const float viewSize = 100f;
        const float padding  = 10f; // 留边，让 hitbox 不会顶到边框

        Rect area = GUILayoutUtility.GetRect(viewSize, viewSize + 4f, GUILayout.Width(viewSize + 4f));
        area.x += 144f;

        EditorGUI.DrawRect(area, new Color(0.08f, 0.08f, 0.10f, 1f));
        page.DrawThinBorder(area, new Color(1f, 1f, 1f, 0.12f));

        // 自适应缩放：根据 hitbox 实际尺寸计算让它刚好填满预览框（留 padding）
        float available = (viewSize - padding * 2f) * 0.5f; // 可用半径像素
        float worldExtent; // hitbox 在世界单位中占据的最大半径/半边
        if (skill.hitbox.shape == SkillHitboxShape.Circle)
            worldExtent = skill.hitbox.radius + Mathf.Max(Mathf.Abs(skill.hitbox.offset.x), Mathf.Abs(skill.hitbox.offset.y));
        else
            worldExtent = Mathf.Max(skill.hitbox.size.x, skill.hitbox.size.y) * 0.5f
                        + Mathf.Max(Mathf.Abs(skill.hitbox.offset.x), Mathf.Abs(skill.hitbox.offset.y));

        worldExtent = Mathf.Max(worldExtent, 0.01f);
        float scale = available / worldExtent; // px per unit，自适应

        Vector2 center = area.center;

        // 十字线
        EditorGUI.DrawRect(new Rect(center.x - 1, area.y + 2, 1, area.height - 4), new Color(1f, 1f, 1f, 0.06f));
        EditorGUI.DrawRect(new Rect(area.x + 2, center.y - 1, area.width - 4, 1),   new Color(1f, 1f, 1f, 0.06f));

        // 原点
        EditorGUI.DrawRect(new Rect(center.x - 2, center.y - 2, 4, 4), new Color(0.6f, 0.6f, 0.6f, 0.8f));

        Vector2 off   = skill.hitbox.offset * scale;
        Vector2 hbCtr = new Vector2(center.x + off.x, center.y - off.y);

        Color hbFill   = new Color(0.70f, 0.68f, 0.22f, 0.40f);
        Color hbBorder = new Color(0.92f, 0.88f, 0.35f, 0.95f);

        if (skill.hitbox.shape == SkillHitboxShape.Circle)
        {
            float r = skill.hitbox.radius * scale;
            DrawCircleGizmo(hbCtr, r, hbFill, hbBorder, area);
        }
        else
        {
            Vector2 hs = skill.hitbox.size * scale * 0.5f;
            Rect boxR = Rect.MinMaxRect(
                Mathf.Max(hbCtr.x - hs.x, area.xMin + 1),
                Mathf.Max(hbCtr.y - hs.y, area.yMin + 1),
                Mathf.Min(hbCtr.x + hs.x, area.xMax - 1),
                Mathf.Min(hbCtr.y + hs.y, area.yMax - 1));
            EditorGUI.DrawRect(boxR, hbFill);
            page.DrawThinBorder(boxR, hbBorder);
        }

        // 尺寸标注（左下角）
        string sizeLabel = skill.hitbox.shape == SkillHitboxShape.Circle
            ? $"r={skill.hitbox.radius:F2}  z={skill.hitbox.zDepth:F1}"
            : $"{skill.hitbox.size.x:F2}×{skill.hitbox.size.y:F2}  z={skill.hitbox.zDepth:F1}";
        var labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal    = { textColor = new Color(0.85f, 0.82f, 0.55f, 0.85f) },
            alignment = TextAnchor.LowerLeft,
            fontSize  = 9,
        };
        GUI.Label(new Rect(area.x + 3, area.yMax - 16, area.width - 6, 14), sizeLabel, labelStyle);
    }

    private static void DrawCircleGizmo(Vector2 center, float radius, Color fill, Color border, Rect clip)
    {
        // 填充：分段扇形近似，裁剪到 clip 区域
        int seg = 24;
        float step = Mathf.PI * 2f / seg;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * step;
            float a1 = (i + 1) * step;
            Vector2 p0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            Vector2 p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;

            float minX = Mathf.Max(Mathf.Min(p0.x, p1.x, center.x), clip.xMin);
            float maxX = Mathf.Min(Mathf.Max(p0.x, p1.x, center.x), clip.xMax);
            float minY = Mathf.Max(Mathf.Min(p0.y, p1.y, center.y), clip.yMin);
            float maxY = Mathf.Min(Mathf.Max(p0.y, p1.y, center.y), clip.yMax);
            if (maxX > minX && maxY > minY)
                EditorGUI.DrawRect(new Rect(minX, minY, maxX - minX, maxY - minY), fill);
        }

        // 描边：一圈点，裁剪到 clip
        for (int i = 0; i < 48; i++)
        {
            float a = i / 48f * Mathf.PI * 2f;
            Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            if (clip.Contains(p))
                EditorGUI.DrawRect(new Rect(p.x - 1, p.y - 1, 2, 2), border);
        }
    }

    // ── 多语言富文本 ──────────────────────────────────────────────────────

    private void DrawLocalizedRichTextList(SerializedObject so, SerializedProperty listProp, string fieldKind, SkillDefinition skill)
    {
        if (listProp == null) { EditorGUILayout.HelpBox("找不到本地化字段。", MessageType.Warning); return; }

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null) { EditorGUILayout.HelpBox("未找到 LocalizationProjectSettings。", MessageType.Warning); return; }

        EnsureLocalizedEntries(listProp, settings);

        List<LocalizationProjectSettings.LanguageEntry> langs = GetOrderedLanguages(settings);
        string defaultCode = GetDefaultLanguageCode(settings);

        for (int i = 0; i < langs.Count; i++)
        {
            var lang = langs[i];
            SerializedProperty entryProp = FindLocalizedEntry(listProp, lang.languageCode);
            if (entryProp == null) continue;

            SerializedProperty textProp = entryProp.FindPropertyRelative("text");
            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault) label += "（默认）";

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            bool openEditor = GUILayout.Button("打开富文本编辑器", GUILayout.Width(140f), GUILayout.Height(24f));
            EditorGUILayout.EndHorizontal();

            string preview = textProp != null && !string.IsNullOrWhiteSpace(textProp.stringValue) ? textProp.stringValue : "（暂无内容）";
            EditorGUILayout.LabelField(preview, EditorStyles.wordWrappedMiniLabel, GUILayout.MaxHeight(40f));
            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);

            if (openEditor)
            {
                string capLang = lang.languageCode;
                string capCurrent = textProp != null ? textProp.stringValue ?? "" : "";
                string capDefault = defaultCode;
                EditorApplication.delayCall += () =>
                {
                    SkyPrisonRichTextEditorWindow.Open(
                        label,
                        capCurrent,
                        updated =>
                        {
                            if (skill == null) return;
                            so.Update();
                            SerializedProperty prop = fieldKind == "name"
                                ? so.FindProperty("localizedNames")
                                : so.FindProperty("localizedDescriptions");
                            SerializedProperty entry = FindLocalizedEntry(prop, capLang);
                            if (entry != null)
                            {
                                SerializedProperty t = entry.FindPropertyRelative("text");
                                if (t != null) t.stringValue = updated ?? "";
                            }
                            if (capLang == capDefault)
                            {
                                string syncField = fieldKind == "name" ? "displayName" : "description";
                                SerializedProperty syncProp = so.FindProperty(syncField);
                                if (syncProp != null) syncProp.stringValue = updated ?? "";
                            }
                            so.ApplyModifiedProperties();
                            EditorUtility.SetDirty(skill);
                        },
                        "skill");
                };
            }
        }
    }

    private void EnsureLocalizedEntries(SerializedProperty listProp, LocalizationProjectSettings settings)
    {
        var existing = new HashSet<string>();
        for (int i = 0; i < listProp.arraySize; i++)
        {
            var codeProp = listProp.GetArrayElementAtIndex(i).FindPropertyRelative("languageCode");
            if (codeProp != null && !string.IsNullOrWhiteSpace(codeProp.stringValue))
                existing.Add(codeProp.stringValue);
        }
        for (int i = 0; i < settings.languages.Count; i++)
        {
            var lang = settings.languages[i];
            if (lang == null || !lang.enabled || existing.Contains(lang.languageCode)) continue;
            int idx = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(idx);
            var newItem = listProp.GetArrayElementAtIndex(idx);
            var cp = newItem.FindPropertyRelative("languageCode");
            var tp = newItem.FindPropertyRelative("text");
            if (cp != null) cp.stringValue = lang.languageCode;
            if (tp != null) tp.stringValue = "";
            existing.Add(lang.languageCode);
        }
    }

    private SerializedProperty FindLocalizedEntry(SerializedProperty listProp, string languageCode)
    {
        for (int i = 0; i < listProp.arraySize; i++)
        {
            var codeProp = listProp.GetArrayElementAtIndex(i).FindPropertyRelative("languageCode");
            if (codeProp != null && codeProp.stringValue == languageCode)
                return listProp.GetArrayElementAtIndex(i);
        }
        return null;
    }

    private string GetDefaultLanguageCode(LocalizationProjectSettings settings)
    {
        for (int i = 0; i < settings.languages.Count; i++)
            if (settings.languages[i] != null && settings.languages[i].isDefault)
                return settings.languages[i].languageCode;
        return settings.languages.Count > 0 ? settings.languages[0].languageCode : "zh";
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetOrderedLanguages(LocalizationProjectSettings settings)
    {
        var result = new List<LocalizationProjectSettings.LanguageEntry>();
        for (int i = 0; i < settings.languages.Count; i++)
        {
            var l = settings.languages[i];
            if (l != null && l.enabled && l.isDefault) result.Insert(0, l);
            else if (l != null && l.enabled)            result.Add(l);
        }
        return result;
    }

    // ── 伤害类型下拉 ──────────────────────────────────────────────────────

    private static BattleParameterDatabase _cachedDB;

    private static BattleParameterDatabase LoadBattleParamDB()
    {
        if (_cachedDB != null) return _cachedDB;
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:BattleParameterDatabase");
        if (guids.Length > 0)
            _cachedDB = UnityEditor.AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        return _cachedDB;
    }

    private static void DrawDamageTypeDropdown(SerializedObject so, BattleParameterDatabase db)
    {
        SerializedProperty prop = so.FindProperty("damageTypeKey");
        if (prop == null) return;

        // 物理类型：damageTypes 里剔除与 elementAnomalies 同 key 的（即 heat/shock 等）
        List<string> physicalKeys = new List<string>();
        List<string> physicalLabels = new List<string>();

        if (db != null && db.damageTypes != null)
        {
            var anomalyKeys = new HashSet<string>();
            if (db.elementAnomalies != null)
                foreach (var a in db.elementAnomalies)
                    if (a != null) anomalyKeys.Add(a.key);

            foreach (var dt in db.damageTypes)
            {
                if (dt == null || anomalyKeys.Contains(dt.key)) continue;
                physicalKeys.Add(dt.key);
                physicalLabels.Add($"{dt.displayName}  ({dt.key})");
            }
        }

        if (physicalKeys.Count == 0)
        {
            // fallback 到文本框
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("物理类型 Key", GUILayout.Width(140));
            prop.stringValue = EditorGUILayout.TextField(prop.stringValue);
            EditorGUILayout.EndHorizontal();
            return;
        }

        int current = physicalKeys.IndexOf(prop.stringValue);
        if (current < 0) current = 0;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("物理类型", GUILayout.Width(140));
        EditorGUI.BeginChangeCheck();
        int selected = EditorGUILayout.Popup(current, physicalLabels.ToArray());
        if (EditorGUI.EndChangeCheck())
            prop.stringValue = physicalKeys[selected];
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawAttributeHitList(SerializedObject so, BattleParameterDatabase db, SkillDefinition skill)
    {
        SerializedProperty listProp = so.FindProperty("attributeHits");
        if (listProp == null) return;

        // 构建元素选项
        List<string> elemKeys   = new List<string>();
        List<string> elemLabels = new List<string>();
        if (db != null && db.elementAnomalies != null)
        {
            foreach (var a in db.elementAnomalies)
            {
                if (a == null) continue;
                elemKeys.Add(a.key);
                elemLabels.Add($"{a.displayName}  ({a.key})");
            }
        }

        // 列表标题 + 添加按钮
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"属性列表  [{listProp.arraySize}]", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("+ 添加属性", EditorStyles.miniButton, GUILayout.Width(72)))
        {
            listProp.InsertArrayElementAtIndex(listProp.arraySize);
            var newElem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
            newElem.FindPropertyRelative("attributeKey").stringValue              = elemKeys.Count > 0 ? elemKeys[0] : "heat";
            newElem.FindPropertyRelative("attributeDamageMultiplier").floatValue  = 0.5f;
            newElem.FindPropertyRelative("anomalyBuildupMultiplier").floatValue = 1.0f;
            so.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        if (listProp.arraySize == 0)
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10 };
            GUILayout.Label("无属性伤害（纯物理攻击）", style);
            return;
        }

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty elem     = listProp.GetArrayElementAtIndex(i);
            SerializedProperty keyProp  = elem.FindPropertyRelative("attributeKey");
            SerializedProperty dmgProp  = elem.FindPropertyRelative("attributeDamageMultiplier");
            SerializedProperty buildProp = elem.FindPropertyRelative("anomalyBuildupMultiplier");

            // 颜色条背景
            Color rowBg = GetElementColor(keyProp.stringValue, db);
            rowBg.a = 0.13f;
            Rect rowRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rowRect, rowBg);

            // 行内容
            EditorGUILayout.BeginHorizontal();

            // 属性下拉
            if (elemKeys.Count > 0)
            {
                int cur = elemKeys.IndexOf(keyProp.stringValue);
                if (cur < 0) cur = 0;
                EditorGUI.BeginChangeCheck();
                int sel = EditorGUILayout.Popup(cur, elemLabels.ToArray(), GUILayout.Width(160));
                if (EditorGUI.EndChangeCheck())
                    keyProp.stringValue = elemKeys[sel];
            }
            else
            {
                keyProp.stringValue = EditorGUILayout.TextField(keyProp.stringValue, GUILayout.Width(100));
            }

            // 伤害倍率
            GUILayout.Label("伤害×", EditorStyles.miniLabel, GUILayout.Width(38));
            dmgProp.floatValue = EditorGUILayout.FloatField(dmgProp.floatValue, GUILayout.Width(48));

            // 蓄积倍率
            GUILayout.Label("蓄积×", EditorStyles.miniLabel, GUILayout.Width(38));
            buildProp.floatValue = EditorGUILayout.FloatField(buildProp.floatValue, GUILayout.Width(48));

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                listProp.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private static Color GetElementColor(string key, BattleParameterDatabase db)
    {
        if (db != null && db.elementAnomalies != null)
            foreach (var a in db.elementAnomalies)
                if (a != null && a.key == key) return a.color;
        return new Color(0.6f, 0.6f, 0.6f);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private void Section(string title, System.Action drawer)
    {
        EditorGUILayout.BeginVertical("box");
        bool old = foldouts.TryGetValue(title, out bool v) ? v : true;
        bool nw  = EditorGUILayout.Foldout(old, title, true, EditorStyles.foldoutHeader);
        foldouts[title] = nw;
        if (nw) { EditorGUILayout.Space(4); drawer?.Invoke(); }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    private static void Row(string label, SerializedProperty prop, bool multiline = false)
    {
        if (prop == null) { EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning); return; }
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140));
        if (multiline && prop.propertyType == SerializedPropertyType.String)
            prop.stringValue = EditorGUILayout.TextArea(prop.stringValue, GUILayout.MinHeight(54));
        else
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        EditorGUILayout.EndHorizontal();
    }

    // SkillSoundEntry[] 专用列表——不用默认的 PropertyField 数组UI，是因为 Unity 的
    // "+"按钮默认复制数组最后一个元素的值，不是用C#类定义的默认值。这几个SE列表最早
    // 有几条素材手动改过Volume=0做过测试，之后每次点"+"新增的元素都被复制成0，感觉
    // 像是"新建的默认值是0"，其实是"复制了上一条"——改成自己控制新增逻辑，新增时
    // 显式给 volume=1，clip=null，不会再继承前一条可能被改坏的音量。
    private static void DrawSoundEntryList(string label, SerializedProperty listProp, SerializedObject so)
    {
        if (listProp == null) { EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning); return; }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.Width(140));
        GUILayout.FlexibleSpace();
        int count = listProp.arraySize;
        GUILayout.Label($"{count}", EditorStyles.miniLabel, GUILayout.Width(24));
        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            listProp.InsertArrayElementAtIndex(count);
            SerializedProperty added = listProp.GetArrayElementAtIndex(count);
            added.FindPropertyRelative("clip").objectReferenceValue = null;
            added.FindPropertyRelative("volume").floatValue = 1f;
            so.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty elem = listProp.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{i + 1}.", GUILayout.Width(22));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("clip"), GUIContent.none, GUILayout.ExpandWidth(true));
            GUILayout.Label("音量", GUILayout.Width(32));
            EditorGUILayout.PropertyField(elem.FindPropertyRelative("volume"), GUIContent.none, GUILayout.Width(50));
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                listProp.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
