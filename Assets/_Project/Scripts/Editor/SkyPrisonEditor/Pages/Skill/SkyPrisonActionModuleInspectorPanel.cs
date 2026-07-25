using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SkyPrisonActionModuleInspectorPanel
{
    private readonly SkyPrisonSkillPage page;
    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息",   true },
        { "轻攻击连段", true },
        { "重攻击",     true },
        { "闪避接突刺", true },
        { "攻击取消后撤步", true },
        { "空中攻击", true },
        { "Locomotion 覆盖", false },
    };

    public SkyPrisonActionModuleInspectorPanel(SkyPrisonSkillPage page) { this.page = page; }

    public void Draw()
    {
        SerializedObject      so  = page.SelectedModuleSO;
        WeaponCombatModule mod = page.SelectedModule;
        if (so == null || mod == null) return;

        // 标题
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(string.IsNullOrWhiteSpace(mod.displayName) ? mod.name : mod.displayName,
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("在 Project 中定位", GUILayout.Height(22), GUILayout.Width(120)))
        { Selection.activeObject = mod; EditorGUIUtility.PingObject(mod); }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        // ── 基础信息 ─────────────────────────────────────────────────────
        Section("基础信息", () =>
        {
            Row("模组 Key",  so.FindProperty("moduleKey"));
            Row("显示名称", so.FindProperty("displayName"));
            Row("描述",     so.FindProperty("description"), multiline: true);
        });

        // ── 轻攻击连段 ────────────────────────────────────────────────────
        Section("轻攻击连段", () =>
        {
            SerializedProperty combo = so.FindProperty("lightAttackCombo");
            DrawSkillList(combo, so);
            DrawComboChainPreview(mod);
        });

        // ── 重攻击 ───────────────────────────────────────────────────────
        Section("重攻击", () =>
        {
            DrawSkillRow("重攻击技能", so.FindProperty("heavyAttack"));
        });

        // ── 闪避接突刺 ─────────────────────────────────────────────────────
        Section("闪避接突刺", () =>
        {
            EditorGUILayout.HelpBox("闪避快结束时可以打断闪避、无缝衔接的专属突刺技能，留空表示这个模组没有这个衔接（比如空手）。", MessageType.None);
            DrawSkillRow("闪避突刺技能", so.FindProperty("dodgeThrustAttack"));
            SerializedProperty openAfterFraction = so.FindProperty("dodgeThrustOpenAfterFraction");
            EditorGUILayout.Slider(openAfterFraction, 0f, 1f, "闪避播放到百分之几可以接");
            EditorGUILayout.HelpBox("闪避播放进度超过这个比例之后，到闪避结束这段窗口内才允许打断闪避衔接突刺。", MessageType.None);
        });

        // ── 攻击取消后撤步 ───────────────────────────────────────────────────
        Section("攻击取消后撤步", () =>
        {
            EditorGUILayout.HelpBox(
                "勾选后，这个模组的攻击在判定帧结束(后摇阶段)时可以按闪避键取消攻击、无缝衔接" +
                "一个固定后撤步——不看输入方向，固定沿角色当前朝向的正后方冲一下，播放dodge_back，" +
                "且全程保持当前朝向不转身。不勾选则这个模组的攻击不能被闪避键取消（比如空手）。",
                MessageType.None);
            Row("允许攻击取消后撤步", so.FindProperty("allowAttackCancelDodgeBack"));
        });

        // ── 空中攻击 ───────────────────────────────────────────────────────
        Section("空中攻击", () =>
        {
            EditorGUILayout.HelpBox(
                "跳跃在空中阶段时按攻击键触发的专属空中攻击技能，留空表示这个模组没有空中攻击" +
                "（比如空手）。每次跳跃只能触发一次，落地后重新计次。",
                MessageType.None);
            DrawSkillRow("空中攻击技能", so.FindProperty("aerialAttack"));
        });

        // ── Locomotion 覆盖 ───────────────────────────────────────────────
        Section("Locomotion 覆盖", () =>
        {
            EditorGUILayout.HelpBox(
                "留空 = 使用默认动画 Key，不为空时覆盖对应状态的 Spine 动画。",
                MessageType.None);

            SerializedProperty loco = so.FindProperty("locomotionOverride");
            Row("Idle",   loco.FindPropertyRelative("idle"));
            Row("Walk",   loco.FindPropertyRelative("walk"));
            Row("Run",    loco.FindPropertyRelative("run"));
            Row("Jump",   loco.FindPropertyRelative("jump"));
            Row("Land",   loco.FindPropertyRelative("land"));
            Row("Crouch", loco.FindPropertyRelative("crouch"));
        });
    }

    // ── 技能列表（显示 displayName，右侧保留 Object 选择器） ─────────────

    private void DrawSkillList(SerializedProperty listProp, SerializedObject so)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("连段列表", GUILayout.Width(140));
        GUILayout.FlexibleSpace();
        int count = listProp.arraySize;
        GUILayout.Label($"{count}", EditorStyles.miniLabel, GUILayout.Width(24));
        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
        {
            listProp.InsertArrayElementAtIndex(count);
            listProp.GetArrayElementAtIndex(count).objectReferenceValue = null;
            so.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty elem = listProp.GetArrayElementAtIndex(i);
            SkillDefinition sk = elem.objectReferenceValue as SkillDefinition;

            EditorGUILayout.BeginHorizontal();
            // 序号
            GUILayout.Label($"{i + 1}.", GUILayout.Width(22));
            // displayName 标签
            string label = sk != null
                ? $"{(string.IsNullOrWhiteSpace(sk.displayName) ? sk.skillKey : sk.displayName)}  [{sk.name}]"
                : "— 未指定 —";
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = sk != null ? new Color(0.92f, 0.88f, 0.55f) : Color.gray }
            };
            GUILayout.Label(label, labelStyle, GUILayout.ExpandWidth(true));
            // Object 选择器
            EditorGUI.BeginChangeCheck();
            var newSk = (SkillDefinition)EditorGUILayout.ObjectField(
                sk, typeof(SkillDefinition), false, GUILayout.Width(180));
            if (EditorGUI.EndChangeCheck())
            {
                elem.objectReferenceValue = newSk;
                so.ApplyModifiedProperties();
            }
            // 删除
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                listProp.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawSkillRow(string label, SerializedProperty prop)
    {
        if (prop == null) return;
        SkillDefinition sk = prop.objectReferenceValue as SkillDefinition;
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140));
        string name = sk != null
            ? $"{(string.IsNullOrWhiteSpace(sk.displayName) ? sk.skillKey : sk.displayName)}  [{sk.name}]"
            : "— 未指定 —";
        var labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = sk != null ? new Color(0.92f, 0.88f, 0.55f) : Color.gray }
        };
        GUILayout.Label(name, labelStyle, GUILayout.ExpandWidth(true));
        EditorGUI.BeginChangeCheck();
        var newSk = (SkillDefinition)EditorGUILayout.ObjectField(
            sk, typeof(SkillDefinition), false, GUILayout.Width(180));
        if (EditorGUI.EndChangeCheck())
            prop.objectReferenceValue = newSk;
        EditorGUILayout.EndHorizontal();
    }

    // ── 连段可视化 ────────────────────────────────────────────────────────

    private void DrawComboChainPreview(WeaponCombatModule mod)
    {
        var combo = mod.lightAttackCombo;
        if (combo == null || combo.Count == 0) return;

        EditorGUILayout.Space(4);
        Rect area = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        area = new Rect(area.x + 4, area.y, area.width - 8, area.height);

        float nodeW = Mathf.Min(100f, (area.width - (combo.Count - 1) * 6f) / combo.Count);
        float arrowW = 6f;
        float totalW = nodeW * combo.Count + arrowW * (combo.Count - 1);
        float startX = area.x + (area.width - totalW) * 0.5f;

        for (int i = 0; i < combo.Count; i++)
        {
            SkillDefinition sk = combo[i];
            float x = startX + i * (nodeW + arrowW);
            Rect nodeR = new Rect(x, area.y + 2, nodeW, area.height - 4);

            EditorGUI.DrawRect(nodeR, new Color(0.70f, 0.68f, 0.22f, 0.18f));
            page.DrawThinBorder(nodeR, new Color(0.70f, 0.68f, 0.22f, 0.55f));

            string label = sk != null
                ? (string.IsNullOrWhiteSpace(sk.displayName) ? sk.skillKey : sk.displayName)
                : "—";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.92f, 0.88f, 0.55f) }
            };
            GUI.Label(nodeR, label, style);

            // 箭头
            if (i < combo.Count - 1)
            {
                Rect arrowR = new Rect(x + nodeW, area.y + (area.height - 4) * 0.5f, arrowW, 4);
                EditorGUI.DrawRect(arrowR, new Color(0.70f, 0.68f, 0.22f, 0.4f));
            }
        }

        // 循环回头箭头提示
        EditorGUILayout.Space(2);
        var loopStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 9 };
        GUILayout.Label("↺ 循环", loopStyle);
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
}
