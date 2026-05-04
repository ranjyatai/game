using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class SkyPrisonAnimationWorkbenchPage : SkyPrisonEditorPageBase
{
    private readonly SkyPrisonAnimationWorkbenchState state = new SkyPrisonAnimationWorkbenchState();
    private readonly SkyPrisonEditorContext ownerContext;

    private const float PreferredLeftWorkbenchWidth = 520f;
    private const float VirtualLeftContentWidth = 500f;

    private readonly SkyPrisonAnimationActionListPanel actionListPanel;
    private readonly SkyPrisonAnimationStructurePanel structurePanel;
    private readonly SkyPrisonAnimationPreviewPanel previewPanel;
    private readonly SkyPrisonAnimationInspectorPanel inspectorPanel;
    private readonly SkyPrisonAnimationTimelinePanel timelinePanel;
    private readonly SkyPrisonAnimationFormulaPanel formulaPanel;

    private const string FixedPackageFolder = "Assets/_Project/Data/AnimationWorkbench/ModelPackages";
    private const string FixedActionPackFolder = "Assets/_Project/Data/AnimationWorkbench/ActionPacks";
    private const string HumanTemplateActionPackFolder = "Assets/_Project/Data/AnimationWorkbench/ActionPacks/HumanTemplate";
    private const string ImportedPsdFolder = "Assets/_Project/Data/AnimationWorkbench/PSDImports";
    private const string PackageExtension = "sky2dmodel.json";
    private const string ActionPackExtension = "skyaction.json";

    private string currentPackageAssetPath = string.Empty;
    private string currentPackageName = "未命名2D模型包";
    private bool hasUnsavedChanges = true;
    private bool leftWidthInitialized;

    // 防止 IMGUI OnGUI -> Repaint -> OnGUI 重入卡死。
    private bool repaintQueued;
    private double nextPreviewRepaintTime;
    private double nextPlaybackTickTime;

    private const double PreviewIdleRepaintIntervalSeconds = 0.50;
    private const double PreviewPlayingMaxFps = 30.0;
    private const double PreviewPausedPhysicsMaxFps = 30.0;

    private const string SessionCacheRelativePath = "Library/SkyPrisonAnimationWorkbenchCache/LastSession.sky2dmodel.cache.json";
    private const string SessionCachePackagePathPrefsKey = "SkyPrison.AnimationWorkbench.SessionCache.PackageAssetPath";
    private const string SessionCachePackageNamePrefsKey = "SkyPrison.AnimationWorkbench.SessionCache.PackageName";
    private const string SessionCacheUnsavedPrefsKey = "SkyPrison.AnimationWorkbench.SessionCache.HasUnsavedChanges";
    private const double SessionAutosaveIntervalSeconds = 8.0;

    private double nextSessionAutosaveTime;
    private bool triedRestoreSessionCache;

    private bool swallowNextUndoExecuteCommand;
    private bool swallowNextRedoExecuteCommand;

    private static bool templateChoiceModalOpen;

    private delegate void SplitterDragHandler(float delta);

    public SkyPrisonAnimationWorkbenchPage(SkyPrisonEditorContext context) : base(context)
    {
        ownerContext = context;
        InitializeEmptyCustomWorkbench();
        actionListPanel = new SkyPrisonAnimationActionListPanel(state);
        structurePanel = new SkyPrisonAnimationStructurePanel(state);
        previewPanel = new SkyPrisonAnimationPreviewPanel(state);
        inspectorPanel = new SkyPrisonAnimationInspectorPanel(state);
        timelinePanel = new SkyPrisonAnimationTimelinePanel(state);
        formulaPanel = new SkyPrisonAnimationFormulaPanel(state);
    }

    public override string TabName { get { return "动作工作台"; } }

    private void InitializeEmptyCustomWorkbench()
    {
        state.CurrentRigTemplateKey = "Custom";
        state.ManualRigTemplateMode = true;

        state.Actions.Clear();
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Idle", name = "待机", type = "自定义", status = "手动", loop = true, duration = 1.2f });
        state.SelectedAction = 0;

        state.RigRows.Clear();
        state.PsbRows.Clear();
        state.SocketRows.Clear();
        state.AssemblySlots.Clear();
        state.TimelineKeyframes.Clear();
        state.LayerOrderKeyframes.Clear();
        state.ClearMotionPoseEditorState(true);
        state.InvalidateManualAngleRigSignature();

        EnterRigWorkspaceKeepingEmptyEditable(true);
    }



    private void EnterRigWorkspaceKeepingEmptyEditable(bool editMode)
    {
        state.StructureTab = SkyPrisonAnimationStructureTab.Rig;
        state.ShowRigLines = true;
        state.ShowRigEdit = editMode;
        if (editMode)
            state.PreviewPlaying = false;

        state.SelectedRig = state.RigRows.Count > 0
            ? Mathf.Clamp(state.SelectedRig, 0, state.RigRows.Count - 1)
            : -1;

        state.SelectedRigRows.Clear();
        state.SelectedRigIndices.Clear();
        state.LastSelectedRigKey = state.SelectedRig >= 0 && state.SelectedRig < state.RigRows.Count
            ? state.RigRows[state.SelectedRig].key
            : string.Empty;
    }

    private void ClearStaleTemplateModalFlag()
    {
        if (templateChoiceModalOpen && !SkyPrisonAnimationTemplateChoiceWindow.HasActiveWindow)
            templateChoiceModalOpen = false;
    }


    private string GetSessionCacheAbsolutePath()
    {
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, SessionCacheRelativePath);
    }

    private bool IsSafeToAutoRestoreSessionCache()
    {
        if (!string.IsNullOrEmpty(currentPackageAssetPath))
            return false;

        if (!string.IsNullOrEmpty(state.SourcePsdAssetPath))
            return false;

        if (state.PsbRows.Count > 0 || state.RigRows.Count > 0 || state.SocketRows.Count > 0)
            return false;

        if (!string.IsNullOrEmpty(currentPackageName) && currentPackageName != "未命名2D模型包")
            return false;

        return true;
    }

    private bool HasMeaningfulSessionContent()
    {
        if (state == null)
            return false;

        return !string.IsNullOrEmpty(currentPackageAssetPath) ||
               !string.IsNullOrEmpty(state.SourcePsdAssetPath) ||
               state.PsbRows.Count > 0 ||
               state.RigRows.Count > 0 ||
               state.SocketRows.Count > 0 ||
               state.Actions.Count > 0;
    }

    private void RememberSessionPackageMeta()
    {
        EditorPrefs.SetString(SessionCachePackagePathPrefsKey, currentPackageAssetPath ?? string.Empty);
        EditorPrefs.SetString(SessionCachePackageNamePrefsKey, currentPackageName ?? string.Empty);
        EditorPrefs.SetBool(SessionCacheUnsavedPrefsKey, hasUnsavedChanges);
    }

    private void TryRestoreSessionCacheOnce()
    {
        if (triedRestoreSessionCache)
            return;

        triedRestoreSessionCache = true;

        if (!IsSafeToAutoRestoreSessionCache())
            return;

        string path = GetSessionCacheAbsolutePath();
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            SkyPrisonAnimationModelPackage package = JsonUtility.FromJson<SkyPrisonAnimationModelPackage>(json);
            if (package == null)
                return;

            ApplyPackage(package);

            currentPackageAssetPath = EditorPrefs.GetString(SessionCachePackagePathPrefsKey, string.Empty);
            string rememberedName = EditorPrefs.GetString(SessionCachePackageNamePrefsKey, string.Empty);
            currentPackageName = !string.IsNullOrEmpty(rememberedName)
                ? rememberedName
                : (string.IsNullOrEmpty(package.displayName) ? "未命名2D模型包" : package.displayName);

            hasUnsavedChanges = EditorPrefs.GetBool(SessionCacheUnsavedPrefsKey, true);
            RepaintOwnerWindow();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("动作工作台：恢复临时缓存失败 → " + ex.Message);
        }
    }

    private void SaveSessionCache(bool force)
    {
        if (!force)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextSessionAutosaveTime)
                return;

            nextSessionAutosaveTime = now + SessionAutosaveIntervalSeconds;
        }

        if (!HasMeaningfulSessionContent())
            return;

        try
        {
            string path = GetSessionCacheAbsolutePath();
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            SkyPrisonAnimationModelPackage package = BuildPackage();
            package.displayName = currentPackageName;

            string json = JsonUtility.ToJson(package, true);
            File.WriteAllText(path, json);
            RememberSessionPackageMeta();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("动作工作台：写入临时缓存失败 → " + ex.Message);
        }
    }

    private void MaybeSaveSessionCacheAfterGui()
    {
        Event e = Event.current;
        bool force =
            GUI.changed ||
            (e != null && (e.type == EventType.MouseUp || e.type == EventType.KeyUp || e.type == EventType.DragPerform));

        // 播放时不要周期性写 JSON 缓存。播放中只在明确操作结束时写。
        if (state.PreviewPlaying && !force)
            return;

        SaveSessionCache(force);
    }

    private void ClearSessionCache()
    {
        string path = GetSessionCacheAbsolutePath();
        if (File.Exists(path))
            File.Delete(path);

        EditorPrefs.DeleteKey(SessionCachePackagePathPrefsKey);
        EditorPrefs.DeleteKey(SessionCachePackageNamePrefsKey);
        EditorPrefs.DeleteKey(SessionCacheUnsavedPrefsKey);
    }


    // 给主窗口读取：左侧工作区折叠时，主窗口也要把左栏宽度压缩，真正给右侧腾空间。
    public bool IsLeftWorkbenchCollapsed
    {
        get { return state.LeftWorkbenchCollapsed; }
    }

    public void SetLeftWorkbenchCollapsed(bool collapsed)
    {
        state.LeftWorkbenchCollapsed = collapsed;
    }

    public override void OnEnable()
    {
        state.LastTime = EditorApplication.timeSinceStartup;
        TryRestoreSessionCacheOnce();
    }

    private bool IsWorkbenchTextInputActive()
    {
        if (EditorGUIUtility.editingTextField)
            return true;

        string focused = GUI.GetNameOfFocusedControl();
        if (string.IsNullOrEmpty(focused))
            return false;

        return focused.StartsWith("SkyPrisonTimeline_", StringComparison.OrdinalIgnoreCase)
            || focused.StartsWith("SkyPrisonAnimation_", StringComparison.OrdinalIgnoreCase)
            || focused.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0
            || focused.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0;
    }


    public override void HandleGlobalShortcuts()
    {
        Event e = Event.current;
        if (e == null)
            return;

        ClearStaleTemplateModalFlag();

        // 模板选择窗只负责选择模板，不再全局吞掉工作台事件。
        // 旧逻辑会在 templateChoiceModalOpen 残留时吃掉所有 MouseDown，
        // 导致 Rig部件 / 编辑模式等基础按钮像被锁住。
        if (e.type == EventType.Layout)
        {
            swallowNextUndoExecuteCommand = false;
            swallowNextRedoExecuteCommand = false;
        }

        bool ctrlOrCmd = e.control || e.command;

        // Unity 的 Ctrl+Z 有时只稳定经过 ValidateCommand；
        // 这里直接在 ValidateCommand 阶段执行工作台撤销，并吞掉随后可能到来的 ExecuteCommand。
        if (e.type == EventType.ValidateCommand)
        {
            if (e.commandName == "Undo")
            {
                DispatchWorkbenchUndo();
                swallowNextUndoExecuteCommand = true;
                e.Use();
                return;
            }

            if (e.commandName == "Redo")
            {
                DispatchWorkbenchRedo();
                swallowNextRedoExecuteCommand = true;
                e.Use();
                return;
            }
        }

        if (e.type == EventType.ExecuteCommand)
        {
            if (e.commandName == "Undo" || e.commandName == "UndoRedoPerformed")
            {
                if (!swallowNextUndoExecuteCommand)
                    DispatchWorkbenchUndo();

                swallowNextUndoExecuteCommand = false;
                e.Use();
                return;
            }

            if (e.commandName == "Redo")
            {
                if (!swallowNextRedoExecuteCommand)
                    DispatchWorkbenchRedo();

                swallowNextRedoExecuteCommand = false;
                e.Use();
                return;
            }
        }

        if (e.type != EventType.KeyDown)
            return;

        // 文本输入框拥有键盘焦点时，工作台级快捷键绝对不能抢焦点。
        // 时间线底部的轨道秒/帧率/速度就是这里被 Space/Z/Delete 等快捷键干扰，
        // 表现成“点到了，但很难进入稳定输入状态”。
        if (IsWorkbenchTextInputActive())
            return;

        // 工作台内不用 Unity 本体的 Ctrl+Z：普通 Z 直接走顶部“编辑/撤销”的同一路由。
        // 这里是工作台级快捷键：只要当前窗口获得键盘事件，不要求鼠标停在预览区。
        // 数值框曾经会残留 editingTextField 导致 Z 被挡住，所以按 Z 时先主动释放文本焦点。
        if (!ctrlOrCmd && e.keyCode == KeyCode.Z)
        {
            if (EditorGUIUtility.editingTextField)
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                EditorGUIUtility.editingTextField = false;
            }

            if (e.shift)
            {
                DispatchWorkbenchRedo();
                swallowNextRedoExecuteCommand = true;
            }
            else
            {
                DispatchWorkbenchUndo();
                swallowNextUndoExecuteCommand = true;
            }

            e.Use();
            return;
        }

        // 旧 Ctrl+Z 仍然保留兜底，但主推荐现在是 Z。
        if (ctrlOrCmd && e.keyCode == KeyCode.Z)
        {
            if (EditorGUIUtility.editingTextField)
            {
                GUI.FocusControl(null);
                EditorGUIUtility.editingTextField = false;
            }

            if (e.shift)
            {
                DispatchWorkbenchRedo();
                swallowNextRedoExecuteCommand = true;
            }
            else
            {
                DispatchWorkbenchUndo();
                swallowNextUndoExecuteCommand = true;
            }

            e.Use();
            return;
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.Y)
        {
            if (EditorGUIUtility.editingTextField)
            {
                GUI.FocusControl(null);
                EditorGUIUtility.editingTextField = false;
            }

            DispatchWorkbenchRedo();
            swallowNextRedoExecuteCommand = true;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Space)
        {
            state.PreviewPlaying = !state.PreviewPlaying;
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.S)
        {
            SaveModelPackage();
            e.Use();
        }
        else if (!EditorGUIUtility.editingTextField && ctrlOrCmd && e.keyCode == KeyCode.C)
        {
            if (state.CopySelectedTimelineKeyframe())
                e.Use();
        }
        else if (!EditorGUIUtility.editingTextField && ctrlOrCmd && e.keyCode == KeyCode.X)
        {
            object snapshot = state.CaptureStructureUndoSnapshot();
            if (state.CutSelectedOrActiveTimelineKeyframe())
            {
                state.PushCapturedStructureUndo(snapshot);
                e.Use();
            }
        }
        else if (!EditorGUIUtility.editingTextField && ctrlOrCmd && e.keyCode == KeyCode.V)
        {
            object snapshot = state.CaptureStructureUndoSnapshot();
            if (state.PasteTimelineKeyframeAtCurrentFrame())
            {
                state.PushCapturedStructureUndo(snapshot);
                e.Use();
            }
        }
        else if (e.keyCode == KeyCode.A && !ctrlOrCmd)
        {
            Debug.Log("动作工作台：在当前时间生成关键帧。");
            e.Use();
        }
        else if (!EditorGUIUtility.editingTextField && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            object snapshot = state.CaptureStructureUndoSnapshot();
            if (state.DeleteSelectedOrActiveTimelineKeyframe())
            {
                state.PushCapturedStructureUndo(snapshot);
                e.Use();
            }
        }
        else if (e.keyCode == KeyCode.Home)
        {
            state.CurrentTime = 0f;
            e.Use();
        }
    }

    public override void OnGUILeft()
    {
        HandleGlobalShortcuts();
        ConsumeDeferredWorkbenchUndoRedoRequests();
        EnsureLeftWorkbenchWidth();
        DrawLeftPanel();
        ConsumeDeferredWorkbenchUndoRedoRequests();
        MaybeSaveSessionCacheAfterGui();
    }

    // 主窗口可直接把左侧 Rect 交给动作工作台绘制，避免外层 GUILayout 把内容压缩。
    public void DrawLeftPanelInRect(Rect rect)
    {
        HandleGlobalShortcuts();
        ConsumeDeferredWorkbenchUndoRedoRequests();
        EnsureLeftWorkbenchWidth();

        if (state.LeftWorkbenchCollapsed)
            DrawCollapsedLeftWorkbench(rect);
        else
            DrawLeftPanelRect(rect);

        HandlePsdDragAndDrop(rect);
        ConsumeDeferredWorkbenchUndoRedoRequests();
        MaybeSaveSessionCacheAfterGui();
    }

    public override void OnGUIRight()
    {
        HandleGlobalShortcuts();
        ConsumeDeferredWorkbenchUndoRedoRequests();
        UpdatePreviewTime();

        Rect full = GUILayoutUtility.GetRect(
            900f,
            760f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );

        DrawRightWorkspace(full);
        ConsumeDeferredWorkbenchUndoRedoRequests();
        HandlePsdDragAndDrop(full);
        MaybeSaveSessionCacheAfterGui();
    }

    private void EnsureLeftWorkbenchWidth()
    {
        if (leftWidthInitialized || ownerContext == null || state.LeftWorkbenchCollapsed)
            return;

        if (ownerContext.LeftPanelWidth + 0.5f < PreferredLeftWorkbenchWidth)
        {
            ownerContext.LeftPanelWidth = PreferredLeftWorkbenchWidth;
            RepaintOwnerWindow();
        }

        leftWidthInitialized = true;
    }

    private void DrawLeftPanel()
    {
        // 左侧不能继续用 GUILayout 自动流式布局。
        // 这里改成整块 Rect 布局：Header / 动作列表 / 分割线 / 结构图层。
        Rect full = GUILayoutUtility.GetRect(
            10f,
            10f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );

        if (state.LeftWorkbenchCollapsed)
            DrawCollapsedLeftWorkbench(full);
        else
            DrawLeftPanelRect(full);

        HandlePsdDragAndDrop(full);
    }

    private void DrawLeftPanelRect(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        float pad = 4f;
        float headerHeight = 86f;
        float sp = SkyPrisonAnimationWorkbenchState.SplitterSize;
        float minStructureHeight = 150f;

        Rect inner = new Rect(rect.x + pad, rect.y + pad, Mathf.Max(1f, rect.width - pad * 2f), Mathf.Max(1f, rect.height - pad * 2f));
        Rect header = new Rect(inner.x, inner.y, inner.width, headerHeight);

        float remaining = Mathf.Max(1f, inner.height - headerHeight - sp - 4f);
        float maxActionHeight = Mathf.Max(SkyPrisonAnimationWorkbenchState.MinLeftActionHeight, remaining - minStructureHeight);
        state.LeftActionListHeight = Mathf.Clamp(
            state.LeftActionListHeight,
            SkyPrisonAnimationWorkbenchState.MinLeftActionHeight,
            maxActionHeight
        );

        Rect actionRect = new Rect(inner.x, header.yMax + 4f, inner.width, state.LeftActionListHeight);
        Rect splitter = new Rect(inner.x, actionRect.yMax, inner.width, sp);
        Rect structureRect = new Rect(inner.x, splitter.yMax, inner.width, Mathf.Max(1f, inner.yMax - splitter.yMax));

        DrawLeftHeader(header);

        DrawActionListPanelDirect(actionRect);

        DrawHorizontalSplitter(splitter, ref state.DraggingLeftActionStructureSplitter, delegate(float deltaY)
        {
            float maxH = Mathf.Max(SkyPrisonAnimationWorkbenchState.MinLeftActionHeight, remaining - minStructureHeight);
            state.LeftActionListHeight = Mathf.Clamp(
                state.LeftActionListHeight + deltaY,
                SkyPrisonAnimationWorkbenchState.MinLeftActionHeight,
                maxH
            );
        });

        DrawStructurePanel(structureRect);
    }

    private void DrawActionListPanelDirect(Rect rect)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Rect inner = new Rect(
            rect.x + 1f,
            rect.y + 1f,
            Mathf.Max(1f, rect.width - 2f),
            Mathf.Max(1f, rect.height - 2f));

        GUILayout.BeginArea(inner);
        actionListPanel.Draw();
        GUILayout.EndArea();
    }

    private void DrawStructurePanel(Rect rect)
    {
        structurePanel.DrawInRect(rect);
    }

    private void DrawLeftHeader(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f));

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("动作工作台", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("<<", EditorStyles.miniButton, GUILayout.Width(34f)))
        {
            state.LeftWorkbenchCollapsed = true;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("PSB图层 → PartRig → 公式/关键帧 → 事件轨", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("同步PSB"))
            Debug.Log("动作工作台：同步PSB图层。");
        if (GUILayout.Button("保存"))
            Debug.Log("动作工作台：保存动作数据。");
        EditorGUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawClippedScrollPanel(Rect rect, ref Vector2 scroll, System.Action drawContent)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        GUILayout.BeginArea(rect);
        scroll = EditorGUILayout.BeginScrollView(scroll, true, true);
        EditorGUILayout.BeginVertical(GUILayout.MinWidth(VirtualLeftContentWidth));
        drawContent();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawCollapsedLeftWorkbench(Rect rect)
    {
        GUI.BeginGroup(rect);

        Rect local = new Rect(0f, 0f, rect.width, rect.height);
        EditorGUI.DrawRect(local, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(local, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Rect button = new Rect(4f, 8f, Mathf.Max(20f, rect.width - 8f), 24f);
        if (GUI.Button(button, ">", EditorStyles.miniButton))
        {
            state.LeftWorkbenchCollapsed = false;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
        }

        GUI.EndGroup();
    }

    private void DrawRightWorkspace(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.Bg);

        Rect header = new Rect(rect.x, rect.y, rect.width, SkyPrisonAnimationWorkbenchState.HeaderHeight);
        DrawHeader(header);

        Rect content = new Rect(
            rect.x + 8f,
            header.yMax + 8f,
            rect.width - 16f,
            rect.height - SkyPrisonAnimationWorkbenchState.HeaderHeight - 16f
        );

        UpdateRightLayoutForContentSize(content);

        float sp = SkyPrisonAnimationWorkbenchState.SplitterSize;
        float availableHeight = Mathf.Max(0f, content.height - sp);
        float upperMin = state.UpperPanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinPreviewHeight;
        float timelineMin = state.TimelinePanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinTimelineHeight;

        float timelineHeight = state.TimelinePanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : Mathf.Clamp(state.RightTimelineHeight, timelineMin, Mathf.Max(timelineMin, availableHeight - upperMin));
        float upperHeight = Mathf.Max(0f, availableHeight - timelineHeight);

        Rect upper = new Rect(content.x, content.y, content.width, upperHeight);
        Rect upperTimelineSplitter = new Rect(content.x, upper.yMax, content.width, sp);
        Rect timeline = new Rect(content.x, upperTimelineSplitter.yMax, content.width, timelineHeight);

        DrawUpperGroup(upper);

        if (!state.UpperPanelCollapsed && !state.TimelinePanelCollapsed)
        {
            DrawHorizontalSplitter(upperTimelineSplitter, ref state.DraggingPreviewTimelineSplitter, delegate(float deltaY)
            {
                float ah = Mathf.Max(0f, content.height - SkyPrisonAnimationWorkbenchState.SplitterSize);
                float maxTimeline = Mathf.Max(
                    SkyPrisonAnimationWorkbenchState.MinTimelineHeight,
                    ah - SkyPrisonAnimationWorkbenchState.MinPreviewHeight
                );

                state.RightTimelineHeight = Mathf.Clamp(
                    state.RightTimelineHeight - deltaY,
                    SkyPrisonAnimationWorkbenchState.MinTimelineHeight,
                    maxTimeline
                );
            });
        }
        else
        {
            DrawSplitterRect(upperTimelineSplitter);
        }

        DrawStackPanel(
            timeline,
            "时间线",
            ref state.TimelinePanelCollapsed,
            delegate(Rect r) { timelinePanel.Draw(r); }
        );
    }

    private void DrawUpperGroup(Rect rect)
    {
        if (state.UpperPanelCollapsed)
        {
            if (DrawCollapsedPanelBar(rect, "实时预览 + 选中项属性"))
                state.UpperPanelCollapsed = false;
            return;
        }

        float sp = SkyPrisonAnimationWorkbenchState.SplitterSize;

        if (state.InspectorPanelCollapsed)
        {
            const float foldedInspectorWidth = 30f;
            Rect foldedInspector = new Rect(rect.xMax - foldedInspectorWidth, rect.y, foldedInspectorWidth, rect.height);
            Rect previewOnly = new Rect(rect.x, rect.y, Mathf.Max(0f, foldedInspector.x - rect.x - sp), rect.height);
            Rect gap = new Rect(previewOnly.xMax, rect.y, sp, rect.height);

            Event focusEventCollapsed = Event.current;
            if (focusEventCollapsed != null && focusEventCollapsed.type == EventType.MouseDown && focusEventCollapsed.button == 0 && !previewOnly.Contains(focusEventCollapsed.mousePosition))
            {
                state.PreviewPanelHasKeyboardFocus = false;
                if (!state.PreviewPanelRigDragging)
                    state.PreviewPanelMouseInside = false;
            }

            previewPanel.Draw(previewOnly);
            DrawSplitterRect(gap);
            DrawCollapsedInspectorBar(foldedInspector);
            return;
        }

        float inspectorWidth = state.InspectorWidth;
        float maxInspector = Mathf.Max(
            SkyPrisonAnimationWorkbenchState.MinInspectorWidth,
            rect.width - SkyPrisonAnimationWorkbenchState.MinPreviewWidth - sp
        );
        inspectorWidth = Mathf.Clamp(inspectorWidth, SkyPrisonAnimationWorkbenchState.MinInspectorWidth, maxInspector);
        state.InspectorWidth = inspectorWidth;

        Rect inspector = new Rect(rect.xMax - inspectorWidth, rect.y, inspectorWidth, rect.height);
        Rect splitter = new Rect(inspector.x - sp, rect.y, sp, rect.height);
        Rect preview = new Rect(rect.x, rect.y, Mathf.Max(0f, splitter.x - rect.x), rect.height);

        Event focusEvent = Event.current;
        if (focusEvent != null && focusEvent.type == EventType.MouseDown && focusEvent.button == 0 && !preview.Contains(focusEvent.mousePosition))
        {
            state.PreviewPanelHasKeyboardFocus = false;
            if (!state.PreviewPanelRigDragging)
                state.PreviewPanelMouseInside = false;
        }

        previewPanel.Draw(preview);
        DrawVerticalSplitter(splitter, ref state.DraggingPreviewInspectorSplitter, delegate(float deltaX)
        {
            float maxWidth = Mathf.Max(
                SkyPrisonAnimationWorkbenchState.MinInspectorWidth,
                rect.width - SkyPrisonAnimationWorkbenchState.MinPreviewWidth - SkyPrisonAnimationWorkbenchState.SplitterSize
            );
            state.InspectorWidth = Mathf.Clamp(
                state.InspectorWidth - deltaX,
                SkyPrisonAnimationWorkbenchState.MinInspectorWidth,
                maxWidth
            );
        });
        inspectorPanel.Draw(inspector);
    }

    private void DrawCollapsedInspectorBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelDeepBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Event e = Event.current;
        bool hover = e != null && rect.Contains(e.mousePosition);
        if (hover)
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

        Rect buttonRect = new Rect(rect.x + 3f, rect.y + 4f, rect.width - 6f, 22f);
        if (GUI.Button(buttonRect, new GUIContent("<<", "展开右侧属性 / 装配"), EditorStyles.miniButton))
        {
            state.InspectorPanelCollapsed = false;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
        }

        GUIStyle verticalStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = new Color(0.86f, 0.86f, 0.88f, 1f) }
        };

        GUI.Label(
            new Rect(rect.x + 4f, buttonRect.yMax + 8f, rect.width - 8f, Mathf.Max(20f, rect.height - buttonRect.height - 16f)),
            "属性\n装配",
            verticalStyle
        );

        if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition) && !buttonRect.Contains(e.mousePosition))
        {
            state.InspectorPanelCollapsed = false;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
            e.Use();
        }
    }

    private void DrawStackPanel(Rect rect, string title, ref bool collapsed, System.Action<Rect> drawExpanded)
    {
        if (collapsed)
        {
            if (DrawCollapsedPanelBar(rect, title))
                collapsed = false;
            return;
        }

        drawExpanded(rect);
        DrawStackFoldButton(rect, ref collapsed, "折叠" + title);
    }

    private void DrawStackFoldButton(Rect panelRect, ref bool collapsed, string tooltip)
    {
        Rect buttonRect = new Rect(panelRect.xMax - 28f, panelRect.y + 4f, 24f, 18f);
        GUIContent content = new GUIContent("︿", tooltip);
        if (GUI.Button(buttonRect, content, EditorStyles.miniButton))
        {
            collapsed = true;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
        }
    }

    private bool DrawCollapsedPanelBar(Rect rect, string title)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelDeepBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        Event e = Event.current;
        bool hover = e != null && rect.Contains(e.mousePosition);
        if (hover)
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));

        bool expandRequested = false;
        Rect btn = new Rect(rect.xMax - 30f, rect.y + 3f, 26f, Mathf.Max(16f, rect.height - 6f));
        if (GUI.Button(btn, "﹀", EditorStyles.miniButton))
            expandRequested = true;

        GUI.Label(
            new Rect(rect.x + 8f, rect.y + 4f, Mathf.Max(10f, rect.width - 46f), rect.height - 8f),
            title + "  已折叠",
            EditorStyles.miniBoldLabel
        );

        if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            expandRequested = true;
            e.Use();
        }

        if (expandRequested)
            RepaintOwnerWindow();

        return expandRequested;
    }

    private void UpdateRightLayoutForContentSize(Rect content)
    {
        float sp = SkyPrisonAnimationWorkbenchState.SplitterSize;
        float availableHeight = Mathf.Max(0f, content.height - sp);

        float upperMin = state.UpperPanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinPreviewHeight;
        float timelineMin = state.TimelinePanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinTimelineHeight;

        float maxInspector = Mathf.Max(
            SkyPrisonAnimationWorkbenchState.MinInspectorWidth,
            content.width - SkyPrisonAnimationWorkbenchState.MinPreviewWidth - sp
        );
        state.InspectorWidth = Mathf.Clamp(
            state.InspectorWidth,
            SkyPrisonAnimationWorkbenchState.MinInspectorWidth,
            maxInspector
        );

        if (availableHeight <= 1f)
            return;

        if (!state.TimelinePanelCollapsed)
        {
            float maxTimeline = Mathf.Max(timelineMin, availableHeight - upperMin);
            state.RightTimelineHeight = Mathf.Clamp(state.RightTimelineHeight, timelineMin, maxTimeline);
        }

        // 节点角度编辑已经移到左侧“结构 / 图层”的动作参数页签，右侧底部不再绘制公式/动作模板面板。
        state.FormulaPanelCollapsed = true;
    }

    private void GetRightLayoutHeights(Rect content, out float upperHeight, out float timelineHeight, out float formulaHeight)
    {
        float sp = SkyPrisonAnimationWorkbenchState.SplitterSize;
        float availableHeight = Mathf.Max(0f, content.height - sp * 2f);

        float upperMin = state.UpperPanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinPreviewHeight;
        float timelineMin = state.TimelinePanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinTimelineHeight;
        float formulaMin = state.FormulaPanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : SkyPrisonAnimationWorkbenchState.MinFormulaHeight;

        float minTotal = upperMin + timelineMin + formulaMin;
        if (availableHeight < minTotal)
        {
            float scale = Mathf.Max(0.01f, availableHeight / Mathf.Max(1f, minTotal));
            upperHeight = upperMin * scale;
            timelineHeight = timelineMin * scale;
            formulaHeight = Mathf.Max(0f, availableHeight - upperHeight - timelineHeight);
            return;
        }

        formulaHeight = state.FormulaPanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : Mathf.Clamp(
                state.RightFormulaHeight,
                formulaMin,
                availableHeight - upperMin - timelineMin
            );

        timelineHeight = state.TimelinePanelCollapsed
            ? SkyPrisonAnimationWorkbenchState.FoldBarSize
            : Mathf.Clamp(
                state.RightTimelineHeight,
                timelineMin,
                availableHeight - upperMin - formulaHeight
            );

        upperHeight = Mathf.Max(upperMin, availableHeight - timelineHeight - formulaHeight);
    }

    private void DrawHorizontalSplitter(Rect rect, ref bool dragging, SplitterDragHandler onDrag)
    {
        DrawSplitterRect(rect);
        Rect hitRect = new Rect(rect.x, rect.y - 8f, rect.width, rect.height + 16f);
        EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeVertical);
        HandleSplitterMouse(hitRect, ref dragging, true, onDrag);
    }

    private void DrawVerticalSplitter(Rect rect, ref bool dragging, SplitterDragHandler onDrag)
    {
        DrawSplitterRect(rect);
        Rect hitRect = new Rect(rect.x - 8f, rect.y, rect.width + 16f, rect.height);
        EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal);
        HandleSplitterMouse(hitRect, ref dragging, false, onDrag);
    }

    private void DrawSplitterRect(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f, 1f));

        Rect line = rect.width > rect.height
            ? new Rect(rect.x, rect.center.y - 0.5f, rect.width, 1f)
            : new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height);

        EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.18f));
    }

    private void HandleSplitterMouse(Rect rect, ref bool dragging, bool horizontal, SplitterDragHandler onDrag)
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            dragging = true;
            GUI.FocusControl(null);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && dragging)
        {
            onDrag(horizontal ? e.delta.y : e.delta.x);
            e.Use();
            RepaintOwnerWindow();
        }
        else if (e.type == EventType.MouseUp && dragging)
        {
            dragging = false;
            e.Use();
            RepaintOwnerWindow();
        }
    }

    private void RepaintOwnerWindow()
    {
        // IMGUI 里不能在 Layout/Repaint 阶段直接 Repaint，否则很容易进入
        // OnGUI -> Repaint -> OnGUI 的重入循环。这里统一排队到 delayCall。
        QueueOwnerRepaint();
    }

    private void QueueOwnerRepaint()
    {
        if (repaintQueued)
            return;

        repaintQueued = true;
        EditorApplication.delayCall += delegate
        {
            repaintQueued = false;

            EditorWindow wnd = EditorWindow.focusedWindow != null
                ? EditorWindow.focusedWindow
                : EditorWindow.mouseOverWindow;

            if (wnd != null)
                wnd.Repaint();
        };
    }


    private void RequestPreviewRepaintThrottled()
    {
        double now = EditorApplication.timeSinceStartup;

        double interval;
        if (state.PreviewPlaying)
        {
            double fps = Mathf.Clamp(state.TimelineFrameRate, 12, (int)PreviewPlayingMaxFps);
            interval = 1.0 / fps;
        }
        else if (ShouldKeepPhysicsSimulatingWhenPaused())
        {
            // 暂停只冻结时间线播放头，不冻结物理预览。
            // 物理弹簧需要持续 Repaint 才能继续衰减/回弹，否则暂停后会像截图一样“硬停”。
            double fps = Mathf.Clamp(state.TimelineFrameRate, 12, (int)PreviewPausedPhysicsMaxFps);
            interval = 1.0 / fps;
        }
        else
        {
            interval = PreviewIdleRepaintIntervalSeconds;
        }

        if (now < nextPreviewRepaintTime)
            return;

        nextPreviewRepaintTime = now + interval;
        QueueOwnerRepaint();
    }


    private string GetHeaderPackageName()
    {
        string name = string.IsNullOrWhiteSpace(currentPackageName) ? "未命名2D模型包" : currentPackageName;
        return hasUnsavedChanges && !name.EndsWith("*", StringComparison.Ordinal)
            ? name + "*"
            : name;
    }

    private void DrawHeader(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelDeepBg);

        GUILayout.BeginArea(rect);
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("文件(F)", EditorStyles.toolbarDropDown, GUILayout.Width(70f)))
            ShowFileMenu();

        if (GUILayout.Button("录像", EditorStyles.toolbarDropDown, GUILayout.Width(55f)))
            ShowRecordMenu();

        if (GUILayout.Button("编辑", EditorStyles.toolbarDropDown, GUILayout.Width(55f)))
            ShowEditMenu();

        if (GUILayout.Button("撤销(Z)", EditorStyles.toolbarButton, GUILayout.Width(66f)))
            DispatchWorkbenchUndo();

        if (GUILayout.Button("重做(Y)", EditorStyles.toolbarButton, GUILayout.Width(66f)))
            DispatchWorkbenchRedo();

        if (GUILayout.Button("窗口", EditorStyles.toolbarDropDown, GUILayout.Width(55f)))
            ShowWindowMenu();

        GUILayout.FlexibleSpace();

        SkyPrisonAnimationActionRow action = state.CurrentAction();
        string packageNameForHeader = GetHeaderPackageName();
        GUILayout.Label(
            packageNameForHeader + " / " + action.name + " / " + state.FormatCurrentTime() + " / " + state.TimelineDurationSeconds.ToString("0.00") + "s",
            EditorStyles.miniLabel
        );

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }


    private void ShowFileMenu()
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("新建/人形模板"), false, delegate { TryCreateTemplate("Human", "人形模板"); });
        menu.AddItem(new GUIContent("新建/史莱姆模板"), false, delegate { TryCreateTemplate("Slime", "史莱姆模板"); });
        menu.AddItem(new GUIContent("新建/丧尸模板"), false, delegate { TryCreateTemplate("Zombie", "丧尸模板"); });
        menu.AddItem(new GUIContent("新建/四足动物模板"), false, delegate { TryCreateTemplate("Quadruped", "四足动物模板"); });
        menu.AddItem(new GUIContent("新建/鸟类模板"), false, delegate { TryCreateTemplate("Bird", "鸟类模板"); });
        menu.AddItem(new GUIContent("新建/小型怪物模板"), false, delegate { TryCreateTemplate("SmallMonster", "小型怪物模板"); });
        menu.AddSeparator("新建/");
        menu.AddItem(new GUIContent("新建/自定义"), false, delegate { TryCreateTemplate("Custom", "自定义"); });

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("导入PSD/PSB"), false, ImportPsdOrPsbByPanel);
        menu.AddItem(new GUIContent("打开"), false, OpenModelPackage);
        menu.AddItem(new GUIContent("保存为2D模型包"), false, SaveModelPackage);
        menu.AddItem(new GUIContent("别名保存"), false, SaveModelPackageAs);

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("行动包/导出当前动作包"), false, ExportCurrentActionPack);
        menu.AddItem(new GUIContent("行动包/加载动作包"), false, ImportActionPack);
        menu.AddItem(new GUIContent("行动包/重新套用人类模板动作"), false, ApplyHumanTemplateActionPacksFromMenu);

        menu.ShowAsContext();
    }

    private void ShowRecordMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("导出MP4"), false, delegate { ExportRecording("mp4"); });
        menu.AddItem(new GUIContent("导出动态图片/GIF"), false, delegate { ExportRecording("gif"); });
        menu.AddItem(new GUIContent("导出动态图片/APNG"), false, delegate { ExportRecording("apng"); });
        menu.AddItem(new GUIContent("导出序列帧/PNG"), false, delegate { ExportRecording("png_sequence"); });
        menu.ShowAsContext();
    }

    private void ConsumeDeferredWorkbenchUndoRedoRequests()
    {
        if (state.WorkbenchUndoShortcutRequested)
        {
            state.WorkbenchUndoShortcutRequested = false;
            DispatchWorkbenchUndo();
        }

        if (state.WorkbenchRedoShortcutRequested)
        {
            state.WorkbenchRedoShortcutRequested = false;
            DispatchWorkbenchRedo();
        }
    }

    private bool DispatchWorkbenchUndo()
    {
        // Z 是整个动作工作台级快捷键，不能依赖鼠标是否进入预览区。
        // 大多数数据修改（动作参数、时间线、检查器、PSB压层、Rig/PSB父子结构）都进 StructureUndo。
        // 只有“骨架编辑模式下直接拖预览骨骼”的旧链路仍会进 RigUndo，所以这里按当前模式分流，
        // 而不是按 PreviewPanelHasKeyboardFocus 分流。这样鼠标在左栏、右侧检查器、时间线时，Z 一样有效。
        bool rigEditContext = !state.StructureAngleEditMode && state.ShowRigEdit;

        if (rigEditContext && state.UndoRig())
        {
            RepaintOwnerWindow();
            return true;
        }

        if (state.UndoStructure())
        {
            RepaintOwnerWindow();
            return true;
        }

        if (!rigEditContext && state.UndoRig())
        {
            RepaintOwnerWindow();
            return true;
        }

        return false;
    }

    private bool DispatchWorkbenchRedo()
    {
        bool rigEditContext = !state.StructureAngleEditMode && state.ShowRigEdit;

        if (rigEditContext && state.RedoRig())
        {
            RepaintOwnerWindow();
            return true;
        }

        if (state.RedoStructure())
        {
            RepaintOwnerWindow();
            return true;
        }

        if (!rigEditContext && state.RedoRig())
        {
            RepaintOwnerWindow();
            return true;
        }

        return false;
    }

    private void ShowEditMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("撤销(Z)"), false, delegate { DispatchWorkbenchUndo(); });
        menu.AddItem(new GUIContent("重做(Y)"), false, delegate { DispatchWorkbenchRedo(); });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("剪切"), false, delegate { Debug.Log("动作工作台：剪切选中内容。"); });
        menu.AddItem(new GUIContent("复制"), false, delegate { Debug.Log("动作工作台：复制选中内容。"); });
        menu.AddItem(new GUIContent("粘贴"), false, delegate { Debug.Log("动作工作台：粘贴内容。"); });
        menu.AddItem(new GUIContent("全选"), false, delegate { SelectAllCurrentStructureRows(); });
        menu.ShowAsContext();
    }

    private void ShowWindowMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("重置布局"), false, delegate { state.ResetWorkbenchLayout(); RepaintOwnerWindow(); });
        menu.ShowAsContext();
    }

    private void TryCreateTemplate(string templateKey, string displayName)
    {
        if (!ConfirmBeforeDestructiveOperation("新建" + displayName, "新建模板会刷新当前动作工作台内容。是否先保存当前2D模型包？"))
            return;

        ApplyTemplate(templateKey);
        currentPackageName = displayName;
        currentPackageAssetPath = string.Empty;
        hasUnsavedChanges = true;
        GUI.FocusControl(null);
        RepaintOwnerWindow();
    }

    private bool ConfirmBeforeDestructiveOperation(string title, string message)
    {
        int result = EditorUtility.DisplayDialogComplex(
            title,
            message,
            "保存后继续",
            "不保存继续",
            "取消");

        if (result == 2)
            return false;

        if (result == 0)
            SaveModelPackage();

        return true;
    }

    private static string NormalizeTemplateKey(string templateKey)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            return "Human";

        string key = templateKey.Trim();
        if (string.Equals(key, "Custom", StringComparison.OrdinalIgnoreCase) || key == "自定义") return "Custom";
        if (string.Equals(key, "Slime", StringComparison.OrdinalIgnoreCase)) return "Slime";
        if (string.Equals(key, "Zombie", StringComparison.OrdinalIgnoreCase)) return "Zombie";
        if (string.Equals(key, "Quadruped", StringComparison.OrdinalIgnoreCase)) return "Quadruped";
        if (string.Equals(key, "Bird", StringComparison.OrdinalIgnoreCase)) return "Bird";
        if (string.Equals(key, "SmallMonster", StringComparison.OrdinalIgnoreCase)) return "SmallMonster";
        return "Human";
    }

    private static bool IsCustomTemplateKey(string templateKey)
    {
        return string.Equals(NormalizeTemplateKey(templateKey), "Custom", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyTemplate(string templateKey)
    {
        templateKey = NormalizeTemplateKey(templateKey);
        state.CurrentRigTemplateKey = templateKey;
        state.ManualRigTemplateMode = IsCustomTemplateKey(templateKey);

        state.Actions.Clear();
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Idle", name = "待机", type = "关键帧", status = "空轨", loop = true, duration = 1.2f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Move", name = "移动", type = "关键帧", status = "空轨", loop = true, duration = 1.2f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Sneak", name = "潜行", type = "关键帧", status = "空轨", loop = true, duration = 1.6f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Run", name = "奔跑", type = "关键帧", status = "空轨", loop = true, duration = 0.8f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Jump", name = "跳跃", type = "关键帧", status = "空轨", loop = true, duration = 0.9f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Attack_01", name = "普通攻击", type = "关键帧", status = "空轨", loop = true, duration = 1.0f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Hit", name = "受击", type = "关键帧", status = "空轨", loop = true, duration = 0.45f });
        state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Death", name = "死亡", type = "关键帧", status = "空轨", loop = true, duration = 1.0f });
        state.SelectedAction = 0;

        state.RigRows.Clear();
        state.PsbRows.Clear();
        state.SocketRows.Clear();
        state.TimelineKeyframes.Clear();
        state.LayerOrderKeyframes.Clear();
        state.ClearMotionPoseEditorState(true);
        state.InvalidateManualAngleRigSignature();

        if (templateKey == "Custom")
            BuildCustomTemplate();
        else if (templateKey == "Slime")
            BuildSlimeTemplate();
        else if (templateKey == "Zombie")
            BuildHumanTemplate(true);
        else if (templateKey == "Quadruped")
            BuildQuadrupedTemplate();
        else if (templateKey == "Bird")
            BuildBirdTemplate();
        else if (templateKey == "SmallMonster")
            BuildSmallMonsterTemplate();
        else
            BuildHumanTemplate(false);

        // 预设只决定初始骨骼参数，不决定 Rig 页是否能打开。
        // RigRows 为空时仍然停留在 Rig 工作区，只是 SelectedRig = -1。
        EnterRigWorkspaceKeepingEmptyEditable(templateKey == "Custom");

        // 模板只负责生成骨架结构，不再自动生成呼吸 / 行走 / 奔跑等写死动作。
        // 动作轨道保持空轨，后续由手动关键帧、复制粘贴或动作参数生成。
        state.CurrentTime = 0f;
        state.SyncCurrentActionDurationFromTimeline();
    }


    private void BuildCustomTemplate()
    {
        state.CurrentRigTemplateKey = "Custom";
        state.ManualRigTemplateMode = true;

        state.RigRows.Clear();
        state.PsbRows.Clear();
        state.SocketRows.Clear();
        state.AssemblySlots.Clear();
        state.TimelineKeyframes.Clear();
        state.LayerOrderKeyframes.Clear();
        state.ClearMotionPoseEditorState(true);
        state.InvalidateManualAngleRigSignature();

        EnterRigWorkspaceKeepingEmptyEditable(true);
    }


    private void ForceCustomTemplateCleanRigAfterImport()
    {
        state.CurrentRigTemplateKey = "Custom";
        state.ManualRigTemplateMode = true;

        state.RigRows.Clear();
        state.SocketRows.Clear();
        state.AssemblySlots.Clear();

        if (state.PsbRows != null)
        {
            for (int i = 0; i < state.PsbRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.PsbRows[i];
                if (row == null) continue;

                row.boundRigKey = string.Empty;
                row.boundRigName = string.Empty;
                row.bindMode = "未绑定";
                row.bindConfidence = 0f;
                row.mapped = false;

                if (!row.isFolder)
                    row.semantic = "PSD Layer";
            }
        }

        EnterRigWorkspaceKeepingEmptyEditable(true);
    }


    private void BuildHumanTemplate(bool zombie)
    {
        AddRig("Root", "", "角色总控", "Root", 0, false, true);
        AddRig("Pelvis", "Root", "骨盆", "Pelvis", 1, false, true);
        AddRig("Chest", "Pelvis", "胸腔", "Chest", 2, false, true);
        AddRig("Neck", "Chest", "脖子", "Neck", 3, false, true);
        AddRig("Head", "Neck", "头", "Head", 4, false, true);
        AddRig("HeadTop", "Head", "头顶", "HeadTop", 5, false, true);

        AddRig("Shoulder_L", "Neck", "左肩膀", "Shoulder / Left", 3, false, true);
        AddRig("Elbow_L", "Shoulder_L", "左手肘", "Elbow / Left", 4, false, true);
        AddRig("Wrist_L", "Elbow_L", "左手腕", "Wrist / Left", 5, false, true);
        AddRig("HandEnd_L", "Wrist_L", "左手端点", "HandEnd / Left", 6, false, true);

        AddRig("Shoulder_R", "Neck", zombie ? "右肩膀（破损）" : "右肩膀", "Shoulder / Right", 3, false, true);
        AddRig("Elbow_R", "Shoulder_R", zombie ? "右手肘（拖拽）" : "右手肘", "Elbow / Right", 4, false, true);
        AddRig("Wrist_R", "Elbow_R", "右手腕", "Wrist / Right", 5, false, true);
        AddRig("HandEnd_R", "Wrist_R", "右手端点", "HandEnd / Right", 6, false, true);

        AddRig("Hip_L", "Pelvis", "左股骨 / 左髋", "Hip / Left", 2, false, true);
        AddRig("Knee_L", "Hip_L", "左膝盖", "Knee / Left", 3, false, true);
        AddRig("Ankle_L", "Knee_L", "左脚踝", "Ankle / Left", 4, false, true);
        AddRig("Foot_L", "Ankle_L", "左脚", "Foot / Left", 5, false, true);

        AddRig("Hip_R", "Pelvis", "右股骨 / 右髋", "Hip / Right", 2, false, true);
        AddRig("Knee_R", "Hip_R", "右膝盖", "Knee / Right", 3, false, true);
        AddRig("Ankle_R", "Knee_R", "右脚踝", "Ankle / Right", 4, false, true);
        AddRig("Foot_R", "Ankle_R", "右脚", "Foot / Right", 5, false, true);

        BuildDefaultPsbAndSockets();
        BuildDefaultAssemblySlots();
    }

    private void BuildSlimeTemplate()
    {
        AddRig("Root", "", "根节点", "Root", 0, true, true);
        AddRig("Body", "Root", "本体", "Slime / Body", 1, true, true);
        AddRig("Core", "Body", "核心", "Core", 2, false, true);
        AddRig("Stretch_L", "Body", "左变形点", "Deform / Left", 2, false, true);
        AddRig("Stretch_R", "Body", "右变形点", "Deform / Right", 2, false, true);
        BuildDefaultPsbAndSockets();
        BuildDefaultAssemblySlots();
    }

    private void BuildQuadrupedTemplate()
    {
        AddRig("Root", "", "根节点", "Root", 0, true, true);
        AddRig("Body", "Root", "身体", "Body", 1, true, true);
        AddRig("Chest", "Body", "胸部", "Chest", 2, false, true);
        AddRig("Head", "Chest", "头部", "Head", 3, false, true);
        AddRig("FrontLeg_L", "Chest", "左前腿", "FrontLeg / Left", 3, true, true);
        AddRig("FrontFoot_L", "FrontLeg_L", "左前脚", "Foot / Left", 4, false, true);
        AddRig("FrontLeg_R", "Chest", "右前腿", "FrontLeg / Right", 3, true, true);
        AddRig("FrontFoot_R", "FrontLeg_R", "右前脚", "Foot / Right", 4, false, true);
        AddRig("BackLeg_L", "Body", "左后腿", "BackLeg / Left", 2, true, true);
        AddRig("BackFoot_L", "BackLeg_L", "左后脚", "Foot / Left", 3, false, true);
        AddRig("BackLeg_R", "Body", "右后腿", "BackLeg / Right", 2, true, true);
        AddRig("BackFoot_R", "BackLeg_R", "右后脚", "Foot / Right", 3, false, true);
        AddRig("Tail", "Body", "尾巴", "Tail", 2, false, true);
        BuildDefaultPsbAndSockets();
        BuildDefaultAssemblySlots();
    }

    private void BuildBirdTemplate()
    {
        AddRig("Root", "", "根节点", "Root", 0, true, true);
        AddRig("Body", "Root", "身体", "Body", 1, true, true);
        AddRig("Head", "Body", "头部", "Head", 2, false, true);
        AddRig("Wing_L", "Body", "左翼", "Wing / Left", 2, true, true);
        AddRig("Wing_R", "Body", "右翼", "Wing / Right", 2, true, true);
        AddRig("Leg_L", "Body", "左腿", "Leg / Left", 2, true, true);
        AddRig("Foot_L", "Leg_L", "左爪足", "Foot / Left", 3, false, true);
        AddRig("Leg_R", "Body", "右腿", "Leg / Right", 2, true, true);
        AddRig("Foot_R", "Leg_R", "右爪足", "Foot / Right", 3, false, true);
        AddRig("TailFeather", "Body", "尾羽", "Tail / Feather", 2, false, true);
        BuildDefaultPsbAndSockets();
        BuildDefaultAssemblySlots();
    }

    private void BuildSmallMonsterTemplate()
    {
        AddRig("Root", "", "根节点", "Root", 0, true, true);
        AddRig("Body", "Root", "身体", "Torso", 1, true, true);
        AddRig("Head", "Body", "头部/眼核", "Head / Eye", 2, false, true);
        AddRig("Arm_L", "Body", "左爪", "Claw / Left", 2, false, true);
        AddRig("Arm_R", "Body", "右爪", "Claw / Right", 2, false, true);
        AddRig("Leg_L", "Body", "左足", "Foot / Left", 2, false, true);
        AddRig("Leg_R", "Body", "右足", "Foot / Right", 2, false, true);
        AddRig("Tail", "Root", "尾部", "Tail", 1, false, true);
        AddRig("CoreGlow", "Root", "核心光", "Accessory / Light", 1, false, true);
        BuildDefaultPsbAndSockets();
        BuildDefaultAssemblySlots();
    }

    private void AddRig(string key, string parentKey, string name, string semantic, int depth, bool isFolder, bool expanded)
    {
        state.RigRows.Add(new SkyPrisonAnimationRigRow
        {
            key = key,
            parentKey = parentKey,
            name = name,
            semantic = semantic,
            depth = depth,
            isFolder = isFolder,
            expanded = expanded,
            mapped = true,
            previewIconNumber = isFolder ? 45 : 42,
            visualSlotKey = GuessVisualSlot(semantic)
        });
    }

    private string GuessVisualSlot(string semantic)
    {
        if (semantic.Contains("Hair")) return "Hair";
        if (semantic.Contains("Head")) return "Head";
        if (semantic.Contains("Shoulder") || semantic.Contains("Elbow") || semantic.Contains("Wrist") || semantic.Contains("HandEnd")) return "Hand";
        if (semantic.Contains("Hip") || semantic.Contains("Knee")) return "Pants";
        if (semantic.Contains("Ankle")) return "Socks";
        if (semantic.Contains("Foot")) return "Shoes";
        if (semantic.Contains("Weapon") || semantic.Contains("Claw")) return "Weapon";
        return "Chest";
    }

    private void BuildDefaultPsbAndSockets()
    {
        state.PsbRows.Clear();
        state.PsbRows.Add(new SkyPrisonAnimationRigRow { key = "Source.psb", name = "Source.psb", semantic = "PSB", depth = 0, locked = true, isFolder = true, expanded = true, previewIconNumber = 45 });
        state.SocketRows.Clear();
        state.SocketRows.Add(new SkyPrisonAnimationRigRow { key = "FootSocket_L", name = "左脚步点", semantic = "Footstep / Left", depth = 0, hasKey = true });
        state.SocketRows.Add(new SkyPrisonAnimationRigRow { key = "FootSocket_R", name = "右脚步点", semantic = "Footstep / Right", depth = 0, hasKey = true });
        state.SocketRows.Add(new SkyPrisonAnimationRigRow { key = "HitboxAnchor", name = "攻击判定锚点", semantic = "Hitbox", depth = 0, hasKey = true });
        state.SocketRows.Add(new SkyPrisonAnimationRigRow { key = "VisionOrigin", name = "视线起点", semantic = "Vision", depth = 0 });
        state.SocketRows.Add(new SkyPrisonAnimationRigRow { key = "VoiceOrigin", name = "声音起点", semantic = "Sound", depth = 0 });
    }

    private void BuildDefaultAssemblySlots()
    {
        state.AssemblySlots.Clear();
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "BaseBody", displayName = "基础身体", assetKey = "BaseBody_None", boundPartKey = "Root", visualSlotKey = "Body", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Head", displayName = "头部", assetKey = "Head_None", boundPartKey = "Head", visualSlotKey = "Head", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Hair", displayName = "发型", assetKey = "Hair_None", boundPartKey = "Head", visualSlotKey = "Hair", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Top", displayName = "上衣", assetKey = "Top_None", boundPartKey = "Chest", visualSlotKey = "Outfit", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Hand", displayName = "手部", assetKey = "Hand_None", boundPartKey = "Wrist_L / Wrist_R", visualSlotKey = "Hand", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Pants", displayName = "裤子", assetKey = "Pants_None", boundPartKey = "Hip_L / Hip_R", visualSlotKey = "Pants", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Socks", displayName = "袜子", assetKey = "Socks_None", boundPartKey = "Ankle_L / Ankle_R", visualSlotKey = "Socks", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Shoes", displayName = "鞋子", assetKey = "Shoes_None", boundPartKey = "Foot_L / Foot_R", visualSlotKey = "Shoes", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Accessory", displayName = "饰品", assetKey = "Accessory_None", boundPartKey = "Head", visualSlotKey = "Accessory", visible = true });
        state.AssemblySlots.Add(new SkyPrisonAnimationAssemblySlot { slotKey = "Weapon", displayName = "武器", assetKey = "Weapon_None", boundPartKey = "Wrist_R", visualSlotKey = "Weapon", visible = true });
    }

    private void ImportPsdOrPsbByPanel()
    {
        string path = EditorUtility.OpenFilePanel("导入PSD/PSB到动作工作台", Application.dataPath, "psd,psb");
        if (string.IsNullOrEmpty(path))
            return;

        ImportPsdOrPsbPath(path);
    }

    private void HandlePsdDragAndDrop(Rect dropRect)
    {
        Event e = Event.current;
        if (e == null)
            return;

        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !dropRect.Contains(e.mousePosition))
            return;

        string path = GetFirstDraggedPsdPath();
        if (string.IsNullOrEmpty(path))
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (e.type == EventType.DragUpdated)
            DrawPsdDropOverlay(dropRect, path);

        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            ImportPsdOrPsbPath(path);
        }

        e.Use();
    }

    private void DrawPsdDropOverlay(Rect rect, string path)
    {
        if (rect.width <= 1f || rect.height <= 1f)
            return;

        Rect overlay = new Rect(rect.x + 10f, rect.y + 10f, Mathf.Max(1f, rect.width - 20f), Mathf.Max(1f, rect.height - 20f));
        EditorGUI.DrawRect(overlay, new Color(0.18f, 0.36f, 0.58f, 0.32f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(overlay, new Color(0.58f, 0.78f, 1f, 0.90f));

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            normal = { textColor = Color.white }
        };

        GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.86f, 0.92f, 1f, 1f) }
        };

        Rect title = new Rect(overlay.x, overlay.center.y - 22f, overlay.width, 24f);
        Rect sub = new Rect(overlay.x, title.yMax + 2f, overlay.width, 18f);

        GUI.Label(title, "松开鼠标：导入 PSD / PSB 到动作工作台", titleStyle);
        GUI.Label(sub, Path.GetFileName(path), subStyle);
    }

    private string GetFirstDraggedPsdPath()
    {
        if (DragAndDrop.paths != null)
        {
            for (int i = 0; i < DragAndDrop.paths.Length; i++)
            {
                string p = DragAndDrop.paths[i];
                if (IsPsdOrPsbPath(p))
                    return p;
            }
        }

        if (DragAndDrop.objectReferences != null)
        {
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                UnityEngine.Object obj = DragAndDrop.objectReferences[i];
                if (obj == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (IsPsdOrPsbPath(assetPath))
                    return assetPath;
            }
        }

        return string.Empty;
    }

    private bool IsPsdOrPsbPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".psd" || ext == ".psb";
    }

    private void ImportPsdOrPsbPath(string inputPath)
    {
        string assetPath;
        try
        {
            assetPath = NormalizeToProjectAssetPath(inputPath);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("导入失败", "没有拿到有效的PSD/PSB路径。", "确定");
                return;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("导入PSD/PSB失败", ex.Message, "确定");
            return;
        }

        if (HasWorkbenchContentForImportChoice())
        {
            int mode = EditorUtility.DisplayDialogComplex(
                "导入PSD/PSB",
                "当前工作台已有内容。\n\n请选择这次导入的目的：",
                "重置贴图",
                "新建新工程",
                "取消");

            if (mode == 2)
                return;

            if (mode == 0)
            {
                ImportPsdOrPsbAsTextureReset(assetPath);
                return;
            }

            if (!ConfirmBeforeDestructiveOperation("新建新工程", "新建工程会刷新当前动作工作台内容。是否先保存当前2D模型包？"))
                return;
        }

        ShowTemplateChoiceMenu("选择置入模板", templateKey =>
        {
            ImportPsdOrPsbAsNewProject(assetPath, templateKey);
        });
    }

    private bool HasWorkbenchContentForImportChoice()
    {
        if (state == null)
            return false;
        if (state.RigRows != null && state.RigRows.Count > 0)
            return true;
        if (state.PsbRows != null && state.PsbRows.Count > 0)
            return true;
        if (state.SocketRows != null && state.SocketRows.Count > 0)
            return true;
        if (state.AssemblySlots != null && state.AssemblySlots.Count > 0)
            return true;
        return !string.IsNullOrEmpty(state.SourcePsdAssetPath);
    }

    private void ShowTemplateChoiceMenu(string title, Action<string> onSelected)
    {
        SkyPrisonAnimationTemplateChoiceWindow.OpenCentered(title, onSelected);
    }

    private sealed class SkyPrisonAnimationTemplateChoiceWindow : EditorWindow
    {
        private static readonly string[] Keys =
        {
            "Human",
            "Slime",
            "Zombie",
            "Quadruped",
            "Bird",
            "SmallMonster",
            "Custom"
        };

        private static readonly string[] Labels =
        {
            "人形模板",
            "史莱姆模板",
            "丧尸模板",
            "四足动物模板",
            "鸟类模板",
            "小型怪物模板",
            "自定义"
        };

        private static SkyPrisonAnimationTemplateChoiceWindow activeWindow;
        public static bool HasActiveWindow { get { return activeWindow != null; } }
        private static double nextTopMostFocusTime;

        private Action<string> onSelected;
        private int selectedIndex = 0;
        private string windowTitle = "选择置入模板";

        public static void OpenCentered(string title, Action<string> onSelected)
        {
            if (activeWindow != null)
                activeWindow.Close();

            SkyPrisonAnimationTemplateChoiceWindow win = CreateInstance<SkyPrisonAnimationTemplateChoiceWindow>();
            win.titleContent = new GUIContent(title);
            win.windowTitle = string.IsNullOrEmpty(title) ? "选择置入模板" : title;
            win.onSelected = onSelected;
            win.minSize = new Vector2(360f, 300f);
            win.maxSize = new Vector2(360f, 300f);
            win.position = CenterRect(new Vector2(360f, 300f));

            activeWindow = win;
            templateChoiceModalOpen = true;

            // 使用普通 Utility 窗口；不再用 update/OnLostFocus 强制抢焦点。
            // 模板选择不是系统级模态，不应该锁死整个动作工作台。
            win.ShowUtility();
            win.Focus();
            win.Repaint();
        }

        private static void KeepTemplateChoiceWindowOnTop()
        {
            // 保留方法名，避免旧代码引用；但不再抢焦点。
            if (activeWindow == null)
                templateChoiceModalOpen = false;
        }

        private static Rect CenterRect(Vector2 size)
        {
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            if (main.width <= 1f || main.height <= 1f)
                main = new Rect(0f, 0f, Screen.currentResolution.width, Screen.currentResolution.height);

            return new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                size.x,
                size.y);
        }

        private void OnDisable()
        {
            if (activeWindow == this)
                activeWindow = null;

            templateChoiceModalOpen = false;
            EditorApplication.update -= KeepTemplateChoiceWindowOnTop;
        }

        private void OnLostFocus()
        {
            // 不再 Focus() 抢回焦点。旧写法会让工作台像被弹窗残留锁住。
        }

        private void OnDestroy()
        {
            if (activeWindow == this)
                activeWindow = null;

            templateChoiceModalOpen = false;
            EditorApplication.update -= KeepTemplateChoiceWindowOnTop;
        }

        private void OnGUI()
        {
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                templateChoiceModalOpen = false;
                activeWindow = null;
                Close();
                e.Use();
                return;
            }

            GUILayout.Space(12f);
            EditorGUILayout.LabelField(windowTitle, EditorStyles.boldLabel);
            GUILayout.Space(6f);
            EditorGUILayout.HelpBox("选择模板后点击“确定”，再创建新的 PSB 动作工程。弹窗关闭前不会响应工作台其它操作。", MessageType.Info);
            GUILayout.Space(8f);

            for (int i = 0; i < Labels.Length; i++)
            {
                bool selected = selectedIndex == i;

                Rect rowRect = EditorGUILayout.GetControlRect(false, 24f);
                rowRect.x += 8f;
                rowRect.width -= 8f;

                bool next = GUI.Toggle(
                    rowRect,
                    selected,
                    Labels[i],
                    EditorStyles.radioButton);

                if (next && !selected)
                    selectedIndex = i;
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("取消", GUILayout.Height(28f)))
            {
                templateChoiceModalOpen = false;
                activeWindow = null;
                Close();
            }

            if (GUILayout.Button("确定", GUILayout.Height(28f)))
            {
                string key = Keys[Mathf.Clamp(selectedIndex, 0, Keys.Length - 1)];
                Action<string> callback = onSelected;
                templateChoiceModalOpen = false;
                activeWindow = null;
                Close();
                callback?.Invoke(key);
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10f);
        }
    }

    private void ImportPsdOrPsbAsNewProject(string assetPath, string templateKey)
    {
        try
        {
            templateKey = NormalizeTemplateKey(templateKey);
            bool customTemplate = IsCustomTemplateKey(templateKey);

            ApplyTemplate(templateKey);
            BuildPsbRowsFromAsset(assetPath);
            if (!customTemplate)
                state.RefreshRigLinksFromPsbBindings();

            if (IsHumanLikeTemplateKey(templateKey))
                ApplyHumanTemplateActionPacksToCurrentWorkbench(false);

            if (customTemplate)
            {
                ForceCustomTemplateCleanRigAfterImport();
            }
            else
            {
                // 非 Custom 默认去 PSB 图层方便绑定，但 Rig 页不能因空数据被锁。
                state.StructureTab = SkyPrisonAnimationStructureTab.PsbLayer;
                state.ShowRigLines = true;
                state.ShowRigEdit = false;
            }

            state.ClearStructureUndo();
            state.ClearRigUndo();
            state.SourcePsdAssetPath = assetPath;
            state.ClearMotionPoseEditorState(true);
            state.InvalidateManualAngleRigSignature();
            state.EnsureMotionPoseEditorStateMatchesCurrentRig();
            currentPackageName = Path.GetFileNameWithoutExtension(assetPath);
            currentPackageAssetPath = string.Empty;
            hasUnsavedChanges = true;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("导入PSD/PSB失败", ex.Message, "确定");
        }
    }


    private void ImportPsdOrPsbAsTextureReset(string assetPath)
    {
        try
        {
            MergePsbRowsFromAsset(assetPath);
            if (!state.IsCustomPurePsbMode)
                state.RefreshRigLinksFromPsbBindings();
            if (IsHumanLikeTemplateKey(state.CurrentRigTemplateKey))
                ApplyHumanTemplateActionPacksToCurrentWorkbench(false);
            state.SourcePsdAssetPath = assetPath;
            state.ClearMotionPoseEditorState(true);
            state.InvalidateManualAngleRigSignature();
            state.EnsureMotionPoseEditorStateMatchesCurrentRig();

            if (state.ManualRigTemplateMode || IsCustomTemplateKey(state.CurrentRigTemplateKey))
                EnterRigWorkspaceKeepingEmptyEditable(true);
            else
            {
                state.StructureTab = SkyPrisonAnimationStructureTab.PsbLayer;
                state.ShowRigLines = true;
            }

            currentPackageName = Path.GetFileNameWithoutExtension(assetPath);
            currentPackageAssetPath = string.Empty;
            hasUnsavedChanges = true;
            GUI.FocusControl(null);
            RepaintOwnerWindow();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("重置贴图失败", ex.Message, "确定");
        }
    }


    private string NormalizeToProjectAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("Assets/"))
            return normalized;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        if (normalized.StartsWith(projectRoot + "/Assets/"))
            return normalized.Substring(projectRoot.Length + 1);

        EnsureImportedPsdFolder();
        string fileName = Path.GetFileName(normalized);
        string dstAssetPath = ImportedPsdFolder + "/" + fileName;
        string dstAbsolute = AssetPathToAbsolutePath(dstAssetPath);
        string folder = Path.GetDirectoryName(dstAbsolute);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        // 同名 PSD/PSB 置入必须覆盖，而不是 GenerateUniqueAssetPath 后产生 xxx 1/2/3。
        File.Copy(normalized, dstAbsolute, true);
        AssetDatabase.ImportAsset(dstAssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        return dstAssetPath;
    }

    private void MergePsbRowsFromAsset(string assetPath)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        Dictionary<string, float> psbLayerWeights = BuildPsbLayerWeightMap(assetPath);
        List<Sprite> sprites = new List<Sprite>();

        for (int i = 0; i < assets.Length; i++)
        {
            Sprite sp = assets[i] as Sprite;
            if (sp != null)
                sprites.Add(sp);
        }

        if (sprites.Count <= 0)
        {
            BuildPsbRowsFromAsset(assetPath);
            return;
        }

        sprites.Sort((a, b) =>
        {
            float wa = psbLayerWeights.TryGetValue(a.name, out float va) ? va : 0f;
            float wb = psbLayerWeights.TryGetValue(b.name, out float vb) ? vb : 0f;
            int byWeight = wa.CompareTo(wb);
            if (byWeight != 0) return byWeight;
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        });

        for (int i = 0; i < sprites.Count; i++)
        {
            Sprite sp = sprites[i];
            if (sp == null)
                continue;

            float weight = psbLayerWeights.TryGetValue(sp.name, out float w) ? w : i;
            weight = BuildBaseBodyPreviewLayerWeight(sp.name, weight, i);
            SkyPrisonAnimationRigRow existing = FindPsbRowBySpriteName(sp.name);
            if (existing != null)
            {
                existing.sourceAssetPath = assetPath;
                existing.sourceSpriteName = sp.name;
                existing.sourceLayerPath = sp.name;
                existing.usePsbLayerWeight = true;
                existing.psbLayerWeight = weight;
                if (string.IsNullOrEmpty(existing.name)) existing.name = sp.name;
                if (state.ManualRigTemplateMode)
                    existing.semantic = "PSD Layer";
                else if (string.IsNullOrEmpty(existing.semantic) || existing.semantic == "PSD Layer")
                    existing.semantic = GuessSemanticFromLayerName(sp.name);
            }
            else
            {
                AddPsbSpriteRow(assetPath, sp, state.PsbRows.Count, weight);
            }
        }

        if (ShouldWarnMergedPsdImport(assetPath, sprites))
            ShowMergedPsdImportWarning(assetPath, sprites[0].name);
    }

    private SkyPrisonAnimationRigRow FindPsbRowBySpriteName(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName) || state.PsbRows == null)
            return null;

        string safe = MakeSafeKey(spriteName);
        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null) continue;
            if (string.Equals(row.sourceSpriteName, spriteName, StringComparison.OrdinalIgnoreCase)) return row;
            if (string.Equals(row.name, spriteName, StringComparison.OrdinalIgnoreCase)) return row;
            if (string.Equals(row.key, safe, StringComparison.OrdinalIgnoreCase)) return row;
        }
        return null;
    }

    private void BuildPsbRowsFromAsset(string assetPath)
    {
        state.PsbRows.Clear();

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        Dictionary<string, float> psbLayerWeights = BuildPsbLayerWeightMap(assetPath);
        List<Sprite> sprites = new List<Sprite>();

        for (int i = 0; i < assets.Length; i++)
        {
            Sprite sp = assets[i] as Sprite;
            if (sp != null)
                sprites.Add(sp);
        }

        if (sprites.Count > 0)
        {
            sprites.Sort((a, b) =>
            {
                float wa = psbLayerWeights.TryGetValue(a.name, out float va) ? va : 0f;
                float wb = psbLayerWeights.TryGetValue(b.name, out float vb) ? vb : 0f;
                int byWeight = wa.CompareTo(wb);
                if (byWeight != 0) return byWeight;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < sprites.Count; i++)
            {
                float weight = psbLayerWeights.TryGetValue(sprites[i].name, out float w) ? w : i;
                weight = BuildBaseBodyPreviewLayerWeight(sprites[i].name, weight, i);
                AddPsbSpriteRow(assetPath, sprites[i], i, weight);
            }

            if (ShouldWarnMergedPsdImport(assetPath, sprites))
                ShowMergedPsdImportWarning(assetPath, sprites[0].name);
        }
        else
        {
            state.PsbRows.Add(new SkyPrisonAnimationRigRow
            {
                key = MakeSafeKey(Path.GetFileNameWithoutExtension(assetPath)),
                name = Path.GetFileNameWithoutExtension(assetPath),
                semantic = "PSD Texture",
                depth = 0,
                parentKey = "",
                visible = true,
                mapped = false,
                hasKey = false,
                sourceAssetPath = assetPath,
                sourceSpriteName = "",
                sourceLayerPath = Path.GetFileName(assetPath),
                previewColor = new Color(0.72f, 0.74f, 0.78f, 1f)
            });

            ShowMergedPsdImportWarning(assetPath, string.Empty);
        }

        state.SelectedRig = state.PsbRows.Count > 0 ? 0 : -1;
        state.SelectedRigRows.Clear();
    }

    private bool ShouldWarnMergedPsdImport(string assetPath, List<Sprite> sprites)
    {
        if (sprites == null || sprites.Count != 1)
            return false;

        string ext = Path.GetExtension(assetPath).ToLowerInvariant();
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string spriteName = sprites[0] != null ? sprites[0].name : string.Empty;

        if (ext == ".psd")
            return true;

        if (string.IsNullOrEmpty(spriteName))
            return true;

        string lowerSprite = spriteName.ToLowerInvariant();
        string lowerFile = fileName.ToLowerInvariant();
        return lowerSprite == lowerFile || lowerSprite == lowerFile + "_0" || lowerSprite.EndsWith("_0");
    }

    private void ShowMergedPsdImportWarning(string assetPath, string spriteName)
    {
        string title = "PSD/PSB没有拆出图层";
        string fileName = Path.GetFileName(assetPath);
        string recognized = string.IsNullOrEmpty(spriteName)
            ? "没有识别到任何子Sprite"
            : "当前只识别到一个Sprite：" + spriteName;

        string message =
            "已导入：" + fileName + "\n" +
            recognized + "\n\n" +
            "这通常说明 Unity 只拿到了合成图，没有拿到PSD/PSB的图层结构。\n\n" +
            "处理方式：\n" +
            "1. 优先把PSD另存为PSB，并保留图层。\n" +
            "2. 确认 Package Manager 已安装 2D PSD Importer。\n" +
            "3. 选中PSB，在Inspector里开启按图层生成Sprite / Multiple。\n" +
            "4. 需要保留文件夹层级时，开启 Character Rig / Use Layer Group。\n\n" +
            "工作台没有继续智能拆分，是为了避免把一张合成图误判成真实图层。";

        EditorUtility.DisplayDialog(title, message, "确定");
    }

    private Dictionary<string, float> BuildPsbLayerWeightMap(string assetPath)
    {
        Dictionary<string, float> map = new Dictionary<string, float>();
        if (string.IsNullOrEmpty(assetPath))
            return map;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        GameObject bestRoot = null;
        SpriteRenderer[] bestRenderers = null;

        for (int i = 0; i < assets.Length; i++)
        {
            GameObject go = assets[i] as GameObject;
            if (go == null) continue;
            SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers == null || renderers.Length == 0) continue;
            if (bestRenderers == null || renderers.Length > bestRenderers.Length)
            {
                bestRoot = go;
                bestRenderers = renderers;
            }
        }

        if (bestRoot == null || bestRenderers == null)
            return map;

        for (int i = 0; i < bestRenderers.Length; i++)
        {
            SpriteRenderer sr = bestRenderers[i];
            if (sr == null || sr.sprite == null) continue;
            int hierarchyOrder = GetPsbImportHierarchyOrder(sr.transform);
            float weight = sr.sortingOrder * 100000f + hierarchyOrder;
            map[sr.sprite.name] = weight;
            map[sr.gameObject.name] = weight;
        }

        return map;
    }

    private int GetPsbImportHierarchyOrder(Transform t)
    {
        int order = 0;
        int mul = 1;
        Transform cur = t;
        while (cur != null)
        {
            order += cur.GetSiblingIndex() * mul;
            mul *= 100;
            cur = cur.parent;
        }
        return order;
    }

    private void AddPsbSpriteRow(string assetPath, Sprite sprite, int index, float layerWeight)
    {
        string layerName = sprite != null ? sprite.name : "Layer_" + index;
        layerWeight = BuildBaseBodyPreviewLayerWeight(layerName, layerWeight, index);
        string key = MakeSafeKey(layerName);
        if (string.IsNullOrEmpty(key))
            key = "Layer_" + index;

        while (state.PsbRows.Exists(x => x.key == key))
            key = key + "_" + index;

        SkyPrisonAnimationRigRow row = new SkyPrisonAnimationRigRow
        {
            key = key,
            name = layerName,
            semantic = state.ManualRigTemplateMode ? "PSD Layer" : GuessSemanticFromLayerName(layerName),
            depth = GuessDepthFromLayerName(layerName),
            parentKey = "",
            visible = true,
            mapped = false,
            hasKey = false,
            sourceAssetPath = assetPath,
            sourceSpriteName = layerName,
            sourceLayerPath = layerName,
            previewColor = GuessColorFromLayerName(layerName),
            usePsbLayerWeight = true,
            psbLayerWeight = layerWeight,
            manualLayerWeightOffset = 0f
        };

        state.AutoBindSinglePsbLayer(row);
        state.PsbRows.Add(row);
    }


    private float BuildBaseBodyPreviewLayerWeight(string layerName, float importedWeight, int index)
    {
        // 裸模/基础 PSB 的 Unity 导入顺序经常会把身体压在左手、左脚前面。
        // 这里不改图层名、不改绑定，只在工作台预览层级里做一层稳定的语义排序。
        string n = NormalizeBaseBodyLayerName(layerName);
        if (string.IsNullOrEmpty(n))
            return importedWeight;

        bool left = HasBaseBodyLeftMarker(n);
        bool right = HasBaseBodyRightMarker(n);
        bool hand = ContainsAnyBaseBody(n, "hand", "palm", "finger", "wrist", "forearm", "lowerarm", "upperarm", "arm", "手", "腕", "掌", "指", "臂");
        bool foot = ContainsAnyBaseBody(n, "foot", "feet", "toe", "ankle", "leg", "thigh", "knee", "calf", "shin", "脚", "足", "腿", "膝", "踝");
        bool upperBody = ContainsAnyBaseBody(n, "torso_upper", "upper_torso", "body_upper", "upperbody", "chest", "bust", "spine_upper", "上半身", "胸", "躯干上", "身体上");
        bool lowerBody = ContainsAnyBaseBody(n, "torso_lower", "lower_torso", "body_lower", "lowerbody", "pelvis", "hip", "waist", "abdomen", "belly", "spine_lower", "下半身", "骨盆", "腰", "腹", "躯干下", "身体下");
        bool body = upperBody || lowerBody || ContainsAnyBaseBody(n, "body", "torso", "basebody", "nude", "skin", "身体", "躯干", "裸");
        bool head = ContainsAnyBaseBody(n, "head", "face", "neck", "头", "顔", "脸", "首", "脖");
        bool hair = ContainsAnyBaseBody(n, "hair", "bang", "fringe", "髪", "发", "刘海");
        bool eye = ContainsAnyBaseBody(n, "eye", "brow", "lash", "pupil", "iris", "眼", "眉", "睫", "瞳");

        float band = float.NaN;

        if (ContainsAnyBaseBody(n, "shadow", "影")) band = -20f;
        else if (ContainsAnyBaseBody(n, "hair_back", "back_hair", "后发", "後髪", "后髪")) band = 0f;
        else if (right && foot) band = 18f;
        else if (right && hand) band = 24f;
        else if (lowerBody) band = 34f;
        else if (upperBody || body) band = 40f;
        // 角色自身左侧是前景侧：左脚、左手必须盖在下身/上身前面。
        else if (left && foot) band = 56f;
        else if (left && hand) band = 62f;
        else if (head) band = 78f;
        else if (hair) band = 88f;
        else if (eye) band = 96f;

        if (float.IsNaN(band))
            return importedWeight;

        // 保留少量导入顺序，避免同一语义带里的多层完全重叠后乱跳。
        float importedFine = Mathf.Repeat(importedWeight, 1000f) * 0.001f;
        return band * 1000f + importedFine + index * 0.01f;
    }

    private string NormalizeBaseBodyLayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return (" " + name.Replace('\\', '/').Replace('-', '_').Replace('.', '_') + " ").ToLowerInvariant();
    }

    private bool HasBaseBodyLeftMarker(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        return n.Contains("_l ") || n.Contains(" l_") || n.Contains("_l_") || n.Contains("/l/") ||
               n.Contains(" left ") || n.Contains("_left") || n.Contains("left_") ||
               n.Contains("左") || n.Contains(" lhand") || n.Contains("l_hand") || n.Contains("lhand") ||
               n.Contains(" lfoot") || n.Contains("l_foot") || n.Contains("lfoot");
    }

    private bool HasBaseBodyRightMarker(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        return n.Contains("_r ") || n.Contains(" r_") || n.Contains("_r_") || n.Contains("/r/") ||
               n.Contains(" right ") || n.Contains("_right") || n.Contains("right_") ||
               n.Contains("右") || n.Contains(" rhand") || n.Contains("r_hand") || n.Contains("rhand") ||
               n.Contains(" rfoot") || n.Contains("r_foot") || n.Contains("rfoot");
    }

    private bool ContainsAnyBaseBody(string n, params string[] keys)
    {
        if (string.IsNullOrEmpty(n) || keys == null) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            if (!string.IsNullOrEmpty(k) && n.Contains(k.ToLowerInvariant()))
                return true;
        }
        return false;
    }

    private string GuessSemanticFromLayerName(string name)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();

        // 这里也按企业绑定顺序：具体部位优先，泛称最后。
        if (n.Contains("eyebrow") || n.Contains("brow") || n.Contains("眉")) return "Head";
        if (n.Contains("eye") || n.Contains("pupil") || n.Contains("iris") || n.Contains("sclera") || n.Contains("眼") || n.Contains("瞳") || n.Contains("眼白")) return "Head";
        if (n.Contains("bang") || n.Contains("fringe") || n.Contains("front_hair") || n.Contains("hair_front") || n.Contains("刘海") || n.Contains("前发") || n.Contains("前髪")) return "HairFront";
        if (n.Contains("hair_back") || n.Contains("back_hair") || n.Contains("后发") || n.Contains("后髪") || n.Contains("後髪")) return "HairBack";
        if (n.Contains("side_hair") || n.Contains("hair_side") || n.Contains("sidelock") || n.Contains("侧发") || n.Contains("横髪")) return "HairSide";
        if (n.Contains("hair") || n.Contains("髪") || n.Contains("发")) return "Hair";
        if (n.Contains("face") || n.Contains("mouth") || n.Contains("nose") || n.Contains("cheek") || n.Contains("脸") || n.Contains("口") || n.Contains("鼻")) return "Head";
        if (n.Contains("head") || n.Contains("头")) return "Head";

        if (n.Contains("forearm") || n.Contains("lowerarm") || n.Contains("lower_arm") || n.Contains("下臂") || n.Contains("前腕") || n.Contains("手腕")) return "Elbow";
        if (n.Contains("upperarm") || n.Contains("upper_arm") || n.Contains("上臂") || n.Contains("二の腕")) return "Shoulder";
        if (n.Contains("hand") || n.Contains("palm") || n.Contains("finger") || n.Contains("手掌") || n.Contains("手指")) return "Wrist";
        if (n.Contains("arm") || n.Contains("腕") || n.Contains("臂") || n.Contains("手臂")) return "Shoulder";

        if (n.Contains("lower_leg") || n.Contains("leg_lower") || n.Contains("calf") || n.Contains("shin") || n.Contains("小腿") || n.Contains("脛")) return "Knee";
        if (n.Contains("upper_leg") || n.Contains("leg_upper") || n.Contains("thigh") || n.Contains("大腿") || n.Contains("太もも")) return "Hip";
        if (n.Contains("ankle") || n.Contains("脚踝") || n.Contains("足首")) return "Ankle";
        if (n.Contains("foot") || n.Contains("feet") || n.Contains("shoe") || n.Contains("sock") || n.Contains("sole") || n.Contains("脚掌") || n.Contains("足先") || n.Contains("つま先") || n.Contains("靴") || n.Contains("鞋") || n.Contains("袜")) return "Foot";
        if (n.Contains("leg") || n.Contains("腿") || n.Contains("脚") || n.Contains("足")) return "Knee";

        if (n.Contains("body") || n.Contains("torso") || n.Contains("chest") || n.Contains("身体") || n.Contains("躯干") || n.Contains("胸")) return "Chest";
        if (n.Contains("cloth") || n.Contains("dress") || n.Contains("coat") || n.Contains("skirt") || n.Contains("衣") || n.Contains("裙")) return "Outfit";
        return "PSD Layer";
    }
    private int GuessDepthFromLayerName(string name)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();

        // PSB 图层初始深度只作为显示层级建议；真正遮挡仍交给 Layer Weight / 关键帧。
        // 数字越大越靠前：后发/尾巴靠后，脸/刘海/眼眉/饰品靠前。
        if (n.Contains("shadow") || n.Contains("影") || n.Contains("阴影")) return -8;
        if (n.Contains("back_hair") || n.Contains("hair_back") || n.Contains("后发") || n.Contains("後髪") || n.Contains("后髪")) return -3;
        if (n.Contains("tail") || n.Contains("尾")) return -2;
        if (n.Contains("body") || n.Contains("torso") || n.Contains("身体") || n.Contains("躯干") || n.Contains("胸")) return 0;
        if (n.Contains("leg") || n.Contains("腿") || n.Contains("脚") || n.Contains("足")) return 1;
        if (n.Contains("arm") || n.Contains("hand") || n.Contains("腕") || n.Contains("臂") || n.Contains("手")) return 2;
        if (n.Contains("head") || n.Contains("face") || n.Contains("头") || n.Contains("脸")) return 3;
        if (n.Contains("hair") || n.Contains("髪") || n.Contains("发")) return 4;
        if (n.Contains("eye") || n.Contains("brow") || n.Contains("pupil") || n.Contains("iris") || n.Contains("眼") || n.Contains("眉") || n.Contains("瞳")) return 5;
        if (n.Contains("accessory") || n.Contains("ribbon") || n.Contains("clip") || n.Contains("饰") || n.Contains("飾") || n.Contains("发卡")) return 6;
        return 0;
    }

    private Color GuessColorFromLayerName(string name)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("head") || n.Contains("头")) return new Color(0.42f, 0.82f, 0.52f, 1f);
        if (n.Contains("hand") || n.Contains("arm") || n.Contains("手") || n.Contains("臂")) return new Color(0.95f, 0.70f, 0.24f, 1f);
        if (n.Contains("foot") || n.Contains("leg") || n.Contains("脚") || n.Contains("腿")) return new Color(0.72f, 0.42f, 0.92f, 1f);
        if (n.Contains("body") || n.Contains("torso") || n.Contains("身体") || n.Contains("躯干")) return new Color(0.30f, 0.58f, 0.92f, 1f);
        return new Color(0.72f, 0.74f, 0.78f, 1f);
    }

    private string MakeSafeKey(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        string s = name.Trim();
        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-';
            if (!ok)
                chars[i] = '_';
        }
        return new string(chars).Trim('_');
    }

    private static void EnsureImportedPsdFolder()
    {
        EnsureFolder("Assets/_Project");
        EnsureFolder("Assets/_Project/Data");
        EnsureFolder("Assets/_Project/Data/AnimationWorkbench");
        EnsureFolder(ImportedPsdFolder);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
        string name = Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private void OpenModelPackage()
    {
        if (!ConfirmBeforeDestructiveOperation("打开2D模型包", "打开文件会替换当前动作工作台内容。是否先保存当前2D模型包？"))
            return;

        string absoluteFolder = GetAbsoluteFixedPackageFolder();
        string path = EditorUtility.OpenFilePanel("打开2D模型包", absoluteFolder, "json");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            SkyPrisonAnimationModelPackage package = JsonUtility.FromJson<SkyPrisonAnimationModelPackage>(json);
            if (package == null)
            {
                EditorUtility.DisplayDialog("打开失败", "文件不是有效的2D模型包。", "确定");
                return;
            }

            ApplyPackage(package);
            currentPackageAssetPath = AbsoluteToAssetPath(path);
            currentPackageName = string.IsNullOrEmpty(package.displayName)
                ? StripModelPackageExtension(Path.GetFileName(path))
                : StripModelPackageExtension(package.displayName);
            hasUnsavedChanges = false;
            SaveSessionCache(true);
            AssetDatabase.Refresh();
            RepaintOwnerWindow();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("打开失败", ex.Message, "确定");
        }
    }

    private void SaveModelPackage()
    {
        if (string.IsNullOrEmpty(currentPackageAssetPath))
        {
            string safeName = StripModelPackageExtension(MakeSafeFileName(currentPackageName));
            if (string.IsNullOrEmpty(safeName) || safeName == "未命名2D模型包")
                safeName = "New2DModelPackage";

            currentPackageAssetPath = FixedPackageFolder + "/" + safeName + "." + PackageExtension;
        }

        SavePackageToAssetPath(currentPackageAssetPath);
    }

    private void SaveModelPackageAs()
    {
        EnsureFixedPackageFolder();

        string defaultName = StripModelPackageExtension(MakeSafeFileName(currentPackageName));
        if (string.IsNullOrWhiteSpace(defaultName) || defaultName == "未命名2D模型包")
            defaultName = "New2DModelPackage";

        string absolutePath = EditorUtility.SaveFilePanel(
            "别名保存2D模型包",
            GetAbsoluteFixedPackageFolder(),
            defaultName + "." + PackageExtension,
            PackageExtension);

        if (string.IsNullOrEmpty(absolutePath))
            return;

        absolutePath = absolutePath.Replace("\\", "/");

        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        if (!absolutePath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "保存失败",
                "2D模型包必须保存到当前 Unity 工程内。\n\n建议保存到：\n" + FixedPackageFolder,
                "确定");
            return;
        }

        string assetPath = AbsoluteToAssetPath(absolutePath);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("保存失败", "无法将保存路径转换为 Unity AssetPath。", "确定");
            return;
        }

        // 别名保存不再使用 SaveFilePanelInProject，避免 Unity Project 搜索窗口/筛选状态吞掉保存焦点。
        // 如果用户在系统保存窗口里选了工程内其它目录，这里仍然强制归档回模型包目录，保持工作台资产结构干净。
        if (!assetPath.StartsWith(FixedPackageFolder + "/", StringComparison.OrdinalIgnoreCase))
            assetPath = FixedPackageFolder + "/" + Path.GetFileName(assetPath);

        if (!assetPath.EndsWith("." + PackageExtension, StringComparison.OrdinalIgnoreCase))
            assetPath += "." + PackageExtension;

        currentPackageAssetPath = assetPath;
        currentPackageName = StripModelPackageExtension(Path.GetFileName(assetPath));
        SavePackageToAssetPath(assetPath);
    }

    private void SavePackageToAssetPath(string assetPath)
    {
        try
        {
            EnsureFixedPackageFolder();
            string absolutePath = AssetPathToAbsolutePath(assetPath);
            string folder = Path.GetDirectoryName(absolutePath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            SkyPrisonAnimationModelPackage package = BuildPackage();
            package.displayName = currentPackageName;
            string json = JsonUtility.ToJson(package, true);
            File.WriteAllText(absolutePath, json);
            AssetDatabase.Refresh();
            hasUnsavedChanges = false;
            SaveSessionCache(true);
            Debug.Log("动作工作台：已保存2D模型包 → " + assetPath);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("保存失败", ex.Message, "确定");
        }
    }


    private void ExportCurrentActionPack()
    {
        SkyPrisonAnimationActionRow action = state.CurrentAction();
        if (action == null || string.IsNullOrWhiteSpace(action.key))
        {
            EditorUtility.DisplayDialog("导出失败", "当前没有可导出的动作。", "确定");
            return;
        }

        EnsureActionPackFolder();

        string actionName = string.IsNullOrWhiteSpace(action.name) ? action.key : action.name;
        string safeActionName = MakeSafeFileName(actionName);
        string safeActionKey = MakeSafeFileName(action.key);
        string defaultName = string.IsNullOrWhiteSpace(safeActionName)
            ? safeActionKey
            : safeActionKey + "_" + safeActionName;

        string absolutePath = EditorUtility.SaveFilePanel(
            "导出当前动作包",
            GetAbsoluteActionPackFolder(),
            defaultName + "." + ActionPackExtension,
            ActionPackExtension);

        if (string.IsNullOrEmpty(absolutePath))
            return;

        absolutePath = absolutePath.Replace("\\", "/");
        if (!absolutePath.EndsWith("." + ActionPackExtension, StringComparison.OrdinalIgnoreCase))
            absolutePath += "." + ActionPackExtension;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        string assetRoot = projectRoot + "/Assets";
        if (!absolutePath.StartsWith(assetRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "导出行动包",
                "行动包需要保存在当前 Unity 项目的 Assets 文件夹内。\n\n建议位置：\n" + FixedActionPackFolder,
                "确定");
            return;
        }

        string assetPath = "Assets" + absolutePath.Substring(assetRoot.Length);

        try
        {
            SkyPrisonAnimationActionPack pack = BuildCurrentActionPack(action);
            string folder = Path.GetDirectoryName(absolutePath);
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(absolutePath, JsonUtility.ToJson(pack, true));
            AssetDatabase.Refresh();
            Debug.Log("动作工作台：已导出行动包 → " + assetPath);
            EditorUtility.DisplayDialog("导出行动包", "已导出：\n" + assetPath, "确定");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("导出行动包失败", ex.Message, "确定");
        }
    }

    private void ImportActionPack()
    {
        EnsureActionPackFolder();

        string path = EditorUtility.OpenFilePanel("加载行动包", GetAbsoluteActionPackFolder(), "json");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            SkyPrisonAnimationActionPack pack = JsonUtility.FromJson<SkyPrisonAnimationActionPack>(json);
            if (pack == null || pack.action == null || string.IsNullOrWhiteSpace(pack.action.key))
            {
                EditorUtility.DisplayDialog("加载失败", "文件不是有效的行动包。", "确定");
                return;
            }

            ApplyActionPack(pack);
            hasUnsavedChanges = true;
            SaveSessionCache(true);
            RepaintOwnerWindow();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("加载行动包失败", ex.Message, "确定");
        }
    }

    private void ApplyHumanTemplateActionPacksFromMenu()
    {
        if (!IsHumanLikeTemplateKey(state.CurrentRigTemplateKey))
        {
            EditorUtility.DisplayDialog("人类模板动作", "当前模型不是 Human / Zombie 人形模板，不能自动套用人类动作包。", "确定");
            return;
        }

        int count = ApplyHumanTemplateActionPacksToCurrentWorkbench(true);
        if (count <= 0)
        {
            EditorUtility.DisplayDialog(
                "人类模板动作",
                "没有找到人类模板动作包。\n\n请把动作包放到：\n" + HumanTemplateActionPackFolder + "\n\n建议文件名：\nHuman_Move_01.skyaction.json\nHuman_Wink_01.skyaction.json",
                "确定");
            return;
        }

        hasUnsavedChanges = true;
        SaveSessionCache(true);
        RepaintOwnerWindow();
        EditorUtility.DisplayDialog("人类模板动作", "已重新套用 " + count + " 个人类模板动作包。", "确定");
    }

    private int ApplyHumanTemplateActionPacksToCurrentWorkbench(bool showWarnings)
    {
        if (!IsHumanLikeTemplateKey(state.CurrentRigTemplateKey))
            return 0;

        EnsureHumanTemplateActionPackFolder();
        string absoluteFolder = AssetPathToAbsolutePath(HumanTemplateActionPackFolder);
        if (!Directory.Exists(absoluteFolder))
            return 0;

        List<string> paths = new List<string>();
        AddHumanTemplateActionPathIfExists(paths, absoluteFolder, "Human_Move_01.skyaction.json");
        AddHumanTemplateActionPathIfExists(paths, absoluteFolder, "Human_Wink_01.skyaction.json");

        if (paths.Count == 0)
        {
            string[] all = Directory.GetFiles(absoluteFolder, "*.skyaction.json", SearchOption.TopDirectoryOnly);
            Array.Sort(all, StringComparer.OrdinalIgnoreCase);
            paths.AddRange(all);
        }

        int applied = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            try
            {
                string json = File.ReadAllText(paths[i]);
                SkyPrisonAnimationActionPack pack = JsonUtility.FromJson<SkyPrisonAnimationActionPack>(json);
                if (pack == null || pack.action == null || string.IsNullOrWhiteSpace(pack.action.key))
                    continue;

                ApplyActionPack(pack, true, true, false);
                applied++;
            }
            catch (Exception ex)
            {
                if (showWarnings)
                    Debug.LogWarning("动作工作台：读取人类模板动作包失败：" + paths[i] + "\n" + ex.Message);
            }
        }

        if (applied > 0)
        {
            NormalizeImportedTemplateMeshDeformerHierarchy();
            EnsureHumanTemplateFaceAliasControllers();
            RebindHumanTemplateEyeWinkKeyframes();
            RebindHumanTemplateLashWinkKeyframes();
            RebindHumanTemplateBrowWinkKeyframes();
            BindHumanTemplateEyeInnerMasks();
            state.SelectedAction = Mathf.Clamp(FindActionIndexByKey("Idle"), 0, Mathf.Max(0, state.Actions.Count - 1));
            SkyPrisonAnimationActionRow action = state.CurrentAction();
            state.TimelineDurationSeconds = Mathf.Max(0.01f, action != null && action.duration > 0f ? action.duration : state.TimelineDurationSeconds);
            state.TimelineDuration = state.TimelineDurationSeconds;
            state.CurrentTime = 0f;
            hasUnsavedChanges = true;
        }

        return applied;
    }

    private void AddHumanTemplateActionPathIfExists(List<string> paths, string absoluteFolder, string fileName)
    {
        if (paths == null || string.IsNullOrEmpty(absoluteFolder) || string.IsNullOrEmpty(fileName))
            return;
        string p = Path.Combine(absoluteFolder, fileName).Replace("\\", "/");
        if (File.Exists(p)) paths.Add(p);
    }

    private void EnsureHumanTemplateActionPackFolder()
    {
        EnsureActionPackFolder();
        EnsureFolder(HumanTemplateActionPackFolder);
    }

    private bool IsHumanLikeTemplateKey(string templateKey)
    {
        templateKey = NormalizeTemplateKey(templateKey);
        return string.Equals(templateKey, "Human", StringComparison.OrdinalIgnoreCase)
            || string.Equals(templateKey, "Zombie", StringComparison.OrdinalIgnoreCase);
    }

    private SkyPrisonAnimationActionPack BuildCurrentActionPack(SkyPrisonAnimationActionRow action)
    {
        SkyPrisonAnimationActionPack pack = new SkyPrisonAnimationActionPack();
        pack.version = "0.1";
        pack.displayName = string.IsNullOrWhiteSpace(action.name) ? action.key : action.name;
        pack.sourcePackageName = currentPackageName;
        pack.sourceRigTemplateKey = state.CurrentRigTemplateKey;
        pack.sourcePsdAssetPath = state.SourcePsdAssetPath;
        pack.timelineFrameRate = state.TimelineFrameRate;
        pack.durationSeconds = Mathf.Max(0.01f, action.duration > 0f ? action.duration : state.TimelineDurationSeconds);
        pack.action = CloneActionRow(action);
        pack.timelineKeyframes = new List<SkyPrisonAnimationTimelineKeyframe>();
        pack.layerOrderKeyframes = new List<SkyPrisonAnimationLayerOrderKeyframe>();
        pack.motionKeyframes = new List<SkyPrisonAnimationMotionKeyframe>();

        for (int i = 0; i < state.TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe key = state.TimelineKeyframes[i];
            if (key != null && key.actionKey == action.key)
                pack.timelineKeyframes.Add(key.Clone());
        }

        for (int i = 0; i < state.LayerOrderKeyframes.Count; i++)
        {
            SkyPrisonAnimationLayerOrderKeyframe key = state.LayerOrderKeyframes[i];
            if (key != null && key.actionKey == action.key)
            {
                pack.layerOrderKeyframes.Add(new SkyPrisonAnimationLayerOrderKeyframe
                {
                    actionKey = key.actionKey,
                    layerKey = key.layerKey,
                    time = key.time,
                    orderWeight = key.orderWeight
                });
            }
        }

        if (state.MotionKeyframes != null)
        {
            for (int i = 0; i < state.MotionKeyframes.Count; i++)
            {
                SkyPrisonAnimationMotionKeyframe key = state.MotionKeyframes[i];
                if (key != null && key.actionKey == action.key)
                    pack.motionKeyframes.Add(key.Clone());
            }
        }

        return pack;
    }

    private void ApplyActionPack(SkyPrisonAnimationActionPack pack)
    {
        ApplyActionPack(pack, false, true, true);
    }

    private void ApplyActionPack(SkyPrisonAnimationActionPack pack, bool replaceSameActionKey, bool retargetToCurrentStructure, bool selectImportedAction)
    {
        if (pack == null || pack.action == null)
            return;

        SkyPrisonAnimationActionRow incoming = CloneActionRow(pack.action);

        string sourceActionKey = string.IsNullOrWhiteSpace(incoming.key) ? "Action" : incoming.key;
        string targetActionKey = sourceActionKey;
        int importedIndex = -1;

        if (replaceSameActionKey)
        {
            importedIndex = FindActionIndexByKey(sourceActionKey);
            if (importedIndex >= 0)
            {
                RemoveActionTimelineData(sourceActionKey);
                incoming.key = sourceActionKey;
                if (string.IsNullOrWhiteSpace(incoming.name)) incoming.name = sourceActionKey;
                if (incoming.duration <= 0f) incoming.duration = Mathf.Max(0.01f, pack.durationSeconds);
                state.Actions[importedIndex] = incoming;
            }
        }

        if (importedIndex < 0)
        {
            // 行动包加载默认追加；模板套用时只有不存在的动作才新增。
            targetActionKey = replaceSameActionKey ? sourceActionKey : GenerateUniqueActionKey(sourceActionKey);
            incoming.key = targetActionKey;
            if (string.IsNullOrWhiteSpace(incoming.name)) incoming.name = targetActionKey;
            if (incoming.duration <= 0f) incoming.duration = Mathf.Max(0.01f, pack.durationSeconds);
            state.Actions.Add(incoming);
            importedIndex = state.Actions.Count - 1;
        }
        else
        {
            targetActionKey = sourceActionKey;
        }

        Dictionary<string, string> meshDeformerRetargetMap = new Dictionary<string, string>();
        int skippedMeshKeys = 0;

        if (pack.timelineKeyframes != null)
        {
            for (int i = 0; i < pack.timelineKeyframes.Count; i++)
            {
                SkyPrisonAnimationTimelineKeyframe key = pack.timelineKeyframes[i];
                if (key == null)
                    continue;

                SkyPrisonAnimationTimelineKeyframe copy = key.Clone();
                copy.actionKey = targetActionKey;

                if (retargetToCurrentStructure && !RetargetImportedTimelineKeyframe(copy, meshDeformerRetargetMap))
                {
                    skippedMeshKeys++;
                    continue;
                }

                state.TimelineKeyframes.Add(copy);
            }
        }

        if (pack.layerOrderKeyframes != null)
        {
            for (int i = 0; i < pack.layerOrderKeyframes.Count; i++)
            {
                SkyPrisonAnimationLayerOrderKeyframe key = pack.layerOrderKeyframes[i];
                if (key == null)
                    continue;

                string layerKey = retargetToCurrentStructure ? RetargetImportedLayerKey(key.layerKey) : key.layerKey;
                if (string.IsNullOrEmpty(layerKey))
                    continue;

                state.LayerOrderKeyframes.Add(new SkyPrisonAnimationLayerOrderKeyframe
                {
                    actionKey = targetActionKey,
                    layerKey = layerKey,
                    time = key.time,
                    orderWeight = key.orderWeight
                });
            }
        }

        if (pack.motionKeyframes != null)
        {
            for (int i = 0; i < pack.motionKeyframes.Count; i++)
            {
                SkyPrisonAnimationMotionKeyframe key = pack.motionKeyframes[i];
                if (key == null)
                    continue;

                SkyPrisonAnimationMotionKeyframe copy = key.Clone();
                copy.actionKey = targetActionKey;
                state.MotionKeyframes.Add(copy);
            }
            state.SortMotionKeyframes();
        }

        if (selectImportedAction)
            state.SelectedAction = Mathf.Clamp(importedIndex, 0, state.Actions.Count - 1);

        if (selectImportedAction)
        {
            state.TimelineDurationSeconds = Mathf.Max(0.01f, incoming.duration > 0f ? incoming.duration : pack.durationSeconds);
            state.TimelineDuration = state.TimelineDurationSeconds;
        }

        state.TimelineFrameRate = Mathf.Max(1, pack.timelineFrameRate > 0 ? pack.timelineFrameRate : state.TimelineFrameRate);
        state.CurrentTime = 0f;
        state.SelectedTimelineKeyframeIndex = -1;
        state.ActiveTimelineTrackKey = string.Empty;
        state.ClearMotionPoseEditorState(true);
        state.InvalidateManualAngleRigSignature();
        state.EnsureMotionPoseEditorStateMatchesCurrentRig();
        state.ClearStructureUndo();
        state.ClearRigUndo();

        if (skippedMeshKeys > 0)
            Debug.LogWarning("动作工作台：行动包中有 " + skippedMeshKeys + " 个曲面变形关键帧没有找到可重映射的 PSB 图层，已跳过。请检查图层命名是否包含眼睛/眼白/眉毛/睫毛/头发等语义。 ");
    }

    private int FindActionIndexByKey(string key)
    {
        if (string.IsNullOrEmpty(key) || state == null || state.Actions == null)
            return -1;
        for (int i = 0; i < state.Actions.Count; i++)
        {
            SkyPrisonAnimationActionRow row = state.Actions[i];
            if (row != null && string.Equals(row.key, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private bool RetargetImportedTimelineKeyframe(SkyPrisonAnimationTimelineKeyframe key, Dictionary<string, string> meshDeformerRetargetMap)
    {
        if (key == null)
            return false;

        bool meshKey = key.useMeshDeform || string.Equals(key.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase);
        if (meshKey)
        {
            string oldKey = key.targetKey ?? string.Empty;
            string mapped;
            if (meshDeformerRetargetMap != null && !string.IsNullOrEmpty(oldKey) && meshDeformerRetargetMap.TryGetValue(oldKey, out mapped))
            {
                key.targetKey = mapped;
                key.layerWeightTargetKey = mapped;
                SkyPrisonAnimationRigRow existingMapped = state.FindRigRow(mapped);
                if (existingMapped != null) key.targetName = existingMapped.name;
                key.targetKind = "MeshDeformer";
                key.useMeshDeform = true;
                return true;
            }

            SkyPrisonAnimationRigRow deformer = EnsureTemplateMeshDeformerForImportedKeyframe(key);
            if (deformer == null)
                return false;

            key.targetKey = deformer.key;
            key.targetName = deformer.name;
            key.targetKind = "MeshDeformer";
            key.layerWeightTargetKey = deformer.key;
            key.useMeshDeform = true;
            if (meshDeformerRetargetMap != null && !string.IsNullOrEmpty(oldKey))
                meshDeformerRetargetMap[oldKey] = deformer.key;
            return true;
        }

        if (!string.IsNullOrEmpty(key.targetKey))
        {
            if (state.FindRigRow(key.targetKey) != null || state.FindPsbRow(key.targetKey) != null)
                return true;

            string remapped = RetargetImportedLayerKey(key.targetKey);
            if (!string.IsNullOrEmpty(remapped))
            {
                key.targetKey = remapped;
                if (string.IsNullOrEmpty(key.layerWeightTargetKey)) key.layerWeightTargetKey = remapped;
                SkyPrisonAnimationRigRow row = state.FindAnyStructureRow(remapped);
                if (row != null) key.targetName = row.name;
                return true;
            }
        }

        return true;
    }

    private string RetargetImportedLayerKey(string sourceKey)
    {
        if (string.IsNullOrEmpty(sourceKey))
            return string.Empty;
        if (state.FindAnyStructureRow(sourceKey) != null)
            return sourceKey;

        SkyPrisonAnimationRigRow best = FindBestPsbLayerByTemplateName(sourceKey, sourceKey);
        return best != null ? best.key : string.Empty;
    }

    private SkyPrisonAnimationRigRow EnsureTemplateMeshDeformerForImportedKeyframe(SkyPrisonAnimationTimelineKeyframe key)
    {
        if (key == null)
            return null;

        string desiredDeformerName = MakeTemplateMeshDeformerName(key.targetName, key.targetKey);
        string desiredControllerName = MakeTemplateControllerNodeName(key.targetName, key.targetKey);

        SkyPrisonAnimationRigRow targetPsb = FindBestPsbLayerByTemplateName(key.targetName, key.targetKey);
        if (targetPsb == null)
            return null;

        SkyPrisonAnimationRigRow controller = EnsureTemplateNoBoneControllerForPsb(targetPsb, desiredControllerName, key.targetName, key.targetKey);
        if (controller == null)
            return null;

        SkyPrisonAnimationRigRow existing = state.FindRigRow(key.targetKey);
        if (existing != null && existing.isMeshDeformer)
        {
            NormalizeTemplateMeshDeformerParent(existing, controller, desiredDeformerName, key);
            return existing;
        }

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer) continue;
            if (string.Equals(row.name, desiredDeformerName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(key.targetKey) && string.Equals(row.key, key.targetKey, StringComparison.OrdinalIgnoreCase)))
            {
                NormalizeTemplateMeshDeformerParent(row, controller, desiredDeformerName, key);
                return row;
            }
        }

        string baseKey = MakeTemplateMeshDeformerKeyBase(controller.key, targetPsb.key);
        string deformerKey = GenerateUniqueRigRowKey(baseKey);

        // 失败更新留下的旧节点可能同名但挂在错误父节点/错误 PSB 上。
        // 显式绑定时必须把它抢回来，而不是另建一个正确节点让旧红框继续显示。
        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer)
                continue;
            if (!string.Equals(row.name, desiredDeformerName, StringComparison.OrdinalIgnoreCase))
                continue;

            row.meshDeformTargetKey = targetPsb.key;
            row.semantic = "MeshDeformer";
            row.meshDeformColumns = Mathf.Clamp(row.meshDeformColumns > 0 ? row.meshDeformColumns : (key.meshDeformColumns > 0 ? key.meshDeformColumns : 3), 2, 16);
            row.meshDeformRows = Mathf.Clamp(row.meshDeformRows > 0 ? row.meshDeformRows : (key.meshDeformRows > 0 ? key.meshDeformRows : 3), 2, 16);
            EnsureMeshDeformerPointGridForRow(row);
            NormalizeTemplateMeshDeformerParent(row, controller, desiredDeformerName, key);
            return row;
        }

        SkyPrisonAnimationRigRow deformer = new SkyPrisonAnimationRigRow
        {
            key = deformerKey,
            name = desiredDeformerName,
            semantic = "MeshDeformer",
            depth = controller.depth + 1,
            parentKey = controller.key,
            isFolder = false,
            expanded = true,
            visible = true,
            mapped = true,
            hasKey = false,
            previewIconNumber = 47,
            previewColor = new Color(0.62f, 0.82f, 1f, 1f),
            isMeshDeformer = true,
            meshDeformTargetKey = targetPsb.key,
            meshDeformColumns = Mathf.Clamp(key.meshDeformColumns > 0 ? key.meshDeformColumns : 3, 2, 16),
            meshDeformRows = Mathf.Clamp(key.meshDeformRows > 0 ? key.meshDeformRows : 3, 2, 16),
            meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(key.meshDeformPoints)
        };

        EnsureMeshDeformerPointGridForRow(deformer);

        int insertIndex = FindRigInsertIndexForTemplateChild(controller);
        state.RigRows.Insert(insertIndex, deformer);
        controller.expanded = true;
        return deformer;
    }

    private SkyPrisonAnimationRigRow EnsureTemplateNoBoneControllerForPsb(SkyPrisonAnimationRigRow targetPsb, string desiredName, string templateTargetName, string templateTargetKey)
    {
        if (targetPsb == null)
            return null;

        SkyPrisonAnimationRigRow head = FindTemplateHeadParentRig();
        int depth = head != null ? head.depth + 1 : 0;
        string parentKey = head != null ? head.key : string.Empty;
        string desiredKeyBase = MakeTemplateControllerKeyBase(templateTargetName, templateTargetKey, targetPsb);

        SkyPrisonAnimationRigRow existing = FindExistingTemplateControllerNode(desiredName, desiredKeyBase, targetPsb);
        if (existing != null)
        {
            existing.isMeshDeformer = false;
            existing.parentKey = parentKey;
            existing.depth = depth;
            existing.hasKey = false;
            existing.isFolder = false;
            existing.expanded = true;
            existing.visible = true;
            existing.mapped = true;
            existing.semantic = MakeTemplateControllerSemantic(templateTargetName, templateTargetKey);
            existing.previewIconNumber = GetTemplateControllerIconNumber(existing.semantic);
            existing.previewColor = targetPsb.previewColor;
            BindTemplatePsbToController(targetPsb, existing);
            MoveRigRowUnderParent(existing, head);
            return existing;
        }

        SkyPrisonAnimationRigRow controller = new SkyPrisonAnimationRigRow
        {
            key = GenerateUniqueRigRowKey(desiredKeyBase),
            name = desiredName,
            semantic = MakeTemplateControllerSemantic(templateTargetName, templateTargetKey),
            depth = depth,
            parentKey = parentKey,
            isFolder = false,
            expanded = true,
            visible = true,
            mapped = true,
            hasKey = false,
            previewIconNumber = GetTemplateControllerIconNumber(MakeTemplateControllerSemantic(templateTargetName, templateTargetKey)),
            previewColor = targetPsb.previewColor,
            isMeshDeformer = false,
            meshDeformTargetKey = string.Empty,
            useCustomBoneLine = false,
            useManualBoneRootOffset = false,
            useManualBoneHeadOffset = false
        };

        controller.sourceAssetPath = targetPsb.sourceAssetPath;
        controller.sourceSpriteName = targetPsb.sourceSpriteName;
        controller.sourceLayerPath = targetPsb.sourceLayerPath;
        controller.psbLayerWeight = targetPsb.psbLayerWeight;
        controller.usePsbLayerWeight = targetPsb.usePsbLayerWeight;
        BindTemplatePsbToController(targetPsb, controller);

        int insertIndex = FindRigInsertIndexForTemplateChild(head);
        state.RigRows.Insert(insertIndex, controller);
        if (head != null) head.expanded = true;
        return controller;
    }

    private void NormalizeTemplateMeshDeformerParent(SkyPrisonAnimationRigRow deformer, SkyPrisonAnimationRigRow controller, string desiredName, SkyPrisonAnimationTimelineKeyframe key)
    {
        if (deformer == null || controller == null)
            return;

        deformer.name = desiredName;
        deformer.semantic = "MeshDeformer";
        deformer.parentKey = controller.key;
        deformer.depth = controller.depth + 1;
        deformer.isFolder = false;
        deformer.expanded = true;
        deformer.visible = true;
        deformer.mapped = true;
        deformer.hasKey = false;
        deformer.previewIconNumber = 47;
        deformer.previewColor = new Color(0.62f, 0.82f, 1f, 1f);
        deformer.isMeshDeformer = true;

        // 子曲面节点的位置完全取决于 meshDeformTargetKey。
        // 之前脏数据的问题正是这里“非空就不改”，导致眉毛曲面继续贴在眼睛/头发/反侧图层上。
        // 现在优先以父控制器绑定的 PSB 为准，保证曲面红框回到它真正控制的图层位置。
        SkyPrisonAnimationRigRow controllerTargetPsb = !string.IsNullOrEmpty(controller.boundRigKey) ? state.FindPsbRow(controller.boundRigKey) : null;
        if (controllerTargetPsb != null)
        {
            deformer.meshDeformTargetKey = controllerTargetPsb.key;
        }
        else if (string.IsNullOrEmpty(deformer.meshDeformTargetKey) || !TemplatePsbMatchesSourceSlot(desiredName, deformer.key, state.FindPsbRow(deformer.meshDeformTargetKey)))
        {
            SkyPrisonAnimationRigRow psb = FindBestPsbLayerByTemplateName(key != null ? key.targetName : desiredName, key != null ? key.targetKey : desiredName);
            if (psb != null) deformer.meshDeformTargetKey = psb.key;
        }
        deformer.meshDeformColumns = Mathf.Clamp(key != null && key.meshDeformColumns > 0 ? key.meshDeformColumns : deformer.meshDeformColumns, 2, 16);
        deformer.meshDeformRows = Mathf.Clamp(key != null && key.meshDeformRows > 0 ? key.meshDeformRows : deformer.meshDeformRows, 2, 16);
        if (key != null && key.meshDeformPoints != null && key.meshDeformPoints.Count > 0)
            deformer.meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(key.meshDeformPoints);
        EnsureMeshDeformerPointGridForRow(deformer);
        MoveRigRowUnderParent(deformer, controller);
        controller.expanded = true;
    }


    private void NormalizeImportedTemplateMeshDeformerHierarchy()
    {
        if (state == null || state.RigRows == null)
            return;

        List<SkyPrisonAnimationRigRow> meshRows = new List<SkyPrisonAnimationRigRow>();
        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row != null && row.isMeshDeformer && IsTemplateFaceHairMeshDeformerRow(row))
                meshRows.Add(row);
        }

        for (int i = 0; i < meshRows.Count; i++)
        {
            SkyPrisonAnimationRigRow deformer = meshRows[i];
            if (deformer == null || !state.RigRows.Contains(deformer))
                continue;

            SkyPrisonAnimationRigRow targetPsb = null;
            if (!string.IsNullOrEmpty(deformer.meshDeformTargetKey))
                targetPsb = state.FindPsbRow(deformer.meshDeformTargetKey);

            // 失败更新留下的脏数据里，meshDeformTargetKey 可能已经错绑到头发或反向眼睛。
            // 清理时不能盲信旧 targetKey，必须用当前 deformer 的名字重新校验左右和部位。
            if (targetPsb != null && !TemplatePsbMatchesSourceSlot(deformer.name, deformer.key, targetPsb))
                targetPsb = null;

            if (targetPsb == null)
                targetPsb = FindBestPsbLayerByTemplateName(deformer.name, deformer.key);
            if (targetPsb == null)
                continue;

            string controllerName = MakeTemplateControllerNodeName(deformer.name, deformer.key);
            SkyPrisonAnimationRigRow controller = EnsureTemplateNoBoneControllerForPsb(targetPsb, controllerName, deformer.name, deformer.key);
            if (controller == null)
                continue;

            SkyPrisonAnimationTimelineKeyframe surrogate = new SkyPrisonAnimationTimelineKeyframe
            {
                targetName = deformer.name,
                targetKey = deformer.key,
                targetKind = "MeshDeformer",
                useMeshDeform = true,
                meshDeformColumns = deformer.meshDeformColumns > 0 ? deformer.meshDeformColumns : 3,
                meshDeformRows = deformer.meshDeformRows > 0 ? deformer.meshDeformRows : 3,
                meshDeformPoints = SkyPrisonAnimationTimelineKeyframe.CloneMeshDeformPoints(deformer.meshDeformPoints)
            };

            NormalizeTemplateMeshDeformerParent(deformer, controller, MakeTemplateMeshDeformerName(deformer.name, deformer.key), surrogate);
        }

        MergeDuplicateTemplateMeshDeformers();
        EnsureHumanTemplateFaceAliasControllers();
        RebindHumanTemplateEyeWinkKeyframes();
        RebindHumanTemplateLashWinkKeyframes();
        RebindHumanTemplateBrowWinkKeyframes();
        BindHumanTemplateEyeInnerMasks();
    }

    private void EnsureHumanTemplateFaceAliasControllers()
    {
        if (state == null || state.PsbRows == null || state.RigRows == null)
            return;

        // 当前 Axia 这套 PSB 约定：brow_L / brow_R 是眉毛，eye_L1 / eye_R1 才是睫毛。
        // 睫毛需要独立生成「睫毛L/R」无骨骼节点和 3x3 曲面变形，并且绑定到 eye_L1 / eye_R1。
        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow psb = state.PsbRows[i];
            if (psb == null || psb.isFolder || string.IsNullOrEmpty(psb.key))
                continue;

            string text = NormalizeTemplateMatchText((psb.name ?? string.Empty) + " " + (psb.key ?? string.Empty) + " " + (psb.sourceSpriteName ?? string.Empty) + " " + (psb.sourceLayerPath ?? string.Empty) + " " + (psb.semantic ?? string.Empty));
            int side = DetectTemplateTargetSide(text);
            if (side == 0)
                continue;

            bool explicitLash = ContainsAnyTemplate(text, "eyelash", "eye_lash", "lash", "睫毛", "睫", "まつげ");
            bool eyeOneLash = IsExactEyeOneSideLayer(text, side);
            if (!explicitLash && !eyeOneLash)
                continue;

            string controllerName = side < 0 ? "睫毛L" : "睫毛R";
            string sourceName = controllerName + "_曲面变形";
            SkyPrisonAnimationRigRow controller = EnsureTemplateNoBoneControllerForPsb(psb, controllerName, sourceName, psb.key);
            if (controller == null)
                continue;

            EnsureTemplateMeshDeformerForExplicitPsb(psb, controller, controllerName + "_曲面变形", 3, 3);
        }
    }



    private void RebindHumanTemplateEyeWinkKeyframes()
    {
        if (state == null || state.PsbRows == null || state.RigRows == null || state.TimelineKeyframes == null)
            return;

        RebindHumanTemplateEyeWinkKeyframesForSide(-1);
        RebindHumanTemplateEyeWinkKeyframesForSide(1);
    }

    private void RebindHumanTemplateEyeWinkKeyframesForSide(int side)
    {
        if (side == 0)
            return;

        // 当前 Axia 眼部分层约定：
        // eye_L1 / eye_R1 = 睫毛，eye_L2 / eye_R2 = 瞳孔/眼黑，eye_L3 / eye_R3 = 眼白/蒙版形状。
        // 眨眼里的「眼睛L/R_曲面变形」应该压缩眼白/蒙版形状，所以必须绑定到 3 号眼层。
        SkyPrisonAnimationRigRow eyePsb = FindTemplateEyeNumberPsbLayer(side, 3);
        if (eyePsb == null)
            eyePsb = FindHumanTemplateEyePsbLayer(side);
        if (eyePsb == null)
            return;

        string controllerName = side < 0 ? "眼睛L" : "眼睛R";
        string deformerName = controllerName + "_曲面变形";
        SkyPrisonAnimationRigRow controller = EnsureTemplateNoBoneControllerForPsb(eyePsb, controllerName, controllerName, eyePsb.key);
        if (controller == null)
            return;

        SkyPrisonAnimationRigRow deformer = EnsureTemplateMeshDeformerForExplicitPsb(eyePsb, controller, deformerName, 3, 3);
        if (deformer == null)
            return;

        for (int i = 0; i < state.TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
            if (k == null)
                continue;

            bool meshKey = k.useMeshDeform || string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase);
            if (!meshKey)
                continue;

            if (!IsHumanTemplateEyeTimelineKeyForSide(k, side))
                continue;

            k.targetKey = deformer.key;
            k.targetName = deformer.name;
            k.targetKind = "MeshDeformer";
            k.layerWeightTargetKey = deformer.key;
            k.useMeshDeform = true;
            k.meshDeformColumns = Mathf.Clamp(k.meshDeformColumns > 0 ? k.meshDeformColumns : 3, 2, 16);
            k.meshDeformRows = Mathf.Clamp(k.meshDeformRows > 0 ? k.meshDeformRows : 3, 2, 16);
        }
    }

    private SkyPrisonAnimationRigRow FindHumanTemplateEyePsbLayer(int side)
    {
        if (state == null || state.PsbRows == null || side == 0)
            return null;

        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
                continue;

            string text = NormalizeTemplateMatchText((row.name ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.sourceSpriteName ?? string.Empty) + " " + (row.sourceLayerPath ?? string.Empty) + " " + (row.semantic ?? string.Empty));
            int rowSide = DetectTemplateTargetSide(text);
            int score = 0;

            if (rowSide == side) score += 60;
            else if (rowSide == -side) score -= 160;

            if (HasTemplateEyeNumberMarker(text, side, 3)) score += 160;
            if (HasTemplateEyeNumberMarker(text, side, 2)) score -= 40;
            if (IsExactEyeOneSideLayer(text, rowSide == 0 ? side : rowSide)) score -= 220;
            if (ContainsAnyTemplate(text, "eye", "eyes", "眼", "目", "眼睛", "眼白", "sclera", "eye_white", "white_eye")) score += 60;
            if (ContainsAnyTemplate(text, "brow", "眉", "lash", "睫", "hair", "髪", "发")) score -= 160;

            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        return bestScore >= 120 ? best : null;
    }

    private bool IsHumanTemplateEyeTimelineKeyForSide(SkyPrisonAnimationTimelineKeyframe key, int side)
    {
        if (key == null || side == 0)
            return false;

        string text = NormalizeTemplateMatchText((key.targetName ?? string.Empty) + " " + (key.targetKey ?? string.Empty) + " " + (key.layerWeightTargetKey ?? string.Empty));
        int keySide = DetectTemplateTargetSide(text);
        if (keySide != 0 && keySide != side)
            return false;

        if (!ContainsAnyTemplate(text, "眼睛", "眼", "eye", "eyes"))
            return false;

        // 睫毛和眉毛都有独立重绑流程，不能被眼睛流程吞掉。
        if (ContainsAnyTemplate(text, "睫毛", "睫", "eyelash", "eye_lash", "lash", "眉毛", "眉", "eyebrow", "eye_brow", "brow"))
            return false;

        return true;
    }

    private void RebindHumanTemplateBrowWinkKeyframes()
    {
        if (state == null || state.PsbRows == null || state.RigRows == null || state.TimelineKeyframes == null)
            return;

        RebindHumanTemplateBrowWinkKeyframesForSide(-1);
        RebindHumanTemplateBrowWinkKeyframesForSide(1);
    }

    private void RebindHumanTemplateBrowWinkKeyframesForSide(int side)
    {
        if (side == 0)
            return;

        SkyPrisonAnimationRigRow browPsb = FindHumanTemplateBrowPsbLayer(side);

        // 当前角色约定：brow_L / brow_R 是眉毛；eye_L1 / eye_R1 才是睫毛。
        // 如果没有找到真正眉毛层，就不要把眉毛动作强行绑到眼睛或头发上。
        if (browPsb == null)
        {
            RemoveWrongHumanTemplateBrowMeshBindingsForSide(side);
            return;
        }

        string controllerName = side < 0 ? "眉毛L" : "眉毛R";
        string deformerName = controllerName + "_曲面变形";
        SkyPrisonAnimationRigRow controller = EnsureTemplateNoBoneControllerForPsb(browPsb, controllerName, controllerName, browPsb.key);
        if (controller == null)
            return;

        SkyPrisonAnimationRigRow deformer = EnsureTemplateMeshDeformerForExplicitPsb(browPsb, controller, deformerName, 3, 3);
        if (deformer == null)
            return;

        for (int i = 0; i < state.TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
            if (k == null)
                continue;

            bool meshKey = k.useMeshDeform || string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase);
            if (!meshKey)
                continue;

            if (!IsHumanTemplateBrowTimelineKeyForSide(k, side))
                continue;

            k.targetKey = deformer.key;
            k.targetName = deformer.name;
            k.targetKind = "MeshDeformer";
            k.layerWeightTargetKey = deformer.key;
            k.useMeshDeform = true;
            k.meshDeformColumns = Mathf.Clamp(k.meshDeformColumns > 0 ? k.meshDeformColumns : 3, 2, 16);
            k.meshDeformRows = Mathf.Clamp(k.meshDeformRows > 0 ? k.meshDeformRows : 3, 2, 16);
        }
    }

    private void RemoveWrongHumanTemplateBrowMeshBindingsForSide(int side)
    {
        if (state == null || state.RigRows == null || side == 0)
            return;

        string controllerName = side < 0 ? "眉毛L" : "眉毛R";
        for (int i = state.RigRows.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer)
                continue;

            if (!string.Equals(row.name, controllerName + "_曲面变形", StringComparison.OrdinalIgnoreCase))
                continue;

            SkyPrisonAnimationRigRow target = !string.IsNullOrEmpty(row.meshDeformTargetKey) ? state.FindPsbRow(row.meshDeformTargetKey) : null;
            if (target == null)
                continue;

            string targetText = NormalizeTemplateMatchText((target.name ?? string.Empty) + " " + (target.key ?? string.Empty) + " " + (target.sourceSpriteName ?? string.Empty) + " " + (target.sourceLayerPath ?? string.Empty) + " " + (target.semantic ?? string.Empty));
            int targetSide = DetectTemplateTargetSide(targetText);
            bool wrongSide = targetSide != 0 && targetSide != side;
            bool hairOrEye = ContainsAnyTemplate(targetText, "hair", "髪", "发", "eye", "eyes", "眼", "目", "睫", "lash");

            if (wrongSide || hairOrEye)
                state.RigRows.RemoveAt(i);
        }
    }

    private SkyPrisonAnimationRigRow FindHumanTemplateBrowPsbLayer(int side)
    {
        if (state == null || state.PsbRows == null || side == 0)
            return null;

        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
                continue;

            string text = NormalizeTemplateMatchText((row.name ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.sourceSpriteName ?? string.Empty) + " " + (row.sourceLayerPath ?? string.Empty) + " " + (row.semantic ?? string.Empty));
            int rowSide = DetectTemplateTargetSide(text);
            int score = 0;

            if (rowSide == side) score += 60;
            else if (rowSide == -side) score -= 140;

            // 当前文件里 brow_L / brow_R 是眉毛；eye_L1 / eye_R1 才是睫毛。
            if (ContainsAnyTemplate(text, "eyebrow", "eye_brow", "brow", "眉毛", "眉", "mayu", "mayuge")) score += 100;
            if (ContainsAnyTemplate(text, "eyelash", "eye_lash", "lash", "睫毛", "睫", "まつげ")) score -= 160;
            if (IsExactEyeOneSideLayer(text, rowSide == 0 ? side : rowSide)) score -= 180;
            if (ContainsAnyTemplate(text, "eye", "eyes", "眼", "目", "pupil", "iris", "瞳")) score -= 120;
            if (ContainsAnyTemplate(text, "hair", "髪", "发")) score -= 120;

            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        return bestScore >= 100 ? best : null;
    }

    private bool IsHumanTemplateBrowTimelineKeyForSide(SkyPrisonAnimationTimelineKeyframe key, int side)
    {
        if (key == null || side == 0)
            return false;

        string text = NormalizeTemplateMatchText((key.targetName ?? string.Empty) + " " + (key.targetKey ?? string.Empty) + " " + (key.layerWeightTargetKey ?? string.Empty));
        int keySide = DetectTemplateTargetSide(text);
        if (keySide != 0 && keySide != side)
            return false;

        if (!ContainsAnyTemplate(text, "眉毛", "眉", "eyebrow", "eye_brow"))
            return false;

        // 保险：睫毛动作绝对不能被眉毛重绑流程吃掉。
        if (ContainsAnyTemplate(text, "睫毛", "睫", "eyelash", "eye_lash", "lash"))
            return false;

        return true;
    }

    private void RebindHumanTemplateLashWinkKeyframes()
    {
        if (state == null || state.PsbRows == null || state.RigRows == null || state.TimelineKeyframes == null)
            return;

        RebindHumanTemplateLashWinkKeyframesForSide(-1);
        RebindHumanTemplateLashWinkKeyframesForSide(1);
    }

    private void RebindHumanTemplateLashWinkKeyframesForSide(int side)
    {
        if (side == 0)
            return;

        SkyPrisonAnimationRigRow lashPsb = FindHumanTemplateLashPsbLayer(side);
        if (lashPsb == null)
            return;

        string controllerName = side < 0 ? "睫毛L" : "睫毛R";
        string deformerName = controllerName + "_曲面变形";
        SkyPrisonAnimationRigRow controller = EnsureTemplateNoBoneControllerForPsb(lashPsb, controllerName, controllerName, lashPsb.key);
        if (controller == null)
            return;

        SkyPrisonAnimationRigRow deformer = EnsureTemplateMeshDeformerForExplicitPsb(lashPsb, controller, deformerName, 3, 3);
        if (deformer == null)
            return;

        for (int i = 0; i < state.TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
            if (k == null)
                continue;

            bool meshKey = k.useMeshDeform || string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase);
            if (!meshKey)
                continue;

            if (!IsHumanTemplateLashTimelineKeyForSide(k, side))
                continue;

            k.targetKey = deformer.key;
            k.targetName = deformer.name;
            k.targetKind = "MeshDeformer";
            k.layerWeightTargetKey = deformer.key;
            k.useMeshDeform = true;
            k.meshDeformColumns = Mathf.Clamp(k.meshDeformColumns > 0 ? k.meshDeformColumns : 3, 2, 16);
            k.meshDeformRows = Mathf.Clamp(k.meshDeformRows > 0 ? k.meshDeformRows : 3, 2, 16);
        }
    }

    private SkyPrisonAnimationRigRow FindHumanTemplateLashPsbLayer(int side)
    {
        if (state == null || state.PsbRows == null || side == 0)
            return null;

        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
                continue;

            string text = NormalizeTemplateMatchText((row.name ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.sourceSpriteName ?? string.Empty) + " " + (row.sourceLayerPath ?? string.Empty) + " " + (row.semantic ?? string.Empty));
            int rowSide = DetectTemplateTargetSide(text);
            int score = 0;

            if (rowSide == side) score += 60;
            else if (rowSide == -side) score -= 120;

            if (IsExactEyeOneSideLayer(text, rowSide == 0 ? side : rowSide)) score += 140;
            if (ContainsAnyTemplate(text, "eyelash", "eye_lash", "lash", "睫毛", "睫", "まつげ")) score += 80;
            if (ContainsAnyTemplate(text, "eyebrow", "eye_brow", "brow", "眉毛", "眉")) score -= 90;
            if (ContainsAnyTemplate(text, "eye", "eyes", "眼", "目") && !IsExactEyeOneSideLayer(text, rowSide == 0 ? side : rowSide) && !ContainsAnyTemplate(text, "lash", "睫")) score -= 70;
            if (ContainsAnyTemplate(text, "hair", "髪", "发")) score -= 80;

            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        return bestScore >= 80 ? best : null;
    }

    private bool IsHumanTemplateLashTimelineKeyForSide(SkyPrisonAnimationTimelineKeyframe key, int side)
    {
        if (key == null || side == 0)
            return false;

        string text = NormalizeTemplateMatchText((key.targetName ?? string.Empty) + " " + (key.targetKey ?? string.Empty) + " " + (key.layerWeightTargetKey ?? string.Empty));
        int keySide = DetectTemplateTargetSide(text);
        if (keySide != 0 && keySide != side)
            return false;

        if (ContainsAnyTemplate(text, "睫毛", "睫", "eyelash", "eye_lash", "lash"))
            return true;

        return false;
    }

    private bool IsExactBrowSideAlias(string normalizedText, int side)
    {
        if (string.IsNullOrEmpty(normalizedText) || side == 0)
            return false;

        string sideLower = side < 0 ? "l" : "r";
        string sideWord = side < 0 ? "left" : "right";
        return ContainsAnyTemplate(
            normalizedText,
            "brow_" + sideLower,
            "brow-" + sideLower,
            "brow " + sideLower,
            "brow" + sideLower,
            "brow_" + sideWord,
            "brow-" + sideWord,
            "brow " + sideWord);
    }


    private bool IsExactEyeOneSideLayer(string normalizedText, int side)
    {
        if (string.IsNullOrEmpty(normalizedText) || side == 0)
            return false;

        string n = NormalizeTemplateMatchText(normalizedText);
        string sideLower = side < 0 ? "l" : "r";
        string sideWord = side < 0 ? "left" : "right";
        return ContainsAnyTemplate(
            n,
            "eye_" + sideLower + "1",
            "eye-" + sideLower + "1",
            "eye " + sideLower + "1",
            "eye" + sideLower + "1",
            "eye_" + sideLower + "_1",
            "eye-" + sideLower + "-1",
            "eye " + sideLower + " 1",
            "eye_" + sideWord + "1",
            "eye-" + sideWord + "1",
            "eye " + sideWord + "1",
            "eye" + sideWord + "1",
            "eye_" + sideWord + "_1",
            "eye-" + sideWord + "-1",
            "eye " + sideWord + " 1");
    }

    private bool IsAnyExactEyeOneSideLayer(string normalizedText)
    {
        return IsExactEyeOneSideLayer(normalizedText, -1) || IsExactEyeOneSideLayer(normalizedText, 1);
    }

    private SkyPrisonAnimationRigRow EnsureTemplateMeshDeformerForExplicitPsb(SkyPrisonAnimationRigRow targetPsb, SkyPrisonAnimationRigRow controller, string desiredName, int columns, int rows)
    {
        if (targetPsb == null || controller == null)
            return null;

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer)
                continue;
            if (string.Equals(row.parentKey, controller.key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.meshDeformTargetKey, targetPsb.key, StringComparison.OrdinalIgnoreCase))
            {
                row.name = desiredName;
                row.semantic = "MeshDeformer";
                row.meshDeformColumns = Mathf.Clamp(row.meshDeformColumns > 0 ? row.meshDeformColumns : columns, 2, 16);
                row.meshDeformRows = Mathf.Clamp(row.meshDeformRows > 0 ? row.meshDeformRows : rows, 2, 16);
                EnsureMeshDeformerPointGridForRow(row);
                NormalizeTemplateMeshDeformerParent(row, controller, desiredName, null);
                return row;
            }
        }

        // 失败更新留下的旧节点可能同名但挂在错误父节点/错误 PSB 上。
        // 显式绑定时必须把它抢回来，而不是另建一个正确节点让旧红框继续显示。
        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer)
                continue;
            if (!string.Equals(row.name, desiredName, StringComparison.OrdinalIgnoreCase))
                continue;

            row.meshDeformTargetKey = targetPsb.key;
            row.semantic = "MeshDeformer";
            row.meshDeformColumns = Mathf.Clamp(row.meshDeformColumns > 0 ? row.meshDeformColumns : columns, 2, 16);
            row.meshDeformRows = Mathf.Clamp(row.meshDeformRows > 0 ? row.meshDeformRows : rows, 2, 16);
            EnsureMeshDeformerPointGridForRow(row);
            NormalizeTemplateMeshDeformerParent(row, controller, desiredName, null);
            return row;
        }

        SkyPrisonAnimationRigRow deformer = new SkyPrisonAnimationRigRow
        {
            key = GenerateUniqueRigRowKey(MakeTemplateMeshDeformerKeyBase(controller.key, targetPsb.key)),
            name = desiredName,
            semantic = "MeshDeformer",
            depth = controller.depth + 1,
            parentKey = controller.key,
            isFolder = false,
            expanded = true,
            visible = true,
            mapped = true,
            hasKey = false,
            previewIconNumber = 47,
            previewColor = new Color(0.62f, 0.82f, 1f, 1f),
            isMeshDeformer = true,
            meshDeformTargetKey = targetPsb.key,
            meshDeformColumns = Mathf.Clamp(columns, 2, 16),
            meshDeformRows = Mathf.Clamp(rows, 2, 16),
            meshDeformPoints = new List<SkyPrisonMeshDeformPoint>()
        };

        EnsureMeshDeformerPointGridForRow(deformer);
        int insertIndex = FindRigInsertIndexForTemplateChild(controller);
        state.RigRows.Insert(insertIndex, deformer);
        controller.expanded = true;
        return deformer;
    }

    private void BindHumanTemplateEyeInnerMasks()
    {
        // 让瞳孔永远被限制在眼白里：R2 -> mask R3，L2 -> mask L3。
        BindHumanTemplateEyeMaskPair(-1, 2, 3);
        BindHumanTemplateEyeMaskPair(1, 2, 3);
    }

    private void BindHumanTemplateEyeMaskPair(int side, int subjectNumber, int maskNumber)
    {
        SkyPrisonAnimationRigRow subject = FindTemplateEyeNumberPsbLayer(side, subjectNumber);
        SkyPrisonAnimationRigRow mask = FindTemplateEyeNumberPsbLayer(side, maskNumber);
        if (subject == null || mask == null || subject == mask)
            return;

        ApplyMaskReferenceToRowAndBoundControllers(subject, mask);
    }

    private SkyPrisonAnimationRigRow FindTemplateEyeNumberPsbLayer(int side, int number)
    {
        if (state == null || state.PsbRows == null)
            return null;

        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key))
                continue;

            string text = NormalizeTemplateMatchText((row.name ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.sourceSpriteName ?? string.Empty) + " " + (row.sourceLayerPath ?? string.Empty) + " " + (row.semantic ?? string.Empty));
            if (!ContainsAnyTemplate(text, "eye", "eyes", "眼", "目"))
                continue;
            if (ContainsAnyTemplate(text, "brow", "眉", "lash", "睫"))
                continue;

            int rowSide = DetectTemplateTargetSide(text);
            int score = 0;
            if (side != 0)
            {
                if (rowSide == side) score += 50;
                else if (rowSide == -side) score -= 100;
            }

            if (HasTemplateEyeNumberMarker(text, side, number)) score += 100;
            if (ContainsAnyTemplate(text, "eye_white", "white_eye", "sclera", "眼白") && number == 3) score += 18;
            if (ContainsAnyTemplate(text, "pupil", "iris", "瞳", "虹彩", "eye_black", "black_eye", "眼黑") && number == 2) score += 18;

            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        return bestScore >= 100 ? best : null;
    }

    private bool HasTemplateEyeNumberMarker(string normalizedText, int side, int number)
    {
        if (string.IsNullOrEmpty(normalizedText))
            return false;

        string sideLower = side < 0 ? "l" : (side > 0 ? "r" : "");
        string sideWord = side < 0 ? "left" : (side > 0 ? "right" : "");
        string num = number.ToString();

        if (!string.IsNullOrEmpty(sideLower))
        {
            if (ContainsAnyTemplate(
                normalizedText,
                "eye_" + sideLower + num,
                "eye_" + sideLower + "_" + num,
                "eye-" + sideLower + num,
                "eye-" + sideLower + "-" + num,
                "eye " + sideLower + num,
                "eye " + sideLower + " " + num,
                "eye" + sideLower + num,
                "eyes_" + sideLower + num,
                "eyes" + sideLower + num,
                "眼睛" + sideLower + num,
                "眼" + sideLower + num))
                return true;
        }

        if (!string.IsNullOrEmpty(sideWord))
        {
            if (ContainsAnyTemplate(
                normalizedText,
                "eye_" + sideWord + num,
                "eye_" + sideWord + "_" + num,
                "eye-" + sideWord + num,
                "eye-" + sideWord + "-" + num,
                "eye " + sideWord + num,
                "eye " + sideWord + " " + num))
                return true;
        }

        return false;
    }

    private void ApplyMaskReferenceToRowAndBoundControllers(SkyPrisonAnimationRigRow subject, SkyPrisonAnimationRigRow mask)
    {
        if (subject == null || mask == null || string.IsNullOrEmpty(mask.key))
            return;

        subject.maskReferenceKey = mask.key;

        if (state == null || state.RigRows == null)
            return;

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || row.isFolder)
                continue;

            bool boundToSubject = string.Equals(row.boundRigKey, subject.key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.meshDeformTargetKey, subject.key, StringComparison.OrdinalIgnoreCase);

            if (boundToSubject)
                row.maskReferenceKey = mask.key;
        }
    }

    private bool IsTemplateFaceHairMeshDeformerRow(SkyPrisonAnimationRigRow row)
    {
        if (row == null)
            return false;

        string text = (row.name ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.semantic ?? string.Empty) + " " + (row.meshDeformTargetKey ?? string.Empty);
        string kind = DetectTemplateTargetKind(text);
        return kind == "eye" || kind == "brow" || kind == "lash" || (!string.IsNullOrEmpty(kind) && kind.StartsWith("hair", StringComparison.OrdinalIgnoreCase));
    }

    private void MergeDuplicateTemplateMeshDeformers()
    {
        if (state == null || state.RigRows == null)
            return;

        HashSet<string> referencedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (state.TimelineKeyframes != null)
        {
            for (int i = 0; i < state.TimelineKeyframes.Count; i++)
            {
                SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
                if (k == null) continue;
                bool meshKey = k.useMeshDeform || string.Equals(k.targetKind, "MeshDeformer", StringComparison.OrdinalIgnoreCase);
                if (meshKey && !string.IsNullOrEmpty(k.targetKey))
                    referencedKeys.Add(k.targetKey);
            }
        }

        Dictionary<string, SkyPrisonAnimationRigRow> keepBySlot = new Dictionary<string, SkyPrisonAnimationRigRow>(StringComparer.OrdinalIgnoreCase);
        List<SkyPrisonAnimationRigRow> removeRows = new List<SkyPrisonAnimationRigRow>();
        Dictionary<string, string> replaceKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || !row.isMeshDeformer || !IsTemplateFaceHairMeshDeformerRow(row))
                continue;

            string slot = (row.parentKey ?? string.Empty) + "|" + NormalizeTemplateMatchText(row.name ?? string.Empty) + "|" + (row.meshDeformTargetKey ?? string.Empty);
            SkyPrisonAnimationRigRow keep;
            if (!keepBySlot.TryGetValue(slot, out keep) || ShouldReplaceTemplateMeshDeformerKeep(row, keep, referencedKeys))
            {
                if (keep != null)
                {
                    removeRows.Add(keep);
                    if (!string.IsNullOrEmpty(keep.key) && !string.IsNullOrEmpty(row.key))
                        replaceKeyMap[keep.key] = row.key;
                }
                keepBySlot[slot] = row;
            }
            else
            {
                removeRows.Add(row);
                if (!string.IsNullOrEmpty(row.key) && keep != null && !string.IsNullOrEmpty(keep.key))
                    replaceKeyMap[row.key] = keep.key;
            }
        }

        if (replaceKeyMap.Count > 0 && state.TimelineKeyframes != null)
        {
            for (int i = 0; i < state.TimelineKeyframes.Count; i++)
            {
                SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
                if (k == null) continue;
                string mapped;
                if (!string.IsNullOrEmpty(k.targetKey) && replaceKeyMap.TryGetValue(k.targetKey, out mapped))
                {
                    k.targetKey = mapped;
                    k.layerWeightTargetKey = mapped;
                    SkyPrisonAnimationRigRow row = state.FindRigRow(mapped);
                    if (row != null) k.targetName = row.name;
                }
                if (!string.IsNullOrEmpty(k.layerWeightTargetKey) && replaceKeyMap.TryGetValue(k.layerWeightTargetKey, out mapped))
                    k.layerWeightTargetKey = mapped;
            }
        }

        for (int i = removeRows.Count - 1; i >= 0; i--)
        {
            SkyPrisonAnimationRigRow row = removeRows[i];
            if (row != null)
                state.RigRows.Remove(row);
        }
    }

    private bool ShouldReplaceTemplateMeshDeformerKeep(SkyPrisonAnimationRigRow candidate, SkyPrisonAnimationRigRow current, HashSet<string> referencedKeys)
    {
        if (candidate == null)
            return false;
        if (current == null)
            return true;

        bool candidateReferenced = referencedKeys != null && !string.IsNullOrEmpty(candidate.key) && referencedKeys.Contains(candidate.key);
        bool currentReferenced = referencedKeys != null && !string.IsNullOrEmpty(current.key) && referencedKeys.Contains(current.key);
        if (candidateReferenced != currentReferenced)
            return candidateReferenced;

        bool candidateHasPoints = candidate.meshDeformPoints != null && candidate.meshDeformPoints.Count > 0;
        bool currentHasPoints = current.meshDeformPoints != null && current.meshDeformPoints.Count > 0;
        if (candidateHasPoints != currentHasPoints)
            return candidateHasPoints;

        return false;
    }

    private SkyPrisonAnimationRigRow FindTemplateHeadParentRig()
    {
        if (state == null || state.RigRows == null)
            return null;

        // 面部模板控制器必须挂到真正的 Head/头 节点下面。
        // 之前这里用 Contains("头")，会把「其它头发组」这种 Hair 组误判成 Head，
        // 结果眼睛/睫毛/眉毛控制器都被塞到头发组下面。
        SkyPrisonAnimationRigRow exact = state.FindRigRow("Head");
        if (exact != null && !exact.isMeshDeformer)
            return exact;

        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || row.isMeshDeformer)
                continue;

            string key = NormalizeTemplateMatchText(row.key ?? string.Empty);
            string name = NormalizeTemplateMatchText(row.name ?? string.Empty);
            string semantic = NormalizeTemplateMatchText(row.semantic ?? string.Empty);
            string text = key + " " + name + " " + semantic;

            // 头发、发组、刘海、辫子绝对不能作为 Head 父节点。
            if (ContainsAnyTemplate(text, "hair", "头发", "頭髪", "髪", "发", "前发", "后发", "側髪", "侧发", "辫", "braid"))
                continue;

            int score = int.MinValue;

            if (string.Equals(row.key, "Head", StringComparison.OrdinalIgnoreCase))
                score = 1000;
            else
            {
                score = 0;

                if (key == "head") score += 260;
                if (semantic == "head") score += 240;
                if (name == "头" || name == "頭" || name == "head") score += 220;

                if (ContainsAnyTemplate(semantic, "head", "face", "顔")) score += 120;
                if (ContainsAnyTemplate(name, "脸", "脸部", "面部", "顔", "face")) score += 90;

                // HeadTop / 头顶 不是面部控制器的父节点。
                if (ContainsAnyTemplate(key, "headtop") || ContainsAnyTemplate(name, "头顶", "頭頂", "top"))
                    score -= 180;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        return bestScore >= 100 ? best : null;
    }

    private string BuildTemplateSlotId(string name, string key, SkyPrisonAnimationRigRow targetPsb)
    {
        string text = (name ?? string.Empty) + " " + (key ?? string.Empty);
        string kind = DetectTemplateTargetKind(text);
        int side = DetectTemplateTargetSide(text);

        if (string.IsNullOrEmpty(kind) && targetPsb != null)
            kind = DetectTemplateTargetKind((targetPsb.name ?? string.Empty) + " " + (targetPsb.key ?? string.Empty) + " " + (targetPsb.sourceSpriteName ?? string.Empty) + " " + (targetPsb.sourceLayerPath ?? string.Empty) + " " + (targetPsb.semantic ?? string.Empty));
        if (side == 0 && targetPsb != null)
            side = DetectTemplateTargetSide((targetPsb.name ?? string.Empty) + " " + (targetPsb.key ?? string.Empty) + " " + (targetPsb.sourceSpriteName ?? string.Empty) + " " + (targetPsb.sourceLayerPath ?? string.Empty) + " " + (targetPsb.semantic ?? string.Empty));

        if (string.IsNullOrEmpty(kind))
            return NormalizeTemplateMatchText(name ?? key ?? string.Empty);

        string targetKey = targetPsb != null ? NormalizeTemplateMatchText(targetPsb.key ?? targetPsb.name ?? string.Empty) : string.Empty;
        return kind + "|" + side + "|" + targetKey;
    }

    private bool TemplatePsbMatchesSourceSlot(string sourceName, string sourceKey, SkyPrisonAnimationRigRow psb)
    {
        if (psb == null)
            return false;

        string sourceText = (sourceName ?? string.Empty) + " " + (sourceKey ?? string.Empty);
        string psbText = (psb.name ?? string.Empty) + " " + (psb.key ?? string.Empty) + " " + (psb.sourceSpriteName ?? string.Empty) + " " + (psb.sourceLayerPath ?? string.Empty) + " " + (psb.semantic ?? string.Empty);

        string sourceKind = DetectTemplateTargetKind(sourceText);
        string psbKind = DetectTemplateTargetKind(psbText);
        int sourceSide = DetectTemplateTargetSide(sourceText);
        int psbSide = DetectTemplateTargetSide(psbText);

        if (!string.IsNullOrEmpty(sourceKind) && !string.IsNullOrEmpty(psbKind))
        {
            bool sourceHair = sourceKind.StartsWith("hair", StringComparison.OrdinalIgnoreCase);
            bool psbHair = psbKind.StartsWith("hair", StringComparison.OrdinalIgnoreCase);

            if (sourceHair != psbHair)
                return false;

            if (!sourceHair && !string.Equals(sourceKind, psbKind, StringComparison.OrdinalIgnoreCase))
                return false;

            // 当前项目里 brow_L / brow_R 是眉毛；eye_L1 / eye_R1 才是睫毛。
            if (sourceKind == "brow" && IsExactEyeOneSideLayer(NormalizeTemplateMatchText(psbText), psbSide == 0 ? sourceSide : psbSide))
                return false;
        }

        if (sourceSide != 0 && psbSide != 0 && sourceSide != psbSide)
            return false;

        return true;
    }

    private SkyPrisonAnimationRigRow FindExistingTemplateControllerNode(string desiredName, string desiredKeyBase, SkyPrisonAnimationRigRow targetPsb)
    {
        if (state == null || state.RigRows == null)
            return null;

        string desiredSlot = BuildTemplateSlotId(desiredName, desiredKeyBase, targetPsb);

        for (int i = 0; i < state.RigRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[i];
            if (row == null || row.isMeshDeformer) continue;

            string rowSlot = BuildTemplateSlotId(row.name, row.key, targetPsb);
            bool sameSlot = !string.IsNullOrEmpty(desiredSlot) && string.Equals(rowSlot, desiredSlot, StringComparison.OrdinalIgnoreCase);

            if (sameSlot && string.Equals(row.name, desiredName, StringComparison.OrdinalIgnoreCase)) return row;
            if (sameSlot && !string.IsNullOrEmpty(desiredKeyBase) && string.Equals(row.key, desiredKeyBase, StringComparison.OrdinalIgnoreCase)) return row;

            // 旧版本曾经只按 boundRigKey 复用控制器，导致“眼睛R曲面”被塞进“眼睛L”下面。
            // 这里必须先确认语义槽位一致，不能再让左右/部位串线。
            if (sameSlot && targetPsb != null && !string.IsNullOrEmpty(row.boundRigKey) && string.Equals(row.boundRigKey, targetPsb.key, StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return null;
    }

    private void BindTemplatePsbToController(SkyPrisonAnimationRigRow psb, SkyPrisonAnimationRigRow controller)
    {
        if (psb == null || controller == null)
            return;

        psb.boundRigKey = controller.key;
        psb.boundRigName = controller.name;
        psb.bindMode = "模板";
        psb.bindConfidence = 1f;
        psb.mapped = true;

        controller.boundRigKey = psb.key;
        controller.boundRigName = psb.name;
        controller.sourceAssetPath = psb.sourceAssetPath;
        controller.sourceSpriteName = psb.sourceSpriteName;
        controller.sourceLayerPath = string.IsNullOrEmpty(psb.sourceLayerPath) ? psb.name : psb.sourceLayerPath;
        controller.previewColor = psb.previewColor;
        controller.psbLayerWeight = psb.psbLayerWeight;
        controller.usePsbLayerWeight = psb.usePsbLayerWeight;
    }

    private void MoveRigRowUnderParent(SkyPrisonAnimationRigRow row, SkyPrisonAnimationRigRow parent)
    {
        if (row == null || state == null || state.RigRows == null)
            return;

        int oldIndex = state.RigRows.IndexOf(row);
        if (oldIndex < 0)
            return;

        state.RigRows.RemoveAt(oldIndex);
        int insertIndex = FindRigInsertIndexForTemplateChild(parent);
        if (insertIndex > state.RigRows.Count) insertIndex = state.RigRows.Count;
        state.RigRows.Insert(insertIndex, row);
    }

    private int FindRigInsertIndexForTemplateChild(SkyPrisonAnimationRigRow parentRig)
    {
        if (state == null || state.RigRows == null)
            return 0;

        if (parentRig == null)
            return state.RigRows.Count;

        int parentIndex = state.RigRows.IndexOf(parentRig);
        if (parentIndex < 0)
            return state.RigRows.Count;

        int insert = parentIndex + 1;
        while (insert < state.RigRows.Count)
        {
            SkyPrisonAnimationRigRow row = state.RigRows[insert];
            if (row == null || row.depth <= parentRig.depth)
                break;
            insert++;
        }
        return insert;
    }

    private int FindRigInsertIndexForTemplateMeshDeformer(SkyPrisonAnimationRigRow parentRig)
    {
        return FindRigInsertIndexForTemplateChild(parentRig);
    }

    private void EnsureMeshDeformerPointGridForRow(SkyPrisonAnimationRigRow deformer)
    {
        if (deformer == null)
            return;

        int columns = Mathf.Clamp(deformer.meshDeformColumns, 2, 16);
        int rows = Mathf.Clamp(deformer.meshDeformRows, 2, 16);
        deformer.meshDeformColumns = columns;
        deformer.meshDeformRows = rows;
        if (deformer.meshDeformPoints == null)
            deformer.meshDeformPoints = new List<SkyPrisonMeshDeformPoint>();

        Dictionary<string, SkyPrisonMeshDeformPoint> old = new Dictionary<string, SkyPrisonMeshDeformPoint>();
        for (int i = 0; i < deformer.meshDeformPoints.Count; i++)
        {
            SkyPrisonMeshDeformPoint p = deformer.meshDeformPoints[i];
            if (p != null) old[p.x + ":" + p.y] = p;
        }

        deformer.meshDeformPoints.Clear();
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                SkyPrisonMeshDeformPoint existing;
                if (old.TryGetValue(x + ":" + y, out existing) && existing != null)
                    deformer.meshDeformPoints.Add(existing);
                else
                    deformer.meshDeformPoints.Add(new SkyPrisonMeshDeformPoint { x = x, y = y, offset = Vector2.zero });
            }
        }
    }

    private string MakeTemplateControllerNodeName(string targetName, string targetKey)
    {
        string raw = !string.IsNullOrWhiteSpace(targetName) ? targetName : targetKey;
        string stripped = StripTemplateDeformerSuffix(raw).Trim();
        string kind = DetectTemplateTargetKind(stripped + " " + targetKey);
        int side = DetectTemplateTargetSide(stripped + " " + targetKey);

        // 面部模板节点必须使用稳定语义名，不能把旧 NewNode / Copy / MeshDeformer 名字带进层级。
        if (kind == "eye" && side < 0) return "眼睛L";
        if (kind == "eye" && side > 0) return "眼睛R";
        if (kind == "brow" && side < 0) return "眉毛L";
        if (kind == "brow" && side > 0) return "眉毛R";
        if (kind == "lash" && side < 0) return "睫毛L";
        if (kind == "lash" && side > 0) return "睫毛R";

        if (string.IsNullOrWhiteSpace(stripped)) stripped = "无骨骼节点";
        return stripped;
    }

    private string MakeTemplateControllerKeyBase(string targetName, string targetKey, SkyPrisonAnimationRigRow targetPsb)
    {
        string name = MakeTemplateControllerNodeName(targetName, targetKey);
        string kind = DetectTemplateTargetKind(name + " " + targetKey);
        int side = DetectTemplateTargetSide(name + " " + targetKey);
        string sideSuffix = side < 0 ? "_L" : (side > 0 ? "_R" : string.Empty);
        string semantic = string.IsNullOrEmpty(kind) ? "Part" : kind;
        string psbPart = targetPsb != null && !string.IsNullOrEmpty(targetPsb.key) ? targetPsb.key : name;
        return "TemplateNode_" + MakeSafeKey(semantic + sideSuffix + "_" + psbPart);
    }

    private string MakeTemplateMeshDeformerKeyBase(string controllerKey, string targetPsbKey)
    {
        string raw = !string.IsNullOrEmpty(controllerKey) ? controllerKey : targetPsbKey;
        return "MeshDeformer_" + MakeSafeKey(raw);
    }

    private string MakeTemplateControllerSemantic(string targetName, string targetKey)
    {
        string kind = DetectTemplateTargetKind((targetName ?? string.Empty) + " " + (targetKey ?? string.Empty));
        if (kind == "eye") return "Face/Eye NoBone";
        if (kind == "brow") return "Face/Brow NoBone";
        if (kind == "lash") return "Face/Lash NoBone";
        if (!string.IsNullOrEmpty(kind) && kind.StartsWith("hair", StringComparison.OrdinalIgnoreCase)) return "Hair NoBone";
        return "NoBone";
    }

    private int GetTemplateControllerIconNumber(string semantic)
    {
        string n = NormalizeTemplateMatchText(semantic);
        if (ContainsAnyTemplate(n, "hair")) return 40;
        if (ContainsAnyTemplate(n, "eye", "brow", "lash", "face")) return 44;
        return 42;
    }

    private string MakeTemplateMeshDeformerName(string targetName, string targetKey)
    {
        string controllerName = MakeTemplateControllerNodeName(targetName, targetKey);
        if (!string.IsNullOrWhiteSpace(controllerName) && controllerName != "无骨骼节点")
            return controllerName + "_曲面变形";

        string raw = !string.IsNullOrWhiteSpace(targetName) ? targetName : targetKey;
        if (string.IsNullOrWhiteSpace(raw)) raw = "曲面变形";
        raw = StripTemplateDeformerSuffix(raw).Trim();
        if (string.IsNullOrWhiteSpace(raw)) raw = "曲面变形";
        return raw + "_曲面变形";
    }

    private SkyPrisonAnimationRigRow FindBestPsbLayerByTemplateName(string targetName, string targetKey)
    {
        string raw = ((targetName ?? string.Empty) + " " + (targetKey ?? string.Empty)).Trim();
        string baseName = StripTemplateDeformerSuffix(raw);
        string kind = DetectTemplateTargetKind(baseName);
        int side = DetectTemplateTargetSide(baseName);
        string normalizedBase = NormalizeTemplateMatchText(baseName);
        string[] tokens = BuildTemplateMatchTokens(normalizedBase);

        SkyPrisonAnimationRigRow best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < state.PsbRows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = state.PsbRows[i];
            if (row == null || row.isFolder || string.IsNullOrEmpty(row.key)) continue;

            string text = NormalizeTemplateMatchText((row.name ?? string.Empty) + " " + (row.key ?? string.Empty) + " " + (row.sourceSpriteName ?? string.Empty) + " " + (row.sourceLayerPath ?? string.Empty) + " " + (row.semantic ?? string.Empty));
            int rowSide = DetectTemplateTargetSide(text);
            int score = 0;

            if (side != 0)
            {
                if (rowSide == side) score += 28;
                else if (rowSide == -side) score -= 90;
            }

            score += ScoreTemplateKindMatch(kind, text);

            // 眉毛模板不能吃 eye_L1 / eye_R1；这两个在当前 Axia PSB 中是睫毛层。
            if (kind == "brow" && IsExactEyeOneSideLayer(text, rowSide == 0 ? side : rowSide))
                score -= 180;

            // 泛用「眼睛L/R_曲面变形」不允许随机吃到 eye_L1/eye_R1 睫毛层，
            // 也不应该落到 eye_L2/eye_R2 瞳孔层；默认绑定到 3 号眼白/蒙版层。
            if (kind == "eye")
            {
                if (IsExactEyeOneSideLayer(text, rowSide == 0 ? side : rowSide)) score -= 240;
                if (HasTemplateEyeNumberMarker(text, rowSide == 0 ? side : rowSide, 3)) score += 140;
                if (HasTemplateEyeNumberMarker(text, rowSide == 0 ? side : rowSide, 2)) score -= 80;
            }

            for (int t = 0; t < tokens.Length; t++)
            {
                string token = tokens[t];
                if (token.Length >= 2 && text.Contains(token)) score += 8;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = row;
            }
        }

        int requiredScore = kind == "brow" ? 80 : 18;
        return bestScore >= requiredScore ? best : null;
    }

    private string StripTemplateDeformerSuffix(string raw)
    {
        string s = raw ?? string.Empty;
        s = s.Replace("_曲面变形", " ").Replace("曲面变形", " ");
        s = s.Replace("MeshDeformer_", " ").Replace("meshdeformer_", " ");
        return s.Trim();
    }

    private string NormalizeTemplateMatchText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        string s = raw.ToLowerInvariant();
        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c > 127))
                chars[i] = ' ';
        }
        return new string(chars);
    }

    private string[] BuildTemplateMatchTokens(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return new string[0];
        string[] raw = normalized.Split(new char[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> result = new List<string>();
        for (int i = 0; i < raw.Length; i++)
        {
            string t = raw[i].Trim();
            if (t.Length < 2) continue;
            if (t == "meshdeformer" || t == "newnode" || t == "copy") continue;
            int dummy;
            if (int.TryParse(t, out dummy)) continue;
            if (!result.Contains(t)) result.Add(t);
        }
        return result.ToArray();
    }

    private string DetectTemplateTargetKind(string text)
    {
        string n = NormalizeTemplateMatchText(text);
        if (ContainsAnyTemplate(n, "eyelash", "eye_lash", "lash", "睫毛", "まつげ", "睫")) return "lash";
        if (ContainsAnyTemplate(n, "eyebrow", "eye_brow", "brow", "眉毛", "眉")) return "brow";
        if (ContainsAnyTemplate(n, "sclera", "eye_white", "white_eye", "eye", "eyes", "眼白", "眼睛", "目", "眼")) return "eye";
        if (ContainsAnyTemplate(n, "hair_back2", "back2", "后发2", "後髪2")) return "hair_back2";
        if (ContainsAnyTemplate(n, "hair_front", "front_hair", "bang", "fringe", "刘海", "前发", "前髪")) return "hair_front";
        if (ContainsAnyTemplate(n, "hair_side", "side_hair", "sidelock", "侧发", "横髪")) return "hair_side";
        if (ContainsAnyTemplate(n, "hair_back", "back_hair", "后发", "後髪")) return "hair_back";
        if (ContainsAnyTemplate(n, "hair", "髪", "发")) return "hair";
        return "";
    }

    private int DetectTemplateTargetSide(string text)
    {
        string n = NormalizeTemplateMatchText(text);
        if (ContainsAnyTemplate(n, "左", " left", "left ")) return -1;
        if (ContainsAnyTemplate(n, "右", " right", "right ")) return 1;

        // 支持眼睛L / 眉毛R / eye_L3 / brow-R / lashR 这类模板命名。
        // 旧写法只认 _l / _r，遇到中文+L/R 会识别失败，直接导致左右串线。
        if (HasTemplateSideMarker(n, 'l')) return -1;
        if (HasTemplateSideMarker(n, 'r')) return 1;
        return 0;
    }

    private bool HasTemplateSideMarker(string n, char side)
    {
        if (string.IsNullOrEmpty(n)) return false;

        for (int i = 0; i < n.Length; i++)
        {
            if (n[i] != side) continue;

            char prev = i > 0 ? n[i - 1] : ' ';
            char next = i + 1 < n.Length ? n[i + 1] : ' ';

            bool prevBoundary = i == 0 || prev == '_' || prev == '-' || prev == ' ' || prev > 127;
            bool nextBoundary = i + 1 >= n.Length || next == '_' || next == '-' || next == ' ' || next > 127 || char.IsDigit(next);

            if (prevBoundary && nextBoundary)
                return true;
        }

        return false;
    }

    private int ScoreTemplateKindMatch(string kind, string text)
    {
        if (string.IsNullOrEmpty(kind)) return 0;
        if (kind == "eye")
        {
            int score = ContainsAnyTemplate(text, "sclera", "eye_white", "white_eye", "眼白") ? 52 : 0;
            if (ContainsAnyTemplate(text, "eye", "eyes", "目", "眼", "眼睛")) score += 40;
            if (ContainsAnyTemplate(text, "brow", "眉", "lash", "睫")) score -= 70;
            return score;
        }
        if (kind == "brow")
        {
            int score = ContainsAnyTemplate(text, "eyebrow", "eye_brow", "brow", "眉毛", "眉", "mayu", "mayuge") ? 90 : -30;
            if (IsAnyExactEyeOneSideLayer(text)) score -= 140;
            if (ContainsAnyTemplate(text, "eyelash", "eye_lash", "lash", "睫毛", "睫")) score -= 90;
            return score;
        }
        if (kind == "lash")
        {
            int score = IsAnyExactEyeOneSideLayer(text) ? 86 : -20;
            if (ContainsAnyTemplate(text, "eyelash", "eye_lash", "lash", "睫毛", "睫")) score = Mathf.Max(score, 58);
            if (ContainsAnyTemplate(text, "eyebrow", "eye_brow", "brow", "眉毛", "眉")) score -= 120;
            return score;
        }
        if (kind == "hair_back2") return ContainsAnyTemplate(text, "hair_back2", "back2", "后发2", "後髪2") ? 65 : (ContainsAnyTemplate(text, "hair_back", "back_hair", "后发", "後髪") ? 36 : -10);
        if (kind == "hair_front") return ContainsAnyTemplate(text, "hair_front", "front_hair", "bang", "fringe", "刘海", "前发", "前髪") ? 60 : -10;
        if (kind == "hair_side") return ContainsAnyTemplate(text, "hair_side", "side_hair", "sidelock", "侧发", "横髪") ? 60 : -10;
        if (kind == "hair_back")
        {
            int score = ContainsAnyTemplate(text, "hair_back", "back_hair", "后发", "後髪") ? 60 : -10;
            if (ContainsAnyTemplate(text, "front", "side", "前", "侧", "横")) score -= 30;
            return score;
        }
        if (kind == "hair") return ContainsAnyTemplate(text, "hair", "髪", "发") ? 35 : -10;
        return 0;
    }

    private bool ContainsAnyTemplate(string text, params string[] keys)
    {
        if (string.IsNullOrEmpty(text) || keys == null) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            if (!string.IsNullOrEmpty(k) && text.Contains(k.ToLowerInvariant())) return true;
        }
        return false;
    }

    private string GenerateUniqueRigRowKey(string baseKey)
    {
        if (string.IsNullOrWhiteSpace(baseKey)) baseKey = "MeshDeformer";
        baseKey = MakeSafeKey(baseKey);
        if (string.IsNullOrEmpty(baseKey)) baseKey = "MeshDeformer";

        string key = baseKey;
        int suffix = 1;
        while (state.FindRigRow(key) != null || state.FindPsbRow(key) != null)
        {
            key = baseKey + "_" + suffix;
            suffix++;
        }
        return key;
    }

    private void RemoveActionTimelineData(string actionKey)
    {
        state.TimelineKeyframes.RemoveAll(x => x != null && x.actionKey == actionKey);
        state.LayerOrderKeyframes.RemoveAll(x => x != null && x.actionKey == actionKey);
        state.MotionKeyframes.RemoveAll(x => x != null && x.actionKey == actionKey);
    }

    private string GenerateUniqueActionKey(string baseKey)
    {
        if (string.IsNullOrWhiteSpace(baseKey))
            baseKey = "Action";

        HashSet<string> used = new HashSet<string>();
        for (int i = 0; i < state.Actions.Count; i++)
        {
            if (state.Actions[i] != null && !string.IsNullOrWhiteSpace(state.Actions[i].key))
                used.Add(state.Actions[i].key);
        }

        if (!used.Contains(baseKey))
            return baseKey;

        int suffix = 1;
        while (used.Contains(baseKey + "_" + suffix))
            suffix++;

        return baseKey + "_" + suffix;
    }

    private SkyPrisonAnimationActionRow CloneActionRow(SkyPrisonAnimationActionRow action)
    {
        if (action == null)
            return new SkyPrisonAnimationActionRow { key = "Action", name = "动作", type = "关键帧", status = "导入", loop = true, duration = 1.2f };

        return new SkyPrisonAnimationActionRow
        {
            key = action.key,
            name = action.name,
            type = action.type,
            status = action.status,
            loop = action.loop,
            duration = action.duration
        };
    }

    private void ExportRecording(string format)
    {
        string extension = format == "png_sequence" ? "json" : format;
        string title = format == "png_sequence" ? "导出序列帧任务" : "导出录像 " + format.ToUpperInvariant();
        string defaultName = MakeSafeFileName(currentPackageName) + "_TimelineExport";

        string absolutePath = EditorUtility.SaveFilePanel(title, Application.dataPath, defaultName, extension);
        if (string.IsNullOrEmpty(absolutePath))
            return;

        try
        {
            SkyPrisonAnimationRecordingExport export = new SkyPrisonAnimationRecordingExport
            {
                format = format,
                packageName = currentPackageName,
                durationSeconds = state.TimelineDurationSeconds,
                frameRate = state.TimelineFrameRate,
                totalFrames = state.TimelineTotalFrames,
                note = "当前版本先保存录像导出任务。真实MP4/GIF/APNG编码需要接入预览区逐帧捕获器后执行。"
            };

            string json = JsonUtility.ToJson(export, true);

            if (format == "png_sequence")
            {
                File.WriteAllText(absolutePath, json);
            }
            else
            {
                string taskPath = absolutePath + ".skyrecord.json";
                File.WriteAllText(taskPath, json);
                EditorUtility.DisplayDialog("录像导出任务已创建", "已创建导出任务：\n" + taskPath + "\n\n真实编码器接入后会按这个任务输出 " + format.ToUpperInvariant() + "。", "确定");
            }

            Debug.Log("动作工作台：录像导出任务 → " + absolutePath);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("录像导出失败", ex.Message, "确定");
        }
    }

    private void SelectAllCurrentStructureRows()
    {
        state.SelectedRigIndices.Clear();
        int count = state.GetCurrentRows().Count;
        for (int i = 0; i < count; i++)
            state.SelectedRigIndices.Add(i);
        if (count > 0)
        {
            state.SelectedRig = 0;
            state.LastSelectedRigIndex = 0;
        }
        RepaintOwnerWindow();
    }

    private SkyPrisonAnimationModelPackage BuildPackage()
    {
        SkyPrisonAnimationModelPackage package = new SkyPrisonAnimationModelPackage();
        package.version = "0.1";
        package.displayName = currentPackageName;
        package.sourcePsdAssetPath = state.SourcePsdAssetPath;
        package.currentRigTemplateKey = state.CurrentRigTemplateKey;
        package.manualRigTemplateMode = state.ManualRigTemplateMode;
        package.timelineDurationSeconds = state.TimelineDurationSeconds;
        package.timelineFrameRate = state.TimelineFrameRate;
        package.playbackSpeedPercent = Mathf.RoundToInt(state.PlaybackSpeedPercent);
        package.formulaType = state.FormulaType.ToString();
        package.formulaAmplitude = state.FormulaAmplitude;
        package.formulaFrequency = state.FormulaFrequency;
        package.formulaPhase = state.FormulaPhase;
        package.formulaOffset = state.FormulaOffset;

        package.actions = new List<SkyPrisonAnimationActionRow>(state.Actions);
        package.rigRows = new List<SkyPrisonAnimationRigRow>(state.RigRows);
        package.psbRows = new List<SkyPrisonAnimationRigRow>(state.PsbRows);
        package.socketRows = new List<SkyPrisonAnimationRigRow>(state.SocketRows);
        package.assemblySlots = new List<SkyPrisonAnimationAssemblySlot>(state.AssemblySlots);
        package.layerOrderKeyframes = new List<SkyPrisonAnimationLayerOrderKeyframe>(state.LayerOrderKeyframes);
        package.timelineKeyframes = new List<SkyPrisonAnimationTimelineKeyframe>(state.TimelineKeyframes);
        package.motionKeyframes = new List<SkyPrisonAnimationMotionKeyframe>();
        if (state.MotionKeyframes != null)
        {
            for (int i = 0; i < state.MotionKeyframes.Count; i++)
            {
                SkyPrisonAnimationMotionKeyframe key = state.MotionKeyframes[i];
                if (key != null)
                    package.motionKeyframes.Add(key.Clone());
            }
        }
        return package;
    }

    private void ApplyPackage(SkyPrisonAnimationModelPackage package)
    {
        state.SourcePsdAssetPath = package.sourcePsdAssetPath ?? string.Empty;
        state.CurrentRigTemplateKey = string.IsNullOrEmpty(package.currentRigTemplateKey) ? "Human" : package.currentRigTemplateKey;
        state.ManualRigTemplateMode = package.manualRigTemplateMode || state.CurrentRigTemplateKey == "Custom";

        state.Actions.Clear();
        if (package.actions != null) state.Actions.AddRange(package.actions);

        state.RigRows.Clear();
        if (package.rigRows != null) state.RigRows.AddRange(package.rigRows);

        state.PsbRows.Clear();
        if (package.psbRows != null) state.PsbRows.AddRange(package.psbRows);

        state.SocketRows.Clear();
        if (package.socketRows != null) state.SocketRows.AddRange(package.socketRows);

        state.AssemblySlots.Clear();
        if (package.assemblySlots != null) state.AssemblySlots.AddRange(package.assemblySlots);
        state.SyncAllAppearanceSlotPreviewRows();
        state.LayerOrderKeyframes.Clear();
        if (package.layerOrderKeyframes != null) state.LayerOrderKeyframes.AddRange(package.layerOrderKeyframes);

        state.TimelineKeyframes.Clear();
        if (package.timelineKeyframes != null) state.TimelineKeyframes.AddRange(package.timelineKeyframes);

        state.MotionKeyframes.Clear();
        if (package.motionKeyframes != null)
        {
            for (int i = 0; i < package.motionKeyframes.Count; i++)
            {
                SkyPrisonAnimationMotionKeyframe key = package.motionKeyframes[i];
                if (key != null)
                    state.MotionKeyframes.Add(key.Clone());
            }
            state.SortMotionKeyframes();
        }

        state.ClearMotionPoseEditorState(true);
        state.InvalidateManualAngleRigSignature();
        state.EnsureMotionPoseEditorStateMatchesCurrentRig();

        if (state.Actions.Count == 0)
        {
            if (state.ManualRigTemplateMode)
                state.Actions.Add(new SkyPrisonAnimationActionRow { key = "Idle", name = "待机", type = "自定义", status = "手动", loop = true, duration = 1.2f });
            else
                state.BuildMockData();
        }

        state.TimelineDurationSeconds = Mathf.Max(0.01f, package.timelineDurationSeconds);
        state.TimelineFrameRate = Mathf.Max(1, package.timelineFrameRate);
        state.PlaybackSpeedPercent = package.playbackSpeedPercent <= 0 ? 100 : package.playbackSpeedPercent;
        state.CurrentTime = Mathf.Clamp(state.CurrentTime, 0f, state.TimelineDurationSeconds);

        SkyPrisonAnimationFormulaType formulaType;
        if (Enum.TryParse(package.formulaType, out formulaType))
            state.FormulaType = formulaType;

        state.FormulaAmplitude = package.formulaAmplitude;
        state.FormulaFrequency = package.formulaFrequency <= 0f ? 1f : package.formulaFrequency;
        state.FormulaPhase = package.formulaPhase;
        state.FormulaOffset = package.formulaOffset;

        state.ClearStructureUndo();
        state.ClearRigUndo();
    }

    private static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "New2DModelPackage";

        string result = name;
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
            result = result.Replace(invalid[i].ToString(), "_");

        return result.Trim();
    }

    private static string StripModelPackageExtension(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Trim();

        string fullExtension = "." + PackageExtension;
        while (name.EndsWith(fullExtension, StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - fullExtension.Length);

        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".json".Length);

        if (name.EndsWith(".sky2dmodel", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".sky2dmodel".Length);

        return name.Trim();
    }

    private static void EnsureFixedPackageFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Project"))
            AssetDatabase.CreateFolder("Assets", "_Project");
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
            AssetDatabase.CreateFolder("Assets/_Project", "Data");
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/AnimationWorkbench"))
            AssetDatabase.CreateFolder("Assets/_Project/Data", "AnimationWorkbench");
        if (!AssetDatabase.IsValidFolder(FixedPackageFolder))
            AssetDatabase.CreateFolder("Assets/_Project/Data/AnimationWorkbench", "ModelPackages");
    }

    private static string GetAbsoluteFixedPackageFolder()
    {
        EnsureFixedPackageFolder();
        return AssetPathToAbsolutePath(FixedPackageFolder);
    }

    private static void EnsureActionPackFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Project"))
            AssetDatabase.CreateFolder("Assets", "_Project");
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
            AssetDatabase.CreateFolder("Assets/_Project", "Data");
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/AnimationWorkbench"))
            AssetDatabase.CreateFolder("Assets/_Project/Data", "AnimationWorkbench");
        if (!AssetDatabase.IsValidFolder(FixedActionPackFolder))
            AssetDatabase.CreateFolder("Assets/_Project/Data/AnimationWorkbench", "ActionPacks");
    }

    private static string GetAbsoluteActionPackFolder()
    {
        EnsureActionPackFolder();
        return AssetPathToAbsolutePath(FixedActionPackFolder);
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
    }

    private static string AbsoluteToAssetPath(string absolutePath)
    {
        string normalized = absolutePath.Replace("\\", "/");
        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        if (normalized.StartsWith(projectRoot))
            return normalized.Substring(projectRoot.Length + 1);
        return normalized;
    }

    [Serializable]
    private class SkyPrisonAnimationModelPackage
    {
        public string version;
        public string displayName;
        public string sourcePsdAssetPath;
        public string currentRigTemplateKey;
        public bool manualRigTemplateMode;
        public float timelineDurationSeconds;
        public int timelineFrameRate;
        public int playbackSpeedPercent;
        public string formulaType;
        public float formulaAmplitude;
        public float formulaFrequency;
        public float formulaPhase;
        public float formulaOffset;

        public List<SkyPrisonAnimationActionRow> actions;
        public List<SkyPrisonAnimationRigRow> rigRows;
        public List<SkyPrisonAnimationRigRow> psbRows;
        public List<SkyPrisonAnimationRigRow> socketRows;
        public List<SkyPrisonAnimationAssemblySlot> assemblySlots;
        public List<SkyPrisonAnimationLayerOrderKeyframe> layerOrderKeyframes;
        public List<SkyPrisonAnimationTimelineKeyframe> timelineKeyframes;
        public List<SkyPrisonAnimationMotionKeyframe> motionKeyframes;
    }

    [Serializable]
    private class SkyPrisonAnimationActionPack
    {
        public string version;
        public string displayName;
        public string sourcePackageName;
        public string sourceRigTemplateKey;
        public string sourcePsdAssetPath;
        public float durationSeconds;
        public int timelineFrameRate;
        public SkyPrisonAnimationActionRow action;
        public List<SkyPrisonAnimationTimelineKeyframe> timelineKeyframes;
        public List<SkyPrisonAnimationLayerOrderKeyframe> layerOrderKeyframes;
        public List<SkyPrisonAnimationMotionKeyframe> motionKeyframes;
    }

    [Serializable]
    private class SkyPrisonAnimationRecordingExport
    {
        public string format;
        public string packageName;
        public float durationSeconds;
        public int frameRate;
        public int totalFrames;
        public string note;
    }

    private void UpdatePreviewTime()
    {
        double now = EditorApplication.timeSinceStartup;

        if (!state.PreviewPlaying)
        {
            state.LastTime = now;
            nextPlaybackTickTime = now;

            if (ShouldKeepPhysicsSimulatingWhenPaused())
                RequestPreviewRepaintThrottled();

            return;
        }

        if (nextPlaybackTickTime <= 0.0001 || nextPlaybackTickTime > now + 1.0)
            nextPlaybackTickTime = now;

        double fps = Mathf.Clamp(state.TimelineFrameRate, 12, (int)PreviewPlayingMaxFps);
        double frameStep = 1.0 / fps;

        if (now < nextPlaybackTickTime)
            return;

        int guard = 0;
        while (now >= nextPlaybackTickTime && guard < 3)
        {
            AdvancePreviewOneFixedStep((float)frameStep);
            nextPlaybackTickTime += frameStep;
            guard++;
        }

        if (now - nextPlaybackTickTime > 0.25)
            nextPlaybackTickTime = now + frameStep;

        RequestPreviewRepaintThrottled();
    }

    private void AdvancePreviewOneFixedStep(float dt)
    {
        SkyPrisonAnimationActionRow action = state.CurrentAction();
        float duration = Mathf.Max(0.01f, state.TimelineDurationSeconds);
        float speed = Mathf.Max(0.01f, state.PlaybackSpeedPercent) / 100f;

        state.CurrentTime += dt * speed;

        if (ShouldPreviewLoop(action))
            state.CurrentTime = Mathf.Repeat(state.CurrentTime, duration);
        else
            state.CurrentTime = Mathf.Min(state.CurrentTime, duration);
    }


    private bool ShouldKeepPhysicsSimulatingWhenPaused()
    {
        if (state == null || !state.ShowPhysicsPreview)
            return false;

        // 有运行中的状态时继续刷新，让弹簧自然收束。
        if (state.PhysicsOscillatorStatuses != null && state.PhysicsOscillatorStatuses.Count > 0)
            return true;

        return HasAnyEnabledPhysicsTarget();
    }

    private bool HasAnyEnabledPhysicsTarget()
    {
        if (state == null)
            return false;

        if (state.RigRows != null)
        {
            for (int i = 0; i < state.RigRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.RigRows[i];
                if (row != null && row.usePhysicsInfluence && !string.IsNullOrWhiteSpace(row.physicsPresetKey))
                    return true;
            }
        }

        if (state.PsbRows != null)
        {
            for (int i = 0; i < state.PsbRows.Count; i++)
            {
                SkyPrisonAnimationRigRow row = state.PsbRows[i];
                if (row != null && row.usePhysicsInfluence && !string.IsNullOrWhiteSpace(row.physicsPresetKey))
                    return true;
            }
        }

        return false;
    }


    private bool ShouldPreviewLoop(SkyPrisonAnimationActionRow action)
    {
        // 动作工作台是预览 / 制作用的时间线，所有动作列表项都应该循环播放。
        // 不再依赖 action.loop，也不再只对白名单动作循环。
        // 这样自建动作、复制动作、导入动作、旧包里的动作都会在播放到最右端后回到 0。
        return true;
    }
}
