using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

public class SkyPrisonAILogicPanel
{
    private enum RowKind
    {
        Node,
        SectionHeader
    }

    private class SegmentView
    {
        public string text;
        public Color color;
        public bool underline;
    }

    private class VisibleRow
    {
        public RowKind rowKind;

        public string path;
        public string parentPath;
        public string sectionFieldName;
        public string sectionLabel;
        public string targetKey;

        public LogicSentenceCategory category;
        public int depth;

        public bool enabled;
        public bool isStructure;
        public bool expanded;
        public string templateId;

        public bool drawTailAddRow;
        public string tailAddLabel;

        public List<SegmentView> segments = new List<SegmentView>();
    }

    private struct DropZone
    {
        public Rect rect;
        public string targetKey;
    }

    private readonly Dictionary<string, bool> sectionExpanded = new Dictionary<string, bool>
    {
        { "事件", true },
        { "条件", true },
        { "行动", true }
    };

    private readonly Dictionary<string, bool> nodeExpanded = new Dictionary<string, bool>();
    private readonly List<VisibleRow> visibleRows = new List<VisibleRow>();
    private readonly List<DropZone> dropZones = new List<DropZone>();

    private bool cacheDirty = true;
    private UnityEngine.Object cachedTargetObject;
    private string cachedRulePropertyPath = "";

    private LogicSentenceCategory selectedCategory = LogicSentenceCategory.Condition;
    private int selectedConditionIndex = -1;
    private int selectedActionIndex = -1;
    private int selectedMotiveIndex = -1;
    private string selectedNodePath = "";
    private string contextNodePath = "";

    private static LogicSentenceInstance sentenceClipboard;
    private static LogicSentenceCategory? sentenceClipboardCategory;

    private bool isDraggingNode = false;
    private string draggingSourcePath = "";
    private LogicSentenceCategory draggingSourceCategory;
    private LogicSentenceInstance draggingClone;

    private Texture2D eventSectionIcon;
    private Texture2D conditionSectionIcon;
    private Texture2D actionSectionIcon;
    private bool sectionIconsLoaded = false;

    private static readonly Color SelectedRowColor = new Color(0.28f, 0.40f, 0.72f, 0.28f);
    private static readonly Color DropZoneColor = new Color(0.30f, 0.60f, 1.00f, 0.10f);
    private static readonly Color OptionalSlotColor = new Color(0.72f, 0.72f, 0.72f, 1f);

    private const float RowHeight = 22f;
    private const float HeaderHeight = 22f;
    private const float IndentWidth = 18f;
    private const float FoldoutWidth = 16f;
    private const float ContentOffset = 4f;

    private const float TailAddRowHeight = 24f;
    private static readonly Color TailAddRowFill = new Color(1f, 1f, 1f, 0.035f);
    private static readonly Color TailAddRowBorder = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color TailAddRowText = new Color(0.72f, 0.72f, 0.72f, 1f);

    private const string IconFolder = "Assets/_Project/Icon/Editor/";
    private const string IconPrefix = "SkyPrisonEditor_";
    private const float SectionIconSize = 25.2f;

    private const bool EnablePerfLog = false;

    private readonly LogicTemplateContext editorContext;

    public SkyPrisonAILogicPanel() : this(LogicTemplateContext.AI) { }

    public SkyPrisonAILogicPanel(LogicTemplateContext context)
    {
        editorContext = context;
    }

    public void ResetSelection()
    {
        selectedConditionIndex = -1;
        selectedActionIndex = -1;
        selectedMotiveIndex = -1;
        selectedNodePath = "";
        contextNodePath = "";

        isDraggingNode = false;
        draggingSourcePath = "";
        draggingClone = null;
        dropZones.Clear();
    }

    public void MarkCacheDirty()
    {
        cacheDirty = true;
    }

    public bool HandleKeyboardShortcuts(SerializedProperty ruleProp, Event e)
    {
        if (ruleProp == null || e == null || e.type != EventType.KeyDown)
            return false;

        bool ctrlOrCmd = e.control || e.command;

        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            return OpenSelectedSentenceEditor(ruleProp);

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            bool deleted = DeleteSelectedSentenceIfAny(ruleProp);
            if (deleted)
                e.Use();
            return deleted;
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.C)
        {
            bool copied = CopySelectedSentence(ruleProp);
            if (copied)
                e.Use();
            return copied;
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.X)
        {
            bool cut = CutSelectedSentence(ruleProp);
            if (cut)
                e.Use();
            return cut;
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.V)
        {
            bool pasted = PasteToSelectedCategory(ruleProp);
            if (pasted)
                e.Use();
            return pasted;
        }

        return false;
    }

    public bool DeleteSelectedSentenceIfAny(SerializedProperty ruleProp)
    {
        if (ruleProp == null || string.IsNullOrWhiteSpace(selectedNodePath))
            return false;

        if (!TryGetNodeProperty(ruleProp, selectedNodePath, out _, out SerializedProperty ownerListProp, out int indexInOwner, out _))
            return false;

        if (ownerListProp == null || indexInOwner < 0 || indexInOwner >= ownerListProp.arraySize)
            return false;

        RecordUndo(ruleProp, "Delete AI Sentence");
        ownerListProp.DeleteArrayElementAtIndex(indexInOwner);

        selectedNodePath = "";
        contextNodePath = "";
        selectedConditionIndex = -1;
        selectedActionIndex = -1;
        selectedMotiveIndex = -1;

        CommitChange(ruleProp);
        MarkCacheDirty();
        return true;
    }

    public void Draw(SerializedProperty ruleProp)
    {
        if (ruleProp == null)
        {
            EditorGUILayout.HelpBox("当前规则为空。", MessageType.Info);
            return;
        }

        EnsureSectionIconsLoaded();

        Stopwatch totalSw = null;
        if (EnablePerfLog)
            totalSw = Stopwatch.StartNew();

        RebuildCacheIfNeeded(ruleProp);
        dropZones.Clear();

        Event e = Event.current;
        if (e != null && e.type == EventType.MouseUp && isDraggingNode)
            HandleDrop(ruleProp, e.mousePosition);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("逻辑树 / " + LogicSentenceContextUtility.GetContextDisplayName(editorContext), EditorStyles.boldLabel);
        GUILayout.Space(6f);

        DrawSentenceSectionHeader("事件", eventSectionIcon);
        if (GetSectionExpanded("事件"))
            DrawVisibleRowsForCategory(ruleProp, LogicSentenceCategory.Motive, "MOT_ROOT");

        GUILayout.Space(8f);

        DrawSentenceSectionHeader("条件", conditionSectionIcon);
        if (GetSectionExpanded("条件"))
            DrawVisibleRowsForCategory(ruleProp, LogicSentenceCategory.Condition, "COND_ROOT");

        GUILayout.Space(8f);

        DrawSentenceSectionHeader("行动", actionSectionIcon);
        if (GetSectionExpanded("行动"))
            DrawVisibleRowsForCategory(ruleProp, LogicSentenceCategory.Action, "ACT_ROOT");

        EditorGUILayout.EndVertical();

        if (EnablePerfLog && totalSw != null)
        {
            totalSw.Stop();
            UnityEngine.Debug.Log($"[AILogicPanel] total draw: {totalSw.ElapsedMilliseconds} ms");
        }
    }

    private void RebuildCacheIfNeeded(SerializedProperty ruleProp)
    {
        UnityEngine.Object target = ruleProp.serializedObject != null ? ruleProp.serializedObject.targetObject : null;
        string propPath = ruleProp.propertyPath ?? "";

        if (!cacheDirty && cachedTargetObject == target && cachedRulePropertyPath == propPath)
            return;

        Stopwatch sw = null;
        if (EnablePerfLog)
            sw = Stopwatch.StartNew();

        cachedTargetObject = target;
        cachedRulePropertyPath = propPath;
        cacheDirty = false;

        visibleRows.Clear();

        BuildRootVisibleRows(ruleProp, "motiveGroup", LogicSentenceCategory.Motive, "M");
        BuildRootVisibleRows(ruleProp, "conditionGroup", LogicSentenceCategory.Condition, "C");
        BuildRootVisibleRows(ruleProp, "actionGroup", LogicSentenceCategory.Action, "A");

        if (EnablePerfLog && sw != null)
        {
            sw.Stop();
            UnityEngine.Debug.Log($"[AILogicPanel] rebuild cache: {sw.ElapsedMilliseconds} ms, rows={visibleRows.Count}");
        }
    }

    private void BuildRootVisibleRows(
        SerializedProperty ruleProp,
        string groupFieldName,
        LogicSentenceCategory category,
        string rootPrefix)
    {
        SerializedProperty listProp = GetGroupItemsProperty(ruleProp, groupFieldName);
        if (listProp == null)
            return;

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty itemProp = listProp.GetArrayElementAtIndex(i);
            string path = $"{rootPrefix}/{i}";
            BuildVisibleNodeRecursive(itemProp, path, category, 0, false);
        }
    }

    private void BuildVisibleNodeRecursive(
        SerializedProperty nodeProp,
        string nodePath,
        LogicSentenceCategory category,
        int depth,
        bool isNestedAction)
    {
        if (nodeProp == null)
            return;

        SerializedProperty templateIdProp = nodeProp.FindPropertyRelative("templateId");
        if (templateIdProp == null || string.IsNullOrWhiteSpace(templateIdProp.stringValue))
            return;

        LogicSentenceTemplate template = AILogicSentenceTemplateLibrary.GetTemplateById(templateIdProp.stringValue);
        if (template == null)
            return;

        bool enabled = nodeProp.FindPropertyRelative("enabled") == null || nodeProp.FindPropertyRelative("enabled").boolValue;
        bool isStructure = IsStructureTemplate(template);

        VisibleRow node = new VisibleRow
        {
            rowKind = RowKind.Node,
            path = nodePath,
            category = category,
            depth = depth,
            enabled = enabled,
            isStructure = isStructure,
            expanded = GetNodeExpanded(nodePath),
            templateId = template.templateId
        };

        BuildSegmentsForNode(node, nodeProp, template, category, nodePath, isNestedAction);
        visibleRows.Add(node);

        if (!isStructure || !node.expanded)
            return;

        BuildStructuredChildren(nodeProp, nodePath, template, depth + 1);
    }

    private void BuildStructuredChildren(
        SerializedProperty nodeProp,
        string nodePath,
        LogicSentenceTemplate template,
        int sectionDepth)
    {
        string templateId = template.templateId;

        if (templateId == "flow_if_then")
        {
            AddSectionAndChildren(nodeProp, nodePath, "If", "conditionChildren", LogicSentenceCategory.Condition, sectionDepth);
            AddSectionAndChildren(nodeProp, nodePath, "Then", "thenChildren", LogicSentenceCategory.Action, sectionDepth);
            return;
        }

        if (templateId == "flow_if_then_else")
        {
            AddSectionAndChildren(nodeProp, nodePath, "If", "conditionChildren", LogicSentenceCategory.Condition, sectionDepth);
            AddSectionAndChildren(nodeProp, nodePath, "Then", "thenChildren", LogicSentenceCategory.Action, sectionDepth);
            AddSectionAndChildren(nodeProp, nodePath, "Else", "elseChildren", LogicSentenceCategory.Action, sectionDepth);
            return;
        }

        if (templateId == "flow_while")
        {
            AddSectionAndChildren(nodeProp, nodePath, "While", "conditionChildren", LogicSentenceCategory.Condition, sectionDepth);
            AddSectionAndChildren(nodeProp, nodePath, "Body", "bodyChildren", LogicSentenceCategory.Action, sectionDepth);
            return;
        }

        if (templateId == "flow_do_while")
        {
            AddSectionAndChildren(nodeProp, nodePath, "Do", "bodyChildren", LogicSentenceCategory.Action, sectionDepth);
            AddSectionAndChildren(nodeProp, nodePath, "While", "conditionChildren", LogicSentenceCategory.Condition, sectionDepth);
            return;
        }

        if (templateId == "flow_loop")
        {
            AddSectionAndChildren(nodeProp, nodePath, "Body", "bodyChildren", LogicSentenceCategory.Action, sectionDepth);
            return;
        }
    }

    private void AddSectionAndChildren(
        SerializedProperty ownerNodeProp,
        string ownerPath,
        string sectionLabel,
        string fieldName,
        LogicSentenceCategory childCategory,
        int sectionDepth)
    {
        visibleRows.Add(new VisibleRow
        {
            rowKind = RowKind.SectionHeader,
            parentPath = ownerPath,
            sectionFieldName = fieldName,
            sectionLabel = sectionLabel,
            category = childCategory,
            depth = sectionDepth,
            targetKey = $"SECTION:{ownerPath}:{fieldName}:{(int)childCategory}",
            drawTailAddRow = true,
            tailAddLabel = childCategory == LogicSentenceCategory.Condition ? "在此添加条件" : "在此添加行动"
        });

        SerializedProperty listProp = ownerNodeProp.FindPropertyRelative(fieldName);
        if (listProp == null)
            return;

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty child = listProp.GetArrayElementAtIndex(i);
            string childPath = $"{ownerPath}/{fieldName}/{i}";
            BuildVisibleNodeRecursive(child, childPath, childCategory, sectionDepth + 1, childCategory == LogicSentenceCategory.Action);
        }
    }

    private bool IsCommentTemplate(LogicSentenceTemplate template)
    {
        if (template == null)
            return false;

        string id = template.templateId ?? "";
        string title = template.displayName ?? "";

        id = id.ToLowerInvariant();
        title = title.ToLowerInvariant();

        return id.Contains("comment") ||
               id.Contains("note") ||
               title.Contains("注释") ||
               title.Contains("comment") ||
               title.Contains("note");
    }

    private void BuildSegmentsForNode(
        VisibleRow node,
        SerializedProperty sentenceProp,
        LogicSentenceTemplate template,
        LogicSentenceCategory category,
        string nodePath,
        bool isNestedAction)
    {
        node.segments.Clear();

        bool isComment = IsCommentTemplate(template);

        if (isComment)
        {
            string commentText = "";

            SerializedProperty assignmentsProp = sentenceProp.FindPropertyRelative("slotAssignments");
            if (assignmentsProp != null)
            {
                for (int i = 0; i < assignmentsProp.arraySize; i++)
                {
                    SerializedProperty item = assignmentsProp.GetArrayElementAtIndex(i);
                    if (item == null)
                        continue;

                    SerializedProperty valueProp = item.FindPropertyRelative("value");
                    if (valueProp == null)
                        continue;

                    string stringValue = valueProp.FindPropertyRelative("stringValue") != null
                        ? valueProp.FindPropertyRelative("stringValue").stringValue
                        : "";

                    if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        commentText = stringValue;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(commentText))
                commentText = "注释";

            node.segments.Add(new SegmentView
            {
                text = "--注释：" + commentText + "--",
                color = new Color(0.35f, 0.85f, 0.35f, 1f),
                underline = false
            });

            return;
        }

        node.segments.Add(new SegmentView
        {
            text = GetPrefix(category, nodePath, isNestedAction),
            color = Color.white,
            underline = false
        });

        SerializedProperty assignmentsProp2 = sentenceProp.FindPropertyRelative("slotAssignments");

        for (int i = 0; i < template.tokens.Count; i++)
        {
            LogicSentenceTemplate.TextToken token = template.tokens[i];

            if (!token.isSlot)
            {
                node.segments.Add(new SegmentView
                {
                    text = token.text,
                    color = Color.white,
                    underline = false
                });
            }
            else
            {
                LogicSentenceTemplate.SlotDefinition slotDef = GetSlotDefinition(template, token.slotId);
                SerializedProperty assignmentProp = FindAssignmentProperty(assignmentsProp2, token.slotId);

                string slotText = GetSlotDisplayTextFromProperty(assignmentProp, slotDef);

                Color slotColor;
                if (slotDef != null && !slotDef.required && IsAssignmentEmpty(assignmentProp))
                {
                    slotColor = !node.enabled ? LogicSentenceEditorGUI.DisabledColor : OptionalSlotColor;
                }
                else
                {
                    bool valid = IsAssignmentValidFromProperty(assignmentProp, slotDef);
                    slotColor = !node.enabled
                        ? LogicSentenceEditorGUI.DisabledColor
                        : valid ? LogicSentenceEditorGUI.ValidColor : LogicSentenceEditorGUI.ErrorColor;
                }

                node.segments.Add(new SegmentView
                {
                    text = slotText,
                    color = slotColor,
                    underline = true
                });
            }
        }
    }

    private void EnsureSectionIconsLoaded()
    {
        if (sectionIconsLoaded)
            return;

        eventSectionIcon = LoadSectionIcon(11);
        conditionSectionIcon = LoadSectionIcon(12);
        actionSectionIcon = LoadSectionIcon(13);
        sectionIconsLoaded = true;
    }

    private Texture2D LoadSectionIcon(int number)
    {
        string num = number.ToString("00");
        string basePath = IconFolder + IconPrefix + num;

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

    private void RequestHostRepaintLight()
    {
        GUI.changed = true;
        EditorWindow focused = EditorWindow.focusedWindow;
        if (focused != null)
            focused.Repaint();
    }

    private void DrawSentenceSectionHeader(string sectionName, Texture2D icon)
    {
        bool expanded = GetSectionExpanded(sectionName);

        Rect rowRect = EditorGUILayout.GetControlRect(false, 30f);

        Rect buttonRect = new Rect(rowRect.x, rowRect.y + 1f, 24f, 24f);
        if (GUI.Button(buttonRect, expanded ? "-" : "+"))
        {
            sectionExpanded[sectionName] = !expanded;
            RequestHostRepaintLight();
        }

        float x = buttonRect.xMax + 6f;

        if (icon != null)
        {
            Rect iconRect = new Rect(x, rowRect.y + (rowRect.height - SectionIconSize) * 0.5f, SectionIconSize, SectionIconSize);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            x = iconRect.xMax + 5f;
        }

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        EditorGUI.LabelField(new Rect(x, rowRect.y + 4f, rowRect.width - (x - rowRect.x), 22f), sectionName, titleStyle);

        Rect lineRect = GUILayoutUtility.GetRect(10f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(lineRect, new Color(0.28f, 0.28f, 0.28f, 1f));
    }

    private void DrawVisibleRowsForCategory(
        SerializedProperty ruleProp,
        LogicSentenceCategory category,
        string rootDropKey)
    {
        Stopwatch sw = null;
        if (EnablePerfLog)
            sw = Stopwatch.StartNew();

        EditorGUILayout.BeginVertical("box");
        Rect startRect = GUILayoutUtility.GetRect(10f, 6f, GUILayout.ExpandWidth(true));
        HandleSectionContextMenu(startRect, ruleProp, category);

        bool any = false;
        for (int i = 0; i < visibleRows.Count; i++)
        {
            if (visibleRows[i].category != category)
                continue;

            DrawVisibleRow(ruleProp, visibleRows[i]);
            any = true;
        }

        if (!any)
            GUILayout.Space(2f);

        string label = category == LogicSentenceCategory.Motive
            ? "在此添加事件"
            : category == LogicSentenceCategory.Condition
                ? "在此添加条件"
                : "在此添加行动";

        DrawTailAddRow(ruleProp, label, category, rootDropKey);

        EditorGUILayout.EndVertical();

        if (EnablePerfLog && sw != null)
        {
            sw.Stop();
            UnityEngine.Debug.Log($"[AILogicPanel] draw {category}: {sw.ElapsedMilliseconds} ms");
        }
    }

    private void DrawVisibleRow(SerializedProperty ruleProp, VisibleRow row)
    {
        if (row.rowKind == RowKind.SectionHeader)
        {
            DrawSectionRow(ruleProp, row);
            return;
        }

        DrawNodeRow(ruleProp, row);
    }

    private void DrawSectionRow(SerializedProperty ruleProp, VisibleRow row)
    {
        Rect rowRect = EditorGUILayout.GetControlRect(false, HeaderHeight);

        float indentX = rowRect.x + row.depth * IndentWidth + FoldoutWidth;

        GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold
        };

        EditorGUI.LabelField(
            new Rect(indentX, rowRect.y + 1f, rowRect.width - (indentX - rowRect.x), 18f),
            row.sectionLabel,
            style
        );

        RegisterDropZone(rowRect, row.targetKey);

        Event e = Event.current;
        if (e != null && e.type == EventType.ContextClick && rowRect.Contains(e.mousePosition))
        {
            contextNodePath = row.parentPath;
            ShowSectionContextMenu(ruleProp, row.parentPath, row.sectionFieldName, row.sectionLabel, row.category);
            e.Use();
        }

        if (row.drawTailAddRow)
        {
            DrawTailAddRow(
                ruleProp,
                row.tailAddLabel,
                row.category,
                row.targetKey,
                row.parentPath,
                row.sectionFieldName
            );
        }
    }

    private void DrawNodeRow(SerializedProperty ruleProp, VisibleRow row)
    {
        bool selected = selectedNodePath == row.path;

        Rect rowRect = EditorGUILayout.GetControlRect(false, RowHeight);

        if (selected)
            EditorGUI.DrawRect(rowRect, SelectedRowColor);

        float indentX = rowRect.x + row.depth * IndentWidth;
        float contentX = indentX;

        if (row.isStructure)
        {
            Rect foldRect = new Rect(indentX, rowRect.y, FoldoutWidth, rowRect.height);
            bool newExpanded = EditorGUI.Foldout(foldRect, row.expanded, GUIContent.none, false);
            if (newExpanded != row.expanded)
            {
                nodeExpanded[row.path] = newExpanded;
                MarkCacheDirty();
                RequestHostRepaintLight();
                row.expanded = newExpanded;
            }
            contentX += FoldoutWidth;
        }
        else
        {
            contentX += FoldoutWidth;
        }

        DrawCachedSentenceSegments(
            new Rect(contentX + ContentOffset, rowRect.y, rowRect.width - (contentX - rowRect.x) - 8f, rowRect.height),
            row
        );

        HandleNodeRowEvents(ruleProp, row, rowRect);
    }

    private void DrawCachedSentenceSegments(Rect rect, VisibleRow row)
    {
        float x = rect.x;
        float y = rect.y + 2f;
        GUIStyle style = new GUIStyle(EditorStyles.label);

        Color oldColor = GUI.color;
        if (!row.enabled)
            GUI.color = LogicSentenceEditorGUI.DisabledColor;

        for (int i = 0; i < row.segments.Count; i++)
        {
            SegmentView seg = row.segments[i];
            Vector2 size = style.CalcSize(new GUIContent(seg.text));
            Rect textRect = new Rect(x, y, size.x, 20f);

            Color segColor = row.enabled ? seg.color : LogicSentenceEditorGUI.DisabledColor;

            Color old = GUI.color;
            GUI.color = segColor;
            GUI.Label(textRect, seg.text, style);
            GUI.color = old;

            if (seg.underline)
            {
                Rect underlineRect = new Rect(textRect.x, textRect.yMax - 2f, textRect.width, 1f);
                EditorGUI.DrawRect(underlineRect, segColor);
            }

            x = textRect.xMax + 6f;
        }

        GUI.color = oldColor;
    }

    private void DrawTailAddRow(
        SerializedProperty ruleProp,
        string label,
        LogicSentenceCategory category,
        string targetKey,
        string ownerPath = "",
        string fieldName = "")
    {
        Rect rowRect = EditorGUILayout.GetControlRect(false, TailAddRowHeight);

        EditorGUI.DrawRect(rowRect, TailAddRowFill);

        Rect topBorder = new Rect(rowRect.x, rowRect.y, rowRect.width, 1f);
        Rect bottomBorder = new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f);
        EditorGUI.DrawRect(topBorder, TailAddRowBorder);
        EditorGUI.DrawRect(bottomBorder, TailAddRowBorder);

        Color old = GUI.color;
        GUI.color = TailAddRowText;
        EditorGUI.LabelField(
            new Rect(rowRect.x + 8f, rowRect.y + 3f, rowRect.width - 16f, 18f),
            "+ " + label,
            EditorStyles.miniLabel
        );
        GUI.color = old;

        RegisterDropZone(rowRect, targetKey);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                if (string.IsNullOrWhiteSpace(ownerPath))
                {
                    OpenTemplatePicker(category, instance =>
                    {
                        if (instance == null)
                            return;

                        SerializedProperty listProp = GetTargetListProperty(ruleProp, targetKey);
                        if (listProp == null)
                            return;

                        RecordUndo(ruleProp, "Add AI Sentence");
                        AddSentenceInstanceToList(listProp, instance);
                        CommitChange(ruleProp);
                        MarkCacheDirty();
                    });
                }
                else
                {
                    OpenTemplatePicker(category, instance =>
                    {
                        if (instance == null)
                            return;

                        SerializedProperty listProp = GetSectionListProperty(ruleProp, ownerPath, fieldName);
                        if (listProp == null)
                            return;

                        RecordUndo(ruleProp, "Add AI Sentence");
                        AddSentenceInstanceToList(listProp, instance);
                        CommitChange(ruleProp);
                        MarkCacheDirty();
                    });
                }

                e.Use();
                return;
            }

            if (e.button == 1)
            {
                if (string.IsNullOrWhiteSpace(ownerPath))
                {
                    ShowSectionRootContextMenu(ruleProp, category);
                }
                else
                {
                    ShowSectionContextMenu(ruleProp, ownerPath, fieldName, label.Replace("在此添加", "").Trim(), category);
                }

                e.Use();
            }
        }
    }

    private void HandleNodeRowEvents(SerializedProperty ruleProp, VisibleRow row, Rect rowRect)
    {
        Event e = Event.current;
        if (e == null)
            return;

        Rect hitRect = new Rect(
            rowRect.x,
            rowRect.y - 1f,
            rowRect.width,
            rowRect.height + 2f
        );

        if (e.type == EventType.MouseDown && hitRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                bool selectionChanged =
                    selectedNodePath != row.path ||
                    selectedCategory != row.category;

                SelectNode(row.path, row.category);

                GUI.FocusControl(null);
                EditorGUIUtility.editingTextField = false;

                if (selectionChanged)
                    RequestHostRepaintLight();

                if (e.clickCount >= 2)
                    OpenNodeEditorByPath(ruleProp, row.path, row.category);

                e.Use();
                return;
            }

            if (e.button == 1)
            {
                contextNodePath = row.path;

                if (selectedNodePath != row.path)
                {
                    selectedNodePath = row.path;
                    selectedCategory = row.category;
                }

                GUI.FocusControl(null);
                EditorGUIUtility.editingTextField = false;

                ShowNodeContextMenu(ruleProp, row.path, row.category, row.templateId);
                e.Use();
                return;
            }
        }

        if (e.type == EventType.MouseDrag &&
            e.button == 0 &&
            hitRect.Contains(e.mousePosition) &&
            selectedNodePath == row.path)
        {
            if (TryGetNodeProperty(ruleProp, row.path, out SerializedProperty selectedNodeProp, out _, out _, out _))
            {
                isDraggingNode = true;
                draggingSourcePath = row.path;
                draggingSourceCategory = row.category;
                draggingClone = CloneSentenceInstance(ReadSentenceInstanceFromProperty(selectedNodeProp));
                e.Use();
            }
        }
    }

    private void HandleDrop(SerializedProperty ruleProp, Vector2 mousePosition)
    {
        if (!isDraggingNode || draggingClone == null)
        {
            ClearDragging();
            return;
        }

        for (int i = 0; i < dropZones.Count; i++)
        {
            if (!dropZones[i].rect.Contains(mousePosition))
                continue;

            SerializedProperty targetListProp = GetTargetListProperty(ruleProp, dropZones[i].targetKey);
            if (targetListProp == null)
                continue;

            if (!IsDropAllowed(dropZones[i].targetKey, draggingSourceCategory))
                continue;

            RecordUndo(ruleProp, "Move AI Sentence");

            if (dropZones[i].targetKey == "MOT_ROOT")
                targetListProp.arraySize = 0;

            RemoveNodeByPath(ruleProp, draggingSourcePath);
            AddSentenceInstanceToList(targetListProp, CloneSentenceInstance(draggingClone));
            CommitChange(ruleProp);
            MarkCacheDirty();

            ClearDragging();
            return;
        }

        ClearDragging();
    }

    private bool IsDropAllowed(string targetKey, LogicSentenceCategory draggingCategory)
    {
        if (targetKey == "COND_ROOT")
            return draggingCategory == LogicSentenceCategory.Condition;

        if (targetKey == "ACT_ROOT")
            return draggingCategory == LogicSentenceCategory.Action;

        if (targetKey == "MOT_ROOT")
            return draggingCategory == LogicSentenceCategory.Motive;

        if (!targetKey.StartsWith("SECTION:"))
            return false;

        string[] parts = targetKey.Split(':');
        if (parts.Length < 4)
            return false;

        if (!int.TryParse(parts[3], out int catInt))
            return false;

        return draggingCategory == (LogicSentenceCategory)catInt;
    }

    private void ClearDragging()
    {
        isDraggingNode = false;
        draggingSourcePath = "";
        draggingClone = null;
    }

    private void RegisterDropZone(Rect rect, string targetKey)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        dropZones.Add(new DropZone
        {
            rect = rect,
            targetKey = targetKey
        });

        if (isDraggingNode && IsDropAllowed(targetKey, draggingSourceCategory))
            EditorGUI.DrawRect(rect, DropZoneColor);
    }

    private Rect GetLastSafeRect()
    {
        Rect last = GUILayoutUtility.GetLastRect();
        return new Rect(last.x, last.y, Mathf.Max(8f, last.width), Mathf.Max(18f, last.height));
    }

    private string GetPrefix(LogicSentenceCategory category, string nodePath, bool isNestedAction)
    {
        if (category == LogicSentenceCategory.Motive)
            return "当 ";

        if (category == LogicSentenceCategory.Condition)
        {
            if (nodePath.StartsWith("C/0"))
                return "如果 ";
            return "并且 ";
        }

        return "执行 ";
    }

    private bool IsStructureTemplate(LogicSentenceTemplate template)
    {
        if (template == null)
            return false;

        return template.nodeKind == LogicNodeKind.Branch ||
               template.nodeKind == LogicNodeKind.Loop ||
               template.nodeKind == LogicNodeKind.Container;
    }

    private void ShowNodeContextMenu(
        SerializedProperty ruleProp,
        string nodePath,
        LogicSentenceCategory category,
        string templateId)
    {
        LogicSentenceTemplate template = AILogicSentenceTemplateLibrary.GetTemplateById(templateId);
        if (template == null)
            return;

        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("编辑"), false, () => OpenNodeEditorByPath(ruleProp, nodePath, category));

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("复制"), false, () => CopyNodeByPath(ruleProp, nodePath, category));
        menu.AddItem(new GUIContent("剪切"), false, () => CutNodeByPath(ruleProp, nodePath, category));

        if (CanPasteToCategory(category))
            menu.AddItem(new GUIContent("粘贴到当前分类"), false, () => PasteToCategoryRoot(ruleProp, category));
        else
            menu.AddDisabledItem(new GUIContent("粘贴到当前分类"));

        menu.AddItem(new GUIContent("删除"), false, () =>
        {
            SelectNode(nodePath, category);
            DeleteSelectedSentenceIfAny(ruleProp);
        });

        if (template.templateId == "flow_if_then")
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在 If 下新建条件"), false, () => AddToSection(ruleProp, nodePath, "conditionChildren", LogicSentenceCategory.Condition, "Add If Condition"));
            menu.AddItem(new GUIContent("在 Then 下新建动作"), false, () => AddToSection(ruleProp, nodePath, "thenChildren", LogicSentenceCategory.Action, "Add Then Action"));
        }
        else if (template.templateId == "flow_if_then_else")
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在 If 下新建条件"), false, () => AddToSection(ruleProp, nodePath, "conditionChildren", LogicSentenceCategory.Condition, "Add If Condition"));
            menu.AddItem(new GUIContent("在 Then 下新建动作"), false, () => AddToSection(ruleProp, nodePath, "thenChildren", LogicSentenceCategory.Action, "Add Then Action"));
            menu.AddItem(new GUIContent("在 Else 下新建动作"), false, () => AddToSection(ruleProp, nodePath, "elseChildren", LogicSentenceCategory.Action, "Add Else Action"));
        }
        else if (template.templateId == "flow_while")
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在 While 下新建条件"), false, () => AddToSection(ruleProp, nodePath, "conditionChildren", LogicSentenceCategory.Condition, "Add While Condition"));
            menu.AddItem(new GUIContent("在 Body 下新建动作"), false, () => AddToSection(ruleProp, nodePath, "bodyChildren", LogicSentenceCategory.Action, "Add While Body Action"));
        }
        else if (template.templateId == "flow_do_while")
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在 Do 下新建动作"), false, () => AddToSection(ruleProp, nodePath, "bodyChildren", LogicSentenceCategory.Action, "Add Do Action"));
            menu.AddItem(new GUIContent("在 While 下新建条件"), false, () => AddToSection(ruleProp, nodePath, "conditionChildren", LogicSentenceCategory.Condition, "Add DoWhile Condition"));
        }
        else if (template.templateId == "flow_loop")
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在 Body 下新建动作"), false, () => AddToSection(ruleProp, nodePath, "bodyChildren", LogicSentenceCategory.Action, "Add Loop Action"));
        }

        menu.ShowAsContext();
    }

    private void ShowSectionContextMenu(
        SerializedProperty ruleProp,
        string ownerPath,
        string fieldName,
        string sectionLabel,
        LogicSentenceCategory childCategory)
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent($"在 {sectionLabel} 下新建"), false, () =>
        {
            OpenTemplatePicker(childCategory, instance =>
            {
                if (instance == null)
                    return;

                SerializedProperty targetList = GetSectionListProperty(ruleProp, ownerPath, fieldName);
                if (targetList == null)
                    return;

                RecordUndo(ruleProp, $"Add {sectionLabel} Child");
                AddSentenceInstanceToList(targetList, instance);
                CommitChange(ruleProp);
                MarkCacheDirty();
            });
        });

        if (CanPasteToCategory(childCategory))
        {
            menu.AddItem(new GUIContent($"粘贴到 {sectionLabel}"), false, () =>
            {
                SerializedProperty targetList = GetSectionListProperty(ruleProp, ownerPath, fieldName);
                if (targetList == null)
                    return;

                RecordUndo(ruleProp, $"Paste To {sectionLabel}");
                AddSentenceInstanceToList(targetList, CloneSentenceInstance(sentenceClipboard));
                CommitChange(ruleProp);
                MarkCacheDirty();
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent($"粘贴到 {sectionLabel}"));
        }

        menu.ShowAsContext();
    }

    private void AddToSection(
        SerializedProperty ruleProp,
        string ownerPath,
        string fieldName,
        LogicSentenceCategory childCategory,
        string undoName)
    {
        OpenTemplatePicker(childCategory, instance =>
        {
            if (instance == null)
                return;

            SerializedProperty targetList = GetSectionListProperty(ruleProp, ownerPath, fieldName);
            if (targetList == null)
                return;

            RecordUndo(ruleProp, undoName);
            AddSentenceInstanceToList(targetList, instance);
            CommitChange(ruleProp);
            MarkCacheDirty();
        });
    }

    private void HandleSectionContextMenu(
        Rect anchorRect,
        SerializedProperty ruleProp,
        LogicSentenceCategory category)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.ContextClick)
            return;

        Rect lastRect = GUILayoutUtility.GetLastRect();
        Rect hitRect = new Rect(
            Mathf.Min(anchorRect.x, lastRect.x),
            anchorRect.y,
            Mathf.Max(anchorRect.width, lastRect.width),
            Mathf.Max(40f, lastRect.yMax - anchorRect.y)
        );

        if (!hitRect.Contains(e.mousePosition))
            return;

        ShowSectionRootContextMenu(ruleProp, category);
        e.Use();
    }

    private void ShowSectionRootContextMenu(SerializedProperty ruleProp, LogicSentenceCategory category)
    {
        GenericMenu menu = new GenericMenu();

        if (category == LogicSentenceCategory.Motive)
        {
            SerializedProperty listProp = GetGroupItemsProperty(ruleProp, "motiveGroup");
            bool hasEvent = listProp != null && listProp.arraySize > 0;

            if (!hasEvent)
            {
                menu.AddItem(new GUIContent("新事件"), false, () =>
                {
                    OpenTemplatePicker(LogicSentenceCategory.Motive, instance =>
                    {
                        if (instance == null)
                            return;

                        SerializedProperty motiveList = GetGroupItemsProperty(ruleProp, "motiveGroup");
                        if (motiveList == null)
                            return;

                        RecordUndo(ruleProp, "Add AI Event");
                        motiveList.arraySize = 0;
                        int newIndex = AddSentenceInstanceToList(motiveList, instance);
                        SelectNode($"M/{newIndex}", LogicSentenceCategory.Motive);
                        CommitChange(ruleProp);
                        MarkCacheDirty();
                    });
                });

                if (CanPasteToCategory(LogicSentenceCategory.Motive))
                {
                    menu.AddItem(new GUIContent("粘贴到事件"), false, () =>
                    {
                        SerializedProperty motiveList = GetGroupItemsProperty(ruleProp, "motiveGroup");
                        if (motiveList == null)
                            return;

                        RecordUndo(ruleProp, "Paste AI Event");
                        motiveList.arraySize = 0;
                        int newIndex = AddSentenceInstanceToList(motiveList, CloneSentenceInstance(sentenceClipboard));
                        SelectNode($"M/{newIndex}", LogicSentenceCategory.Motive);
                        CommitChange(ruleProp);
                        MarkCacheDirty();
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("粘贴到事件"));
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("新事件（事件区最多 1 条）"));
                menu.AddDisabledItem(new GUIContent("粘贴到事件（事件区最多 1 条）"));
            }
        }
        else if (category == LogicSentenceCategory.Condition)
        {
            menu.AddItem(new GUIContent("新条件"), false, () =>
            {
                OpenTemplatePicker(LogicSentenceCategory.Condition, instance =>
                {
                    if (instance == null)
                        return;

                    SerializedProperty listProp = GetGroupItemsProperty(ruleProp, "conditionGroup");
                    if (listProp == null)
                        return;

                    RecordUndo(ruleProp, "Add AI Condition");
                    int newIndex = AddSentenceInstanceToList(listProp, instance);
                    SelectNode($"C/{newIndex}", LogicSentenceCategory.Condition);
                    CommitChange(ruleProp);
                    MarkCacheDirty();
                });
            });

            if (CanPasteToCategory(LogicSentenceCategory.Condition))
                menu.AddItem(new GUIContent("粘贴到条件"), false, () => PasteToCategoryRoot(ruleProp, LogicSentenceCategory.Condition));
            else
                menu.AddDisabledItem(new GUIContent("粘贴到条件"));
        }
        else
        {
            menu.AddItem(new GUIContent("新行动"), false, () =>
            {
                OpenTemplatePicker(LogicSentenceCategory.Action, instance =>
                {
                    if (instance == null)
                        return;

                    SerializedProperty listProp = GetGroupItemsProperty(ruleProp, "actionGroup");
                    if (listProp == null)
                        return;

                    RecordUndo(ruleProp, "Add AI Action");
                    int newIndex = AddSentenceInstanceToList(listProp, instance);
                    SelectNode($"A/{newIndex}", LogicSentenceCategory.Action);
                    CommitChange(ruleProp);
                    MarkCacheDirty();
                });
            });

            if (CanPasteToCategory(LogicSentenceCategory.Action))
                menu.AddItem(new GUIContent("粘贴到行动"), false, () => PasteToCategoryRoot(ruleProp, LogicSentenceCategory.Action));
            else
                menu.AddDisabledItem(new GUIContent("粘贴到行动"));
        }

        menu.ShowAsContext();
    }

    private void OpenTemplatePicker(LogicSentenceCategory category, Action<LogicSentenceInstance> onPicked)
    {
        AILogicSentenceTemplatePickerWindow.Open(category, editorContext, onPicked);
    }

    private bool OpenSelectedSentenceEditor(SerializedProperty ruleProp)
    {
        if (string.IsNullOrWhiteSpace(selectedNodePath))
            return false;

        if (!TryGetNodeProperty(ruleProp, selectedNodePath, out _, out _, out _, out LogicSentenceCategory category))
            return false;

        OpenNodeEditorByPath(ruleProp, selectedNodePath, category);
        return true;
    }

    private void OpenNodeEditorByPath(SerializedProperty ruleProp, string nodePath, LogicSentenceCategory category)
    {
        if (!TryGetNodeProperty(ruleProp, nodePath, out SerializedProperty nodeProp, out _, out _, out _))
            return;

        LogicSentenceInstance existing = ReadSentenceInstanceFromProperty(nodeProp);
        if (existing == null)
            return;

        AILogicSentenceTemplatePickerWindow.OpenForEdit(category, editorContext, existing, edited =>
        {
            if (edited == null)
                return;

            RecordUndo(ruleProp, "Edit AI Sentence");
            WriteSentenceInstanceToProperty(edited, nodeProp);
            SelectNode(nodePath, category);
            CommitChange(ruleProp);
            MarkCacheDirty();
        });
    }

    private bool CopySelectedSentence(SerializedProperty ruleProp)
    {
        if (string.IsNullOrWhiteSpace(selectedNodePath))
            return false;

        return CopyNodeByPath(ruleProp, selectedNodePath, selectedCategory);
    }

    private bool CopyNodeByPath(SerializedProperty ruleProp, string path, LogicSentenceCategory category)
    {
        if (!TryGetNodeProperty(ruleProp, path, out SerializedProperty nodeProp, out _, out _, out _))
            return false;

        sentenceClipboard = CloneSentenceInstance(ReadSentenceInstanceFromProperty(nodeProp));
        sentenceClipboardCategory = category;
        return true;
    }

    private bool CutSelectedSentence(SerializedProperty ruleProp)
    {
        if (!CopySelectedSentence(ruleProp))
            return false;

        return DeleteSelectedSentenceIfAny(ruleProp);
    }

    private bool CutNodeByPath(SerializedProperty ruleProp, string path, LogicSentenceCategory category)
    {
        if (!CopyNodeByPath(ruleProp, path, category))
            return false;

        SelectNode(path, category);
        return DeleteSelectedSentenceIfAny(ruleProp);
    }

    private bool PasteToSelectedCategory(SerializedProperty ruleProp)
    {
        if (sentenceClipboard == null || !sentenceClipboardCategory.HasValue)
            return false;

        PasteToCategoryRoot(ruleProp, sentenceClipboardCategory.Value);
        return true;
    }

    private bool CanPasteToCategory(LogicSentenceCategory category)
    {
        return sentenceClipboard != null &&
               sentenceClipboardCategory.HasValue &&
               sentenceClipboardCategory.Value == category;
    }

    private void PasteToCategoryRoot(SerializedProperty ruleProp, LogicSentenceCategory category)
    {
        if (!CanPasteToCategory(category))
            return;

        if (category == LogicSentenceCategory.Motive)
        {
            SerializedProperty motiveList = GetGroupItemsProperty(ruleProp, "motiveGroup");
            if (motiveList == null)
                return;

            RecordUndo(ruleProp, "Paste AI Event");
            motiveList.arraySize = 0;
            int newIndex = AddSentenceInstanceToList(motiveList, CloneSentenceInstance(sentenceClipboard));
            SelectNode($"M/{newIndex}", LogicSentenceCategory.Motive);
            CommitChange(ruleProp);
            MarkCacheDirty();
            return;
        }

        SerializedProperty targetList = category == LogicSentenceCategory.Condition
            ? GetGroupItemsProperty(ruleProp, "conditionGroup")
            : GetGroupItemsProperty(ruleProp, "actionGroup");

        if (targetList == null)
            return;

        RecordUndo(ruleProp, "Paste AI Sentence");
        int newIndex2 = AddSentenceInstanceToList(targetList, CloneSentenceInstance(sentenceClipboard));
        SelectNode(category == LogicSentenceCategory.Condition ? $"C/{newIndex2}" : $"A/{newIndex2}", category);
        CommitChange(ruleProp);
        MarkCacheDirty();
    }

    private void RecordUndo(SerializedProperty ruleProp, string actionName)
    {
        if (ruleProp == null)
            return;

        SerializedObject so = ruleProp.serializedObject;
        if (so == null || so.targetObject == null)
            return;

        Undo.RecordObject(so.targetObject, actionName);
    }

    private void CommitChange(SerializedProperty ruleProp)
    {
        if (ruleProp == null)
            return;

        SerializedObject so = ruleProp.serializedObject;
        if (so == null)
            return;

        so.ApplyModifiedProperties();

        if (so.targetObject != null)
            EditorUtility.SetDirty(so.targetObject);
    }

    private void SelectNode(string nodePath, LogicSentenceCategory category)
    {
        selectedNodePath = nodePath;
        selectedCategory = category;
        contextNodePath = nodePath;

        selectedConditionIndex = -1;
        selectedActionIndex = -1;
        selectedMotiveIndex = -1;

        if (category == LogicSentenceCategory.Condition && TryGetTopLevelIndex(nodePath, out int cIdx))
            selectedConditionIndex = cIdx;

        if (category == LogicSentenceCategory.Action && TryGetTopLevelIndex(nodePath, out int aIdx))
            selectedActionIndex = aIdx;

        if (category == LogicSentenceCategory.Motive && TryGetTopLevelIndex(nodePath, out int mIdx))
            selectedMotiveIndex = mIdx;
    }

    private bool TryGetTopLevelIndex(string nodePath, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(nodePath))
            return false;

        string[] parts = nodePath.Split('/');
        if (parts.Length < 2)
            return false;

        return int.TryParse(parts[1], out index);
    }

    private bool GetSectionExpanded(string key)
    {
        if (!sectionExpanded.TryGetValue(key, out bool expanded))
        {
            expanded = true;
            sectionExpanded[key] = true;
        }

        return expanded;
    }

    private bool GetNodeExpanded(string nodePath)
    {
        if (!nodeExpanded.TryGetValue(nodePath, out bool expanded))
        {
            expanded = true;
            nodeExpanded[nodePath] = true;
        }

        return expanded;
    }

    private int AddSentenceInstanceToList(SerializedProperty listProp, LogicSentenceInstance instance)
    {
        if (instance == null || listProp == null)
            return -1;

        int newIndex = listProp.arraySize;
        listProp.arraySize++;
        SerializedProperty newProp = listProp.GetArrayElementAtIndex(newIndex);
        WriteSentenceInstanceToProperty(instance, newProp);
        return newIndex;
    }

    private SerializedProperty GetRootProperty(SerializedProperty ruleProp)
    {
        if (ruleProp == null)
            return null;

        return ruleProp.FindPropertyRelative("sentenceRoot");
    }

    private SerializedProperty GetGroupProperty(SerializedProperty ruleProp, string groupFieldName)
    {
        SerializedProperty rootProp = GetRootProperty(ruleProp);
        if (rootProp == null)
            return null;

        return rootProp.FindPropertyRelative(groupFieldName);
    }

    private SerializedProperty GetGroupItemsProperty(SerializedProperty ruleProp, string groupFieldName)
    {
        SerializedProperty groupProp = GetGroupProperty(ruleProp, groupFieldName);
        if (groupProp == null)
            return null;

        return groupProp.FindPropertyRelative("items");
    }

    private SerializedProperty GetSectionListProperty(SerializedProperty ruleProp, string ownerPath, string fieldName)
    {
        if (!TryGetNodeProperty(ruleProp, ownerPath, out SerializedProperty ownerNodeProp, out _, out _, out _))
            return null;

        return ownerNodeProp.FindPropertyRelative(fieldName);
    }

    private SerializedProperty GetTargetListProperty(SerializedProperty ruleProp, string targetKey)
    {
        if (ruleProp == null || string.IsNullOrWhiteSpace(targetKey))
            return null;

        if (targetKey == "COND_ROOT")
            return GetGroupItemsProperty(ruleProp, "conditionGroup");

        if (targetKey == "ACT_ROOT")
            return GetGroupItemsProperty(ruleProp, "actionGroup");

        if (targetKey == "MOT_ROOT")
            return GetGroupItemsProperty(ruleProp, "motiveGroup");

        if (!targetKey.StartsWith("SECTION:"))
            return null;

        string[] parts = targetKey.Split(':');
        if (parts.Length < 4)
            return null;

        string ownerPath = parts[1];
        string fieldName = parts[2];

        return GetSectionListProperty(ruleProp, ownerPath, fieldName);
    }

    private void RemoveNodeByPath(SerializedProperty ruleProp, string nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath))
            return;

        if (!TryGetNodeProperty(ruleProp, nodePath, out _, out SerializedProperty ownerListProp, out int indexInOwner, out _))
            return;

        if (ownerListProp != null && indexInOwner >= 0 && indexInOwner < ownerListProp.arraySize)
            ownerListProp.DeleteArrayElementAtIndex(indexInOwner);
    }

    private bool TryGetNodeProperty(
        SerializedProperty ruleProp,
        string nodePath,
        out SerializedProperty nodeProp,
        out SerializedProperty ownerListProp,
        out int indexInOwner,
        out LogicSentenceCategory category)
    {
        nodeProp = null;
        ownerListProp = null;
        indexInOwner = -1;
        category = LogicSentenceCategory.Action;

        if (ruleProp == null || string.IsNullOrWhiteSpace(nodePath))
            return false;

        string[] parts = nodePath.Split('/');
        if (parts.Length < 2)
            return false;

        SerializedProperty currentList = null;
        int cursor = 0;

        if (parts[0] == "C")
        {
            currentList = GetGroupItemsProperty(ruleProp, "conditionGroup");
            category = LogicSentenceCategory.Condition;
            cursor = 1;
        }
        else if (parts[0] == "M")
        {
            currentList = GetGroupItemsProperty(ruleProp, "motiveGroup");
            category = LogicSentenceCategory.Motive;
            cursor = 1;
        }
        else if (parts[0] == "A")
        {
            currentList = GetGroupItemsProperty(ruleProp, "actionGroup");
            category = LogicSentenceCategory.Action;
            cursor = 1;
        }
        else
        {
            return false;
        }

        while (cursor < parts.Length)
        {
            if (!int.TryParse(parts[cursor], out int idx))
                return false;

            if (currentList == null || idx < 0 || idx >= currentList.arraySize)
                return false;

            ownerListProp = currentList;
            indexInOwner = idx;
            nodeProp = currentList.GetArrayElementAtIndex(idx);
            cursor++;

            if (cursor >= parts.Length)
                return true;

            string sectionField = parts[cursor];
            cursor++;

            if (sectionField != "conditionChildren" &&
                sectionField != "thenChildren" &&
                sectionField != "elseChildren" &&
                sectionField != "bodyChildren")
                return false;

            currentList = nodeProp.FindPropertyRelative(sectionField);
        }

        return nodeProp != null;
    }

    private LogicSentenceInstance ReadSentenceInstanceFromProperty(SerializedProperty prop)
    {
        if (prop == null)
            return null;

        LogicSentenceInstance instance = new LogicSentenceInstance
        {
            templateId = prop.FindPropertyRelative("templateId").stringValue,
            enabled = prop.FindPropertyRelative("enabled").boolValue
        };

        SerializedProperty assignmentsProp = prop.FindPropertyRelative("slotAssignments");
        if (assignmentsProp != null)
        {
            for (int i = 0; i < assignmentsProp.arraySize; i++)
            {
                SerializedProperty src = assignmentsProp.GetArrayElementAtIndex(i);
                LogicSlotAssignment assignment = new LogicSlotAssignment
                {
                    slotId = src.FindPropertyRelative("slotId").stringValue,
                    value = new LogicSlotValue()
                };

                SerializedProperty valueProp = src.FindPropertyRelative("value");
                assignment.value.valueType = (LogicSlotValueType)valueProp.FindPropertyRelative("valueType").enumValueIndex;
                assignment.value.sourceType = (LogicValueSourceType)valueProp.FindPropertyRelative("sourceType").enumValueIndex;
                assignment.value.boolValue = valueProp.FindPropertyRelative("boolValue").boolValue;
                assignment.value.intValue = valueProp.FindPropertyRelative("intValue").intValue;
                assignment.value.intValueMax = valueProp.FindPropertyRelative("intValueMax")?.intValue ?? 0;
                assignment.value.floatValue = valueProp.FindPropertyRelative("floatValue").floatValue;
                assignment.value.floatValueMax = valueProp.FindPropertyRelative("floatValueMax")?.floatValue ?? 0f;
                assignment.value.stringValue = valueProp.FindPropertyRelative("stringValue").stringValue;
                assignment.value.enumValue = valueProp.FindPropertyRelative("enumValue").stringValue;
                assignment.value.variableKey = valueProp.FindPropertyRelative("variableKey").stringValue;
                assignment.value.contextKey = valueProp.FindPropertyRelative("contextKey").stringValue;
                assignment.value.assetReference = valueProp.FindPropertyRelative("assetReference").objectReferenceValue;
                assignment.value.sceneObjectId = valueProp.FindPropertyRelative("sceneObjectId").stringValue;
                assignment.value.sceneObjectName = valueProp.FindPropertyRelative("sceneObjectName").stringValue;

                instance.slotAssignments.Add(assignment);
            }
        }

        ReadSentenceChildList(prop, "conditionChildren", instance.conditionChildren);
        ReadSentenceChildList(prop, "thenChildren", instance.thenChildren);
        ReadSentenceChildList(prop, "elseChildren", instance.elseChildren);
        ReadSentenceChildList(prop, "bodyChildren", instance.bodyChildren);

        return instance;
    }

    private void ReadSentenceChildList(SerializedProperty prop, string fieldName, List<LogicSentenceInstance> target)
    {
        SerializedProperty listProp = prop.FindPropertyRelative(fieldName);
        if (listProp == null || target == null)
            return;

        for (int i = 0; i < listProp.arraySize; i++)
        {
            LogicSentenceInstance child = ReadSentenceInstanceFromProperty(listProp.GetArrayElementAtIndex(i));
            if (child != null)
                target.Add(child);
        }
    }

    private LogicSentenceInstance CloneSentenceInstance(LogicSentenceInstance src)
    {
        if (src == null)
            return null;

        LogicSentenceInstance cloned = new LogicSentenceInstance
        {
            templateId = src.templateId,
            enabled = src.enabled
        };

        for (int i = 0; i < src.slotAssignments.Count; i++)
        {
            LogicSlotAssignment s = src.slotAssignments[i];
            if (s == null)
                continue;

            LogicSlotAssignment d = new LogicSlotAssignment
            {
                slotId = s.slotId,
                value = new LogicSlotValue
                {
                    valueType = s.value.valueType,
                    sourceType = s.value.sourceType,
                    boolValue = s.value.boolValue,
                    intValue = s.value.intValue,
                    floatValue = s.value.floatValue,
                    stringValue = s.value.stringValue,
                    enumValue = s.value.enumValue,
                    variableKey = s.value.variableKey,
                    contextKey = s.value.contextKey,
                    assetReference = s.value.assetReference,
                    sceneObjectId = s.value.sceneObjectId,
                    sceneObjectName = s.value.sceneObjectName
                }
            };

            cloned.slotAssignments.Add(d);
        }

        CloneSentenceChildList(src.conditionChildren, cloned.conditionChildren);
        CloneSentenceChildList(src.thenChildren, cloned.thenChildren);
        CloneSentenceChildList(src.elseChildren, cloned.elseChildren);
        CloneSentenceChildList(src.bodyChildren, cloned.bodyChildren);

        return cloned;
    }

    private void CloneSentenceChildList(List<LogicSentenceInstance> src, List<LogicSentenceInstance> dst)
    {
        if (src == null || dst == null)
            return;

        for (int i = 0; i < src.Count; i++)
        {
            LogicSentenceInstance childClone = CloneSentenceInstance(src[i]);
            if (childClone != null)
                dst.Add(childClone);
        }
    }

    private void WriteSentenceInstanceToProperty(LogicSentenceInstance instance, SerializedProperty prop)
    {
        if (instance == null || prop == null)
            return;

        prop.FindPropertyRelative("templateId").stringValue = instance.templateId;
        prop.FindPropertyRelative("enabled").boolValue = instance.enabled;

        SerializedProperty assignmentsProp = prop.FindPropertyRelative("slotAssignments");
        if (assignmentsProp != null)
        {
            assignmentsProp.arraySize = instance.slotAssignments.Count;
            for (int i = 0; i < instance.slotAssignments.Count; i++)
            {
                LogicSlotAssignment src = instance.slotAssignments[i];
                SerializedProperty dst = assignmentsProp.GetArrayElementAtIndex(i);

                dst.FindPropertyRelative("slotId").stringValue = src.slotId;

                SerializedProperty valueProp = dst.FindPropertyRelative("value");
                valueProp.FindPropertyRelative("valueType").enumValueIndex = (int)src.value.valueType;
                valueProp.FindPropertyRelative("sourceType").enumValueIndex = (int)src.value.sourceType;
                valueProp.FindPropertyRelative("boolValue").boolValue = src.value.boolValue;
                valueProp.FindPropertyRelative("intValue").intValue = src.value.intValue;
                SerializedProperty intMaxP = valueProp.FindPropertyRelative("intValueMax");
                if (intMaxP != null) intMaxP.intValue = src.value.intValueMax;
                valueProp.FindPropertyRelative("floatValue").floatValue = src.value.floatValue;
                SerializedProperty floatMaxP = valueProp.FindPropertyRelative("floatValueMax");
                if (floatMaxP != null) floatMaxP.floatValue = src.value.floatValueMax;
                valueProp.FindPropertyRelative("stringValue").stringValue = src.value.stringValue ?? "";
                valueProp.FindPropertyRelative("enumValue").stringValue = src.value.enumValue ?? "";
                valueProp.FindPropertyRelative("variableKey").stringValue = src.value.variableKey ?? "";
                valueProp.FindPropertyRelative("contextKey").stringValue = src.value.contextKey ?? "";
                valueProp.FindPropertyRelative("assetReference").objectReferenceValue = src.value.assetReference;
                valueProp.FindPropertyRelative("sceneObjectId").stringValue = src.value.sceneObjectId ?? "";
                valueProp.FindPropertyRelative("sceneObjectName").stringValue = src.value.sceneObjectName ?? "";
            }
        }

        WriteSentenceChildList(instance.conditionChildren, prop, "conditionChildren");
        WriteSentenceChildList(instance.thenChildren, prop, "thenChildren");
        WriteSentenceChildList(instance.elseChildren, prop, "elseChildren");
        WriteSentenceChildList(instance.bodyChildren, prop, "bodyChildren");
    }

    private void WriteSentenceChildList(List<LogicSentenceInstance> src, SerializedProperty prop, string fieldName)
    {
        SerializedProperty listProp = prop.FindPropertyRelative(fieldName);
        if (listProp == null || src == null)
            return;

        listProp.arraySize = src.Count;
        for (int i = 0; i < src.Count; i++)
            WriteSentenceInstanceToProperty(src[i], listProp.GetArrayElementAtIndex(i));
    }

    private SerializedProperty FindAssignmentProperty(SerializedProperty assignmentsProp, string slotId)
    {
        if (assignmentsProp == null || string.IsNullOrWhiteSpace(slotId))
            return null;

        for (int i = 0; i < assignmentsProp.arraySize; i++)
        {
            SerializedProperty item = assignmentsProp.GetArrayElementAtIndex(i);
            SerializedProperty slotIdProp = item.FindPropertyRelative("slotId");
            if (slotIdProp != null && slotIdProp.stringValue == slotId)
                return item;
        }

        return null;
    }

    private string GetSlotDisplayTextFromProperty(SerializedProperty assignmentProp, LogicSentenceTemplate.SlotDefinition slotDef)
    {
        string fixedText = LogicSentenceContextUtility.GetFixedSlotDisplayText(editorContext, slotDef);
        if (!string.IsNullOrEmpty(fixedText))
            return fixedText;

        if (slotDef == null)
            return "<未指定>";

        if (assignmentProp == null)
            return slotDef.required ? "<未指定>" : $"<{slotDef.displayName}>";

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null)
            return slotDef.required ? "<未指定>" : $"<{slotDef.displayName}>";

        LogicSlotValue value = ReadLogicSlotValueFromProperty(valueProp);

        // 旧数据 enumDisplayName 为空时，从模板 enumOptions 兜底查找中文显示名
        if (value.valueType == LogicSlotValueType.Enum
            && string.IsNullOrWhiteSpace(value.enumDisplayName)
            && !string.IsNullOrWhiteSpace(value.enumValue)
            && slotDef?.enumOptions != null)
        {
            foreach (var opt in slotDef.enumOptions)
            {
                if (opt != null && opt.value == value.enumValue && !string.IsNullOrWhiteSpace(opt.displayName))
                {
                    value.enumDisplayName = opt.displayName;
                    break;
                }
            }
        }

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(value.valueType);
        if (handler == null)
            return "<未注册处理器>";

        return handler.GetDisplayText(value);
    }

    private bool IsAssignmentValidFromProperty(SerializedProperty assignmentProp, LogicSentenceTemplate.SlotDefinition slotDef)
    {
        if (LogicSentenceContextUtility.IsFixedSelfUnitSlot(editorContext, slotDef) ||
            LogicSentenceContextUtility.IsCurrentTargetUnitSlot(editorContext, slotDef))
            return true;

        if (slotDef == null)
            return false;

        if (assignmentProp == null)
            return !slotDef.required;

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null)
            return !slotDef.required;

        LogicSlotValue value = ReadLogicSlotValueFromProperty(valueProp);
        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(value.valueType);
        if (handler == null)
            return false;

        return handler.IsValid(slotDef, value);
    }

    private bool IsAssignmentEmpty(SerializedProperty assignmentProp)
    {
        if (assignmentProp == null)
            return true;

        SerializedProperty valueProp = assignmentProp.FindPropertyRelative("value");
        if (valueProp == null)
            return true;

        string stringValue = valueProp.FindPropertyRelative("stringValue")?.stringValue ?? "";
        string enumValue = valueProp.FindPropertyRelative("enumValue")?.stringValue ?? "";
        string variableKey = valueProp.FindPropertyRelative("variableKey")?.stringValue ?? "";
        string contextKey = valueProp.FindPropertyRelative("contextKey")?.stringValue ?? "";
        string sceneObjectId = valueProp.FindPropertyRelative("sceneObjectId")?.stringValue ?? "";

        bool boolValue = valueProp.FindPropertyRelative("boolValue") != null && valueProp.FindPropertyRelative("boolValue").boolValue;
        int intValue = valueProp.FindPropertyRelative("intValue") != null ? valueProp.FindPropertyRelative("intValue").intValue : 0;
        float floatValue = valueProp.FindPropertyRelative("floatValue") != null ? valueProp.FindPropertyRelative("floatValue").floatValue : 0f;
        UnityEngine.Object assetRef = valueProp.FindPropertyRelative("assetReference") != null ? valueProp.FindPropertyRelative("assetReference").objectReferenceValue : null;

        return
            !boolValue &&
            intValue == 0 &&
            Mathf.Approximately(floatValue, 0f) &&
            string.IsNullOrWhiteSpace(stringValue) &&
            string.IsNullOrWhiteSpace(enumValue) &&
            string.IsNullOrWhiteSpace(variableKey) &&
            string.IsNullOrWhiteSpace(contextKey) &&
            string.IsNullOrWhiteSpace(sceneObjectId) &&
            assetRef == null;
    }

    private LogicSlotValue ReadLogicSlotValueFromProperty(SerializedProperty valueProp)
    {
        return new LogicSlotValue
        {
            valueType = (LogicSlotValueType)valueProp.FindPropertyRelative("valueType").enumValueIndex,
            sourceType = (LogicValueSourceType)valueProp.FindPropertyRelative("sourceType").enumValueIndex,
            boolValue = valueProp.FindPropertyRelative("boolValue").boolValue,
            intValue = valueProp.FindPropertyRelative("intValue").intValue,
            intValueMax = valueProp.FindPropertyRelative("intValueMax")?.intValue ?? 0,
            floatValue = valueProp.FindPropertyRelative("floatValue").floatValue,
            floatValueMax = valueProp.FindPropertyRelative("floatValueMax")?.floatValue ?? 0f,
            stringValue = valueProp.FindPropertyRelative("stringValue").stringValue,
            enumValue = valueProp.FindPropertyRelative("enumValue").stringValue,
            enumDisplayName = valueProp.FindPropertyRelative("enumDisplayName")?.stringValue ?? "",
            variableKey = valueProp.FindPropertyRelative("variableKey").stringValue,
            contextKey = valueProp.FindPropertyRelative("contextKey").stringValue,
            assetReference = valueProp.FindPropertyRelative("assetReference").objectReferenceValue,
            sceneObjectId = valueProp.FindPropertyRelative("sceneObjectId").stringValue,
            sceneObjectName = valueProp.FindPropertyRelative("sceneObjectName").stringValue
        };
    }

    private LogicSentenceTemplate.SlotDefinition GetSlotDefinition(LogicSentenceTemplate template, string slotId)
    {
        if (template == null || template.slots == null)
            return null;

        for (int i = 0; i < template.slots.Count; i++)
        {
            if (template.slots[i].slotId == slotId)
                return template.slots[i];
        }

        return null;
    }
}
