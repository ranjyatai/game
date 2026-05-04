
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SkyPrisonAnimationStructurePanel
{
    private const string DragPayloadKey = "SkyPrisonAnimationStructurePanel.Rows";

    private readonly List<SkyPrisonAnimationRigRow> clipboardRows = new List<SkyPrisonAnimationRigRow>();
    private SkyPrisonAnimationStructureTab clipboardTab = SkyPrisonAnimationStructureTab.Rig;

    private readonly SkyPrisonAnimationWorkbenchState state;

    private int renamingIndex = -1;
    private string renamingBuffer = string.Empty;
    private string renamingControlName = string.Empty;

    // 键盘快捷键焦点保护：只有最近一次点击发生在结构面板内，结构面板才吃 Delete/Ctrl+C 等快捷键。
    // 防止用户在时间线秒数、检查器数值框等文本/数字输入中按 Delete 时误删 Rig/PSB 图层。
    private bool hasStructureKeyboardFocus = false;

    // 动作参数滑动条拖动合并 Undo：一次按下/拖动/松开只产生一条撤销记录。
    private string activeManualAngleSliderKey = null;
    private object activeManualAngleSliderUndoSnapshot = null;
    private bool activeManualAngleSliderChanged = false;
    private bool activeManualAngleSliderUndoPushed = false;

    public SkyPrisonAnimationStructurePanel(SkyPrisonAnimationWorkbenchState state)
    {
        this.state = state;
    }

    public void DrawInRect(Rect rect)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        UpdateKeyboardFocusFromMouse(rect);

        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        const float pad = 4f;
        float fixedHeaderHeight = GetFixedHeaderHeight();
        fixedHeaderHeight = Mathf.Min(fixedHeaderHeight, Mathf.Max(1f, rect.height - 24f));

        Rect fixedRect = new Rect(rect.x + pad, rect.y + pad, Mathf.Max(1f, rect.width - pad * 2f), fixedHeaderHeight);
        Rect bodyRect = new Rect(rect.x + pad, fixedRect.yMax + 2f, Mathf.Max(1f, rect.width - pad * 2f), Mathf.Max(1f, rect.yMax - fixedRect.yMax - pad - 2f));

        GUILayout.BeginArea(fixedRect);
        EditorGUILayout.BeginVertical();
        DrawTopToolbar();
        DrawTabButtons();
        if (!state.StructureAngleEditMode)
        {
            DrawTabToolbar();
            GUI.SetNextControlName("结构搜索");
            state.StructureSearch = EditorGUILayout.TextField("查找", state.StructureSearch);
            DrawHeader();
        }
        EditorGUILayout.EndVertical();
        GUILayout.EndArea();

        EditorGUI.DrawRect(bodyRect, new Color(0f, 0f, 0f, 0.08f));

        GUILayout.BeginArea(bodyRect);
        if (state.StructureAngleEditMode)
        {
            DrawManualAngleEditor(new Rect(0f, 0f, bodyRect.width, bodyRect.height));
        }
        else
        {
            state.StructureScroll = EditorGUILayout.BeginScrollView(state.StructureScroll, true, true);
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(500f));
            DrawRowsOnly();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        GUILayout.EndArea();

        if (!state.StructureAngleEditMode)
            HandleBlankAreaSelectionClear(bodyRect);
    }

    public void Draw()
    {
        EditorGUILayout.BeginVertical("box");
        DrawTopToolbar();
        DrawTabButtons();
        if (state.StructureAngleEditMode)
            DrawManualAngleEditor(new Rect(0f, 0f, 520f, 520f));
        else
        {
            DrawTabToolbar();
            GUI.SetNextControlName("结构搜索");
            state.StructureSearch = EditorGUILayout.TextField("查找", state.StructureSearch);
            DrawHeader();
            DrawRowsOnly();
        }
        EditorGUILayout.EndVertical();
    }

    private float GetFixedHeaderHeight()
    {
        if (state != null && state.StructureAngleEditMode)
            return 24f + 23f + 8f;

        // 顶部工具条 + 页签 + 绑定工具条 + 搜索 + 表头。
        return 24f + 23f + 24f + 22f + SkyPrisonAnimationWorkbenchState.RowHeight + 10f;
    }

    private bool IsManualCustomRigMode()
    {
        return state != null &&
               (state.ManualRigTemplateMode ||
                string.Equals(state.CurrentRigTemplateKey, "Custom", System.StringComparison.OrdinalIgnoreCase));
    }

    private void EnterRigTabKeepingEmptyRigEditable()
    {
        state.StructureTab = SkyPrisonAnimationStructureTab.Rig;
        state.ShowRigLines = true;
        state.SelectedRig = state.RigRows.Count > 0
            ? Mathf.Clamp(state.SelectedRig, 0, state.RigRows.Count - 1)
            : -1;

        if (IsManualCustomRigMode())
            state.ShowRigEdit = true;
    }

    private void DrawTabButtons()
    {
        EditorGUILayout.BeginHorizontal();

        int current = state.StructureAngleEditMode ? 3 :
            (state.StructureTab == SkyPrisonAnimationStructureTab.Rig ? 0 :
            (state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer ? 1 : 2));

        int next = GUILayout.Toolbar(current, new string[] { "Rig部件", "PSB图层", "Socket", "动作参数" }, EditorStyles.toolbarButton);

        if (next != current)
        {
            state.StructureAngleEditMode = next == 3;
            if (next == 0)
            {
                EnterRigTabKeepingEmptyRigEditable();
            }
            else if (next == 1)
            {
                state.StructureTab = SkyPrisonAnimationStructureTab.PsbLayer;
                state.ShowRigEdit = false;
            }
            else if (next == 2)
            {
                state.StructureTab = SkyPrisonAnimationStructureTab.Socket;
                state.ShowRigEdit = false;
            }
            else
            {
                // 动作参数编辑属于“动画姿势”层，不允许同时处在骨骼编辑模式。
                // 否则用户拖角度时容易误以为在调动作，实际却改到了 Rest Pose / 骨架结构。
                state.StructureTab = SkyPrisonAnimationStructureTab.Rig;
                state.ShowRigEdit = false;
                state.ShowRigLines = true;
            }

            GUI.FocusControl(null);
            GUI.changed = true;
        }

        EditorGUILayout.EndHorizontal();
    }


    private void DrawManualAngleEditor(Rect bodyRect)
    {
        // 注意：这里只在切换进入动作参数页、或真正拖动/生成动作参数时关闭骨骼编辑。
        // 不能在每帧持续强制 ShowRigEdit=false，否则回到 Rig 部件页后会出现骨骼编辑模式打不开。

        const float pad = 8f;
        const float gap = 6f;

        // 动作参数页的数值必须跟随当前时间线白线帧。
        // 若当前帧没有姿势点/精确 RigAngle 关键帧，则这里会自动归零到 Rest Pose。
        state.SyncManualAnglesFromCurrentFrame(false);
        List<SkyPrisonAnimationRigRow> rows = state.GetManualAngleTargetRows();

        float x = pad;
        float y = pad;
        float w = Mathf.Max(320f, bodyRect.width - pad * 2f);

        SkyPrisonAnimationActionRow action = state.CurrentAction();
        GUI.Label(new Rect(x, y, w, 18f), "全身姿势点 / 节点角度", EditorStyles.boldLabel);
        y += 22f;
        GUI.Label(new Rect(x, y, w, 18f), string.Format("当前动作：{0} [{1}]    当前帧：{2} / {3}", action.name, action.key, state.TimelineCurrentFrame, state.TimelineTotalFrames), EditorStyles.miniLabel);
        y += 22f;
        GUI.Label(new Rect(x, y, w, 18f), string.Format("姿势点：{0} 个。保存多个全身姿势点后，一键生成当前动作关键帧。", state.ManualPoseKeys.Count), EditorStyles.miniLabel);
        y += 24f;

        state.ManualAngleReplaceExisting = GUI.Toggle(new Rect(x, y, w, 20f), state.ManualAngleReplaceExisting, "生成时覆盖同帧同节点关键帧");
        y += 26f;

        float buttonGap = 5f;
        float buttonW = (w - buttonGap * 2f) / 3f;
        if (GUI.Button(new Rect(x, y, buttonW, 24f), "保存为姿势点"))
        {
            state.ShowRigEdit = false;
            state.SaveCurrentManualPoseKey();
            GUI.changed = true;
        }
        if (GUI.Button(new Rect(x + buttonW + buttonGap, y, buttonW, 24f), "更新姿势点"))
        {
            state.ShowRigEdit = false;
            state.UpdateSelectedManualPoseKey();
            GUI.changed = true;
        }
        if (GUI.Button(new Rect(x + (buttonW + buttonGap) * 2f, y, buttonW, 24f), "生成当前动作"))
        {
            state.ShowRigEdit = false;
            state.GenerateManualPoseKeysToCurrentAction();
            GUI.changed = true;
        }
        y += 30f;

        buttonW = (w - buttonGap * 2f) / 3f;
        if (GUI.Button(new Rect(x, y, buttonW, 22f), "删除姿势点"))
        {
            state.DeleteSelectedManualPoseKey();
            GUI.changed = true;
        }
        if (GUI.Button(new Rect(x + buttonW + buttonGap, y, buttonW, 22f), "清空姿势点"))
        {
            state.ClearManualPoseKeys();
            GUI.changed = true;
        }
        if (GUI.Button(new Rect(x + (buttonW + buttonGap) * 2f, y, buttonW, 22f), "角度归零"))
        {
            state.ResetManualBoneAngles();
            GUI.changed = true;
        }
        y += 30f;

        float poseListHeight = Mathf.Min(132f, Mathf.Max(78f, state.ManualPoseKeys.Count * 24f + 28f));
        DrawManualPoseKeyList(x, ref y, w, poseListHeight);

        DrawMotionArcTool(x, ref y, w);

        GUI.Label(new Rect(x, y, w, 38f), "调下方全身节点角度会实时预览当前姿势。调整好后保存为姿势点；多个姿势点会一起投射到当前动作的所有参与骨骼轨道。", EditorStyles.wordWrappedMiniLabel);
        y += 44f;

        // 参数量以后会越来越多，所以这里必须是独立固定容器：
        // 上面的姿势点和工具按钮保持不动，只有下方骨骼参数列表自己滚动。
        Rect parameterBox = new Rect(x, y, w, Mathf.Max(86f, bodyRect.height - y - pad));
        DrawManualBoneAngleParameterContainer(parameterBox, rows);

        EndManualAngleSliderUndoIfMouseReleased();
    }

    private void DrawMotionArcTool(float x, ref float y, float w)
    {
        float h = state.MotionArcToolExpanded ? 214f : 30f;
        Rect box = new Rect(x, y, w, h);
        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.16f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(box, new Color(1f, 1f, 1f, 0.06f));

        Rect foldRect = new Rect(box.x + 6f, box.y + 5f, box.width - 12f, 20f);
        state.MotionArcToolExpanded = EditorGUI.Foldout(foldRect, state.MotionArcToolExpanded, "弧线补间工具", true, EditorStyles.foldout);

        if (!state.MotionArcToolExpanded)
        {
            y += h + 8f;
            return;
        }

        float yy = box.y + 30f;
        float labelW = 70f;
        float fieldX = box.x + labelW + 10f;
        float fieldW = box.width - labelW - 18f;

        GUI.Label(new Rect(box.x + 8f, yy, labelW, 18f), "生成对象", EditorStyles.miniLabel);
        GUI.Label(new Rect(fieldX, yy, fieldW, 18f), state.GetMotionArcTargetLabel(), EditorStyles.miniLabel);
        yy += 22f;

        GUI.Label(new Rect(box.x + 8f, yy, labelW, 18f), "补间数量", EditorStyles.miniLabel);
        state.MotionArcTweenCount = EditorGUI.IntSlider(new Rect(fieldX, yy, fieldW, 18f), state.MotionArcTweenCount, 1, 24);
        yy += 24f;

        GUI.Label(new Rect(box.x + 8f, yy, labelW, 18f), "缓动感觉", EditorStyles.miniLabel);
        string[] easeLabels = { "线性", "平滑", "柔和", "有弹性" };
        int easeIndex = Mathf.Clamp((int)state.MotionArcEasePreset, 0, easeLabels.Length - 1);
        easeIndex = GUI.Toolbar(new Rect(fieldX, yy, fieldW, 20f), easeIndex, easeLabels, EditorStyles.toolbarButton);
        state.MotionArcEasePreset = (SkyPrisonMotionArcEasePreset)easeIndex;
        yy += 26f;

        using (new EditorGUI.DisabledScope(state.MotionArcEasePreset == SkyPrisonMotionArcEasePreset.Linear))
        {
            GUI.Label(new Rect(box.x + 8f, yy, labelW, 18f), "缓动量", EditorStyles.miniLabel);
            state.MotionArcEaseAmount = EditorGUI.Slider(new Rect(fieldX, yy, fieldW, 18f), state.MotionArcEaseAmount, 0f, 1f);
        }
        yy += 24f;

        state.MotionArcOverwriteInnerKeys = GUI.Toggle(new Rect(box.x + 8f, yy, box.width - 16f, 18f), state.MotionArcOverwriteInnerKeys, "覆盖区间中间旧 Key");
        yy += 22f;

        Rect infoRect = new Rect(box.x + 8f, yy, box.width - 16f, 18f);
        GUI.Label(infoRect, state.GetMotionArcRangeLabel(), EditorStyles.miniLabel);
        yy += 22f;

        Rect buttonRect = new Rect(box.x + 8f, yy, box.width - 16f, 24f);
        using (new EditorGUI.DisabledScope(!state.CanGenerateMotionArcKeys()))
        {
            if (GUI.Button(buttonRect, "生成中间帧"))
            {
                state.ShowRigEdit = false;
                state.GenerateMotionArcKeysForActiveTrack();
                GUI.changed = true;
            }
        }

        y += h + 8f;
    }

    private void DrawManualBoneAngleParameterContainer(Rect box, List<SkyPrisonAnimationRigRow> rows)
    {
        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.16f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(box, new Color(1f, 1f, 1f, 0.06f));

        GUI.Label(new Rect(box.x + 6f, box.y + 5f, box.width - 12f, 18f), "骨骼参数", EditorStyles.miniBoldLabel);

        Rect listView = new Rect(box.x + 4f, box.y + 26f, box.width - 8f, Mathf.Max(24f, box.height - 30f));
        float contentHeight = Mathf.Max(listView.height, rows.Count * 48f + 4f);
        Rect content = new Rect(0f, 0f, Mathf.Max(10f, listView.width - 14f), contentHeight);

        state.ManualAngleParameterScroll = GUI.BeginScrollView(listView, state.ManualAngleParameterScroll, content, false, true);

        float rowX = 0f;
        float rowY = 2f;
        float rowW = content.width;
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null) continue;
            DrawManualBoneAngleRowCompact(rowX, ref rowY, rowW, row, i);
        }

        GUI.EndScrollView();
    }

    private void DrawManualPoseKeyList(float x, ref float y, float w, float h)
    {
        Rect box = new Rect(x, y, w, h);
        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.16f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(box, new Color(1f, 1f, 1f, 0.06f));

        GUI.Label(new Rect(x + 6f, y + 5f, w - 12f, 18f), "全身姿势点", EditorStyles.miniBoldLabel);
        Rect listView = new Rect(x + 4f, y + 26f, w - 8f, h - 30f);
        float contentH = Mathf.Max(listView.height, state.ManualPoseKeys.Count * 24f);
        Rect content = new Rect(0f, 0f, Mathf.Max(10f, listView.width - 14f), contentH);
        state.ManualPoseListScroll = GUI.BeginScrollView(listView, state.ManualPoseListScroll, content, false, true);

        for (int i = 0; i < state.ManualPoseKeys.Count; i++)
        {
            SkyPrisonManualPoseKey pose = state.ManualPoseKeys[i];
            if (pose == null) continue;
            Rect rowRect = new Rect(0f, i * 24f, content.width, 22f);
            bool selected = state.SelectedManualPoseKeyIndex == i;
            if (selected)
                EditorGUI.DrawRect(rowRect, new Color(0.28f, 0.20f, 0.10f, 1f));
            else if (rowRect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.04f));

            string label = string.Format("{0}帧   {1}", pose.frame, string.IsNullOrEmpty(pose.label) ? "姿势点" : pose.label);
            GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, 18f), label, EditorStyles.miniLabel);
            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
            {
                state.LoadManualPoseKeyToParameters(i);
                GUI.changed = true;
            }
        }

        GUI.EndScrollView();
        y += h + 8f;
    }

    private void DrawManualBoneAngleRowCompact(float x, ref float y, float w, SkyPrisonAnimationRigRow row, int index)
    {
        Rect bg = new Rect(x, y, w, 44f);
        EditorGUI.DrawRect(bg, index % 2 == 0 ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.06f));

        string label = string.IsNullOrEmpty(row.name) ? row.key : row.name;
        if (!string.IsNullOrEmpty(row.key)) label += " [" + row.key + "]";

        float value = state.GetManualBoneAngle(row.key);
        Rect fieldRect = new Rect(x + w - 68f, y + 2f, 62f, 19f);
        Rect sliderRect = new Rect(x + 8f, y + 25f, w - 16f, 16f);

        GUI.Label(new Rect(x + 6f, y + 3f, w - 76f, 18f), label, EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        float fieldValue = EditorGUI.FloatField(fieldRect, value);
        if (EditorGUI.EndChangeCheck())
        {
            object undoSnapshot = state.CaptureStructureUndoSnapshot();
            ApplyManualAngleValue(row.key, fieldValue);
            state.PushCapturedStructureUndo(undoSnapshot);
        }

        value = state.GetManualBoneAngle(row.key);
        Event e = Event.current;
        if (e != null && e.type == EventType.MouseDown && e.button == 0 && sliderRect.Contains(e.mousePosition))
            BeginManualAngleSliderUndo(row.key);

        EditorGUI.BeginChangeCheck();
        float sliderValue = GUI.HorizontalSlider(sliderRect, value, -180f, 180f);
        if (EditorGUI.EndChangeCheck())
        {
            if (activeManualAngleSliderUndoSnapshot == null)
                BeginManualAngleSliderUndo(row.key);
            ApplyManualAngleValue(row.key, sliderValue);
            activeManualAngleSliderChanged = true;
            PushActiveManualAngleSliderUndoImmediatelyIfNeeded();
        }

        y += 48f;
    }

    private void ApplyManualAngleValue(string rigKey, float value)
    {
        state.ShowRigEdit = false;
        state.SetManualBoneAngle(rigKey, value);
        state.ApplyManualAngleLiveChange(rigKey);
        GUI.changed = true;
    }

    private void BeginManualAngleSliderUndo(string rigKey)
    {
        if (activeManualAngleSliderUndoSnapshot != null && activeManualAngleSliderKey == rigKey)
            return;

        EndManualAngleSliderUndo(true);
        activeManualAngleSliderKey = rigKey;
        activeManualAngleSliderUndoSnapshot = state.CaptureStructureUndoSnapshot();
        activeManualAngleSliderChanged = false;
        activeManualAngleSliderUndoPushed = false;
    }

    private void PushActiveManualAngleSliderUndoImmediatelyIfNeeded()
    {
        if (activeManualAngleSliderUndoPushed)
            return;

        if (activeManualAngleSliderUndoSnapshot == null)
            return;

        state.PushCapturedStructureUndo(activeManualAngleSliderUndoSnapshot);
        activeManualAngleSliderUndoPushed = true;
    }

    private void EndManualAngleSliderUndoIfMouseReleased()
    {
        Event e = Event.current;
        if (e == null) return;
        if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp || e.type == EventType.Ignore)
            EndManualAngleSliderUndo(true);
    }

    private void EndManualAngleSliderUndo(bool pushIfChanged)
    {
        if (activeManualAngleSliderUndoSnapshot != null && pushIfChanged && activeManualAngleSliderChanged && !activeManualAngleSliderUndoPushed)
            state.PushCapturedStructureUndo(activeManualAngleSliderUndoSnapshot);

        activeManualAngleSliderKey = null;
        activeManualAngleSliderUndoSnapshot = null;
        activeManualAngleSliderChanged = false;
        activeManualAngleSliderUndoPushed = false;
    }

    private void DrawRowsOnly()
    {
        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        for (int i = 0; i < rows.Count; i++)
        {
            if (!PassStructureSearch(rows[i]))
                continue;
            if (string.IsNullOrWhiteSpace(state.StructureSearch) && !IsVisibleByParentExpansion(i, rows))
                continue;

            DrawRow(rows[i], i, rows);
        }

        HandleKeyboard(rows);
    }

    private void HandleBlankAreaSelectionClear(Rect bodyRect)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
            return;

        if (state.StructureTab != SkyPrisonAnimationStructureTab.PsbLayer)
            return;

        if (!bodyRect.Contains(e.mousePosition))
            return;

        state.ClearCurrentStructureSelection(true);
        GUI.FocusControl(null);
        GUI.changed = true;
        e.Use();
    }

    private void DrawTabToolbar()
    {
        if (state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("自动绑定到Rig", GUILayout.Height(20f)))
            {
                state.PushStructureUndo();
                state.AutoBindPsbLayersToRig();
                GUI.changed = true;
            }

            using (new EditorGUI.DisabledScope(state.FindCurrentSelectedRigForBinding() == null || state.FindCurrentSelectedPsbForBinding() == null))
            {
                if (GUILayout.Button("绑定到选中Rig", GUILayout.Width(108f), GUILayout.Height(20f)))
                {
                    state.PushStructureUndo();
                    state.BindRememberedPsbToRememberedRig();
                    GUI.changed = true;
                }
            }

            if (GUILayout.Button("清空绑定", GUILayout.Width(76f), GUILayout.Height(20f)))
            {
                state.PushStructureUndo();
                state.ClearPsbLayerBindings();
                GUI.changed = true;
            }

            EditorGUILayout.EndHorizontal();
        }
        else if (state.StructureTab == SkyPrisonAnimationStructureTab.Rig)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(state.FindCurrentSelectedRigForBinding() == null || state.FindCurrentSelectedPsbForBinding() == null))
            {
                if (GUILayout.Button("绑定最近选中PSB图层", GUILayout.Height(20f)))
                {
                    state.PushStructureUndo();
                    state.BindRememberedPsbToRememberedRig();
                    GUI.changed = true;
                }
            }

            using (new EditorGUI.DisabledScope(state.GetSelectedRigRow() == null || state.GetSelectedRigRow().isFolder))
            {
                if (GUILayout.Button("解除Rig绑定", GUILayout.Width(92f), GUILayout.Height(20f)))
                {
                    state.PushStructureUndo();
                    state.UnbindRigFromPsb(state.GetSelectedRigRow());
                    GUI.changed = true;
                }
            }

            if (GUILayout.Button("刷新绑定预览", GUILayout.Width(96f), GUILayout.Height(20f)))
            {
                state.RefreshRigLinksFromPsbBindings();
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawTopToolbar()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 24f);

        GUI.Label(new Rect(r.x, r.y + 2f, 120f, r.height), "结构 / 图层", EditorStyles.boldLabel);

        const float buttonSize = 24f;
        const float gap = 4f;
        const float safeRight = 2f;

        float y = r.y + 1f;
        float right = r.xMax - safeRight;

        Rect deleteRect = new Rect(right - buttonSize, y, buttonSize, 22f);
        Rect folderRect = new Rect(deleteRect.x - gap - buttonSize, y, buttonSize, 22f);
        Rect meshRect = new Rect(folderRect.x - gap - buttonSize, y, buttonSize, 22f);
        Rect nodeRect = new Rect(meshRect.x - gap - buttonSize, y, buttonSize, 22f);

        DrawIconButton(nodeRect, 42, "新建节点", AddNode);
        DrawIconButton(folderRect, 45, "新建文件夹", AddFolder);
        DrawIconButton(meshRect, 47, "为选中节点生成曲面变形", OpenMeshDeformerPopup, CanCreateMeshDeformerForSelection());
        DrawIconButton(deleteRect, 23, "删除选中", DeleteSelected);
    }

    private void DrawHeader()
    {
        Rect r = EditorGUILayout.GetControlRect(false, SkyPrisonAnimationWorkbenchState.RowHeight);
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.04f));
        float x = r.x + 4f;

        if (state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer)
        {
            bool allVisible = AreAllPsbLayersVisible();
            Rect allVisibleRect = new Rect(x, r.y + 3f, 16f, 16f);
            GUIContent content = new GUIContent(
                SkyPrisonAnimationWorkbenchStyle.LoadEditorIcon(35),
                allVisible ? "隐藏全部PSB图层" : "显示全部PSB图层");

            Color oldColor = GUI.color;
            GUI.color = allVisible ? Color.white : new Color(1f, 1f, 1f, 0.42f);
            if (GUI.Button(allVisibleRect, content, GUIStyle.none))
            {
                state.PushStructureUndo();
                SetAllPsbLayersVisible(!allVisible);
                GUI.changed = true;
            }
            GUI.color = oldColor;
        }
        else
        {
            GUI.Label(new Rect(x, r.y + 2f, 20f, r.height), "显", EditorStyles.miniLabel);
        }
        x += 24f;

        GUI.Label(new Rect(x, r.y + 2f, 20f, r.height), "锁", EditorStyles.miniLabel); x += 24f;
        GUI.Label(new Rect(x, r.y + 2f, 20f, r.height), "键", EditorStyles.miniLabel); x += 24f;
        GUI.Label(new Rect(x, r.y + 2f, 22f, r.height), "映", EditorStyles.miniLabel); x += 28f;
        GUI.Label(new Rect(x, r.y + 2f, 32f, r.height), "蒙", EditorStyles.miniLabel);
        GUI.Label(new Rect(r.xMax - 132f, r.y + 2f, 128f, r.height), state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer ? "绑定 / 语义" : "语义 / 图层", EditorStyles.miniLabel);
    }

    private void DrawRow(SkyPrisonAnimationRigRow row, int index, List<SkyPrisonAnimationRigRow> rows)
    {
        Rect r = EditorGUILayout.GetControlRect(false, SkyPrisonAnimationWorkbenchState.RowHeight);
        bool selected = state.SelectedRigRows.Contains(index) || state.SelectedRig == index;

        if (row.isFolder)
            EditorGUI.DrawRect(r, new Color(0.28f, 0.24f, 0.12f, 0.55f));
        if (selected)
            EditorGUI.DrawRect(r, new Color(0.38f, 0.30f, 0.18f, 0.85f));
        else if (r.Contains(Event.current.mousePosition))
            EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.05f));

        DrawDropHintAndHandleDrop(r, index, rows);

        float x = r.x + 4f + row.depth * 14f;
        bool hasChildren = HasChildren(row, rows);
        Rect foldRect = new Rect(x, r.y + 3f, 14f, 14f);
        if (hasChildren)
        {
            if (GUI.Button(foldRect, row.expanded ? "▼" : "▶", GUIStyle.none)) row.expanded = !row.expanded;
        }
        x += 16f;

        Rect visRect = new Rect(x, r.y + 3f, 16f, 16f);
        if (GUI.Button(visRect, new GUIContent(SkyPrisonAnimationWorkbenchStyle.LoadEditorIcon(row.visible ? 35 : 36), row.visible ? "隐藏" : "显示"), GUIStyle.none))
        {
            state.PushStructureUndo();
            SetVisibleRecursive(row, rows, !row.visible);
            GUI.changed = true;
        }
        x += 24f;

        Rect lockRect = new Rect(x, r.y + 3f, 16f, 16f);
        if (row.locked)
        {
            if (GUI.Button(lockRect, new GUIContent(SkyPrisonAnimationWorkbenchStyle.LoadEditorIcon(38), "解锁"), GUIStyle.none))
            {
                state.PushStructureUndo();
                row.locked = false;
                GUI.changed = true;
            }
        }
        else
        {
            if (GUI.Button(lockRect, "-", GUIStyle.none))
            {
                state.PushStructureUndo();
                row.locked = true;
                GUI.changed = true;
            }
        }
        x += 24f;

        GUI.Label(new Rect(x, r.y + 2f, 18f, r.height), row.hasKey ? "◆" : "-", EditorStyles.miniLabel); x += 22f;
        GUI.Label(new Rect(x, r.y + 2f, 18f, r.height), row.mapped ? "✓" : "!", EditorStyles.miniLabel); x += 22f;

        if (!string.IsNullOrEmpty(row.maskReferenceKey) && !row.isFolder)
            GUI.DrawTexture(new Rect(x, r.y + 3f, 16f, 16f), SkyPrisonAnimationWorkbenchStyle.LoadEditorIcon(44), ScaleMode.ScaleToFit);
        else
            GUI.Label(new Rect(x, r.y + 2f, 18f, r.height), "-", EditorStyles.miniLabel);
        x += 22f;

        Rect previewRect = new Rect(x, r.y + 2f, 18f, 18f);
        DrawLayerPreviewThumbnail(previewRect, row);
        x += 24f;

        Rect semanticRect = new Rect(r.xMax - 132f, r.y, 128f, r.height);
        Rect nameRect = new Rect(x, r.y + 2f, Mathf.Max(20f, semanticRect.x - x - 6f), r.height);
        DrawEditableName(nameRect, row, index);

        EditorGUI.DrawRect(semanticRect, row.isFolder ? new Color(0.28f, 0.24f, 0.12f, 0.95f) : SkyPrisonAnimationWorkbenchStyle.PanelBg);
        GUI.Label(new Rect(semanticRect.x + 4f, semanticRect.y + 2f, semanticRect.width - 8f, semanticRect.height), BuildRightColumnLabel(row), EditorStyles.miniLabel);

        HandleRowInput(r, index, rows);
    }

    private string BuildRightColumnLabel(SkyPrisonAnimationRigRow row)
    {
        if (row == null) return "-";
        if (row.isFolder) return "文件夹";

        if (state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer)
        {
            string weightText = " W:" + row.psbLayerWeight.ToString("0");
            if (!string.IsNullOrEmpty(row.boundRigKey))
                return "→ " + (string.IsNullOrEmpty(row.boundRigName) ? row.boundRigKey : row.boundRigName) + weightText;
            return "未绑定 / " + row.semantic + weightText;
        }

        if (state.StructureTab == SkyPrisonAnimationStructureTab.Rig && !string.IsNullOrEmpty(row.sourceLayerPath))
            return "← " + row.sourceLayerPath;

        return row.semantic;
    }

    private void DrawLayerPreviewThumbnail(Rect rect, SkyPrisonAnimationRigRow row)
    {
        EditorGUI.DrawRect(rect, new Color(0.05f, 0.05f, 0.055f, 1f));
        DrawThinPreviewBorder(rect, new Color(1f, 1f, 1f, 0.12f));

        if (row == null) return;

        if (row.isFolder)
        {
            DrawFolderPreview(rect);
            return;
        }

        Rect inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);

        if (TryDrawSourceSpriteThumbnail(inner, row))
        {
            if (!row.visible)
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.45f));
            return;
        }

        Color c = ResolveLayerPreviewColor(row);
        string all = ((row.semantic ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.name ?? string.Empty)).ToLowerInvariant();

        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = c;

        if (all.Contains("arm") || all.Contains("leg") || all.Contains("臂") || all.Contains("腿"))
        {
            Handles.DrawAAPolyLine(3f, new Vector3(rect.x + rect.width * .25f, rect.y + rect.height * .78f), new Vector3(rect.x + rect.width * .72f, rect.y + rect.height * .24f));
            Handles.DrawSolidDisc(new Vector2(rect.x + rect.width * .25f, rect.y + rect.height * .78f), Vector3.forward, 2f);
            Handles.DrawSolidDisc(new Vector2(rect.x + rect.width * .72f, rect.y + rect.height * .24f), Vector3.forward, 2f);
        }
        else
        {
            Handles.DrawSolidDisc(inner.center, Vector3.forward, Mathf.Min(inner.width, inner.height) * 0.30f);
        }

        Handles.color = old;
        Handles.EndGUI();

        if (!row.visible)
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.45f));
    }

    private bool TryDrawSourceSpriteThumbnail(Rect rect, SkyPrisonAnimationRigRow row)
    {
        if (row == null || string.IsNullOrEmpty(row.sourceAssetPath))
            return false;

        Sprite sprite = LoadSpriteFromRow(row);
        if (sprite != null && sprite.texture != null)
        {
            Rect tr = sprite.textureRect;
            Texture2D tex = sprite.texture;
            Rect uv = new Rect(
                tr.x / tex.width,
                tr.y / tex.height,
                tr.width / tex.width,
                tr.height / tex.height);

            GUI.DrawTextureWithTexCoords(rect, tex, uv, true);
            return true;
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(row.sourceAssetPath);
        if (texture != null)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            return true;
        }

        return false;
    }

    private Sprite LoadSpriteFromRow(SkyPrisonAnimationRigRow row)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(row.sourceAssetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            Sprite sprite = assets[i] as Sprite;
            if (sprite == null)
                continue;

            if (string.IsNullOrEmpty(row.sourceSpriteName) || sprite.name == row.sourceSpriteName)
                return sprite;
        }

        return null;
    }

    private void DrawFolderPreview(Rect rect)
    {
        Color c = new Color(0.72f, 0.56f, 0.28f, 1f);
        EditorGUI.DrawRect(new Rect(rect.x + 3f, rect.y + 4f, rect.width * 0.42f, 4f), c);
        EditorGUI.DrawRect(new Rect(rect.x + 3f, rect.y + 7f, rect.width - 6f, rect.height - 10f), new Color(c.r, c.g, c.b, 0.78f));
    }

    private Color ResolveLayerPreviewColor(SkyPrisonAnimationRigRow row)
    {
        string all = ((row.semantic ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.name ?? string.Empty)).ToLowerInvariant();

        if (row.previewColor.a > 0.001f && row.previewColor != new Color(.75f, .78f, .82f, 1f))
            return row.previewColor;

        if (all.Contains("head") || all.Contains("头")) return new Color(0.42f, 0.82f, 0.52f, 1f);
        if (all.Contains("hand") || all.Contains("arm") || all.Contains("手") || all.Contains("臂")) return new Color(0.95f, 0.70f, 0.24f, 1f);
        if (all.Contains("foot") || all.Contains("toe") || all.Contains("leg") || all.Contains("ankle") || all.Contains("脚") || all.Contains("腿") || all.Contains("踝")) return new Color(0.72f, 0.42f, 0.92f, 1f);
        if (all.Contains("core") || all.Contains("核心")) return new Color(0.20f, 0.88f, 0.95f, 1f);
        if (all.Contains("torso") || all.Contains("chest") || all.Contains("pelvis") || all.Contains("body") || all.Contains("躯干") || all.Contains("胸") || all.Contains("骨盆") || all.Contains("身体")) return new Color(0.30f, 0.58f, 0.92f, 1f);
        return new Color(0.72f, 0.74f, 0.78f, 1f);
    }

    private void DrawThinPreviewBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void DrawEditableName(Rect nameRect, SkyPrisonAnimationRigRow row, int index)
    {
        Event e = Event.current;

        if (renamingIndex == index)
        {
            if (string.IsNullOrEmpty(renamingControlName))
                renamingControlName = "RigNodeRename_" + index;

            GUI.SetNextControlName(renamingControlName);
            string newName = EditorGUI.TextField(nameRect, renamingBuffer);
            if (newName != renamingBuffer)
                renamingBuffer = newName;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
            {
                CommitRename(row);
                e.Use();
                return;
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelRename();
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && !nameRect.Contains(e.mousePosition))
            {
                // 外部点击只用于提交别名，不能继续传给搜索框或列表选择。
                CommitRename(row);
                e.Use();
                return;
            }

            if (GUI.GetNameOfFocusedControl() != renamingControlName)
                EditorGUI.FocusTextInControl(renamingControlName);

            return;
        }

        GUI.Label(nameRect, row.name);

        if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && nameRect.Contains(e.mousePosition))
        {
            BeginRename(index, row.name);
            e.Use();
        }
    }

    private void BeginRename(int index, string currentName)
    {
        renamingIndex = index;
        renamingBuffer = currentName ?? string.Empty;
        renamingControlName = "RigNodeRename_" + index;
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = true;
    }

    private void CommitRename(SkyPrisonAnimationRigRow row)
    {
        string next = (renamingBuffer ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(next) && next != row.name)
        {
            state.PushStructureUndo();
            row.name = next;
            GUI.changed = true;
        }
        CancelRename();
    }

    private void CancelRename()
    {
        renamingIndex = -1;
        renamingBuffer = string.Empty;
        renamingControlName = string.Empty;
        GUI.FocusControl(null);
    }

    private void HandleRowInput(Rect r, int index, List<SkyPrisonAnimationRigRow> rows)
    {
        Event e = Event.current;

        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
        {
            if (e.shift && state.LastClickedRig >= 0)
            {
                state.SelectedRigRows.Clear();
                int a = Mathf.Min(state.LastClickedRig, index);
                int b = Mathf.Max(state.LastClickedRig, index);
                for (int i = a; i <= b; i++) state.SelectedRigRows.Add(i);
            }
            else if (e.control || e.command)
            {
                if (state.SelectedRigRows.Contains(index)) state.SelectedRigRows.Remove(index);
                else state.SelectedRigRows.Add(index);
                state.LastClickedRig = index;
            }
            else
            {
                state.SelectedRigRows.Clear();
                state.SelectedRigRows.Add(index);
                state.SelectedRig = index;
                state.LastClickedRig = index;
            }

            state.RememberSelectedStructureRow(rows[index]);

            SkyPrisonAnimationRigRow clickedRow = rows[index];
            if (clickedRow != null && !clickedRow.isFolder)
            {
                if (state.StructureTab == SkyPrisonAnimationStructureTab.Rig)
                {
                    // 点击 Rig 节点时，轨道和当前白线帧关键帧同步锁定到这个节点。
                    // preferRigAngle=true：如果同一帧同时有 Rig 与 RigAngle，优先选角度关键帧，避免左侧参数写错槽。
                    state.LockCurrentFrameKeyframeForRigTarget(clickedRow.key, false, true);
                }
                else if (state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer && !string.IsNullOrEmpty(clickedRow.boundRigKey))
                {
                    // 点击 PSB 图层时，时间线锁到它绑定的 Rig；PSB 自己不创建轨道。
                    state.LockCurrentFrameKeyframeForRigTarget(clickedRow.boundRigKey, false, false, clickedRow.key);
                }
            }

            GUI.FocusControl(null);
            e.Use();
        }

        if (e.type == EventType.MouseDrag && r.Contains(e.mousePosition) && state.SelectedRigRows.Count > 0)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragPayloadKey, state.StructureTab);
            DragAndDrop.objectReferences = new UnityEngine.Object[0];
            DragAndDrop.StartDrag("Move Structure Rows");
            e.Use();
        }

        if (e.type == EventType.ContextClick && r.Contains(e.mousePosition))
        {
            // 右键菜单以当前右键点到的行为操作对象。
            // 如果右键点到的是未选中的行，则先切换为单选；
            // 如果右键点到的是已选中的多选集合成员，则保留多选集合，方便批量复制/剪切/删除。
            SelectRowForContextMenu(index, rows);

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("新建子节点"), false, AddNode);
            menu.AddItem(new GUIContent("新建文件夹"), false, AddFolder);
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("复制节点"), false, () => CopySelectedRows(rows));
            menu.AddItem(new GUIContent("剪切节点"), false, () => CutSelectedRows(rows));
            if (CanPasteRows())
                menu.AddItem(new GUIContent("粘贴节点"), false, () => PasteRows(rows));
            else
                menu.AddDisabledItem(new GUIContent("粘贴节点"));

            menu.AddSeparator("");

            if (state.StructureTab == SkyPrisonAnimationStructureTab.Rig)
            {
                menu.AddItem(new GUIContent("绑定最近选中PSB图层"), false, () => { state.PushStructureUndo(); state.BindRememberedPsbToRememberedRig(); });
                menu.AddItem(new GUIContent("解除此Rig绑定"), false, () => { state.PushStructureUndo(); state.UnbindRigFromPsb(rows[index]); });
            }
            else if (state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer)
            {
                menu.AddItem(new GUIContent("绑定到最近选中Rig"), false, () => { state.PushStructureUndo(); state.BindRememberedPsbToRememberedRig(); });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("锁定/解锁"), false, () => { state.PushStructureUndo(); rows[index].locked = !rows[index].locked; });
            menu.AddItem(new GUIContent("显示/隐藏"), false, () => { state.PushStructureUndo(); SetVisibleRecursive(rows[index], rows, !rows[index].visible); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除节点"), false, DeleteSelected);
            menu.ShowAsContext();
            e.Use();
        }
    }

    private void DrawDropHintAndHandleDrop(Rect rowRect, int targetIndex, List<SkyPrisonAnimationRigRow> rows)
    {
        Event e = Event.current;
        if (e == null || !rowRect.Contains(e.mousePosition))
            return;

        // 只在真正拖拽结构行时画落点提示。
        // 之前 DragAndDrop 的 GenericData 偶尔会残留，普通滚动/悬停也可能画出黄色线。
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            return;

        object payload = DragAndDrop.GetGenericData(DragPayloadKey);
        if (payload == null || !(payload is SkyPrisonAnimationStructureTab) || (SkyPrisonAnimationStructureTab)payload != state.StructureTab)
            return;

        if (state.SelectedRigRows.Contains(targetIndex))
            return;

        float t = Mathf.InverseLerp(rowRect.y, rowRect.yMax, e.mousePosition.y);

        // Rig 页签的层级本身就是骨骼父子关系：
        // - 拖到行中段：把被拖 Rig 作为目标 Rig / 文件夹的子节点，目标行画整框。
        // - 拖到行上/下边：平级移动，画横线。
        // PSB / Socket 页签仍然维持旧规则：只有文件夹可以接收子节点。
        bool canDropAsChild = rows[targetIndex].isFolder || state.StructureTab == SkyPrisonAnimationStructureTab.Rig;
        bool asChild = canDropAsChild && t > 0.30f && t < 0.70f;
        bool after = t >= 0.5f;

        if (asChild)
        {
            // 中段：加入目标节点作为子节点，用橙色整框提示。
            Color c = new Color(1f, 0.72f, 0.22f, 0.95f);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, 2f), c);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 2f, rowRect.width, 2f), c);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 2f, rowRect.height), c);
            EditorGUI.DrawRect(new Rect(rowRect.xMax - 2f, rowRect.y, 2f, rowRect.height), c);
            EditorGUI.DrawRect(rowRect, new Color(1f, 0.72f, 0.22f, 0.10f));
        }
        else
        {
            // 上/下边缘：平级排序移动，用蓝色横线提示。
            // 这条线表示“插入到目标节点之前/之后”，不是加入子节点。
            float y = after ? rowRect.yMax - 2f : rowRect.y;
            Color blue = new Color(0.18f, 0.72f, 1f, 1f);
            Color blueGlow = new Color(0.18f, 0.72f, 1f, 0.18f);
            Rect glowRect = new Rect(rowRect.x, y - 3f, rowRect.width, 8f);
            Rect lineRect = new Rect(rowRect.x, y, rowRect.width, 2f);
            EditorGUI.DrawRect(glowRect, blueGlow);
            EditorGUI.DrawRect(lineRect, blue);
        }

        if (e.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            e.Use();
        }
        else if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            state.PushStructureUndo();
            MoveSelectedRows(targetIndex, asChild, after, rows);
            GUI.changed = true;
            e.Use();
        }
    }

    private void MoveSelectedRows(int targetIndex, bool asChild, bool after, List<SkyPrisonAnimationRigRow> rows)
    {
        if (rows == null || rows.Count == 0 || targetIndex < 0 || targetIndex >= rows.Count)
            return;

        List<int> selected = new List<int>(state.SelectedRigRows);
        if (selected.Count == 0 && state.SelectedRig >= 0)
            selected.Add(state.SelectedRig);

        selected.Sort();
        if (selected.Contains(targetIndex))
            return;

        int sourceIndex = selected[0];
        if (sourceIndex < 0 || sourceIndex >= rows.Count)
            return;

        int sourceEnd = FindSubtreeEnd(sourceIndex, rows);
        if (targetIndex >= sourceIndex && targetIndex <= sourceEnd)
            return;

        List<SkyPrisonAnimationRigRow> block = rows.GetRange(sourceIndex, sourceEnd - sourceIndex + 1);
        SkyPrisonAnimationRigRow target = rows[targetIndex];

        int targetDepth = target.depth;
        string targetParent = target.parentKey;
        int insertIndex = targetIndex;

        bool canDropAsChild = target.isFolder || state.StructureTab == SkyPrisonAnimationStructureTab.Rig;
        if (asChild && canDropAsChild)
        {
            target.expanded = true;
            targetParent = target.key;
            targetDepth = target.depth + 1;
            insertIndex = FindSubtreeEnd(targetIndex, rows) + 1;
        }
        else if (after)
        {
            insertIndex = FindSubtreeEnd(targetIndex, rows) + 1;
        }

        rows.RemoveRange(sourceIndex, block.Count);
        if (insertIndex > sourceIndex)
            insertIndex -= block.Count;

        int delta = targetDepth - block[0].depth;
        block[0].parentKey = targetParent;
        for (int i = 0; i < block.Count; i++)
            block[i].depth = Mathf.Max(0, block[i].depth + delta);

        insertIndex = Mathf.Clamp(insertIndex, 0, rows.Count);
        rows.InsertRange(insertIndex, block);

        state.SelectedRigRows.Clear();
        for (int i = 0; i < block.Count; i++)
            state.SelectedRigRows.Add(insertIndex + i);
        state.SelectedRig = insertIndex;
        state.LastClickedRig = insertIndex;
    }

    private int FindSubtreeEnd(int index, List<SkyPrisonAnimationRigRow> rows)
    {
        int baseDepth = rows[index].depth;
        int end = index;
        for (int i = index + 1; i < rows.Count; i++)
        {
            if (rows[i].depth <= baseDepth)
                break;
            end = i;
        }
        return end;
    }

    private void SetVisibleRecursive(SkyPrisonAnimationRigRow row, List<SkyPrisonAnimationRigRow> rows, bool visible)
    {
        if (row == null || rows == null)
            return;

        bool isPsbRows = rows == state.PsbRows || state.StructureTab == SkyPrisonAnimationStructureTab.PsbLayer;
        ApplyVisibleState(row, visible, isPsbRows);

        string parentKey = row.key;
        for (int i = 0; i < rows.Count; i++)
        {
            if (IsDescendantOf(rows[i], parentKey, rows))
                ApplyVisibleState(rows[i], visible, isPsbRows);
        }
    }

    private bool AreAllPsbLayersVisible()
    {
        if (state == null || state.PsbRows == null || state.PsbRows.Count == 0)
            return true;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row != null && !row.visible)
                return false;
        }

        return true;
    }

    private void SetAllPsbLayersVisible(bool visible)
    {
        if (state == null || state.PsbRows == null)
            return;

        for (int i = 0; i < state.PsbRows.Count; i++)
            ApplyVisibleState(state.PsbRows[i], visible, true);
    }

    private void ApplyVisibleState(SkyPrisonAnimationRigRow row, bool visible, bool isPsbRow)
    {
        if (row == null)
            return;

        row.visible = visible;

        // PSB 图层的“眼睛”必须是最终可见性的硬开关。
        // 旧逻辑只改 visible，部分绑定图层仍可能因为 opacity / 绑定预览状态看起来没有被关闭。
        // 这里对非文件夹 PSB 行同步 opacity，保证列表状态和预览显示一致。
        if (isPsbRow && !row.isFolder)
        {
            if (!visible)
                row.opacity = 0f;
            else if (row.opacity <= 0.001f)
                row.opacity = 1f;
        }
    }

    private bool IsDescendantOf(SkyPrisonAnimationRigRow row, string ancestorKey, List<SkyPrisonAnimationRigRow> rows)
    {
        if (row == null || string.IsNullOrEmpty(ancestorKey))
            return false;

        string p = row.parentKey;
        int guard = 0;
        while (!string.IsNullOrEmpty(p) && guard++ < 256)
        {
            if (p == ancestorKey)
                return true;

            SkyPrisonAnimationRigRow parent = FindRowByKey(p, rows);
            if (parent == null)
                return false;
            p = parent.parentKey;
        }
        return false;
    }

    private bool IsVisibleByParentExpansion(int index, List<SkyPrisonAnimationRigRow> rows)
    {
        if (index < 0 || index >= rows.Count)
            return false;

        SkyPrisonAnimationRigRow row = rows[index];
        string p = row.parentKey;
        int guard = 0;
        while (!string.IsNullOrEmpty(p) && guard++ < 256)
        {
            SkyPrisonAnimationRigRow parent = FindRowByKey(p, rows);
            if (parent == null)
                return true;
            if (!parent.expanded)
                return false;
            p = parent.parentKey;
        }
        return true;
    }

    private SkyPrisonAnimationRigRow FindRowByKey(string key, List<SkyPrisonAnimationRigRow> rows)
    {
        if (string.IsNullOrEmpty(key) || rows == null)
            return null;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].key == key)
                return rows[i];
        return null;
    }

    private void HandleKeyboard(List<SkyPrisonAnimationRigRow> rows)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (!hasStructureKeyboardFocus)
            return;

        if (renamingIndex >= 0)
            return;

        string focused = GUI.GetNameOfFocusedControl();
        bool textFieldFocused = IsAnyTextEditingControlFocused(focused);
        bool ctrl = e.control || e.command;
        bool shift = e.shift;

        if (ctrl && e.keyCode == KeyCode.Z)
        {
            bool changed = shift ? state.RedoStructure() : state.UndoStructure();
            if (changed)
            {
                ClampSelectionAfterRowMutation();
                GUI.changed = true;
            }
            e.Use();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.Y)
        {
            if (state.RedoStructure())
            {
                ClampSelectionAfterRowMutation();
                GUI.changed = true;
            }
            e.Use();
            return;
        }

        if (textFieldFocused)
            return;

        if (ctrl && e.keyCode == KeyCode.A)
        {
            SelectAllVisibleRows(rows);
            e.Use();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.C)
        {
            CopySelectedRows(rows);
            e.Use();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.X)
        {
            CutSelectedRows(rows);
            e.Use();
            return;
        }

        if (ctrl && e.keyCode == KeyCode.V)
        {
            PasteRows(rows);
            e.Use();
            return;
        }

        // Backspace 是文本编辑高频键，不再作为删除节点快捷键。
        // Delete 只有在结构面板拥有键盘焦点且没有任何文本/数字框正在编辑时才会删除节点。
        if (e.keyCode == KeyCode.Delete)
        {
            DeleteSelected();
            e.Use();
        }
    }

    private void UpdateKeyboardFocusFromMouse(Rect panelRect)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown)
            return;

        hasStructureKeyboardFocus = panelRect.Contains(e.mousePosition);
    }

    private bool IsAnyTextEditingControlFocused(string focusedControlName)
    {
        if (EditorGUIUtility.editingTextField)
            return true;

        if (focusedControlName == "结构搜索")
            return true;

        if (!string.IsNullOrEmpty(focusedControlName) && focusedControlName.StartsWith("RigNodeRename_"))
            return true;

        // 某些 EditorGUILayout.FloatField / IntField 没有显式 controlName，
        // 但只要键盘焦点落在文本编辑控件里，editingTextField 通常会为 true。
        return false;
    }

    private void SelectRowForContextMenu(int index, List<SkyPrisonAnimationRigRow> rows)
    {
        if (rows == null || index < 0 || index >= rows.Count)
            return;

        if (!state.SelectedRigRows.Contains(index))
        {
            state.SelectedRigRows.Clear();
            state.SelectedRigRows.Add(index);
        }

        state.SelectedRig = index;
        state.LastClickedRig = index;
        state.RememberSelectedStructureRow(rows[index]);
        hasStructureKeyboardFocus = true;
        GUI.FocusControl(null);
        GUI.changed = true;
    }

    private bool CanPasteRows()
    {
        return clipboardRows.Count > 0 && clipboardTab == state.StructureTab;
    }

    private void SelectAllVisibleRows(List<SkyPrisonAnimationRigRow> rows)
    {
        state.SelectedRigRows.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(state.StructureSearch) || IsVisibleByParentExpansion(i, rows))
                state.SelectedRigRows.Add(i);
        }

        if (state.SelectedRigRows.Count > 0)
        {
            int first = int.MaxValue;
            foreach (int i in state.SelectedRigRows)
                first = Mathf.Min(first, i);
            state.SelectedRig = first;
            state.LastClickedRig = first;
        }

        GUI.changed = true;
    }

    private void CopySelectedRows(List<SkyPrisonAnimationRigRow> rows)
    {
        clipboardRows.Clear();
        clipboardTab = state.StructureTab;

        List<int> roots = GetSelectedRootIndices(rows);
        for (int r = 0; r < roots.Count; r++)
        {
            int start = roots[r];
            int finish = FindSubtreeEnd(start, rows);
            for (int i = start; i <= finish; i++)
                clipboardRows.Add(rows[i].Clone());
        }
    }

    private void CutSelectedRows(List<SkyPrisonAnimationRigRow> rows)
    {
        if (rows == null || rows.Count == 0)
            return;

        CopySelectedRows(rows);
        DeleteSelected();
    }

    private void PasteRows(List<SkyPrisonAnimationRigRow> rows)
    {
        if (rows == null || clipboardRows.Count == 0 || clipboardTab != state.StructureTab)
            return;

        state.PushStructureUndo();

        SkyPrisonAnimationRigRow selected = state.GetSelectedRigRow();
        bool pasteIntoFolder = selected != null && selected.key != "-" && selected.isFolder;

        string newParentKey = "";
        int baseDepth = 0;
        int insertIndex = rows.Count;

        if (selected != null && selected.key != "-" && rows.Contains(selected))
        {
            int selectedIndex = rows.IndexOf(selected);
            if (pasteIntoFolder)
            {
                newParentKey = selected.key;
                baseDepth = selected.depth + 1;
                selected.expanded = true;
                insertIndex = FindSubtreeEnd(selectedIndex, rows) + 1;
            }
            else
            {
                newParentKey = selected.parentKey;
                baseDepth = selected.depth;
                insertIndex = FindSubtreeEnd(selectedIndex, rows) + 1;
            }
        }

        List<SkyPrisonAnimationRigRow> clones = CloneClipboardForPaste(rows, newParentKey, baseDepth);
        insertIndex = Mathf.Clamp(insertIndex, 0, rows.Count);
        rows.InsertRange(insertIndex, clones);

        state.SelectedRigRows.Clear();
        for (int i = 0; i < clones.Count; i++)
            state.SelectedRigRows.Add(insertIndex + i);
        state.SelectedRig = insertIndex;
        state.LastClickedRig = insertIndex;
        if (clones.Count > 0)
            state.RememberSelectedStructureRow(clones[0]);

        GUI.changed = true;
    }

    private List<SkyPrisonAnimationRigRow> CloneClipboardForPaste(List<SkyPrisonAnimationRigRow> rows, string newParentKey, int baseDepth)
    {
        List<SkyPrisonAnimationRigRow> clones = new List<SkyPrisonAnimationRigRow>();
        Dictionary<string, string> keyMap = new Dictionary<string, string>();

        int minDepth = int.MaxValue;
        for (int i = 0; i < clipboardRows.Count; i++)
            minDepth = Mathf.Min(minDepth, clipboardRows[i].depth);
        if (minDepth == int.MaxValue)
            minDepth = 0;

        for (int i = 0; i < clipboardRows.Count; i++)
        {
            SkyPrisonAnimationRigRow clone = clipboardRows[i].Clone();
            string oldKey = clone.key;
            clone.key = GenerateUniqueRowKey(rows, oldKey + "_Copy");
            keyMap[oldKey] = clone.key;
            clone.name = BuildCopyName(clone.name);
            clone.boundRigKey = "";
            clone.boundRigName = "";
            clones.Add(clone);
        }

        for (int i = 0; i < clones.Count; i++)
        {
            SkyPrisonAnimationRigRow original = clipboardRows[i];
            SkyPrisonAnimationRigRow clone = clones[i];
            clone.depth = baseDepth + Mathf.Max(0, original.depth - minDepth);

            if (string.IsNullOrEmpty(original.parentKey) || !keyMap.ContainsKey(original.parentKey))
                clone.parentKey = newParentKey;
            else
                clone.parentKey = keyMap[original.parentKey];
        }

        return clones;
    }

    private string BuildCopyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "副本";
        if (name.EndsWith(" 副本"))
            return name;
        return name + " 副本";
    }

    private List<int> GetSelectedRootIndices(List<SkyPrisonAnimationRigRow> rows)
    {
        List<int> selected = new List<int>(state.SelectedRigRows);
        if (selected.Count == 0 && state.SelectedRig >= 0)
            selected.Add(state.SelectedRig);

        selected.Sort();
        List<int> roots = new List<int>();

        for (int i = 0; i < selected.Count; i++)
        {
            int idx = selected[i];
            if (idx < 0 || idx >= rows.Count)
                continue;

            bool insideExistingRoot = false;
            for (int r = 0; r < roots.Count; r++)
            {
                int finish = FindSubtreeEnd(roots[r], rows);
                if (idx >= roots[r] && idx <= finish)
                {
                    insideExistingRoot = true;
                    break;
                }
            }

            if (!insideExistingRoot)
                roots.Add(idx);
        }

        return roots;
    }

    private void ClampSelectionAfterRowMutation()
    {
        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        state.SelectedRigRows.RemoveWhere(i => i < 0 || i >= rows.Count);
        state.SelectedRig = Mathf.Clamp(state.SelectedRig, 0, Mathf.Max(0, rows.Count - 1));
        if (state.SelectedRigRows.Count == 0 && rows.Count > 0)
            state.SelectedRigRows.Add(state.SelectedRig);
    }

    private bool PassStructureSearch(SkyPrisonAnimationRigRow row)
    {
        if (string.IsNullOrWhiteSpace(state.StructureSearch)) return true;
        string s = state.StructureSearch.Trim().ToLower();
        return SkyPrisonAnimationWorkbenchState.SafeContains(row.key, s) || SkyPrisonAnimationWorkbenchState.SafeContains(row.name, s) || SkyPrisonAnimationWorkbenchState.SafeContains(row.semantic, s) || SkyPrisonAnimationWorkbenchState.SafeContains(row.sourceLayerPath, s);
    }

    private bool HasChildren(SkyPrisonAnimationRigRow row, List<SkyPrisonAnimationRigRow> rows)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].parentKey == row.key) return true;
        return false;
    }

    private bool CanCreateMeshDeformerForSelection()
    {
        if (state == null || state.StructureTab != SkyPrisonAnimationStructureTab.Rig)
            return false;

        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        if (rows == null || rows.Count == 0 || state.SelectedRig < 0 || state.SelectedRig >= rows.Count)
            return false;

        SkyPrisonAnimationRigRow row = rows[state.SelectedRig];
        return IsValidMeshDeformerTarget(row);
    }

    private bool IsValidMeshDeformerTarget(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.isFolder || row.isMeshDeformer)
            return false;

        // 曲面变形只能作用在“有实际 PSB 图层呈现”的 Rig 节点上。
        // 纯控制骨、总控、骨盆等没有绑定图层的节点没有贴图矩形，不能生成曲面变形控制器。
        return HasDirectPsbLayerBinding(row);
    }

    private bool HasDirectPsbLayerBinding(SkyPrisonAnimationRigRow rig)
    {
        if (rig == null || string.IsNullOrEmpty(rig.key))
            return false;

        // 手动/自动绑定时会把 PSB 源信息复制到 Rig 上，这是最快的正向判断。
        if (!string.IsNullOrEmpty(rig.boundRigKey) &&
            (!string.IsNullOrEmpty(rig.sourceAssetPath) ||
             !string.IsNullOrEmpty(rig.sourceSpriteName) ||
             !string.IsNullOrEmpty(rig.sourceLayerPath)))
            return true;

        // 兼容旧缓存：有些旧数据可能只保留了 PSB 行上的反向绑定。
        if (state.PsbRows != null)
        {
            for (int i = 0; i < state.PsbRows.Count; i++)
            {
                SkyPrisonAnimationRigRow psb = state.PsbRows[i];
                if (psb == null || psb.isFolder)
                    continue;

                if (psb.boundRigKey == rig.key)
                    return true;
            }
        }

        return false;
    }

    private void OpenMeshDeformerPopup()
    {
        if (!CanCreateMeshDeformerForSelection())
            return;

        SkyPrisonAnimationRigRow target = state.GetSelectedRigRow();
        SkyPrisonMeshDeformerCreateWindow.Open(target != null ? target.name : "选中节点", (columns, rows) =>
        {
            CreateMeshDeformerForSelected(columns, rows);
        });
    }

    private void CreateMeshDeformerForSelected(int columns, int rowsCount)
    {
        if (!CanCreateMeshDeformerForSelection())
            return;

        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        int targetIndex = state.SelectedRig;
        SkyPrisonAnimationRigRow target = rows[targetIndex];
        if (!IsValidMeshDeformerTarget(target))
            return;

        columns = Mathf.Clamp(columns, 2, 16);
        rowsCount = Mathf.Clamp(rowsCount, 2, 16);

        state.PushStructureUndo();

        string baseKey = string.IsNullOrWhiteSpace(target.key) ? "MeshDeformer" : "MeshDeformer_" + target.key;
        string key = GenerateUniqueRowKey(rows, baseKey);

        SkyPrisonAnimationRigRow deformer = new SkyPrisonAnimationRigRow
        {
            key = key,
            name = target.name + "_曲面变形",
            semantic = "MeshDeformer",
            depth = Mathf.Max(0, target.depth + 1),
            parentKey = target.key,
            isFolder = false,
            expanded = true,
            visible = true,
            mapped = true,
            hasKey = false,
            previewIconNumber = 47,
            previewColor = new Color(0.62f, 0.82f, 1f, 1f),
            isMeshDeformer = true,
            meshDeformTargetKey = target.key,
            meshDeformColumns = columns,
            meshDeformRows = rowsCount,
            meshDeformPoints = new List<SkyPrisonMeshDeformPoint>()
        };

        for (int y = 0; y < rowsCount; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                deformer.meshDeformPoints.Add(new SkyPrisonMeshDeformPoint { x = x, y = y, offset = Vector2.zero });
            }
        }

        int insertIndex = Mathf.Clamp(FindSubtreeEnd(targetIndex, rows) + 1, 0, rows.Count);
        rows.Insert(insertIndex, deformer);

        target.expanded = true;
        state.SelectedRig = insertIndex;
        state.LastSelectedRigKey = deformer.key;
        state.SelectedRigRows.Clear();
        state.SelectedRigRows.Add(insertIndex);
        state.SelectedRigIndices.Clear();
        state.SelectedRigIndices.Add(insertIndex);

        GUI.changed = true;
    }

    private void AddNode()
    {
        if (state.StructureTab == SkyPrisonAnimationStructureTab.Rig)
            EnterRigTabKeepingEmptyRigEditable();

        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        state.PushStructureUndo();
        SkyPrisonAnimationRigRow parent = state.GetSelectedRigRow();

        if (IsManualCustomRigMode() && state.StructureTab == SkyPrisonAnimationStructureTab.Rig)
        {
            string key = GenerateUniqueRowKey(rows, "CustomBone");
            int index = rows.Count + 1;
            rows.Add(new SkyPrisonAnimationRigRow
            {
                key = key,
                name = "自定义骨骼_" + index,
                semantic = "CustomBone",
                depth = 0,
                parentKey = "",
                isFolder = false,
                expanded = true,
                hasKey = true,
                mapped = true,
                previewIconNumber = 100,
                previewColor = new Color(0.92f, 0.70f, 1f, 1f),
                useCustomBoneLine = true,
                customBoneRoot = new Vector2(-24f, 0f),
                customBoneHead = new Vector2(24f, 0f),
                visible = true
            });
        }
        else
        {
            string key = GenerateUniqueRowKey(rows, "NewNode");
            rows.Add(new SkyPrisonAnimationRigRow { key = key, name = "新建节点", semantic = "未映射", depth = Mathf.Max(0, parent.depth + 1), parentKey = parent.key == "-" ? "" : parent.key, previewIconNumber = 42, visible = true });
        }

        state.SelectedRig = rows.Count - 1;
        state.LastSelectedRigKey = rows[state.SelectedRig].key;
        state.SelectedRigRows.Clear();
        state.SelectedRigRows.Add(state.SelectedRig);
        GUI.changed = true;
    }

    private void AddFolder()
    {
        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        state.PushStructureUndo();
        SkyPrisonAnimationRigRow parent = state.GetSelectedRigRow();
        string key = GenerateUniqueRowKey(rows, "Folder");
        rows.Add(new SkyPrisonAnimationRigRow { key = key, name = "新建文件夹", semantic = "文件夹", depth = parent.key == "-" ? 0 : parent.depth + 1, parentKey = parent.key == "-" ? "" : parent.key, isFolder = true, previewIconNumber = 45, expanded = true, visible = true });
        state.SelectedRig = rows.Count - 1;
        state.SelectedRigRows.Clear();
        state.SelectedRigRows.Add(state.SelectedRig);
    }

    private string GenerateUniqueRowKey(List<SkyPrisonAnimationRigRow> rows, string baseKey)
    {
        int i = rows != null ? rows.Count : 0;
        string key = baseKey + "_" + i;
        while (rows != null && rows.Exists(x => x.key == key))
        {
            i++;
            key = baseKey + "_" + i;
        }
        return key;
    }

    private void DeleteSelected()
    {
        List<SkyPrisonAnimationRigRow> rows = state.GetCurrentRows();
        if (rows == null || rows.Count == 0)
            return;

        state.PushStructureUndo();

        List<int> selected = new List<int>(state.SelectedRigRows);
        if (selected.Count == 0 && state.SelectedRig >= 0)
            selected.Add(state.SelectedRig);

        selected.Sort();
        HashSet<int> toDelete = new HashSet<int>();
        for (int s = 0; s < selected.Count; s++)
        {
            int idx = selected[s];
            if (idx < 0 || idx >= rows.Count)
                continue;
            int end = FindSubtreeEnd(idx, rows);
            for (int i = idx; i <= end; i++)
                toDelete.Add(i);
        }

        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (toDelete.Contains(i))
                rows.RemoveAt(i);
        }

        state.SelectedRigRows.Clear();
        state.SelectedRig = Mathf.Clamp(state.SelectedRig, 0, Mathf.Max(0, rows.Count - 1));
    }

    private void DrawIconButton(Rect rect, int iconNumber, string tooltip, System.Action action)
    {
        DrawIconButton(rect, iconNumber, tooltip, action, true);
    }

    private void DrawIconButton(Rect rect, int iconNumber, string tooltip, System.Action action, bool enabled)
    {
        GUIContent content = new GUIContent(SkyPrisonAnimationWorkbenchStyle.LoadEditorIcon(iconNumber), tooltip);
        using (new EditorGUI.DisabledScope(!enabled))
        {
            if (GUI.Button(rect, content))
                action?.Invoke();
        }
    }
}

public sealed class SkyPrisonMeshDeformerCreateWindow : EditorWindow
{
    private int columns = 3;
    private int rows = 3;
    private string targetName = "选中节点";
    private System.Action<int, int> onCreate;

    public static void Open(string targetName, System.Action<int, int> onCreate)
    {
        SkyPrisonMeshDeformerCreateWindow window = CreateInstance<SkyPrisonMeshDeformerCreateWindow>();
        window.titleContent = new GUIContent("生成曲面变形");
        window.targetName = string.IsNullOrWhiteSpace(targetName) ? "选中节点" : targetName;
        window.onCreate = onCreate;
        window.minSize = new Vector2(300f, 132f);
        window.maxSize = new Vector2(300f, 132f);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("目标节点", targetName, EditorStyles.boldLabel);
        GUILayout.Space(6f);

        EditorGUILayout.HelpBox("输入 N × M 网格。第一版会生成曲面变形子节点，后续网格点编辑会直接写入这个节点。", MessageType.None);

        columns = Mathf.Clamp(EditorGUILayout.IntField("N / 横向列数", columns), 2, 16);
        rows = Mathf.Clamp(EditorGUILayout.IntField("M / 纵向行数", rows), 2, 16);

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("取消", GUILayout.Height(24f)))
            Close();
        if (GUILayout.Button("生成", GUILayout.Height(24f)))
        {
            onCreate?.Invoke(columns, rows);
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }
}

