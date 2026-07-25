using UnityEditor;
using UnityEngine;

public class SkyPrisonTechTreeCanvasSurface
{
    private const float MinZoom = 0.35f;
    private const float MaxZoom = 2.50f;
    private const float DefaultZoom = 1.0f;

    private const float ZoomBarWidth = 232f;
    private const float ZoomBarHeight = 24f;
    private const float ZoomBarMarginRight = 18f;
    private const float ZoomBarBottomOffset = 18f;

    private Vector2 panWorld;
    private float zoom = DefaultZoom;

    private bool isPanning;
    private Vector2 lastMousePosition;

    private Texture2D resetZoomIcon;
    private bool resetZoomIconLoaded;

    private readonly Color backgroundColor = new Color(0.10f, 0.10f, 0.11f, 1f);
    private readonly Color subGridColor = new Color(1f, 1f, 1f, 0.03f);
    private readonly Color mainGridColor = new Color(1f, 1f, 1f, 0.06f);

    public float Zoom => zoom;
    public Vector2 PanWorld => panWorld;

    public void ResetView()
    {
        panWorld = Vector2.zero;
        zoom = DefaultZoom;
        isPanning = false;
    }

    public void FocusWorldPoint(Vector2 worldPoint)
    {
        panWorld = worldPoint;
    }

    public void Begin(Rect rect)
    {
        HandleInput(rect);
        DrawBackground(rect);
        DrawGrid(rect);
    }

    public void End(Rect rect)
    {
        DrawZoomBar(rect);
    }

    public Vector2 WorldToCanvasPoint(Vector2 worldPoint, Vector2 canvasSize)
    {
        return canvasSize * 0.5f + (worldPoint - panWorld) * zoom;
    }

    public Rect WorldToCanvasRect(Rect worldRect, Vector2 canvasSize)
    {
        Vector2 center = WorldToCanvasPoint(worldRect.center, canvasSize);
        Vector2 size = worldRect.size * zoom;
        return new Rect(
            center.x - size.x * 0.5f,
            center.y - size.y * 0.5f,
            size.x,
            size.y
        );
    }

    public Vector2 CanvasToWorldPoint(Vector2 canvasPoint, Vector2 canvasSize)
    {
        return panWorld + (canvasPoint - canvasSize * 0.5f) / zoom;
    }

    private void HandleInput(Rect rect)
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (!rect.Contains(e.mousePosition) && !isPanning)
            return;

        if (isPanning)
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);

        if (e.type == EventType.MouseDown && e.button == 2 && rect.Contains(e.mousePosition))
        {
            isPanning = true;
            lastMousePosition = e.mousePosition;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && isPanning)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            Vector2 delta = e.mousePosition - lastMousePosition;
            panWorld -= delta / zoom;
            lastMousePosition = e.mousePosition;
            GUI.changed = true;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && isPanning)
        {
            isPanning = false;
            e.Use();
            return;
        }

        if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
        {
            Vector2 localMouse = e.mousePosition - rect.position;
            Vector2 worldBefore = CanvasToWorldPoint(localMouse, rect.size);

            float zoomDelta = -e.delta.y * 0.06f;
            float oldZoom = zoom;
            zoom = Mathf.Clamp(zoom + zoomDelta, MinZoom, MaxZoom);

            if (!Mathf.Approximately(oldZoom, zoom))
            {
                Vector2 worldAfter = CanvasToWorldPoint(localMouse, rect.size);
                panWorld += worldBefore - worldAfter;
                GUI.changed = true;
            }

            e.Use();
        }
    }

    private void DrawBackground(Rect rect)
    {
        EditorGUI.DrawRect(rect, backgroundColor);
    }

    private void DrawGrid(Rect rect)
    {
        Handles.BeginGUI();

        float smallGrid = 32f * zoom;
        float bigGrid = 160f * zoom;

        if (smallGrid >= 12f)
            DrawGridLines(rect, smallGrid, subGridColor);

        if (bigGrid >= 24f)
            DrawGridLines(rect, bigGrid, mainGridColor);

        Handles.EndGUI();
    }

    private void DrawGridLines(Rect rect, float spacing, Color color)
    {
        if (spacing <= 0.001f)
            return;

        Handles.color = color;

        Vector2 canvasCenter = rect.center;
        Vector2 worldCenterScreen = canvasCenter - panWorld * zoom;

        float startX = worldCenterScreen.x % spacing;
        if (startX < 0f) startX += spacing;

        for (float x = rect.xMin + startX; x <= rect.xMax; x += spacing)
            Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));

        float startY = worldCenterScreen.y % spacing;
        if (startY < 0f) startY += spacing;

        for (float y = rect.yMin + startY; y <= rect.yMax; y += spacing)
            Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));
    }

    private void DrawZoomBar(Rect rect)
    {
        EnsureResetZoomIconLoaded();

        Rect barRect = new Rect(
            rect.xMax - ZoomBarWidth - ZoomBarMarginRight,
            rect.yMax - ZoomBarHeight - ZoomBarBottomOffset,
            ZoomBarWidth,
            ZoomBarHeight
        );

        EditorGUI.DrawRect(barRect, new Color(0f, 0f, 0f, 0.34f));

        Rect resetRect = new Rect(barRect.x + 4f, barRect.y + 2f, 22f, barRect.height - 4f);
        Rect minusRect = new Rect(resetRect.xMax + 4f, barRect.y + 2f, 22f, barRect.height - 4f);
        Rect plusRect = new Rect(barRect.xMax - 26f, barRect.y + 2f, 22f, barRect.height - 4f);
        Rect labelRect = new Rect(plusRect.x - 58f, barRect.y + 2f, 48f, barRect.height - 4f);
        Rect sliderRect = new Rect(minusRect.xMax + 6f, barRect.y + 4f, labelRect.x - (minusRect.xMax + 12f), barRect.height - 8f);

        GUIContent resetContent = resetZoomIcon != null
            ? new GUIContent(resetZoomIcon, "重置缩放")
            : new GUIContent("R", "重置缩放");

        if (GUI.Button(resetRect, resetContent))
            zoom = DefaultZoom;

        if (GUI.Button(minusRect, "-"))
            zoom = Mathf.Clamp(zoom - 0.1f, MinZoom, MaxZoom);

        if (GUI.Button(plusRect, "+"))
            zoom = Mathf.Clamp(zoom + 0.1f, MinZoom, MaxZoom);

        zoom = GUI.HorizontalSlider(sliderRect, zoom, MinZoom, MaxZoom);

        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        GUI.Label(labelRect, Mathf.RoundToInt(zoom * 100f) + "%", labelStyle);
    }

    private void EnsureResetZoomIconLoaded()
    {
        if (resetZoomIconLoaded)
            return;

        resetZoomIcon = LoadEditorIcon(15);
        resetZoomIconLoaded = true;
    }

    private Texture2D LoadEditorIcon(int number)
    {
        const string editorIconFolder = "Assets/_Project/Icon/Editor/";
        const string editorIconPrefix = "SkyPrisonEditor_";

        string num = number.ToString("00");
        string basePath = editorIconFolder + editorIconPrefix + num;

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
}
