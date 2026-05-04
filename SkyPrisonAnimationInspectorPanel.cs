using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public sealed class SkyPrisonAnimationInspectorPanel
{
    private readonly SkyPrisonAnimationWorkbenchState state;

    private const float HeaderHeight = 28f;
    private const float TopFoldBarHeight = 30f;
    private const float ContainerGap = 6f;
    private const float ContainerPadding = 8f;
    private const float AssemblyExpandedHeight = 330f;
    private const float CollapsedHeight = 30f;
    private const float InspectorAssemblySplitterHeight = 5f;
    private const float InspectorAssemblyMinHeight = 148f;
    private const float SelectedInspectorMinHeight = 112f;

    private string activeDelayedFloatUndoControl = string.Empty;
    private object activeDelayedFloatUndoSnapshot = null;
    private bool draggingInspectorAssemblySplitter = false;
    private float inspectorAssemblyPanelHeight = AssemblyExpandedHeight;

    // Inspector 绘制期上下文：
    // 节点 / PSB 图层 / Socket 的“结构属性”不应被时间线锁轨禁用；
    // 只有真正写入当前帧关键帧的动画参数，才需要受锁轨和关键帧选择约束。
    private bool inspectorAnimatedControlsLocked = false;
    private bool inspectorEditingSelectedFrameRig = false;
    private bool inspectorEditingSelectedFrameLayerWeight = false;

    // 0 = 结构属性，1 = 当前帧属性。
    // 这里只影响 Inspector 显示哪一类，不改变任何数据归属。
    private int selectedPropertyScopeTab = 0;
    private string lastSelectedPropertyScopeKey = string.Empty;
    private Vector2 physicsOscillatorScroll = Vector2.zero;

    // Shader 参数容器自己的滚动状态。
    // 不使用外层 Inspector 滚动承载，否则复杂 Shader 的参数会把右侧属性面板撑爆。
    private Vector2 layerShaderParametersScroll = Vector2.zero;
    private string lastLayerShaderParametersScrollKey = string.Empty;

    private const float LayerShaderParameterScrollMaxHeight = 220f;
    private const float LayerShaderParameterRowHeight = 24f;
    private const float LayerShaderParameterFooterHeight = 28f;

    public SkyPrisonAnimationInspectorPanel(SkyPrisonAnimationWorkbenchState state)
    {
        this.state = state;
    }

    public void Draw(Rect rect)
    {
        if (rect.width <= 4f || rect.height <= 4f)
            return;

        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Rect inner = new Rect(rect.x + 6f, rect.y + 6f, Mathf.Max(1f, rect.width - 12f), Mathf.Max(1f, rect.height - 12f));

        Rect topFoldBar = new Rect(inner.x, inner.y, inner.width, TopFoldBarHeight);
        DrawInspectorTopFoldBar(topFoldBar);

        Rect body = new Rect(
            inner.x,
            topFoldBar.yMax + ContainerGap,
            inner.width,
            Mathf.Max(1f, inner.yMax - topFoldBar.yMax - ContainerGap));

        // 装配/属性面板不再显示右上角折叠箭头。
        // 这里强制保持展开，避免旧布局缓存里的 collapsed 状态导致内容消失且无法恢复。
        state.AssemblyPanelCollapsed = false;
        state.SelectedInspectorCollapsed = false;

        if (inspectorAssemblyPanelHeight <= 1f)
            inspectorAssemblyPanelHeight = Mathf.Min(AssemblyExpandedHeight, Mathf.Max(CollapsedHeight, body.height * 0.48f));

        float assemblyHeight = Mathf.Clamp(
            inspectorAssemblyPanelHeight,
            InspectorAssemblyMinHeight,
            Mathf.Max(InspectorAssemblyMinHeight, body.height - SelectedInspectorMinHeight - ContainerGap - InspectorAssemblySplitterHeight));

        Rect assemblyRect = new Rect(body.x, body.y, body.width, assemblyHeight);
        Rect splitterRect = new Rect(body.x, assemblyRect.yMax + ContainerGap * 0.5f, body.width, InspectorAssemblySplitterHeight);
        Rect selectedRect = new Rect(
            body.x,
            splitterRect.yMax + ContainerGap * 0.5f,
            body.width,
            Mathf.Max(CollapsedHeight, body.yMax - splitterRect.yMax - ContainerGap * 0.5f));

        HandleInspectorAssemblySplitter(body, splitterRect);

        DrawContainer(assemblyRect, "装配模拟", DrawAssemblyPreviewContent);
        DrawInspectorAssemblySplitter(splitterRect);
        DrawContainer(selectedRect, "选中项属性", DrawSelectedPropertiesContent);
    }

    private void HandleInspectorAssemblySplitter(Rect body, Rect splitterRect)
    {
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && e.button == 0 && splitterRect.Contains(e.mousePosition))
        {
            draggingInspectorAssemblySplitter = true;
            GUI.FocusControl(null);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingInspectorAssemblySplitter)
        {
            float maxHeight = Mathf.Max(InspectorAssemblyMinHeight, body.height - SelectedInspectorMinHeight - ContainerGap - InspectorAssemblySplitterHeight);
            inspectorAssemblyPanelHeight = Mathf.Clamp(e.mousePosition.y - body.y - ContainerGap * 0.5f, InspectorAssemblyMinHeight, maxHeight);
            GUI.changed = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingInspectorAssemblySplitter)
        {
            draggingInspectorAssemblySplitter = false;
            e.Use();
        }
    }

    private void DrawInspectorAssemblySplitter(Rect rect)
    {
        if (rect.width <= 4f || rect.height <= 1f)
            return;

        Color c = draggingInspectorAssemblySplitter
            ? new Color(0.45f, 0.62f, 0.86f, 0.85f)
            : new Color(1f, 1f, 1f, 0.10f);
        EditorGUI.DrawRect(rect, c);
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
    }

    private void DrawInspectorTopFoldBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.14f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        const float foldButtonWidth = 34f;
        Rect labelRect = new Rect(rect.x + 8f, rect.y + 6f, Mathf.Max(10f, rect.width - 16f - foldButtonWidth), 18f);
        GUI.Label(labelRect, "属性 / 装配", EditorStyles.boldLabel);

        Rect foldRect = new Rect(rect.xMax - foldButtonWidth - 4f, rect.y + 4f, foldButtonWidth, rect.height - 8f);
        if (GUI.Button(foldRect, new GUIContent(">>", "折叠右侧属性 / 装配，把空间留给预览"), EditorStyles.miniButton))
        {
            state.InspectorPanelCollapsed = true;
            GUI.FocusControl(null);
        }
    }

    private void DrawContainer(Rect rect, string title, System.Action<Rect> drawContent)
    {
        if (rect.width <= 4f || rect.height <= 4f)
            return;

        EditorGUI.DrawRect(rect, new Color(0.145f, 0.145f, 0.15f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Rect header = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
        EditorGUI.DrawRect(header, new Color(0.18f, 0.18f, 0.19f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(header, SkyPrisonAnimationWorkbenchStyle.LineColor);

        // 右上角折叠按钮已移除，避免和窗口顶部控制叠在一起。
        GUI.Label(new Rect(header.x + 8f, header.y + 5f, header.width - 16f, 20f), title, EditorStyles.boldLabel);

        Rect content = new Rect(
            rect.x + ContainerPadding,
            header.yMax + ContainerPadding,
            Mathf.Max(1f, rect.width - ContainerPadding * 2f),
            Mathf.Max(1f, rect.yMax - header.yMax - ContainerPadding * 2f));

        drawContent.Invoke(content);
    }

    private void DrawAssemblyPreviewContent(Rect rect)
    {
        state.BuildMockAssemblyData();

        SkyPrisonAnimationAssemblySlot currentSlot = state.CurrentAssemblySlot();
        int layerCount = currentSlot != null ? state.CountAppearanceLayers(currentSlot.appearanceLayers) : 0;
        float contentHeight = Mathf.Max(rect.height + 1f, 88f + state.AssemblySlots.Count * 112f + Mathf.Min(520f, Mathf.Max(180f, layerCount * 22f + 310f)));
        Rect view = rect;
        Rect content = new Rect(0f, 0f, Mathf.Max(1f, view.width - 18f), contentHeight);

        state.AssemblyScroll = GUI.BeginScrollView(view, state.AssemblyScroll, content, false, true);
        GUILayout.BeginArea(content);

        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 72f;

        object undoSnapshot = state.CaptureStructureUndoSnapshot();
        EditorGUI.BeginChangeCheck();

        state.AssemblyPreviewMode = EditorGUILayout.Popup("预览模式", state.AssemblyPreviewMode, state.AssemblyPreviewModes);
        GUILayout.Space(6f);

        EditorGUILayout.LabelField("外貌槽位", EditorStyles.miniBoldLabel);
        for (int i = 0; i < state.AssemblySlots.Count; i++)
        {
            DrawAssemblySlot(i, state.AssemblySlots[i]);
            GUILayout.Space(4f);
        }

        GUILayout.Space(6f);
        DrawCurrentAppearancePartEditor(state.CurrentAssemblySlot());

        if (EditorGUI.EndChangeCheck())
            state.PushCapturedStructureUndo(undoSnapshot);

        EditorGUIUtility.labelWidth = oldLabelWidth;
        GUILayout.EndArea();
        GUI.EndScrollView();
    }

    private void DrawAssemblySlot(int index, SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null)
            return;

        Rect box = EditorGUILayout.BeginVertical("box");

        if (state.SelectedAssemblySlot == index && Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(new Rect(box.x, box.y, box.width, 2f), new Color(0.35f, 0.55f, 0.9f, 0.9f));

        EditorGUILayout.BeginHorizontal();
        bool selected = state.SelectedAssemblySlot == index;
        string label = slot.displayName;
        int layerCount = state.CountAppearanceLayers(slot.appearanceLayers);
        if (layerCount > 0)
            label += "  [" + layerCount + "]";

        if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton, GUILayout.MinWidth(88f)))
            state.SelectedAssemblySlot = index;

        slot.visible = GUILayout.Toggle(slot.visible, new GUIContent("显", "预览显示"), EditorStyles.toolbarButton, GUILayout.Width(34f));
        EditorGUILayout.EndHorizontal();

        slot.assetKey = EditorGUILayout.TextField("资源Key", slot.assetKey);
        slot.boundPartKey = EditorGUILayout.TextField("默认绑定", slot.boundPartKey);
        slot.visualSlotKey = EditorGUILayout.TextField("显示插槽", slot.visualSlotKey);

        EditorGUILayout.EndVertical();
    }

    private void DrawCurrentAppearancePartEditor(SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null)
            return;

        Rect box = EditorGUILayout.BeginVertical("box");
        GUI.Label(new Rect(box.x + 6f, box.y + 4f, Mathf.Max(10f, box.width - 12f), 18f), "当前衣物 PSB / 外貌部件包", EditorStyles.boldLabel);
        GUILayout.Space(22f);

        Rect dropRect = GUILayoutUtility.GetRect(10f, 58f, GUILayout.ExpandWidth(true));
        DrawAppearancePsbDropZone(dropRect, slot);
        GUILayout.Space(5f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择PSB", GUILayout.Height(22f)))
        {
            string path = EditorUtility.OpenFilePanel("导入衣物 PSD/PSB", Application.dataPath, "psd,psb");
            if (!string.IsNullOrEmpty(path))
                ImportAppearancePsbIntoCurrentSlot(path);
        }

        using (new EditorGUI.DisabledScope(slot.appearanceLayers == null || slot.appearanceLayers.Count == 0))
        {
            if (GUILayout.Button("重新识别规则", GUILayout.Height(22f)))
                state.AnalyzeAppearanceLayerRules(slot);

            if (GUILayout.Button("绑定选中图层到当前节点", GUILayout.Height(22f)))
                state.BindSelectedAppearanceLayerToSelectedNode();

            if (GUILayout.Button("清空", GUILayout.Width(48f), GUILayout.Height(22f)))
                state.ClearSelectedAssemblyAppearancePsb();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(slot.appearanceSourceAssetPath))
            EditorGUILayout.SelectableLabel(slot.appearanceSourceAssetPath, EditorStyles.miniLabel, GUILayout.Height(18f));

        DrawAppearanceDyeChannels(slot);
        GUILayout.Space(4f);
        DrawAppearanceLayerTree(slot);

        SkyPrisonAppearancePsbLayerNode selectedLayer = state.GetSelectedAppearanceLayerInCurrentSlot();
        DrawSelectedAppearanceLayerBinding(selectedLayer);

        EditorGUILayout.EndVertical();
    }

    private void DrawAppearancePsbDropZone(Rect rect, SkyPrisonAnimationAssemblySlot slot)
    {
        bool hover = rect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(rect, hover ? new Color(0.18f, 0.30f, 0.42f, 1f) : new Color(0.105f, 0.105f, 0.115f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, hover ? new Color(0.48f, 0.72f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.10f));

        GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.86f, 0.92f, 1f, 1f) }
        };
        GUIStyle sub = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.72f, 0.76f, 0.82f, 1f) }
        };

        string main = slot.appearanceLayers != null && slot.appearanceLayers.Count > 0
            ? "已读取衣物 PSB 树：" + slot.appearancePackageKey
            : "拖入衣物 PSD / PSB 到这里";
        GUI.Label(new Rect(rect.x + 6f, rect.y + 10f, rect.width - 12f, 20f), main, title);
        GUI.Label(new Rect(rect.x + 6f, rect.y + 32f, rect.width - 12f, 18f), "自动识别 body/top/bottom/arm/leg/mask/dyeMask 与绑定建议", sub);

        Event e = Event.current;
        if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && rect.Contains(e.mousePosition))
        {
            string droppedPath = GetDraggedAppearanceAssetPath();
            bool valid = IsAppearancePsbPath(droppedPath);
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (valid)
                    ImportAppearancePsbIntoCurrentSlot(droppedPath);
                else
                    Debug.LogWarning("[SkyPrisonAnimation] 拖入的资源不是 PSD/PSB，或无法取得 AssetDatabase 路径。请拖入 .psd/.psb，外部文件会自动复制到 AppearanceImports。", slot != null ? null : null);
            }
            e.Use();
        }
    }

    private string GetDraggedAppearanceAssetPath()
    {
        if (DragAndDrop.paths != null)
        {
            for (int i = 0; i < DragAndDrop.paths.Length; i++)
            {
                string path = DragAndDrop.paths[i];
                if (IsAppearancePsbPath(path))
                    return path;
            }
        }

        if (DragAndDrop.objectReferences != null)
        {
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                UnityEngine.Object obj = DragAndDrop.objectReferences[i];
                if (obj == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (IsAppearancePsbPath(path))
                    return path;
            }
        }

        return string.Empty;
    }

    private bool IsAppearancePsbPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string lower = path.ToLowerInvariant();
        return lower.EndsWith(".psb") || lower.EndsWith(".psd");
    }

    private void ImportAppearancePsbIntoCurrentSlot(string path)
    {
        object undoSnapshot = state.CaptureStructureUndoSnapshot();
        if (state.ImportAppearancePsbAssetIntoSelectedSlot(path))
        {
            state.PushCapturedStructureUndo(undoSnapshot);
            GUI.changed = true;
        }
        else
        {
            Debug.LogWarning("[SkyPrisonAnimation] 衣物 PSB 导入失败，未读取到图层树：" + path + "。如果是外部文件，会自动复制到 AppearanceImports；请确认 PSD Importer 已生成 Sprite/Layer 资源。");
        }
    }

    private void DrawAppearanceDyeChannels(SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null || slot.dyeChannels == null || slot.dyeChannels.Count == 0)
            return;

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("染色通道（RGB 遮罩编号，按当前槽位/外貌包解释）", EditorStyles.miniBoldLabel);
        for (int i = 0; i < slot.dyeChannels.Count; i++)
        {
            SkyPrisonAppearanceDyeChannel ch = slot.dyeChannels[i];
            if (ch == null) continue;
            EditorGUILayout.BeginHorizontal();
            ch.enabled = EditorGUILayout.Toggle(ch.enabled, GUILayout.Width(18f));
            GUILayout.Label(ch.maskChannel + " →", GUILayout.Width(32f));
            ch.displayName = EditorGUILayout.TextField(ch.displayName);
            ch.previewColor = EditorGUILayout.ColorField(GUIContent.none, ch.previewColor, false, false, false, GUILayout.Width(46f));
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawAppearanceLayerTree(SkyPrisonAnimationAssemblySlot slot)
    {
        if (slot == null || slot.appearanceLayers == null || slot.appearanceLayers.Count == 0)
        {
            EditorGUILayout.HelpBox("当前槽位还没有导入衣物 PSB。", MessageType.Info);
            return;
        }

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("衣物 PSB 图层树", EditorStyles.miniBoldLabel);
        Rect treeRect = GUILayoutUtility.GetRect(10f, Mathf.Min(300f, Mathf.Max(150f, state.CountAppearanceLayers(slot.appearanceLayers) * 22f + 70f)), GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(treeRect, new Color(0.11f, 0.11f, 0.12f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(treeRect, new Color(1f, 1f, 1f, 0.08f));

        Rect view = new Rect(treeRect.x + 4f, treeRect.y + 4f, treeRect.width - 8f, treeRect.height - 8f);
        float contentHeight = Mathf.Max(view.height, CountVisibleAppearanceRows(slot.appearanceLayers) * 22f + 4f);
        Rect content = new Rect(0f, 0f, view.width - 16f, contentHeight);
        appearanceTreeScroll = GUI.BeginScrollView(view, appearanceTreeScroll, content, false, true);

        float y = 2f;
        DrawAppearanceLayerNodes(slot, slot.appearanceLayers, ref y, content.width);

        GUI.EndScrollView();
    }

    private Vector2 appearanceTreeScroll = Vector2.zero;

    private int CountVisibleAppearanceRows(List<SkyPrisonAppearancePsbLayerNode> nodes)
    {
        if (nodes == null) return 0;
        int c = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode n = nodes[i];
            if (n == null) continue;
            c++;
            if (n.expanded) c += CountVisibleAppearanceRows(n.children);
        }
        return c;
    }

    private void DrawAppearanceLayerNodes(SkyPrisonAnimationAssemblySlot slot, List<SkyPrisonAppearancePsbLayerNode> nodes, ref float y, float width)
    {
        if (nodes == null) return;
        for (int i = 0; i < nodes.Count; i++)
        {
            SkyPrisonAppearancePsbLayerNode n = nodes[i];
            if (n == null) continue;
            Rect row = new Rect(0f, y, width, 22f);
            DrawAppearanceLayerRow(slot, n, row);
            y += 22f;
            if (n.expanded) DrawAppearanceLayerNodes(slot, n.children, ref y, width);
        }
    }

    private void DrawAppearanceLayerRow(SkyPrisonAnimationAssemblySlot slot, SkyPrisonAppearancePsbLayerNode node, Rect row)
    {
        bool selected = slot.selectedAppearanceLayerKey == node.key;
        bool hover = row.Contains(Event.current.mousePosition);

        if (selected)
            EditorGUI.DrawRect(row, new Color(0.25f, 0.38f, 0.52f, 0.9f));
        else if (hover)
            EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.04f));

        Color nameColor = selected
            ? new Color(0.98f, 0.98f, 1f, 1f)
            : (node.isDyeMask ? new Color(0.62f, 0.86f, 1f, 1f) : new Color(0.82f, 0.84f, 0.86f, 1f));
        Color summaryColor = selected
            ? new Color(0.90f, 0.94f, 1f, 1f)
            : new Color(0.68f, 0.70f, 0.72f, 1f);
        Color iconColor = node.isDyeMask
            ? new Color(0.58f, 0.92f, 1f, 1f)
            : new Color(0.86f, 0.86f, 0.88f, 1f);

        GUIStyle foldStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.84f, 0.84f, 0.86f, 1f) }
        };
        GUIStyle iconStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = iconColor }
        };
        GUIStyle nameStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = nameColor }
        };
        GUIStyle summaryStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            clipping = TextClipping.Clip,
            normal = { textColor = summaryColor }
        };

        float x = row.x + node.depth * 14f + 2f;
        if (node.children != null && node.children.Count > 0)
        {
            Rect fold = new Rect(x, row.y + 2f, 16f, 18f);
            if (GUI.Button(fold, GUIContent.none, GUIStyle.none))
                node.expanded = !node.expanded;
            GUI.Label(fold, node.expanded ? "▼" : "▶", foldStyle);
        }
        x += 18f;

        node.visible = GUI.Toggle(new Rect(x, row.y + 3f, 18f, 18f), node.visible, GUIContent.none);
        x += 20f;

        string icon = node.isFolder ? "▣" : (node.isDyeMask ? "RGB" : "◆");
        GUI.Label(new Rect(x, row.y + 2f, 34f, 18f), icon, iconStyle);
        x += 36f;

        string name = node.name;
        if (node.isDyeMask) name += "  [Mask]";

        Rect nameRect = new Rect(x, row.y, Mathf.Max(40f, row.width - x - 210f), 22f);
        if (GUI.Button(nameRect, GUIContent.none, GUIStyle.none))
            slot.selectedAppearanceLayerKey = node.key;
        GUI.Label(nameRect, name, nameStyle);

        string summary = node.isFolder ? "" : BuildAppearanceLayerSummary(node);
        GUI.Label(new Rect(row.xMax - 210f, row.y + 2f, 206f, 18f), summary, summaryStyle);
    }

    private string BuildAppearanceLayerSummary(SkyPrisonAppearancePsbLayerNode node)
    {
        if (node == null) return "";
        if (node.isDyeMask) return "染色遮罩 → " + (string.IsNullOrEmpty(node.dyeMaskForLayerKey) ? "未配对" : "已配对");
        string s = node.slotKey + " / " + node.bindMode;
        if (!string.IsNullOrEmpty(node.bindTargetName)) s += " / " + node.bindTargetName;
        if (node.hasDyeMask) s += " / RGB";
        if (node.sortLayer == "BehindBody") s += " / 后层";
        return s;
    }

    private void DrawSelectedAppearanceLayerBinding(SkyPrisonAppearancePsbLayerNode selectedLayer)
    {
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("当前衣物图层识别", EditorStyles.miniBoldLabel);
        if (selectedLayer == null)
        {
            EditorGUILayout.HelpBox("从衣物 PSB 图层树里选择一个图层。", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("图层", selectedLayer.name);
        EditorGUILayout.LabelField("源路径", selectedLayer.sourceLayerPath);
        EditorGUILayout.LabelField("身体区域", selectedLayer.bodyRegion);
        EditorGUILayout.LabelField("槽位/部件", selectedLayer.slotKey + " / " + selectedLayer.partType);
        EditorGUILayout.LabelField("左右/段位", selectedLayer.side + " / " + selectedLayer.segment);
        EditorGUILayout.LabelField("排序", selectedLayer.sortLayer);
        EditorGUILayout.LabelField("绑定模式", selectedLayer.bindMode);
        EditorGUILayout.LabelField("绑定目标", string.IsNullOrEmpty(selectedLayer.bindTargetName) ? "-" : selectedLayer.bindTargetName);
        EditorGUILayout.LabelField("染色Mask", selectedLayer.isDyeMask ? "这是 Mask 图层" : (selectedLayer.hasDyeMask ? "已配对：" + selectedLayer.dyeMaskLayerKey : "未配对"));

        using (new EditorGUI.DisabledScope(selectedLayer.isFolder || selectedLayer.isDyeMask))
        {
            if (GUILayout.Button("设为硬绑定到当前节点"))
                state.BindSelectedAppearanceLayerToSelectedNode();
        }
    }

    private void DrawSelectedPropertiesContent(Rect rect)
    {
        Rect view = rect;
        Rect content = new Rect(0f, 0f, Mathf.Max(1f, view.width - 18f), 1450f);

        state.InspectorScroll = GUI.BeginScrollView(view, state.InspectorScroll, content, false, true);
        GUILayout.BeginArea(content);

        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 82f;

        SkyPrisonAnimationRigRow selectedBeforeDraw = state.GetSelectedRigRow();
        bool lockedOutByTrack = selectedBeforeDraw != null && !state.CanEditInspectorSelectedRowUnderTrackLock(selectedBeforeDraw);
        // 关键帧必须由时间线右键菜单显式创建。
        // 这里不能因为选中了轨道就自动 EnsureCurrentFrameKeyframeForRow，
        // 否则左键点轨道/选中节点时会偷偷生成关键帧。
        bool redirectAnimatedEditsToKeyframe = selectedBeforeDraw != null
            && state.ShouldRedirectAnimatedEditToTimelineKeyframe(selectedBeforeDraw)
            && state.IsSelectedTimelineKeyframeForRowAtCurrentFrame(selectedBeforeDraw);
        bool redirectLayerWeightEditsToKeyframe = selectedBeforeDraw != null
            && state.IsSelectedTimelineKeyframeForLayerWeightRowAtCurrentFrame(selectedBeforeDraw);
        bool oldUseManualRigLayerOffset = selectedBeforeDraw != null && selectedBeforeDraw.useManualRigLayerOffset;
        Vector2 oldManualRigLayerOffset = selectedBeforeDraw != null ? selectedBeforeDraw.manualRigLayerOffset : Vector2.zero;
        float oldOpacity = selectedBeforeDraw != null ? selectedBeforeDraw.opacity : 1f;
        bool oldUsePsbLayerWeight = selectedBeforeDraw != null && selectedBeforeDraw.usePsbLayerWeight;
        float oldPsbLayerWeight = selectedBeforeDraw != null ? selectedBeforeDraw.psbLayerWeight : 0f;
        float oldManualLayerWeightOffset = selectedBeforeDraw != null ? selectedBeforeDraw.manualLayerWeightOffset : 0f;

        bool overlayLayerWeightFromKeyframe = false;
        float keyframePsbLayerWeight;
        float keyframeManualLayerWeightOffset;
        if (selectedBeforeDraw != null
            && state.TryGetSelectedTimelineLayerWeightsForRow(selectedBeforeDraw, out keyframePsbLayerWeight, out keyframeManualLayerWeightOffset))
        {
            overlayLayerWeightFromKeyframe = true;
            selectedBeforeDraw.psbLayerWeight = keyframePsbLayerWeight;
            selectedBeforeDraw.manualLayerWeightOffset = keyframeManualLayerWeightOffset;
        }

        object undoSnapshot = state.CaptureStructureUndoSnapshot();
        EditorGUI.BeginChangeCheck();

        if (lockedOutByTrack && selectedBeforeDraw != null)
        {
            EditorGUILayout.HelpBox("当前时间线已锁定到轨道：" + state.GetTimelineTrackLabel(state.ActiveTimelineTrackKey) + "。节点/PSB图层的结构属性仍可编辑；只有当前帧动画参数不会写入这里。", MessageType.Info);
        }
        else if (redirectLayerWeightEditsToKeyframe && selectedBeforeDraw != null)
        {
            EditorGUILayout.HelpBox("当前正在编辑关键帧权重：修改 PSB权重 / 手动权重偏移只会写入当前选中的关键帧，不会污染图层默认值。", MessageType.None);
        }

        inspectorAnimatedControlsLocked = lockedOutByTrack;
        inspectorEditingSelectedFrameRig = redirectAnimatedEditsToKeyframe;
        inspectorEditingSelectedFrameLayerWeight = redirectLayerWeightEditsToKeyframe;
        DrawSelectedPropertiesGUILayout();
        inspectorAnimatedControlsLocked = false;
        inspectorEditingSelectedFrameRig = false;
        inspectorEditingSelectedFrameLayerWeight = false;

        bool inspectorChanged = EditorGUI.EndChangeCheck();
        if (inspectorChanged)
        {
            if ((redirectAnimatedEditsToKeyframe || redirectLayerWeightEditsToKeyframe) && selectedBeforeDraw != null)
            {
                // 选中关键帧时，右侧动画相关参数改的是“这个关键帧的值”，
                // 不污染结构行自身的默认值；两帧之间仍然只由补帧计算出来。
                state.UpdateSelectedTimelineKeyframeFromRow(selectedBeforeDraw);
                selectedBeforeDraw.useManualRigLayerOffset = oldUseManualRigLayerOffset;
                selectedBeforeDraw.manualRigLayerOffset = oldManualRigLayerOffset;
                selectedBeforeDraw.opacity = oldOpacity;
                selectedBeforeDraw.usePsbLayerWeight = oldUsePsbLayerWeight;
                selectedBeforeDraw.psbLayerWeight = oldPsbLayerWeight;
                selectedBeforeDraw.manualLayerWeightOffset = oldManualLayerWeightOffset;
            }

            state.PushCapturedStructureUndo(undoSnapshot);
        }
        else if (overlayLayerWeightFromKeyframe && selectedBeforeDraw != null)
        {
            // 只是为了显示关键帧值而临时覆盖了 Inspector 行，绘制结束后还原默认值。
            selectedBeforeDraw.psbLayerWeight = oldPsbLayerWeight;
            selectedBeforeDraw.manualLayerWeightOffset = oldManualLayerWeightOffset;
        }

        EditorGUIUtility.labelWidth = oldLabelWidth;
        GUILayout.EndArea();
        GUI.EndScrollView();
    }

    private void DrawSelectedPropertiesGUILayout()
    {
        SkyPrisonAnimationRigRow row = state.GetSelectedRigRow();
        if (row == null)
        {
            EditorGUILayout.HelpBox("当前没有选中节点 / 图层。", MessageType.Info);
            return;
        }

        string structureLabel = GetStructureScopeLabel(row);
        string selectedKey = GetSelectedScopeIdentity(row);
        if (lastSelectedPropertyScopeKey != selectedKey)
        {
            lastSelectedPropertyScopeKey = selectedKey;
            selectedPropertyScopeTab = (inspectorEditingSelectedFrameRig || inspectorEditingSelectedFrameLayerWeight) ? 1 : 0;
        }

        EditorGUILayout.HelpBox(
            "这里已经硬拆成两个面板：结构属性 和 当前帧属性。\n" +
            "物理、合成方式、蒙版、Shader、装配绑定只在结构属性里；权重、Rig位移、RigAngle旋转只在当前帧属性里。",
            MessageType.None);

        string[] tabs = { structureLabel, "当前帧属性" };
        int nextTab = GUILayout.Toolbar(Mathf.Clamp(selectedPropertyScopeTab, 0, 1), tabs, GUILayout.Height(24f));
        if (nextTab != selectedPropertyScopeTab)
        {
            selectedPropertyScopeTab = nextTab;
            GUI.FocusControl(null);
        }

        GUILayout.Space(6f);

        if (selectedPropertyScopeTab == 0)
        {
            DrawHardSeparatedScopeBox(structureLabel + "（结构属性 / 不随帧变化）", () =>
            {
                switch (state.StructureTab)
                {
                    case SkyPrisonAnimationStructureTab.PsbLayer:
                        DrawPsbLayerStructureProperties(row);
                        break;
                    case SkyPrisonAnimationStructureTab.Socket:
                        DrawSocketStructureProperties(row);
                        break;
                    default:
                        DrawRigNodeStructureProperties(row);
                        break;
                }
            });
        }
        else
        {
            DrawHardSeparatedScopeBox("当前帧属性（动画关键帧 / 随时间变化）", () =>
            {
                switch (state.StructureTab)
                {
                    case SkyPrisonAnimationStructureTab.PsbLayer:
                        DrawCurrentFrameLayerAnimationProperties(row);
                        break;
                    case SkyPrisonAnimationStructureTab.Socket:
                        DrawCurrentFrameSocketAnimationProperties(row);
                        break;
                    default:
                        DrawCurrentFrameRigAnimationProperties(row);
                        break;
                }
            });
        }

        GUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Button("锁定/解锁");
        GUILayout.Button("定位");
        GUILayout.Button("重映射");
        EditorGUILayout.EndHorizontal();
    }

    private string GetStructureScopeLabel(SkyPrisonAnimationRigRow row)
    {
        switch (state.StructureTab)
        {
            case SkyPrisonAnimationStructureTab.PsbLayer:
                return "PSB图层属性";
            case SkyPrisonAnimationStructureTab.Socket:
                return "Socket属性";
            default:
                return "节点属性";
        }
    }

    private string GetSelectedScopeIdentity(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return string.Empty;

        return ((int)state.StructureTab).ToString() + "|" + row.key + "|" + state.SelectedTimelineKeyframeIndex.ToString();
    }

    private void DrawHardSeparatedScopeBox(string title, System.Action draw)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        GUILayout.Space(3f);
        draw?.Invoke();
        EditorGUILayout.EndVertical();
    }

    private void DrawRigNodeStructureProperties(SkyPrisonAnimationRigRow row)
    {
        EditorGUILayout.LabelField("基础信息", EditorStyles.miniBoldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("Node Key", row.key);
        EditorGUILayout.TextField("父节点", string.IsNullOrWhiteSpace(row.parentKey) ? "-" : row.parentKey);
        EditorGUILayout.Toggle("有骨骼线", row.hasKey || row.useCustomBoneLine);
        EditorGUI.EndDisabledGroup();

        row.name = EditorGUILayout.TextField("显示名", row.name);

        if (row.isMeshDeformer)
        {
            DrawMeshDeformerStructureProperties(row);
            return;
        }

        DrawSemanticAndSide(row);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("默认绑定修正（结构属性）", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox("这里是节点自己的默认绑定修正，不是某一帧的动画。逐帧移动 / 旋转请在下面“当前帧属性”或预览骨骼线上操作。", MessageType.None);
        row.useManualRigOffset = EditorGUILayout.Toggle("启用骨骼线偏移", row.useManualRigOffset);
        EditorGUI.BeginDisabledGroup(!row.useManualRigOffset);
        row.manualRigOffset = EditorGUILayout.Vector2Field("骨骼线偏移", row.manualRigOffset);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("开启预览骨骼编辑", GUILayout.Height(20f)))
        {
            state.ShowRigEdit = true;
            state.ShowRigLines = true;
            state.ShowVisualParts = true;
            GUI.changed = true;
        }
        if (GUILayout.Button("清零默认偏移", GUILayout.Height(20f)))
        {
            row.manualRigOffset = Vector2.zero;
            row.useManualRigOffset = false;
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6f);
        DrawPhysicsStructureControls(row);

        GUILayout.Space(6f);
        DrawCapabilityFlags(row);
    }

    private string NormalizeMeshDeformScaleRule(string value)
    {
        if (string.Equals(value, "KeepOppositeFixed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "固定对边/对角", StringComparison.OrdinalIgnoreCase))
            return "对角固定";
        if (string.Equals(value, "FixedOppositeEdge", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "固定对边", StringComparison.OrdinalIgnoreCase))
            return "固定对边";
        if (string.Equals(value, "DiagonalFixed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "对角固定", StringComparison.OrdinalIgnoreCase))
            return "对角固定";
        if (string.Equals(value, "Symmetric", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value))
            return "中心对称伸缩";
        return "中心对称伸缩";
    }

    private void DrawMeshDeformScaleRuleAndBrightnessControls(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return;

        string[] scaleRuleNames = { "中心对称伸缩", "固定对边", "对角固定" };
        string currentScaleRule = NormalizeMeshDeformScaleRule(row.meshDeformScaleRule);
        int scaleRuleIndex = 0;
        for (int i = 0; i < scaleRuleNames.Length; i++)
        {
            if (string.Equals(scaleRuleNames[i], currentScaleRule, StringComparison.OrdinalIgnoreCase))
            {
                scaleRuleIndex = i;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        scaleRuleIndex = EditorGUILayout.Popup("红框伸缩规则", scaleRuleIndex, scaleRuleNames);
        if (EditorGUI.EndChangeCheck())
        {
            row.meshDeformScaleRule = scaleRuleNames[Mathf.Clamp(scaleRuleIndex, 0, scaleRuleNames.Length - 1)];
            GUI.changed = true;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        float nextBrightness = EditorGUILayout.Slider("UV亮度修正", row.meshDeformTextureBrightness <= 0f ? 1f : row.meshDeformTextureBrightness, 0.20f, 2.00f);
        if (EditorGUI.EndChangeCheck())
        {
            row.meshDeformTextureBrightness = Mathf.Clamp(nextBrightness, 0.20f, 2.00f);
            GUI.changed = true;
        }
        if (GUILayout.Button("重置", GUILayout.Width(48f)))
        {
            row.meshDeformTextureBrightness = 1f;
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMeshDeformerStructureProperties(SkyPrisonAnimationRigRow row)
    {
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("曲面变形", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("目标节点 Key", string.IsNullOrWhiteSpace(row.meshDeformTargetKey) ? "-" : row.meshDeformTargetKey);
        EditorGUILayout.TextField("节点类型", "曲面变形节点");
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox("这个节点作为目标节点的子控制器存在，不改变原节点身份。它会跟随父节点移动；后续拖拽网格点时，只写入这里的控制点偏移。", MessageType.None);

        DrawMeshDeformScaleRuleAndBrightnessControls(row);
        EditorGUILayout.HelpBox("红框伸缩规则属于曲面节点结构设置。UV亮度修正的新 1.00 = 旧实际亮度 0.50，用来抵消曲面 RT / GUI 贴回后发白的问题。", MessageType.None);

        EditorGUI.BeginChangeCheck();
        int nextColumns = Mathf.Clamp(EditorGUILayout.IntField("N / 横向列数", row.meshDeformColumns), 2, 16);
        int nextRows = Mathf.Clamp(EditorGUILayout.IntField("M / 纵向行数", row.meshDeformRows), 2, 16);
        if (EditorGUI.EndChangeCheck())
        {
            row.meshDeformColumns = nextColumns;
            row.meshDeformRows = nextRows;
            EnsureMeshDeformPointCount(row);
            GUI.changed = true;
        }

        EnsureMeshDeformPointCount(row);
        EditorGUILayout.LabelField("控制点数量", row.meshDeformPoints != null ? row.meshDeformPoints.Count.ToString() : "0");
    }

    private void EnsureMeshDeformPointCount(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return;

        row.meshDeformColumns = Mathf.Clamp(row.meshDeformColumns, 2, 16);
        row.meshDeformRows = Mathf.Clamp(row.meshDeformRows, 2, 16);
        if (row.meshDeformPoints == null)
            row.meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();

        Dictionary<string, SkyPrisonMeshDeformPoint> old = new Dictionary<string, SkyPrisonMeshDeformPoint>();
        for (int i = 0; i < row.meshDeformPoints.Count; i++)
        {
            SkyPrisonMeshDeformPoint p = row.meshDeformPoints[i];
            if (p == null) continue;
            old[p.x + "_" + p.y] = p;
        }

        row.meshDeformPoints.Clear();
        for (int y = 0; y < row.meshDeformRows; y++)
        {
            for (int x = 0; x < row.meshDeformColumns; x++)
            {
                string key = x + "_" + y;
                if (old.TryGetValue(key, out SkyPrisonMeshDeformPoint existing) && existing != null)
                    row.meshDeformPoints.Add(existing);
                else
                    row.meshDeformPoints.Add(new SkyPrisonMeshDeformPoint { x = x, y = y, offset = Vector2.zero });
            }
        }
    }

    private void DrawPsbLayerStructureProperties(SkyPrisonAnimationRigRow row)
    {
        EditorGUILayout.LabelField("基础信息", EditorStyles.miniBoldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("Layer Key", row.key);
        EditorGUILayout.TextField("源图层", string.IsNullOrWhiteSpace(row.sourceLayerPath) ? "PSB::" + row.key : row.sourceLayerPath);
        EditorGUILayout.TextField("绑定节点", string.IsNullOrWhiteSpace(row.boundRigKey) ? "-" : row.boundRigKey);
        EditorGUI.EndDisabledGroup();

        row.name = EditorGUILayout.TextField("显示名", row.name);
        DrawSemanticAndSide(row);

        GUILayout.Space(6f);
        DrawPhysicsStructureControls(row);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("图层合成 / 蒙版（结构属性）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("合成方式、蒙版参照、Shader 是这个图层长期的渲染规则，不写入某一帧。权重不放在这里。", MessageType.None);
        row.opacity = EditorGUILayout.Slider("默认不透明度", row.opacity, 0f, 1f);
        row.blendMode = DrawStringPopup("合成方式", row.blendMode, state.BlendModeOptions);
        DrawMaskReferenceControls(row);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Shader（结构属性）", EditorStyles.boldLabel);
        DrawLayerShaderObjectField(row);
        DrawLayerShaderParameterContainer(row);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("游戏染色（结构属性）", EditorStyles.boldLabel);
        row.useGameDyeRegion = EditorGUILayout.Toggle("参与染色", row.useGameDyeRegion || row.isDyeRegion);
        row.isDyeRegion = row.useGameDyeRegion;
        row.dyeRegionKey = DrawStringPopup("染色通道", row.dyeRegionKey, state.DyeRegionOptions);
        row.dyePreviewColor = EditorGUILayout.ColorField("编辑器预览色", row.dyePreviewColor);
        EditorGUILayout.HelpBox("这里记录的是游戏染色通道，不是固定最终颜色。真正颜色由游戏内染剂/DyeSet决定。", MessageType.None);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("装配绑定（结构属性）", EditorStyles.boldLabel);
        row.visualSlotKey = EditorGUILayout.TextField("所属插槽", string.IsNullOrEmpty(row.visualSlotKey) ? row.slotKey : row.visualSlotKey);
        row.slotKey = row.visualSlotKey;
        row.hideBaseBodyPart = EditorGUILayout.Toggle("遮挡基础身体", row.hideBaseBodyPart || row.hideBody);
        row.hideBody = row.hideBaseBodyPart;
        row.boundEquipmentKey = EditorGUILayout.TextField("绑定装备", row.boundEquipmentKey);
        row.equipmentSourceKey = EditorGUILayout.TextField("装备来源", row.equipmentSourceKey);
    }

    private Shader ResolveLayerShaderReference(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return null;

        if (row.renderShader != null)
            return row.renderShader;

        if (string.IsNullOrWhiteSpace(row.shaderKey))
            return null;

        Shader shader = null;

        // 兼容旧版本：shaderKey 可能存的是 Asset 路径。
        if (row.shaderKey.StartsWith("Assets/"))
            shader = AssetDatabase.LoadAssetAtPath<Shader>(row.shaderKey);

        // 兼容新版本：shaderKey 也可能存的是 Shader.name。
        if (shader == null)
            shader = Shader.Find(row.shaderKey);

        if (shader != null)
            row.renderShader = shader;

        return shader;
    }

    private string GetLayerShaderKey(Shader shader)
    {
        if (shader == null)
            return string.Empty;

        string path = AssetDatabase.GetAssetPath(shader);
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        return shader.name;
    }

    private void DrawLayerShaderObjectField(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return;

        Shader currentShader = ResolveLayerShaderReference(row);

        Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        Rect labelRect = new Rect(rect.x, rect.y, 96f, rect.height);
        Rect fieldRect = new Rect(labelRect.xMax + 6f, rect.y, Mathf.Max(1f, rect.width - 102f), rect.height);

        EditorGUI.LabelField(labelRect, "图层Shader");

        Shader droppedShader;
        bool dragAccepted = HandleShaderDragAndDrop(fieldRect, out droppedShader);

        EditorGUI.BeginChangeCheck();
        Shader newShader = (Shader)EditorGUI.ObjectField(
            fieldRect,
            currentShader,
            typeof(Shader),
            false);
        bool objectFieldChanged = EditorGUI.EndChangeCheck();

        if (dragAccepted)
        {
            ApplyLayerShader(row, droppedShader);
            return;
        }

        if (objectFieldChanged || newShader != currentShader)
        {
            ApplyLayerShader(row, newShader);
            return;
        }

        // 兜底：如果旧工程只保存了 shaderKey，Object 引用为空，Resolve 成功后也要同步参数和刷新。
        if (currentShader != null && row.renderShader == null)
        {
            ApplyLayerShader(row, currentShader);
            return;
        }

        if (currentShader != null)
            SyncShaderParameterOverrides(row, false);
    }

    private bool HandleShaderDragAndDrop(Rect rect, out Shader shader)
    {
        shader = null;

        Event e = Event.current;
        if (e == null)
            return false;

        if (!rect.Contains(e.mousePosition))
            return false;

        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            return false;

        UnityEngine.Object[] refs = DragAndDrop.objectReferences;
        if (refs == null || refs.Length == 0)
            return false;

        Shader found = null;
        for (int i = 0; i < refs.Length; i++)
        {
            UnityEngine.Object obj = refs[i];
            if (obj is Shader directShader)
            {
                found = directShader;
                break;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path))
            {
                Shader loaded = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (loaded != null)
                {
                    found = loaded;
                    break;
                }
            }
        }

        if (found == null)
            return false;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            shader = found;
            e.Use();
            return true;
        }

        e.Use();
        return false;
    }

    private void ApplyLayerShader(SkyPrisonAnimationRigRow row, Shader shader)
    {
        if (row == null)
            return;

        row.renderShader = shader;

        if (shader == null)
        {
            row.shaderKey = string.Empty;
            row.shaderParameterShaderKey = string.Empty;
            if (row.shaderParameters != null)
                row.shaderParameters.Clear();
            row.shaderParametersExpanded = false;
        }
        else
        {
            row.shaderKey = GetLayerShaderKey(shader);
            row.shaderParameterShaderKey = string.Empty;
            SyncShaderParameterOverrides(row, true);
            row.shaderParametersExpanded = true;
        }

        GUI.changed = true;
        RequestShaderInspectorRefresh();
    }

    private void RequestShaderInspectorRefresh()
    {
        EditorApplication.delayCall += () =>
        {
            SceneView.RepaintAll();
            InternalEditorUtility.RepaintAllViews();

            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused != null)
                focused.Repaint();
        };
    }

    private static readonly HashSet<string> HiddenLayerShaderProperties = new HashSet<string>
    {
        "_MainTex",
        "_Color",
        "_SkyPrisonTime",
        "_PreviewTime",
        "_LayerTexelSize",
        "_SkyPrisonLayerRect",
        "_SkyPrisonLayerRectPixels",
    };

    private void DrawLayerShaderParameterContainer(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.renderShader == null)
            return;

        EnsureShaderParameterList(row);

        string scrollKey = BuildLayerShaderParameterScrollKey(row);
        if (lastLayerShaderParametersScrollKey != scrollKey)
        {
            lastLayerShaderParametersScrollKey = scrollKey;
            layerShaderParametersScroll = Vector2.zero;
        }

        GUILayout.Space(4f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        row.shaderParametersExpanded = EditorGUILayout.Foldout(row.shaderParametersExpanded, "Shader 参数", true);

        if (row.shaderParametersExpanded)
        {
            EditorGUILayout.HelpBox("这里是当前图层 Shader 的可调参数，只影响这个 PSB 图层的预览效果。_MainTex、_Color、时间、图层范围等系统参数会自动隐藏。", MessageType.None);

            int visibleCount = CountVisibleLayerShaderParameters(row);

            if (row.shaderParameters == null || visibleCount == 0)
            {
                EditorGUILayout.LabelField("这个 Shader 没有可手动调节的公开参数。", EditorStyles.miniLabel);
            }
            else
            {
                float contentHeight = visibleCount * LayerShaderParameterRowHeight + LayerShaderParameterFooterHeight;
                float scrollHeight = Mathf.Min(LayerShaderParameterScrollMaxHeight, Mathf.Max(64f, contentHeight));

                Rect scrollOuterRect = EditorGUILayout.GetControlRect(false, scrollHeight);
                EditorGUI.DrawRect(scrollOuterRect, new Color(0f, 0f, 0f, 0.10f));
                SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(scrollOuterRect, new Color(1f, 1f, 1f, 0.06f));

                Rect scrollViewRect = new Rect(
                    scrollOuterRect.x + 4f,
                    scrollOuterRect.y + 4f,
                    Mathf.Max(1f, scrollOuterRect.width - 8f),
                    Mathf.Max(1f, scrollOuterRect.height - 8f));

                Rect contentRect = new Rect(
                    0f,
                    0f,
                    Mathf.Max(1f, scrollViewRect.width - 16f),
                    Mathf.Max(scrollViewRect.height, contentHeight));

                layerShaderParametersScroll = GUI.BeginScrollView(
                    scrollViewRect,
                    layerShaderParametersScroll,
                    contentRect,
                    false,
                    true);

                float y = 0f;
                for (int i = 0; i < row.shaderParameters.Count; i++)
                {
                    SkyPrisonAnimationShaderPropertyOverride prop = row.shaderParameters[i];
                    if (prop == null || string.IsNullOrEmpty(prop.propertyName))
                        continue;

                    Rect rowRect = new Rect(0f, y, contentRect.width, LayerShaderParameterRowHeight);
                    DrawSingleLayerShaderParameterRow(row.renderShader, prop, rowRect);
                    y += LayerShaderParameterRowHeight;
                }

                y += 4f;
                Rect resetRect = new Rect(
                    Mathf.Max(0f, contentRect.width - 92f),
                    y,
                    88f,
                    20f);

                if (GUI.Button(resetRect, "重置参数"))
                {
                    SyncShaderParameterOverrides(row, true);
                    layerShaderParametersScroll = Vector2.zero;
                    GUI.changed = true;
                }

                GUI.EndScrollView();
            }
        }

        EditorGUILayout.EndVertical();
    }

    private string BuildLayerShaderParameterScrollKey(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return string.Empty;

        string shaderKey = row.renderShader != null ? row.renderShader.name : row.shaderKey;
        return $"{row.key}|{row.name}|{shaderKey}";
    }

    private int CountVisibleLayerShaderParameters(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.shaderParameters == null)
            return 0;

        int count = 0;
        for (int i = 0; i < row.shaderParameters.Count; i++)
        {
            SkyPrisonAnimationShaderPropertyOverride prop = row.shaderParameters[i];
            if (prop != null && !string.IsNullOrEmpty(prop.propertyName))
                count++;
        }

        return count;
    }

    private void DrawSingleLayerShaderParameterRow(Shader shader, SkyPrisonAnimationShaderPropertyOverride prop, Rect rowRect)
    {
        Rect toggleRect = new Rect(rowRect.x, rowRect.y + 2f, 18f, 18f);
        Rect fieldRect = new Rect(toggleRect.xMax + 4f, rowRect.y, Mathf.Max(1f, rowRect.width - 22f), rowRect.height - 2f);

        prop.enabled = EditorGUI.Toggle(toggleRect, prop.enabled);

        using (new EditorGUI.DisabledScope(!prop.enabled))
        {
            DrawSingleLayerShaderParameter(fieldRect, shader, prop);
        }
    }

    private void DrawSingleLayerShaderParameter(Rect rect, Shader shader, SkyPrisonAnimationShaderPropertyOverride prop)
    {
        if (prop == null)
            return;

        string label = string.IsNullOrWhiteSpace(prop.displayName) ? prop.propertyName : prop.displayName;
        Rect labelRect = new Rect(rect.x, rect.y + 2f, Mathf.Min(128f, rect.width * 0.42f), EditorGUIUtility.singleLineHeight);
        Rect valueRect = new Rect(labelRect.xMax + 4f, rect.y + 1f, Mathf.Max(1f, rect.xMax - labelRect.xMax - 4f), EditorGUIUtility.singleLineHeight);

        EditorGUI.LabelField(labelRect, label);

        switch (prop.propertyKind)
        {
            case SkyPrisonAnimationShaderPropertyKind.Range:
            {
                float min, max;
                GetShaderRangeLimits(shader, prop.propertyName, out min, out max);
                prop.floatValue = EditorGUI.Slider(valueRect, GUIContent.none, prop.floatValue, min, max);
                break;
            }
            case SkyPrisonAnimationShaderPropertyKind.Float:
                prop.floatValue = EditorGUI.FloatField(valueRect, GUIContent.none, prop.floatValue);
                break;
            case SkyPrisonAnimationShaderPropertyKind.Color:
                prop.colorValue = EditorGUI.ColorField(valueRect, GUIContent.none, prop.colorValue, false, true, false);
                break;
            case SkyPrisonAnimationShaderPropertyKind.Vector:
                prop.vectorValue = EditorGUI.Vector4Field(valueRect, GUIContent.none, prop.vectorValue);
                break;
            case SkyPrisonAnimationShaderPropertyKind.Texture:
                prop.textureValue = (Texture)EditorGUI.ObjectField(valueRect, GUIContent.none, prop.textureValue, typeof(Texture), false);
                break;
            default:
                EditorGUI.LabelField(valueRect, "不支持的参数类型");
                break;
        }
    }

    private void DrawSingleLayerShaderParameter(Shader shader, SkyPrisonAnimationShaderPropertyOverride prop)
    {
        string label = string.IsNullOrWhiteSpace(prop.displayName) ? prop.propertyName : prop.displayName;

        switch (prop.propertyKind)
        {
            case SkyPrisonAnimationShaderPropertyKind.Range:
            {
                float min, max;
                GetShaderRangeLimits(shader, prop.propertyName, out min, out max);
                prop.floatValue = EditorGUILayout.Slider(label, prop.floatValue, min, max);
                break;
            }
            case SkyPrisonAnimationShaderPropertyKind.Float:
                prop.floatValue = EditorGUILayout.FloatField(label, prop.floatValue);
                break;
            case SkyPrisonAnimationShaderPropertyKind.Color:
                prop.colorValue = EditorGUILayout.ColorField(label, prop.colorValue);
                break;
            case SkyPrisonAnimationShaderPropertyKind.Vector:
                prop.vectorValue = EditorGUILayout.Vector4Field(label, prop.vectorValue);
                break;
            case SkyPrisonAnimationShaderPropertyKind.Texture:
                prop.textureValue = (Texture)EditorGUILayout.ObjectField(label, prop.textureValue, typeof(Texture), false);
                break;
            default:
                EditorGUILayout.LabelField(label, "不支持的参数类型");
                break;
        }
    }

    private void SyncShaderParameterOverrides(SkyPrisonAnimationRigRow row, bool forceReset)
    {
        if (row == null)
            return;

        if (row.renderShader == null)
        {
            row.shaderParameterShaderKey = string.Empty;
            if (row.shaderParameters != null)
                row.shaderParameters.Clear();
            return;
        }

        row.shaderKey = GetLayerShaderKey(row.renderShader);
        if (forceReset || row.shaderParameters == null || row.shaderParameterShaderKey != row.shaderKey)
        {
            row.shaderParameters = BuildDefaultShaderParameterOverrides(row.renderShader);
            row.shaderParameterShaderKey = row.shaderKey;
            return;
        }

        EnsureShaderParameterList(row);
    }

    private void EnsureShaderParameterList(SkyPrisonAnimationRigRow row)
    {
        if (row == null || row.renderShader == null)
            return;

        if (row.shaderParameters == null)
            row.shaderParameters = new List<SkyPrisonAnimationShaderPropertyOverride>();

        List<SkyPrisonAnimationShaderPropertyOverride> defaults = BuildDefaultShaderParameterOverrides(row.renderShader);
        for (int i = 0; i < defaults.Count; i++)
        {
            SkyPrisonAnimationShaderPropertyOverride def = defaults[i];
            if (FindShaderParameter(row.shaderParameters, def.propertyName) == null)
                row.shaderParameters.Add(def);
        }

        for (int i = row.shaderParameters.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationShaderPropertyOverride prop = row.shaderParameters[i];
            if (prop == null || FindShaderParameter(defaults, prop.propertyName) == null)
                row.shaderParameters.RemoveAt(i);
        }

        row.shaderParameterShaderKey = row.shaderKey;
    }

    private List<SkyPrisonAnimationShaderPropertyOverride> BuildDefaultShaderParameterOverrides(Shader shader)
    {
        List<SkyPrisonAnimationShaderPropertyOverride> list = new List<SkyPrisonAnimationShaderPropertyOverride>();
        if (shader == null)
            return list;

        Material temp = null;
        try
        {
            temp = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                if (string.IsNullOrEmpty(name) || HiddenLayerShaderProperties.Contains(name))
                    continue;

                ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(shader, i);
                SkyPrisonAnimationShaderPropertyOverride prop = new SkyPrisonAnimationShaderPropertyOverride
                {
                    propertyName = name,
                    displayName = ShaderUtil.GetPropertyDescription(shader, i),
                    enabled = true,
                };

                switch (type)
                {
                    case ShaderUtil.ShaderPropertyType.Range:
                        prop.propertyKind = SkyPrisonAnimationShaderPropertyKind.Range;
                        prop.floatValue = temp.HasProperty(name) ? temp.GetFloat(name) : 0f;
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                        prop.propertyKind = SkyPrisonAnimationShaderPropertyKind.Float;
                        prop.floatValue = temp.HasProperty(name) ? temp.GetFloat(name) : 0f;
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        prop.propertyKind = SkyPrisonAnimationShaderPropertyKind.Color;
                        prop.colorValue = temp.HasProperty(name) ? temp.GetColor(name) : Color.white;
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        prop.propertyKind = SkyPrisonAnimationShaderPropertyKind.Vector;
                        prop.vectorValue = temp.HasProperty(name) ? temp.GetVector(name) : Vector4.zero;
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        prop.propertyKind = SkyPrisonAnimationShaderPropertyKind.Texture;
                        prop.textureValue = temp.HasProperty(name) ? temp.GetTexture(name) : null;
                        break;
                    default:
                        continue;
                }

                if (string.IsNullOrWhiteSpace(prop.displayName))
                    prop.displayName = name;

                list.Add(prop);
            }
        }
        finally
        {
            if (temp != null)
                UnityEngine.Object.DestroyImmediate(temp);
        }

        return list;
    }

    private SkyPrisonAnimationShaderPropertyOverride FindShaderParameter(List<SkyPrisonAnimationShaderPropertyOverride> list, string propertyName)
    {
        if (list == null || string.IsNullOrEmpty(propertyName))
            return null;

        for (int i = 0; i < list.Count; i++)
        {
            SkyPrisonAnimationShaderPropertyOverride p = list[i];
            if (p != null && p.propertyName == propertyName)
                return p;
        }
        return null;
    }

    private void GetShaderRangeLimits(Shader shader, string propertyName, out float min, out float max)
    {
        min = 0f;
        max = 1f;
        if (shader == null || string.IsNullOrEmpty(propertyName))
            return;

        int count = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < count; i++)
        {
            if (ShaderUtil.GetPropertyName(shader, i) != propertyName)
                continue;

            try
            {
                float a = ShaderUtil.GetRangeLimits(shader, i, 1);
                float b = ShaderUtil.GetRangeLimits(shader, i, 2);
                min = Mathf.Min(a, b);
                max = Mathf.Max(a, b);
                if (Mathf.Approximately(min, max))
                {
                    min = 0f;
                    max = 1f;
                }
            }
            catch
            {
                min = 0f;
                max = 1f;
            }
            return;
        }
    }

    private void DrawSocketStructureProperties(SkyPrisonAnimationRigRow row)
    {
        EditorGUILayout.LabelField("基础信息", EditorStyles.miniBoldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("Socket Key", row.key);
        EditorGUILayout.TextField("父节点", string.IsNullOrWhiteSpace(row.parentKey) ? "-" : row.parentKey);
        EditorGUI.EndDisabledGroup();

        row.name = EditorGUILayout.TextField("显示名", row.name);
        DrawSemanticAndSide(row);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("挂点默认设置（结构属性）", EditorStyles.miniBoldLabel);
        row.visualSlotKey = EditorGUILayout.TextField("显示插槽", string.IsNullOrEmpty(row.visualSlotKey) ? row.slotKey : row.visualSlotKey);
        row.slotKey = row.visualSlotKey;
        row.boundEquipmentKey = EditorGUILayout.TextField("默认绑定资源", row.boundEquipmentKey);

        GUILayout.Space(6f);
        DrawPhysicsStructureControls(row);
        GUILayout.Space(6f);
        DrawCapabilityFlags(row);
    }

    private void DrawCurrentFrameRigAnimationProperties(SkyPrisonAnimationRigRow row)
    {
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("动画参数", EditorStyles.miniBoldLabel);
        if (row != null && row.isMeshDeformer)
        {
            EditorGUILayout.HelpBox("曲面变形节点本身不记录普通 Rig 位移/旋转关键帧。网格点、方向柄、红框变换会单独写入曲面控制点轨道。", MessageType.Info);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.IntField("网格列数", row.meshDeformColumns);
            EditorGUILayout.IntField("网格行数", row.meshDeformRows);
            EditorGUILayout.IntField("控制点数量", row.meshDeformPoints != null ? row.meshDeformPoints.Count : 0);
            EditorGUI.EndDisabledGroup();

            string[] scaleRuleNames = { "中心对称伸缩", "固定对边", "对角固定" };
            string[] scaleRuleValues = { "中心对称伸缩", "固定对边", "对角固定" };
            int scaleRuleIndex = 0;
            string currentScaleRule = string.IsNullOrEmpty(row.meshDeformScaleRule) ? "中心对称伸缩" : row.meshDeformScaleRule;
            if (string.Equals(currentScaleRule, "KeepOppositeFixed", StringComparison.OrdinalIgnoreCase) || string.Equals(currentScaleRule, "固定对边/对角", StringComparison.OrdinalIgnoreCase))
                currentScaleRule = "对角固定";
            else if (string.Equals(currentScaleRule, "FixedOppositeEdge", StringComparison.OrdinalIgnoreCase))
                currentScaleRule = "固定对边";
            else if (string.Equals(currentScaleRule, "DiagonalFixed", StringComparison.OrdinalIgnoreCase))
                currentScaleRule = "对角固定";
            else if (string.Equals(currentScaleRule, "Symmetric", StringComparison.OrdinalIgnoreCase))
                currentScaleRule = "中心对称伸缩";

            for (int i = 0; i < scaleRuleValues.Length; i++)
            {
                if (string.Equals(scaleRuleValues[i], currentScaleRule, StringComparison.OrdinalIgnoreCase))
                {
                    scaleRuleIndex = i;
                    break;
                }
            }

            EditorGUI.BeginChangeCheck();
            scaleRuleIndex = EditorGUILayout.Popup("红框伸缩规则", scaleRuleIndex, scaleRuleNames);
            if (EditorGUI.EndChangeCheck())
            {
                row.meshDeformScaleRule = scaleRuleValues[Mathf.Clamp(scaleRuleIndex, 0, scaleRuleValues.Length - 1)];
                GUI.changed = true;
            }
            EditorGUILayout.HelpBox("中心对称伸缩：拖边/角时以红框中心缩放。固定对边：拖边时保持对边两个点不动，拖角仍按中心缩放。对角固定：拖角时保持对角不动；拖边时也保持对边不动。", MessageType.None);

            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("复原当前帧曲面", GUILayout.Height(22f)))
            {
                if (state != null)
                {
                    state.ResetCurrentFrameMeshDeformerToRect(row);
                    GUI.changed = true;
                }
            }
            if (GUILayout.Button("复原本动作曲面帧", GUILayout.Height(22f)))
            {
                if (state != null && EditorUtility.DisplayDialog("复原本动作曲面帧", "确定将当前动作中这个曲面节点的所有曲面关键帧复原为规整矩形吗？", "复原", "取消"))
                {
                    state.ResetAllMeshDeformerKeyframesToRect(row);
                    GUI.changed = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("复原会清空主控制点偏移和方向柄偏移，使曲面回到原始 N×M 规整矩形。当前帧复原会在当前时间点生成 / 更新曲面关键帧。", MessageType.None);
            return;
        }
        if (inspectorAnimatedControlsLocked)
            EditorGUILayout.HelpBox("当前时间线锁定到其它轨道。这里不会写入这个节点的当前帧关键帧。", MessageType.Info);
        else if (inspectorEditingSelectedFrameRig)
            EditorGUILayout.HelpBox("当前选中了此节点在当前帧的关键帧。位移 / 旋转修改只属于这一帧。", MessageType.None);
        else
            EditorGUILayout.HelpBox("未选中当前帧关键帧。请通过时间线创建/选中 Rig 或 RigAngle 关键帧，或直接在预览窗口拖 Root/Head 端。", MessageType.None);

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.Vector2Field("当前默认骨骼偏移", row.manualRigOffset);
        EditorGUILayout.Vector2Field("当前帧图层偏移", row.manualRigLayerOffset);
        EditorGUILayout.FloatField("当前动作时间", state.CurrentTime);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawCurrentFrameLayerAnimationProperties(SkyPrisonAnimationRigRow row)
    {
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("动画参数", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox("权重 / 临时压层只属于当前帧或当前动作的关键帧，不再作为 PSB 图层结构属性。", MessageType.None);

        bool canEditFrameWeight = inspectorEditingSelectedFrameLayerWeight;
        if (canEditFrameWeight)
            EditorGUILayout.HelpBox("当前正在编辑选中的权重关键帧。修改后不会污染图层默认结构。", MessageType.None);
        else
            EditorGUILayout.HelpBox("未选中当前帧权重关键帧时，权重数值只读。需要修改请先记录当前帧权重，或在时间线上选中对应权重关键帧。", MessageType.Info);

        string weightScope = state.IsSelectedTimelineKeyframeForLayerWeightRowAtCurrentFrame(row) ? " [关键帧]" : " [只读]";
        EditorGUI.BeginDisabledGroup(!canEditFrameWeight);
        bool newUsePsbLayerWeight = EditorGUILayout.Toggle("使用PSB原始权重" + weightScope, row.usePsbLayerWeight);
        if (canEditFrameWeight && newUsePsbLayerWeight != row.usePsbLayerWeight)
        {
            state.PushStructureUndo();
            row.usePsbLayerWeight = newUsePsbLayerWeight;
            GUI.changed = true;
        }
        row.psbLayerWeight = DrawUndoableDelayedFloatField("PSB权重" + weightScope, row.psbLayerWeight, "SPAW_PSB_WEIGHT_" + row.key);
        row.manualLayerWeightOffset = DrawUndoableDelayedFloatField("手动权重偏移" + weightScope, row.manualLayerWeightOffset, "SPAW_MANUAL_WEIGHT_OFFSET_" + row.key);
        EditorGUI.EndDisabledGroup();

        float effectiveLayerWeight = state.GetEffectiveLayerOrderWeight(row);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.FloatField("当前有效权重", effectiveLayerWeight);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("记录当前帧权重", GUILayout.Height(20f)))
        {
            state.SetLayerOrderKeyframe(row, state.CurrentTime, effectiveLayerWeight);
            row.hasKey = true;
            GUI.changed = true;
        }
        if (GUILayout.Button("清空本动作权重帧", GUILayout.Height(20f)))
        {
            state.ClearLayerOrderKeyframes(row);
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();

        DrawLayerWeightBatchControls(row);
    }

    private void DrawCurrentFrameSocketAnimationProperties(SkyPrisonAnimationRigRow row)
    {
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("动画参数", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox("Socket 的帧动画后续可以用于武器挂点、特效挂点的临时偏移。当前版本先保持只读，避免和结构挂点混在一起。", MessageType.None);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.Vector3Field("Local Position", Vector3.zero);
        EditorGUILayout.Vector3Field("Local Rotation", Vector3.zero);
        EditorGUILayout.Vector3Field("Local Scale", Vector3.one);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawCapabilityFlags(SkyPrisonAnimationRigRow row)
    {
        EditorGUILayout.LabelField("能力标记（结构属性）", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.Toggle("可产生脚步声", row.semantic.Contains("Foot"));
        EditorGUILayout.Toggle("可作为视线起点", row.semantic.Contains("Head") || row.semantic.Contains("Eye"));
        EditorGUILayout.Toggle("可挂武器", row.semantic.Contains("Hand") || row.semantic.Contains("Claw"));
        EditorGUILayout.Toggle("可作为攻击锚点", row.semantic.Contains("Claw") || row.semantic.Contains("Socket"));
        EditorGUI.EndDisabledGroup();
    }


    private void DrawPhysicsStructureControls(SkyPrisonAnimationRigRow row)
    {
        EditorGUILayout.LabelField("物理引擎（结构属性）", EditorStyles.boldLabel);
        state.EnsureDefaultPhysicsPresets();

        using (new EditorGUI.DisabledScope(row.isFolder))
        {
            bool nextUsePhysics = EditorGUILayout.Toggle("参与物理影响", row.usePhysicsInfluence);
            if (nextUsePhysics != row.usePhysicsInfluence)
            {
                row.usePhysicsInfluence = nextUsePhysics;
                if (row.usePhysicsInfluence)
                {
                    if (row.physicsInfluenceStrength <= 0f) row.physicsInfluenceStrength = 0.35f;
                    if (string.IsNullOrWhiteSpace(row.physicsPresetKey))
                    {
                        string guessed = state.GuessPhysicsPresetKeyForRow(row);
                        if (!string.IsNullOrWhiteSpace(guessed)) row.physicsPresetKey = guessed;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!row.usePhysicsInfluence))
            {
                int presetIndex = state.GetPhysicsPresetIndex(row.physicsPresetKey);
                int nextPresetIndex = EditorGUILayout.Popup("物理预设", presetIndex, state.GetPhysicsPresetLabels());
                if (nextPresetIndex != presetIndex)
                {
                    row.physicsPresetKey = state.GetPhysicsPresetKeyByIndex(nextPresetIndex);
                    SkyPrisonPhysicsPreset selected = state.FindPhysicsPreset(row.physicsPresetKey);
                    if (selected != null && row.physicsInfluenceStrength <= 0.001f)
                        row.physicsInfluenceStrength = selected.defaultBlend;
                }

                row.physicsInfluenceStrength = EditorGUILayout.Slider("影响强度", row.physicsInfluenceStrength, 0f, 1f);
                row.physicsLocalDelayMultiplier = EditorGUILayout.Slider("局部延迟倍率", row.physicsLocalDelayMultiplier, 0f, 3f);
                row.physicsLocalSwingMultiplier = EditorGUILayout.Slider("局部摆动倍率", row.physicsLocalSwingMultiplier, 0f, 3f);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("自动推荐", GUILayout.Height(20f)))
                {
                    string guessed = state.GuessPhysicsPresetKeyForRow(row);
                    if (!string.IsNullOrWhiteSpace(guessed))
                    {
                        row.physicsPresetKey = guessed;
                        SkyPrisonPhysicsPreset selected = state.FindPhysicsPreset(guessed);
                        if (selected != null) row.physicsInfluenceStrength = selected.defaultBlend;
                    }
                    GUI.changed = true;
                }
                if (GUILayout.Button("新建预设", GUILayout.Height(20f)))
                {
                    SkyPrisonPhysicsPreset created = state.CreatePhysicsPreset("新物理预设", 3);
                    row.physicsPresetKey = created.presetKey;
                    row.usePhysicsInfluence = true;
                    GUI.changed = true;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.HelpBox("节点 / PSB 图层这里只保存：是否启用物理、引用哪个预设、局部强度倍率。振子节数和每节参数保存在下方物理预设中，不属于某一帧。", MessageType.None);

        SkyPrisonPhysicsPreset preset = state.FindPhysicsPreset(row.physicsPresetKey);
        if (preset == null && state.PhysicsPresets.Count > 0 && state.SelectedPhysicsPresetIndex >= 0 && state.SelectedPhysicsPresetIndex < state.PhysicsPresets.Count)
            preset = state.PhysicsPresets[state.SelectedPhysicsPresetIndex];

        using (new EditorGUI.DisabledScope(!row.usePhysicsInfluence || preset == null))
        {
            DrawPhysicsOscillatorStatusWindow(row, preset);
        }

        EditorGUILayout.BeginHorizontal();
        state.PhysicsPresetEditorExpanded = EditorGUILayout.Foldout(state.PhysicsPresetEditorExpanded, "物理模型预设 / 振子链", true);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(preset == null))
        {
            if (GUILayout.Button("复制", GUILayout.Width(46f), GUILayout.Height(18f)))
            {
                SkyPrisonPhysicsPreset duplicated = state.DuplicatePhysicsPreset(preset);
                row.physicsPresetKey = duplicated.presetKey;
                GUI.changed = true;
            }
            if (GUILayout.Button("删除", GUILayout.Width(46f), GUILayout.Height(18f)))
            {
                if (EditorUtility.DisplayDialog("删除物理预设", "确定删除当前物理预设吗？引用它的节点会清空预设引用。", "删除", "取消"))
                {
                    string deletingKey = preset == null ? string.Empty : preset.presetKey;
                    state.DeletePhysicsPreset(preset);
                    if (row.physicsPresetKey == deletingKey) row.physicsPresetKey = string.Empty;
                    GUI.changed = true;
                    preset = null;
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (state.PhysicsPresetEditorExpanded)
            DrawPhysicsPresetEditor(preset);
    }

    private void DrawPhysicsOscillatorStatusWindow(SkyPrisonAnimationRigRow row, SkyPrisonPhysicsPreset preset)
    {
        EditorGUILayout.LabelField("振子状态（只读预览）", EditorStyles.miniBoldLabel);
        Rect rect = GUILayoutUtility.GetRect(10f, 150f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.105f, 0.11f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, new Color(1f, 1f, 1f, 0.10f));

        SkyPrisonPhysicsOscillatorStatus status = FindPhysicsStatusForRow(row);
        bool live = status != null && status.active && status.points != null && status.points.Count >= 2;

        List<Vector2> points = live ? status.points : BuildStaticPhysicsStatusPoints(rect, preset);
        if (points == null || points.Count == 0)
        {
            GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 22f), "没有可显示的振子。", EditorStyles.miniLabel);
            return;
        }

        List<Vector2> fitted = live ? FitPhysicsStatusPointsToRect(points, rect) : points;

        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = live ? new Color(0.35f, 0.78f, 1f, 0.95f) : new Color(0.55f, 0.62f, 0.70f, 0.55f);
        for (int i = 0; i < fitted.Count - 1; i++)
            Handles.DrawAAPolyLine(2.2f, fitted[i], fitted[i + 1]);

        for (int i = 0; i < fitted.Count; i++)
        {
            Handles.color = i == 0 ? new Color(1f, 1f, 1f, 0.95f) : (live ? new Color(0.25f, 0.68f, 1f, 0.98f) : new Color(0.75f, 0.80f, 0.86f, 0.72f));
            Handles.DrawSolidDisc(fitted[i], Vector3.forward, i == 0 ? 4.2f : 3.3f);
        }
        Handles.color = old;
        Handles.EndGUI();

        string text = live
            ? string.Format("运行中  输入 {0:0.0}° / 输出 {1:0.0}° / 偏移 {2:0.0}", status.inputAngle, status.outputAngle, status.offsetAmount)
            : "等待预览状态：打开预览区“物理”并播放/转动父节点后刷新。";
        GUI.Label(new Rect(rect.x + 8f, rect.yMax - 22f, rect.width - 16f, 18f), text, EditorStyles.miniLabel);
    }

    private SkyPrisonPhysicsOscillatorStatus FindPhysicsStatusForRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null || state == null || state.PhysicsOscillatorStatuses == null)
            return null;

        for (int i = state.PhysicsOscillatorStatuses.Count - 1; i >= 0; i--)
        {
            SkyPrisonPhysicsOscillatorStatus s = state.PhysicsOscillatorStatuses[i];
            if (s == null) continue;
            if (s.rowKey == row.key || s.sourceKey == row.key || (!string.IsNullOrEmpty(row.boundRigKey) && s.sourceKey == row.boundRigKey))
                return s;
        }
        return null;
    }

    private List<Vector2> BuildStaticPhysicsStatusPoints(Rect rect, SkyPrisonPhysicsPreset preset)
    {
        List<Vector2> points = new List<Vector2>();
        int count = preset != null ? Mathf.Clamp(preset.oscillatorCount, 1, 12) : 3;
        float totalHeight = Mathf.Max(30f, rect.height - 42f);
        Vector2 p = new Vector2(rect.center.x, rect.y + 18f);
        points.Add(p);
        for (int i = 0; i < count; i++)
        {
            float section = (i + 1f) / Mathf.Max(1f, count);
            float sway = Mathf.Sin(section * Mathf.PI) * 18f;
            p = new Vector2(rect.center.x + sway * 0.22f, rect.y + 18f + totalHeight * section);
            points.Add(p);
        }
        return points;
    }

    private List<Vector2> FitPhysicsStatusPointsToRect(List<Vector2> source, Rect rect)
    {
        List<Vector2> fitted = new List<Vector2>();
        if (source == null || source.Count == 0)
            return fitted;

        float minX = source[0].x, maxX = source[0].x, minY = source[0].y, maxY = source[0].y;
        for (int i = 1; i < source.Count; i++)
        {
            Vector2 p = source[i];
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }

        float w = Mathf.Max(1f, maxX - minX);
        float h = Mathf.Max(1f, maxY - minY);
        float scale = Mathf.Min((rect.width - 32f) / w, (rect.height - 42f) / h);
        scale = Mathf.Clamp(scale, 0.4f, 6f);
        Vector2 srcCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        Vector2 dstCenter = new Vector2(rect.center.x, rect.center.y - 4f);

        for (int i = 0; i < source.Count; i++)
            fitted.Add(dstCenter + (source[i] - srcCenter) * scale);
        return fitted;
    }


    private void DrawPhysicsPresetEditor(SkyPrisonPhysicsPreset preset)
    {
        if (preset == null)
        {
            EditorGUILayout.HelpBox("当前没有选择物理预设。打开“参与物理影响”后选择或新建一个预设。", MessageType.Info);
            return;
        }

        preset.EnsureOscillatorCount();
        EditorGUILayout.BeginVertical("box");
        preset.displayName = EditorGUILayout.TextField("预设名", preset.displayName);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("Preset Key", preset.presetKey);
        EditorGUI.EndDisabledGroup();

        int nextCount = EditorGUILayout.IntSlider("振子节数", preset.oscillatorCount, 1, 12);
        if (nextCount != preset.oscillatorCount)
        {
            preset.oscillatorCount = nextCount;
            preset.EnsureOscillatorCount();
        }

        preset.globalScale = EditorGUILayout.FloatField("全局缩放", preset.globalScale);
        preset.defaultBlend = EditorGUILayout.Slider("默认混合强度", preset.defaultBlend, 0f, 1f);
        preset.gravityAngle = EditorGUILayout.Slider("重力方向", preset.gravityAngle, -180f, 180f);
        preset.gravityStrength = EditorGUILayout.Slider("重力强度", preset.gravityStrength, 0f, 3f);
        preset.velocityInfluence = EditorGUILayout.Slider("全体速度影响", preset.velocityInfluence, 0f, 3f);
        preset.windInfluence = EditorGUILayout.Slider("风影响", preset.windInfluence, 0f, 3f);

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("振子设置", EditorStyles.miniBoldLabel);
        GUILayout.Label("固定高度，可滚动", EditorStyles.miniLabel, GUILayout.Width(110f));
        EditorGUILayout.EndHorizontal();

        physicsOscillatorScroll = EditorGUILayout.BeginScrollView(physicsOscillatorScroll, GUILayout.Height(260f));
        for (int i = 0; i < preset.oscillators.Count; i++)
        {
            SkyPrisonPhysicsOscillator osc = preset.oscillators[i];
            if (osc == null)
            {
                osc = new SkyPrisonPhysicsOscillator();
                preset.oscillators[i] = osc;
            }
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("No." + (i + 1), EditorStyles.miniBoldLabel);
            osc.length = Mathf.Max(0f, EditorGUILayout.FloatField("长度", osc.length));
            osc.swayEase = EditorGUILayout.Slider("摇晃容易度", osc.swayEase, 0f, 1f);
            osc.reactionSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("反应速度", osc.reactionSpeed));
            osc.returnSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("收束速度", osc.returnSpeed));
            osc.damping = EditorGUILayout.Slider("阻尼", osc.damping, 0f, 1f);
            osc.weight = Mathf.Max(0f, EditorGUILayout.FloatField("重量", osc.weight));
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawLayerWeightBatchControls(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return;

        List<SkyPrisonAnimationRigRow> targets = state.GetLayerWeightTargetRows(row);
        int targetCount = targets == null ? 0 : targets.Count;

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("批量权重", EditorStyles.miniBoldLabel);
        GUILayout.Label("当前 " + targetCount + " 项", EditorStyles.miniLabel, GUILayout.Width(72f));
        EditorGUILayout.EndHorizontal();

        state.LayerWeightBatchStep = Mathf.Max(1f, EditorGUILayout.FloatField("步长 / 1层", state.LayerWeightBatchStep));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("当前帧 -1层", GUILayout.Height(20f)))
            ApplyLayerWeightDelta(targets, -state.LayerWeightBatchStep);
        if (GUILayout.Button("当前帧 -5层", GUILayout.Height(20f)))
            ApplyLayerWeightDelta(targets, -state.LayerWeightBatchStep * 5f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("当前帧 +1层", GUILayout.Height(20f)))
            ApplyLayerWeightDelta(targets, state.LayerWeightBatchStep);
        if (GUILayout.Button("当前帧 +5层", GUILayout.Height(20f)))
            ApplyLayerWeightDelta(targets, state.LayerWeightBatchStep * 5f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清空选中权重帧", GUILayout.Height(20f)))
        {
            state.PushStructureUndo();
            state.ClearLayerOrderKeyframesForTargets(targets);
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void ApplyLayerWeightDelta(List<SkyPrisonAnimationRigRow> targets, float delta)
    {
        if (targets == null || targets.Count == 0)
            return;

        state.PushStructureUndo();
        state.SetLayerOrderKeyframeForTargets(targets, state.CurrentTime, delta);
        GUI.changed = true;
    }

    private void ApplyDefaultLayerWeightOffset(List<SkyPrisonAnimationRigRow> targets, float delta)
    {
        if (targets == null || targets.Count == 0)
            return;

        state.PushStructureUndo();
        for (int i = 0; i < targets.Count; i++)
        {
            SkyPrisonAnimationRigRow target = targets[i];
            if (target == null || target.isFolder) continue;
            target.manualLayerWeightOffset += delta;
        }
        GUI.changed = true;
    }
    private void DrawMaskReferenceControls(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return;

        string[] labels = state.GetMaskReferenceOptionLabels(row);
        int index = state.GetMaskReferenceIndex(row);
        int next = EditorGUILayout.Popup("蒙版参照", index, labels);
        if (next != index)
        {
            state.SetMaskReferenceByIndex(row, next);
            GUI.changed = true;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("自动参照", GUILayout.Height(20f)))
        {
            if (state.AutoBindMaskReferenceForRow(row))
                GUI.changed = true;
        }
        if (GUILayout.Button("清空", GUILayout.Width(54f), GUILayout.Height(20f)))
        {
            row.maskReferenceKey = string.Empty;
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();

        SkyPrisonAnimationRigRow mask = state.FindAnyStructureRow(row.maskReferenceKey);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("当前参照", mask == null ? "-" : ((string.IsNullOrEmpty(mask.name) ? mask.key : mask.name) + "  [" + mask.key + "]"));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox("蒙版参照用于预览与导出约束：当前层会被限制在参照层的范围内。典型用法：眼黑/瞳孔 → 眼白，眼部高光 → 眼白或眼黑。", MessageType.None);
    }

    private void DrawSemanticAndSide(SkyPrisonAnimationRigRow row)
    {
        string[] semantics = { "Root", "Pelvis", "Chest", "Neck", "Head", "HeadTop", "Shoulder", "Elbow", "Wrist", "HandEnd", "Hip", "Knee", "Ankle", "Foot", "Claw", "Tail", "Socket", "Accessory" };
        int index = Mathf.Clamp(state.GetSemanticIndex(row.semantic), 0, semantics.Length - 1);
        int next = EditorGUILayout.Popup("身体语义", index, semantics);
        if (next != index && next >= 0 && next < semantics.Length)
            row.semantic = semantics[next];

        string side = "Center";
        if (row.semantic.Contains("Left") || row.key.EndsWith("_L")) side = "Left";
        else if (row.semantic.Contains("Right") || row.key.EndsWith("_R")) side = "Right";
        EditorGUILayout.Popup("侧别", side == "Left" ? 1 : side == "Right" ? 2 : 0, new[] { "Center", "Left", "Right", "None" });
    }


    private float DrawUndoableDelayedFloatField(string label, float value, string controlName)
    {
        string safeControlName = string.IsNullOrEmpty(controlName) ? label : controlName;

        if (GUI.GetNameOfFocusedControl() == safeControlName && activeDelayedFloatUndoControl != safeControlName)
        {
            activeDelayedFloatUndoControl = safeControlName;
            activeDelayedFloatUndoSnapshot = state.CaptureStructureUndoSnapshot();
        }

        GUI.SetNextControlName(safeControlName);
        EditorGUI.BeginChangeCheck();
        float next = EditorGUILayout.FloatField(label, value);
        if (EditorGUI.EndChangeCheck())
        {
            if (activeDelayedFloatUndoControl == safeControlName && activeDelayedFloatUndoSnapshot != null)
                state.PushCapturedStructureUndo(activeDelayedFloatUndoSnapshot);
            else
                state.PushStructureUndo();

            activeDelayedFloatUndoControl = string.Empty;
            activeDelayedFloatUndoSnapshot = null;
            GUI.changed = true;
        }

        if (GUI.GetNameOfFocusedControl() != safeControlName && activeDelayedFloatUndoControl == safeControlName)
        {
            activeDelayedFloatUndoControl = string.Empty;
            activeDelayedFloatUndoSnapshot = null;
        }

        return next;
    }

    private string DrawStringPopup(string label, string current, string[] options)
    {
        if (options == null || options.Length == 0)
            return current ?? string.Empty;

        int index = 0;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] == current)
            {
                index = i;
                break;
            }
        }

        index = EditorGUILayout.Popup(label, index, options);
        return options[Mathf.Clamp(index, 0, options.Length - 1)];
    }
}
