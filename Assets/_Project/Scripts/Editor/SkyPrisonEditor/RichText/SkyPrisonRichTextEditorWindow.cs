using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 正式一期 v2：
/// - 单区文本本体编辑
/// - 更准确的字符间距和选区填充
/// - 右侧常驻 HSV 色轮 + 中心明度/饱和度方块
/// - 选中文字后，右侧改色会直接作用到选区
/// </summary>
public class SkyPrisonRichTextEditorWindow : EditorWindow
{
    private const string BoldIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_20.png";
    private const string ItalicIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_21.png";
    private const string UnderlineIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_22.png";
    private const string ClearIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_23.png";
    private const string UndoIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_16.png";
    private const string RedoIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_17.png";
    private const string HexControlName = "RichTextHexInput";

    private static Action<string> onSaveCallback;

    private static readonly Color[] PresetColors =
    {
        new Color(1.00f, 0.40f, 0.35f, 1f),
        new Color(0.35f, 1.00f, 0.50f, 1f),
        new Color(1.00f, 0.55f, 0.25f, 1f),
        new Color(0.45f, 0.80f, 1.00f, 1f),
        new Color(0.50f, 1.00f, 0.60f, 1f),
        new Color(0.72f, 0.92f, 1.00f, 1f),
        new Color(1.00f, 0.92f, 0.35f, 1f),
        new Color(0.90f, 0.56f, 1.00f, 1f),
    };

    private struct CharFormat
    {
        public bool bold;
        public bool italic;
        public bool underline;
        public bool hasColor;
        public Color color;

        public static CharFormat Default
        {
            get
            {
                CharFormat f = new CharFormat();
                f.bold = false;
                f.italic = false;
                f.underline = false;
                f.hasColor = false;
                f.color = Color.white;
                return f;
            }
        }
    }

    private class LineLayout
    {
        public int start;
        public int end;
        public List<float> xs = new List<float>();
        public List<float> widths = new List<float>();
        public List<float> drawOffsets = new List<float>();
        public float totalWidth;
    }

    private string editorTitle = "富文本编辑器";
    private string plainText = string.Empty;
    private readonly List<CharFormat> formats = new List<CharFormat>();

    private int caretIndex;
    private int selectionAnchor;
    private bool draggingSelection;
    private bool editorFocused;
    private Vector2 editorScroll;

    private Texture2D boldIcon;
    private Texture2D italicIcon;
    private Texture2D underlineIcon;
    private Texture2D clearIcon;
    private Texture2D undoIcon;
    private Texture2D redoIcon;

    private float hue = 0.08f;
    private float saturation = 0.75f;
    private float value = 1.0f;
    private Color currentColor = new Color(1.00f, 0.55f, 0.25f, 1f);
    private string hexInput = "#FF8C40";
    private Texture2D hueWheelTexture;
    private Texture2D svSquareTexture;
    private float lastSvHue = -1f;

    private readonly Stack<EditorStateSnapshot> undoStack = new Stack<EditorStateSnapshot>();
    private readonly Stack<EditorStateSnapshot> redoStack = new Stack<EditorStateSnapshot>();
    private enum ColorDragMode { None, HueWheel, SVSquare }
    private ColorDragMode colorDragMode;
    private bool colorDragUndoPushed;

    private string parameterContext = string.Empty;
    private readonly List<string> parameterSuggestions = new List<string>();
    private bool showParameterSuggestions;
    private int selectedSuggestionIndex;
    private int suggestionReplaceStart = -1;
    private string suggestionPrefix = string.Empty;

    private readonly GUIStyle drawStyle = new GUIStyle();
    private readonly GUIStyle titleStyle = new GUIStyle();
    private readonly GUIStyle miniHeaderStyle = new GUIStyle();

    private class EditorStateSnapshot
    {
        public string plainText;
        public List<CharFormat> formats;
        public int caretIndex;
        public int selectionAnchor;
    }


    public static void Open(string title, string initialText, Action<string> onSave)
    {
        Open(title, initialText, onSave, string.Empty);
    }

    public static void Open(string title, string initialText, Action<string> onSave, string context)
    {
        onSaveCallback = onSave;

        SkyPrisonRichTextEditorWindow window = CreateInstance<SkyPrisonRichTextEditorWindow>();
        window.titleContent = new GUIContent("富文本编辑器");
        window.editorTitle = string.IsNullOrWhiteSpace(title) ? "富文本编辑器" : title;
        window.parameterContext = context ?? string.Empty;
        window.ParseRichTextToDocument(initialText ?? string.Empty);
        window.minSize = new Vector2(980f, 580f);
        window.position = new Rect(140f, 90f, 980f, 580f);
        window.ShowModalUtility();
    }

    private static Rect GetEditorMainWindowPosition()
    {
        Type containerWinType = Type.GetType("UnityEditor.ContainerWindow,UnityEditor");
        if (containerWinType != null)
        {
            var showModeField = containerWinType.GetField("m_ShowMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var positionProperty = containerWinType.GetProperty("position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var windows = Resources.FindObjectsOfTypeAll(containerWinType);
            foreach (UnityEngine.Object win in windows)
            {
                if (showModeField == null || positionProperty == null)
                    break;
                int showMode = (int)showModeField.GetValue(win);
                if (showMode == 4)
                    return (Rect)positionProperty.GetValue(win, null);
            }
        }
        return new Rect(100f, 100f, 1400f, 900f);
    }

    private void OnEnable()
    {
        boldIcon = LoadOptionalIcon(BoldIconPath);
        italicIcon = LoadOptionalIcon(ItalicIconPath);
        underlineIcon = LoadOptionalIcon(UnderlineIconPath);
        clearIcon = LoadOptionalIcon(ClearIconPath);
        undoIcon = LoadOptionalIcon(UndoIconPath);
        redoIcon = LoadOptionalIcon(RedoIconPath);

        drawStyle.font = EditorStyles.textArea.font;
        drawStyle.fontSize = 14;
        drawStyle.normal.textColor = new Color(0.92f, 0.92f, 0.95f, 1f);
        drawStyle.alignment = TextAnchor.UpperLeft;
        drawStyle.wordWrap = false;
        drawStyle.clipping = TextClipping.Overflow;

        titleStyle.fontSize = 13;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = new Color(0.94f, 0.94f, 0.96f, 1f);

        miniHeaderStyle.fontSize = 12;
        miniHeaderStyle.fontStyle = FontStyle.Bold;
        miniHeaderStyle.normal.textColor = new Color(0.88f, 0.88f, 0.90f, 1f);

        SyncHSVFromColor(currentColor);
        hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
    }

    private void OnGUI()
    {
        HandleKeyboard(Event.current);
        Input.imeCompositionMode = editorFocused ? IMECompositionMode.On : IMECompositionMode.Auto;

        Rect fullRect = new Rect(0f, 0f, position.width, position.height);
        EditorGUI.DrawRect(fullRect, new Color(0.24f, 0.24f, 0.24f, 1f));

        Rect topRect = new Rect(10f, 8f, position.width - 20f, 50f);
        Rect bodyRect = new Rect(10f, 62f, position.width - 20f, position.height - 106f);
        Rect bottomRect = new Rect(10f, position.height - 46f, position.width - 20f, 30f);

        DrawTopToolbar(topRect);
        DrawBody(bodyRect);
        DrawBottomBar(bottomRect);
        DrawParameterSuggestionPopup();

        if (Event.current.type == EventType.Repaint)
            Repaint();
    }

    private void DrawTopToolbar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f, 1f));
        const float buttonSize = 24f;
        const float gap = 4f;

        GUIStyle langStyle = new GUIStyle(EditorStyles.miniBoldLabel);
        langStyle.fontSize = 11;
        langStyle.normal.textColor = new Color(0.82f, 0.82f, 0.86f, 1f);

        const float alignOffset = 0f;
        Rect langRect = new Rect(rect.x + alignOffset, rect.y, rect.width - alignOffset, 16f);
        GUI.Label(langRect, editorTitle, langStyle);

        Rect groupRect = new Rect(rect.x + alignOffset, rect.y + 20f, rect.width - alignOffset, 28f);
        EditorGUI.DrawRect(groupRect, new Color(0.24f, 0.24f, 0.24f, 1f));

        float y = groupRect.y + 2f;
        Rect undoRect = new Rect(groupRect.x + 4f, y, buttonSize, 24f);
        Rect redoRect = new Rect(undoRect.xMax + gap, y, buttonSize, 24f);
        Rect boldRect = new Rect(redoRect.xMax + gap + 6f, y, buttonSize, 24f);
        Rect italicRect = new Rect(boldRect.xMax + gap, y, buttonSize, 24f);
        Rect underlineRect = new Rect(italicRect.xMax + gap, y, buttonSize, 24f);
        Rect clearRect = new Rect(underlineRect.xMax + gap, y, buttonSize, 24f);
        Rect paramRect = new Rect(clearRect.xMax + gap + 8f, y, 68f, 24f);
        Rect paramFullRect = new Rect(paramRect.xMax + gap, y, 76f, 24f);

        if (GUI.Button(undoRect, undoIcon != null ? new GUIContent(undoIcon, "撤销") : new GUIContent("↶", "撤销")))
            UndoAction();
        if (GUI.Button(redoRect, redoIcon != null ? new GUIContent(redoIcon, "重做") : new GUIContent("↷", "重做")))
            RedoAction();

        if (DrawToggleToolbarButton(boldRect, SelectionFullyBold(), boldIcon != null ? new GUIContent(boldIcon, "加粗") : new GUIContent("B", "加粗")))
            ToggleBold();
        if (DrawToggleToolbarButton(italicRect, SelectionFullyItalic(), italicIcon != null ? new GUIContent(italicIcon, "斜体") : new GUIContent("I", "斜体")))
            ToggleItalic();
        if (DrawToggleToolbarButton(underlineRect, SelectionFullyUnderline(), underlineIcon != null ? new GUIContent(underlineIcon, "下划线") : new GUIContent("U", "下划线")))
            ToggleUnderline();

        using (new EditorGUI.DisabledScope(!HasSelection()))
        {
            if (GUI.Button(clearRect, clearIcon != null ? new GUIContent(clearIcon, "清除格式") : new GUIContent("清", "清除格式")))
                ClearFormattingOnSelection();
        }

        if (GUI.Button(paramRect, "参数"))
            InsertParameterTemplate("{param:this.}", 12);
        if (GUI.Button(paramFullRect, "参数模板"))
            InsertParameterTemplate("{param:this.|format|fallback}", 12);
    }

    private void DrawBody(Rect rect)
    {
        const float sideWidth = 236f;
        const float parameterHeight = 84f;
        const float gap = 8f;

        float leftWidth = rect.width - sideWidth;
        Rect editorRect = new Rect(rect.x, rect.y, leftWidth, rect.height - parameterHeight - gap);
        Rect parameterRect = new Rect(rect.x, editorRect.yMax + gap, leftWidth, parameterHeight);
        Rect sideRect = new Rect(editorRect.xMax, rect.y, sideWidth, rect.height);

        DrawEditorSurface(editorRect);
        DrawParameterReferencePanel(parameterRect);
        DrawSidebar(sideRect);
    }

    private void DrawEditorSurface(Rect rect)
    {
        Color panelBg = new Color(0.14f, 0.14f, 0.15f, 1f);
        Color innerBg = new Color(0.16f, 0.16f, 0.17f, 1f);
        EditorGUI.DrawRect(rect, panelBg);
        DrawBorder(rect, new Color(1f, 1f, 1f, 0.07f));

        Rect innerRect = new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, rect.height - 20f);
        EditorGUI.DrawRect(innerRect, innerBg);
        DrawBorder(innerRect, new Color(1f, 1f, 1f, 0.05f));

        List<LineLayout> lines = BuildLineLayouts();
        float contentHeight = Mathf.Max(innerRect.height - 20f, lines.Count * 24f + 20f);
        float maxWidth = innerRect.width - 20f;
        for (int i = 0; i < lines.Count; i++)
            maxWidth = Mathf.Max(maxWidth, lines[i].totalWidth + 24f);

        Rect scrollRect = new Rect(innerRect.x, innerRect.y, innerRect.width, innerRect.height);
        Rect viewRect = new Rect(0f, 0f, maxWidth, contentHeight);
        editorScroll = GUI.BeginScrollView(scrollRect, editorScroll, viewRect, true, true);

        Rect localRect = new Rect(0f, 0f, viewRect.width, viewRect.height);
        HandleMouseSelection(localRect, lines);
        DrawStyledText(localRect, lines);
        DrawCaret(localRect, lines);

        GUI.EndScrollView();
    }

    private void DrawStyledText(Rect localRect, List<LineLayout> lines)
    {
        int selectionStart = GetSelectionStart();
        int selectionEnd = GetSelectionEnd();
        const float lineHeight = 24f;
        const float topPadding = 10f;

        for (int line = 0; line < lines.Count; line++)
        {
            LineLayout layout = lines[line];
            float y = topPadding + line * lineHeight;

            bool underlineOpen = false;
            float underlineStartX = 0f;
            float underlineEndX = 0f;
            Color underlineColor = Color.white;

            bool selectionOpen = false;
            float selectionStartX = 0f;
            float selectionEndX = 0f;

            for (int localIndex = 0; localIndex < layout.widths.Count; localIndex++)
            {
                int globalIndex = layout.start + localIndex;
                float drawOffset = layout.drawOffsets.Count > localIndex ? layout.drawOffsets[localIndex] : 0f;
                Rect charRect = new Rect(layout.xs[localIndex], y, layout.widths[localIndex], lineHeight);
                Rect drawRect = new Rect(charRect.x + drawOffset, charRect.y, charRect.width - drawOffset + 1f, charRect.height);

                bool selected = globalIndex >= selectionStart && globalIndex < selectionEnd;
                if (selected)
                {
                    if (!selectionOpen)
                    {
                        selectionOpen = true;
                        selectionStartX = charRect.x;
                        selectionEndX = charRect.xMax;
                    }
                    else
                    {
                        selectionEndX = charRect.xMax;
                    }
                }
                else if (selectionOpen)
                {
                    EditorGUI.DrawRect(new Rect(selectionStartX, y + 2f, Mathf.Max(1f, selectionEndX - selectionStartX), lineHeight - 4f), new Color(0.31f, 0.45f, 0.82f, 0.62f));
                    selectionOpen = false;
                }

                CharFormat fmt = formats[globalIndex];
                GUIStyle style = BuildStyleForFormat(fmt);
                GUI.Label(drawRect, plainText[globalIndex].ToString(), style);

                Color charColor = style.normal.textColor;
                if (fmt.underline)
                {
                    if (!underlineOpen)
                    {
                        underlineOpen = true;
                        underlineStartX = charRect.x;
                        underlineEndX = charRect.xMax;
                        underlineColor = charColor;
                    }
                    else if (ApproximatelySameColor(underlineColor, charColor))
                    {
                        underlineEndX = charRect.xMax;
                    }
                    else
                    {
                        EditorGUI.DrawRect(new Rect(underlineStartX, y + lineHeight - 5f, Mathf.Max(1f, underlineEndX - underlineStartX), 0.8f), underlineColor);
                        underlineStartX = charRect.x;
                        underlineEndX = charRect.xMax;
                        underlineColor = charColor;
                    }
                }
                else if (underlineOpen)
                {
                    EditorGUI.DrawRect(new Rect(underlineStartX, y + lineHeight - 5f, Mathf.Max(1f, underlineEndX - underlineStartX + 0.5f), 0.8f), underlineColor);
                    underlineOpen = false;
                }
            }

            if (selectionOpen)
                EditorGUI.DrawRect(new Rect(selectionStartX, y + 2f, Mathf.Max(1f, selectionEndX - selectionStartX), lineHeight - 4f), new Color(0.31f, 0.45f, 0.82f, 0.62f));
            if (underlineOpen)
                EditorGUI.DrawRect(new Rect(underlineStartX, y + lineHeight - 5f, Mathf.Max(1f, underlineEndX - underlineStartX + 0.5f), 0.8f), underlineColor);
        }
    }

    private void DrawCaret(Rect localRect, List<LineLayout> lines)
    {
        if (!editorFocused || HasSelection())
            return;
        if ((DateTime.Now.Millisecond / 500) % 2 != 0)
            return;

        const float lineHeight = 24f;
        const float topPadding = 10f;

        int lineIndex, columnIndex;
        GetLineColumnFromIndex(caretIndex, lines, out lineIndex, out columnIndex);
        float x = 10f;
        if (lineIndex >= 0 && lineIndex < lines.Count)
        {
            LineLayout layout = lines[lineIndex];
            if (columnIndex > 0 && columnIndex - 1 < layout.xs.Count)
                x = layout.xs[columnIndex - 1] + layout.widths[columnIndex - 1];
            else if (layout.xs.Count > 0)
                x = layout.xs[0];
        }
        Rect caretRect = new Rect(x, topPadding + lineIndex * lineHeight + 2f, 1.5f, lineHeight - 4f);
        EditorGUI.DrawRect(caretRect, new Color(0.95f, 0.95f, 0.98f, 1f));
    }

    private void DrawParameterReferencePanel(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.20f, 1f));
        DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

        Rect inner = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);
        GUI.Label(new Rect(inner.x, inner.y, 120f, 16f), "参数引用", miniHeaderStyle);
        GUI.Label(new Rect(inner.x, inner.y + 20f, inner.width, 18f), "基础：{param:this.hp}", EditorStyles.miniLabel);
        GUI.Label(new Rect(inner.x, inner.y + 38f, inner.width, 18f), "完整：{param:this.|format|fallback}", EditorStyles.miniLabel);
        GUI.Label(new Rect(inner.x + 220f, inner.y + 20f, inner.width - 220f, 18f), "快捷键：Ctrl/Cmd+Shift+P", EditorStyles.miniLabel);
        GUI.Label(new Rect(inner.x + 220f, inner.y + 38f, inner.width - 220f, 18f), "模板：Ctrl/Cmd+Shift+F", EditorStyles.miniLabel);
    }

    private void DrawSidebar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.24f, 0.24f, 0.24f, 1f));
        DrawBorder(rect, new Color(1f, 1f, 1f, 0.07f));

        Rect inner = new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, rect.height - 24f);
        GUILayout.BeginArea(inner);

        EditorGUILayout.LabelField("样式侧栏", titleStyle);
        GUILayout.Space(8f);
        EditorGUILayout.LabelField("色轮", miniHeaderStyle);

        Rect wheelRect = GUILayoutUtility.GetRect(200f, 200f, GUILayout.Width(200f), GUILayout.Height(200f));
        Rect svRect = new Rect(wheelRect.x + 52f, wheelRect.y + 52f, 96f, 96f);
        DrawColorWheelControl(wheelRect, svRect);

        GUILayout.Space(8f);
        Rect colorGroup = GUILayoutUtility.GetRect(0f, 10000f, 104f, 104f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(colorGroup, new Color(0.19f, 0.19f, 0.20f, 1f));
        DrawBorder(colorGroup, new Color(1f, 1f, 1f, 0.08f));
        Rect currentRect = new Rect(colorGroup.x + 8f, colorGroup.y + 8f, 22f, 22f);
        EditorGUI.DrawRect(currentRect, currentColor);
        DrawBorder(currentRect, new Color(1f, 1f, 1f, 0.10f));
        if (GUI.Button(currentRect, GUIContent.none, GUIStyle.none))
        {
            if (HasSelection())
                ApplyColorToSelection(currentColor);
        }

        string hexValue = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
        Rect hexRect = new Rect(currentRect.xMax + 8f, currentRect.y + 1f, colorGroup.width - 46f, 20f);
        string editedHex = EditorGUI.TextField(hexRect, hexValue);
        Color parsedHex;
        if (!string.IsNullOrWhiteSpace(editedHex))
        {
            string normalized = editedHex.StartsWith("#") ? editedHex : "#" + editedHex;
            if (ColorUtility.TryParseHtmlString(normalized, out parsedHex) && parsedHex != currentColor)
            {
                currentColor = parsedHex;
                SyncHSVFromColor(currentColor);
        hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
                svSquareTexture = null;
                if (HasSelection())
                    ApplyColorToSelection(currentColor);
            }
        }

        Rect applyCurrentRect = new Rect(colorGroup.x + 8f, currentRect.yMax + 4f, colorGroup.width - 16f, 18f);
        using (new EditorGUI.DisabledScope(!HasSelection()))
        {
            if (GUI.Button(applyCurrentRect, "应用当前色"))
                ApplyColorToSelection(currentColor);
        }

        GUI.Label(new Rect(colorGroup.x + 8f, applyCurrentRect.yMax + 4f, 40f, 16f), "RGB", miniHeaderStyle);
        int r = Mathf.RoundToInt(currentColor.r * 255f);
        int g = Mathf.RoundToInt(currentColor.g * 255f);
        int b = Mathf.RoundToInt(currentColor.b * 255f);
        Rect rgbRow = new Rect(colorGroup.x + 8f, colorGroup.y + 68f, colorGroup.width - 16f, 22f);
        DrawRgbRow(rgbRow, ref r, ref g, ref b);
        Color rgbColor = new Color(r / 255f, g / 255f, b / 255f, 1f);
        if (rgbColor != currentColor)
        {
            currentColor = rgbColor;
            SyncHSVFromColor(currentColor);
            hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
            svSquareTexture = null;
            if (HasSelection())
                ApplyColorToSelection(currentColor);
        }

        GUILayout.Space(10f);
        Rect quickGroup = GUILayoutUtility.GetRect(0f, 10000f, 88f, 90f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(quickGroup, new Color(0.19f, 0.19f, 0.20f, 1f));
        DrawBorder(quickGroup, new Color(1f, 1f, 1f, 0.08f));
        GUI.Label(new Rect(quickGroup.x + 8f, quickGroup.y + 6f, 60f, 16f), "快捷色", miniHeaderStyle);

        int swatchSize = 20;
        int gap2 = 6;
        for (int i = 0; i < PresetColors.Length; i++)
        {
            int row = i / 4;
            int col = i % 4;
            Rect swatchRect = new Rect(quickGroup.x + 8f + col * (swatchSize + gap2), quickGroup.y + 28f + row * (swatchSize + gap2), swatchSize, swatchSize);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = PresetColors[i];
            if (GUI.Button(swatchRect, GUIContent.none))
            {
                currentColor = PresetColors[i];
                SyncHSVFromColor(currentColor);
                hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
        hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
                ApplyColorToSelection(currentColor);
            }
            GUI.backgroundColor = old;
        }
        GUILayout.EndArea();
    }

    private void DrawColorWheelControl(Rect wheelRect, Rect svRect)
    {
        if (hueWheelTexture == null)
            hueWheelTexture = BuildHueWheelTexture(512, 34);
        if (svSquareTexture == null || Mathf.Abs(lastSvHue - hue) > 0.0001f)
        {
            svSquareTexture = BuildSVSquareTexture(128, hue);
            lastSvHue = hue;
        }

        GUI.DrawTexture(wheelRect, hueWheelTexture, ScaleMode.StretchToFill, true);
        Handles.BeginGUI();
        Color oldHandles = Handles.color;
        Handles.color = new Color(0.20f, 0.20f, 0.21f, 1f);
        Vector3[] hole = new Vector3[72];
        Vector2 centerPt = wheelRect.center;
        float holeRadius = 78f;
        for (int i = 0; i < hole.Length; i++)
        {
            float t = i / (float)hole.Length * Mathf.PI * 2f;
            hole[i] = centerPt + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * holeRadius;
        }
        Handles.DrawAAConvexPolygon(hole);
        Handles.color = oldHandles;
        Handles.EndGUI();

        GUI.DrawTexture(svRect, svSquareTexture, ScaleMode.StretchToFill, false);
        DrawBorder(svRect, new Color(0f, 0f, 0f, 0.45f));

        Vector2 center = wheelRect.center;
        float outerRadius = wheelRect.width * 0.5f - 1f;
        float innerRadius = outerRadius - 34f;
        float ringMid = outerRadius - 8f;
        float angle = (1f - hue) * Mathf.PI * 2f;
        Vector2 ringHandle = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringMid;

        DrawRingHandle(ringHandle, angle);

        Vector2 svHandle = new Vector2(svRect.x + saturation * svRect.width, svRect.y + (1f - value) * svRect.height);
        DrawSvHandle(svHandle);

        Event e = Event.current;
        Vector2 mouse = e.mousePosition;
        Vector2 delta = mouse - center;
        float dist = delta.magnitude;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (svRect.Contains(mouse))
            {
                colorDragMode = ColorDragMode.SVSquare;
                colorDragUndoPushed = false;
                UpdateSVFromMouse(mouse, svRect);
                e.Use();
            }
            else if (dist >= innerRadius - 8f && dist <= outerRadius + 8f)
            {
                colorDragMode = ColorDragMode.HueWheel;
                colorDragUndoPushed = false;
                UpdateHueFromMouse(mouse, center);
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && colorDragMode != ColorDragMode.None)
        {
            if (colorDragMode == ColorDragMode.HueWheel)
            {
                UpdateHueFromMouse(mouse, center);
                e.Use();
            }
            else if (colorDragMode == ColorDragMode.SVSquare)
            {
                UpdateSVFromMouse(mouse, svRect);
                e.Use();
            }
        }
        else if (e.type == EventType.MouseUp && colorDragMode != ColorDragMode.None)
        {
            colorDragMode = ColorDragMode.None;
            colorDragUndoPushed = false;
            e.Use();
        }
    }

    private void UpdateHueFromMouse(Vector2 mouse, Vector2 center)
    {
        Vector2 delta = mouse - center;
        float a = Mathf.Atan2(delta.y, delta.x);
        if (a < 0f)
            a += Mathf.PI * 2f;
        hue = 1f - a / (Mathf.PI * 2f);
        if (hue >= 1f) hue -= 1f;
        if (hue < 0f) hue += 1f;

        currentColor = Color.HSVToRGB(hue, saturation, value);
        hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
        if (HasSelection())
        {
            if (!colorDragUndoPushed)
            {
                PushUndoState();
                colorDragUndoPushed = true;
            }
            ApplyColorToSelectionDirect(currentColor);
        }
        svSquareTexture = BuildSVSquareTexture(128, hue);
        lastSvHue = hue;
        Repaint();
    }

    private void UpdateSVFromMouse(Vector2 mouse, Rect svRect)
    {
        saturation = Mathf.Clamp01((mouse.x - svRect.x) / svRect.width);
        value = Mathf.Clamp01(1f - (mouse.y - svRect.y) / svRect.height);
        currentColor = Color.HSVToRGB(hue, saturation, value);
        hexInput = "#" + ColorUtility.ToHtmlStringRGB(currentColor);
        if (HasSelection())
        {
            if (!colorDragUndoPushed)
            {
                PushUndoState();
                colorDragUndoPushed = true;
            }
            ApplyColorToSelectionDirect(currentColor);
        }
        Repaint();
    }

    private void DrawRingHandle(Vector2 center, float angle)
    {
        Vector2 toCenter = new Vector2(-Mathf.Cos(angle), -Mathf.Sin(angle)).normalized;
        Vector2 tangent = new Vector2(-toCenter.y, toCenter.x);

        Vector2 innerMid = center + toCenter * 6f;
        Vector2 outerMid = center - toCenter * 8f;
        float innerHalf = 3f;
        float outerHalf = 6f;

        Vector3[] outer = new Vector3[5];
        outer[0] = innerMid + tangent * innerHalf;
        outer[1] = innerMid - tangent * innerHalf;
        outer[2] = outerMid - tangent * outerHalf;
        outer[3] = outerMid + tangent * outerHalf;
        outer[4] = outer[0];

        Vector2 innerMid2 = center + toCenter * 5f;
        Vector2 outerMid2 = center - toCenter * 7f;
        float innerHalf2 = 2f;
        float outerHalf2 = 5f;

        Vector3[] inner = new Vector3[5];
        inner[0] = innerMid2 + tangent * innerHalf2;
        inner[1] = innerMid2 - tangent * innerHalf2;
        inner[2] = outerMid2 - tangent * outerHalf2;
        inner[3] = outerMid2 + tangent * outerHalf2;
        inner[4] = inner[0];

        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = Color.black;
        Handles.DrawAAPolyLine(2f, outer);
        Handles.color = Color.white;
        Handles.DrawAAPolyLine(2f, inner);
        Handles.color = old;
        Handles.EndGUI();
    }

    private void DrawSvHandle(Vector2 center)
    {
        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = Color.black;
        Handles.DrawWireDisc(center, Vector3.forward, 6f);
        Handles.color = Color.white;
        Handles.DrawWireDisc(center, Vector3.forward, 5f);
        Handles.color = old;
        Handles.EndGUI();
    }

    private void DrawRectOutline(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void DrawRgbEntry(Rect rect, Color swatchColor, ref int value)
    {
        Rect swatch = new Rect(rect.x, rect.y + 3f, 12f, 12f);
        EditorGUI.DrawRect(swatch, swatchColor);
        Rect field = new Rect(swatch.xMax + 4f, rect.y, rect.width - 16f, 18f);
        value = Mathf.Clamp(EditorGUI.IntField(field, value), 0, 255);
    }

    private void DrawRgbRow(Rect rect, ref int r, ref int g, ref int b)
    {
        float w = (rect.width - 12f) / 3f;
        DrawRgbEntry(new Rect(rect.x, rect.y, w, 18f), new Color(1f, 0.15f, 0.15f, 1f), ref r);
        DrawRgbEntry(new Rect(rect.x + w + 6f, rect.y, w, 18f), new Color(0.15f, 1f, 0.20f, 1f), ref g);
        DrawRgbEntry(new Rect(rect.x + (w + 6f) * 2f, rect.y, w, 18f), new Color(0.20f, 0.35f, 1f, 1f), ref b);
    }

    private void DrawBottomBar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.17f, 0.17f, 0.18f, 1f));
        DrawBorder(rect, new Color(1f, 1f, 1f, 0.07f));

        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, rect.height - 8f));
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(24f);
        GUILayout.Label(string.Format("长度 {0} | 选区 {1}-{2}", plainText.Length, GetSelectionStart(), GetSelectionEnd()), EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("复制富文本结果", GUILayout.Width(120f), GUILayout.Height(22f)))
            EditorGUIUtility.systemCopyBuffer = BuildRichTextString();

        if (GUILayout.Button("保存", GUILayout.Width(88f), GUILayout.Height(24f)))
        {
            if (onSaveCallback != null)
                onSaveCallback(BuildRichTextString());
            Close();
            GUIUtility.ExitGUI();
        }

        if (GUILayout.Button("关闭", GUILayout.Width(80f), GUILayout.Height(22f)))
        {
            Close();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private float GetCompactCharWidth(char c, GUIStyle style)
    {
        float width = Mathf.Round(style.CalcSize(new GUIContent(c.ToString())).x);
        if (c < 127)
        {
            width *= 0.84f;
            switch (c)
            {
                case 'i':
                case 'l':
                case 'I':
                case '1':
                case '|':
                case '!':
                case '\'':
                case '`':
                    width *= 0.40f;
                    break;
                case '.':
                case ',':
                case ';':
                case ':':
                    width *= 0.46f;
                    break;
                case 'f':
                case 't':
                case 'r':
                case 'j':
                    width *= 0.62f;
                    break;
            }
        }
        return Mathf.Max(4f, width);
    }

    private float GetCharDrawOffset(char c)
    {
        switch (c)
        {
            case 'i':
            case 'l':
            case 'I':
            case '1':
                return 1.0f;
            case '|':
            case '!':
            case '\'':
            case '`':
                return 0.6f;
            case 'f':
            case 'r':
            case 'j':
                return 0.35f;
            case 't':
                return 0.4f;
            default:
                return 0f;
        }
    }

    private List<LineLayout> BuildLineLayouts()
    {
        List<LineLayout> lines = new List<LineLayout>();
        StringBuilder sb = new StringBuilder();
        int start = 0;
        for (int i = 0; i <= plainText.Length; i++)
        {
            bool atEnd = i == plainText.Length;
            if (atEnd || plainText[i] == '\n')
            {
                LineLayout line = new LineLayout();
                line.start = start;
                line.end = i;
                float x = 16f;
                for (int j = start; j < i; j++)
                {
                    line.xs.Add(x);
                    GUIStyle style = BuildStyleForFormat(formats[j]);
                    char ch = plainText[j];
                    float w = GetCompactCharWidth(ch, style);
                    line.widths.Add(w);
                    line.drawOffsets.Add(GetCharDrawOffset(ch));
                    x += w;
                }
                line.totalWidth = x + 16f;
                lines.Add(line);
                start = i + 1;
            }
        }
        if (lines.Count == 0)
            lines.Add(new LineLayout { start = 0, end = 0, totalWidth = 20f });
        return lines;
    }

    private GUIStyle BuildStyleForFormat(CharFormat fmt)
    {
        GUIStyle style = new GUIStyle(drawStyle);
        style.fontStyle = fmt.bold && fmt.italic ? FontStyle.BoldAndItalic : fmt.bold ? FontStyle.Bold : fmt.italic ? FontStyle.Italic : FontStyle.Normal;
        style.normal.textColor = fmt.hasColor ? fmt.color : new Color(0.92f, 0.92f, 0.95f, 1f);
        return style;
    }

    private bool ApproximatelySameColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f
               && Mathf.Abs(a.g - b.g) < 0.01f
               && Mathf.Abs(a.b - b.b) < 0.01f
               && Mathf.Abs(a.a - b.a) < 0.01f;
    }

    private void HandleMouseSelection(Rect localRect, List<LineLayout> lines)
    {
        Event e = Event.current;
        Vector2 mouse = e.mousePosition + editorScroll;

        if (e.type == EventType.MouseDown && localRect.Contains(mouse))
        {
            editorFocused = true;
            if (e.clickCount >= 2)
            {
                selectionAnchor = 0;
                caretIndex = plainText.Length;
                draggingSelection = false;
                e.Use();
                return;
            }
            int hit = HitTestIndex(mouse, lines);
            caretIndex = hit;
            selectionAnchor = hit;
            draggingSelection = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingSelection)
        {
            caretIndex = HitTestIndex(mouse, lines);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingSelection)
        {
            draggingSelection = false;
            e.Use();
        }
        else if (e.type == EventType.MouseDown && !localRect.Contains(mouse))
        {
            editorFocused = false;
            draggingSelection = false;
        }
    }

    private int HitTestIndex(Vector2 mouse, List<LineLayout> lines)
    {
        const float lineHeight = 24f;
        const float topPadding = 10f;

        int line = Mathf.Clamp(Mathf.FloorToInt((mouse.y - topPadding) / lineHeight), 0, lines.Count - 1);
        LineLayout layout = lines[line];
        if (layout.widths.Count == 0)
            return layout.start;

        for (int i = 0; i < layout.widths.Count; i++)
        {
            float left = layout.xs[i];
            float right = layout.xs[i] + layout.widths[i];
            float threshold = left + layout.widths[i] * 0.70f;

            if (i == 0 && mouse.x <= left + 4f)
                return layout.start;

            if (mouse.x >= left && mouse.x <= right)
                return mouse.x < threshold ? layout.start + i : layout.start + i + 1;
            if (mouse.x < left)
                return layout.start + i;
        }
        return layout.end;
    }

    private void HandleKeyboard(Event e)
    {
        if (!editorFocused || e.type != EventType.KeyDown)
            return;

        if (showParameterSuggestions)
        {
            if (e.keyCode == KeyCode.DownArrow)
            {
                selectedSuggestionIndex = Mathf.Min(parameterSuggestions.Count - 1, selectedSuggestionIndex + 1);
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.UpArrow)
            {
                selectedSuggestionIndex = Mathf.Max(0, selectedSuggestionIndex - 1);
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab)
            {
                AcceptCurrentSuggestion();
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.Escape)
            {
                showParameterSuggestions = false;
                e.Use();
                return;
            }
        }
        if (GUI.GetNameOfFocusedControl() == HexControlName)
            return;

        bool shift = e.shift;
        bool ctrl = e.control || e.command;

        if (ctrl && e.keyCode == KeyCode.A)
        {
            selectionAnchor = 0;
            caretIndex = plainText.Length;
            e.Use();
            return;
        }
        if (ctrl && e.keyCode == KeyCode.B) { ToggleBold(); e.Use(); return; }
        if (ctrl && e.keyCode == KeyCode.I) { ToggleItalic(); e.Use(); return; }
        if (ctrl && e.shift && e.keyCode == KeyCode.P) { InsertParameterTemplate("{param:this.}", 12); e.Use(); return; }
        if (ctrl && e.shift && e.keyCode == KeyCode.F) { InsertParameterTemplate("{param:this.|format|fallback}", 12); e.Use(); return; }
        if (ctrl && e.keyCode == KeyCode.C) { CopySelection(); e.Use(); return; }
        if (ctrl && e.keyCode == KeyCode.X) { CutSelection(); e.Use(); return; }
        if (ctrl && e.keyCode == KeyCode.V) { PasteClipboard(); e.Use(); return; }
        if (ctrl && e.keyCode == KeyCode.Z) { UndoAction(); e.Use(); return; }
        if (ctrl && (e.keyCode == KeyCode.Y)) { RedoAction(); e.Use(); return; }

        switch (e.keyCode)
        {
            case KeyCode.LeftArrow: MoveCaret(Mathf.Max(0, caretIndex - 1), shift); e.Use(); return;
            case KeyCode.RightArrow: MoveCaret(Mathf.Min(plainText.Length, caretIndex + 1), shift); e.Use(); return;
            case KeyCode.UpArrow: MoveVertical(-1, shift); e.Use(); return;
            case KeyCode.DownArrow: MoveVertical(1, shift); e.Use(); return;
            case KeyCode.Backspace: Backspace(); e.Use(); return;
            case KeyCode.Delete: DeleteForward(); e.Use(); return;
            case KeyCode.Return:
            case KeyCode.KeypadEnter: InsertText("\n"); e.Use(); return;
        }

        if (!ctrl && e.character != 0 && !char.IsControl(e.character))
        {
            InsertText(e.character.ToString());
            e.Use();
        }
    }

    private void UpdateParameterSuggestions()
    {
        showParameterSuggestions = false;
        parameterSuggestions.Clear();
        suggestionReplaceStart = -1;
        suggestionPrefix = string.Empty;

        if (!editorFocused || string.IsNullOrEmpty(parameterContext) || HasSelection())
            return;

        string before = plainText.Substring(0, Mathf.Clamp(caretIndex, 0, plainText.Length));
        int tokenStart = before.LastIndexOf("{param:", StringComparison.Ordinal);
        if (tokenStart < 0)
            return;
        int closeBrace = before.LastIndexOf('}');
        if (closeBrace > tokenStart)
            return;

        string expr = before.Substring(tokenStart + 7);
        int pipeIndex = expr.IndexOf('|');
        if (pipeIndex >= 0)
            expr = expr.Substring(0, pipeIndex);

        int dotIndex = expr.LastIndexOf('.');
        string basePath = dotIndex >= 0 ? expr.Substring(0, dotIndex) : expr;
        string partial = dotIndex >= 0 ? expr.Substring(dotIndex + 1) : string.Empty;

        List<string> candidates = GetSuggestionsForBasePath(basePath);
        if (candidates == null || candidates.Count == 0)
            return;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.IsNullOrEmpty(partial) || candidates[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                parameterSuggestions.Add(candidates[i]);
        }

        if (parameterSuggestions.Count == 0)
            return;

        suggestionPrefix = partial;
        suggestionReplaceStart = caretIndex - partial.Length;
        selectedSuggestionIndex = Mathf.Clamp(selectedSuggestionIndex, 0, parameterSuggestions.Count - 1);
        showParameterSuggestions = true;
    }

    private List<string> GetSuggestionsForBasePath(string basePath)
    {
        if (!string.Equals(parameterContext, "status", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrEmpty(basePath))
            return new List<string> { "this", "player", "battle" };
        if (basePath == "this")
            return new List<string> { "statusId", "displayName", "note", "description", "grantMode", "accumulationSourceKey", "accumulationThreshold", "durationType", "baseDuration", "maxDuration", "canStack", "baseStack", "maxStack", "dot", "onApplyTriggerKey", "onTickTriggerKey", "tickInterval", "onRemoveTriggerKey", "onExpireTriggerKey" };
        if (basePath == "this.dot")
            return new List<string> { "enabled", "tickInterval", "targetResource", "valueMode", "baseValue", "percentValue", "attributeKey", "attributeRatio", "stackMode", "stackStepValue", "canKill", "affectedByResistance", "damageTypeKey" };
        return null;
    }

    private void AcceptCurrentSuggestion()
    {
        if (!showParameterSuggestions || selectedSuggestionIndex < 0 || selectedSuggestionIndex >= parameterSuggestions.Count)
            return;

        string suggestion = parameterSuggestions[selectedSuggestionIndex];
        int replaceStart = Mathf.Clamp(suggestionReplaceStart, 0, plainText.Length);
        int replaceLength = Mathf.Clamp(caretIndex - replaceStart, 0, plainText.Length - replaceStart);
        plainText = plainText.Remove(replaceStart, replaceLength).Insert(replaceStart, suggestion);

        for (int i = 0; i < replaceLength; i++)
        {
            if (replaceStart < formats.Count)
                formats.RemoveAt(replaceStart);
        }
        for (int i = 0; i < suggestion.Length; i++)
            formats.Insert(replaceStart + i, CharFormat.Default);

        caretIndex = replaceStart + suggestion.Length;
        selectionAnchor = caretIndex;
        showParameterSuggestions = false;
    }

    private void DrawParameterSuggestionPopup()
    {
        if (!showParameterSuggestions)
            return;

        Rect popup = new Rect(24f, 86f, 220f, Mathf.Min(180f, parameterSuggestions.Count * 22f + 8f));
        EditorGUI.DrawRect(popup, new Color(0.15f, 0.15f, 0.16f, 1f));
        DrawBorder(popup, new Color(1f, 1f, 1f, 0.10f));

        for (int i = 0; i < parameterSuggestions.Count; i++)
        {
            Rect row = new Rect(popup.x + 4f, popup.y + 4f + i * 22f, popup.width - 8f, 20f);
            if (i == selectedSuggestionIndex)
                EditorGUI.DrawRect(row, new Color(0.25f, 0.38f, 0.70f, 0.75f));
            GUI.Label(row, parameterSuggestions[i], EditorStyles.miniLabel);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                selectedSuggestionIndex = i;
                AcceptCurrentSuggestion();
                Event.current.Use();
            }
        }
    }

    private void PushUndoState()
    {
        EditorStateSnapshot snap = new EditorStateSnapshot();
        snap.plainText = plainText;
        snap.formats = new List<CharFormat>(formats);
        snap.caretIndex = caretIndex;
        snap.selectionAnchor = selectionAnchor;
        undoStack.Push(snap);
        redoStack.Clear();
    }

    private void RestoreSnapshot(EditorStateSnapshot snap)
    {
        if (snap == null)
            return;
        plainText = snap.plainText ?? string.Empty;
        formats.Clear();
        formats.AddRange(snap.formats ?? new List<CharFormat>());
        caretIndex = snap.caretIndex;
        selectionAnchor = snap.selectionAnchor;
    }

    private void UndoAction()
    {
        if (undoStack.Count == 0)
            return;
        EditorStateSnapshot current = new EditorStateSnapshot();
        current.plainText = plainText;
        current.formats = new List<CharFormat>(formats);
        current.caretIndex = caretIndex;
        current.selectionAnchor = selectionAnchor;
        redoStack.Push(current);
        RestoreSnapshot(undoStack.Pop());
    }

    private void RedoAction()
    {
        if (redoStack.Count == 0)
            return;
        EditorStateSnapshot current = new EditorStateSnapshot();
        current.plainText = plainText;
        current.formats = new List<CharFormat>(formats);
        current.caretIndex = caretIndex;
        current.selectionAnchor = selectionAnchor;
        undoStack.Push(current);
        RestoreSnapshot(redoStack.Pop());
    }

    private void CopySelection()
    {
        if (!HasSelection())
            return;
        EditorGUIUtility.systemCopyBuffer = BuildRichTextStringForRange(GetSelectionStart(), GetSelectionEnd());
    }

    private void CutSelection()
    {
        if (!HasSelection())
            return;
        CopySelection();
        PushUndoState();
        DeleteSelectionIfAny();
    }

    private void PasteClipboard()
    {
        string clip = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
        if (string.IsNullOrEmpty(clip))
            return;
        PushUndoState();
        DeleteSelectionIfAny();
        InsertRichTextSnippet(clip);
    }

    private void InsertParameterTemplate(string template, int caretOffset)
    {
        PushUndoState();
        DeleteSelectionIfAny();
        plainText = plainText.Insert(caretIndex, template);
        for (int i = 0; i < template.Length; i++)
            formats.Insert(caretIndex + i, CharFormat.Default);
        caretIndex += caretOffset;
        selectionAnchor = caretIndex;
    }

    private void MoveCaret(int newIndex, bool keepAnchor)
    {
        caretIndex = Mathf.Clamp(newIndex, 0, plainText.Length);
        if (!keepAnchor)
            selectionAnchor = caretIndex;
    }

    private void MoveVertical(int deltaLines, bool keepAnchor)
    {
        List<LineLayout> lines = BuildLineLayouts();
        int lineIndex, columnIndex;
        GetLineColumnFromIndex(caretIndex, lines, out lineIndex, out columnIndex);
        int newLine = Mathf.Clamp(lineIndex + deltaLines, 0, lines.Count - 1);
        LineLayout layout = lines[newLine];
        int newIndex = Mathf.Min(layout.start + columnIndex, layout.end);
        MoveCaret(newIndex, keepAnchor);
    }

    private void GetLineColumnFromIndex(int index, List<LineLayout> lines, out int lineIndex, out int columnIndex)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (index >= lines[i].start && index <= lines[i].end)
            {
                lineIndex = i;
                columnIndex = index - lines[i].start;
                return;
            }
        }
        lineIndex = Mathf.Max(0, lines.Count - 1);
        columnIndex = Mathf.Max(0, index - lines[lineIndex].start);
    }

    private void InsertText(string value)
    {
        PushUndoState();
        DeleteSelectionIfAny();
        plainText = plainText.Insert(caretIndex, value);
        for (int i = 0; i < value.Length; i++)
            formats.Insert(caretIndex + i, CharFormat.Default);
        caretIndex += value.Length;
        selectionAnchor = caretIndex;
    }

    private void Backspace()
    {
        PushUndoState();
        if (DeleteSelectionIfAny()) return;
        if (caretIndex <= 0) return;
        plainText = plainText.Remove(caretIndex - 1, 1);
        formats.RemoveAt(caretIndex - 1);
        caretIndex--;
        selectionAnchor = caretIndex;
    }

    private void DeleteForward()
    {
        PushUndoState();
        if (DeleteSelectionIfAny()) return;
        if (caretIndex >= plainText.Length) return;
        plainText = plainText.Remove(caretIndex, 1);
        formats.RemoveAt(caretIndex);
        selectionAnchor = caretIndex;
    }

    private bool DeleteSelectionIfAny()
    {
        if (!HasSelection()) return false;
        int start = GetSelectionStart();
        int end = GetSelectionEnd();
        plainText = plainText.Remove(start, end - start);
        formats.RemoveRange(start, end - start);
        caretIndex = start;
        selectionAnchor = start;
        return true;
    }

    private bool HasSelection() { return caretIndex != selectionAnchor; }
    private int GetSelectionStart() { return Mathf.Min(caretIndex, selectionAnchor); }
    private int GetSelectionEnd() { return Mathf.Max(caretIndex, selectionAnchor); }

    private bool SelectionFullyBold() { return SelectionFullyMatches(delegate(CharFormat f) { return f.bold; }); }
    private bool SelectionFullyItalic() { return SelectionFullyMatches(delegate(CharFormat f) { return f.italic; }); }
    private bool SelectionFullyUnderline() { return SelectionFullyMatches(delegate(CharFormat f) { return f.underline; }); }

    private bool SelectionFullyMatches(Func<CharFormat, bool> predicate)
    {
        if (!HasSelection()) return false;
        for (int i = GetSelectionStart(); i < GetSelectionEnd(); i++)
        {
            if (!predicate(formats[i])) return false;
        }
        return true;
    }

    private void ToggleBold() { ToggleSelectionStyle(delegate(CharFormat f) { return f.bold; }, delegate(ref CharFormat f, bool v) { f.bold = v; }); }
    private void ToggleItalic() { ToggleSelectionStyle(delegate(CharFormat f) { return f.italic; }, delegate(ref CharFormat f, bool v) { f.italic = v; }); }
    private void ToggleUnderline() { ToggleSelectionStyle(delegate(CharFormat f) { return f.underline; }, delegate(ref CharFormat f, bool v) { f.underline = v; }); }

    private delegate bool StyleGetter(CharFormat f);
    private delegate void StyleSetter(ref CharFormat f, bool value);

    private void ToggleSelectionStyle(StyleGetter getter, StyleSetter setter)
    {
        if (!HasSelection()) return;
        PushUndoState();
        bool target = !SelectionFullyMatches(delegate(CharFormat f) { return getter(f); });
        for (int i = GetSelectionStart(); i < GetSelectionEnd(); i++)
        {
            CharFormat f = formats[i];
            setter(ref f, target);
            formats[i] = f;
        }
    }

    private void ApplyColorToSelection(Color color)
    {
        if (!HasSelection()) return;
        PushUndoState();
        ApplyColorToSelectionDirect(color);
    }

    private void ApplyColorToSelectionDirect(Color color)
    {
        if (!HasSelection()) return;
        for (int i = GetSelectionStart(); i < GetSelectionEnd(); i++)
        {
            CharFormat f = formats[i];
            f.hasColor = true;
            f.color = color;
            formats[i] = f;
        }
    }

    private void ClearFormattingOnSelection()
    {
        if (!HasSelection()) return;
        PushUndoState();
        for (int i = GetSelectionStart(); i < GetSelectionEnd(); i++)
            formats[i] = CharFormat.Default;
    }

    private bool DrawToggleToolbarButton(Rect rect, bool active, GUIContent content)
    {
        Color old = GUI.backgroundColor;
        if (active)
            GUI.backgroundColor = new Color(0.42f, 0.52f, 0.78f, 1f);
        bool clicked = GUI.Button(rect, content);
        GUI.backgroundColor = old;
        return clicked;
    }

    private string BuildRichTextString()
    {
        if (plainText.Length == 0) return string.Empty;

        StringBuilder sb = new StringBuilder();
        bool openB = false, openI = false, openU = false, openColor = false;
        Color lastColor = Color.white;

        for (int i = 0; i < plainText.Length; i++)
        {
            CharFormat cur = formats[i];
            if (openColor && (!cur.hasColor || cur.color != lastColor)) { sb.Append("</color>"); openColor = false; }
            if (openU && !cur.underline) { sb.Append("</u>"); openU = false; }
            if (openI && !cur.italic) { sb.Append("</i>"); openI = false; }
            if (openB && !cur.bold) { sb.Append("</b>"); openB = false; }

            if (!openB && cur.bold) { sb.Append("<b>"); openB = true; }
            if (!openI && cur.italic) { sb.Append("<i>"); openI = true; }
            if (!openU && cur.underline) { sb.Append("<u>"); openU = true; }
            if (!openColor && cur.hasColor)
            {
                sb.Append("<color=#");
                sb.Append(ColorUtility.ToHtmlStringRGB(cur.color));
                sb.Append(">");
                openColor = true;
                lastColor = cur.color;
            }

            sb.Append(EscapeChar(plainText[i]));
        }

        if (openColor) sb.Append("</color>");
        if (openU) sb.Append("</u>");
        if (openI) sb.Append("</i>");
        if (openB) sb.Append("</b>");
        return sb.ToString();
    }

    private string EscapeChar(char c)
    {
        switch (c)
        {
            case '<': return "&lt;";
            case '>': return "&gt;";
            case '&': return "&amp;";
            default: return c.ToString();
        }
    }

    private string BuildRichTextStringForRange(int start, int end)
    {
        string backupText = plainText;
        List<CharFormat> backupFormats = new List<CharFormat>(formats);
        string subText = plainText.Substring(start, end - start);
        List<CharFormat> subFormats = formats.GetRange(start, end - start);
        plainText = subText;
        formats.Clear();
        formats.AddRange(subFormats);
        string result = BuildRichTextString();
        plainText = backupText;
        formats.Clear();
        formats.AddRange(backupFormats);
        return result;
    }

    private void InsertRichTextSnippet(string richText)
    {
        string backupText = plainText;
        List<CharFormat> backupFormats = new List<CharFormat>(formats);
        int insertIndex = caretIndex;
        int insertAnchor = selectionAnchor;

        ParseRichTextToDocument(richText ?? string.Empty);
        string insertText = plainText;
        List<CharFormat> insertFormats = new List<CharFormat>(formats);

        plainText = backupText;
        formats.Clear();
        formats.AddRange(backupFormats);
        caretIndex = insertIndex;
        selectionAnchor = insertAnchor;

        plainText = plainText.Insert(caretIndex, insertText);
        formats.InsertRange(caretIndex, insertFormats);
        caretIndex += insertText.Length;
        selectionAnchor = caretIndex;
    }

    private void ParseRichTextToDocument(string input)
    {
        plainText = string.Empty;
        formats.Clear();

        bool bold = false, italic = false, underline = false, hasColor = false;
        Color color = Color.white;
        StringBuilder textBuilder = new StringBuilder();

        for (int i = 0; i < input.Length;)
        {
            if (input[i] == '<')
            {
                int close = input.IndexOf('>', i);
                if (close > i)
                {
                    string tag = input.Substring(i, close - i + 1);
                    string lower = tag.ToLowerInvariant();
                    if (lower == "<b>") { bold = true; i = close + 1; continue; }
                    if (lower == "</b>") { bold = false; i = close + 1; continue; }
                    if (lower == "<i>") { italic = true; i = close + 1; continue; }
                    if (lower == "</i>") { italic = false; i = close + 1; continue; }
                    if (lower == "<u>") { underline = true; i = close + 1; continue; }
                    if (lower == "</u>") { underline = false; i = close + 1; continue; }
                    if (lower.StartsWith("<color=#") && lower.EndsWith(">"))
                    {
                        string hex = tag.Substring(8, tag.Length - 9);
                        Color parsed;
                        if (ColorUtility.TryParseHtmlString("#" + hex, out parsed))
                        {
                            hasColor = true;
                            color = parsed;
                        }
                        i = close + 1;
                        continue;
                    }
                    if (lower == "</color>") { hasColor = false; color = Color.white; i = close + 1; continue; }
                }
            }

            char c = input[i];
            if (c == '&')
            {
                if (input.IndexOf("&lt;", i, StringComparison.Ordinal) == i) { c = '<'; i += 4; }
                else if (input.IndexOf("&gt;", i, StringComparison.Ordinal) == i) { c = '>'; i += 4; }
                else if (input.IndexOf("&amp;", i, StringComparison.Ordinal) == i) { c = '&'; i += 5; }
                else { i++; }
            }
            else
            {
                i++;
            }

            textBuilder.Append(c);
            CharFormat fmt = CharFormat.Default;
            fmt.bold = bold;
            fmt.italic = italic;
            fmt.underline = underline;
            fmt.hasColor = hasColor;
            fmt.color = color;
            formats.Add(fmt);
        }

        plainText = textBuilder.ToString();
        caretIndex = plainText.Length;
        selectionAnchor = caretIndex;
    }

    private void SyncHSVFromColor(Color color)
    {
        Color.RGBToHSV(color, out hue, out saturation, out value);
    }

    private Texture2D BuildHueWheelTexture(int size, int ringThickness)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float outer = size * 0.5f - 1f;
        float inner = outer - ringThickness;
        const float outline = 1.0f;
        const float aa = 3.0f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = p.magnitude;

                if (dist <= outer + aa && dist >= inner - aa)
                {
                    Color pixel;
                    bool edge = dist >= outer - outline || dist <= inner + outline;
                    if (edge)
                    {
                        pixel = new Color(0f, 0f, 0f, 1f);
                    }
                    else
                    {
                        float angle = Mathf.Atan2(p.y, p.x);
                        if (angle < 0f) angle += Mathf.PI * 2f;
                        float h = angle / (Mathf.PI * 2f);
                        pixel = Color.HSVToRGB(h, 1f, 1f);
                    }

                    float alpha = 1f;
                    if (dist > outer - aa)
                        alpha *= Mathf.Clamp01((outer + aa - dist) / (aa * 2f));
                    if (dist < inner + aa)
                        alpha *= Mathf.Clamp01((dist - (inner - aa)) / (aa * 2f));
                    pixel.a = alpha;
                    tex.SetPixel(x, y, pixel);
                }
                else
                {
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                }
            }
        }
        tex.Apply();
        return tex;
    }

    private Texture2D BuildSVSquareTexture(int size, float h)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float s = x / (float)(size - 1);
                float v = y / (float)(size - 1);
                tex.SetPixel(x, y, Color.HSVToRGB(h, s, v));
            }
        }
        tex.Apply();
        return tex;
    }

    private Texture2D LoadOptionalIcon(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private void DrawBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }
}
