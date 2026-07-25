using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;

public class SkyPrisonAudioWorkshopPage : SkyPrisonEditorPageBase
{
    public const string DefaultAudioPackageFolder = "Assets/_Project/Audio/Packages";
    public const string DefaultRawAudioFolder = "Assets/_Project/Audio/Raw";
    public const string DefaultBakedAudioFolder = "Assets/_Project/Audio/Baked";

    private readonly Color leftBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color timelineBg = new Color(0.10f, 0.11f, 0.13f, 1f);
    private readonly Color mixerBg = new Color(0.11f, 0.115f, 0.13f, 1f);
    private readonly Color accentAudio = new Color(0.95f, 0.24f, 0.18f, 1f);
    private readonly Color selectedRow = new Color(0.75f, 0.18f, 0.14f, 0.34f);
    private readonly Color rowHover = new Color(1f, 1f, 1f, 0.05f);

    private const float RightInspectorWidth = 288f;
    // V2: 右侧属性面板内容可能远高于当前窗口。原先固定 980f 会把片段属性底部控件裁掉。
    private const float RightInspectorMinContentHeight = 2200f;
    private const float WorkspaceGap = 6f;
    private const float WorkspacePanelPadding = 6f;

    private const float FixedMixerHeight = 205f;
    private const float MinMixerHeight = 150f;
    private const float MaxMixerHeight = 225f;
    private const float MinTimelineHeight = 145f;

    private const float TimelineTrackHeaderWidth = 128f;
    private const float TimelineTrackRowMinHeight = 52f;
    private const float TimelineTrackRowMaxHeight = 180f;
    private const float TimelineTrackRowDefaultHeight = 64f;
    private const float TimelineTrackRowGap = 3f;

    private const string IconGoOriginPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_27.png";
    private const string IconPlayPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_26.png";
    private const string IconPausePath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_29.png";
    private const string IconGoEndPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_28.png";
    private const string IconStopPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_30.png";
    private const string IconCutPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_31.png";
    private const string IconMutePath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_32.png";
    private const string IconSoundOnPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_33.png";

    private const int TimelineTimecodeFrameRate = 30;

    private List<SkyPrisonAudioPackage> packages = new List<SkyPrisonAudioPackage>();
    private SkyPrisonAudioPackage selectedPackage;
    private SerializedObject selectedSO;

    private Vector2 packageListScroll;
    private Vector2 trackScroll;
    private Vector2 mixerScroll;
    private Vector2 inspectorScroll;
    private float masterVolume = 1f;
    private bool masterMute = false;
    private float masterMeterPreview = 0f;
    private float masterPeakHold = 0f;
    private double masterPeakHoldUntil = 0d;
    private string search = "";
    private float timelineTrackRowHeight = TimelineTrackRowDefaultHeight;
    private float lastTimelineLaneVisibleWidth = 0f;

    private struct SegmentRef
    {
        public int trackIndex;
        public int segmentIndex;

        public SegmentRef(int trackIndex, int segmentIndex)
        {
            this.trackIndex = trackIndex;
            this.segmentIndex = segmentIndex;
        }
    }

    private class SegmentClipboardData
    {
        public int relativeTrackIndex;
        public float relativeTimelineStart;
        public SkyPrisonAudioSegment segment;
    }

    private class SegmentDragOriginal
    {
        public int trackIndex;
        public int segmentIndex;
        public float timelineStart;
        public float sourceStart;
        public float sourceEnd;
    }

    private int selectedTrackIndex = -1;
    private int selectedSegmentIndex = -1;
    private int dragHoverTrackIndex = -1;
    private readonly List<SegmentRef> selectedSegments = new List<SegmentRef>();
    private readonly List<int> selectedTrackIndices = new List<int>();
    private readonly List<SegmentDragOriginal> segmentDragOriginals = new List<SegmentDragOriginal>();
    private static readonly List<SegmentClipboardData> segmentClipboard = new List<SegmentClipboardData>();
    private float playheadTime = 0f;

    private bool draggingTimeRange = false;
    private int timeRangeTrackIndex = -1;
    private float timeRangeAnchorTime = 0f;
    private float timeRangeStart = 0f;
    private float timeRangeEnd = 0f;

    private bool packageSettingsExpanded = true;
    private float packageSettingsHeight = 220f;
    private float timelineSectionHeight = 210f;
    private float mixerSectionHeight = 220f;
    private bool draggingPackageTimelineSplitter = false;
    private bool draggingMixerInspectorSplitter = false;

    private bool previewPlaying = false;
    private bool previewLoop = false;
    private double previewLastEditorTime = 0d;
    private double previewStartEditorTime = 0d;
    private float previewStartPlayhead = 0f;
    private int previewActiveTrackIndex = -1;
    private int previewActiveSegmentIndex = -1;
    private SkyPrisonAudioSegment previewActiveSegment = null;
    private readonly List<string> previewActiveSegmentKeys = new List<string>();
    private readonly List<PreviewAudioSourceEntry> previewAudioSourceEntries = new List<PreviewAudioSourceEntry>();
    private GameObject previewAudioRoot = null;

    private class PreviewAudioSourceEntry
    {
        public string key;
        public AudioSource source;
    }

    private class WaveformPreviewData
    {
        public float[] min;
        public float[] max;
    }

    private readonly Dictionary<AudioClip, WaveformPreviewData> waveformCache = new Dictionary<AudioClip, WaveformPreviewData>();

    private struct RuntimeLayerOption
    {
        public string key;
        public string label;

        public RuntimeLayerOption(string key, string label)
        {
            this.key = key;
            this.label = label;
        }
    }

    private static readonly RuntimeLayerOption[] RuntimeLayerOptions =
    {
        new RuntimeLayerOption("", "未指定"),
        new RuntimeLayerOption("base_impact", "基础冲击 / base_impact"),
        new RuntimeLayerOption("shoe_soft", "软鞋底 / shoe_soft"),
        new RuntimeLayerOption("shoe_metal", "金属鞋跟 / shoe_metal"),
        new RuntimeLayerOption("surface_stone", "石质地面 / surface_stone"),
        new RuntimeLayerOption("surface_metal", "金属地面 / surface_metal"),
        new RuntimeLayerOption("surface_wood", "木质地面 / surface_wood"),
        new RuntimeLayerOption("surface_grass", "草地摩擦 / surface_grass"),
        new RuntimeLayerOption("surface_water", "浅水水花 / surface_water"),
        new RuntimeLayerOption("surface_sand", "沙地颗粒 / surface_sand"),
        new RuntimeLayerOption("surface_mud", "泥地黏滞 / surface_mud"),
        new RuntimeLayerOption("gear_jingle", "装备轻响 / gear_jingle"),
        new RuntimeLayerOption("heavy_low_end", "重物低频 / heavy_low_end"),
        new RuntimeLayerOption("mechanical_servo", "机械伺服 / mechanical_servo"),
        new RuntimeLayerOption("cloth_rustle", "布料摩擦 / cloth_rustle"),
        new RuntimeLayerOption("custom", "自定义 / custom")
    };

    private bool showTimeline = true;
    private bool showMixer = true;

    private const float TimelineZoomMin = 4f;
    private const float TimelineZoomMax = 640f;
    private float timelinePixelsPerSecond = 120f;
    private bool timelineSnapEnabled = true;
    private float timelineSnapInterval = 0.05f;

    private enum SegmentDragMode
    {
        None,
        Move,
        ResizeLeft,
        ResizeRight
    }

    private SegmentDragMode segmentDragMode = SegmentDragMode.None;
    private int draggingTrackIndex = -1;
    private int draggingSegmentIndex = -1;
    private float dragStartMouseX = 0f;
    private float dragMouseGrabOffsetSeconds = 0f;
    private float dragOriginalTimelineStart = 0f;
    private float dragOriginalSourceStart = 0f;
    private float dragOriginalSourceEnd = 0f;
    private bool segmentDragFinalizing = false;

    public SkyPrisonAudioWorkshopPage(SkyPrisonEditorContext context) : base(context)
    {
    }

    public override string TabName => "音声合成";

    public override void OnEnable()
    {
        EnsureFolderExists(DefaultAudioPackageFolder);
        EnsureFolderExists(DefaultRawAudioFolder);
        EnsureFolderExists(DefaultBakedAudioFolder);
        EditorApplication.update -= OnEditorPreviewUpdate;
        EditorApplication.update += OnEditorPreviewUpdate;
        Refresh();
    }

    public override void OnDisable()
    {
        EditorApplication.update -= OnEditorPreviewUpdate;
        StopPreview();
    }

    public override void Refresh()
    {
        string selectedPath = selectedPackage != null ? AssetDatabase.GetAssetPath(selectedPackage) : "";

        string[] guids = AssetDatabase.FindAssets("t:SkyPrisonAudioPackage");
        packages = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<SkyPrisonAudioPackage>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => x.packageType.ToString())
            .ThenBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            SkyPrisonAudioPackage matched = packages.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                SelectPackage(matched);
        }

        if (selectedPackage == null && packages.Count > 0)
            SelectPackage(packages[0]);
    }

    public override void OnGUILeft()
    {
        DrawLeftPanel();
    }

    public override void OnGUIRight()
    {
        if (selectedPackage == null)
        {
            EditorGUILayout.HelpBox("请先在左侧创建或选择一个音声包。", MessageType.Info);
            DrawEmptyQuickStart();
            return;
        }

        EnsureSelectedSO();
        selectedPackage.EnsureValid();
        selectedSO.Update();

        DrawMainWorkspace();

        selectedSO.ApplyModifiedProperties();

        if (GUI.changed)
        {
            selectedPackage.EnsureValid();
            EditorUtility.SetDirty(selectedPackage);
        }
    }

    public void TrySelectObject(UnityEngine.Object obj)
    {
        SkyPrisonAudioPackage package = obj as SkyPrisonAudioPackage;
        if (package != null)
            SelectPackage(package);
    }

    private void DrawLeftPanel()
    {
        EditorGUILayout.LabelField("音声包列表", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(24f)))
            CreateNewPackage();

        using (new EditorGUI.DisabledScope(selectedPackage == null))
        {
            if (GUILayout.Button("-", GUILayout.Width(28f), GUILayout.Height(24f)))
                DeleteSelectedPackage();
        }

        Texture2D refreshIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Icon/Editor/SkyPrisonEditor_18.png");
        GUIContent refreshContent = refreshIcon != null ? new GUIContent(refreshIcon, "刷新") : new GUIContent("⟳", "刷新");
        if (GUILayout.Button(refreshContent, GUILayout.Width(28f), GUILayout.Height(24f)))
            Refresh();

        EditorGUILayout.EndHorizontal();

        search = EditorGUILayout.TextField(search);

        GUILayout.Space(6f);

        Rect container = EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(container, leftBg);
        packageListScroll = EditorGUILayout.BeginScrollView(packageListScroll);

        IEnumerable<SkyPrisonAudioPackage> filtered = packages;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                (x.displayName != null && x.displayName.ToLowerInvariant().Contains(keyword)) ||
                (x.packageKey != null && x.packageKey.ToLowerInvariant().Contains(keyword)) ||
                x.name.ToLowerInvariant().Contains(keyword) ||
                AssetDatabase.GetAssetPath(x).ToLowerInvariant().Contains(keyword));
        }

        SkyPrisonAudioPackageType? currentType = null;
        foreach (SkyPrisonAudioPackage package in filtered)
        {
            if (package == null)
                continue;

            if (currentType == null || currentType.Value != package.packageType)
            {
                currentType = package.packageType;
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(GetPackageTypeLabel(currentType.Value), EditorStyles.miniBoldLabel);
            }

            DrawPackageRow(package);
        }

        if (!filtered.Any())
            EditorGUILayout.LabelField("没有匹配的音声包", EditorStyles.miniLabel);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPackageRow(SkyPrisonAudioPackage package)
    {
        // V4: package rows must show both the human display name and the technical package key/asset name.
        // Footstep packages are referenced from UnitDefinition by package assets/keys; showing only the display name
        // made AP_Footwear_* packages hard to recognize and easy to mis-bind.
        Rect row = GUILayoutUtility.GetRect(1f, 42f, GUILayout.ExpandWidth(true));
        bool selected = selectedPackage == package;
        bool hover = row.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(row, selectedRow);
            EditorGUI.DrawRect(new Rect(row.x, row.y, 4f, row.height), accentAudio);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(row, rowHover);
        }

        string title = string.IsNullOrWhiteSpace(package.displayName) ? package.name : package.displayName;
        string key = string.IsNullOrWhiteSpace(package.packageKey) ? "<no key>" : package.packageKey;
        string assetName = package.name;
        string line2 = key;
        if (!string.Equals(assetName, key, StringComparison.OrdinalIgnoreCase) && !string.Equals(assetName, title, StringComparison.OrdinalIgnoreCase))
            line2 += "  ·  " + assetName;

        GUI.Label(new Rect(row.x + 10f, row.y + 3f, row.width - 20f, 18f), title, EditorStyles.label);

        GUIStyle mini = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(1f, 1f, 1f, selected ? 0.72f : 0.48f) }
        };
        GUI.Label(new Rect(row.x + 10f, row.y + 22f, row.width - 20f, 16f), line2, mini);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                SelectPackage(package);
                e.Use();
            }
            else if (e.button == 1)
            {
                SelectPackage(package);
                ShowPackageContextMenu(package);
                e.Use();
            }
        }
    }

    private void DrawMainWorkspace()
    {
        HandleTimelineEditHotkeys();

        Rect workspaceRect = GUILayoutUtility.GetRect(
            1f,
            1f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        float inspectorWidth = Mathf.Clamp(RightInspectorWidth, 248f, RightInspectorWidth);
        float minimumMainWidth = 720f;
        if (workspaceRect.width - inspectorWidth - WorkspaceGap < minimumMainWidth)
            inspectorWidth = Mathf.Clamp(workspaceRect.width - WorkspaceGap - minimumMainWidth, 248f, RightInspectorWidth);

        float mainWidth = Mathf.Max(360f, workspaceRect.width - inspectorWidth - WorkspaceGap);

        Rect mainRect = new Rect(
            workspaceRect.x,
            workspaceRect.y,
            mainWidth,
            workspaceRect.height);

        Rect inspectorRect = new Rect(
            mainRect.xMax + WorkspaceGap,
            workspaceRect.y,
            Mathf.Max(248f, workspaceRect.xMax - (mainRect.xMax + WorkspaceGap)),
            workspaceRect.height);

        if (inspectorRect.xMax > workspaceRect.xMax)
        {
            inspectorRect.width = Mathf.Max(248f, workspaceRect.xMax - inspectorRect.x);
        }

        DrawWorkspaceMainPanel(mainRect);
        DrawWorkspaceInspectorPanel(inspectorRect);
    }

    private void DrawWorkspaceMainPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.105f, 0.11f, 0.125f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.05f));

        Rect inner = new Rect(
            rect.x + WorkspacePanelPadding,
            rect.y + WorkspacePanelPadding,
            Mathf.Max(10f, rect.width - WorkspacePanelPadding * 2f),
            Mathf.Max(10f, rect.height - WorkspacePanelPadding * 2f));

        float splitterHeight = 5f;
        float availableHeight = Mathf.Max(160f, inner.height);

        mixerSectionHeight = Mathf.Clamp(
            FixedMixerHeight,
            MinMixerHeight,
            Mathf.Min(MaxMixerHeight, availableHeight - MinTimelineHeight - splitterHeight));

        if (availableHeight < MinTimelineHeight + MinMixerHeight + splitterHeight)
            mixerSectionHeight = Mathf.Max(80f, availableHeight * 0.32f);

        timelineSectionHeight = Mathf.Max(80f, availableHeight - mixerSectionHeight - splitterHeight);

        Rect timelineRect = new Rect(inner.x, inner.y, inner.width, timelineSectionHeight);
        Rect dividerRect = new Rect(inner.x, timelineRect.yMax, inner.width, splitterHeight);
        Rect mixerRect = new Rect(inner.x, dividerRect.yMax, inner.width, Mathf.Max(60f, inner.yMax - dividerRect.yMax));

        DrawTimelineSection(timelineRect);
        DrawLockedHorizontalDivider(dividerRect);
        DrawMixerSection(mixerRect);
    }

    private void DrawWorkspaceInspectorPanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.17f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect inner = new Rect(
            rect.x + 6f,
            rect.y + 6f,
            rect.width - 12f,
            rect.height - 12f);

        inspectorScroll.x = 0f;

        GUI.BeginGroup(inner);

        Rect viewRect = new Rect(0f, 0f, inner.width, inner.height);
        float contentWidth = Mathf.Max(10f, inner.width - 12f);

        // V2: 不再用 980f 作为右侧属性区的硬上限。
        // 片段属性、空间音频、AI 噪声参数等字段叠加后会超过 980f；
        // contentRect 太短时，GUI.BeginScrollView 会把底部控件直接裁掉，看起来像“容器长度不够”。
        float contentHeight = Mathf.Max(inner.height + 1f, RightInspectorMinContentHeight);
        Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

        inspectorScroll = GUI.BeginScrollView(viewRect, inspectorScroll, contentRect, false, true);

        GUILayout.BeginArea(new Rect(0f, 0f, contentWidth, contentHeight));

        EditorGUILayout.LabelField("音声包设置", EditorStyles.boldLabel);
        DrawPackageInspector();

        GUILayout.Space(10f);
        DrawSelectionInspectorBody();

        GUILayout.EndArea();

        GUI.EndScrollView();
        GUI.EndGroup();
    }

    private void DrawLockedHorizontalDivider()
    {
        Rect rect = GUILayoutUtility.GetRect(1f, 5f, GUILayout.ExpandWidth(true));
        DrawLockedHorizontalDivider(rect);
    }

    private void DrawLockedHorizontalDivider(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f, 1f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, rect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
    }

    private void DrawHorizontalSplitter(ref bool draggingFlag, Action<float> onDrag)
    {
        Rect rect = GUILayoutUtility.GetRect(1f, 5f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f, 1f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, rect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0)
        {
            draggingFlag = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingFlag)
        {
            onDrag?.Invoke(e.delta.y);
            Context.Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingFlag)
        {
            draggingFlag = false;
            e.Use();
        }
    }

    private void DrawPackageHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("SkyPrison Audio Workshop", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("音声包不直接绑定死原素材，而是通过音轨、片段与调音台生成可复用的游戏音声包。", EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加音轨", GUILayout.Width(100f)))
            AddTrack();

        using (new EditorGUI.DisabledScope(selectedTrackIndex < 0 || selectedTrackIndex >= selectedPackage.tracks.Count))
        {
            if (GUILayout.Button("删除音轨", GUILayout.Width(100f)))
                RemoveSelectedTrack();

            if (GUILayout.Button("添加片段", GUILayout.Width(100f)))
                AddSegmentToSelectedTrack();

            if (GUILayout.Button("竖线切割", GUILayout.Width(100f)))
                SplitSelectedSegmentsAtPlayhead();
        }

        using (new EditorGUI.DisabledScope(selectedSegments.Count == 0))
        {
            if (GUILayout.Button("复制片段", GUILayout.Width(80f)))
                CopySelectedSegments();

            if (GUILayout.Button("剪切片段", GUILayout.Width(80f)))
                CutSelectedSegments();

            if (GUILayout.Button("删除片段", GUILayout.Width(80f)))
                DeleteSelectedSegments();
        }

        using (new EditorGUI.DisabledScope(segmentClipboard.Count == 0))
        {
            if (GUILayout.Button("粘贴片段", GUILayout.Width(80f)))
                PasteSegmentsAtPlayhead();
        }

        GUILayout.FlexibleSpace();

        DrawTransportButtons();

        GUILayout.Space(10f);

        if (GUILayout.Button("定位资产", GUILayout.Width(100f)))
        {
            Selection.activeObject = selectedPackage;
            EditorGUIUtility.PingObject(selectedPackage);
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawTransportButtons()
    {
        if (GUILayout.Button("回到 0:00", GUILayout.Width(76f)))
            SetPlayheadAndStopAudio(0f);

        if (GUILayout.Button(previewPlaying ? "暂停" : "播放", GUILayout.Width(56f)))
            TogglePreviewPlayback();

        if (GUILayout.Button("跳到竖线", GUILayout.Width(76f)))
            JumpPreviewToPlayhead();

        GUILayout.Label("竖线 " + FormatTime(playheadTime), EditorStyles.miniLabel, GUILayout.Width(90f));
    }

    private void DrawTimelineTransportAndLengthBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.135f, 0.15f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.05f));

        float y = rect.y + 3f;
        float x = rect.x + 6f;
        const float buttonSize = 26f;
        const float buttonHeight = 22f;
        const float gap = 4f;

        if (DrawIconToolbarButton(new Rect(x, y, buttonSize, buttonHeight), IconGoOriginPath, "回到原点"))
            SetPlayheadAndStopAudio(0f);
        x += buttonSize + gap;

        if (DrawIconToolbarButton(new Rect(x, y, buttonSize, buttonHeight), previewPlaying ? IconPausePath : IconPlayPath, previewPlaying ? "暂停" : "播放"))
            TogglePreviewPlayback();
        x += buttonSize + gap;

        if (DrawIconToolbarButton(new Rect(x, y, buttonSize, buttonHeight), IconGoEndPath, "跳到音轨末"))
            SetPlayheadAndStopAudio(GetTimelineTotalLengthSeconds());
        x += buttonSize + gap;

        if (DrawIconToolbarButton(new Rect(x, y, buttonSize, buttonHeight), IconStopPath, "结束播放"))
        {
            StopPreview();
            SetPlayheadAndStopAudio(0f);
        }
        x += buttonSize + gap;

        using (new EditorGUI.DisabledScope(previewPlaying))
        {
            if (DrawIconToolbarButton(new Rect(x, y, buttonSize, buttonHeight), IconCutPath, "按红色竖线剪切片段"))
                SplitSegmentsCrossingPlayhead();
        }
        x += buttonSize + 10f;

        GUI.Label(new Rect(x, y + 2f, 28f, 18f), "循环", EditorStyles.miniLabel);
        x += 30f;

        previewLoop = DrawSliderSwitch(new Rect(x, y + 1f, 38f, 20f), previewLoop, "开启后，播放头到达音轨末尾会自动回到 0:00 继续播放。");
        x += 46f;

        GUI.Label(new Rect(x, y + 2f, 44f, 18f), "总长：", EditorStyles.label);
        x += 46f;

        float outputButtonWidth = 50f;
        Rect outputButtonRect = new Rect(rect.xMax - outputButtonWidth - 8f, y, outputButtonWidth, buttonHeight);
        if (GUI.Button(outputButtonRect, "输出", EditorStyles.toolbarButton))
            ShowExportAudioMenu();

        float fieldRight = outputButtonRect.x - 8f;
        float fieldWidth = Mathf.Clamp(fieldRight - x, 120f, 190f);
        Rect fieldRect = new Rect(x, y, fieldWidth, buttonHeight);

        string current = FormatTimecode(GetTimelineTotalLengthSeconds());

        // V3: give this text field a package-specific control name.
        // Unity keeps the active text editor value by control id while a TextField is focused;
        // when switching packages from the left list, the old total-length text could visually remain
        // until focus changed. A package-specific control name plus focus reset in SelectPackage
        // makes the field immediately show the newly selected package value.
        GUI.SetNextControlName(selectedPackage != null
            ? "AudioTimelineLength_" + selectedPackage.GetInstanceID()
            : "AudioTimelineLength_None");

        EditorGUI.BeginChangeCheck();
        string next = EditorGUI.TextField(fieldRect, current);
        if (EditorGUI.EndChangeCheck())
        {
            if (TryParseTimecode(next, out float seconds))
            {
                float oldLength = GetTimelineTotalLengthSeconds();
                selectedPackage.timelineLengthSeconds = Mathf.Max(0.1f, seconds);
                selectedPackage.EnsureValid();
                EditorUtility.SetDirty(selectedPackage);
                HandleTimelineLengthChanged(oldLength, GetTimelineTotalLengthSeconds());
                Context.Repaint();
            }
        }
    }

    private bool DrawIconToolbarButton(string iconPath, string tooltip)
    {
        Texture2D icon = LoadEditorIcon(iconPath);
        GUIContent content = icon != null ? new GUIContent(icon, tooltip) : new GUIContent("■", tooltip);
        return GUILayout.Button(content, EditorStyles.toolbarButton, GUILayout.Width(26f), GUILayout.Height(22f));
    }

    private bool DrawIconToolbarButton(Rect rect, string iconPath, string tooltip)
    {
        Texture2D icon = LoadEditorIcon(iconPath);
        GUIContent content = icon != null ? new GUIContent(icon, tooltip) : new GUIContent("■", tooltip);
        return GUI.Button(rect, content, EditorStyles.toolbarButton);
    }

    private bool DrawSliderSwitch(Rect rect, bool value, string tooltip)
    {
        Event e = Event.current;
        bool hover = e != null && rect.Contains(e.mousePosition);
        Color bg = value
            ? new Color(0.34f, 0.36f, 0.46f, 1f)
            : new Color(0.25f, 0.25f, 0.29f, 1f);

        if (hover)
            bg = Color.Lerp(bg, Color.white, 0.08f);

        EditorGUI.DrawRect(rect, bg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.16f : 0.06f));

        float knobSize = Mathf.Max(12f, rect.height - 4f);
        float knobX = value ? rect.xMax - knobSize - 2f : rect.x + 2f;
        Rect knobRect = new Rect(knobX, rect.y + 2f, knobSize, knobSize);
        EditorGUI.DrawRect(knobRect, value ? new Color(0.74f, 0.80f, 1f, 1f) : Color.white);

        GUI.Label(rect, new GUIContent("", tooltip));

        if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            value = !value;
            GUI.changed = true;
            e.Use();
            Context.Repaint();
        }

        return value;
    }

    private bool DrawSliderSwitch(bool value, string tooltip, params GUILayoutOption[] options)
    {
        Rect rect = GUILayoutUtility.GetRect(42f, 18f, options);
        Event e = Event.current;

        bool hover = e != null && rect.Contains(e.mousePosition);
        Color bg = value
            ? new Color(0.34f, 0.36f, 0.46f, 1f)
            : new Color(0.25f, 0.25f, 0.29f, 1f);
        if (hover)
            bg = Color.Lerp(bg, Color.white, 0.08f);

        EditorGUI.DrawRect(rect, bg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.16f : 0.06f));

        float knobSize = Mathf.Max(12f, rect.height - 4f);
        float knobX = value ? rect.xMax - knobSize - 2f : rect.x + 2f;
        Rect knobRect = new Rect(knobX, rect.y + 2f, knobSize, knobSize);
        EditorGUI.DrawRect(knobRect, value ? new Color(0.74f, 0.80f, 1f, 1f) : Color.white);

        GUI.Label(rect, new GUIContent("", tooltip));

        if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            value = !value;
            GUI.changed = true;
            e.Use();
            Context.Repaint();
        }

        return value;
    }

    private Texture2D LoadEditorIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }


    private void DrawSelectedPackageAssetIdentityBlock()
    {
        if (selectedPackage == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedPackage);
        string assetName = selectedPackage.name;
        string key = string.IsNullOrWhiteSpace(selectedPackage.packageKey) ? "<no key>" : selectedPackage.packageKey;

        Rect rect = EditorGUILayout.BeginVertical("box");
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.085f, 0.095f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.08f));

        EditorGUILayout.LabelField("资产定位", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("资产名", assetName);
        EditorGUILayout.LabelField("包 Key", key);
        EditorGUILayout.LabelField("路径", string.IsNullOrEmpty(path) ? "<unsaved>" : path, EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(selectedPackage == null))
        {
            if (GUILayout.Button("定位资产"))
            {
                Selection.activeObject = selectedPackage;
                EditorGUIUtility.PingObject(selectedPackage);
            }

            if (GUILayout.Button("同步改名", GUILayout.Width(82f)))
                SyncSelectedPackageAssetNameWithIdentity();

            if (GUILayout.Button("复制 Key", GUILayout.Width(76f)))
                EditorGUIUtility.systemCopyBuffer = key;

            if (GUILayout.Button("复制路径", GUILayout.Width(76f)))
                EditorGUIUtility.systemCopyBuffer = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        GUILayout.Space(4f);
    }


    // V5: explicit package rename sync.
    // The audio workshop has three names with different purposes:
    // - displayName: user-facing name shown in the list.
    // - packageKey: runtime/reference key used by UnitDefinition and footstep binding.
    // - asset name: Unity asset filename shown in Project.
    // Changing displayName/packageKey should not silently rename assets while the user types.
    // This button performs the sync deliberately and safely.
    private void SyncSelectedPackageAssetNameWithIdentity()
    {
        if (selectedPackage == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedPackage);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("同步改名", "当前音声包还没有保存为资产，无法同步资产名。", "OK");
            return;
        }

        string rawName = !string.IsNullOrWhiteSpace(selectedPackage.packageKey)
            ? selectedPackage.packageKey
            : (!string.IsNullOrWhiteSpace(selectedPackage.displayName) ? selectedPackage.displayName : selectedPackage.name);

        string sanitized = SanitizeFileName(rawName);
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "AudioPackage";

        // Keep a clear asset naming convention while using packageKey as the identity source.
        string desiredAssetName = sanitized.StartsWith("SAP_", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : "SAP_" + sanitized;

        string currentAssetName = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(currentAssetName, desiredAssetName, StringComparison.Ordinal))
        {
            EditorUtility.DisplayDialog("同步改名", "资产名已经和当前包 Key / 显示名同步。", "OK");
            return;
        }

        string folder = Path.GetDirectoryName(path);
        string desiredPath = (string.IsNullOrEmpty(folder) ? "" : folder.Replace('\\', '/') + "/") + desiredAssetName + ".asset";
        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
        string finalAssetName = Path.GetFileNameWithoutExtension(uniquePath);

        Undo.RecordObject(selectedPackage, "Sync Audio Package Asset Name");
        selectedPackage.EnsureValid();
        EditorUtility.SetDirty(selectedPackage);

        string error = AssetDatabase.RenameAsset(path, finalAssetName);
        if (!string.IsNullOrEmpty(error))
        {
            EditorUtility.DisplayDialog("同步改名失败", error, "OK");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string newPath = (string.IsNullOrEmpty(folder) ? "" : folder.Replace('\\', '/') + "/") + finalAssetName + ".asset";
        SkyPrisonAudioPackage reloaded = AssetDatabase.LoadAssetAtPath<SkyPrisonAudioPackage>(newPath);
        Refresh();
        if (reloaded != null)
            SelectPackage(reloaded);

        Selection.activeObject = reloaded != null ? reloaded : selectedPackage;
        if (Selection.activeObject != null)
            EditorGUIUtility.PingObject(Selection.activeObject);

        Context.Repaint();
    }

    private void DrawPackageInspector()
    {
        EditorGUILayout.BeginVertical("box");

        DrawSelectedPackageAssetIdentityBlock();

        DrawProperty("包 Key", "packageKey");
        DrawProperty("显示名", "displayName");
        DrawProperty("类型", "packageType");
        DrawProperty("播放模式", "playMode");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("全局播放参数", EditorStyles.miniBoldLabel);
        DrawProperty("主音量", "masterVolume");
        EditorGUILayout.LabelField("说明", "包主音量会影响工作台预览、导出与运行时播放。下方调音台 MASTER 是编辑器预览总线。", EditorStyles.wordWrappedMiniLabel);
        DrawProperty("随机音量", "randomVolumeRange");
        DrawProperty("随机音高", "randomPitchRange");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("空间音频", EditorStyles.miniBoldLabel);
        DrawProperty("3D 空间音频", "spatial3D");
        DrawProperty("最小距离", "minDistance");
        DrawProperty("最大距离", "maxDistance");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("AI 噪声参数", EditorStyles.miniBoldLabel);
        DrawProperty("噪声半径", "noiseRadius");
        DrawProperty("噪声强度", "noiseStrength");

        EditorGUILayout.EndVertical();
    }

    private void DrawTimelineSection(float height)
    {
        Rect rect = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
        DrawTimelineSection(rect);
    }

    private void DrawTimelineSection(Rect sectionRect)
    {
        const float toolbarHeight = 24f;
        const float transportHeight = 28f;
        Rect toolbarRect = new Rect(sectionRect.x, sectionRect.y, sectionRect.width, toolbarHeight);
        Rect transportRect = new Rect(sectionRect.x, toolbarRect.yMax, sectionRect.width, transportHeight);
        Rect rect = new Rect(sectionRect.x, transportRect.yMax, sectionRect.width, Mathf.Max(40f, sectionRect.height - toolbarHeight - transportHeight));

        GUILayout.BeginArea(toolbarRect);
        DrawTimelineToolbar();
        GUILayout.EndArea();

        DrawTimelineTransportAndLengthBar(transportRect);

        EditorGUI.DrawRect(rect, timelineBg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));
        HandleTimelineMouseWheel(rect);

        Rect headerRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 22f);
        DrawTimelineRuler(headerRect);

        Rect contentRect = new Rect(rect.x + 8f, rect.y + 31f, rect.width - 16f, rect.height - 38f);
        Rect frozenHeaderRect = new Rect(contentRect.x, contentRect.y, TimelineTrackHeaderWidth, contentRect.height);
        Rect laneScrollRect = new Rect(
            frozenHeaderRect.xMax,
            contentRect.y,
            Mathf.Max(40f, contentRect.width - TimelineTrackHeaderWidth),
            contentRect.height);

        lastTimelineLaneVisibleWidth = Mathf.Max(1f, laneScrollRect.width - 18f);

        EditorGUI.DrawRect(frozenHeaderRect, new Color(0.12f, 0.13f, 0.15f, 1f));
        DrawThinBorder(frozenHeaderRect, new Color(1f, 1f, 1f, 0.05f));

        float timelineWidth = Mathf.Max(laneScrollRect.width - 16f, CalculateTimelineLengthSeconds() * timelinePixelsPerSecond + 140f);
        float contentHeight = Mathf.Max(laneScrollRect.height, selectedPackage.tracks.Count * GetTimelineTrackRowStep());
        Rect laneViewRect = new Rect(0f, 0f, timelineWidth, contentHeight);

        trackScroll = GUI.BeginScrollView(laneScrollRect, trackScroll, laneViewRect, true, true);

        for (int i = 0; i < selectedPackage.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[i];
            Rect row = new Rect(-TimelineTrackHeaderWidth, i * GetTimelineTrackRowStep(), laneViewRect.width + TimelineTrackHeaderWidth, timelineTrackRowHeight);
            DrawTrackRow(row, track, i);
        }

        DrawTimelineLockedArea(laneViewRect);
        DrawGlobalTimeRangeSelection(laneViewRect);
        DrawPlayheadLineInTimeline(laneViewRect);

        // 片段拖拽必须在 BeginScrollView 的坐标系内处理。
        // 否则 MouseDown 使用的是内容坐标，MouseDrag/MouseUp 却会落回窗口坐标，导致片段永远跟在鼠标后面。
        HandleSegmentDrag(Event.current);

        if (selectedPackage.tracks.Count == 0)
            GUI.Label(new Rect(8f, 12f, laneViewRect.width - 16f, 22f), "还没有音轨。点击“添加音轨”开始。", EditorStyles.miniLabel);

        GUI.EndScrollView();

        DrawFrozenTrackHeaders(frozenHeaderRect);
    }

    private float GetTimelineTrackRowStep()
    {
        return timelineTrackRowHeight + TimelineTrackRowGap;
    }

    private void HandleTimelineMouseWheel(Rect timelineRect)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.ScrollWheel || !timelineRect.Contains(e.mousePosition))
            return;

        if (e.control)
        {
            float oldHeight = timelineTrackRowHeight;
            float delta = e.delta.y > 0f ? -8f : 8f;
            timelineTrackRowHeight = Mathf.Clamp(timelineTrackRowHeight + delta, TimelineTrackRowMinHeight, TimelineTrackRowMaxHeight);

            if (!Mathf.Approximately(oldHeight, timelineTrackRowHeight))
            {
                float mouseLocalY = Mathf.Max(0f, e.mousePosition.y - timelineRect.y - 31f);
                float focusTrack = (trackScroll.y + mouseLocalY) / Mathf.Max(1f, oldHeight + TimelineTrackRowGap);
                trackScroll.y = Mathf.Max(0f, focusTrack * GetTimelineTrackRowStep() - mouseLocalY);
            }

            Context.Repaint();
            e.Use();
            return;
        }

        float oldPixelsPerSecond = timelinePixelsPerSecond;
        float mouseLocalX = Mathf.Max(0f, e.mousePosition.x - timelineRect.x - 8f - TimelineTrackHeaderWidth);
        float focusSecond = (trackScroll.x + mouseLocalX) / Mathf.Max(1f, oldPixelsPerSecond);

        float zoomFactor = e.delta.y > 0f ? 0.72f : 1.38f;
        timelinePixelsPerSecond = Mathf.Clamp(timelinePixelsPerSecond * zoomFactor, TimelineZoomMin, TimelineZoomMax);

        float newScrollX = focusSecond * timelinePixelsPerSecond - mouseLocalX;
        trackScroll.x = Mathf.Max(0f, newScrollX);

        Context.Repaint();
        e.Use();
    }

    private void DrawTimelineLockedArea(Rect viewRect)
    {
        float hardEnd = GetTimelineTotalLengthSeconds();
        float x = hardEnd * timelinePixelsPerSecond;
        if (x >= viewRect.xMax)
            return;

        Rect lockedRect = new Rect(Mathf.Max(0f, x), 0f, viewRect.xMax - Mathf.Max(0f, x), viewRect.height);
        if (lockedRect.width <= 0f)
            return;

        // 音轨总长之后是不可编辑区域：用发白遮罩表现“已经超出工程时间线”。
        EditorGUI.DrawRect(lockedRect, new Color(1f, 1f, 1f, 0.11f));
        EditorGUI.DrawRect(new Rect(lockedRect.x, lockedRect.y, 2f, lockedRect.height), new Color(1f, 1f, 1f, 0.42f));
    }

    private void DrawTimelineRulerLockedArea(Rect rect)
    {
        float laneStart = rect.x + TimelineTrackHeaderWidth - trackScroll.x;
        float hardEndX = laneStart + GetTimelineTotalLengthSeconds() * timelinePixelsPerSecond;
        float lockStart = Mathf.Max(rect.x + TimelineTrackHeaderWidth, hardEndX);
        if (lockStart >= rect.xMax)
            return;

        Rect lockedRect = new Rect(lockStart, rect.y, rect.xMax - lockStart, rect.height);
        EditorGUI.DrawRect(lockedRect, new Color(1f, 1f, 1f, 0.12f));
        EditorGUI.DrawRect(new Rect(lockedRect.x, lockedRect.y, 2f, lockedRect.height), new Color(1f, 1f, 1f, 0.45f));
    }

    private void DrawPlayheadLineInTimeline(Rect viewRect)
    {
        float x = playheadTime * timelinePixelsPerSecond;
        if (x < 0f || x > viewRect.xMax)
            return;

        EditorGUI.DrawRect(new Rect(x - 1f, 0f, 2f, viewRect.height), accentAudio);
    }

    private void SetPlayheadFromMouseX(float mouseX, float laneStartX)
    {
        playheadTime = Mathf.Clamp(SnapIfNeeded((trackScroll.x + mouseX - laneStartX) / Mathf.Max(1f, timelinePixelsPerSecond)), 0f, GetTimelineTotalLengthSeconds());
        ResyncPreviewAfterManualPlayheadJump();
        Context.Repaint();
    }

    private void DrawTimelineToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("时间线", GUILayout.Width(48f));
        GUILayout.Label("缩放", GUILayout.Width(32f));
        timelinePixelsPerSecond = GUILayout.HorizontalSlider(timelinePixelsPerSecond, TimelineZoomMin, TimelineZoomMax, GUILayout.Width(120f));
        GUILayout.Label(timelinePixelsPerSecond.ToString("0") + " px/s", GUILayout.Width(64f));

        GUILayout.Space(12f);
        timelineSnapEnabled = GUILayout.Toggle(timelineSnapEnabled, "吸附", EditorStyles.toolbarButton, GUILayout.Width(48f));

        using (new EditorGUI.DisabledScope(!timelineSnapEnabled))
        {
            GUILayout.Label("网格", GUILayout.Width(32f));
            string[] labels = { "0.01s", "0.05s", "0.10s", "0.25s", "0.50s", "1.00s" };
            float[] values = { 0.01f, 0.05f, 0.10f, 0.25f, 0.50f, 1.00f };
            int current = 1;
            for (int i = 0; i < values.Length; i++)
            {
                if (Mathf.Approximately(timelineSnapInterval, values[i]))
                {
                    current = i;
                    break;
                }
            }

            int next = EditorGUILayout.Popup(current, labels, GUILayout.Width(80f));
            timelineSnapInterval = values[Mathf.Clamp(next, 0, values.Length - 1)];
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("滚轮大幅缩放时间线；Ctrl+滚轮调整单轨高度；网格会随缩放自动分层。", EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();
    }

    private void HandleTimelineLengthChanged(float oldLength, float newLength)
    {
        newLength = Mathf.Max(0.1f, newLength);

        playheadTime = Mathf.Clamp(playheadTime, 0f, newLength);
        timeRangeStart = Mathf.Clamp(timeRangeStart, 0f, newLength);
        timeRangeEnd = Mathf.Clamp(timeRangeEnd, 0f, newLength);
        timeRangeAnchorTime = Mathf.Clamp(timeRangeAnchorTime, 0f, newLength);

        if (trackScroll.x > newLength * timelinePixelsPerSecond)
            trackScroll.x = Mathf.Max(0f, newLength * timelinePixelsPerSecond - lastTimelineLaneVisibleWidth * 0.5f);

        // 播放中修改总长时，显示层会立刻截断，但旧 AudioSource 不会自动跟着新边界停止。
        // 所以这里强制按新总长重启预览状态。
        if (previewPlaying)
        {
            if (playheadTime >= newLength - 0.001f)
            {
                if (previewLoop)
                {
                    playheadTime = 0f;
                    ResyncPreviewAfterManualPlayheadJump();
                }
                else
                {
                    StopPreview();
                    playheadTime = 0f;
                }
            }
            else
            {
                ResyncPreviewAfterManualPlayheadJump();
            }
        }

        ResetMixerMeters();
        KeepPlayheadVisibleAfterManualJump();
    }

    private float CalculateTimelineLengthSeconds()
    {
        if (selectedPackage == null)
            return 4f;

        // 音轨总长是时间线的硬边界。
        // 片段超过这里时，只允许显示 / 播放到这条边界为止，避免“被拉短后隐藏部分仍然可播放”。
        return Mathf.Max(4f, selectedPackage.timelineLengthSeconds);
    }

    private float GetTimelineTotalLengthSeconds()
    {
        if (selectedPackage == null)
            return CalculateTimelineLengthSeconds();

        return Mathf.Max(0.1f, selectedPackage.timelineLengthSeconds);
    }

    private string FormatTimecode(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int totalFrames = Mathf.RoundToInt(seconds * TimelineTimecodeFrameRate);
        int frames = totalFrames % TimelineTimecodeFrameRate;
        int totalSeconds = totalFrames / TimelineTimecodeFrameRate;
        int sec = totalSeconds % 60;
        int min = (totalSeconds / 60) % 60;
        int hour = totalSeconds / 3600;
        return $"{hour:00}:{min:00}:{sec:00}:{frames:00}";
    }

    private bool TryParseTimecode(string text, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = text.Trim().Replace('：', ':');
        string[] parts = normalized.Split(':');
        if (parts.Length != 4)
            return false;

        if (!int.TryParse(parts[0], out int h)) return false;
        if (!int.TryParse(parts[1], out int m)) return false;
        if (!int.TryParse(parts[2], out int s)) return false;
        if (!int.TryParse(parts[3], out int f)) return false;

        h = Mathf.Max(0, h);
        m = Mathf.Clamp(m, 0, 59);
        s = Mathf.Clamp(s, 0, 59);
        f = Mathf.Clamp(f, 0, TimelineTimecodeFrameRate - 1);
        seconds = h * 3600f + m * 60f + s + f / (float)TimelineTimecodeFrameRate;
        return true;
    }

    private void DrawTimelineRuler(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.16f, 0.18f, 1f));
        DrawTimelineRulerLockedArea(rect);

        GUI.Label(new Rect(rect.x + 4f, rect.y + 3f, 80f, 18f), "Track");

        Rect addTrackButtonRect = new Rect(rect.x + TimelineTrackHeaderWidth - 24f, rect.y + 2f, 20f, 18f);
        if (GUI.Button(addTrackButtonRect, new GUIContent("+", "添加音轨"), EditorStyles.miniButton))
        {
            AddTrack();
            Event.current?.Use();
        }

        HandleGlobalTimeRangeSelectionFromRuler(rect);

        float laneStart = rect.x + TimelineTrackHeaderWidth - trackScroll.x;
        float visibleStartSecond = Mathf.Max(0f, trackScroll.x / Mathf.Max(1f, timelinePixelsPerSecond));
        float visibleEndSecond = visibleStartSecond + Mathf.Max(1f, rect.width - TimelineTrackHeaderWidth) / Mathf.Max(1f, timelinePixelsPerSecond);

        GetTimelineVisualGridSteps(out float minorStep, out float majorStep);
        int firstMinorTick = Mathf.FloorToInt(visibleStartSecond / minorStep);
        int maxMinorTicks = Mathf.CeilToInt((visibleEndSecond - visibleStartSecond) / minorStep) + 4;

        for (int i = firstMinorTick; i <= firstMinorTick + maxMinorTicks; i++)
        {
            float time = i * minorStep;
            if (time < 0f || time > visibleEndSecond + minorStep)
                continue;

            float x = laneStart + time * timelinePixelsPerSecond;
            if (x < rect.x + TimelineTrackHeaderWidth - 15f || x > rect.xMax)
                continue;

            bool major = IsGridMajorTick(time, majorStep);
            Color lineColor = major ? new Color(1f, 1f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0.07f);
            float lineWidth = major ? 2f : 1f;
            EditorGUI.DrawRect(new Rect(x, rect.y, lineWidth, rect.height), lineColor);

            if (major)
            {
                const float labelWidth = 90f;
                float labelX = Mathf.Clamp(
                    x + 4f,
                    rect.x + TimelineTrackHeaderWidth + 4f,
                    rect.xMax - labelWidth - 4f);

                GUI.Label(new Rect(labelX, rect.y + 3f, labelWidth, 18f), FormatTime(time), EditorStyles.miniLabel);
            }
        }

        float playheadX = laneStart + playheadTime * timelinePixelsPerSecond;
        if (playheadX >= rect.x + TimelineTrackHeaderWidth - 15f && playheadX <= rect.xMax)
        {
            EditorGUI.DrawRect(new Rect(playheadX - 1f, rect.y, 2f, rect.height), accentAudio);
            GUI.Label(new Rect(playheadX + 4f, rect.y + 3f, 80f, 18f), FormatTime(playheadTime), EditorStyles.miniBoldLabel);
        }

        Event e = Event.current;
        if (e != null && e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0)
        {
            SetPlayheadFromMouseX(e.mousePosition.x, rect.x + TimelineTrackHeaderWidth);
            e.Use();
        }
    }

    private float ChooseRulerStep()
    {
        GetTimelineVisualGridSteps(out _, out float majorStep);
        return majorStep;
    }

    private void GetTimelineVisualGridSteps(out float minorStep, out float majorStep)
    {
        // 低缩放时把旧的小格合并成大单位，避免 5 分钟视图变成灰色竖纹。
        if (timelinePixelsPerSecond < 8f)
        {
            minorStep = 5f;
            majorStep = 30f;
        }
        else if (timelinePixelsPerSecond < 20f)
        {
            minorStep = 1f;
            majorStep = 10f;
        }
        else if (timelinePixelsPerSecond < 60f)
        {
            minorStep = 1f;
            majorStep = 5f;
        }
        else if (timelinePixelsPerSecond < 160f)
        {
            minorStep = 0.25f;
            majorStep = 1f;
        }
        else if (timelinePixelsPerSecond < 320f)
        {
            minorStep = 0.10f;
            majorStep = 0.50f;
        }
        else
        {
            minorStep = 0.05f;
            majorStep = 0.25f;
        }
    }

    private bool IsGridMajorTick(float time, float majorStep)
    {
        if (majorStep <= 0f)
            return false;

        float nearest = Mathf.Round(time / majorStep) * majorStep;
        return Mathf.Abs(time - nearest) <= 0.0005f;
    }

    private bool IsTrackSelected(int trackIndex)
    {
        return selectedTrackIndices.Contains(trackIndex);
    }

    private void SelectTrack(int trackIndex, bool additive)
    {
        if (trackIndex < 0 || selectedPackage == null || selectedPackage.tracks == null || trackIndex >= selectedPackage.tracks.Count)
            return;

        selectedSegments.Clear();
        selectedSegmentIndex = -1;
        timeRangeTrackIndex = -1;
        draggingTimeRange = false;

        if (!additive)
        {
            selectedTrackIndices.Clear();
            selectedTrackIndices.Add(trackIndex);
        }
        else
        {
            if (selectedTrackIndices.Contains(trackIndex))
                selectedTrackIndices.Remove(trackIndex);
            else
                selectedTrackIndices.Add(trackIndex);

            if (selectedTrackIndices.Count == 0)
                selectedTrackIndices.Add(trackIndex);
        }

        selectedTrackIndex = trackIndex;
        GUI.FocusControl(null);
        Context.Repaint();
    }

    private void DrawTrackRow(Rect row, SkyPrisonAudioTrack track, int index)
    {
        if (track == null)
            return;

        bool selected = (selectedTrackIndex == index || IsTrackSelected(index)) && selectedSegmentIndex < 0;
        EditorGUI.DrawRect(row, selected ? new Color(0.36f, 0.13f, 0.11f, 1f) : new Color(0.13f, 0.14f, 0.16f, 1f));
        EditorGUI.DrawRect(new Rect(row.x, row.y, 4f, row.height), track.color);

        Rect header = new Rect(row.x + 8f, row.y + 5f, TimelineTrackHeaderWidth - 14f, row.height - 10f);
        GUI.Label(new Rect(header.x, header.y, header.width, 18f), track.displayName, EditorStyles.boldLabel);

        float toggleY = row.yMax - 23f;
        track.mute = GUI.Toggle(new Rect(header.x, toggleY, 28f, 18f), track.mute, "M");
        track.solo = GUI.Toggle(new Rect(header.x + 32f, toggleY, 28f, 18f), track.solo, "S");
        track.locked = GUI.Toggle(new Rect(header.x + 64f, toggleY, 50f, 18f), track.locked, "Lock");

        Rect lane = new Rect(row.x + TimelineTrackHeaderWidth, row.y + 5f, row.width - TimelineTrackHeaderWidth - 6f, row.height - 10f);
        EditorGUI.DrawRect(lane, dragHoverTrackIndex == index ? new Color(0.18f, 0.07f, 0.06f, 1f) : new Color(0.08f, 0.085f, 0.10f, 1f));
        HandleAudioClipDragIntoLane(lane, track, index);

        DrawTimeRangeSelection(lane, index);
        DrawSegments(lane, track, index);
        HandleTimeRangeSelection(lane, index);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
        {
            if (lane.Contains(e.mousePosition))
            {
                // segment click handled inside DrawSegments by rect hit test first.
            }
            else
            {
                SelectTrack(index, e.shift);
                e.Use();
            }
        }
    }

    private void DrawFrozenTrackHeaders(Rect headerRect)
    {
        GUI.BeginGroup(headerRect);

        float yOffset = -trackScroll.y;
        for (int i = 0; i < selectedPackage.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[i];
            if (track == null)
                continue;

            Rect row = new Rect(0f, yOffset + i * GetTimelineTrackRowStep(), headerRect.width, timelineTrackRowHeight);
            if (row.yMax < 0f || row.y > headerRect.height)
                continue;

            bool selected = (selectedTrackIndex == i || IsTrackSelected(i)) && selectedSegmentIndex < 0;
            EditorGUI.DrawRect(row, selected ? new Color(0.36f, 0.13f, 0.11f, 1f) : new Color(0.13f, 0.14f, 0.16f, 1f));
            EditorGUI.DrawRect(new Rect(row.x, row.y, 4f, row.height), track.color);
            EditorGUI.DrawRect(new Rect(row.xMax - 1f, row.y, 1f, row.height), new Color(1f, 1f, 1f, 0.08f));

            Rect inner = new Rect(row.x + 8f, row.y + 5f, row.width - 12f, row.height - 10f);

            Rect soundStateIconRect = new Rect(inner.x, inner.y, 18f, 18f);
            DrawSoundStateIcon(soundStateIconRect, track.mute);

            Rect removeTrackButtonRect = new Rect(inner.xMax - 22f, inner.y, 20f, 18f);
            Rect trackNameRect = new Rect(inner.x + 24f, inner.y, Mathf.Max(20f, inner.width - 50f), 18f);
            GUI.Label(trackNameRect, track.displayName, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(track.locked))
            {
                if (GUI.Button(removeTrackButtonRect, new GUIContent("-", track.locked ? "锁定音轨不能删除" : "删除此音轨"), EditorStyles.miniButton))
                {
                    RemoveTrackAtIndex(i);
                    GUI.EndGroup();
                    return;
                }
            }

            float toggleY = row.yMax - 24f;
            track.mute = GUI.Toggle(new Rect(inner.x, toggleY, 28f, 18f), track.mute, "M");
            if (selectedPackage != null && selectedPackage.mixerChannels != null && i >= 0 && i < selectedPackage.mixerChannels.Count && selectedPackage.mixerChannels[i] != null)
                selectedPackage.mixerChannels[i].mute = track.mute;
            track.solo = GUI.Toggle(new Rect(inner.x + 32f, toggleY, 28f, 18f), track.solo, "S");
            if (selectedPackage != null && selectedPackage.mixerChannels != null && i >= 0 && i < selectedPackage.mixerChannels.Count && selectedPackage.mixerChannels[i] != null)
                selectedPackage.mixerChannels[i].solo = track.solo;
            track.locked = GUI.Toggle(new Rect(inner.x + 64f, toggleY, 56f, 18f), track.locked, "Lock");

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && row.Contains(e.mousePosition) && e.button == 0)
            {
                selectedTrackIndex = i;
                selectedSegmentIndex = -1;
                selectedSegments.Clear();
                GUI.FocusControl(null);
                Context.Repaint();
                e.Use();
            }
        }

        GUI.EndGroup();
    }

    private void HandleAudioClipDragIntoLane(Rect lane, SkyPrisonAudioTrack track, int trackIndex)
    {
        Event e = Event.current;
        if (e == null || track == null || !lane.Contains(e.mousePosition))
            return;

        bool hasAudioClip = false;
        UnityEngine.Object[] refs = DragAndDrop.objectReferences;
        for (int i = 0; i < refs.Length; i++)
        {
            if (refs[i] is AudioClip)
            {
                hasAudioClip = true;
                break;
            }
        }

        if (!hasAudioClip)
            return;

        if (e.type == EventType.DragUpdated)
        {
            dragHoverTrackIndex = trackIndex;
            DragAndDrop.visualMode = track.locked ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Copy;
            Context.Repaint();
            e.Use();
        }
        else if (e.type == EventType.DragPerform)
        {
            if (track.locked)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                e.Use();
                return;
            }

            DragAndDrop.AcceptDrag();

            float timelineStart = Mathf.Max(0f, (e.mousePosition.x - lane.x) / Mathf.Max(1f, timelinePixelsPerSecond));
            timelineStart = SnapIfNeeded(timelineStart);

            Undo.RecordObject(selectedPackage, "Drag AudioClip To Track");

            if (track.segments == null)
                track.segments = new List<SkyPrisonAudioSegment>();

            int addedCount = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                AudioClip clip = refs[i] as AudioClip;
                if (clip == null)
                    continue;

                SkyPrisonAudioSegment segment = new SkyPrisonAudioSegment
                {
                    displayName = clip.name,
                    sourceClip = clip,
                    timelineStart = timelineStart,
                    sourceStart = 0f,
                    sourceEnd = clip.length,
                    volume = 1f,
                    pitch = 1f,
                    pan = 0f
                };

                track.segments.Add(segment);
                selectedTrackIndex = trackIndex;
                selectedSegmentIndex = track.segments.Count - 1;

                timelineStart += Mathf.Max(0.05f, clip.length) + timelineSnapInterval;
                addedCount++;
            }

            dragHoverTrackIndex = -1;

            if (addedCount > 0)
            {
                selectedPackage.EnsureValid();
                EditorUtility.SetDirty(selectedPackage);
            }

            Context.Repaint();
            e.Use();
        }
        else if (e.type == EventType.DragExited)
        {
            dragHoverTrackIndex = -1;
            Context.Repaint();
        }
    }

    private void DrawGlobalTimeRangeSelection(Rect viewRect)
    {
        if (timeRangeTrackIndex != -2)
            return;

        float start = Mathf.Min(timeRangeStart, timeRangeEnd);
        float end = Mathf.Max(timeRangeStart, timeRangeEnd);
        if (end - start <= 0.001f)
            return;

        float x = start * timelinePixelsPerSecond;
        float w = (end - start) * timelinePixelsPerSecond;
        Rect rect = new Rect(x, 0f, w, viewRect.height);
        rect.xMin = Mathf.Max(rect.xMin, 0f);
        rect.xMax = Mathf.Min(rect.xMax, viewRect.xMax);
        if (rect.width <= 0f)
            return;

        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.16f));
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.84f));
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.84f));
        GUI.Label(new Rect(rect.x + 4f, rect.y + 4f, Mathf.Max(80f, rect.width - 8f), 18f), $"{FormatTime(start)} - {FormatTime(end)}", EditorStyles.miniLabel);
    }

    private void HandleGlobalTimeRangeSelectionFromRuler(Rect rulerRect)
    {
        Event e = Event.current;
        if (e == null)
            return;

        Rect laneRuler = new Rect(rulerRect.x + TimelineTrackHeaderWidth, rulerRect.y, rulerRect.width - TimelineTrackHeaderWidth, rulerRect.height);

        if (draggingTimeRange && timeRangeTrackIndex == -2)
        {
            if (e.type == EventType.MouseDrag)
            {
                timeRangeEnd = Mathf.Clamp(SnapIfNeeded((e.mousePosition.x - laneRuler.x + trackScroll.x) / Mathf.Max(1f, timelinePixelsPerSecond)), 0f, GetTimelineTotalLengthSeconds());
                Context.Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                timeRangeEnd = Mathf.Clamp(SnapIfNeeded((e.mousePosition.x - laneRuler.x + trackScroll.x) / Mathf.Max(1f, timelinePixelsPerSecond)), 0f, GetTimelineTotalLengthSeconds());
                draggingTimeRange = false;
                NormalizeTimeRange();
                Context.Repaint();
                e.Use();
            }

            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && laneRuler.Contains(e.mousePosition))
        {
            selectedSegments.Clear();
            selectedSegmentIndex = -1;

            // 单点：立即移动红色播放竖线。
            // 后续如果继续拖拽，才会扩展成白色时间选区。
            timeRangeTrackIndex = -2;
            timeRangeAnchorTime = Mathf.Clamp(
                SnapIfNeeded((e.mousePosition.x - laneRuler.x + trackScroll.x) / Mathf.Max(1f, timelinePixelsPerSecond)),
                0f,
                GetTimelineTotalLengthSeconds());
            timeRangeStart = timeRangeAnchorTime;
            timeRangeEnd = timeRangeAnchorTime;
            playheadTime = timeRangeAnchorTime;
            ResyncPreviewAfterManualPlayheadJump();

            draggingTimeRange = true;
            GUI.FocusControl(null);
            Context.Repaint();
            e.Use();
        }
    }

    private void DrawTimeRangeSelection(Rect lane, int trackIndex)
    {
        if (timeRangeTrackIndex != trackIndex)
            return;

        float start = Mathf.Min(timeRangeStart, timeRangeEnd);
        float end = Mathf.Max(timeRangeStart, timeRangeEnd);
        if (end - start <= 0.001f)
            return;

        float x = lane.x + start * timelinePixelsPerSecond;
        float w = (end - start) * timelinePixelsPerSecond;

        Rect rect = new Rect(x, lane.y, w, lane.height);
        rect.xMin = Mathf.Max(rect.xMin, lane.x);
        rect.xMax = Mathf.Min(rect.xMax, lane.xMax);
        if (rect.width <= 0f)
            return;

        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.22f));
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.78f));
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.78f));
        GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, Mathf.Max(60f, rect.width - 8f), 18f), $"{FormatTime(start)} - {FormatTime(end)}", EditorStyles.miniLabel);
    }

    private void HandleTimeRangeSelection(Rect lane, int trackIndex)
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (draggingTimeRange && timeRangeTrackIndex == trackIndex)
        {
            if (e.type == EventType.MouseDrag)
            {
                timeRangeEnd = GetLaneTimeFromMouse(lane, e.mousePosition.x);
                Context.Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                timeRangeEnd = GetLaneTimeFromMouse(lane, e.mousePosition.x);
                draggingTimeRange = false;
                NormalizeTimeRange();
                Context.Repaint();
                e.Use();
            }

            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0 && lane.Contains(e.mousePosition))
        {
            selectedSegments.Clear();
            selectedTrackIndex = trackIndex;
            selectedTrackIndices.Clear();
            selectedTrackIndices.Add(trackIndex);
            selectedSegmentIndex = -1;

            // 单点：立即移动红色播放竖线。
            // 拖拽：在当前 TRACK 行生成白色选区。
            timeRangeTrackIndex = trackIndex;
            timeRangeAnchorTime = GetLaneTimeFromMouse(lane, e.mousePosition.x);
            timeRangeStart = timeRangeAnchorTime;
            timeRangeEnd = timeRangeAnchorTime;
            playheadTime = timeRangeAnchorTime;
            ResyncPreviewAfterManualPlayheadJump();
            draggingTimeRange = true;

            GUI.FocusControl(null);
            Context.Repaint();
            e.Use();
        }
    }

    private float GetLaneTimeFromMouse(Rect lane, float mouseX)
    {
        float t = (mouseX - lane.x) / Mathf.Max(1f, timelinePixelsPerSecond);
        return Mathf.Clamp(SnapIfNeeded(t), 0f, GetTimelineTotalLengthSeconds());
    }

    private void NormalizeTimeRange()
    {
        float hardEnd = GetTimelineTotalLengthSeconds();
        float a = Mathf.Clamp(Mathf.Min(timeRangeStart, timeRangeEnd), 0f, hardEnd);
        float b = Mathf.Clamp(Mathf.Max(timeRangeStart, timeRangeEnd), 0f, hardEnd);
        timeRangeStart = a;
        timeRangeEnd = b;

        if (timeRangeEnd - timeRangeStart <= 0.001f)
        {
            timeRangeStart = timeRangeEnd = timeRangeAnchorTime;
        }
    }

    private bool HasTimeRangeSelection()
    {
        return (timeRangeTrackIndex >= 0 || timeRangeTrackIndex == -2) && Mathf.Abs(timeRangeEnd - timeRangeStart) > 0.001f;
    }

    private bool TryGetTimeRange(out int trackIndex, out float start, out float end)
    {
        trackIndex = timeRangeTrackIndex;
        start = Mathf.Min(timeRangeStart, timeRangeEnd);
        end = Mathf.Max(timeRangeStart, timeRangeEnd);
        return (trackIndex >= 0 || trackIndex == -2) && end - start > 0.001f;
    }

    private bool SegmentIntersectsRange(SkyPrisonAudioSegment segment, float rangeStart, float rangeEnd)
    {
        if (segment == null)
            return false;

        float segStart = segment.timelineStart;
        float segEnd = segment.timelineStart + segment.Duration;
        return segEnd > rangeStart && segStart < rangeEnd;
    }

    private void DrawSegments(Rect lane, SkyPrisonAudioTrack track, int trackIndex)
    {
        if (track.segments == null)
            return;

        DrawLaneGrid(lane);

        for (int i = 0; i < track.segments.Count; i++)
        {
            SkyPrisonAudioSegment segment = track.segments[i];
            if (segment == null)
                continue;

            float hardTimelineEnd = GetTimelineTotalLengthSeconds();
            float visibleEnd = Mathf.Min(segment.timelineStart + segment.Duration, hardTimelineEnd);
            float duration = Mathf.Max(0f, visibleEnd - segment.timelineStart);
            if (duration <= 0f)
                continue;

            float x = lane.x + segment.timelineStart * timelinePixelsPerSecond;
            float w = Mathf.Max(18f, duration * timelinePixelsPerSecond);
            Rect fullSegRect = new Rect(x, lane.y + 5f, w, lane.height - 10f);
            Rect segRect = new Rect(
                Mathf.Max(fullSegRect.x, lane.x),
                fullSegRect.y,
                Mathf.Min(fullSegRect.xMax, lane.xMax) - Mathf.Max(fullSegRect.x, lane.x),
                fullSegRect.height
            );

            if (segRect.width <= 0f || segRect.xMax < lane.x || segRect.x > lane.xMax)
                continue;

            bool selected = IsSegmentSelected(trackIndex, i);
            Color col = selected ? new Color(1.00f, 0.36f, 0.28f, 1f) : track.color;
            EditorGUI.DrawRect(segRect, new Color(col.r, col.g, col.b, selected ? 0.88f : 0.62f));

            float effectiveSourceEnd = Mathf.Min(segment.sourceEnd, segment.sourceStart + duration);
            bool clippedByTimelineEnd = segment.timelineStart + segment.Duration > hardTimelineEnd + 0.0001f;
            DrawMiniWaveform(segRect, segment, effectiveSourceEnd);

            Rect leftHandle = new Rect(segRect.x, segRect.y, Mathf.Min(7f, segRect.width * 0.35f), segRect.height);
            Rect rightHandle = new Rect(segRect.xMax - Mathf.Min(7f, segRect.width * 0.35f), segRect.y, Mathf.Min(7f, segRect.width * 0.35f), segRect.height);
            EditorGUI.DrawRect(leftHandle, new Color(0f, 0f, 0f, 0.28f));
            EditorGUI.DrawRect(rightHandle, new Color(0f, 0f, 0f, 0.28f));
            EditorGUIUtility.AddCursorRect(leftHandle, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(rightHandle, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(segRect.x + leftHandle.width, segRect.y, Mathf.Max(0f, segRect.width - leftHandle.width - rightHandle.width), segRect.height), MouseCursor.Pan);

            GUI.Label(new Rect(segRect.x + 8f, segRect.y + 2f, segRect.width - 16f, 18f), GetSegmentLabel(segment), EditorStyles.miniLabel);
            string clipMark = clippedByTimelineEnd ? "  截断" : "";
            GUI.Label(new Rect(segRect.x + 8f, segRect.yMax - 18f, segRect.width - 16f, 16f), $"{FormatTime(segment.timelineStart)} / {FormatTime(segment.sourceStart)} - {FormatTime(effectiveSourceEnd)}{clipMark}", EditorStyles.miniLabel);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && segRect.Contains(e.mousePosition) && e.button == 0)
            {
                bool additive = e.alt || e.shift || e.control || e.command;
                SelectSegment(trackIndex, i, additive);

                if (!track.locked)
                {
                    BeginSegmentDrag(
                        leftHandle.Contains(e.mousePosition) ? SegmentDragMode.ResizeLeft :
                        rightHandle.Contains(e.mousePosition) ? SegmentDragMode.ResizeRight :
                        SegmentDragMode.Move,
                        trackIndex,
                        i,
                        e.mousePosition.x - lane.x,
                        segment
                    );
                }

                GUI.FocusControl(null);
                Context.Repaint();
                e.Use();
            }
        }
    }

    private void DrawLaneGrid(Rect lane)
    {
        GetTimelineVisualGridSteps(out float minorStep, out float majorStep);
        if (minorStep <= 0f)
            return;

        float totalSeconds = lane.width / Mathf.Max(1f, timelinePixelsPerSecond);
        int maxTicks = Mathf.CeilToInt(totalSeconds / minorStep) + 4;

        for (int i = 0; i < maxTicks; i++)
        {
            float time = i * minorStep;
            float x = lane.x + time * timelinePixelsPerSecond;
            if (x > lane.xMax)
                break;

            bool major = IsGridMajorTick(time, majorStep);
            Color color = major ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.035f);
            float lineWidth = major ? 2f : 1f;
            EditorGUI.DrawRect(new Rect(x, lane.y, lineWidth, lane.height), color);
        }
    }

    private SkyPrisonAudioSegment GetSegment(int trackIndex, int segmentIndex)
    {
        if (selectedPackage == null || selectedPackage.tracks == null)
            return null;

        if (trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
            return null;

        SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
        if (track == null || track.segments == null)
            return null;

        if (segmentIndex < 0 || segmentIndex >= track.segments.Count)
            return null;

        return track.segments[segmentIndex];
    }

    private bool IsSegmentSelected(int trackIndex, int segmentIndex)
    {
        for (int i = 0; i < selectedSegments.Count; i++)
        {
            if (selectedSegments[i].trackIndex == trackIndex && selectedSegments[i].segmentIndex == segmentIndex)
                return true;
        }

        return false;
    }

    private void SelectSegment(int trackIndex, int segmentIndex, bool additive)
    {
        timeRangeTrackIndex = -1;
        draggingTimeRange = false;

        if (!additive)
            selectedSegments.Clear();
        selectedTrackIndices.Clear();

        bool alreadySelected = IsSegmentSelected(trackIndex, segmentIndex);
        if (additive && alreadySelected)
            selectedSegments.RemoveAll(x => x.trackIndex == trackIndex && x.segmentIndex == segmentIndex);
        else if (!alreadySelected)
            selectedSegments.Add(new SegmentRef(trackIndex, segmentIndex));

        selectedTrackIndex = trackIndex;
        selectedSegmentIndex = segmentIndex;

        if (selectedSegments.Count == 0)
            selectedSegments.Add(new SegmentRef(trackIndex, segmentIndex));
    }

    private void CleanupSelectedSegments()
    {
        selectedSegments.RemoveAll(x => GetSegment(x.trackIndex, x.segmentIndex) == null);
    }

    private SkyPrisonAudioSegment CloneSegment(SkyPrisonAudioSegment source)
    {
        if (source == null)
            return null;

        return new SkyPrisonAudioSegment
        {
            displayName = source.displayName,
            sourceClip = source.sourceClip,
            timelineStart = source.timelineStart,
            sourceStart = source.sourceStart,
            sourceEnd = source.sourceEnd,
            volume = source.volume,
            pitch = source.pitch,
            pan = source.pan,
            fadeIn = source.fadeIn,
            fadeOut = source.fadeOut,
            randomWeight = source.randomWeight,
            tag = source.tag
        };
    }

    private void CopySelectedSegments()
    {
        if (selectedPackage == null)
            return;

        if (HasTimeRangeSelection())
        {
            CopyTimeRangeSelection();
            return;
        }

        CleanupSelectedSegments();
        segmentClipboard.Clear();

        if (selectedSegments.Count == 0 && selectedTrackIndex >= 0 && selectedSegmentIndex >= 0)
            selectedSegments.Add(new SegmentRef(selectedTrackIndex, selectedSegmentIndex));

        if (selectedSegments.Count == 0)
            return;

        float minStart = float.MaxValue;
        int minTrack = int.MaxValue;

        for (int i = 0; i < selectedSegments.Count; i++)
        {
            SegmentRef sr = selectedSegments[i];
            SkyPrisonAudioSegment segment = GetSegment(sr.trackIndex, sr.segmentIndex);
            if (segment == null)
                continue;

            minStart = Mathf.Min(minStart, segment.timelineStart);
            minTrack = Mathf.Min(minTrack, sr.trackIndex);
        }

        if (float.IsInfinity(minStart) || minTrack == int.MaxValue)
            return;

        for (int i = 0; i < selectedSegments.Count; i++)
        {
            SegmentRef sr = selectedSegments[i];
            SkyPrisonAudioSegment segment = GetSegment(sr.trackIndex, sr.segmentIndex);
            if (segment == null)
                continue;

            segmentClipboard.Add(new SegmentClipboardData
            {
                relativeTrackIndex = sr.trackIndex - minTrack,
                relativeTimelineStart = segment.timelineStart - minStart,
                segment = CloneSegment(segment)
            });
        }
    }

    private void CutSelectedSegments()
    {
        if (HasTimeRangeSelection())
        {
            CopyTimeRangeSelection();
            DeleteTimeRangeSelection();
            return;
        }

        CopySelectedSegments();
        DeleteSelectedSegments();
    }

    private void CopyTimeRangeSelection()
    {
        if (selectedPackage == null)
            return;

        if (!TryGetTimeRange(out int trackIndex, out float rangeStart, out float rangeEnd))
            return;

        List<int> targetTracks = GetTargetTrackIndicesForTimeRange(trackIndex);
        if (targetTracks.Count == 0)
            return;

        segmentClipboard.Clear();
        int baseTrack = targetTracks[0];

        for (int t = 0; t < targetTracks.Count; t++)
        {
            int currentTrackIndex = targetTracks[t];
            if (currentTrackIndex < 0 || currentTrackIndex >= selectedPackage.tracks.Count)
                continue;

            SkyPrisonAudioTrack track = selectedPackage.tracks[currentTrackIndex];
            if (track == null || track.segments == null)
                continue;

            for (int i = 0; i < track.segments.Count; i++)
            {
                SkyPrisonAudioSegment segment = track.segments[i];
                if (!SegmentIntersectsRange(segment, rangeStart, rangeEnd))
                    continue;

                float overlapStart = Mathf.Max(rangeStart, segment.timelineStart);
                float overlapEnd = Mathf.Min(rangeEnd, segment.timelineStart + segment.Duration);
                if (overlapEnd <= overlapStart)
                    continue;

                SkyPrisonAudioSegment copied = CloneSegment(segment);
                copied.timelineStart = overlapStart - rangeStart;
                copied.sourceStart = segment.sourceStart + (overlapStart - segment.timelineStart);
                copied.sourceEnd = segment.sourceStart + (overlapEnd - segment.timelineStart);
                copied.ClampToClip();

                segmentClipboard.Add(new SegmentClipboardData
                {
                    relativeTrackIndex = currentTrackIndex - baseTrack,
                    relativeTimelineStart = copied.timelineStart,
                    segment = copied
                });
            }
        }
    }

    private List<int> GetTargetTrackIndicesForTimeRange(int trackIndex)
    {
        List<int> result = new List<int>();
        if (selectedPackage == null || selectedPackage.tracks == null)
            return result;

        if (trackIndex >= 0)
        {
            if (trackIndex < selectedPackage.tracks.Count)
                result.Add(trackIndex);
            return result;
        }

        if (trackIndex == -2)
        {
            if (selectedTrackIndices.Count > 0)
            {
                for (int i = 0; i < selectedTrackIndices.Count; i++)
                {
                    int idx = selectedTrackIndices[i];
                    if (idx >= 0 && idx < selectedPackage.tracks.Count && !result.Contains(idx))
                        result.Add(idx);
                }
            }
            else
            {
                for (int i = 0; i < selectedPackage.tracks.Count; i++)
                    result.Add(i);
            }
        }

        result.Sort();
        return result;
    }

    private void DeleteTimeRangeSelection()
    {
        if (selectedPackage == null)
            return;

        if (!TryGetTimeRange(out int trackIndex, out float rangeStart, out float rangeEnd))
            return;

        List<int> targetTracks = GetTargetTrackIndicesForTimeRange(trackIndex);
        if (targetTracks.Count == 0)
            return;

        Undo.RecordObject(selectedPackage, "Delete Audio Time Range");

        for (int t = 0; t < targetTracks.Count; t++)
        {
            int currentTrackIndex = targetTracks[t];
            if (currentTrackIndex < 0 || currentTrackIndex >= selectedPackage.tracks.Count)
                continue;

            SkyPrisonAudioTrack track = selectedPackage.tracks[currentTrackIndex];
            if (track == null || track.segments == null)
                continue;

            for (int i = track.segments.Count - 1; i >= 0; i--)
            {
                SkyPrisonAudioSegment segment = track.segments[i];
                if (!SegmentIntersectsRange(segment, rangeStart, rangeEnd))
                    continue;

                float segStart = segment.timelineStart;
                float segEnd = segment.timelineStart + segment.Duration;
                float originalSourceStart = segment.sourceStart;
                float originalSourceEnd = segment.sourceEnd;

                bool cutWhole = rangeStart <= segStart && rangeEnd >= segEnd;
                bool cutLeft = rangeStart <= segStart && rangeEnd < segEnd;
                bool cutRight = rangeStart > segStart && rangeEnd >= segEnd;
                bool cutMiddle = rangeStart > segStart && rangeEnd < segEnd;

                if (cutWhole)
                {
                    track.segments.RemoveAt(i);
                }
                else if (cutLeft)
                {
                    float removeDuration = rangeEnd - segStart;
                    segment.timelineStart = rangeEnd;
                    segment.sourceStart = Mathf.Min(originalSourceEnd, originalSourceStart + removeDuration);
                    segment.ClampToClip();
                }
                else if (cutRight)
                {
                    float keepDuration = rangeStart - segStart;
                    segment.sourceEnd = Mathf.Clamp(originalSourceStart + keepDuration, originalSourceStart, originalSourceEnd);
                    segment.ClampToClip();
                }
                else if (cutMiddle)
                {
                    float leftDuration = rangeStart - segStart;
                    float rightOffset = rangeEnd - segStart;

                    SkyPrisonAudioSegment right = CloneSegment(segment);
                    right.timelineStart = rangeEnd;
                    right.sourceStart = Mathf.Clamp(originalSourceStart + rightOffset, originalSourceStart, originalSourceEnd);
                    right.sourceEnd = originalSourceEnd;
                    right.ClampToClip();

                    segment.sourceEnd = Mathf.Clamp(originalSourceStart + leftDuration, originalSourceStart, originalSourceEnd);
                    segment.ClampToClip();

                    track.segments.Insert(i + 1, right);
                }
            }
        }

        selectedSegments.Clear();
        selectedSegmentIndex = -1;
        timeRangeTrackIndex = -1;
        draggingTimeRange = false;

        EditorUtility.SetDirty(selectedPackage);
        Context.Repaint();
    }

    private void PasteSegmentsAtPlayhead()
    {
        if (selectedPackage == null || segmentClipboard.Count == 0)
            return;

        Undo.RecordObject(selectedPackage, "Paste Audio Segments");
        selectedSegments.Clear();

        int baseTrack = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;

        for (int i = 0; i < segmentClipboard.Count; i++)
        {
            SegmentClipboardData data = segmentClipboard[i];
            if (data == null || data.segment == null)
                continue;

            int targetTrackIndex = Mathf.Clamp(baseTrack + data.relativeTrackIndex, 0, selectedPackage.tracks.Count - 1);
            if (targetTrackIndex < 0 || targetTrackIndex >= selectedPackage.tracks.Count)
                continue;

            SkyPrisonAudioTrack track = selectedPackage.tracks[targetTrackIndex];
            if (track.segments == null)
                track.segments = new List<SkyPrisonAudioSegment>();

            SkyPrisonAudioSegment pasted = CloneSegment(data.segment);
            pasted.timelineStart = SnapIfNeeded(playheadTime + data.relativeTimelineStart);
            track.segments.Add(pasted);

            selectedSegments.Add(new SegmentRef(targetTrackIndex, track.segments.Count - 1));
            selectedTrackIndex = targetTrackIndex;
            selectedSegmentIndex = track.segments.Count - 1;
        }

        EditorUtility.SetDirty(selectedPackage);
        Context.Repaint();
    }

    private void DeleteSelectedSegments()
    {
        if (selectedPackage == null)
            return;

        if (HasTimeRangeSelection())
        {
            DeleteTimeRangeSelection();
            return;
        }

        CleanupSelectedSegments();
        if (selectedSegments.Count == 0 && selectedTrackIndex >= 0 && selectedSegmentIndex >= 0)
            selectedSegments.Add(new SegmentRef(selectedTrackIndex, selectedSegmentIndex));

        if (selectedSegments.Count == 0)
            return;

        Undo.RecordObject(selectedPackage, "Delete Audio Segments");

        var sorted = selectedSegments
            .OrderByDescending(x => x.trackIndex)
            .ThenByDescending(x => x.segmentIndex)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            SegmentRef sr = sorted[i];
            if (sr.trackIndex < 0 || sr.trackIndex >= selectedPackage.tracks.Count)
                continue;

            SkyPrisonAudioTrack track = selectedPackage.tracks[sr.trackIndex];
            if (track == null || track.segments == null)
                continue;

            if (sr.segmentIndex >= 0 && sr.segmentIndex < track.segments.Count)
                track.segments.RemoveAt(sr.segmentIndex);
        }

        selectedSegments.Clear();
        selectedSegmentIndex = -1;
        EditorUtility.SetDirty(selectedPackage);
        Context.Repaint();
    }

    private void SplitSegmentsCrossingPlayhead()
    {
        if (selectedPackage == null || previewPlaying)
            return;

        float cutTime = Mathf.Clamp(SnapIfNeeded(playheadTime), 0f, GetTimelineTotalLengthSeconds());
        float timelineEnd = GetTimelineTotalLengthSeconds();
        List<int> targetTracks = selectedTrackIndices.Count > 0
            ? selectedTrackIndices.Distinct().OrderBy(x => x).ToList()
            : Enumerable.Range(0, selectedPackage.tracks.Count).ToList();

        Undo.RecordObject(selectedPackage, "Cut Audio Segments At Playhead");

        List<SegmentRef> newSelection = new List<SegmentRef>();
        bool changed = false;

        for (int t = 0; t < targetTracks.Count; t++)
        {
            int trackIndex = targetTracks[t];
            if (trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
                continue;

            SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
            if (track == null || track.locked || track.segments == null)
                continue;

            for (int i = track.segments.Count - 1; i >= 0; i--)
            {
                SkyPrisonAudioSegment segment = track.segments[i];
                if (segment == null)
                    continue;

                float segmentStart = segment.timelineStart;
                float visibleEnd = Mathf.Min(segment.timelineStart + segment.Duration, timelineEnd);

                if (cutTime <= segmentStart + 0.01f || cutTime >= visibleEnd - 0.01f)
                    continue;

                float localTime = cutTime - segmentStart;
                float cutSourceTime = segment.sourceStart + localTime;
                if (cutSourceTime <= segment.sourceStart + 0.01f || cutSourceTime >= segment.sourceEnd - 0.01f)
                    continue;

                SkyPrisonAudioSegment right = CloneSegment(segment);
                right.timelineStart = cutTime;
                right.sourceStart = cutSourceTime;
                right.sourceEnd = segment.sourceEnd;
                right.ClampToClip();

                segment.sourceEnd = cutSourceTime;
                segment.ClampToClip();

                int insertIndex = Mathf.Clamp(i + 1, 0, track.segments.Count);
                track.segments.Insert(insertIndex, right);
                newSelection.Add(new SegmentRef(trackIndex, insertIndex));
                changed = true;
            }
        }

        if (!changed)
            return;

        selectedSegments.Clear();
        selectedSegments.AddRange(newSelection.OrderBy(x => x.trackIndex).ThenBy(x => x.segmentIndex));
        if (selectedSegments.Count > 0)
        {
            selectedTrackIndex = selectedSegments[0].trackIndex;
            selectedSegmentIndex = selectedSegments[0].segmentIndex;
        }

        EditorUtility.SetDirty(selectedPackage);
        Context.Repaint();
    }

    private void SplitSelectedSegmentsAtPlayhead()
    {
        if (selectedPackage == null)
            return;

        CleanupSelectedSegments();
        if (selectedSegments.Count == 0 && selectedTrackIndex >= 0 && selectedSegmentIndex >= 0)
            selectedSegments.Add(new SegmentRef(selectedTrackIndex, selectedSegmentIndex));

        if (selectedSegments.Count == 0)
            return;

        Undo.RecordObject(selectedPackage, "Split Audio Segments");

        List<SegmentRef> newSelection = new List<SegmentRef>();

        for (int i = 0; i < selectedSegments.Count; i++)
        {
            SegmentRef sr = selectedSegments[i];
            SkyPrisonAudioSegment segment = GetSegment(sr.trackIndex, sr.segmentIndex);
            if (segment == null)
                continue;

            float localTime = playheadTime - segment.timelineStart;
            if (localTime <= 0.01f || localTime >= segment.Duration - 0.01f)
                continue;

            float cutSourceTime = segment.sourceStart + localTime;
            SkyPrisonAudioSegment right = CloneSegment(segment);
            right.timelineStart = playheadTime;
            right.sourceStart = cutSourceTime;

            segment.sourceEnd = cutSourceTime;

            SkyPrisonAudioTrack track = selectedPackage.tracks[sr.trackIndex];
            int insertIndex = Mathf.Clamp(sr.segmentIndex + 1, 0, track.segments.Count);
            track.segments.Insert(insertIndex, right);
            newSelection.Add(new SegmentRef(sr.trackIndex, insertIndex));
        }

        if (newSelection.Count > 0)
        {
            selectedSegments.Clear();
            selectedSegments.AddRange(newSelection);
            selectedTrackIndex = newSelection[0].trackIndex;
            selectedSegmentIndex = newSelection[0].segmentIndex;
        }

        EditorUtility.SetDirty(selectedPackage);
        Context.Repaint();
    }

    private void HandleTimelineEditHotkeys()
    {
        Event e = Event.current;
        if (e == null || selectedPackage == null || e.type != EventType.KeyDown)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        bool ctrlOrCmd = e.control || e.command;

        if (e.keyCode == KeyCode.Space)
        {
            TogglePreviewPlayback();
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.C)
        {
            CopySelectedSegments();
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.X)
        {
            CutSelectedSegments();
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.V)
        {
            PasteSegmentsAtPlayhead();
            e.Use();
        }
        else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            DeleteSelectedSegments();
            e.Use();
        }
        else if (e.keyCode == KeyCode.S)
        {
            SplitSelectedSegmentsAtPlayhead();
            e.Use();
        }
    }

    private void BeginSegmentDrag(SegmentDragMode mode, int trackIndex, int segmentIndex, float mouseX, SkyPrisonAudioSegment segment)
    {
        if (segment == null)
            return;

        segmentDragMode = mode;
        draggingTrackIndex = trackIndex;
        draggingSegmentIndex = segmentIndex;
        dragStartMouseX = mouseX;
        dragOriginalTimelineStart = segment.timelineStart;
        dragMouseGrabOffsetSeconds = Mathf.Max(0f, (mouseX / Mathf.Max(1f, timelinePixelsPerSecond)) - segment.timelineStart);
        dragOriginalSourceStart = segment.sourceStart;
        dragOriginalSourceEnd = segment.sourceEnd;

        segmentDragOriginals.Clear();

        if (mode == SegmentDragMode.Move && IsSegmentSelected(trackIndex, segmentIndex))
        {
            for (int i = 0; i < selectedSegments.Count; i++)
            {
                SegmentRef sr = selectedSegments[i];
                SkyPrisonAudioSegment selected = GetSegment(sr.trackIndex, sr.segmentIndex);
                if (selected == null)
                    continue;

                segmentDragOriginals.Add(new SegmentDragOriginal
                {
                    trackIndex = sr.trackIndex,
                    segmentIndex = sr.segmentIndex,
                    timelineStart = selected.timelineStart,
                    sourceStart = selected.sourceStart,
                    sourceEnd = selected.sourceEnd
                });
            }
        }
        else
        {
            segmentDragOriginals.Add(new SegmentDragOriginal
            {
                trackIndex = trackIndex,
                segmentIndex = segmentIndex,
                timelineStart = segment.timelineStart,
                sourceStart = segment.sourceStart,
                sourceEnd = segment.sourceEnd
            });
        }

        if (selectedPackage != null)
            Undo.RecordObject(selectedPackage, mode == SegmentDragMode.Move ? "Move Audio Segment" : "Trim Audio Segment");
    }

    private void HandleSegmentDrag(Event e)
    {
        if (e == null || segmentDragMode == SegmentDragMode.None)
            return;

        if (selectedPackage == null)
        {
            EndSegmentDrag();
            return;
        }

        if (e.type == EventType.MouseDrag)
        {
            segmentDragFinalizing = false;
            float deltaSeconds = CalculateSegmentDragDelta(e.mousePosition.x);
            ApplySegmentDrag(deltaSeconds);
            EditorUtility.SetDirty(selectedPackage);
            Context.Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
        {
            segmentDragFinalizing = true;
            ApplySegmentDrag(CalculateSegmentDragDelta(e.mousePosition.x));
            segmentDragFinalizing = false;
            EditorUtility.SetDirty(selectedPackage);
            EndSegmentDrag();
            e.Use();
        }
    }

    private float CalculateSegmentDragDelta(float mouseX)
    {
        float px = Mathf.Max(1f, timelinePixelsPerSecond);

        // Move uses an anchor measured from the exact mouse grab point inside the segment.
        // This prevents the clip from visually lagging behind the cursor when the timeline is zoomed, snapped,
        // or horizontally scrolled. Resize still uses raw delta so edge handles stay predictable.
        if (segmentDragMode == SegmentDragMode.Move)
        {
            float mouseTime = mouseX / px;
            float desiredStart = mouseTime - dragMouseGrabOffsetSeconds;
            return desiredStart - dragOriginalTimelineStart;
        }

        return (mouseX - dragStartMouseX) / px;
    }

    private void ApplySegmentDrag(float deltaSeconds)
    {
        if (segmentDragMode == SegmentDragMode.Move)
            deltaSeconds = GetCollisionClampedMoveDelta(deltaSeconds);

        for (int i = 0; i < segmentDragOriginals.Count; i++)
        {
            SegmentDragOriginal original = segmentDragOriginals[i];
            SkyPrisonAudioSegment segment = GetSegment(original.trackIndex, original.segmentIndex);
            if (segment == null)
                continue;

            ApplySegmentDragFromOriginal(segment, original, deltaSeconds);
        }
    }

    private void ApplySegmentDragFromOriginal(SkyPrisonAudioSegment segment, SegmentDragOriginal original, float deltaSeconds)
    {
        if (segment == null || original == null)
            return;

        const float minDuration = 0.03f;
        float hardTimelineEnd = GetTimelineTotalLengthSeconds();
        float clipLength = segment.sourceClip != null ? segment.sourceClip.length : Mathf.Max(original.sourceEnd, 60f);
        float originalDuration = Mathf.Max(minDuration, original.sourceEnd - original.sourceStart);

        switch (segmentDragMode)
        {
            case SegmentDragMode.Move:
            {
                // 拖拽过程中不吸附，保证鼠标抓住的位置和片段实际位置一致。
                // 松开鼠标时再做最终吸附，避免视觉上“片段落在鼠标后面”。
                float desiredStart = original.timelineStart + deltaSeconds;
                if (segmentDragFinalizing)
                    desiredStart = SnapIfNeeded(desiredStart);

                float maxStartByTimeline = Mathf.Max(0f, hardTimelineEnd - originalDuration);
                segment.timelineStart = Mathf.Clamp(desiredStart, 0f, maxStartByTimeline);
                break;
            }

            case SegmentDragMode.ResizeLeft:
            {
                float currentEnd = original.timelineStart + originalDuration;
                float prevEnd = GetPreviousSegmentEnd(original.trackIndex, original.segmentIndex, original.timelineStart);
                float minTimelineStart = Mathf.Max(0f, prevEnd);
                float maxTimelineStart = Mathf.Max(minTimelineStart, currentEnd - minDuration);

                float desiredTimelineStart = SnapIfNeeded(original.timelineStart + deltaSeconds);
                float newTimelineStart = Mathf.Clamp(desiredTimelineStart, minTimelineStart, maxTimelineStart);

                float appliedDelta = newTimelineStart - original.timelineStart;
                float newSourceStart = Mathf.Clamp(original.sourceStart + appliedDelta, 0f, original.sourceEnd - minDuration);

                segment.sourceStart = SnapIfNeeded(newSourceStart);
                segment.timelineStart = SnapIfNeeded(original.timelineStart + (segment.sourceStart - original.sourceStart));
                segment.timelineStart = Mathf.Clamp(segment.timelineStart, minTimelineStart, maxTimelineStart);
                break;
            }

            case SegmentDragMode.ResizeRight:
            {
                float nextStart = GetNextSegmentStart(original.trackIndex, original.segmentIndex, original.timelineStart + originalDuration);
                float maxTimelineEnd = Mathf.Min(hardTimelineEnd, nextStart);
                float maxDurationByTimeline = Mathf.Max(minDuration, maxTimelineEnd - original.timelineStart);
                float maxSourceEndByTimeline = original.sourceStart + maxDurationByTimeline;
                float desiredSourceEnd = SnapIfNeeded(original.sourceEnd + deltaSeconds);

                float maxSourceEnd = Mathf.Min(clipLength, maxSourceEndByTimeline);
                segment.sourceEnd = Mathf.Clamp(desiredSourceEnd, original.sourceStart + minDuration, maxSourceEnd);
                break;
            }
        }

        segment.ClampToClip();
    }

    private float GetCollisionClampedMoveDelta(float deltaSeconds)
    {
        float minDelta = -999999f;
        float maxDelta = 999999f;
        float hardTimelineEnd = GetTimelineTotalLengthSeconds();

        for (int i = 0; i < segmentDragOriginals.Count; i++)
        {
            SegmentDragOriginal original = segmentDragOriginals[i];
            SkyPrisonAudioSegment moving = GetSegment(original.trackIndex, original.segmentIndex);
            if (moving == null)
                continue;

            float duration = Mathf.Max(0.03f, original.sourceEnd - original.sourceStart);
            minDelta = Mathf.Max(minDelta, -original.timelineStart);
            maxDelta = Mathf.Min(maxDelta, hardTimelineEnd - duration - original.timelineStart);

            if (selectedPackage == null || selectedPackage.tracks == null || original.trackIndex < 0 || original.trackIndex >= selectedPackage.tracks.Count)
                continue;

            SkyPrisonAudioTrack track = selectedPackage.tracks[original.trackIndex];
            if (track == null || track.segments == null)
                continue;

            float originalEnd = original.timelineStart + duration;
            for (int s = 0; s < track.segments.Count; s++)
            {
                if (s == original.segmentIndex || IsSegmentBeingDragged(original.trackIndex, s))
                    continue;

                SkyPrisonAudioSegment other = track.segments[s];
                if (other == null)
                    continue;

                float otherStart = other.timelineStart;
                float otherEnd = other.timelineStart + other.Duration;

                // 原本在左侧的片段，只能移动到对方开始之前。
                if (originalEnd <= otherStart + 0.0001f)
                {
                    maxDelta = Mathf.Min(maxDelta, otherStart - duration - original.timelineStart);
                }
                // 原本在右侧的片段，只能移动到对方结束之后。
                else if (original.timelineStart >= otherEnd - 0.0001f)
                {
                    minDelta = Mathf.Max(minDelta, otherEnd - original.timelineStart);
                }
            }
        }

        if (minDelta > maxDelta)
            return 0f;

        return Mathf.Clamp(deltaSeconds, minDelta, maxDelta);
    }

    private bool IsSegmentBeingDragged(int trackIndex, int segmentIndex)
    {
        for (int i = 0; i < segmentDragOriginals.Count; i++)
        {
            SegmentDragOriginal original = segmentDragOriginals[i];
            if (original.trackIndex == trackIndex && original.segmentIndex == segmentIndex)
                return true;
        }

        return false;
    }

    private float GetPreviousSegmentEnd(int trackIndex, int segmentIndex, float originalStart)
    {
        float result = 0f;
        if (selectedPackage == null || selectedPackage.tracks == null || trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
            return result;

        SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
        if (track == null || track.segments == null)
            return result;

        for (int i = 0; i < track.segments.Count; i++)
        {
            if (i == segmentIndex || IsSegmentBeingDragged(trackIndex, i))
                continue;

            SkyPrisonAudioSegment other = track.segments[i];
            if (other == null)
                continue;

            float otherEnd = other.timelineStart + other.Duration;
            if (otherEnd <= originalStart + 0.0001f)
                result = Mathf.Max(result, otherEnd);
        }

        return result;
    }

    private float GetNextSegmentStart(int trackIndex, int segmentIndex, float originalEnd)
    {
        float result = GetTimelineTotalLengthSeconds();
        if (selectedPackage == null || selectedPackage.tracks == null || trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
            return result;

        SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
        if (track == null || track.segments == null)
            return result;

        for (int i = 0; i < track.segments.Count; i++)
        {
            if (i == segmentIndex || IsSegmentBeingDragged(trackIndex, i))
                continue;

            SkyPrisonAudioSegment other = track.segments[i];
            if (other == null)
                continue;

            if (other.timelineStart >= originalEnd - 0.0001f)
                result = Mathf.Min(result, other.timelineStart);
        }

        return result;
    }

    private void KeepPlaybackPlayheadVisible()
    {
        if (!previewPlaying || timelinePixelsPerSecond <= 0f || lastTimelineLaneVisibleWidth <= 1f)
            return;

        float playheadX = playheadTime * timelinePixelsPerSecond;
        float visibleStartX = trackScroll.x;
        float visibleEndX = trackScroll.x + lastTimelineLaneVisibleWidth;
        float leftMargin = 36f;
        float rightMargin = 64f;

        if (playheadX > visibleEndX - rightMargin || playheadX < visibleStartX + leftMargin)
            trackScroll.x = Mathf.Max(0f, playheadX - leftMargin);
    }

    private void EndSegmentDrag()
    {
        segmentDragMode = SegmentDragMode.None;
        draggingTrackIndex = -1;
        draggingSegmentIndex = -1;
        segmentDragOriginals.Clear();
        segmentDragFinalizing = false;
    }

    private float SnapIfNeeded(float value)
    {
        return timelineSnapEnabled ? Snap(value) : value;
    }

    private float Snap(float value)
    {
        float step = Mathf.Max(0.001f, timelineSnapInterval);
        return Mathf.Round(value / step) * step;
    }

    private string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float remain = seconds - minutes * 60f;

        if (minutes > 0)
            return $"{minutes}:{remain:00.00}";

        return $"{remain:0.00}s";
    }

    private void DrawMiniWaveform(Rect rect, SkyPrisonAudioSegment segment, float effectiveSourceEnd = -1f)
    {
        Rect waveRect = new Rect(rect.x + 3f, rect.y + 5f, Mathf.Max(1f, rect.width - 6f), Mathf.Max(1f, rect.height - 10f));
        float centerY = waveRect.y + waveRect.height * 0.5f;
        float halfHeight = waveRect.height * 0.46f;

        Color waveColor = new Color(0f, 0f, 0f, 0.46f);
        Color waveCoreColor = new Color(0f, 0f, 0f, 0.26f);

        if (segment == null || segment.sourceClip == null)
        {
            EditorGUI.DrawRect(new Rect(waveRect.x, centerY - 1f, waveRect.width, 2f), waveCoreColor);
            return;
        }

        WaveformPreviewData data = GetWaveformData(segment.sourceClip);
        if (data == null || data.min == null || data.max == null || data.min.Length == 0 || data.max.Length == 0)
        {
            EditorGUI.DrawRect(new Rect(waveRect.x, centerY - 1f, waveRect.width, 2f), waveCoreColor);
            return;
        }

        AudioClip clip = segment.sourceClip;
        float clipLength = Mathf.Max(0.001f, clip.length);

        float sourceEndForPreview = effectiveSourceEnd >= 0f ? Mathf.Min(segment.sourceEnd, effectiveSourceEnd) : segment.sourceEnd;
        float start01 = Mathf.Clamp01(segment.sourceStart / clipLength);
        float end01 = Mathf.Clamp01(sourceEndForPreview / clipLength);
        if (end01 <= start01)
            end01 = Mathf.Min(1f, start01 + 0.001f);

        int samplesOnScreen = Mathf.Clamp(Mathf.CeilToInt(waveRect.width * 2.0f), 24, 4096);
        int lastIndex = Mathf.Min(data.min.Length, data.max.Length) - 1;
        if (lastIndex <= 0)
            return;

        Vector3[] upper = new Vector3[samplesOnScreen + 1];
        Vector3[] lower = new Vector3[samplesOnScreen + 1];

        for (int i = 0; i <= samplesOnScreen; i++)
        {
            float t = i / Mathf.Max(1f, samplesOnScreen);
            float source01 = Mathf.Lerp(start01, end01, t);
            float sourceIndex = source01 * lastIndex;

            int i0 = Mathf.Clamp(Mathf.FloorToInt(sourceIndex), 0, lastIndex);
            int i1 = Mathf.Clamp(i0 + 1, 0, lastIndex);
            float blend = Mathf.Clamp01(sourceIndex - i0);

            float min = Mathf.Lerp(data.min[i0], data.min[i1], blend);
            float max = Mathf.Lerp(data.max[i0], data.max[i1], blend);

            // 轻微压缩动态范围，细小声音也能看见，但不再出现大块阶梯。
            float visualMax = Mathf.Sign(max) * Mathf.Pow(Mathf.Abs(max), 0.58f);
            float visualMin = -Mathf.Pow(Mathf.Abs(min), 0.58f);

            float x = waveRect.x + t * waveRect.width;
            float yTop = centerY - visualMax * halfHeight;
            float yBottom = centerY - visualMin * halfHeight;
            if (yBottom < yTop)
            {
                float temp = yBottom;
                yBottom = yTop;
                yTop = temp;
            }

            upper[i] = new Vector3(x, yTop, 0f);
            lower[i] = new Vector3(x, yBottom, 0f);
        }

        Handles.BeginGUI();
        Color oldColor = Handles.color;
        Handles.color = waveColor;
        for (int i = 0; i < samplesOnScreen; i++)
        {
            Handles.DrawAAConvexPolygon(upper[i], upper[i + 1], lower[i + 1], lower[i]);
        }

        Handles.color = new Color(0f, 0f, 0f, 0.32f);
        Handles.DrawAAPolyLine(1.2f, upper);
        Handles.DrawAAPolyLine(1.2f, lower);
        Handles.color = oldColor;
        Handles.EndGUI();

        EditorGUI.DrawRect(new Rect(waveRect.x, centerY - 0.5f, waveRect.width, 1f), new Color(0f, 0f, 0f, 0.18f));
    }

    private WaveformPreviewData GetWaveformData(AudioClip clip)
    {
        if (clip == null)
            return null;

        if (waveformCache.TryGetValue(clip, out WaveformPreviewData cached) && cached != null && cached.min != null && cached.max != null)
            return cached;

        const int peakCount = 8192;
        WaveformPreviewData result = new WaveformPreviewData
        {
            min = new float[peakCount],
            max = new float[peakCount]
        };

        try
        {
            int channels = Mathf.Max(1, clip.channels);
            int samples = Mathf.Max(1, clip.samples);
            int totalSamples = samples * channels;
            float[] raw = new float[totalSamples];
            clip.GetData(raw, 0);

            for (int i = 0; i < peakCount; i++)
            {
                int startSample = Mathf.FloorToInt((i / (float)peakCount) * samples);
                int endSample = Mathf.Min(samples, Mathf.CeilToInt(((i + 1f) / peakCount) * samples));

                float min = 0f;
                float max = 0f;

                for (int sample = startSample; sample < endSample; sample++)
                {
                    int baseIndex = sample * channels;
                    float mixed = 0f;

                    for (int c = 0; c < channels; c++)
                    {
                        int rawIndex = baseIndex + c;
                        if (rawIndex >= 0 && rawIndex < raw.Length)
                            mixed += raw[rawIndex];
                    }

                    mixed /= channels;
                    min = Mathf.Min(min, mixed);
                    max = Mathf.Max(max, mixed);
                }

                result.min[i] = min;
                result.max[i] = max;
            }
        }
        catch
        {
            // 压缩流式音频可能无法 GetData。失败时绘制一条细波形，避免编辑器报错。
            for (int i = 0; i < peakCount; i++)
            {
                float fallback = Mathf.Sin(i * 0.07f) * 0.08f;
                result.min[i] = -Mathf.Abs(fallback);
                result.max[i] = Mathf.Abs(fallback);
            }
        }

        waveformCache[clip] = result;
        return result;
    }

    private void DrawMixerSection(float height)
    {
        Rect rect = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
        DrawMixerSection(rect);
    }

    private void DrawMixerSection(Rect rect)
    {
        EditorGUI.DrawRect(rect, mixerBg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect content = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
        const float stripWidth = 78f;
        const float stripStep = 86f;
        const float masterWidth = 92f;
        const float masterGap = 14f;

        float stripsWidth = selectedPackage.tracks.Count * stripStep;
        Rect view = new Rect(0f, 0f, Mathf.Max(content.width, stripsWidth + masterGap + masterWidth), Mathf.Max(120f, content.height - 16f));

        mixerScroll = GUI.BeginScrollView(content, mixerScroll, view, true, false);

        float x = 0f;
        for (int i = 0; i < selectedPackage.tracks.Count; i++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[i];
            if (track == null)
                continue;

            SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
            if (channel == null)
                continue;

            DrawMixerStrip(new Rect(x, 0f, stripWidth, view.height), track, channel, i);
            x += stripStep;
        }

        if (selectedPackage.tracks.Count == 0)
            GUI.Label(new Rect(8f, 8f, view.width - 16f, 22f), "添加音轨后，下方会自动生成对应调音台条。", EditorStyles.miniLabel);

        DrawMasterMixerStrip(new Rect(Mathf.Max(x + masterGap, content.width - masterWidth - 4f), 0f, masterWidth, view.height));

        GUI.EndScrollView();
    }

    private void DrawMixerStrip(Rect rect, SkyPrisonAudioTrack track, SkyPrisonMixerChannel channel, int index)
    {
        bool selected = selectedTrackIndex == index && selectedSegmentIndex < 0;

        EditorGUI.DrawRect(rect, selected ? new Color(0.34f, 0.13f, 0.11f, 1f) : new Color(0.14f, 0.145f, 0.16f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.05f));

        GUI.Label(new Rect(rect.x + 5f, rect.y + 4f, rect.width - 10f, 18f), track.displayName, EditorStyles.miniBoldLabel);

        channel.mute = GUI.Toggle(new Rect(rect.x + 8f, rect.y + 26f, 28f, 18f), channel.mute, "M");
        track.mute = channel.mute;
        channel.solo = GUI.Toggle(new Rect(rect.x + 42f, rect.y + 26f, 28f, 18f), channel.solo, "S");
        track.solo = channel.solo;

        GUI.Label(new Rect(rect.x + 8f, rect.y + 52f, 50f, 16f), "Pan", EditorStyles.miniLabel);
        channel.pan = GUI.HorizontalSlider(new Rect(rect.x + 8f, rect.y + 68f, rect.width - 16f, 16f), channel.pan, -1f, 1f);

        Rect meterBgRect = new Rect(rect.x + rect.width - 18f, rect.y + 94f, 8f, rect.height - 126f);
        DrawMixerLevelMeter(meterBgRect, Mathf.Clamp01(channel.editorMeterPreview), false);

        Rect sliderRect = new Rect(rect.x + 24f, rect.y + 92f, 30f, rect.height - 122f);
        channel.volume = GUI.VerticalSlider(sliderRect, channel.volume, 2f, 0f);

        GUI.Label(new Rect(rect.x + 4f, rect.yMax - 26f, rect.width - 8f, 18f), LinearToDecibelLabel(channel.volume), EditorStyles.centeredGreyMiniLabel);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0)
        {
            selectedTrackIndex = index;
            selectedSegmentIndex = -1;
            Context.Repaint();
            e.Use();
        }
    }

    private void DrawMixerLevelMeter(Rect meterBgRect, float meter, bool master)
    {
        EditorGUI.DrawRect(meterBgRect, new Color(0.035f, 0.035f, 0.045f, 1f));
        DrawThinBorder(meterBgRect, new Color(1f, 1f, 1f, 0.05f));

        Rect redZone = new Rect(meterBgRect.x, meterBgRect.y, meterBgRect.width, Mathf.Max(3f, meterBgRect.height * 0.12f));
        EditorGUI.DrawRect(redZone, new Color(0.55f, 0.05f, 0.04f, 0.35f));

        meter = Mathf.Clamp01(meter);
        if (meter > 0.001f)
        {
            float fillHeight = meterBgRect.height * meter;
            Rect fillRect = new Rect(meterBgRect.x + 1f, meterBgRect.yMax - fillHeight, Mathf.Max(1f, meterBgRect.width - 2f), fillHeight);
            int steps = Mathf.Clamp(Mathf.CeilToInt(fillRect.height / 3f), 4, 40);
            Color blue = new Color(0.08f, 0.55f, 1.00f, 0.95f);
            Color violet = new Color(0.72f, 0.20f, 1.00f, 0.95f);
            Color red = new Color(1.0f, 0.08f, 0.05f, 0.95f);

            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps;
                float t1 = (i + 1f) / steps;
                float y0 = Mathf.Lerp(fillRect.yMax, fillRect.y, t1);
                float y1 = Mathf.Lerp(fillRect.yMax, fillRect.y, t0);
                Color c = t0 > 0.88f ? Color.Lerp(violet, red, Mathf.InverseLerp(0.88f, 1f, t0)) : Color.Lerp(blue, violet, t0);
                EditorGUI.DrawRect(new Rect(fillRect.x, y0, fillRect.width, Mathf.Max(1f, y1 - y0 + 1f)), c);
            }

            EditorGUI.DrawRect(new Rect(fillRect.x, fillRect.y, fillRect.width, 1f), meter > 0.95f ? new Color(1f, 0.2f, 0.16f, 1f) : new Color(0.95f, 0.85f, 1f, 0.9f));
        }

        if (master && masterPeakHold > 0.001f)
        {
            float peakY = Mathf.Lerp(meterBgRect.yMax, meterBgRect.y, Mathf.Clamp01(masterPeakHold));
            EditorGUI.DrawRect(new Rect(meterBgRect.x - 1f, peakY, meterBgRect.width + 2f, 2f), masterPeakHold > 0.95f ? new Color(1f, 0.12f, 0.08f, 1f) : new Color(0.95f, 0.85f, 1f, 1f));
        }
    }

    private void DrawMasterMixerStrip(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.11f, 0.12f, 0.16f, 1f));
        DrawThinBorder(rect, new Color(0.50f, 0.58f, 0.95f, 0.28f));

        GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 18f), "MASTER", EditorStyles.miniBoldLabel);

        masterMute = GUI.Toggle(new Rect(rect.x + 8f, rect.y + 26f, 34f, 18f), masterMute, "M");

        GUI.Label(new Rect(rect.x + 8f, rect.y + 52f, rect.width - 16f, 16f), "Out", EditorStyles.miniLabel);

        Rect meterBgRect = new Rect(rect.x + rect.width - 22f, rect.y + 78f, 12f, rect.height - 110f);
        DrawMixerLevelMeter(meterBgRect, Mathf.Clamp01(masterMeterPreview), true);

        Rect sliderRect = new Rect(rect.x + 28f, rect.y + 78f, 34f, rect.height - 108f);
        using (new EditorGUI.DisabledScope(masterMute))
        {
            masterVolume = GUI.VerticalSlider(sliderRect, masterVolume, 2f, 0f);
        }

        GUI.Label(new Rect(rect.x + 4f, rect.yMax - 26f, rect.width - 8f, 18f), masterMute ? "-∞ dB" : LinearToDecibelLabel(masterVolume), EditorStyles.centeredGreyMiniLabel);

        if (masterPeakHold > 0.95f)
            GUI.Label(new Rect(rect.x + 6f, rect.yMax - 44f, rect.width - 12f, 16f), "CLIP", GetClipLabelStyle());
    }

    private string LinearToDecibelLabel(float linear)
    {
        if (linear <= 0.0001f)
            return "-∞ dB";

        float db = 20f * Mathf.Log10(Mathf.Max(0.0001f, linear));
        return db >= 0f ? $"+{db:0.0} dB" : $"{db:0.0} dB";
    }

    private GUIStyle GetClipLabelStyle()
    {
        return new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            normal = { textColor = new Color(1f, 0.18f, 0.12f, 1f) }
        };
    }

    private void DrawSoundStateIcon(Rect rect, bool muted)
    {
        Texture2D icon = LoadEditorIcon(muted ? IconMutePath : IconSoundOnPath);

        if (icon != null)
        {
            Color oldColor = GUI.color;
            GUI.color = muted ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.90f);
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = oldColor;
        }
        else
        {
            GUI.Label(rect, muted ? "M" : "♪", EditorStyles.centeredGreyMiniLabel);
        }

        GUI.Label(rect, new GUIContent("", muted ? "当前状态：静音。按 M 解除静音。" : "当前状态：开启声音。按 M 静音。"));
    }

    private void DrawSelectionInspector()
    {
        inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll, GUILayout.MinHeight(160f));
        DrawSelectionInspectorBody();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSelectionInspectorBody()
    {
        EditorGUILayout.LabelField("属性", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        CleanupSelectedSegments();

        if (HasTimeRangeSelection())
        {
            TryGetTimeRange(out int rangeTrack, out float rangeStart, out float rangeEnd);
            string rangeOwner = rangeTrack == -2 ? "全轨道" : $"音轨 {rangeTrack + 1}";
            EditorGUILayout.HelpBox($"已选择{rangeOwner}时间区域：{FormatTime(rangeStart)} - {FormatTime(rangeEnd)}。支持 Ctrl/Cmd+C 复制、Ctrl/Cmd+X 剪切、Ctrl/Cmd+V 粘贴、Delete 删除。", MessageType.Info);
        }
        else if (selectedSegments.Count > 1)
        {
            EditorGUILayout.HelpBox($"已选择 {selectedSegments.Count} 个片段。支持 Ctrl/Cmd+C 复制、Ctrl/Cmd+X 剪切、Ctrl/Cmd+V 粘贴、Delete 删除、S 按竖线切割。", MessageType.Info);
        }
        else if (selectedTrackIndex >= 0 && selectedTrackIndex < selectedPackage.tracks.Count)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[selectedTrackIndex];

            if (selectedSegmentIndex >= 0 && track.segments != null && selectedSegmentIndex < track.segments.Count)
                DrawSegmentInspector(track, track.segments[selectedSegmentIndex]);
            else
                DrawTrackInspector(track);
        }
        else
        {
            EditorGUILayout.HelpBox("选择一条音轨、片段或调音台条后，会在这里显示详细属性。", MessageType.Info);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTrackInspector(SkyPrisonAudioTrack track)
    {
        EditorGUILayout.LabelField("音轨属性", EditorStyles.miniBoldLabel);
        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 82f;
        track.displayName = EditorGUILayout.TextField("名称", track.displayName);
        DrawReadonlyTrackId(track);

        if (ShouldShowRuntimeLayerOptions())
        {
            DrawRuntimeLayerDropdown(track);

            if (selectedPackage != null && selectedPackage.packageType == SkyPrisonAudioPackageType.Footstep)
                track.footSideCondition = (SkyPrisonAudioFootSideCondition)EditorGUILayout.EnumPopup("脚侧条件", track.footSideCondition);
        }

        track.color = EditorGUILayout.ColorField("颜色", track.color);
        track.mute = EditorGUILayout.Toggle("静音", track.mute);
        track.solo = EditorGUILayout.Toggle("独奏", track.solo);
        track.locked = EditorGUILayout.Toggle("锁定", track.locked);

        SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
        if (channel != null)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("对应调音台", EditorStyles.miniBoldLabel);
            channel.displayName = EditorGUILayout.TextField("通道名", channel.displayName);
            channel.volume = EditorGUILayout.Slider("音量", channel.volume, 0f, 2f);
            channel.pan = EditorGUILayout.Slider("声像", channel.pan, -1f, 1f);
        }
        EditorGUIUtility.labelWidth = oldLabelWidth;
    }

    private bool ShouldShowRuntimeLayerOptions()
    {
        if (selectedPackage == null)
            return false;

        return selectedPackage.packageType == SkyPrisonAudioPackageType.Footstep
            || selectedPackage.packageType == SkyPrisonAudioPackageType.Generic;
    }

    private void DrawReadonlyTrackId(SkyPrisonAudioTrack track)
    {
        if (track == null)
            return;

        if (string.IsNullOrWhiteSpace(track.trackId))
            track.trackId = Guid.NewGuid().ToString("N");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Track ID", GUILayout.Width(EditorGUIUtility.labelWidth));
        EditorGUILayout.SelectableLabel(track.trackId, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuntimeLayerDropdown(SkyPrisonAudioTrack track)
    {
        if (track == null)
            return;

        if (track.runtimeLayerKey == null)
            track.runtimeLayerKey = "";

        string[] labels = new string[RuntimeLayerOptions.Length];
        int current = 0;

        for (int i = 0; i < RuntimeLayerOptions.Length; i++)
        {
            labels[i] = RuntimeLayerOptions[i].label;
            if (RuntimeLayerOptions[i].key == track.runtimeLayerKey)
                current = i;
        }

        bool customValue = !string.IsNullOrWhiteSpace(track.runtimeLayerKey) && !RuntimeLayerOptions.Any(x => x.key == track.runtimeLayerKey);
        if (customValue)
        {
            Array.Resize(ref labels, labels.Length + 1);
            labels[labels.Length - 1] = "当前自定义：" + track.runtimeLayerKey;
            current = labels.Length - 1;
        }

        int next = EditorGUILayout.Popup("运行时层", current, labels);
        if (next >= 0 && next < RuntimeLayerOptions.Length)
        {
            track.runtimeLayerKey = RuntimeLayerOptions[next].key;
        }

        if (track.runtimeLayerKey == "custom" || customValue)
        {
            string input = EditorGUILayout.TextField("自定义层", customValue ? track.runtimeLayerKey : "");
            track.runtimeLayerKey = SanitizeRuntimeLayerKey(input);
        }

        EditorGUILayout.HelpBox("运行时层用于脚步声、攻击音效等控制器按条件激活音轨。比如脚步声检测到浅水地面时，可以只打开 surface_water 层。", MessageType.None);
    }

    private string SanitizeRuntimeLayerKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        value = value.Trim().ToLowerInvariant();
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            chars[i] = ok ? c : '_';
        }

        return new string(chars);
    }

    private void DrawSegmentInspector(SkyPrisonAudioTrack track, SkyPrisonAudioSegment segment)
    {
        EditorGUILayout.LabelField("片段属性", EditorStyles.miniBoldLabel);

        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 70f;

        segment.displayName = EditorGUILayout.TextField("名称", segment.displayName);
        segment.sourceClip = (AudioClip)EditorGUILayout.ObjectField("源 AudioClip", segment.sourceClip, typeof(AudioClip), false);

        if (segment.sourceClip != null)
        {
            EditorGUILayout.LabelField("源素材长度", segment.sourceClip.length.ToString("0.000") + " 秒");
            if (GUILayout.Button("自动设为完整 Clip", GUILayout.Width(140f)))
            {
                segment.sourceStart = 0f;
                segment.sourceEnd = segment.sourceClip.length;
            }
        }

        segment.timelineStart = Mathf.Max(0f, EditorGUILayout.FloatField("时间线起点", segment.timelineStart));
        segment.sourceStart = Mathf.Max(0f, EditorGUILayout.FloatField("源开始时间", segment.sourceStart));
        segment.sourceEnd = Mathf.Max(segment.sourceStart, EditorGUILayout.FloatField("源结束时间", segment.sourceEnd));
        EditorGUILayout.LabelField("裁剪范围", $"{FormatTime(segment.sourceStart)}  →  {FormatTime(segment.sourceEnd)}");
        EditorGUILayout.LabelField("片段时长", FormatTime(segment.Duration));

        segment.volume = EditorGUILayout.Slider("片段音量", segment.volume, 0f, 2f);
        segment.pitch = EditorGUILayout.Slider("片段音高", segment.pitch, 0.25f, 3f);
        segment.pan = EditorGUILayout.Slider("片段声像", segment.pan, -1f, 1f);
        segment.fadeIn = Mathf.Max(0f, EditorGUILayout.FloatField("淡入", segment.fadeIn));
        segment.fadeOut = Mathf.Max(0f, EditorGUILayout.FloatField("淡出", segment.fadeOut));
        segment.randomWeight = Mathf.Max(0f, EditorGUILayout.FloatField("随机权重", segment.randomWeight));
        segment.tag = EditorGUILayout.TextField("标签", segment.tag);

        segment.ClampToClip();
        EditorGUIUtility.labelWidth = oldLabelWidth;

        GUILayout.Space(6f);
        if (GUILayout.Button("删除该片段", GUILayout.Width(120f)))
        {
            track.segments.Remove(segment);
            selectedSegmentIndex = -1;
            GUIUtility.ExitGUI();
        }
    }

    private void DrawEmptyQuickStart()
    {
        GUILayout.Space(12f);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("第一版目标", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("1. 新建音声包", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("2. 添加音轨，自动生成调音台条", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("3. 给音轨添加 AudioClip 片段", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("4. 设置裁剪时间、音量、音高、声像", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("5. 后续再接波形拖拽、预览播放、脚步声系统", EditorStyles.miniLabel);
        GUILayout.Space(8f);
        if (GUILayout.Button("创建第一个音声包", GUILayout.Width(160f)))
            CreateNewPackage();
        EditorGUILayout.EndVertical();
    }

    private void OnEditorPreviewUpdate()
    {
        if (!previewPlaying)
            return;

        double now = EditorApplication.timeSinceStartup;
        double delta = now - previewLastEditorTime;
        previewLastEditorTime = now;

        playheadTime += (float)delta;
        KeepPlaybackPlayheadVisible();

        if (playheadTime >= GetTimelineTotalLengthSeconds())
        {
            if (previewLoop)
            {
                playheadTime = 0f;
                StopEditorClip(false);
                ClearPreviewActiveSegmentState();
                previewLastEditorTime = EditorApplication.timeSinceStartup;
                EnsurePreviewClipForCurrentPlayhead();
                KeepPlaybackPlayheadVisible();
                Context?.Repaint();
                return;
            }

            StopPreview();
            playheadTime = 0f;
            Context?.Repaint();
            return;
        }

        EnsurePreviewClipForCurrentPlayhead();
        UpdateMixerMetersForPlayhead();
        Context?.Repaint();
    }

    private void UpdateMixerMetersForPlayhead()
    {
        if (selectedPackage == null || selectedPackage.tracks == null)
            return;

        bool anySolo = HasAnySoloTrackOrChannel();
        float masterTarget = 0f;

        for (int t = 0; t < selectedPackage.tracks.Count; t++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[t];
            if (track == null)
                continue;

            SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
            if (channel == null)
                continue;

            bool muted = track.mute || channel.mute;
            bool solo = track.solo || channel.solo;

            float target = 0f;
            if (previewPlaying && !muted && (!anySolo || solo) && track.segments != null)
            {
                for (int s = 0; s < track.segments.Count; s++)
                {
                    SkyPrisonAudioSegment segment = track.segments[s];
                    if (segment == null || segment.sourceClip == null)
                        continue;

                    float visibleEnd = Mathf.Min(segment.timelineStart + segment.Duration, GetTimelineTotalLengthSeconds());
                    if (playheadTime < segment.timelineStart || playheadTime >= visibleEnd)
                        continue;

                    float localTime = Mathf.Clamp(playheadTime - segment.timelineStart, 0f, segment.Duration);
                    float sourceTime = Mathf.Clamp(segment.sourceStart + localTime, 0f, segment.sourceClip.length);
                    float amplitude = SampleWaveformAmplitude(segment.sourceClip, sourceTime);
                    target = Mathf.Max(target, amplitude * Mathf.Clamp01(segment.volume) * Mathf.Clamp01(channel.volume) * GetSelectedPackageMasterVolume());
                }
            }

            float rise = 0.55f;
            float fall = 0.14f;
            float lerp = target > channel.editorMeterPreview ? rise : fall;
            channel.editorMeterPreview = Mathf.Lerp(channel.editorMeterPreview, Mathf.Clamp01(target), lerp);

            masterTarget = Mathf.Max(masterTarget, target);
        }

        masterTarget = masterMute ? 0f : Mathf.Clamp01(masterTarget * Mathf.Max(0f, masterVolume));
        float masterRise = 0.55f;
        float masterFall = 0.14f;
        float masterLerp = masterTarget > masterMeterPreview ? masterRise : masterFall;
        masterMeterPreview = Mathf.Lerp(masterMeterPreview, masterTarget, masterLerp);

        if (masterTarget > masterPeakHold)
        {
            masterPeakHold = masterTarget;
            masterPeakHoldUntil = EditorApplication.timeSinceStartup + 0.65d;
        }
        else if (EditorApplication.timeSinceStartup > masterPeakHoldUntil)
        {
            masterPeakHold = Mathf.Lerp(masterPeakHold, masterTarget, 0.12f);
        }
    }

    private float SampleWaveformAmplitude(AudioClip clip, float sourceTime)
    {
        if (clip == null || clip.length <= 0f)
            return 0f;

        WaveformPreviewData data = GetWaveformData(clip);
        if (data == null || data.min == null || data.max == null || data.min.Length == 0 || data.max.Length == 0)
            return 0f;

        int last = Mathf.Min(data.min.Length, data.max.Length) - 1;
        int index = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(sourceTime / Mathf.Max(0.001f, clip.length)) * last), 0, last);
        float peak = Mathf.Max(Mathf.Abs(data.min[index]), Mathf.Abs(data.max[index]));
        return Mathf.Clamp01(Mathf.Pow(peak, 0.55f));
    }

    private void ResetMixerMeters()
    {
        if (selectedPackage == null || selectedPackage.tracks == null)
            return;

        foreach (SkyPrisonAudioTrack track in selectedPackage.tracks)
        {
            if (track == null)
                continue;

            SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
            if (channel != null)
                channel.editorMeterPreview = 0f;
        }

        masterMeterPreview = 0f;
        masterPeakHold = 0f;
        masterPeakHoldUntil = 0d;
    }

    private void TogglePreviewPlayback()
    {
        if (previewPlaying)
            PausePreview();
        else
            StartPreviewFromPlayhead();
    }

    private void ResyncPreviewAfterManualPlayheadJump()
    {
        if (!previewPlaying)
            return;

        StopPreviewAudioSourcesOnly();
        ClearPreviewActiveSegmentState();
        previewStartEditorTime = EditorApplication.timeSinceStartup;
        previewLastEditorTime = previewStartEditorTime;
        previewStartPlayhead = playheadTime;
        EnsurePreviewClipForCurrentPlayhead();
        UpdateMixerMetersForPlayhead();
    }

    private void StartPreviewFromPlayhead()
    {
        StopEditorClip(false);

        previewPlaying = true;
        previewStartEditorTime = EditorApplication.timeSinceStartup;
        previewLastEditorTime = previewStartEditorTime;
        previewStartPlayhead = playheadTime;
        ClearPreviewActiveSegmentState();

        // v28：不再混成临时 Clip。压缩/流式音频经常无法 GetData，导致混音结果为空。
        // 改为隐藏 AudioSource 组来播放原始 AudioClip，停止时直接 Destroy 根物体，控制力更强。
        EnsurePreviewClipForCurrentPlayhead();

        Context.Repaint();
    }

    private void PausePreview()
    {
        previewPlaying = false;
        StopEditorClip();
        ClearPreviewActiveSegmentState();
        ResetMixerMeters();
        Context.Repaint();
    }

    private void StopPreview()
    {
        previewPlaying = false;
        StopEditorClip();
        ClearPreviewActiveSegmentState();
        ResetMixerMeters();
    }

    private void SetPlayheadAndStopAudio(float time)
    {
        playheadTime = Mathf.Clamp(SnapIfNeeded(time), 0f, GetTimelineTotalLengthSeconds());
        PausePreview();
        KeepPlayheadVisibleAfterManualJump();
        Context.Repaint();
    }

    private void KeepPlayheadVisibleAfterManualJump()
    {
        if (timelinePixelsPerSecond <= 0f || lastTimelineLaneVisibleWidth <= 1f)
            return;

        float playheadX = playheadTime * timelinePixelsPerSecond;
        float visibleStartX = trackScroll.x;
        float visibleEndX = trackScroll.x + lastTimelineLaneVisibleWidth;

        // 手动跳转时，如果红线已经离开当前视野，让横向滚动条一起跳过去。
        // 红线放在视野左侧约 1/4 的位置，方便继续向后查看。
        if (playheadX < visibleStartX + 24f || playheadX > visibleEndX - 48f)
            trackScroll.x = Mathf.Max(0f, playheadX - lastTimelineLaneVisibleWidth * 0.25f);
    }

    private void JumpPreviewToPlayhead()
    {
        StopEditorClip(!previewPlaying);
        ClearPreviewActiveSegmentState();
        if (previewPlaying)
            StartPreviewFromPlayhead();
        else
            Context.Repaint();
    }

    private void EnsurePreviewClipForCurrentPlayhead()
    {
        if (!previewPlaying)
            return;

        List<PreviewSegmentToPlay> activeSegments = CollectActiveSegmentsForPreview(playheadTime);

        bool sameSet = activeSegments.Count == previewActiveSegmentKeys.Count;
        if (sameSet)
        {
            for (int i = 0; i < activeSegments.Count; i++)
            {
                if (previewActiveSegmentKeys[i] != activeSegments[i].key)
                {
                    sameSet = false;
                    break;
                }
            }
        }

        if (sameSet)
            return;

        StopPreviewAudioSourcesOnly();
        ClearPreviewActiveSegmentState();

        for (int i = 0; i < activeSegments.Count; i++)
        {
            PreviewSegmentToPlay item = activeSegments[i];
            PlayPreviewSourceEntry(item);
            previewActiveSegmentKeys.Add(item.key);
        }

        if (activeSegments.Count > 0)
        {
            previewActiveTrackIndex = activeSegments[0].trackIndex;
            previewActiveSegmentIndex = activeSegments[0].segmentIndex;
            previewActiveSegment = activeSegments[0].segment;
        }
    }

    private void ClearPreviewActiveSegmentState()
    {
        previewActiveTrackIndex = -1;
        previewActiveSegmentIndex = -1;
        previewActiveSegment = null;
        previewActiveSegmentKeys.Clear();
    }

    private struct PreviewSegmentToPlay
    {
        public string key;
        public int trackIndex;
        public int segmentIndex;
        public SkyPrisonAudioSegment segment;
        public float clipTime;
    }

    private List<PreviewSegmentToPlay> CollectActiveSegmentsForPreview(float time)
    {
        List<PreviewSegmentToPlay> result = new List<PreviewSegmentToPlay>();

        if (selectedPackage == null || selectedPackage.tracks == null)
            return result;

        bool anySolo = HasAnySoloTrackOrChannel();
        float hardTimelineEnd = GetTimelineTotalLengthSeconds();

        for (int t = 0; t < selectedPackage.tracks.Count; t++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[t];
            if (track == null || track.segments == null)
                continue;

            SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
            bool muted = track.mute || (channel != null && channel.mute);
            bool solo = track.solo || (channel != null && channel.solo);

            if (muted)
                continue;

            if (anySolo && !solo)
                continue;

            for (int s = 0; s < track.segments.Count; s++)
            {
                SkyPrisonAudioSegment segment = track.segments[s];
                if (segment == null || segment.sourceClip == null)
                    continue;

                float visibleSegmentEnd = Mathf.Min(segment.timelineStart + segment.Duration, hardTimelineEnd);
                if (time < segment.timelineStart || time >= visibleSegmentEnd)
                    continue;

                float localTime = Mathf.Clamp(time - segment.timelineStart, 0f, segment.Duration);
                float clipTime = Mathf.Clamp(segment.sourceStart + localTime, 0f, segment.sourceClip.length);

                result.Add(new PreviewSegmentToPlay
                {
                    key = t + ":" + s + ":" + segment.segmentId,
                    trackIndex = t,
                    segmentIndex = s,
                    segment = segment,
                    clipTime = clipTime
                });
            }
        }

        return result;
    }

    private bool HasAnySoloTrackOrChannel()
    {
        if (selectedPackage == null || selectedPackage.tracks == null)
            return false;

        for (int t = 0; t < selectedPackage.tracks.Count; t++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[t];
            if (track == null)
                continue;

            SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
            if (track.solo || (channel != null && channel.solo))
                return true;
        }

        return false;
    }

    private SkyPrisonAudioSegment GetActiveSegmentForPreview(float time)
    {
        List<PreviewSegmentToPlay> active = CollectActiveSegmentsForPreview(time);
        return active.Count > 0 ? active[0].segment : null;
    }

    private bool TryGetActiveSegmentForPreview(float time, out int trackIndex, out int segmentIndex, out SkyPrisonAudioSegment result)
    {
        List<PreviewSegmentToPlay> active = CollectActiveSegmentsForPreview(time);
        if (active.Count > 0)
        {
            trackIndex = active[0].trackIndex;
            segmentIndex = active[0].segmentIndex;
            result = active[0].segment;
            return true;
        }

        trackIndex = -1;
        segmentIndex = -1;
        result = null;
        return false;
    }

    private void PlayPreviewSourceEntry(PreviewSegmentToPlay item)
    {
        if (item.segment == null || item.segment.sourceClip == null)
            return;

        EnsurePreviewAudioRoot();

        GameObject sourceObject = new GameObject("Preview_" + item.key);
        sourceObject.hideFlags = HideFlags.HideAndDontSave;
        sourceObject.transform.SetParent(previewAudioRoot.transform, false);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.hideFlags = HideFlags.HideAndDontSave;
        source.playOnAwake = false;
        source.loop = false;
        source.clip = item.segment.sourceClip;
        source.volume = masterMute ? 0f : Mathf.Clamp01(item.segment.volume) * GetPreviewChannelVolume(item.trackIndex) * GetSelectedPackageMasterVolume() * Mathf.Clamp01(masterVolume);
        source.pitch = Mathf.Max(0.01f, item.segment.pitch);
        source.panStereo = Mathf.Clamp(item.segment.pan + GetPreviewChannelPan(item.trackIndex), -1f, 1f);
        source.spatialBlend = 0f;
        source.time = Mathf.Clamp(item.clipTime, 0f, Mathf.Max(0f, item.segment.sourceClip.length - 0.001f));

        previewAudioSourceEntries.Add(new PreviewAudioSourceEntry
        {
            key = item.key,
            source = source
        });

        source.Play();
    }

    private float GetPreviewChannelVolume(int trackIndex)
    {
        if (selectedPackage == null || selectedPackage.tracks == null || trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
            return 1f;

        SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
        SkyPrisonMixerChannel channel = track != null ? selectedPackage.FindMixerChannel(track.mixerChannelId) : null;
        return channel != null ? Mathf.Clamp01(channel.volume) : 1f;
    }

    private float GetSelectedPackageMasterVolume()
    {
        return selectedPackage != null ? Mathf.Max(0f, selectedPackage.masterVolume) : 1f;
    }

    private float GetPreviewChannelPan(int trackIndex)
    {
        if (selectedPackage == null || selectedPackage.tracks == null || trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
            return 0f;

        SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
        SkyPrisonMixerChannel channel = track != null ? selectedPackage.FindMixerChannel(track.mixerChannelId) : null;
        return channel != null ? Mathf.Clamp(channel.pan, -1f, 1f) : 0f;
    }

    private void EnsurePreviewAudioRoot()
    {
        if (previewAudioRoot != null)
            return;

        previewAudioRoot = new GameObject("SkyPrison Audio Workshop Preview");
        previewAudioRoot.hideFlags = HideFlags.HideAndDontSave;
    }

    private void StopPreviewAudioSourcesOnly()
    {
        for (int i = 0; i < previewAudioSourceEntries.Count; i++)
        {
            PreviewAudioSourceEntry entry = previewAudioSourceEntries[i];
            if (entry == null || entry.source == null)
                continue;

            entry.source.Stop();
            if (entry.source.gameObject != null)
                UnityEngine.Object.DestroyImmediate(entry.source.gameObject);
        }

        previewAudioSourceEntries.Clear();

        if (previewAudioRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(previewAudioRoot);
            previewAudioRoot = null;
        }
    }

    private void PlayEditorClip(AudioClip clip, float startTimeSeconds)
    {
        if (clip == null)
            return;

        Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtil == null)
            return;

        int samplePosition = Mathf.Clamp(Mathf.RoundToInt(startTimeSeconds * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));

        MethodInfo playMethod = audioUtil.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (playMethod != null)
        {
            playMethod.Invoke(null, new object[] { clip, samplePosition, false });
            return;
        }

        playMethod = audioUtil.GetMethod("PlayClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (playMethod != null)
        {
            playMethod.Invoke(null, new object[] { clip, samplePosition, false });
        }
    }

    private void StopEditorClip(bool scheduleDelayedStop = true)
    {
        StopPreviewAudioSourcesOnly();

        // 兜底：如果之前版本留下了 AudioUtil PreviewClip，也一起收掉。
        Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtil == null)
            return;

        BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        TryInvokeAudioUtil(audioUtil, "StopAllPreviewClips", flags);
        TryInvokeAudioUtil(audioUtil, "StopAllClips", flags);

        if (scheduleDelayedStop)
        {
            EditorApplication.delayCall += () =>
            {
                StopPreviewAudioSourcesOnly();

                Type delayedAudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                if (delayedAudioUtil == null)
                    return;

                BindingFlags delayedFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                TryInvokeAudioUtil(delayedAudioUtil, "StopAllPreviewClips", delayedFlags);
                TryInvokeAudioUtil(delayedAudioUtil, "StopAllClips", delayedFlags);
            };
        }
    }

    private bool TryInvokeAudioUtil(Type audioUtil, string methodName, BindingFlags flags, AudioClip clip = null)
    {
        MethodInfo[] methods = audioUtil.GetMethods(flags);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method == null || method.Name != methodName)
                continue;

            ParameterInfo[] parameters = method.GetParameters();

            try
            {
                if (clip == null && parameters.Length == 0)
                {
                    method.Invoke(null, null);
                    return true;
                }

                if (clip != null && parameters.Length >= 1 && parameters[0].ParameterType == typeof(AudioClip))
                {
                    object[] args = new object[parameters.Length];
                    args[0] = clip;

                    for (int a = 1; a < parameters.Length; a++)
                    {
                        Type parameterType = parameters[a].ParameterType;
                        if (parameterType == typeof(int))
                            args[a] = 0;
                        else if (parameterType == typeof(bool))
                            args[a] = false;
                        else if (parameterType.IsValueType)
                            args[a] = Activator.CreateInstance(parameterType);
                        else
                            args[a] = null;
                    }

                    method.Invoke(null, args);
                    return true;
                }
            }
            catch
            {
                // 继续尝试其他重载。
            }
        }

        return false;
    }

    private enum AudioExportFormat
    {
        Wav,
        Mp3,
        Ogg
    }

    private class MixedAudioData
    {
        public float[] samples;
        public int sampleRate;
        public int channels;
        public int skippedClips;
    }

    private void ShowExportAudioMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("输出为 WAV (.wav)"), false, () => ExportAudioPackage(AudioExportFormat.Wav));
        menu.AddItem(new GUIContent("输出为 MP3 (.mp3) / 需要 ffmpeg"), false, () => ExportAudioPackage(AudioExportFormat.Mp3));
        menu.AddItem(new GUIContent("输出为 OGG (.ogg) / 需要 ffmpeg"), false, () => ExportAudioPackage(AudioExportFormat.Ogg));
        menu.ShowAsContext();
    }

    private void ExportAudioPackage(AudioExportFormat format)
    {
        if (selectedPackage == null)
        {
            EditorUtility.DisplayDialog("输出音声包", "请先选择一个音声包。", "确定");
            return;
        }

        string ext = format == AudioExportFormat.Wav ? "wav" : format == AudioExportFormat.Mp3 ? "mp3" : "ogg";
        string defaultName = SanitizeFileName(string.IsNullOrWhiteSpace(selectedPackage.displayName) ? selectedPackage.name : selectedPackage.displayName);
        string path = EditorUtility.SaveFilePanel($"输出音声包为 {ext.ToUpperInvariant()}", DefaultBakedAudioFolder, defaultName + "." + ext, ext);
        if (string.IsNullOrEmpty(path))
            return;

        StopPreview();

        MixedAudioData mixed = BuildMixedAudioDataForExport(44100, 2);
        if (mixed == null || mixed.samples == null || mixed.samples.Length == 0)
        {
            EditorUtility.DisplayDialog("输出失败", "没有可输出的音频数据。请确认音轨中存在有效 AudioClip，且没有全部静音。", "确定");
            return;
        }

        string wavPath = path;
        bool tempWav = false;

        try
        {
            if (format == AudioExportFormat.Wav)
            {
                WriteWavFile(path, mixed.samples, mixed.sampleRate, mixed.channels);
            }
            else
            {
                wavPath = Path.Combine(Path.GetTempPath(), "SkyPrisonAudioExport_" + Guid.NewGuid().ToString("N") + ".wav");
                tempWav = true;
                WriteWavFile(wavPath, mixed.samples, mixed.sampleRate, mixed.channels);

                if (!ConvertWavWithFfmpeg(wavPath, path, format, out string error))
                {
                    EditorUtility.DisplayDialog(
                        "输出需要 ffmpeg",
                        "Unity 本身没有内置 MP3 / OGG 编码器。\n\n我已经准备好了导出流程，但当前没有成功调用 ffmpeg。\n\n解决方式：\n1. 安装 ffmpeg 并加入系统 PATH；或\n2. 先输出 WAV，再用外部工具转成 MP3 / OGG。\n\n错误信息：\n" + error,
                        "确定");
                    return;
                }
            }

            AssetDatabase.Refresh();

            string warning = mixed.skippedClips > 0
                ? $"\n\n注意：有 {mixed.skippedClips} 个 AudioClip 无法读取采样，可能是压缩/流式导入设置导致，已跳过。需要把对应 AudioClip 的 Load Type 改为 Decompress On Load 后再导出。"
                : "";

            EditorUtility.DisplayDialog("输出完成", $"已输出：\n{path}{warning}", "确定");
            EditorUtility.RevealInFinder(path);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("输出失败", ex.Message, "确定");
        }
        finally
        {
            if (tempWav && File.Exists(wavPath))
            {
                try { File.Delete(wavPath); } catch { }
            }
        }
    }

    private MixedAudioData BuildMixedAudioDataForExport(int sampleRate, int outputChannels)
    {
        float timelineEnd = GetTimelineTotalLengthSeconds();
        int outputSamples = Mathf.Max(1, Mathf.CeilToInt(timelineEnd * sampleRate));
        float[] mix = new float[outputSamples * outputChannels];

        MixedAudioData result = new MixedAudioData
        {
            samples = mix,
            sampleRate = sampleRate,
            channels = outputChannels,
            skippedClips = 0
        };

        if (selectedPackage == null || selectedPackage.tracks == null)
            return result;

        bool anySolo = HasAnySoloTrackOrChannel();
        bool wroteAnySample = false;

        for (int t = 0; t < selectedPackage.tracks.Count; t++)
        {
            SkyPrisonAudioTrack track = selectedPackage.tracks[t];
            if (track == null || track.segments == null)
                continue;

            SkyPrisonMixerChannel channel = selectedPackage.FindMixerChannel(track.mixerChannelId);
            bool muted = masterMute || track.mute || (channel != null && channel.mute);
            bool solo = track.solo || (channel != null && channel.solo);
            if (muted || (anySolo && !solo))
                continue;

            float channelVolume = channel != null ? Mathf.Max(0f, channel.volume) : 1f;
            float channelPan = channel != null ? Mathf.Clamp(channel.pan, -1f, 1f) : 0f;

            for (int s = 0; s < track.segments.Count; s++)
            {
                SkyPrisonAudioSegment segment = track.segments[s];
                if (segment == null || segment.sourceClip == null)
                    continue;

                float segmentStart = segment.timelineStart;
                float segmentEnd = Mathf.Min(segment.timelineStart + segment.Duration, timelineEnd);
                if (segmentEnd <= 0f || segmentStart >= timelineEnd || segmentEnd <= segmentStart)
                    continue;

                AudioClip source = segment.sourceClip;
                int sourceChannels = Mathf.Max(1, source.channels);
                int sourceSamples = Mathf.Max(1, source.samples);
                float[] sourceData = new float[sourceSamples * sourceChannels];

                try
                {
                    source.GetData(sourceData, 0);
                }
                catch
                {
                    result.skippedClips++;
                    continue;
                }

                int outStart = Mathf.Clamp(Mathf.FloorToInt(segmentStart * sampleRate), 0, outputSamples - 1);
                int outEnd = Mathf.Clamp(Mathf.CeilToInt(segmentEnd * sampleRate), outStart, outputSamples);

                float volume = Mathf.Max(0f, segment.volume) * channelVolume * GetSelectedPackageMasterVolume() * Mathf.Max(0f, masterVolume);
                float combinedPan = Mathf.Clamp(segment.pan + channelPan, -1f, 1f);
                float leftGain = combinedPan <= 0f ? 1f : 1f - combinedPan;
                float rightGain = combinedPan >= 0f ? 1f : 1f + combinedPan;

                for (int outSample = outStart; outSample < outEnd; outSample++)
                {
                    float timelineTime = outSample / (float)sampleRate;
                    float localTime = timelineTime - segment.timelineStart;
                    float sourceTime = segment.sourceStart + localTime * Mathf.Max(0.01f, segment.pitch);

                    if (sourceTime < segment.sourceStart || sourceTime >= segment.sourceEnd || sourceTime < 0f || sourceTime >= source.length)
                        continue;

                    float sourceSampleFloat = sourceTime * source.frequency;
                    int sourceSampleIndex = Mathf.Clamp(Mathf.FloorToInt(sourceSampleFloat), 0, sourceSamples - 1);
                    int baseIndex = sourceSampleIndex * sourceChannels;

                    float mono = 0f;
                    for (int c = 0; c < sourceChannels; c++)
                    {
                        int rawIndex = baseIndex + c;
                        if (rawIndex >= 0 && rawIndex < sourceData.Length)
                            mono += sourceData[rawIndex];
                    }
                    mono /= sourceChannels;

                    float fade = 1f;
                    float segmentLocalDuration = Mathf.Max(0.001f, segment.Duration);
                    if (segment.fadeIn > 0.001f)
                        fade = Mathf.Min(fade, Mathf.Clamp01(localTime / segment.fadeIn));
                    if (segment.fadeOut > 0.001f)
                        fade = Mathf.Min(fade, Mathf.Clamp01((segmentLocalDuration - localTime) / segment.fadeOut));

                    float sample = mono * volume * fade;
                    int outBase = outSample * outputChannels;
                    mix[outBase] += sample * leftGain;
                    if (outputChannels > 1)
                        mix[outBase + 1] += sample * rightGain;

                    wroteAnySample = true;
                }
            }
        }

        if (!wroteAnySample)
            return result;

        NormalizeIfNeeded(mix);
        return result;
    }

    private void NormalizeIfNeeded(float[] samples)
    {
        if (samples == null || samples.Length == 0)
            return;

        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));

        if (peak <= 1f)
            return;

        float gain = 1f / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }

    private void WriteWavFile(string path, float[] samples, int sampleRate, int channels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        const int bytesPerSample = 2;
        int sampleCount = samples.Length;
        int dataSize = sampleCount * bytesPerSample;
        int byteRate = sampleRate * channels * bytesPerSample;
        short blockAlign = (short)(channels * bytesPerSample);

        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });

            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)(bytesPerSample * 8));

            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short value = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                writer.Write(value);
            }
        }
    }

    private bool ConvertWavWithFfmpeg(string wavPath, string outputPath, AudioExportFormat format, out string error)
    {
        error = "";

        string codecArgs = format == AudioExportFormat.Mp3
            ? "-codec:a libmp3lame -b:a 192k"
            : "-codec:a libvorbis -q:a 5";

        try
        {
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{wavPath}\" {codecArgs} \"{outputPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
            {
                process.WaitForExit();
                string stdErr = process.StandardError.ReadToEnd();
                if (process.ExitCode != 0)
                {
                    error = stdErr;
                    return false;
                }
            }

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "AudioPackage";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }

    private void DrawProperty(string label, string propertyName)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        float oldLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 70f;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(70f));
        if (prop != null)
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        else
            EditorGUILayout.LabelField("字段不存在");
        EditorGUILayout.EndHorizontal();

        EditorGUIUtility.labelWidth = oldLabelWidth;
    }

    private void AddTrack()
    {
        Undo.RecordObject(selectedPackage, "Add Audio Track");
        SkyPrisonAudioTrack track = selectedPackage.AddTrack("Track " + (selectedPackage.tracks.Count + 1));
        selectedTrackIndex = selectedPackage.tracks.IndexOf(track);
        selectedTrackIndices.Clear();
        selectedTrackIndices.Add(selectedTrackIndex);
        selectedSegmentIndex = -1;
        EditorUtility.SetDirty(selectedPackage);
    }

    private void RemoveSelectedTrack()
    {
        RemoveTrackAtIndex(selectedTrackIndex);
    }

    private void RemoveTrackAtIndex(int trackIndex)
    {
        if (selectedPackage == null || trackIndex < 0 || trackIndex >= selectedPackage.tracks.Count)
            return;

        SkyPrisonAudioTrack track = selectedPackage.tracks[trackIndex];
        if (track != null && track.locked)
            return;

        Undo.RecordObject(selectedPackage, "Remove Audio Track");
        selectedPackage.RemoveTrackAt(trackIndex);

        selectedTrackIndex = Mathf.Clamp(trackIndex - 1, -1, selectedPackage.tracks.Count - 1);
        selectedTrackIndices.Clear();
        if (selectedTrackIndex >= 0)
            selectedTrackIndices.Add(selectedTrackIndex);

        selectedSegmentIndex = -1;
        selectedSegments.Clear();
        selectedSegmentIndex = -1;
        dragHoverTrackIndex = -1;
        EditorUtility.SetDirty(selectedPackage);
        Context.Repaint();
    }

    private void AddSegmentToSelectedTrack()
    {
        if (selectedPackage == null || selectedTrackIndex < 0 || selectedTrackIndex >= selectedPackage.tracks.Count)
            return;

        SkyPrisonAudioTrack track = selectedPackage.tracks[selectedTrackIndex];
        if (track.segments == null)
            track.segments = new List<SkyPrisonAudioSegment>();

        Undo.RecordObject(selectedPackage, "Add Audio Segment");

        SkyPrisonAudioSegment segment = new SkyPrisonAudioSegment
        {
            displayName = "Segment " + (track.segments.Count + 1),
            timelineStart = track.segments.Count * 0.15f,
            sourceStart = 0f,
            sourceEnd = 1f
        };

        track.segments.Add(segment);
        selectedSegmentIndex = track.segments.Count - 1;
        selectedSegments.Clear();
        selectedSegments.Add(new SegmentRef(selectedTrackIndex, selectedSegmentIndex));
        EditorUtility.SetDirty(selectedPackage);
    }

    private void CreateNewPackage()
    {
        EnsureFolderExists(DefaultAudioPackageFolder);

        SkyPrisonAudioPackage asset = ScriptableObject.CreateInstance<SkyPrisonAudioPackage>();
        asset.packageKey = GenerateUniquePackageKey("new_audio_package");
        asset.displayName = "新音声包";
        asset.AddTrack("Track 1");

        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultAudioPackageFolder + "/SAP_NewAudioPackage.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        SelectPackage(asset);
    }

    private void DeleteSelectedPackage()
    {
        if (selectedPackage == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedPackage);
        bool ok = EditorUtility.DisplayDialog("删除音声包", "确定删除当前音声包？\n" + path, "删除", "取消");
        if (!ok)
            return;

        AssetDatabase.DeleteAsset(path);
        selectedPackage = null;
        selectedSO = null;
        selectedTrackIndex = -1;
        selectedSegmentIndex = -1;
        selectedTrackIndices.Clear();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Refresh();
    }

    private void SelectPackage(SkyPrisonAudioPackage package)
    {
        if (selectedSO != null)
        {
            selectedSO.ApplyModifiedProperties();
            selectedSO = null;
        }

        // V3: switching from the left package list must clear all editor-side selection/focus state.
        // Otherwise Unity can keep the previous TextField editing buffer and the right inspector can
        // continue showing the previous track/segment/time-range selection for one or more repaints.
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (previewPlaying)
            StopPreview();

        selectedPackage = package;
        if (selectedPackage != null)
        {
            selectedPackage.EnsureValid();
            EditorUtility.SetDirty(selectedPackage);
            selectedSO = new SerializedObject(selectedPackage);
        }

        selectedTrackIndex = selectedPackage != null && selectedPackage.tracks != null && selectedPackage.tracks.Count > 0 ? 0 : -1;
        selectedSegmentIndex = -1;
        selectedTrackIndices.Clear();
        if (selectedTrackIndex >= 0)
            selectedTrackIndices.Add(selectedTrackIndex);

        selectedSegments.Clear();
        timeRangeTrackIndex = -1;
        timeRangeAnchorTime = 0f;
        timeRangeStart = 0f;
        timeRangeEnd = 0f;
        draggingTimeRange = false;
        segmentDragMode = SegmentDragMode.None;
        draggingTrackIndex = -1;
        draggingSegmentIndex = -1;

        playheadTime = 0f;
        trackScroll = Vector2.zero;
        inspectorScroll = Vector2.zero;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    private void EnsureSelectedSO()
    {
        if (selectedPackage == null)
        {
            selectedSO = null;
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedPackage)
            selectedSO = new SerializedObject(selectedPackage);
    }

    private void ShowPackageContextMenu(SkyPrisonAudioPackage package)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("复制包 Key"), false, () => EditorGUIUtility.systemCopyBuffer = package != null ? package.packageKey : "");
        menu.AddItem(new GUIContent("复制路径"), false, () => EditorGUIUtility.systemCopyBuffer = AssetDatabase.GetAssetPath(package));
        menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        {
            Selection.activeObject = package;
            EditorGUIUtility.PingObject(package);
        });
        menu.AddItem(new GUIContent("同步资产名"), false, () =>
        {
            SelectPackage(package);
            SyncSelectedPackageAssetNameWithIdentity();
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, DeleteSelectedPackage);
        menu.ShowAsContext();
    }

    private string GenerateUniquePackageKey(string baseKey)
    {
        string key = baseKey;
        int index = 1;
        while (packages.Any(x => x != null && x.packageKey == key))
        {
            key = baseKey + "_" + index;
            index++;
        }

        return key;
    }

    private string GetSegmentLabel(SkyPrisonAudioSegment segment)
    {
        if (segment == null)
            return "-";

        if (!string.IsNullOrWhiteSpace(segment.displayName))
            return segment.displayName;

        if (segment.sourceClip != null)
            return segment.sourceClip.name;

        return "Empty Segment";
    }

    private string GetPackageTypeLabel(SkyPrisonAudioPackageType type)
    {
        switch (type)
        {
            case SkyPrisonAudioPackageType.Footstep: return "脚步声";
            case SkyPrisonAudioPackageType.Combat: return "战斗音效";
            case SkyPrisonAudioPackageType.UI: return "界面音效";
            case SkyPrisonAudioPackageType.Ambience: return "环境音";
            case SkyPrisonAudioPackageType.BGM: return "BGM";
            case SkyPrisonAudioPackageType.Voice: return "语音";
            case SkyPrisonAudioPackageType.Generic:
            default:
                return "通用";
        }
    }

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }
}
