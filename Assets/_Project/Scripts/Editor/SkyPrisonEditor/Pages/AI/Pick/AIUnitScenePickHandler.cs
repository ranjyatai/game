using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class AIUnitScenePickHandler : IAIScenePickHandler
{
    private static AIUnitScenePickHandler instance;

    private AIScenePickRequest activeRequest;
    private AIScenePickResult pendingResult;

    private string hintText = "请在当前地图中点击一个单位。ESC 取消。";
    private string subHintText = "可直接点击角色图形区域，或在 Hierarchy 中选择单位。";

    private SkyPrisonSceneUnitMarker hoveredMarker;
    private int lastSelectionInstanceId = 0;

    public AIScenePickKind Kind => AIScenePickKind.Unit;

    public static void EnsureRegistered()
    {
        if (instance != null)
            return;

        instance = new AIUnitScenePickHandler();
        AIScenePickCoordinator.RegisterHandler(instance);
    }

    public void Begin(AIScenePickRequest request)
    {
        activeRequest = request;
        pendingResult = null;
        hoveredMarker = null;
        lastSelectionInstanceId = Selection.activeInstanceID;

        hintText = string.IsNullOrWhiteSpace(request?.title)
            ? "请在当前地图中点击一个单位。ESC 取消。"
            : request.title;

        subHintText = string.IsNullOrWhiteSpace(request?.hint)
            ? "可直接点击角色图形区域，或在 Hierarchy 中选择单位。"
            : request.hint;

        FocusBackToCurrentMapView();

        Debug.Log(
            $"[AIUnitScenePickHandler] Begin. " +
            $"slotId={request?.slotId}, title={request?.title}"
        );
    }

    public void Cancel()
    {
        Debug.Log("[AIUnitScenePickHandler] Cancel.");

        activeRequest = null;
        pendingResult = null;
        hoveredMarker = null;
        lastSelectionInstanceId = 0;

        InternalEditorUtility.RepaintAllViews();
    }

    public void OnSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        if (e == null)
            return;

        hoveredMarker = null;

        HandleHierarchySelectionPick();

        if (pendingResult != null)
        {
            sceneView.Repaint();
            return;
        }

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        DrawOverlayHint();

        SkyPrisonSceneUnitMarker[] markers = Object.FindObjectsByType<SkyPrisonSceneUnitMarker>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        if (markers == null || markers.Length == 0)
        {
            hintText = "场景中一个 SkyPrisonSceneUnitMarker 都没有找到。";
            subHintText = "请确认单位根对象上真的挂了 SkyPrisonSceneUnitMarker。";
            sceneView.Repaint();
            return;
        }

        Vector2 mouse = e.mousePosition;
        int validCount = 0;

        GUIStyle labelStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };

        Handles.BeginGUI();

        foreach (var marker in markers)
        {
            if (marker == null)
                continue;

            marker.RefreshBindingCache();

            if (!marker.IsValidUnit())
                continue;

            validCount++;

            if (!TryGetUnitScreenRect(marker, out Rect unitRect))
                continue;

            bool isHover = unitRect.Contains(mouse);
            if (isHover)
            {
                hoveredMarker = marker;
                hintText = $"可选择单位：{marker.GetBestLabel()}";
                subHintText = $"SceneGuid：{marker.SceneUnitGuid}";
            }

            DrawUnitHotspot(unitRect, isHover);

            Rect labelRect = new Rect(unitRect.x - 40f, unitRect.y - 20f, unitRect.width + 80f, 18f);
            GUI.Label(labelRect, marker.GetBestLabel(), labelStyle);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && unitRect.Contains(mouse))
            {
                marker.RefreshBindingCache();

                Selection.activeGameObject = marker.gameObject;
                EditorGUIUtility.PingObject(marker.gameObject);

                if (TryBuildResultFromMarker(marker, out AIScenePickResult result))
                {
                    pendingResult = result;
                    hintText = $"已选择单位：{result.objectName}";
                    subHintText = $"ID：{result.objectId}";
                }

                e.Use();
                Handles.EndGUI();
                sceneView.Repaint();
                return;
            }
        }

        if (hoveredMarker == null)
        {
            hintText = $"已找到有效单位 {validCount} 个。";
            subHintText = "请直接点击角色图形区域，或在 Hierarchy 中选择单位。";
        }

        Handles.EndGUI();
        sceneView.Repaint();
    }

    public bool TryGetResult(out AIScenePickResult result)
    {
        result = pendingResult;
        if (pendingResult == null)
            return false;

        pendingResult = null;
        return true;
    }

    private void HandleHierarchySelectionPick()
    {
        GameObject selected = Selection.activeGameObject;
        int currentSelectionId = selected != null ? selected.GetInstanceID() : 0;

        if (currentSelectionId == lastSelectionInstanceId)
            return;

        lastSelectionInstanceId = currentSelectionId;

        if (selected == null)
            return;

        SkyPrisonSceneUnitMarker marker = selected.GetComponent<SkyPrisonSceneUnitMarker>();
        if (marker == null)
            marker = selected.GetComponentInParent<SkyPrisonSceneUnitMarker>();

        if (marker == null)
            return;

        marker.RefreshBindingCache();

        if (!marker.IsValidUnit())
            return;

        if (TryBuildResultFromMarker(marker, out AIScenePickResult result))
        {
            pendingResult = result;
            hintText = $"已从 Hierarchy 选择单位：{result.objectName}";
            subHintText = $"ID：{result.objectId}";
            Debug.Log($"[AIUnitScenePickHandler] Picked from Hierarchy: {result.objectName} ({result.objectId})");
        }
    }

    private bool TryBuildResultFromMarker(SkyPrisonSceneUnitMarker marker, out AIScenePickResult result)
    {
        result = null;
        if (marker == null)
            return false;

        string rawUnitDefinitionId = marker.UnitDefinitionId;
        string rawSceneUnitGuid = marker.SceneUnitGuid;
        string rawDisplayName = marker.DisplayName;

        string pickedId = rawUnitDefinitionId;
        if (string.IsNullOrWhiteSpace(pickedId))
            pickedId = rawSceneUnitGuid;

        string pickedName = rawDisplayName;
        if (string.IsNullOrWhiteSpace(pickedName))
            pickedName = marker.name;

        Debug.Log(
            $"[AIUnitScenePickHandler] Resolve marker. " +
            $"UnitDefinitionId={rawUnitDefinitionId}, SceneUnitGuid={rawSceneUnitGuid}, DisplayName={rawDisplayName}, MarkerName={marker.name}"
        );

        if (string.IsNullOrWhiteSpace(pickedId))
        {
            Debug.LogWarning("[AIUnitScenePickHandler] Pick failed: resolved objectId is empty.");
            return false;
        }

        result = new AIScenePickResult
        {
            pickKind = AIScenePickKind.Unit,
            objectId = pickedId,
            objectName = pickedName,
            objectType = "Unit",
            sceneObject = marker.gameObject,
            worldPosition = marker.transform.position,
            bounds = TryGetBestBounds(marker, out Bounds b) ? b : new Bounds(marker.transform.position, Vector3.zero),
            extraJson = ""
        };

        return true;
    }

    private void DrawOverlayHint()
    {
        Handles.BeginGUI();

        Rect areaRect = new Rect(12f, 12f, 560f, 82f);
        GUILayout.BeginArea(areaRect, "SkyPrison 场景单位选择", GUI.skin.window);
        GUILayout.Label(hintText);

        GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true
        };
        GUILayout.Label(subHintText, subStyle);
        GUILayout.EndArea();

        Handles.EndGUI();
    }

    private void DrawUnitHotspot(Rect rect, bool isHover)
    {
        Color fill = isHover
            ? new Color(0.20f, 0.85f, 1f, 0.18f)
            : new Color(0.20f, 0.85f, 1f, 0.04f);

        Color border = isHover
            ? new Color(0.20f, 0.85f, 1f, 0.95f)
            : new Color(0.20f, 0.85f, 1f, 0.35f);

        EditorGUI.DrawRect(rect, fill);

        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), border);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), border);
    }

    private bool TryGetUnitScreenRect(SkyPrisonSceneUnitMarker marker, out Rect rect)
    {
        rect = default;

        if (marker == null)
            return false;

        if (!TryGetBestBounds(marker, out Bounds bounds))
            return false;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        Vector3[] worldCorners = new Vector3[8]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z),
        };

        bool hasPoint = false;
        float xMin = float.MaxValue;
        float yMin = float.MaxValue;
        float xMax = float.MinValue;
        float yMax = float.MinValue;

        for (int i = 0; i < worldCorners.Length; i++)
        {
            Vector2 gui = HandleUtility.WorldToGUIPoint(worldCorners[i]);

            if (float.IsNaN(gui.x) || float.IsNaN(gui.y))
                continue;

            hasPoint = true;
            xMin = Mathf.Min(xMin, gui.x);
            yMin = Mathf.Min(yMin, gui.y);
            xMax = Mathf.Max(xMax, gui.x);
            yMax = Mathf.Max(yMax, gui.y);
        }

        if (!hasPoint)
            return false;

        const float padding = 8f;
        rect = Rect.MinMaxRect(xMin - padding, yMin - padding, xMax + padding, yMax + padding);

        if (rect.width < 24f) rect.width = 24f;
        if (rect.height < 24f) rect.height = 24f;

        return true;
    }

    private bool TryGetBestBounds(SkyPrisonSceneUnitMarker marker, out Bounds bounds)
    {
        Transform root = marker.transform;

        Transform visualRoot = root.Find("VisualRoot");
        if (visualRoot != null && TryGetBoundsFromRenderers(visualRoot, out bounds))
            return true;

        Transform model1 = FindChildRecursive(root, "模型1");
        if (model1 != null && TryGetBoundsFromRenderers(model1, out bounds))
            return true;

        if (TryGetBoundsFromRenderers(root, out bounds))
            return true;

        Collider col3D = marker.GetComponentInChildren<Collider>();
        if (col3D != null)
        {
            bounds = col3D.bounds;
            return true;
        }

        Collider2D col2D = marker.GetComponentInChildren<Collider2D>();
        if (col2D != null)
        {
            bounds = col2D.bounds;
            return true;
        }

        bounds = new Bounds(marker.transform.position, Vector3.one);
        return true;
    }

    private bool TryGetBoundsFromRenderers(Transform targetRoot, out Bounds bounds)
    {
        bounds = default;

        if (targetRoot == null)
            return false;

        Renderer[] renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        bool initialized = false;
        Bounds result = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (!initialized)
            {
                result = r.bounds;
                initialized = true;
            }
            else
            {
                result.Encapsulate(r.bounds);
            }
        }

        if (!initialized)
            return false;

        bounds = result;
        return true;
    }

    private Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);

            if (child.name == targetName)
                return child;

            Transform found = FindChildRecursive(child, targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void FocusBackToCurrentMapView()
    {
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.Show();
            SceneView.lastActiveSceneView.Focus();
            SceneView.lastActiveSceneView.Repaint();
        }
        else
        {
            EditorApplication.ExecuteMenuItem("Window/General/Scene");
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Show();
                SceneView.lastActiveSceneView.Focus();
                SceneView.lastActiveSceneView.Repaint();
            }
        }
    }
}
