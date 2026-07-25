using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SkyPrisonBattleDefinitionsWorkspace
{
    private readonly SkyPrisonBattleParametersPage page;

    private readonly HashSet<string> standardCoreKeys = new HashSet<string>
    {
        "maxHp","maxLp","hp","lp","atk","atkSpeed","hpRecoveryRate","lpRecoveryRate",
        "heatDamage","shockDamage","corrosionDamage","freezeDamage",
        "dodgeLpCostRate","sprintLpCostRate","lightAtkLpCostRate","heavyAtkLpCostRate","chargeAtkLpCostRate",
        "lpRecoveryDelayRate","dodgeInvulTimeRate","staggerRecoveryRate","poise","hitRecoveryRate",
        "critRate","critDamageMultiplier","negativeCritRate","negativeCritDamageMultiplier",
        "lpRecoveryBase","hpRecoveryBase","lpRecoveryDelayBase",
        "heatBuildUp","shockBuildUp","corrosionBuildUp","freezeBuildUp",
        "def","slashResist","strikeResist","impactResist","heatResist","shockResist","corrosionResist","freezeResist"
    };

    private readonly HashSet<string> standardActionModuleKeys = new HashSet<string>
    {
        "one_hand_sword","hammer"
    };

    private readonly HashSet<string> standardDamageTypeKeys = new HashSet<string>
    {
        "slash","strike","impact"
    };

    private readonly HashSet<string> standardElementKeys = new HashSet<string>
    {
        "heat","shock","corrosion","freeze"
    };

    private readonly HashSet<string> standardStatusDisplayKeys = new HashSet<string>
    {
        "default_status_display"
    };

    private enum FormulaFocusGroup
    {
        None,
        BaseValue,
        Additive,
        Multiplicative
    }

    private enum FormulaDropTarget
    {
        None,
        BaseValue,
        Additive,
        Multiplicative
    }

    private Vector2 listScroll;
    private Vector2 detailScroll;
    private float listContentHeight = 0f;

    private int selectedCoreAttributeIndex = -1;
    private int selectedActionModuleIndex = -1;
    private int selectedDamageTypeIndex = -1;
    private int selectedElementIndex = -1;
    private int selectedStatusDisplayIndex = -1;
    private int selectedFormulaRuleIndex = -1;

    private bool coreStandardFoldout = true;
    private bool coreCustomFoldout = true;
    private bool actionStandardFoldout = true;
    private bool actionCustomFoldout = true;
    private bool damageStandardFoldout = true;
    private bool damageCustomFoldout = true;
    private bool elementStandardFoldout = true;
    private bool elementCustomFoldout = true;
    private bool statusStandardFoldout = true;
    private bool statusCustomFoldout = true;

    private FormulaFocusGroup focusedFormulaGroup = FormulaFocusGroup.None;
    private int focusedFormulaItemIndex = -1;
    private FormulaDropTarget hoveredFormulaDropTarget = FormulaDropTarget.None;

    private string formulaToolboxSearchText = "";
    private Vector2 formulaToolboxScroll;
    private Vector2 formulaAdditiveScroll;
    private Vector2 formulaMultiplicativeScroll;

    private Rect baseValueSlotRect;
    private Rect additivePanelScreenRect;
    private Rect multiplicativePanelScreenRect;
    private Rect toolboxPanelScreenRect;

    private const string FormulaDragKey = "SkyPrison.Formula.AttributeKey";
    private const string FormulaDragDisplayName = "SkyPrison.Formula.AttributeDisplayName";

    public SkyPrisonBattleDefinitionsWorkspace(SkyPrisonBattleParametersPage page)
    {
        this.page = page;
    }

    public void Draw()
    {
        Rect fullRect = GUILayoutUtility.GetRect(
            0f, 100000f, 0f, 100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        const float leftWidth = 320f;
        const float splitterWidth = 4f;
        const float headerHeight = 126f;
        const float topGap = 8f;
        const float panelGap = 8f;

        Rect headerRect = new Rect(fullRect.x, fullRect.y, fullRect.width, headerHeight);
        Rect contentRect = new Rect(
            fullRect.x,
            headerRect.yMax + topGap,
            fullRect.width,
            Mathf.Max(50f, fullRect.height - headerHeight - topGap));

        DrawDatabaseHeader(headerRect);

        Rect listRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
        Rect splitterRect = new Rect(listRect.xMax, contentRect.y, splitterWidth, contentRect.height);
        Rect detailRect = new Rect(
            splitterRect.xMax + panelGap,
            contentRect.y,
            contentRect.width - leftWidth - splitterWidth - panelGap,
            contentRect.height);

        DrawListPanel(listRect);
        DrawSplitter(splitterRect);
        DrawDetailPanel(detailRect);
    }

    public void EnsureMigration()
    {
        SerializedObject so = page.SelectedSO;
        if (so == null)
            return;

        EnsureStandardFlag(so.FindProperty("coreAttributes"), standardCoreKeys);
        EnsureStandardFlag(so.FindProperty("actionModules"), standardActionModuleKeys);
        EnsureStandardFlag(so.FindProperty("damageTypes"), standardDamageTypeKeys);
        EnsureStandardFlag(so.FindProperty("elementAnomalies"), standardElementKeys);
        EnsureStandardFlag(so.FindProperty("statusDisplays"), standardStatusDisplayKeys);
    }

    private void EnsureStandardFlag(SerializedProperty array, HashSet<string> standardKeys)
    {
        if (array == null)
            return;

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = item.FindPropertyRelative("key");
            SerializedProperty standardProp = item.FindPropertyRelative("isStandard");
            if (keyProp == null || standardProp == null)
                continue;

            if (standardKeys.Contains(keyProp.stringValue) && !standardProp.boolValue)
                standardProp.boolValue = true;
        }
    }

    private void DrawDatabaseHeader(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.19f, 1f));
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect inner = new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, rect.height - 20f);
        GUILayout.BeginArea(inner);

        EditorGUILayout.LabelField("战斗参数定义工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetSectionTitle(), EditorStyles.miniBoldLabel);

        GUILayout.Space(8f);

        SerializedProperty databaseIdProp = page.SelectedSO.FindProperty("databaseId");
        SerializedProperty displayNameProp = page.SelectedSO.FindProperty("displayName");
        SerializedProperty runtimeActiveProp = page.SelectedSO.FindProperty("isRuntimeActive");

        page.DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(page.SelectedDatabase));
        page.DrawPropertyRow("参数库 ID", databaseIdProp);
        page.DrawPropertyRow("显示名称", displayNameProp);

        if (runtimeActiveProp != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("运行时使用", GUILayout.Width(96f));

            bool current = runtimeActiveProp.boolValue;
            bool next = EditorGUILayout.Toggle(current, GUILayout.Width(18f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (next != current)
            {
                runtimeActiveProp.boolValue = next;
                page.SelectedSO.ApplyModifiedProperties();

                if (next)
                    page.SetRuntimeActiveDatabase(page.SelectedDatabase);

                EditorUtility.SetDirty(page.SelectedDatabase);
                AssetDatabase.SaveAssets();
                GUI.changed = true;
            }
        }

        GUILayout.EndArea();
    }

    private string GetSectionTitle()
    {
        return GetCurrentSection() switch
        {
            BattleParameterSection.CoreAttributes => "核心属性",
            BattleParameterSection.ActionModules => "动作模组",
            BattleParameterSection.DamageTypes => "伤害类型",
            BattleParameterSection.ElementAnomalies => "属性 / 异常",
            BattleParameterSection.StatusDisplays => "状态显示",
            BattleParameterSection.FormulaRules => "公式规则",
            _ => "定义编辑"
        };
    }

    private void DrawListPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.19f, 1f));
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.05f));

        Rect inner = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        Rect titleRect = new Rect(inner.x, inner.y, inner.width, 18f);
        Rect viewRect = new Rect(inner.x, titleRect.yMax + 6f, inner.width, Mathf.Max(20f, inner.height - 24f));

        GUI.Label(titleRect, GetSectionTitle(), EditorStyles.boldLabel);

        float contentHeight = Mathf.Max(viewRect.height - 1f, GetListContentHeightEstimate());
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        listScroll = GUI.BeginScrollView(viewRect, listScroll, contentRect, false, true);
        GUILayout.BeginArea(contentRect);

        switch (GetCurrentSection())
        {
            case BattleParameterSection.CoreAttributes:
                DrawCoreAttributeList();
                break;
            case BattleParameterSection.ActionModules:
                DrawActionModuleList();
                break;
            case BattleParameterSection.DamageTypes:
                DrawDamageTypeList();
                break;
            case BattleParameterSection.ElementAnomalies:
                DrawElementList();
                break;
            case BattleParameterSection.StatusDisplays:
                DrawStatusDisplayList();
                break;
            case BattleParameterSection.FormulaRules:
                DrawFormulaList();
                break;
            default:
                EditorGUILayout.HelpBox("请选择一个模块。", MessageType.Info);
                break;
        }

        GUILayout.EndArea();
        GUI.EndScrollView();
    }

    private float GetListContentHeightEstimate()
    {
        return GetCurrentSection() switch
        {
            BattleParameterSection.CoreAttributes => 1100f,
            BattleParameterSection.ActionModules => 620f,
            BattleParameterSection.DamageTypes => 520f,
            BattleParameterSection.ElementAnomalies => 560f,
            BattleParameterSection.StatusDisplays => 520f,
            BattleParameterSection.FormulaRules => 720f,
            _ => 420f,
        };
    }

    private void DrawDetailPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.19f, 1f));
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.05f));

        Rect inner = new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, rect.height - 20f);
        Rect viewRect = new Rect(inner.x, inner.y, inner.width, inner.height);
        float contentHeight = Mathf.Max(viewRect.height - 1f, GetDetailContentHeightEstimate());
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        detailScroll = GUI.BeginScrollView(viewRect, detailScroll, contentRect, false, true);
        GUILayout.BeginArea(contentRect);

        switch (GetCurrentSection())
        {
            case BattleParameterSection.CoreAttributes:
                DrawCoreAttributeDetail();
                break;
            case BattleParameterSection.ActionModules:
                DrawActionModuleDetail();
                break;
            case BattleParameterSection.DamageTypes:
                DrawDamageTypeDetail();
                break;
            case BattleParameterSection.ElementAnomalies:
                DrawElementDetail();
                break;
            case BattleParameterSection.StatusDisplays:
                DrawStatusDisplayDetail();
                break;
            case BattleParameterSection.FormulaRules:
                DrawFormulaDetail();
                break;
            default:
                DrawPlaceholder("定义", "请选择一个模块。");
                break;
        }

        GUILayout.EndArea();
        GUI.EndScrollView();
    }

    private float GetDetailContentHeightEstimate()
    {
        return GetCurrentSection() switch
        {
            BattleParameterSection.CoreAttributes => 520f,
            BattleParameterSection.ActionModules => 860f,
            BattleParameterSection.DamageTypes => 520f,
            BattleParameterSection.ElementAnomalies => 700f,
            BattleParameterSection.StatusDisplays => 620f,
            BattleParameterSection.FormulaRules => 980f,
            _ => 420f,
        };
    }

    private BattleParameterSection GetCurrentSection()
    {
        return page.SelectedSection;
    }

    private void DrawCoreAttributeList()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("coreAttributes");
        DrawSectionToolbar(
            "核心属性",
            "新建核心属性",
            array,
            selectedCoreAttributeIndex,
            idx => selectedCoreAttributeIndex = idx,
            item =>
            {
                item.FindPropertyRelative("key").stringValue = GenerateUniqueKey(array, "new_attribute");
                item.FindPropertyRelative("displayName").stringValue = "新属性";
                item.FindPropertyRelative("note").stringValue = "";
                item.FindPropertyRelative("valueType").enumValueIndex = (int)BattleValueType.Float;
                item.FindPropertyRelative("scope").enumValueIndex = (int)BattleDefinitionScope.Unit;
                item.FindPropertyRelative("showInUnitDefinition").boolValue = true;
                item.FindPropertyRelative("showInBuildEvaluation").boolValue = true;
                item.FindPropertyRelative("readOnlyInUnitDefinition").boolValue = false;
                item.FindPropertyRelative("isStandard").boolValue = false;
            },
            DeleteCoreAttributeAt);

        DrawGroupedList(array, ref coreStandardFoldout, ref coreCustomFoldout, selectedCoreAttributeIndex, i => selectedCoreAttributeIndex = i);
    }

    private void DrawActionModuleList()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("actionModules");
        DrawSectionToolbar(
            "动作模组",
            "新建动作模组",
            array,
            selectedActionModuleIndex,
            idx => selectedActionModuleIndex = idx,
            item =>
            {
                item.FindPropertyRelative("key").stringValue = GenerateUniqueKey(array, "new_action_module");
                item.FindPropertyRelative("displayName").stringValue = "新动作模组";
                item.FindPropertyRelative("note").stringValue = "";
                item.FindPropertyRelative("isStandard").boolValue = false;
            },
            DeleteActionModuleAt);

        DrawGroupedList(array, ref actionStandardFoldout, ref actionCustomFoldout, selectedActionModuleIndex, i => selectedActionModuleIndex = i);
    }

    private void DrawDamageTypeList()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("damageTypes");
        DrawSectionToolbar(
            "伤害类型",
            "新建伤害类型",
            array,
            selectedDamageTypeIndex,
            idx => selectedDamageTypeIndex = idx,
            item =>
            {
                item.FindPropertyRelative("key").stringValue = GenerateUniqueKey(array, "new_damage_type");
                item.FindPropertyRelative("displayName").stringValue = "新伤害类型";
                item.FindPropertyRelative("note").stringValue = "";
                item.FindPropertyRelative("isStandard").boolValue = false;
            },
            DeleteDamageTypeAt);

        DrawGroupedList(array, ref damageStandardFoldout, ref damageCustomFoldout, selectedDamageTypeIndex, i => selectedDamageTypeIndex = i);
    }

    private void DrawElementList()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("elementAnomalies");
        DrawSectionToolbar(
            "属性 / 异常",
            "新建属性 / 异常",
            array,
            selectedElementIndex,
            idx => selectedElementIndex = idx,
            item =>
            {
                item.FindPropertyRelative("key").stringValue = GenerateUniqueKey(array, "new_element");
                item.FindPropertyRelative("displayName").stringValue = "新属性";
                item.FindPropertyRelative("anomalyDisplayName").stringValue = "新异常";
                item.FindPropertyRelative("note").stringValue = "";
                item.FindPropertyRelative("defaultAnomalyAccumulationRate").floatValue = 1f;
                item.FindPropertyRelative("isStandard").boolValue = false;
            },
            DeleteElementAt);

        DrawGroupedList(array, ref elementStandardFoldout, ref elementCustomFoldout, selectedElementIndex, i => selectedElementIndex = i);
    }

    private void DrawStatusDisplayList()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("statusDisplays");
        if (array == null)
        {
            EditorGUILayout.HelpBox("当前数据库中还没有 statusDisplays 字段。", MessageType.Warning);
            return;
        }

        DrawSectionToolbar(
            "状态显示",
            "新建状态显示",
            array,
            selectedStatusDisplayIndex,
            idx => selectedStatusDisplayIndex = idx,
            item =>
            {
                item.FindPropertyRelative("key").stringValue = GenerateUniqueKey(array, "new_status_display");
                item.FindPropertyRelative("displayName").stringValue = "新状态显示";
                item.FindPropertyRelative("note").stringValue = "";
                item.FindPropertyRelative("refreshDirection").enumValueIndex = (int)StatusDisplayDirection.BottomToTop;
                item.FindPropertyRelative("overflowMode").enumValueIndex = (int)StatusDisplayOverflowMode.Wrap;
                item.FindPropertyRelative("maxLines").intValue = 1;
                item.FindPropertyRelative("iconSize").vector2Value = new Vector2(18f, 18f);
                item.FindPropertyRelative("spacing").vector2Value = new Vector2(2f, 2f);
                item.FindPropertyRelative("rotatePageInterval").floatValue = 1.25f;
                item.FindPropertyRelative("exposeToUnitUI").boolValue = true;
                item.FindPropertyRelative("uiSemanticKey").stringValue = "new_status_ui";
                item.FindPropertyRelative("isStandard").boolValue = false;
            },
            DeleteStatusDisplayAt);

        DrawGroupedList(array, ref statusStandardFoldout, ref statusCustomFoldout, selectedStatusDisplayIndex, i => selectedStatusDisplayIndex = i);
    }

    private void DrawFormulaList()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("formulaRules");
        if (array == null)
        {
            EditorGUILayout.LabelField("公式规则", EditorStyles.boldLabel);
            GUILayout.Space(4f);
            page.DrawSimpleDivider();
            EditorGUILayout.HelpBox("当前数据库中还没有 formulaRules 字段。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("公式规则", EditorStyles.boldLabel);
        GUILayout.Space(4f);

        bool canDelete = IsValidIndex(array, selectedFormulaRuleIndex);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("新建公式规则", GUILayout.Height(22f)))
        {
            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            SerializedProperty item = array.GetArrayElementAtIndex(index);

            item.FindPropertyRelative("key").stringValue = GenerateUniqueKey(array, "new_formula_rule");
            item.FindPropertyRelative("displayName").stringValue = "新公式规则";
            item.FindPropertyRelative("note").stringValue = "";
            item.FindPropertyRelative("baseValueAttributeKey").stringValue = "";
            item.FindPropertyRelative("baseValueDisplayName").stringValue = "";

            RemoveFormulaPlaceholderSlots(item);
            selectedFormulaRuleIndex = index;
        }

        using (new EditorGUI.DisabledScope(!canDelete))
        {
            if (GUILayout.Button("删除", GUILayout.Width(60f), GUILayout.Height(22f)))
            {
                bool ok = EditorUtility.DisplayDialog("删除公式规则", "确定删除当前公式规则吗？", "删除", "取消");
                if (ok)
                {
                    array.DeleteArrayElementAtIndex(selectedFormulaRuleIndex);
                    selectedFormulaRuleIndex = Mathf.Clamp(selectedFormulaRuleIndex - 1, -1, array.arraySize - 1);
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        page.DrawSimpleDivider();
        GUILayout.Space(6f);

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            string key = item.FindPropertyRelative("key").stringValue;
            string displayName = item.FindPropertyRelative("displayName").stringValue;
            string label = string.IsNullOrWhiteSpace(displayName) ? key : $"{displayName}  ({key})";

            Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 24f, 24f, GUILayout.ExpandWidth(true));
            DrawLineSelectableRow(rect, selectedFormulaRuleIndex == i, label, null, () => selectedFormulaRuleIndex = i);
        }

        if (array.arraySize == 0)
            EditorGUILayout.LabelField("暂无公式规则", EditorStyles.miniLabel);
    }

    private void DrawSectionToolbar(
        string title,
        string addLabel,
        SerializedProperty array,
        int selectedIndex,
        Action<int> onSelect,
        Action<SerializedProperty> setupNew,
        Action<int> onDelete)
    {
        bool canDelete = IsValidIndex(array, selectedIndex) && !GetBool(array.GetArrayElementAtIndex(selectedIndex), "isStandard");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(addLabel, GUILayout.Height(22f)))
        {
            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);
            SerializedProperty item = array.GetArrayElementAtIndex(index);
            setupNew?.Invoke(item);
            onSelect?.Invoke(index);
        }

        using (new EditorGUI.DisabledScope(!canDelete))
        {
            if (GUILayout.Button("删除", GUILayout.Width(60f), GUILayout.Height(22f)))
                onDelete?.Invoke(selectedIndex);
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        page.DrawSimpleDivider();
        GUILayout.Space(6f);
    }

    private void DrawGroupedList(SerializedProperty array, ref bool standardFoldout, ref bool customFoldout, int selectedIndex, Action<int> onSelect)
    {
        DrawFoldoutHeader(ref standardFoldout, "标准");
        if (standardFoldout)
        {
            page.DrawSimpleDivider();
            DrawGroupedItems(array, true, selectedIndex, onSelect);
            GUILayout.Space(8f);
        }

        DrawFoldoutHeader(ref customFoldout, "自定义");
        if (customFoldout)
        {
            page.DrawSimpleDivider();
            DrawGroupedItems(array, false, selectedIndex, onSelect);
        }
    }

    private void DrawGroupedItems(SerializedProperty array, bool isStandardGroup, int selectedIndex, Action<int> onSelect)
    {
        bool hasAny = false;

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            if (GetBool(item, "isStandard") != isStandardGroup)
                continue;

            hasAny = true;

            string key = item.FindPropertyRelative("key").stringValue;
            string displayName = item.FindPropertyRelative("displayName").stringValue;
            string label = string.IsNullOrWhiteSpace(displayName) ? key : $"{displayName}  ({key})";

            Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 24f, 24f, GUILayout.ExpandWidth(true));
            DrawLineSelectableRow(rect, selectedIndex == i, label, isStandardGroup ? "标准" : null, () => onSelect?.Invoke(i));
        }

        if (!hasAny)
            EditorGUILayout.LabelField(isStandardGroup ? "暂无标准项" : "暂无自定义项", EditorStyles.miniLabel);
    }

    private void DrawCoreAttributeDetail()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("coreAttributes");
        if (!IsValidIndex(array, selectedCoreAttributeIndex))
        {
            DrawPlaceholder("核心属性", "左侧选择一个核心属性定义，或新建一项。");
            return;
        }

        SerializedProperty item = array.GetArrayElementAtIndex(selectedCoreAttributeIndex);
        bool isStandard = GetBool(item, "isStandard");

        EditorGUILayout.LabelField(isStandard ? "标准核心属性定义" : "自定义核心属性定义", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        page.DrawReadonlyRow("类别", isStandard ? "标准" : "自定义");
        if (isStandard) page.DrawReadonlyPropertyRow("Key", item.FindPropertyRelative("key"));
        else page.DrawReadonlyPropertyRow("Key", item.FindPropertyRelative("key"));

        page.DrawPropertyRow("显示名", item.FindPropertyRelative("displayName"));
        page.DrawPropertyRow("类型", item.FindPropertyRelative("valueType"));
        page.DrawPropertyRow("作用域", item.FindPropertyRelative("scope"));
        page.DrawPropertyRow("单位定义可见", item.FindPropertyRelative("showInUnitDefinition"));
        page.DrawPropertyRow("Build评估可见", item.FindPropertyRelative("showInBuildEvaluation"));
        page.DrawPropertyRow("单位定义只读", item.FindPropertyRelative("readOnlyInUnitDefinition"));
        page.DrawPropertyRow("备注", item.FindPropertyRelative("note"), true);
    }

    private void DrawActionModuleDetail()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("actionModules");
        if (!IsValidIndex(array, selectedActionModuleIndex))
        {
            DrawPlaceholder("动作模组", "左侧选择一个动作模组，或新建一项。");
            return;
        }

        SerializedProperty item = array.GetArrayElementAtIndex(selectedActionModuleIndex);
        bool isStandard = GetBool(item, "isStandard");

        EditorGUILayout.LabelField(isStandard ? "标准动作模组定义" : "自定义动作模组定义", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        page.DrawReadonlyRow("类别", isStandard ? "标准" : "自定义");
        if (isStandard) page.DrawReadonlyPropertyRow("Key", item.FindPropertyRelative("key"));
        else page.DrawPropertyRow("Key", item.FindPropertyRelative("key"));

        page.DrawPropertyRow("显示名", item.FindPropertyRelative("displayName"));

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("移动参数", EditorStyles.miniBoldLabel);
        page.DrawSimpleDivider();
        page.DrawPropertyRow("移动速度", item.FindPropertyRelative("moveSpeed"));
        page.DrawPropertyRow("冲刺速度", item.FindPropertyRelative("sprintSpeed"));
        page.DrawPropertyRow("闪避距离", item.FindPropertyRelative("dodgeDistance"));
        page.DrawPropertyRow("重量影响", item.FindPropertyRelative("weightInfluence"));

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("攻击节奏", EditorStyles.miniBoldLabel);
        page.DrawSimpleDivider();
        page.DrawPropertyRow("攻速基准", item.FindPropertyRelative("attackSpeedBase"));
        page.DrawPropertyRow("轻攻击前摇", item.FindPropertyRelative("lightAttackStartup"));
        page.DrawPropertyRow("轻攻击后摇", item.FindPropertyRelative("lightAttackRecovery"));
        page.DrawPropertyRow("重攻击前摇", item.FindPropertyRelative("heavyAttackStartup"));
        page.DrawPropertyRow("重攻击后摇", item.FindPropertyRelative("heavyAttackRecovery"));
        page.DrawPropertyRow("连段间隔", item.FindPropertyRelative("comboInterval"));
        page.DrawPropertyRow("蓄力时间", item.FindPropertyRelative("chargeTime"));

        GUILayout.Space(8f);
        EditorGUILayout.LabelField("稳定性", EditorStyles.miniBoldLabel);
        page.DrawSimpleDivider();
        page.DrawPropertyRow("基础韧性", item.FindPropertyRelative("poiseBase"));
        page.DrawPropertyRow("失衡恢复", item.FindPropertyRelative("staggerRecovery"));
        page.DrawPropertyRow("受击恢复", item.FindPropertyRelative("hitRecovery"));

        GUILayout.Space(8f);
        page.DrawPropertyRow("备注", item.FindPropertyRelative("note"), true);
    }

    private void DrawStatusDisplayDetail()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("statusDisplays");
        if (!IsValidIndex(array, selectedStatusDisplayIndex))
        {
            DrawPlaceholder("状态显示", "左侧选择一个状态显示定义，或新建一项。");
            return;
        }

        SerializedProperty item = array.GetArrayElementAtIndex(selectedStatusDisplayIndex);
        bool isStandard = GetBool(item, "isStandard");

        EditorGUILayout.LabelField(isStandard ? "标准状态显示定义" : "自定义状态显示定义", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        page.DrawReadonlyRow("类别", isStandard ? "标准" : "自定义");
        if (isStandard) page.DrawReadonlyPropertyRow("Key", item.FindPropertyRelative("key"));
        else page.DrawPropertyRow("Key", item.FindPropertyRelative("key"));

        page.DrawPropertyRow("显示名", item.FindPropertyRelative("displayName"));
        page.DrawPropertyRow("刷新方向", item.FindPropertyRelative("refreshDirection"));
        page.DrawPropertyRow("溢出模式", item.FindPropertyRelative("overflowMode"));
        page.DrawPropertyRow("最大行数", item.FindPropertyRelative("maxLines"));
        page.DrawPropertyRow("图标尺寸", item.FindPropertyRelative("iconSize"));
        page.DrawPropertyRow("图标间距", item.FindPropertyRelative("spacing"));
        page.DrawPropertyRow("轮播间隔", item.FindPropertyRelative("rotatePageInterval"));
        page.DrawPropertyRow("暴露给单位UI", item.FindPropertyRelative("exposeToUnitUI"));
        page.DrawPropertyRow("UI语义Key", item.FindPropertyRelative("uiSemanticKey"));
        page.DrawPropertyRow("备注", item.FindPropertyRelative("note"), true);
    }

    private void DrawDamageTypeDetail()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("damageTypes");
        if (!IsValidIndex(array, selectedDamageTypeIndex))
        {
            DrawPlaceholder("伤害类型", "左侧选择一个伤害类型，或新建一项。");
            return;
        }

        SerializedProperty item = array.GetArrayElementAtIndex(selectedDamageTypeIndex);
        bool isStandard = GetBool(item, "isStandard");

        EditorGUILayout.LabelField(isStandard ? "标准伤害类型定义" : "自定义伤害类型定义", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        page.DrawReadonlyRow("类别", isStandard ? "标准" : "自定义");
        if (isStandard) page.DrawReadonlyPropertyRow("Key", item.FindPropertyRelative("key"));
        else page.DrawPropertyRow("Key", item.FindPropertyRelative("key"));

        page.DrawPropertyRow("显示名", item.FindPropertyRelative("displayName"));
        page.DrawPropertyRow("颜色", item.FindPropertyRelative("color"));
        DrawCompactSpriteFieldRow("图标", item.FindPropertyRelative("icon"));
        page.DrawPropertyRow("备注", item.FindPropertyRelative("note"), true);
    }

    private void DrawCompactSpriteFieldRow(string label, SerializedProperty spriteProp)
    {
        if (spriteProp == null)
            return;

        const float labelColumnWidth = 96f;
        const float fieldColumnOffset = 96f;
        const float fieldWidth = 320f;
        const float gap = 6f;
        const float pingButtonWidth = 48f;
        const float clearButtonWidth = 48f;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelColumnWidth));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        Rect rowRect = GUILayoutUtility.GetRect(
            0f, 10000f,
            EditorGUIUtility.singleLineHeight,
            EditorGUIUtility.singleLineHeight,
            GUILayout.ExpandWidth(true));

        Rect fieldRect = new Rect(rowRect.x + fieldColumnOffset, rowRect.y, fieldWidth, rowRect.height);
        Rect pingRect = new Rect(fieldRect.xMax + gap, rowRect.y, pingButtonWidth, rowRect.height);
        Rect clearRect = new Rect(pingRect.xMax + gap, rowRect.y, clearButtonWidth, rowRect.height);

        EditorGUI.BeginChangeCheck();
        UnityEngine.Object picked = EditorGUI.ObjectField(fieldRect, GUIContent.none, spriteProp.objectReferenceValue, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck())
        {
            spriteProp.objectReferenceValue = picked;
            GUI.changed = true;
        }

        using (new EditorGUI.DisabledScope(spriteProp.objectReferenceValue == null))
        {
            if (GUI.Button(pingRect, "定位"))
            {
                EditorGUIUtility.PingObject(spriteProp.objectReferenceValue);
                Selection.activeObject = spriteProp.objectReferenceValue;
            }

            if (GUI.Button(clearRect, "清空"))
            {
                spriteProp.objectReferenceValue = null;
                GUI.changed = true;
            }
        }

        GUILayout.Space(2f);
    }


    private void DrawElementDetail()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("elementAnomalies");
        if (!IsValidIndex(array, selectedElementIndex))
        {
            DrawPlaceholder("属性 / 异常", "左侧选择一个属性 / 异常，或新建一项。");
            return;
        }

        SerializedProperty item = array.GetArrayElementAtIndex(selectedElementIndex);
        bool isStandard = GetBool(item, "isStandard");

        EditorGUILayout.LabelField(isStandard ? "标准属性 / 异常定义" : "自定义属性 / 异常定义", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        page.DrawReadonlyRow("类别", isStandard ? "标准" : "自定义");
        if (isStandard) page.DrawReadonlyPropertyRow("Key", item.FindPropertyRelative("key"));
        else page.DrawPropertyRow("Key", item.FindPropertyRelative("key"));

        page.DrawPropertyRow("属性名", item.FindPropertyRelative("displayName"));
        page.DrawPropertyRow("异常名", item.FindPropertyRelative("anomalyDisplayName"));
        page.DrawPropertyRow("默认累计系数", item.FindPropertyRelative("defaultAnomalyAccumulationRate"));
        page.DrawPropertyRow("颜色", item.FindPropertyRelative("color"));
        DrawCompactSpriteFieldRow("图标", item.FindPropertyRelative("icon"));

        SerializedProperty enableStatus = item.FindPropertyRelative("enableAnomalyStatusOnFull");
        SerializedProperty grantedStatusDef = item.FindPropertyRelative("grantedStatusDefinition");
        SerializedProperty grantedStatusKey = item.FindPropertyRelative("grantedStatusKey");
        SerializedProperty grantedStatusDisplayName = item.FindPropertyRelative("grantedStatusDisplayName");

        if (enableStatus != null) page.DrawPropertyRow("满值赋予状态", enableStatus);
        if (enableStatus != null && enableStatus.boolValue)
        {
            if (grantedStatusDef != null)
                DrawStatusBindingRow("绑定状态", grantedStatusDef, grantedStatusKey, grantedStatusDisplayName);

            SyncGrantedStatusFields(grantedStatusDef, grantedStatusKey, grantedStatusDisplayName);

            if (grantedStatusKey != null)
                page.DrawReadonlyRow("状态 Key", grantedStatusKey.stringValue);
            if (grantedStatusDisplayName != null)
                page.DrawReadonlyRow("状态显示名", grantedStatusDisplayName.stringValue);
        }

        page.DrawPropertyRow("备注", item.FindPropertyRelative("note"), true);

        GUILayout.Space(10f);
        DrawGlobalAnomalyReceiveSettings();
    }

    private void DrawGlobalAnomalyReceiveSettings()
    {
        EditorGUILayout.LabelField("整体类别异常倍率", EditorStyles.miniBoldLabel);
        page.DrawSimpleDivider();

        SerializedProperty nonCharacterProp = page.SelectedSO.FindProperty("nonCharacterAnomalyReceiveMultiplier");
        if (nonCharacterProp != null)
            page.DrawPropertyRow("非角色/掉落物", nonCharacterProp);

        SerializedProperty listProp = page.SelectedSO.FindProperty("characterIdentityAnomalyReceiveMultipliers");
        EnsureCharacterIdentityAnomalyReceiveMultiplierEntries(listProp);

        if (listProp != null)
        {
            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty item = listProp.GetArrayElementAtIndex(i);
                SerializedProperty identityProp = item.FindPropertyRelative("identity");
                SerializedProperty multiplierProp = item.FindPropertyRelative("multiplier");
                if (identityProp == null || multiplierProp == null)
                    continue;

                string label = ((CharacterIdentity)identityProp.enumValueIndex).ToString();
                page.DrawPropertyRow(label, multiplierProp);
            }
        }

        SerializedProperty decayDelayProp = page.SelectedSO.FindProperty("anomalyDecayDelay");
        SerializedProperty decayPerSecondProp = page.SelectedSO.FindProperty("anomalyDecayPerSecond");

        if (decayDelayProp != null)
            page.DrawPropertyRow("衰减延迟", decayDelayProp);
        if (decayPerSecondProp != null)
            page.DrawPropertyRow("每秒衰减", decayPerSecondProp);
    }

    private void EnsureCharacterIdentityAnomalyReceiveMultiplierEntries(SerializedProperty listProp)
    {
        if (listProp == null)
            return;

        HashSet<int> existing = new HashSet<int>();
        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            SerializedProperty identityProp = item.FindPropertyRelative("identity");
            if (identityProp != null)
                existing.Add(identityProp.enumValueIndex);
        }

        CharacterIdentity[] allValues = (CharacterIdentity[])Enum.GetValues(typeof(CharacterIdentity));
        for (int i = 0; i < allValues.Length; i++)
        {
            int enumIndex = (int)allValues[i];
            if (existing.Contains(enumIndex))
                continue;

            int newIndex = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(newIndex);

            SerializedProperty newItem = listProp.GetArrayElementAtIndex(newIndex);
            SerializedProperty identityProp = newItem.FindPropertyRelative("identity");
            SerializedProperty multiplierProp = newItem.FindPropertyRelative("multiplier");

            if (identityProp != null)
                identityProp.enumValueIndex = enumIndex;

            if (multiplierProp != null)
            {
                float defaultValue = 1f;
                if (allValues[i] == CharacterIdentity.Elite) defaultValue = 0.75f;
                else if (allValues[i] == CharacterIdentity.Boss) defaultValue = 0.5f;
                else if (allValues[i] == CharacterIdentity.NeutralPassive) defaultValue = 0f;

                multiplierProp.floatValue = defaultValue;
            }
        }
    }
    private void DrawStatusBindingRow(string label, SerializedProperty grantedStatusDef, SerializedProperty grantedStatusKey, SerializedProperty grantedStatusDisplayName)
    {
        if (grantedStatusDef == null)
            return;

        const float labelColumnWidth = 96f;
        const float fieldColumnOffset = 96f;
        const float fieldWidth = 320f;
        const float gap = 6f;
        const float pickerButtonWidth = 72f;
        const float pingButtonWidth = 48f;
        const float clearButtonWidth = 48f;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelColumnWidth));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        Rect rowRect = GUILayoutUtility.GetRect(
            0f, 10000f,
            EditorGUIUtility.singleLineHeight,
            EditorGUIUtility.singleLineHeight,
            GUILayout.ExpandWidth(true));

        Rect fieldRect = new Rect(rowRect.x + fieldColumnOffset, rowRect.y, fieldWidth, rowRect.height);
        Rect pickerRect = new Rect(fieldRect.xMax + gap, rowRect.y, pickerButtonWidth, rowRect.height);
        Rect pingRect = new Rect(pickerRect.xMax + gap, rowRect.y, pingButtonWidth, rowRect.height);
        Rect clearRect = new Rect(pingRect.xMax + gap, rowRect.y, clearButtonWidth, rowRect.height);

        EditorGUI.BeginChangeCheck();
        UnityEngine.Object picked = EditorGUI.ObjectField(fieldRect, GUIContent.none, grantedStatusDef.objectReferenceValue, typeof(StatusDefinition), false);
        if (EditorGUI.EndChangeCheck())
        {
            grantedStatusDef.objectReferenceValue = picked;
            SyncGrantedStatusFields(grantedStatusDef, grantedStatusKey, grantedStatusDisplayName);
            GUI.changed = true;
        }

        if (GUI.Button(pickerRect, "选择"))
        {
            StatusPickerWindow.OpenAccumulationOnly(
                grantedStatusDef.objectReferenceValue as StatusDefinition,
                pickedStatus =>
                {
                    grantedStatusDef.serializedObject.Update();
                    grantedStatusDef.objectReferenceValue = pickedStatus;
                    SyncGrantedStatusFields(grantedStatusDef, grantedStatusKey, grantedStatusDisplayName);
                    grantedStatusDef.serializedObject.ApplyModifiedProperties();
                    GUI.changed = true;
                });
        }

        using (new EditorGUI.DisabledScope(grantedStatusDef.objectReferenceValue == null))
        {
            if (GUI.Button(pingRect, "定位"))
            {
                EditorGUIUtility.PingObject(grantedStatusDef.objectReferenceValue);
                Selection.activeObject = grantedStatusDef.objectReferenceValue;
            }

            if (GUI.Button(clearRect, "清空"))
            {
                grantedStatusDef.objectReferenceValue = null;
                SyncGrantedStatusFields(grantedStatusDef, grantedStatusKey, grantedStatusDisplayName);
                GUI.changed = true;
            }
        }

        GUILayout.Space(2f);
    }

    private void SyncGrantedStatusFields(SerializedProperty grantedStatusDef, SerializedProperty grantedStatusKey, SerializedProperty grantedStatusDisplayName)
    {
        if (grantedStatusDef == null)
            return;

        StatusDefinition status = grantedStatusDef.objectReferenceValue as StatusDefinition;
        string key = "";
        string displayName = "";

        if (status != null)
        {
            key = status.statusId ?? "";
            displayName = !string.IsNullOrWhiteSpace(status.displayName) ? status.displayName : status.name;
        }

        if (grantedStatusKey != null && grantedStatusKey.stringValue != key)
            grantedStatusKey.stringValue = key;

        if (grantedStatusDisplayName != null && grantedStatusDisplayName.stringValue != displayName)
            grantedStatusDisplayName.stringValue = displayName;
    }

    private void DrawFormulaDetail()
    {
        SerializedProperty array = page.SelectedSO.FindProperty("formulaRules");
        if (!IsValidIndex(array, selectedFormulaRuleIndex))
        {
            DrawPlaceholder("公式规则", "左侧选择一个公式规则，或新建一项。");
            return;
        }

        SerializedProperty item = array.GetArrayElementAtIndex(selectedFormulaRuleIndex);
        EnsureFormulaRuleMinimumSkeleton(item);

        EditorGUILayout.LabelField("公式规则定义", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        page.DrawPropertyRow("Key", item.FindPropertyRelative("key"));
        page.DrawPropertyRow("显示名", item.FindPropertyRelative("displayName"));
        page.DrawPropertyRow("备注", item.FindPropertyRelative("note"), true);

        GUILayout.Space(10f);
        EditorGUILayout.LabelField("词条配置", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox("先单击上方“加算区”或“乘算区”作为当前目标，再在下方工具箱中双击词条进行添加。三块区域都可滚动。", MessageType.Info);
        GUILayout.Space(6f);

        const float composerHeight = 560f;
        Rect hostRect = GUILayoutUtility.GetRect(0f, 10000f, 760f, composerHeight, GUILayout.ExpandWidth(true));
        float width = Mathf.Min(760f, hostRect.width);
        Rect drawRect = new Rect(hostRect.x, hostRect.y, width, composerHeight);
        DrawFormulaDoubleClickComposer(drawRect, item);
    }

    private void DrawFormulaDoubleClickComposer(Rect rect, SerializedProperty ruleItem)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.11f, 1f));
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.05f));

        Rect safeRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        const float gap = 12f;
        const float topHeight = 220f;

        float halfWidth = (safeRect.width - gap) * 0.5f;
        Rect addRect = new Rect(safeRect.x, safeRect.y, halfWidth, topHeight);
        Rect mulRect = new Rect(addRect.xMax + gap, safeRect.y, halfWidth, topHeight);
        Rect toolboxRect = new Rect(safeRect.x, addRect.yMax + 16f, safeRect.width, safeRect.height - topHeight - 16f);

        SerializedProperty addArray = ruleItem.FindPropertyRelative("additiveTerms");
        SerializedProperty mulArray = ruleItem.FindPropertyRelative("multiplicativeTerms");

        DrawFormulaTargetPanel(addRect, "加算区", addArray, FormulaFocusGroup.Additive, ref formulaAdditiveScroll);
        DrawFormulaTargetPanel(mulRect, "乘算区", mulArray, FormulaFocusGroup.Multiplicative, ref formulaMultiplicativeScroll);
        DrawFormulaToolboxDoubleClickPanel(toolboxRect, ruleItem);
    }

    private bool ShouldHideFormulaEntry(SerializedProperty entry)
    {
        if (entry == null)
            return true;

        string key = entry.FindPropertyRelative("key")?.stringValue ?? "";
        string sourceKey = entry.FindPropertyRelative("sourceAttributeKey")?.stringValue ?? "";
        return string.IsNullOrWhiteSpace(sourceKey) || key == "flat_slot" || key == "multiplier_slot";
    }

    private void DrawFormulaTargetPanel(Rect rect, string title, SerializedProperty array, FormulaFocusGroup group, ref Vector2 scroll)
    {
        bool selectedTarget = focusedFormulaGroup == group;

        GUI.BeginGroup(rect);
        Rect localRect = new Rect(0f, 0f, rect.width, rect.height);

        EditorGUI.DrawRect(localRect, selectedTarget ? new Color(1f, 0.60f, 0.18f, 0.12f) : new Color(0.15f, 0.15f, 0.16f, 0.98f));
        page.DrawThinBorder(localRect, selectedTarget ? new Color(1f, 0.60f, 0.18f, 0.60f) : new Color(1f, 1f, 1f, 0.06f));

        Rect titleRect = new Rect(10f, 8f, rect.width - 20f, 18f);
        GUI.Label(titleRect, selectedTarget ? $"{title}  （当前目标）" : title, EditorStyles.miniBoldLabel);

        Rect hintRect = new Rect(10f, titleRect.yMax + 2f, rect.width - 20f, 16f);
        GUIStyle hintStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = selectedTarget ? new Color(1f, 0.78f, 0.46f, 1f) : new Color(0.74f, 0.74f, 0.76f, 1f) }
        };
        GUI.Label(hintRect, selectedTarget ? "当前双击词条会加入这里" : "点击整个框体可设为当前目标", hintStyle);
        EditorGUI.DrawRect(new Rect(10f, hintRect.yMax + 4f, rect.width - 20f, 1f), new Color(1f, 1f, 1f, 0.08f));

        Rect viewportRect = new Rect(8f, hintRect.yMax + 10f, rect.width - 16f, rect.height - (hintRect.yMax + 18f));
        int visibleCount = 0;
        for (int i = 0; i < array.arraySize; i++)
        {
            if (!ShouldHideFormulaEntry(array.GetArrayElementAtIndex(i)))
                visibleCount++;
        }

        float contentHeight = Mathf.Max(viewportRect.height, visibleCount * 24f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewportRect.width - 14f), contentHeight);

        Vector2 groupMouse = Event.current.mousePosition;
        Vector2 contentMouse = groupMouse + scroll - new Vector2(viewportRect.x, viewportRect.y);

        scroll = GUI.BeginScrollView(viewportRect, scroll, contentRect, false, true);

        int visibleIndex = 0;
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex(i);
            if (ShouldHideFormulaEntry(entry))
                continue;

            Rect rowRect = new Rect(0f, visibleIndex * 24f, contentRect.width, 22f);
            bool hover = rowRect.Contains(contentMouse);
            bool activeRow = selectedTarget && focusedFormulaItemIndex == i;

            EditorGUI.DrawRect(rowRect, activeRow
                ? new Color(1f, 0.60f, 0.18f, 0.16f)
                : hover ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0.03f));
            page.DrawThinBorder(rowRect, activeRow
                ? new Color(1f, 0.60f, 0.18f, 0.40f)
                : new Color(1f, 1f, 1f, 0.05f));

            string sourceDisplay = entry.FindPropertyRelative("sourceDisplayName")?.stringValue;
            string sourceKey = entry.FindPropertyRelative("sourceAttributeKey")?.stringValue;
            string displayName = entry.FindPropertyRelative("displayName")?.stringValue;
            string label = !string.IsNullOrWhiteSpace(sourceDisplay)
                ? sourceDisplay
                : (!string.IsNullOrWhiteSpace(displayName) ? displayName : sourceKey);

            GUI.Label(new Rect(8f, rowRect.y + 2f, rowRect.width - 52f, rowRect.height - 4f), label, EditorStyles.label);

            Rect removeRect = new Rect(rowRect.xMax - 38f, rowRect.y + 1f, 34f, rowRect.height - 2f);
            if (GUI.Button(removeRect, "-"))
            {
                array.DeleteArrayElementAtIndex(i);
                if (focusedFormulaGroup == group)
                    focusedFormulaItemIndex = -1;
                GUI.changed = true;
                GUI.EndScrollView();
                GUI.EndGroup();
                return;
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rowRect.Contains(contentMouse))
            {
                focusedFormulaGroup = group;
                focusedFormulaItemIndex = i;
                Event.current.Use();
            }

            visibleIndex++;
        }

        GUI.EndScrollView();

        bool clickedHeaderArea = Event.current.type == EventType.MouseDown
            && Event.current.button == 0
            && localRect.Contains(groupMouse)
            && !viewportRect.Contains(groupMouse);

        if (clickedHeaderArea)
        {
            focusedFormulaGroup = group;
            focusedFormulaItemIndex = -1;
            Event.current.Use();
        }

        GUI.EndGroup();
    }

    private void DrawFormulaToolboxDoubleClickPanel(Rect rect, SerializedProperty ruleItem)
    {
        GUI.BeginGroup(rect);
        Rect localRect = new Rect(0f, 0f, rect.width, rect.height);

        EditorGUI.DrawRect(localRect, new Color(0.15f, 0.15f, 0.16f, 0.98f));
        page.DrawThinBorder(localRect, new Color(1f, 1f, 1f, 0.06f));

        Rect searchRect = new Rect(10f, 10f, rect.width - 20f, 20f);
        formulaToolboxSearchText = EditorGUI.TextField(searchRect, formulaToolboxSearchText ?? "");

        EditorGUI.DrawRect(new Rect(10f, searchRect.yMax + 6f, rect.width - 20f, 1f), new Color(1f, 1f, 1f, 0.08f));

        Rect viewportRect = new Rect(10f, searchRect.yMax + 12f, rect.width - 20f, rect.height - 48f);

        List<CoreAttributeDefinition> defs = GetCoreAttributeDefinitionsForToolbox();
        string search = string.IsNullOrWhiteSpace(formulaToolboxSearchText)
            ? ""
            : formulaToolboxSearchText.Trim().ToLowerInvariant();

        List<CoreAttributeDefinition> filtered = new List<CoreAttributeDefinition>();
        for (int i = 0; i < defs.Count; i++)
        {
            CoreAttributeDefinition def = defs[i];
            string combined = $"{def.displayName} {def.key}".ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(search) || combined.Contains(search))
                filtered.Add(def);
        }

        float contentHeight = Mathf.Max(viewportRect.height, filtered.Count * 28f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewportRect.width - 14f), contentHeight);

        Vector2 groupMouse = Event.current.mousePosition;
        Vector2 contentMouse = groupMouse + formulaToolboxScroll - new Vector2(viewportRect.x, viewportRect.y);

        formulaToolboxScroll = GUI.BeginScrollView(viewportRect, formulaToolboxScroll, contentRect, false, true);

        SerializedProperty addArray = ruleItem.FindPropertyRelative("additiveTerms");
        SerializedProperty mulArray = ruleItem.FindPropertyRelative("multiplicativeTerms");

        for (int i = 0; i < filtered.Count; i++)
        {
            CoreAttributeDefinition def = filtered[i];
            Rect rowRect = new Rect(0f, i * 28f, contentRect.width, 24f);
            bool hover = rowRect.Contains(contentMouse);

            bool alreadyInTarget = focusedFormulaGroup switch
            {
                FormulaFocusGroup.Additive => FormulaArrayContainsSource(addArray, def.key),
                FormulaFocusGroup.Multiplicative => FormulaArrayContainsSource(mulArray, def.key),
                _ => false
            };

            EditorGUI.DrawRect(rowRect, hover ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.04f));
            page.DrawThinBorder(rowRect, new Color(1f, 1f, 1f, hover ? 0.10f : 0.05f));
            GUI.Label(new Rect(10f, rowRect.y + 2f, rowRect.width - 120f, rowRect.height - 4f), $"{def.displayName}  ({def.key})", EditorStyles.label);

            string stateText = focusedFormulaGroup == FormulaFocusGroup.None
                ? "未选择目标"
                : alreadyInTarget ? "已存在" : "双击加入";
            GUIStyle stateStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.80f, 0.80f, 0.82f, 1f) }
            };
            GUI.Label(new Rect(rowRect.x + rowRect.width - 100f, rowRect.y, 90f, rowRect.height), stateText, stateStyle);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rowRect.Contains(contentMouse) && Event.current.clickCount == 2)
            {
                if (focusedFormulaGroup == FormulaFocusGroup.Additive)
                    AddFormulaAttributeReference(addArray, def.key, def.displayName, FormulaFocusGroup.Additive);
                else if (focusedFormulaGroup == FormulaFocusGroup.Multiplicative)
                    AddFormulaAttributeReference(mulArray, def.key, def.displayName, FormulaFocusGroup.Multiplicative);

                Event.current.Use();
                GUI.changed = true;
                GUI.EndScrollView();
                GUI.EndGroup();
                return;
            }
        }

        GUI.EndScrollView();
        GUI.EndGroup();
    }

    private bool FormulaArrayContainsSource(SerializedProperty array, string sourceKey)
    {
        if (array == null || string.IsNullOrWhiteSpace(sourceKey))
            return false;

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            SerializedProperty sourceProp = item.FindPropertyRelative("sourceAttributeKey");
            if (sourceProp != null && sourceProp.stringValue == sourceKey)
                return true;
        }

        return false;
    }

    private void AddFormulaAttributeReference(SerializedProperty array, string sourceKey, string sourceDisplayName, FormulaFocusGroup group)
    {
        if (array == null || string.IsNullOrWhiteSpace(sourceKey))
            return;

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            if (item.FindPropertyRelative("sourceAttributeKey").stringValue == sourceKey)
                return;
        }

        int index = array.arraySize;
        array.InsertArrayElementAtIndex(index);

        SerializedProperty newItem = array.GetArrayElementAtIndex(index);
        newItem.FindPropertyRelative("key").stringValue = sourceKey;
        newItem.FindPropertyRelative("displayName").stringValue = sourceDisplayName;
        newItem.FindPropertyRelative("sourceAttributeKey").stringValue = sourceKey;
        newItem.FindPropertyRelative("sourceDisplayName").stringValue = sourceDisplayName;

        SerializedProperty defaultValueProp = newItem.FindPropertyRelative("defaultValue");
        if (defaultValueProp != null)
            defaultValueProp.floatValue = 0f;

        newItem.FindPropertyRelative("isStandard").boolValue = false;

        focusedFormulaGroup = group;
        focusedFormulaItemIndex = index;
    }

    private void EnsureFormulaRuleMinimumSkeleton(SerializedProperty item)
    {
    }

    private void RemoveFormulaPlaceholderSlots(SerializedProperty item)
    {
        SerializedProperty addArray = item.FindPropertyRelative("additiveTerms");
        SerializedProperty mulArray = item.FindPropertyRelative("multiplicativeTerms");
        RemovePlaceholderEntries(addArray);
        RemovePlaceholderEntries(mulArray);
    }

    private void RemovePlaceholderEntries(SerializedProperty array)
    {
        if (array == null)
            return;

        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex(i);
            string key = entry.FindPropertyRelative("key")?.stringValue ?? "";
            string sourceKey = entry.FindPropertyRelative("sourceAttributeKey")?.stringValue ?? "";
            if (string.IsNullOrWhiteSpace(sourceKey) || key == "flat_slot" || key == "multiplier_slot")
                array.DeleteArrayElementAtIndex(i);
        }
    }

    private List<CoreAttributeDefinition> GetCoreAttributeDefinitionsForToolbox()
    {
        List<CoreAttributeDefinition> result = new List<CoreAttributeDefinition>();
        SerializedProperty array = page.SelectedSO.FindProperty("coreAttributes");
        if (array == null)
            return result;

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            string key = item.FindPropertyRelative("key").stringValue;
            string displayName = item.FindPropertyRelative("displayName").stringValue;

            result.Add(new CoreAttributeDefinition
            {
                key = key,
                displayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName
            });
        }

        return result;
    }

    private void DeleteCoreAttributeAt(int index)
    {
        DeleteIfCustom(page.SelectedSO.FindProperty("coreAttributes"), index, "核心属性", ref selectedCoreAttributeIndex);
    }

    private void DeleteActionModuleAt(int index)
    {
        DeleteIfCustom(page.SelectedSO.FindProperty("actionModules"), index, "动作模组", ref selectedActionModuleIndex);
    }

    private void DeleteDamageTypeAt(int index)
    {
        DeleteIfCustom(page.SelectedSO.FindProperty("damageTypes"), index, "伤害类型", ref selectedDamageTypeIndex);
    }

    private void DeleteElementAt(int index)
    {
        DeleteIfCustom(page.SelectedSO.FindProperty("elementAnomalies"), index, "属性 / 异常", ref selectedElementIndex);
    }

    private void DeleteStatusDisplayAt(int index)
    {
        DeleteIfCustom(page.SelectedSO.FindProperty("statusDisplays"), index, "状态显示", ref selectedStatusDisplayIndex);
    }

    private void DeleteIfCustom(SerializedProperty array, int index, string title, ref int selectedIndex)
    {
        if (!IsValidIndex(array, index))
            return;

        SerializedProperty item = array.GetArrayElementAtIndex(index);
        if (GetBool(item, "isStandard"))
        {
            EditorUtility.DisplayDialog("无法删除", $"标准{title}不能删除。", "确定");
            return;
        }

        string label = item.FindPropertyRelative("displayName").stringValue;
        if (string.IsNullOrWhiteSpace(label))
            label = item.FindPropertyRelative("key").stringValue;

        bool ok = EditorUtility.DisplayDialog($"删除{title}", $"确定删除“{label}”吗？", "删除", "取消");
        if (!ok)
            return;

        array.DeleteArrayElementAtIndex(index);
        selectedIndex = Mathf.Clamp(index - 1, -1, array.arraySize - 1);
    }

    private void DrawFoldoutHeader(ref bool expanded, string title)
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 20f, 20f, GUILayout.ExpandWidth(true));

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            expanded = !expanded;

        string marker = expanded ? "-" : "+";
        Rect markerRect = new Rect(rect.x + 2f, rect.y, 16f, rect.height);
        Rect labelRect = new Rect(rect.x + 18f, rect.y, rect.width - 18f, rect.height);

        GUI.Label(markerRect, marker, EditorStyles.boldLabel);
        GUI.Label(labelRect, title, EditorStyles.miniBoldLabel);
    }

    private void DrawLineSelectableRow(Rect rect, bool selected, string label, string rightTag, Action onClick)
    {
        bool hover = rect.Contains(Event.current.mousePosition);

        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.25f, 0.18f, 0.10f, 0.85f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), page.AccentOrange);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.03f));
        }

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            onClick?.Invoke();

        Rect labelRect = new Rect(rect.x + 10f, rect.y, rect.width - 20f, rect.height);
        GUI.Label(labelRect, label, EditorStyles.label);

        if (!string.IsNullOrEmpty(rightTag))
        {
            GUIStyle tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.76f, 0.40f, 1f) }
            };
            GUI.Label(labelRect, rightTag, tagStyle);
        }
    }

    private void DrawSplitter(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
    }

    private void DrawPlaceholder(string title, string message)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        GUILayout.Space(6f);
        EditorGUILayout.HelpBox(message, MessageType.Info);
    }

    private void DrawFormulaCanvasGrid(Rect rect)
    {
        const float spacing = 32f;
        Color lineColor = new Color(1f, 1f, 1f, 0.03f);

        for (float x = rect.x + 8f; x < rect.xMax; x += spacing)
            EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), lineColor);

        for (float y = rect.y + 8f; y < rect.yMax; y += spacing)
            EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), lineColor);
    }

    private static bool GetBool(SerializedProperty item, string relativeName)
    {
        SerializedProperty prop = item.FindPropertyRelative(relativeName);
        return prop != null && prop.boolValue;
    }

    private static bool IsValidIndex(SerializedProperty array, int index)
    {
        return array != null && index >= 0 && index < array.arraySize;
    }

    private static string GenerateUniqueKey(SerializedProperty array, string baseKey)
    {
        HashSet<string> used = new HashSet<string>();

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty item = array.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = item.FindPropertyRelative("key");
            if (keyProp != null && !string.IsNullOrWhiteSpace(keyProp.stringValue))
                used.Add(keyProp.stringValue.Trim());
        }

        string safeBase = string.IsNullOrWhiteSpace(baseKey) ? "new_item" : baseKey.Trim();
        if (!used.Contains(safeBase))
            return safeBase;

        int suffix = 1;
        while (used.Contains($"{safeBase}_{suffix}"))
            suffix++;

        return $"{safeBase}_{suffix}";
    }
}