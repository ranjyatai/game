using UnityEditor;
using UnityEngine;

/// <summary>Hierarchy 里给玩家初始点（以及它的父节点 SpawnRoot）画荧光绿底色和星标，一眼找到。</summary>
[InitializeOnLoad]
public static class SkyPrisonPlayerStartPointHierarchyMarker
{
    private static readonly Color Green = new Color(0.25f, 1f, 0.55f, 1f);

    static SkyPrisonPlayerStartPointHierarchyMarker()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnItemGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static SkyPrisonPlayerStartPoint[] s_points = new SkyPrisonPlayerStartPoint[0];
    private static double s_nextRefresh;

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        // 每秒重新收集一次，不在每次重绘里全场查找。
        if (EditorApplication.timeSinceStartup >= s_nextRefresh)
        {
            s_nextRefresh = EditorApplication.timeSinceStartup + 1.0;
            s_points = Object.FindObjectsByType<SkyPrisonPlayerStartPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        for (int i = 0; i < s_points.Length; i++)
        {
            SkyPrisonPlayerStartPoint p = s_points[i];
            if (p == null)
                continue;
            p.DrawEditorMarker(Selection.Contains(p.gameObject));
        }
    }

    private static void OnItemGUI(int instanceID, Rect rect)
    {
        var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (go == null)
            return;

        bool isPoint = go.GetComponent<SkyPrisonPlayerStartPoint>() != null;
        if (!isPoint)
            return;

        EditorGUI.DrawRect(rect, new Color(Green.r, Green.g, Green.b, 0.16f));
        var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
        style.normal.textColor = Green;
        GUI.Label(new Rect(rect.xMax - 24f, rect.y, 22f, rect.height), "★", style);
    }
}
