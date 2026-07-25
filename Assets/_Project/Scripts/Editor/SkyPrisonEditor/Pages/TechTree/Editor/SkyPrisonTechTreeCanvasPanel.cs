using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonTechTreeCanvasPanel
{
    private const float NodeAddButtonSize = 22f;
    private const float NodeAddButtonDistance = 26f;

    private readonly Dictionary<int, Rect> screenNodeRects = new Dictionary<int, Rect>();
    private readonly SkyPrisonTechTreeCanvasSurface surface = new SkyPrisonTechTreeCanvasSurface();

    private bool requestFocusToSelectedNode = true;
    private bool initializedView = false;

    private bool simulationMode = false;
    private readonly Dictionary<int, int> simulatedLevels = new Dictionary<int, int>();

    private Rect overviewRect = new Rect(24f, 24f, 300f, 280f);
    private bool draggingOverview = false;
    private Vector2 overviewDragOffset;
    private bool overviewCollapsed = false;

    // 框选
    private bool isBoxSelecting = false;
    private Vector2 boxSelectStart;
    private Vector2 boxSelectCurrent;

    public void RequestFocus()
    {
        requestFocusToSelectedNode = true;
    }

    public void ResetView()
    {
        surface.ResetView();
        requestFocusToSelectedNode = true;
        initializedView = false;
        draggingOverview = false;
        isBoxSelecting = false;
    }

    public void DrawSimulationToolbar()
    {
        GUILayout.BeginHorizontal(GUILayout.Width(190f));

        GUILayout.Label("模拟模式", GUILayout.Width(54f));

        Rect toggleRect = GUILayoutUtility.GetRect(42f, 20f, GUILayout.Width(42f), GUILayout.Height(20f));
        bool newMode = DrawToggleSwitch(toggleRect, simulationMode);
        if (newMode != simulationMode)
        {
            simulationMode = newMode;

            if (simulationMode)
                isBoxSelecting = false;

            GUI.changed = true;
        }

        using (new EditorGUI.DisabledScope(!simulationMode))
        {
            if (GUILayout.Button("重置模拟", EditorStyles.toolbarButton, GUILayout.Width(80f), GUILayout.Height(24f)))
            {
                simulatedLevels.Clear();
                GUI.changed = true;
            }
        }

        GUILayout.EndHorizontal();
    }

    public void Draw(
        Rect rect,
        SerializedObject selectedSO,
        int selectedNodeIndex,
        HashSet<int> selectedNodeIndices,
        Action<int> selectSingleNode,
        Action<int> addMultiSelection,
        Action<int> addChildToNode,
        Action<int> deleteSingleNode,
        Action deleteSelection)
    {
        if (selectedSO == null)
            return;

        HandleKeyboardDelete(selectedNodeIndices, deleteSelection);

        surface.Begin(rect);

        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        int nodeCount = nodesProp != null ? nodesProp.arraySize : 0;

        EnsureSimulationState(nodeCount, nodesProp);

        if (nodeCount == 0)
        {
            DrawEmptyCanvas(rect, addChildToNode);
            surface.End(rect);
            return;
        }

        SerializedProperty layoutModeProp = selectedSO.FindProperty("layoutMode");
        TechTreeGraphAsset.LayoutMode mode = layoutModeProp != null
            ? (TechTreeGraphAsset.LayoutMode)layoutModeProp.enumValueIndex
            : TechTreeGraphAsset.LayoutMode.Vertical;

        Dictionary<int, Rect> worldLayout = SkyPrisonTechTreeLayoutUtility.BuildNodeLayout(
            nodeCount,
            i => nodesProp.GetArrayElementAtIndex(i).FindPropertyRelative("primaryParentIndex").intValue,
            mode
        );

        Rect worldBounds = SkyPrisonTechTreeLayoutUtility.CalculateBoundsRect(worldLayout);

        if (!initializedView)
        {
            surface.FocusWorldPoint(worldBounds.center);
            initializedView = true;
        }
        else if (requestFocusToSelectedNode)
        {
            if (selectedNodeIndex >= 0 && worldLayout.TryGetValue(selectedNodeIndex, out Rect focusRect))
                surface.FocusWorldPoint(focusRect.center);
            else
                surface.FocusWorldPoint(worldBounds.center);

            requestFocusToSelectedNode = false;
        }

        screenNodeRects.Clear();

        GUI.BeginGroup(rect);
        Vector2 canvasSize = rect.size;

        foreach (var pair in worldLayout)
            screenNodeRects[pair.Key] = surface.WorldToCanvasRect(pair.Value, canvasSize);

        if (!simulationMode)
        {
            HandleBoxSelection(
                canvasSize,
                selectedNodeIndices,
                selectSingleNode,
                addMultiSelection,
                screenNodeRects
            );
        }

        DrawConnections(nodesProp, screenNodeRects, mode);
        DrawNodes(
            nodesProp,
            screenNodeRects,
            selectedNodeIndex,
            selectedNodeIndices,
            selectSingleNode,
            addMultiSelection,
            addChildToNode,
            deleteSingleNode
        );

        if (!simulationMode)
            DrawNodeAddButtonOverlay(nodesProp, screenNodeRects, selectedNodeIndex, mode, addChildToNode);

        if (simulationMode)
            DrawSimulationOverview(rect.size, nodesProp);

        if (!simulationMode && isBoxSelecting)
            DrawSelectionBox();

        GUI.EndGroup();

        surface.End(rect);
    }

    private bool DrawToggleSwitch(Rect rect, bool value)
    {
        Event e = Event.current;
        bool clicked = e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0;

        if (clicked)
        {
            value = !value;
            e.Use();
        }

        Color bg = value
            ? new Color(0.23f, 0.71f, 0.95f, 1f)
            : new Color(0.28f, 0.28f, 0.30f, 1f);

        EditorGUI.DrawRect(rect, bg);

        float knobSize = rect.height - 4f;
        float knobX = value ? rect.xMax - knobSize - 2f : rect.x + 2f;
        Rect knobRect = new Rect(knobX, rect.y + 2f, knobSize, knobSize);
        EditorGUI.DrawRect(knobRect, Color.white);

        return value;
    }

    private void EnsureSimulationState(int nodeCount, SerializedProperty nodesProp)
    {
        List<int> toRemove = simulatedLevels.Keys.Where(k => k < 0 || k >= nodeCount).ToList();
        for (int i = 0; i < toRemove.Count; i++)
            simulatedLevels.Remove(toRemove[i]);

        for (int i = 0; i < nodeCount; i++)
        {
            if (!simulatedLevels.ContainsKey(i))
                simulatedLevels[i] = 0;

            int maxLv = Mathf.Max(1, nodesProp.GetArrayElementAtIndex(i).FindPropertyRelative("maxLevel").intValue);
            simulatedLevels[i] = Mathf.Clamp(simulatedLevels[i], 0, maxLv);
        }
    }

    private void HandleKeyboardDelete(HashSet<int> selectedNodeIndices, Action deleteSelection)
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
        {
            if (selectedNodeIndices != null && selectedNodeIndices.Count > 0)
            {
                deleteSelection?.Invoke();
                e.Use();
            }
        }
    }

    private void HandleBoxSelection(
        Vector2 canvasSize,
        HashSet<int> selectedNodeIndices,
        Action<int> selectSingleNode,
        Action<int> addMultiSelection,
        Dictionary<int, Rect> nodeRects)
    {
        Event e = Event.current;
        if (e == null)
            return;

        bool pointerOnNode = nodeRects.Values.Any(r => r.Contains(e.mousePosition));

        if (e.type == EventType.MouseDown && e.button == 0 && !pointerOnNode)
        {
            isBoxSelecting = true;
            boxSelectStart = e.mousePosition;
            boxSelectCurrent = e.mousePosition;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && isBoxSelecting)
        {
            boxSelectCurrent = ClampToCanvas(e.mousePosition, canvasSize);
            GUI.changed = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && isBoxSelecting)
        {
            boxSelectCurrent = ClampToCanvas(e.mousePosition, canvasSize);

            Rect selectionRect = GetNormalizedRect(boxSelectStart, boxSelectCurrent);
            bool append = e.shift;

            List<int> hits = nodeRects
                .Where(kv => selectionRect.Overlaps(kv.Value, true))
                .Select(kv => kv.Key)
                .OrderBy(i => i)
                .ToList();

            if (hits.Count > 0)
            {
                if (!append)
                {
                    selectSingleNode?.Invoke(hits[0]);
                    for (int i = 1; i < hits.Count; i++)
                        addMultiSelection?.Invoke(hits[i]);
                }
                else
                {
                    for (int i = 0; i < hits.Count; i++)
                        addMultiSelection?.Invoke(hits[i]);
                }
            }
            else if (!append)
            {
                selectSingleNode?.Invoke(-1);
            }

            isBoxSelecting = false;
            GUI.changed = true;
            e.Use();
        }
    }

    private void DrawSelectionBox()
    {
        Rect r = GetNormalizedRect(boxSelectStart, boxSelectCurrent);
        EditorGUI.DrawRect(r, new Color(0.25f, 0.56f, 1f, 0.16f));
        DrawBorder(r, new Color(0.42f, 0.68f, 1f, 0.95f), 1f);
    }

    private Rect GetNormalizedRect(Vector2 a, Vector2 b)
    {
        float xMin = Mathf.Min(a.x, b.x);
        float xMax = Mathf.Max(a.x, b.x);
        float yMin = Mathf.Min(a.y, b.y);
        float yMax = Mathf.Max(a.y, b.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private Vector2 ClampToCanvas(Vector2 pos, Vector2 canvasSize)
    {
        return new Vector2(
            Mathf.Clamp(pos.x, 0f, canvasSize.x),
            Mathf.Clamp(pos.y, 0f, canvasSize.y)
        );
    }

    private void DrawEmptyCanvas(Rect rect, Action<int> addChildToNode)
    {
        GUI.BeginGroup(rect);

        Rect buttonRect = new Rect(
            rect.width * 0.5f - 90f,
            rect.height * 0.5f - 24f,
            180f,
            48f
        );

        if (GUI.Button(buttonRect, "+ 添加根节点"))
            addChildToNode?.Invoke(-1);

        GUI.EndGroup();
    }

    private void DrawConnections(SerializedProperty nodesProp, Dictionary<int, Rect> layout, TechTreeGraphAsset.LayoutMode mode)
    {
        Handles.BeginGUI();

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);
            int parentIndex = node.FindPropertyRelative("primaryParentIndex").intValue;

            if (parentIndex < 0 || !layout.ContainsKey(parentIndex) || !layout.ContainsKey(i))
                continue;

            bool childActivated = simulationMode && simulatedLevels.ContainsKey(i) && simulatedLevels[i] > 0;
            Handles.color = childActivated
                ? new Color(0.95f, 0.95f, 0.95f, 1f)
                : new Color(0.45f, 0.45f, 0.48f, 1f);

            Rect parentRect = layout[parentIndex];
            Rect childRect = layout[i];

            switch (mode)
            {
                case TechTreeGraphAsset.LayoutMode.Horizontal:
                    DrawOrthogonalHorizontalEdge(parentRect, childRect);
                    break;
                case TechTreeGraphAsset.LayoutMode.Vertical:
                    DrawOrthogonalVerticalEdge(parentRect, childRect);
                    break;
                default:
                    DrawStraightEdgeConnection(parentRect, childRect);
                    break;
            }
        }

        Handles.EndGUI();
    }

    private void DrawNodes(
        SerializedProperty nodesProp,
        Dictionary<int, Rect> layout,
        int selectedNodeIndex,
        HashSet<int> selectedNodeIndices,
        Action<int> selectSingleNode,
        Action<int> addMultiSelection,
        Action<int> addChildToNode,
        Action<int> deleteSingleNode)
    {
        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            if (!layout.TryGetValue(i, out Rect rect))
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);

            bool isRoot = node.FindPropertyRelative("primaryParentIndex").intValue < 0;
            bool isPrimarySelected = i == selectedNodeIndex;
            bool isSecondarySelected = selectedNodeIndices != null && selectedNodeIndices.Contains(i) && !isPrimarySelected;

            int simulatedLevel = simulatedLevels.ContainsKey(i) ? simulatedLevels[i] : 0;
            int maxLevel = Mathf.Max(1, node.FindPropertyRelative("maxLevel").intValue);
            bool activated = simulationMode && simulatedLevel > 0;

            DrawNodeCard(rect, node, isRoot, isPrimarySelected, isSecondarySelected, activated, simulatedLevel, maxLevel);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                if (simulationMode)
                {
                    if (HandleSimulationClick(i, rect, node))
                    {
                        e.Use();
                        continue;
                    }
                }

                if (e.button == 0)
                {
                    if (e.shift)
                        addMultiSelection?.Invoke(i);
                    else
                        selectSingleNode?.Invoke(i);

                    e.Use();
                }
                else if (e.button == 1)
                {
                    selectSingleNode?.Invoke(i);
                    ShowNodeContextMenu(i, addChildToNode, deleteSingleNode);
                    e.Use();
                }
            }
        }
    }

    private bool HandleSimulationClick(int nodeIndex, Rect rect, SerializedProperty node)
    {
        float z = surface.Zoom;
        if (z < 1.16f)
            return false;

        Rect minusRect;
        Rect valueRect;
        Rect plusRect;
        Rect lvLabelRect;
        GetSimulationControlRects(rect, out lvLabelRect, out minusRect, out valueRect, out plusRect);

        int currentLevel = simulatedLevels[nodeIndex];
        int maxLevel = Mathf.Max(1, node.FindPropertyRelative("maxLevel").intValue);

        Event e = Event.current;

        if (minusRect.Contains(e.mousePosition))
        {
            if (currentLevel > 0)
            {
                simulatedLevels[nodeIndex] = currentLevel - 1;
                GUI.changed = true;
            }
            return true;
        }

        if (plusRect.Contains(e.mousePosition))
        {
            if (CanIncreaseSimulatedLevel(node, currentLevel, maxLevel))
            {
                simulatedLevels[nodeIndex] = currentLevel + 1;
                GUI.changed = true;
            }
            return true;
        }

        if (valueRect.Contains(e.mousePosition) || lvLabelRect.Contains(e.mousePosition))
            return true;

        return false;
    }

    private bool CanIncreaseSimulatedLevel(SerializedProperty node, int currentLevel, int maxLevel)
    {
        if (currentLevel >= maxLevel)
            return false;

        int parentIndex = node.FindPropertyRelative("primaryParentIndex").intValue;
        if (parentIndex < 0)
            return true;

        return simulatedLevels.ContainsKey(parentIndex) && simulatedLevels[parentIndex] > 0;
    }

    private void ShowNodeContextMenu(int nodeIndex, Action<int> addChildToNode, Action<int> deleteSingleNode)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("添加子节点"), false, () => addChildToNode?.Invoke(nodeIndex));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除节点"), false, () => deleteSingleNode?.Invoke(nodeIndex));
        menu.ShowAsContext();
    }

    private void DrawNodeAddButtonOverlay(
        SerializedProperty nodesProp,
        Dictionary<int, Rect> layout,
        int selectedNodeIndex,
        TechTreeGraphAsset.LayoutMode mode,
        Action<int> addChildToNode)
    {
        if (selectedNodeIndex < 0 || !layout.TryGetValue(selectedNodeIndex, out Rect selectedRect))
            return;

        float z = surface.Zoom;
        if (z < 0.78f)
            return;

        Vector2 direction = GetNodeGrowthDirection(selectedRect.center, mode);
        Vector2 pos = selectedRect.center + direction * (Mathf.Max(selectedRect.width, selectedRect.height) * 0.5f + NodeAddButtonDistance * z);

        Rect addRect = new Rect(
            pos.x - NodeAddButtonSize * 0.5f,
            pos.y - NodeAddButtonSize * 0.5f,
            NodeAddButtonSize,
            NodeAddButtonSize
        );

        if (GUI.Button(addRect, new GUIContent("+", "添加子节点")))
        {
            addChildToNode?.Invoke(selectedNodeIndex);
            GUI.changed = true;
        }
    }

    private Vector2 GetNodeGrowthDirection(Vector2 nodeCenter, TechTreeGraphAsset.LayoutMode mode)
    {
        switch (mode)
        {
            case TechTreeGraphAsset.LayoutMode.Horizontal:
                return Vector2.right;
            case TechTreeGraphAsset.LayoutMode.RadialOutward:
                {
                    Vector2 dir = nodeCenter.normalized;
                    return dir.sqrMagnitude < 0.0001f ? Vector2.down : dir;
                }
            case TechTreeGraphAsset.LayoutMode.RadialInward:
                {
                    Vector2 dir = (-nodeCenter).normalized;
                    return dir.sqrMagnitude < 0.0001f ? Vector2.up : dir;
                }
            default:
                return Vector2.down;
        }
    }

    private void DrawNodeCard(
        Rect rect,
        SerializedProperty node,
        bool isRoot,
        bool primarySelected,
        bool secondarySelected,
        bool activated,
        int simulatedLevel,
        int maxLevel)
    {
        float z = surface.Zoom;

        bool showText = z >= 0.58f;
        bool showTitle = z >= 0.72f;
        bool showDetails = z >= 0.88f;
        bool showSimulationControls = simulationMode && z >= 1.16f;
        bool showRootText = z >= 0.86f;
        bool showRootTag = z >= 0.68f;

        bool enabled = node.FindPropertyRelative("enabled").boolValue;
        bool useCustomColor = node.FindPropertyRelative("useCustomColor").boolValue;
        Color customColor = node.FindPropertyRelative("customColor").colorValue;

        Color bg;
        if (simulationMode)
            bg = activated ? new Color(0.18f, 0.22f, 0.28f, 0.95f) : new Color(0.15f, 0.15f, 0.16f, 0.95f);
        else
            bg = new Color(0.16f, 0.16f, 0.17f, 0.95f);

        if (!enabled)
            bg = new Color(bg.r * 0.9f, bg.g * 0.9f, bg.b * 0.9f, bg.a);

        EditorGUI.DrawRect(rect, bg);

        if (useCustomColor)
        {
            float stripWidth = Mathf.Clamp(8f * z, 5f, 10f);
            Rect stripRect = new Rect(rect.x + 4f, rect.y + 6f, stripWidth, rect.height - 12f);
            EditorGUI.DrawRect(stripRect, customColor);
        }

        Color borderColor = new Color(1f, 1f, 1f, 0.10f);

        if (isRoot)
            borderColor = new Color(0.95f, 0.95f, 0.95f, 0.95f);

        if (activated)
            borderColor = new Color(0.85f, 0.93f, 1f, 0.92f);

        if (secondarySelected)
            borderColor = new Color(0.42f, 0.64f, 1f, 0.76f);

        if (primarySelected)
            borderColor = new Color(0.52f, 0.72f, 1f, 1f);

        DrawBorder(rect, borderColor, 1f);

        if (isRoot && showRootTag)
            DrawRootTagOutside(rect, z, showRootText);

        if (!showText)
            return;

        SerializedProperty iconProp = node.FindPropertyRelative("icon");
        SerializedProperty nameProp = node.FindPropertyRelative("nodeName");
        Sprite sprite = iconProp != null ? iconProp.objectReferenceValue as Sprite : null;
        Texture2D iconTex = sprite != null ? sprite.texture : null;

        float iconSize = Mathf.Lerp(22f, 68f, Mathf.InverseLerp(0.58f, 1.35f, z));
        float iconX = rect.x + 14f;
        float iconY = rect.y + (showTitle ? 20f : 12f);
        Rect iconRect = new Rect(iconX, iconY, iconSize, iconSize);

        if (iconTex != null)
            GUI.DrawTexture(iconRect, iconTex, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.08f));

        if (showTitle)
        {
            string nodeName = !string.IsNullOrWhiteSpace(nameProp.stringValue) ? nameProp.stringValue : "未命名节点";
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(14f * z), 11, 22),
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = enabled ? Color.white : new Color(0.82f, 0.82f, 0.82f) }
            };

            float titleX = iconRect.xMax + 12f;
            float titleY = rect.y + 18f;
            float titleW = rect.width - (titleX - rect.x) - 14f;
            GUI.Label(new Rect(titleX, titleY, titleW, 30f), nodeName, titleStyle);
        }

        if (showDetails)
        {
            GUIStyle miniStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(10f * z), 8, 12),
                normal = { textColor = activated ? Color.white : new Color(0.80f, 0.80f, 0.82f) }
            };

            GUI.Label(
                new Rect(rect.x + 12f, rect.yMax - 24f, 86f, 16f),
                $"Max Lv.{maxLevel}",
                miniStyle
            );
        }

        if (showSimulationControls)
        {
            Rect minusRect;
            Rect valueRect;
            Rect plusRect;
            Rect lvLabelRect;
            GetSimulationControlRects(rect, out lvLabelRect, out minusRect, out valueRect, out plusRect);

            GUIStyle lvStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(10f * z), 8, 11),
                normal = { textColor = Color.white }
            };

            GUI.Label(lvLabelRect, "Lv", lvStyle);

            bool canPlus = CanIncreaseSimulatedLevel(node, simulatedLevel, maxLevel);
            bool canMinus = simulatedLevel > 0;

            DrawMiniControlButton(minusRect, "-", canMinus);

            EditorGUI.DrawRect(valueRect, new Color(1f, 1f, 1f, 0.05f));
            GUI.Label(valueRect, simulatedLevel.ToString(), GetCenteredMiniWhite());

            DrawMiniControlButton(plusRect, "+", canPlus);
        }
    }

    private void DrawMiniControlButton(Rect rect, string text, bool enabled)
    {
        Color bg = enabled
            ? new Color(1f, 1f, 1f, 0.08f)
            : new Color(1f, 1f, 1f, 0.03f);

        Color textColor = enabled
            ? new Color(0.92f, 0.92f, 0.94f, 1f)
            : new Color(0.45f, 0.45f, 0.48f, 1f);

        EditorGUI.DrawRect(rect, bg);

        GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = textColor }
        };

        GUI.Label(rect, text, style);
    }

    private void GetSimulationControlRects(Rect rect, out Rect lvLabelRect, out Rect minusRect, out Rect valueRect, out Rect plusRect)
    {
        float baseY = rect.yMax - 24f;

        plusRect = new Rect(rect.xMax - 28f, baseY, 20f, 18f);
        valueRect = new Rect(plusRect.x - 42f, baseY, 38f, 18f);
        minusRect = new Rect(valueRect.x - 24f, baseY, 20f, 18f);
        lvLabelRect = new Rect(minusRect.x - 22f, baseY + 1f, 16f, 16f);
    }

    private void DrawRootTagOutside(Rect rect, float zoom, bool showText)
    {
        float t = Mathf.InverseLerp(0.68f, 1.25f, zoom);
        float tagH = Mathf.Lerp(9f, 16f, t);
        float tagW = showText ? Mathf.Lerp(24f, 56f, t) : 25f;

        Rect tagRect = new Rect(rect.x + 8f, rect.y - tagH + 1f, tagW, tagH);
        EditorGUI.DrawRect(tagRect, new Color(0.94f, 0.39f, 0.64f, 1f));

        if (showText)
        {
            GUIStyle tagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                fontSize = Mathf.Clamp(Mathf.RoundToInt(7f + t * 2f), 7, 9)
            };
            GUI.Label(tagRect, "ROOT", tagStyle);
        }
    }

    private void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private GUIStyle GetCenteredMiniWhite()
    {
        return new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
    }

    private void DrawOrthogonalVerticalEdge(Rect parentRect, Rect childRect)
    {
        Vector3 start = new Vector3(parentRect.center.x, parentRect.yMax, 0f);
        Vector3 end = new Vector3(childRect.center.x, childRect.yMin, 0f);
        float midY = (start.y + end.y) * 0.5f;

        Handles.DrawLine(start, new Vector3(start.x, midY, 0f));
        Handles.DrawLine(new Vector3(start.x, midY, 0f), new Vector3(end.x, midY, 0f));
        Handles.DrawLine(new Vector3(end.x, midY, 0f), end);
    }

    private void DrawOrthogonalHorizontalEdge(Rect parentRect, Rect childRect)
    {
        Vector3 start = new Vector3(parentRect.xMax, parentRect.center.y, 0f);
        Vector3 end = new Vector3(childRect.xMin, childRect.center.y, 0f);
        float midX = (start.x + end.x) * 0.5f;

        Handles.DrawLine(start, new Vector3(midX, start.y, 0f));
        Handles.DrawLine(new Vector3(midX, start.y, 0f), new Vector3(midX, end.y, 0f));
        Handles.DrawLine(new Vector3(midX, end.y, 0f), end);
    }

    private void DrawStraightEdgeConnection(Rect parentRect, Rect childRect)
    {
        Vector2 startCenter = parentRect.center;
        Vector2 endCenter = childRect.center;
        Vector2 dir = (endCenter - startCenter).normalized;
        if (dir.sqrMagnitude < 0.0001f)
            return;

        Vector2 start = GetRectEdgePoint(parentRect, dir);
        Vector2 end = GetRectEdgePoint(childRect, -dir);

        start += dir * 2f;
        end -= dir * 2f;

        Handles.DrawLine(start, end);
    }

    private Vector2 GetRectEdgePoint(Rect rect, Vector2 dir)
    {
        Vector2 center = rect.center;
        float halfW = rect.width * 0.5f;
        float halfH = rect.height * 0.5f;

        float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
        float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;

        float t = Mathf.Min(tx, ty);
        return center + dir * t;
    }

    private void DrawSimulationOverview(Vector2 canvasSize, SerializedProperty nodesProp)
    {
        HandleOverviewDragging(canvasSize);

        float currentHeight = overviewCollapsed ? 24f : overviewRect.height;
        Rect currentRect = new Rect(overviewRect.x, overviewRect.y, overviewRect.width, currentHeight);

        EditorGUI.DrawRect(currentRect, new Color(0f, 0f, 0f, 0.68f));

        Rect titleRect = new Rect(currentRect.x, currentRect.y, currentRect.width, 24f);
        EditorGUI.DrawRect(titleRect, new Color(0f, 0f, 0f, 0.82f));

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(titleRect.x + 8f, titleRect.y + 3f, titleRect.width - 32f, 18f), "模拟总览", titleStyle);

        Rect foldRect = new Rect(titleRect.xMax - 20f, titleRect.y + 4f, 14f, 14f);
        if (DrawTextIconButton(foldRect, overviewCollapsed ? "+" : "-"))
            overviewCollapsed = !overviewCollapsed;

        DrawBorder(currentRect, new Color(1f, 1f, 1f, 0.10f), 1f);

        if (overviewCollapsed)
            return;

        int totalPoints = simulatedLevels.Values.Sum();
        Dictionary<string, RewardAccumulator> rewards = new Dictionary<string, RewardAccumulator>();
        Dictionary<string, int> costs = new Dictionary<string, int>();

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            int currentLv = simulatedLevels.ContainsKey(i) ? simulatedLevels[i] : 0;
            if (currentLv <= 0)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);
            SerializedProperty levelsProp = node.FindPropertyRelative("levels");

            for (int lv = 0; lv < currentLv && lv < levelsProp.arraySize; lv++)
            {
                SerializedProperty level = levelsProp.GetArrayElementAtIndex(lv);

                SerializedProperty rewardsProp = level.FindPropertyRelative("rewards");
                for (int r = 0; r < rewardsProp.arraySize; r++)
                {
                    SerializedProperty reward = rewardsProp.GetArrayElementAtIndex(r);
                    string key = reward.FindPropertyRelative("key").stringValue;
                    string display = reward.FindPropertyRelative("displayName").stringValue;
                    if (string.IsNullOrWhiteSpace(display))
                        display = string.IsNullOrWhiteSpace(key) ? "未命名收益" : key;

                    if (!rewards.ContainsKey(display))
                        rewards[display] = new RewardAccumulator
                        {
                            displayName = display,
                            isPercent = reward.FindPropertyRelative("isPercent").boolValue
                        };

                    rewards[display].value += reward.FindPropertyRelative("value").floatValue;
                }

                SerializedProperty costsProp = level.FindPropertyRelative("costs");
                for (int c = 0; c < costsProp.arraySize; c++)
                {
                    SerializedProperty cost = costsProp.GetArrayElementAtIndex(c);
                    UnityEngine.Object item = cost.FindPropertyRelative("item").objectReferenceValue;
                    string itemName = item != null ? item.name : "未命名道具";
                    if (!costs.ContainsKey(itemName))
                        costs[itemName] = 0;
                    costs[itemName] += cost.FindPropertyRelative("amount").intValue;
                }
            }
        }

        float y = currentRect.y + 30f;
        GUIStyle whiteMini = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
        GUIStyle grayMini = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.82f, 0.82f, 0.84f) } };

        GUI.Label(new Rect(currentRect.x + 8f, y, currentRect.width - 16f, 18f), $"投入点数：{totalPoints}", whiteMini);
        y += 18f;

        GUI.Label(new Rect(currentRect.x + 8f, y, currentRect.width - 16f, 18f), "累计收益", whiteMini);
        y += 18f;

        foreach (var reward in rewards.Values.OrderBy(r => r.displayName))
        {
            string text = reward.isPercent
                ? $"{reward.displayName}  +{reward.value:0.##}%"
                : $"{reward.displayName}  +{reward.value:0.##}";
            GUI.Label(new Rect(currentRect.x + 14f, y, currentRect.width - 20f, 16f), text, grayMini);
            y += 16f;
            if (y > currentRect.yMax - 60f) break;
        }

        y += 4f;
        GUI.Label(new Rect(currentRect.x + 8f, y, currentRect.width - 16f, 18f), "材料消耗", whiteMini);
        y += 18f;

        foreach (var cost in costs.OrderBy(c => c.Key))
        {
            GUI.Label(new Rect(currentRect.x + 14f, y, currentRect.width - 20f, 16f), $"{cost.Key}  ×{cost.Value}", grayMini);
            y += 16f;
            if (y > currentRect.yMax - 18f) break;
        }
    }

    private bool DrawTextIconButton(Rect rect, string text)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);

        GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = hover ? Color.white : new Color(0.86f, 0.86f, 0.88f) }
        };

        if (e.type == EventType.MouseDown && e.button == 0 && hover)
        {
            e.Use();
            GUI.changed = true;
            return true;
        }

        GUI.Label(rect, text, style);
        return false;
    }

    private void HandleOverviewDragging(Vector2 canvasSize)
    {
        Event e = Event.current;
        if (e == null || !simulationMode)
            return;

        float currentHeight = overviewCollapsed ? 24f : overviewRect.height;
        Rect currentRect = new Rect(overviewRect.x, overviewRect.y, overviewRect.width, currentHeight);
        Rect titleRect = new Rect(currentRect.x, currentRect.y, currentRect.width, 24f);
        Rect foldRect = new Rect(titleRect.xMax - 20f, titleRect.y + 4f, 14f, 14f);

        if (e.type == EventType.MouseDown && e.button == 0 && titleRect.Contains(e.mousePosition) && !foldRect.Contains(e.mousePosition))
        {
            draggingOverview = true;
            overviewDragOffset = e.mousePosition - currentRect.position;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && draggingOverview)
        {
            Vector2 pos = e.mousePosition - overviewDragOffset;

            float maxX = Mathf.Max(0f, canvasSize.x - currentRect.width);
            float maxY = Mathf.Max(0f, canvasSize.y - currentRect.height);

            pos.x = Mathf.Clamp(pos.x, 0f, maxX);
            pos.y = Mathf.Clamp(pos.y, 0f, maxY);

            overviewRect.position = pos;
            GUI.changed = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && draggingOverview)
        {
            draggingOverview = false;
            e.Use();
        }
    }

    private class RewardAccumulator
    {
        public string displayName;
        public float value;
        public bool isPercent;
    }
}
