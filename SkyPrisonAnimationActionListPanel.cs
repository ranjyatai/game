using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SkyPrisonAnimationActionListPanel
{
    private readonly SkyPrisonAnimationWorkbenchState state;

    private int editingActionIndex = -1;
    private ActionEditField editingField = ActionEditField.None;
    private string editingNameBuffer = string.Empty;
    private string editingKeyBuffer = string.Empty;
    private bool requestFocusEditField = false;
    private Rect currentEditingRect;
    private Rect currentEditingScreenRect;

    private int editingGroupIndex = -1;
    private string editingGroupNameBuffer = string.Empty;
    private bool requestFocusGroupEditField = false;
    private Rect currentGroupEditingRect;
    private Rect currentGroupEditingScreenRect;

    private Rect currentScrollViewRect;

    private const float DragHandleWidth = 18f;
    private const float GroupHeaderHeight = 24f;
    private const float ActionChildIndent = 18f;
    private const float ActionRowsMinHeight = 96f;
    private const float ActionRowsMaxHeight = 260f;
    private const float ToolbarRowHeight = 24f;
    private const float SearchRowHeight = 22f;
    private const float ToolbarButtonWidth = 44f;
    private const float ToolbarButtonGap = 4f;
    private readonly Color groupHeaderBg = new Color(0.20f, 0.19f, 0.22f, 1f);
    private readonly Color groupHeaderSelectedBg = new Color(0.26f, 0.23f, 0.18f, 1f);
    private readonly Color groupAccent = new Color(1.00f, 0.62f, 0.22f, 1f);
    private readonly Color groupSeparator = new Color(1f, 1f, 1f, 0.08f);
    private const float KeyFieldWidth = 96f;
    private const float StatusWidth = 58f;
    private const string EditNameControl = "SkyPrison_ActionList_EditName";
    private const string EditKeyControl = "SkyPrison_ActionList_EditKey";
    private const string EditGroupNameControl = "SkyPrison_ActionList_EditGroupName";
    private const string SearchControl = "SkyPrison_ActionList_Search";

    private enum ActionEditField { None = 0, Name = 1, Key = 2 }

    public SkyPrisonAnimationActionListPanel(SkyPrisonAnimationWorkbenchState state)
    {
        this.state = state;
    }

    public void Draw()
    {
        state.EnsureActionGroups();
        HandleGlobalEditCommitBeforeControls();

        EditorGUILayout.BeginVertical("box");

        Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarRowHeight, GUILayout.ExpandWidth(true));
        DrawToolbarRow(toolbarRect);

        Rect searchRect = GUILayoutUtility.GetRect(1f, SearchRowHeight, GUILayout.ExpandWidth(true));
        DrawSearchRow(searchRect);

        // 只有组列表/动作行进入滚动容器。
        // 这里不用 EditorGUILayout.BeginScrollView，而是手动接管滚轮事件。
        // 原因：这个面板本身通常又在外层 ScrollView 里，内层滚轮不 Use 掉的话，
        // 会出现“拖/滚内层列表时外层滚动条也跟着动”的嵌套滚动污染。
        DrawScrollableActionRows();

        HandleEditKeyboard();
        EditorGUILayout.Space(2f);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.LabelField("提示：+组/-组管理动作组；双击组名可重命名；双击动作名称/Key 重命名；右键可删除。", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }


    private void DrawToolbarRow(Rect rect)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        const int buttonCount = 5;
        float totalButtonWidth = ToolbarButtonWidth * buttonCount + ToolbarButtonGap * (buttonCount - 1);
        float buttonY = rect.y + Mathf.Max(0f, (rect.height - 20f) * 0.5f);
        float x = rect.xMax - totalButtonWidth;

        // 按钮组永远锚在右侧。窗口变窄时，左侧标题被裁掉，按钮不会被右边吃掉。
        Rect titleRect = new Rect(rect.x, rect.y + 2f, Mathf.Max(20f, x - rect.x - 6f), rect.height - 4f);
        GUI.Label(titleRect, "动作列表", EditorStyles.boldLabel);

        if (x < rect.x)
            x = rect.x;

        Rect addGroupRect = new Rect(x, buttonY, ToolbarButtonWidth, 20f);
        x += ToolbarButtonWidth + ToolbarButtonGap;
        Rect deleteGroupRect = new Rect(x, buttonY, ToolbarButtonWidth, 20f);
        x += ToolbarButtonWidth + ToolbarButtonGap;
        Rect addActionRect = new Rect(x, buttonY, ToolbarButtonWidth, 20f);
        x += ToolbarButtonWidth + ToolbarButtonGap;
        Rect duplicateRect = new Rect(x, buttonY, ToolbarButtonWidth, 20f);
        x += ToolbarButtonWidth + ToolbarButtonGap;
        Rect deleteRect = new Rect(x, buttonY, ToolbarButtonWidth, 20f);

        if (GUI.Button(addGroupRect, "+组"))
        {
            CommitAnyEdit();
            state.AddActionGroup();
            GUI.FocusControl(null);
        }

        using (new EditorGUI.DisabledScope(state.ActionGroups.Count <= 1))
        {
            if (GUI.Button(deleteGroupRect, "-组"))
            {
                CommitAnyEdit();
                state.DeleteSelectedActionGroup();
                GUI.FocusControl(null);
            }
        }

        if (GUI.Button(addActionRect, "新建"))
        {
            CommitAnyEdit();
            state.AddAction();
            GUI.FocusControl(null);
        }

        using (new EditorGUI.DisabledScope(state.IsActionGroupSelected()))
        {
            if (GUI.Button(duplicateRect, "复制"))
            {
                CommitAnyEdit();
                state.DuplicateAction();
                GUI.FocusControl(null);
            }

            if (GUI.Button(deleteRect, "删除"))
            {
                CommitAnyEdit();
                state.DeleteAction();
                GUI.FocusControl(null);
            }
        }
    }

    private void DrawSearchRow(Rect rect)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        Rect labelRect = new Rect(rect.x, rect.y + 2f, 34f, rect.height - 4f);
        Rect fieldRect = new Rect(labelRect.xMax + 4f, rect.y + 1f, Mathf.Max(20f, rect.xMax - labelRect.xMax - 4f), rect.height - 2f);

        GUI.Label(labelRect, "搜索", EditorStyles.miniLabel);
        using (new EditorGUI.DisabledScope(editingField != ActionEditField.None || editingGroupIndex >= 0))
        {
            GUI.SetNextControlName(SearchControl);
            state.Search = EditorGUI.TextField(fieldRect, state.Search);
        }
    }

    private void DrawScrollableActionRows()
    {
        // 这个面板通常和“结构 / 图层”面板同处左侧区域。
        // 列表自己已经有内层滚动条，所以这里不要再向父级 GUILayout 请求 ExpandHeight。
        // 否则父级会认为本面板需要一个非常高的内容区，从而生成第二层外部滚动条。
        float contentHeight = CalculateActionListContentHeight();
        float viewHeight = Mathf.Clamp(contentHeight, ActionRowsMinHeight, ActionRowsMaxHeight);

        currentScrollViewRect = GUILayoutUtility.GetRect(
            0f,
            100000f,
            viewHeight,
            viewHeight,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(false));

        Rect contentRect = new Rect(0f, 0f, Mathf.Max(1f, currentScrollViewRect.width - 16f), Mathf.Max(contentHeight, currentScrollViewRect.height));

        HandleInnerScrollWheel(currentScrollViewRect, contentRect.height);

        state.ActionListScroll = GUI.BeginScrollView(
            currentScrollViewRect,
            state.ActionListScroll,
            contentRect,
            false,
            true);

        float y = 0f;
        for (int g = 0; g < state.ActionGroups.Count; g++)
        {
            SkyPrisonAnimationActionGroupRow group = state.ActionGroups[g];
            if (group == null) continue;

            Rect groupRect = new Rect(0f, y, contentRect.width, GroupHeaderHeight);
            DrawGroupRow(groupRect, g, group);
            y += GroupHeaderHeight;

            if (!group.expanded) continue;

            for (int i = 0; i < state.Actions.Count; i++)
            {
                SkyPrisonAnimationActionRow row = state.Actions[i];
                if (row == null) continue;
                if (!string.Equals(row.groupKey, group.key, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (editingActionIndex != i && !PassActionSearch(row)) continue;

                Rect actionRect = new Rect(0f, y, contentRect.width, SkyPrisonAnimationWorkbenchState.RowHeight);
                DrawActionRow(actionRect, i, row);
                y += SkyPrisonAnimationWorkbenchState.RowHeight;
            }
        }

        GUI.EndScrollView();
    }

    private float CalculateActionListContentHeight()
    {
        float h = 0f;
        for (int g = 0; g < state.ActionGroups.Count; g++)
        {
            SkyPrisonAnimationActionGroupRow group = state.ActionGroups[g];
            if (group == null) continue;
            h += GroupHeaderHeight;
            if (!group.expanded) continue;

            for (int i = 0; i < state.Actions.Count; i++)
            {
                SkyPrisonAnimationActionRow row = state.Actions[i];
                if (row == null) continue;
                if (!string.Equals(row.groupKey, group.key, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (editingActionIndex != i && !PassActionSearch(row)) continue;
                h += SkyPrisonAnimationWorkbenchState.RowHeight;
            }
        }
        return Mathf.Max(h, 1f);
    }

    private void HandleInnerScrollWheel(Rect viewRect, float contentHeight)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.ScrollWheel) return;
        if (!viewRect.Contains(e.mousePosition)) return;

        float maxY = Mathf.Max(0f, contentHeight - viewRect.height);
        Vector2 scroll = state.ActionListScroll;
        scroll.y = Mathf.Clamp(scroll.y + e.delta.y * 18f, 0f, maxY);
        state.ActionListScroll = scroll;

        // 关键：吞掉内层滚轮事件，外层窗口/面板的 ScrollView 就不会同步滚动。
        e.Use();
        GUI.changed = true;
    }

    private Rect ContentToScreenRect(Rect contentRect)
    {
        if (currentScrollViewRect.width <= 0f || currentScrollViewRect.height <= 0f)
            return contentRect;

        return new Rect(
            currentScrollViewRect.x + contentRect.x - state.ActionListScroll.x,
            currentScrollViewRect.y + contentRect.y - state.ActionListScroll.y,
            contentRect.width,
            contentRect.height);
    }

    private void DrawGroupRow(Rect r, int index, SkyPrisonAnimationActionGroupRow group)
    {
        bool selected = state.ActionGroupSelectionActive && state.SelectedActionGroup == index;
        bool hover = r.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(r, selected ? groupHeaderSelectedBg : groupHeaderBg);
        if (hover) EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.045f));
        EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), selected ? groupAccent : new Color(groupAccent.r, groupAccent.g, groupAccent.b, 0.38f));
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), groupSeparator);

        Rect foldRect = new Rect(r.x + 7f, r.y + 2f, 18f, r.height - 4f);
        Rect nameRect = new Rect(foldRect.xMax + 4f, r.y + 2f, r.width - 34f, r.height - 4f);
        GUIStyle foldStyle = GetGroupFoldoutStyle(selected);
        GUIStyle nameStyle = GetGroupLabelStyle(selected);
        GUI.Label(foldRect, group.expanded ? "▾" : "▸", foldStyle);

        bool editingThisGroup = editingGroupIndex == index;
        if (editingThisGroup)
        {
            GUI.SetNextControlName(EditGroupNameControl);
            editingGroupNameBuffer = EditorGUI.TextField(nameRect, editingGroupNameBuffer);
            currentGroupEditingRect = nameRect;
            currentGroupEditingScreenRect = ContentToScreenRect(nameRect);
            if (requestFocusGroupEditField && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl(EditGroupNameControl);
                requestFocusGroupEditField = false;
            }
        }
        else
        {
            GUI.Label(nameRect, string.IsNullOrWhiteSpace(group.name) ? "未命名动作组" : group.name, nameStyle);
        }

        Event e = Event.current;
        if (e != null && e.type == EventType.MouseDown && r.Contains(e.mousePosition))
        {
            if (e.button == 1)
            {
                ShowGroupContextMenu(index);
                e.Use();
                return;
            }

            if (e.button != 0)
                return;

            if (editingThisGroup && !currentGroupEditingRect.Contains(e.mousePosition))
            {
                CommitGroupEdit();
                GUI.FocusControl(null);
                e.Use();
                return;
            }

            CommitActionEdit();
            state.SelectedActionGroup = index;
            state.ActionGroupSelectionActive = true;
            state.SelectedTimelineKeyframeIndex = -1;
            state.SelectedMotionKeyframeIndex = -1;
            state.PreviewPlaying = false;

            if (nameRect.Contains(e.mousePosition) && e.clickCount >= 2)
            {
                BeginGroupEdit(index);
                e.Use();
                return;
            }

            if (foldRect.Contains(e.mousePosition))
                group.expanded = !group.expanded;

            GUI.changed = true;
            e.Use();
        }
    }

    private void DrawActionRow(Rect r, int index, SkyPrisonAnimationActionRow row)
    {
        r = new Rect(r.x + ActionChildIndent, r.y, r.width - ActionChildIndent, r.height);
        bool selected = !state.ActionGroupSelectionActive && state.SelectedAction == index;
        bool hover = r.Contains(Event.current.mousePosition);
        bool editingThisRow = editingActionIndex == index && editingField != ActionEditField.None;

        if (selected) EditorGUI.DrawRect(r, SkyPrisonAnimationWorkbenchStyle.SelectedBg);
        else if (hover) EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.06f));

        EditorGUI.DrawRect(new Rect(r.x - 8f, r.y + 3f, 1f, r.height - 6f), new Color(1f, 1f, 1f, selected ? 0.18f : 0.08f));

        Rect handleRect = new Rect(r.x + 2f, r.y + 2f, DragHandleWidth - 4f, r.height - 4f);
        Rect statusRect = new Rect(r.xMax - StatusWidth - 4f, r.y + 2f, StatusWidth, r.height - 4f);
        Rect keyRect = new Rect(statusRect.x - KeyFieldWidth - 4f, r.y + 2f, KeyFieldWidth, r.height - 4f);
        Rect nameRect = new Rect(handleRect.xMax + 4f, r.y + 2f, keyRect.x - handleRect.xMax - 8f, r.height - 4f);

        GUI.Label(handleRect, "•", selected ? GetSelectedMiniLabelStyle() : EditorStyles.miniLabel);
        if (editingThisRow) DrawEditingFields(row, nameRect, keyRect);
        else DrawReadOnlyFields(row, nameRect, keyRect, selected);
        GUI.Label(statusRect, row.status, EditorStyles.miniLabel);
        HandleRowMouseInput(r, nameRect, keyRect, index, selected, editingThisRow);
    }

    private void DrawReadOnlyFields(SkyPrisonAnimationActionRow row, Rect nameRect, Rect keyRect, bool selected)
    {
        string name = string.IsNullOrEmpty(row.name) ? "未命名动作" : row.name;
        string key = string.IsNullOrEmpty(row.key) ? "-" : row.key;
        GUI.Label(nameRect, name, selected ? GetSelectedLabelStyle() : EditorStyles.label);
        GUI.Label(keyRect, "[" + key + "]", selected ? GetSelectedMiniLabelStyle() : EditorStyles.miniLabel);
    }

    private void DrawEditingFields(SkyPrisonAnimationActionRow row, Rect nameRect, Rect keyRect)
    {
        bool editingName = editingField == ActionEditField.Name;
        bool editingKey = editingField == ActionEditField.Key;
        if (editingName) { GUI.SetNextControlName(EditNameControl); editingNameBuffer = EditorGUI.TextField(nameRect, editingNameBuffer); currentEditingRect = nameRect; currentEditingScreenRect = ContentToScreenRect(nameRect); }
        else GUI.Label(nameRect, string.IsNullOrEmpty(row.name) ? "未命名动作" : row.name, GetSelectedLabelStyle());
        if (editingKey) { GUI.SetNextControlName(EditKeyControl); editingKeyBuffer = EditorGUI.TextField(keyRect, editingKeyBuffer); currentEditingRect = keyRect; currentEditingScreenRect = ContentToScreenRect(keyRect); }
        else GUI.Label(keyRect, "[" + (string.IsNullOrEmpty(row.key) ? "-" : row.key) + "]", GetSelectedMiniLabelStyle());
        if (requestFocusEditField && Event.current.type == EventType.Repaint) { EditorGUI.FocusTextInControl(editingName ? EditNameControl : EditKeyControl); requestFocusEditField = false; }
    }

    private void HandleRowMouseInput(Rect rowRect, Rect nameRect, Rect keyRect, int index, bool selected, bool editingThisRow)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || !rowRect.Contains(e.mousePosition)) return;
        if (e.button == 1) { ShowActionContextMenu(index); e.Use(); return; }
        if (e.button != 0) return;
        if (e.clickCount >= 2 && nameRect.Contains(e.mousePosition)) { BeginActionEdit(index, ActionEditField.Name); e.Use(); return; }
        if (e.clickCount >= 2 && keyRect.Contains(e.mousePosition)) { BeginActionEdit(index, ActionEditField.Key); e.Use(); return; }
        if (!selected) { CommitAnyEdit(); state.SelectActionAndRefresh(index); state.SelectedActionGroup = Mathf.Max(0, state.FindActionGroupIndex(state.Actions[index].groupKey)); GUI.FocusControl(null); e.Use(); return; }
        if (editingThisRow && !currentEditingRect.Contains(e.mousePosition)) { CommitActionEdit(); GUI.FocusControl(null); e.Use(); return; }
        e.Use();
    }

    private void ShowActionContextMenu(int index)
    {
        if (index < 0 || index >= state.Actions.Count) return;
        CommitAnyEdit(); state.SelectActionAndRefresh(index); GUI.FocusControl(null);
        SkyPrisonAnimationActionRow row = state.Actions[index];
        string actionName = row != null && !string.IsNullOrWhiteSpace(row.name) ? row.name : "当前动作";
        GenericMenu menu = new GenericMenu();
        if (state.Actions.Count > 1)
        {
            menu.AddItem(new GUIContent("删除动作"), false, () =>
            {
                bool ok = EditorUtility.DisplayDialog("删除动作", "确定删除动作「" + actionName + "」吗？\n\n该动作下的时间线关键帧、Motion关键帧和图层顺序关键帧也会一起删除。", "删除", "取消");
                if (!ok) return;
                state.DeleteActionAt(index); GUI.changed = true;
            });
        }
        else menu.AddDisabledItem(new GUIContent("删除动作/至少保留一个动作"));
        menu.ShowAsContext();
    }

    private void BeginActionEdit(int index, ActionEditField field)
    {
        if (index < 0 || index >= state.Actions.Count) return;
        CommitGroupEdit();
        state.SelectActionAndRefresh(index);
        SkyPrisonAnimationActionRow row = state.Actions[index];
        editingActionIndex = index; editingField = field;
        editingNameBuffer = row != null ? (row.name ?? string.Empty) : string.Empty;
        editingKeyBuffer = row != null ? (row.key ?? string.Empty) : string.Empty;
        requestFocusEditField = true;
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = true;
        GUI.changed = true;
    }

    private void CommitActionEdit()
    {
        if (editingActionIndex < 0 || editingActionIndex >= state.Actions.Count || editingField == ActionEditField.None) { ClearActionEdit(); return; }
        int index = editingActionIndex; ActionEditField field = editingField; string name = editingNameBuffer; string key = editingKeyBuffer;
        ClearActionEdit();
        if (field == ActionEditField.Name) state.RenameActionName(index, name);
        else if (field == ActionEditField.Key) state.RenameActionKey(index, key);
        GUI.changed = true;
    }

    private void ClearActionEdit(){ editingActionIndex=-1; editingField=ActionEditField.None; editingNameBuffer=string.Empty; editingKeyBuffer=string.Empty; requestFocusEditField=false; currentEditingRect=default(Rect); currentEditingScreenRect=default(Rect); }

    private void BeginGroupEdit(int index)
    {
        if (index < 0 || index >= state.ActionGroups.Count) return;
        CommitActionEdit();
        state.SelectedActionGroup = index;
        state.ActionGroupSelectionActive = true;
        editingGroupIndex = index;
        SkyPrisonAnimationActionGroupRow group = state.ActionGroups[index];
        editingGroupNameBuffer = group != null ? (group.name ?? string.Empty) : string.Empty;
        requestFocusGroupEditField = true;
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = true;
        GUI.changed = true;
    }

    private void CommitGroupEdit()
    {
        if (editingGroupIndex < 0 || editingGroupIndex >= state.ActionGroups.Count)
        {
            ClearGroupEdit();
            return;
        }

        int index = editingGroupIndex;
        string nextName = string.IsNullOrWhiteSpace(editingGroupNameBuffer) ? "动作组" : editingGroupNameBuffer.Trim();
        ClearGroupEdit();

        SkyPrisonAnimationActionGroupRow group = state.ActionGroups[index];
        if (group == null) return;
        if (group.name == nextName) return;

        state.PushStructureUndo();
        group.name = nextName;
        GUI.changed = true;
    }

    private void ClearGroupEdit()
    {
        editingGroupIndex = -1;
        editingGroupNameBuffer = string.Empty;
        requestFocusGroupEditField = false;
        currentGroupEditingRect = default(Rect);
        currentGroupEditingScreenRect = default(Rect);
    }

    private void CommitAnyEdit()
    {
        CommitActionEdit();
        CommitGroupEdit();
    }

    private void HandleGlobalEditCommitBeforeControls()
    {
        bool editingAction = editingField != ActionEditField.None;
        bool editingGroup = editingGroupIndex >= 0;
        if (!editingAction && !editingGroup) return;

        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0) return;
        if (editingAction && currentEditingScreenRect.Contains(e.mousePosition)) return;
        if (editingGroup && currentGroupEditingScreenRect.Contains(e.mousePosition)) return;

        // 外部点击只用于提交当前重命名，不再把同一次点击继续传给“搜索”等控件。
        // 否则会出现：想保存别名/组名，结果焦点跳到搜索框，看起来像保存失败。
        CommitAnyEdit();
        GUI.FocusControl(null);
        e.Use();
    }

    private void HandleEditKeyboard()
    {
        bool editingAction = editingField != ActionEditField.None;
        bool editingGroup = editingGroupIndex >= 0;
        if (!editingAction && !editingGroup) return;

        Event e = Event.current;
        if(e==null||e.type!=EventType.KeyDown)return;
        if(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter)
        {
            CommitAnyEdit();
            GUI.FocusControl(null);
            e.Use();
        }
        else if(e.keyCode==KeyCode.Escape)
        {
            ClearActionEdit();
            ClearGroupEdit();
            GUI.FocusControl(null);
            GUI.changed=true;
            e.Use();
        }
    }

    private void ShowGroupContextMenu(int index)
    {
        if (index < 0 || index >= state.ActionGroups.Count) return;
        CommitAnyEdit();
        state.SelectedActionGroup = index;
        state.ActionGroupSelectionActive = true;
        GUI.FocusControl(null);

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("重命名动作组"), false, () =>
        {
            BeginGroupEdit(index);
            GUI.changed = true;
        });

        if (state.ActionGroups.Count > 1)
        {
            menu.AddItem(new GUIContent("删除动作组"), false, () =>
            {
                bool ok = EditorUtility.DisplayDialog("删除动作组", "确定删除动作组「" + (state.ActionGroups[index].name ?? "动作组") + "」吗？\n\n组内动作会移动到上一个动作组，不会删除动作本身。", "删除", "取消");
                if (!ok) return;
                state.SelectedActionGroup = index;
                state.DeleteSelectedActionGroup();
                GUI.changed = true;
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("删除动作组/至少保留一个组"));
        }

        menu.ShowAsContext();
    }
    private bool PassActionSearch(SkyPrisonAnimationActionRow row)
    {
        if (row == null) return false;
        string s = state.Search;
        if (string.IsNullOrWhiteSpace(s)) return true;
        s = s.Trim().ToLowerInvariant();
        return SafeContains(row.key, s) || SafeContains(row.name, s) || SafeContains(row.type, s) || SafeContains(row.status, s);
    }

    private bool SafeContains(string value, string search)
    {
        return !string.IsNullOrEmpty(value) && value.ToLowerInvariant().Contains(search);
    }

    private GUIStyle GetSelectedLabelStyle(){ GUIStyle style=new GUIStyle(EditorStyles.label); style.normal.textColor=Color.white; return style; }
    private GUIStyle GetSelectedMiniLabelStyle(){ GUIStyle style=new GUIStyle(EditorStyles.miniLabel); style.normal.textColor=new Color(0.92f,0.92f,0.94f,1f); return style; }
    private GUIStyle GetGroupLabelStyle(bool selected)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.fontSize = 12;
        style.alignment = TextAnchor.MiddleLeft;
        style.normal.textColor = selected ? new Color(1f, 0.86f, 0.58f, 1f) : new Color(0.92f, 0.88f, 0.78f, 1f);
        return style;
    }
    private GUIStyle GetGroupFoldoutStyle(bool selected)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = selected ? new Color(1f, 0.86f, 0.58f, 1f) : new Color(0.82f, 0.78f, 0.70f, 1f);
        return style;
    }
}
