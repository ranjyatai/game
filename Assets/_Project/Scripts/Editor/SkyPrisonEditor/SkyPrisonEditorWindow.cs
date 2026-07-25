using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SkyPrisonEditorWindow : EditorWindow
{
    private const string PrefKeyDrawerExpanded = "SkyPrison.Editor.DrawerExpanded";
    private const string PrefKeyShowHomeScreen = "SkyPrison.Editor.ShowHomeScreen";

    private const float TopQuickTabWidth = 92f;

    private const string EditorIconFolder = "Assets/_Project/Icon/Editor/";
    private const string EditorIconPrefix = "SkyPrisonEditor_";

    private const float HomeLogoScale = 1.2f;
    private const float DrawerItemIconSize = 18f;

    private const float DrawerToggleButtonVisualSize = 28f;
    private const float DrawerToggleButtonHitSize = 40f;
    private const float DrawerToggleButtonMarginBottom = 12f;

    private const float DrawerSmoothTime = 0.12f;

    private static readonly Color DrawerPanelColor = new Color(0.14f, 0.14f, 0.14f, 1f);
    private static readonly Color DrawerBorderColor = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color DrawerItemSelectedColor = new Color(0.36f, 0.36f, 0.36f, 1f);
    private static readonly Color DrawerItemHoverColor = new Color(0.24f, 0.24f, 0.24f, 1f);

    private static readonly Color DrawerToggleButtonBg = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color DrawerToggleButtonBorder = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color DrawerToggleButtonText = new Color(0.88f, 0.88f, 0.88f, 1f);

    private SkyPrisonEditorContext context;

    private readonly List<SkyPrisonEditorPageBase> pages = new List<SkyPrisonEditorPageBase>();
    private readonly List<Texture2D> drawerIcons = new List<Texture2D>();

    private int currentTabIndex = 0;
    private int lastMainEditorTabIndex = 0;

    private static SkyPrisonEditorWindow activeWindow;
    private static bool reopenAfterScenePick;
    private static string reopenTabNameAfterScenePick;

    private bool drawerExpanded;
    private float drawerAnimT;
    private float drawerAnimVelocity;
    private bool showHomeScreen = true;

    private Texture2D homeLogoTexture;
    private bool homeLogoLoaded;

    private double lastAnimTime;

    [MenuItem("Tools/天空囚笼编辑器")]
    public static void OpenWindow()
    {
        OpenWindowWithTab(null, null);
    }

    public static void OpenWindowWithTab(string tabName, Object targetObject = null)
    {
        SkyPrisonEditorWindow window = GetWindow<SkyPrisonEditorWindow>("天空囚笼编辑器");
        window.minSize = new Vector2(1100f, 700f);
        window.Show();
        window.Focus();

        if (!string.IsNullOrWhiteSpace(tabName))
            window.SwitchToTab(tabName, targetObject);
    }

    public static void HideForScenePick()
    {
        if (activeWindow == null)
            return;

        reopenAfterScenePick = true;
        reopenTabNameAfterScenePick = activeWindow.CurrentPage != null ? activeWindow.CurrentPage.TabName : null;

        activeWindow.Close();
        activeWindow = null;
    }

    public static void RestoreAfterScenePick()
    {
        if (!reopenAfterScenePick)
            return;

        reopenAfterScenePick = false;

        string tabName = reopenTabNameAfterScenePick;
        reopenTabNameAfterScenePick = null;

        OpenWindowWithTab(tabName, null);
    }

    private void SwitchToTab(string tabName, Object targetObject = null)
    {
        if (pages == null || pages.Count == 0)
            return;

        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] != null && pages[i].TabName == tabName)
            {
                currentTabIndex = i;
                lastMainEditorTabIndex = i;

                if (context != null)
                {
                    context.LeftScroll = Vector2.zero;
                    context.RightScroll = Vector2.zero;
                }

                if (targetObject != null)
                    pages[i].TrySelectObject(targetObject);

                showHomeScreen = false;
                SaveWindowPrefs();
                Repaint();
                break;
            }
        }
    }

    private void OnEnable()
    {
        activeWindow = this;

        context = new SkyPrisonEditorContext(this);

        drawerExpanded = EditorPrefs.GetBool(PrefKeyDrawerExpanded, false);
        drawerAnimT = drawerExpanded ? 1f : 0f;
        drawerAnimVelocity = 0f;
        showHomeScreen = EditorPrefs.GetBool(PrefKeyShowHomeScreen, true);
        lastAnimTime = EditorApplication.timeSinceStartup;

        pages.Clear();
        pages.Add(new SkyPrisonUnitDefinitionPage(context));
        pages.Add(new SkyPrisonItemDefinitionPage(context));
        pages.Add(new SkyPrisonItemPoolPage(context));
        pages.Add(new SkyPrisonSkillPage(context));
        pages.Add(new SkyPrisonStatusPage(context));
        pages.Add(new SkyPrisonTerrainDecorationDefinitionPage(context));
        pages.Add(new SkyPrisonGroundSurfaceMaterialPage(context));
        pages.Add(new SkyPrisonMapEditorPage(context));
        pages.Add(new SkyPrisonPlaceholderPage(context, "特效", "特效页面开发中。"));
        pages.Add(new SkyPrisonAudioWorkshopPage(context));
        pages.Add(new SkyPrisonTechTreePage(context));
        pages.Add(new SkyPrisonTriggerPage(context));
        pages.Add(new SkyPrisonAIPage(context));
        pages.Add(new SkyPrisonBattleParametersPage(context));
        pages.Add(new SkyPrisonLocalizationPage(context));

        foreach (var page in pages)
            page.OnEnable();

        LoadDrawerIcons();
    }

    private void OnDisable()
    {
        SaveWindowPrefs();

        foreach (var page in pages)
            page.OnDisable();

        if (activeWindow == this)
            activeWindow = null;
    }

    private void OnDestroy()
    {
        SaveWindowPrefs();

        if (activeWindow == this)
            activeWindow = null;
    }

    private void SaveWindowPrefs()
    {
        EditorPrefs.SetBool(PrefKeyDrawerExpanded, drawerExpanded);
        EditorPrefs.SetBool(PrefKeyShowHomeScreen, showHomeScreen);
    }

    private void OnGUI()
    {
        if (context == null)
            OnEnable();

        UpdateDrawerAnimation();
        HandleEditorWindowUndoRedoHotkeys();

        if (!showHomeScreen)
            CurrentPage?.HandleGlobalShortcuts();

        DrawTopToolbar();

        Rect bodyRect = new Rect(
            0f,
            SkyPrisonEditorContext.ToolbarHeight,
            position.width,
            position.height - SkyPrisonEditorContext.ToolbarHeight
        );

        bool drawerOverlayEnabled = true;

        Rect drawerRect = Rect.zero;
        Rect buttonVisualRect = Rect.zero;
        Rect buttonHitRect = Rect.zero;
        if (drawerOverlayEnabled)
            GetOverlayRects(bodyRect, out drawerRect, out buttonVisualRect, out buttonHitRect);

        // 核心修复：
        // 在绘制底层 page / 首页之前，先判断这次事件是否落在抽屉或按钮区域。
        // 如果落在这些区域，就让底层内容在这一帧忽略事件，防止穿透误触。
        EventType originalEventType = Event.current.type;
        bool suppressUnderlyingInput = drawerOverlayEnabled && ShouldSuppressUnderlyingInput(Event.current, drawerRect, buttonHitRect);
        if (suppressUnderlyingInput)
            Event.current.type = EventType.Ignore;

        if (showHomeScreen)
            DrawHomeScreen(bodyRect);
        else
            DrawCurrentPage(bodyRect);

        if (suppressUnderlyingInput)
            Event.current.type = originalEventType;

        if (drawerOverlayEnabled)
            DrawDrawerOverlay(bodyRect, drawerRect, buttonVisualRect, buttonHitRect);

        if (!showHomeScreen)
            CurrentPage?.HandlePostGUI();
    }

    private void GetOverlayRects(Rect bodyRect, out Rect drawerRect, out Rect buttonVisualRect, out Rect buttonHitRect)
    {
        float drawerWidth = SkyPrisonEditorContext.DrawerWidth * drawerAnimT;
        drawerRect = new Rect(bodyRect.x, bodyRect.y, drawerWidth, bodyRect.height);

        float buttonVisualX = bodyRect.x + drawerWidth + SkyPrisonEditorContext.DrawerButtonMargin;
        float buttonVisualY = bodyRect.yMax - DrawerToggleButtonVisualSize - DrawerToggleButtonMarginBottom;

        buttonVisualRect = new Rect(
            buttonVisualX,
            buttonVisualY,
            DrawerToggleButtonVisualSize,
            DrawerToggleButtonVisualSize
        );

        buttonHitRect = new Rect(
            buttonVisualRect.x - (DrawerToggleButtonHitSize - buttonVisualRect.width) * 0.5f,
            buttonVisualRect.y - (DrawerToggleButtonHitSize - buttonVisualRect.height) * 0.5f,
            DrawerToggleButtonHitSize,
            DrawerToggleButtonHitSize
        );
    }

    private bool ShouldSuppressUnderlyingInput(Event e, Rect drawerRect, Rect buttonHitRect)
    {
        if (e == null)
            return false;

        bool isPointerEvent =
            e.type == EventType.MouseDown ||
            e.type == EventType.MouseUp ||
            e.type == EventType.MouseDrag ||
            e.type == EventType.ScrollWheel ||
            e.type == EventType.ContextClick;

        if (!isPointerEvent)
            return false;

        if (buttonHitRect.Contains(e.mousePosition))
            return true;

        if (drawerAnimT > 0.001f && drawerRect.Contains(e.mousePosition))
            return true;

        return false;
    }

    private void UpdateDrawerAnimation()
    {
        double now = EditorApplication.timeSinceStartup;
        float deltaTime = Mathf.Clamp((float)(now - lastAnimTime), 0f, 0.05f);
        lastAnimTime = now;

        float target = drawerExpanded ? 1f : 0f;
        if (Mathf.Approximately(drawerAnimT, target) && Mathf.Approximately(drawerAnimVelocity, 0f))
            return;

        drawerAnimT = Mathf.SmoothDamp(drawerAnimT, target, ref drawerAnimVelocity, DrawerSmoothTime, Mathf.Infinity, deltaTime);

        if (Mathf.Abs(drawerAnimT - target) < 0.001f)
        {
            drawerAnimT = target;
            drawerAnimVelocity = 0f;
        }

        Repaint();
    }

    private void HandleEditorWindowUndoRedoHotkeys()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (EditorWindow.focusedWindow != this)
            return;

        bool ctrlOrCmd = e.control || e.command;
        if (!ctrlOrCmd)
            return;

        bool isUndo = e.keyCode == KeyCode.Z && !e.shift;
        bool isRedo = e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift);

        if (!isUndo && !isRedo)
            return;

        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;

        if (isUndo)
            Undo.PerformUndo();
        else
            Undo.PerformRedo();

        Repaint();
        e.Use();
    }

    private SkyPrisonEditorPageBase CurrentPage
    {
        get
        {
            if (pages.Count == 0 || currentTabIndex < 0 || currentTabIndex >= pages.Count)
                return null;
            return pages[currentTabIndex];
        }
    }

    private void DrawTopToolbar()
    {
        Rect rect = new Rect(0f, 0f, position.width, SkyPrisonEditorContext.ToolbarHeight);
        GUILayout.BeginArea(rect, EditorStyles.toolbar);

        EditorGUILayout.BeginHorizontal();

        GUILayout.Space(4f);
        DrawTopMainEditorButton(TopQuickTabWidth + 12f);

        GUILayout.FlexibleSpace();

        string stateText = showHomeScreen
            ? "START NOW"
            : (CurrentPage != null ? CurrentPage.TabName : "未选择页面");
        GUILayout.Label(stateText, EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawTopMainEditorButton(float width)
    {
        bool selected = true;

        GUIStyle style = new GUIStyle(EditorStyles.toolbarButton)
        {
            alignment = TextAnchor.MiddleCenter
        };

        Color oldBg = GUI.backgroundColor;
        if (selected)
            GUI.backgroundColor = new Color(0.58f, 0.58f, 0.58f, 1f);

        bool clicked = GUILayout.Button("天空囚笼编辑器", style, GUILayout.Width(width), GUILayout.Height(19f));

        GUI.backgroundColor = oldBg;

        if (!clicked)
            return;

        int target = Mathf.Clamp(lastMainEditorTabIndex, 0, pages.Count - 1);
        currentTabIndex = target;
        lastMainEditorTabIndex = currentTabIndex;

        showHomeScreen = false;
        drawerExpanded = false;

        if (context != null)
        {
            context.LeftScroll = Vector2.zero;
            context.RightScroll = Vector2.zero;
        }

        SaveWindowPrefs();
        Repaint();
    }

    private bool ShouldShowPageInDrawer(SkyPrisonEditorPageBase page)
    {
        return page != null;
    }

    private void DrawCurrentPage(Rect bodyRect)
    {
        HandleSplitterEvents(bodyRect);

        float leftWidth = context.LeftPanelWidth;
        float splitterWidth = SkyPrisonEditorContext.SplitterWidth;

        Rect leftRect = new Rect(bodyRect.x, bodyRect.y, leftWidth, bodyRect.height);
        Rect splitterRect = new Rect(leftRect.xMax, bodyRect.y, splitterWidth, bodyRect.height);
        Rect rightRect = new Rect(splitterRect.xMax, bodyRect.y, bodyRect.width - leftWidth - splitterWidth, bodyRect.height);

        DrawLeftPanel(leftRect);
        DrawSplitter(splitterRect);
        DrawRightPanel(rightRect);
    }

    private void DrawHomeScreen(Rect bodyRect)
    {
        EnsureHomeLogoLoaded();

        GUILayout.BeginArea(bodyRect);

        if (homeLogoTexture == null)
        {
            GUIStyle fallback = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24
            };
            GUI.Label(new Rect(0f, 0f, bodyRect.width, bodyRect.height), "START NOW", fallback);
        }
        else
        {
            float textureWidth = homeLogoTexture.width * HomeLogoScale;
            float textureHeight = homeLogoTexture.height * HomeLogoScale;

            float maxWidth = bodyRect.width * 0.8f;
            float maxHeight = bodyRect.height * 0.8f;

            float scale = Mathf.Min(
                1f,
                Mathf.Min(maxWidth / Mathf.Max(1f, textureWidth), maxHeight / Mathf.Max(1f, textureHeight))
            );

            float drawWidth = textureWidth * scale;
            float drawHeight = textureHeight * scale;

            Rect rect = new Rect(
                (bodyRect.width - drawWidth) * 0.5f,
                (bodyRect.height - drawHeight) * 0.5f,
                drawWidth,
                drawHeight
            );

            GUI.DrawTexture(rect, homeLogoTexture, ScaleMode.ScaleToFit, true);
        }

        GUILayout.EndArea();
    }

    private void EnsureHomeLogoLoaded()
    {
        if (homeLogoLoaded)
            return;

        homeLogoLoaded = true;
        homeLogoTexture = LoadEditorIcon(0);
    }

    private void LoadDrawerIcons()
    {
        drawerIcons.Clear();
        for (int i = 0; i < pages.Count; i++)
        {
            SkyPrisonEditorPageBase page = pages[i];
            int iconNumber = GetDrawerIconNumber(page != null ? page.TabName : string.Empty, i + 1);
            drawerIcons.Add(LoadEditorIcon(iconNumber));
        }
    }

    private int GetDrawerIconNumber(string tabName, int fallback)
    {
        switch (tabName)
        {
            case "单位定义": return 1;
            case "物品定义": return 2;
            case "掉落池": return 3;
            case "技能": return 4;
            case "状态": return 19;
            case "地形装饰物": return 34;
            case "地表材质": return 46;
            case "地图编辑器": return 24;
            case "特效": return 5;
            case "音声合成": return 25;
            case "科技树": return 6;
            case "触发器": return 7;
            case "AI": return 8;
            case "战斗参数": return 9;
            case "字典表": return 10;
            default: return fallback;
        }
    }

    private Texture2D LoadEditorIcon(int number)
    {
        string num = number.ToString("00");
        string basePath = EditorIconFolder + EditorIconPrefix + num;

        string[] paths =
        {
            basePath + ".png",
            basePath + ".tga",
            basePath + ".jpg",
            basePath + ".jpeg",
            basePath + ".psd"
        };

        for (int i = 0; i < paths.Length; i++)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
            if (tex != null)
                return tex;
        }

        return null;
    }

    private void HandleSplitterEvents(Rect bodyRect)
    {
        Rect splitterRect = new Rect(context.LeftPanelWidth, bodyRect.y, SkyPrisonEditorContext.SplitterWidth, bodyRect.height);
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
        {
            context.DraggingSplitter = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && context.DraggingSplitter)
        {
            context.LeftPanelWidth = Mathf.Clamp(e.mousePosition.x, SkyPrisonEditorContext.MinLeftWidth, SkyPrisonEditorContext.MaxLeftWidth);
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && context.DraggingSplitter)
        {
            context.DraggingSplitter = false;
            e.Use();
        }
    }

    private void DrawLeftPanel(Rect rect)
    {
        GUILayout.BeginArea(rect, EditorStyles.helpBox);
        context.LeftScroll = EditorGUILayout.BeginScrollView(context.LeftScroll);
        CurrentPage?.OnGUILeft();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawSplitter(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
    }

    private void DrawRightPanel(Rect rect)
    {
        GUILayout.BeginArea(rect);
        context.RightScroll = EditorGUILayout.BeginScrollView(context.RightScroll);
        CurrentPage?.OnGUIRight();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawDrawerOverlay(Rect bodyRect, Rect drawerRect, Rect buttonVisualRect, Rect buttonHitRect)
    {
        Event e = Event.current;

        if (e != null && buttonHitRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                drawerExpanded = !drawerExpanded;
                SaveWindowPrefs();
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp ||
                     e.type == EventType.MouseDrag ||
                     e.type == EventType.ScrollWheel ||
                     e.type == EventType.ContextClick)
            {
                e.Use();
            }
        }

        // 抽屉使用纯 Rect 绘制，不走 GUILayout。
        // 这样展开/收起动画过程中 Layout/Repaint 控件数量不会不一致。
        DrawDrawerPanel(drawerRect);

        if (drawerAnimT > 0.001f && e != null && drawerRect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDown ||
                e.type == EventType.MouseUp ||
                e.type == EventType.MouseDrag ||
                e.type == EventType.ScrollWheel ||
                e.type == EventType.ContextClick)
            {
                e.Use();
            }
        }

        DrawDrawerToggleButton(buttonVisualRect, buttonHitRect);
    }

    private void DrawDrawerToggleButton(Rect visualRect, Rect hitRect)
    {
        bool hover = hitRect.Contains(Event.current.mousePosition);

        Vector2 center = visualRect.center;
        float radius = visualRect.width * 0.5f;

        Handles.BeginGUI();

        Color fill = DrawerToggleButtonBg;
        if (hover)
            fill = new Color(fill.r + 0.04f, fill.g + 0.04f, fill.b + 0.04f, fill.a);

        Handles.color = fill;
        Handles.DrawSolidDisc(center, Vector3.forward, radius);

        Handles.color = DrawerToggleButtonBorder;
        Handles.DrawWireDisc(center, Vector3.forward, radius - 0.5f);

        Handles.EndGUI();

        Color old = GUI.color;
        GUI.color = DrawerToggleButtonText;

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13
        };

        GUI.Label(visualRect, drawerExpanded ? "<" : ">", style);
        GUI.color = old;
    }

    private void DrawDrawerPanel(Rect rect)
    {
        if (rect.width <= 0.5f)
            return;

        EditorGUI.DrawRect(rect, DrawerPanelColor);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), DrawerBorderColor);

        float y = rect.y + 16f;
        DrawDrawerItem(new Rect(rect.x + 12f, y, Mathf.Max(0f, rect.width - 24f), 32f), "START NOW", -1, null);
        y += 32f;

        for (int i = 0; i < pages.Count; i++)
        {
            if (!ShouldShowPageInDrawer(pages[i]))
                continue;

            Texture2D icon = i < drawerIcons.Count ? drawerIcons[i] : null;
            DrawDrawerItem(new Rect(rect.x + 12f, y, Mathf.Max(0f, rect.width - 24f), 32f), pages[i].TabName, i, icon);
            y += 32f;
        }
    }

    private void DrawDrawerItem(Rect rowRect, string label, int pageIndex, Texture2D icon)
    {
        if (rowRect.width <= 1f || rowRect.height <= 1f)
            return;

        bool isSelected = pageIndex < 0
            ? showHomeScreen
            : (!showHomeScreen && currentTabIndex == pageIndex);

        Event e = Event.current;
        bool isHover = e != null && rowRect.Contains(e.mousePosition);

        Rect highlightRect = new Rect(
            rowRect.x + 2f,
            rowRect.y + 2f,
            rowRect.width - 4f,
            rowRect.height - 4f
        );

        if (isSelected)
            DrawRoundedRect(highlightRect, DrawerItemSelectedColor, 7f);
        else if (isHover)
            DrawRoundedRect(highlightRect, DrawerItemHoverColor, 7f);

        float x = rowRect.x + 10f;

        if (icon != null)
        {
            Rect iconRect = new Rect(
                x,
                rowRect.y + (rowRect.height - DrawerItemIconSize) * 0.5f,
                DrawerItemIconSize,
                DrawerItemIconSize
            );
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            x = iconRect.xMax + 8f;
        }

        GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
        labelStyle.normal.textColor = isSelected
            ? new Color(0.95f, 0.95f, 0.95f, 1f)
            : new Color(0.82f, 0.82f, 0.82f, 1f);

        GUI.Label(
            new Rect(x, rowRect.y + 7f, rowRect.width - (x - rowRect.x) - 10f, 18f),
            label,
            labelStyle
        );

        if (e != null && e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition) && e.button == 0)
        {
            if (pageIndex < 0)
            {
                showHomeScreen = true;
                drawerExpanded = false;
                SaveWindowPrefs();
                Repaint();
            }
            else
            {
                currentTabIndex = pageIndex;
                lastMainEditorTabIndex = pageIndex;
                showHomeScreen = false;
                context.LeftScroll = Vector2.zero;
                context.RightScroll = Vector2.zero;
                drawerExpanded = false;
                SaveWindowPrefs();
                Repaint();
            }

            e.Use();
        }
    }

    private void DrawRoundedRect(Rect rect, Color color, float radius = 6f, int segments = 8)
    {
        Handles.BeginGUI();
        Handles.color = color;

        float r = Mathf.Min(radius, rect.width * 0.5f, rect.height * 0.5f);

        Vector3[] points = new Vector3[(segments + 1) * 4];
        int index = 0;

        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.PI + Mathf.PI * 0.5f * (i / (float)segments);
            points[index++] = new Vector3(rect.xMin + r + Mathf.Cos(t) * r, rect.yMin + r + Mathf.Sin(t) * r);
        }

        for (int i = 0; i <= segments; i++)
        {
            float t = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * (i / (float)segments);
            points[index++] = new Vector3(rect.xMax - r + Mathf.Cos(t) * r, rect.yMin + r + Mathf.Sin(t) * r);
        }

        for (int i = 0; i <= segments; i++)
        {
            float t = 0f + Mathf.PI * 0.5f * (i / (float)segments);
            points[index++] = new Vector3(rect.xMax - r + Mathf.Cos(t) * r, rect.yMax - r + Mathf.Sin(t) * r);
        }

        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.PI * 0.5f + Mathf.PI * 0.5f * (i / (float)segments);
            points[index++] = new Vector3(rect.xMin + r + Mathf.Cos(t) * r, rect.yMax - r + Mathf.Sin(t) * r);
        }

        Handles.DrawAAConvexPolygon(points);
        Handles.EndGUI();
    }
}
