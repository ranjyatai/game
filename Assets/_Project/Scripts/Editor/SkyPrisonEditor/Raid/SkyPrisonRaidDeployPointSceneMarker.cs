using UnityEditor;
using UnityEngine;

/// <summary>
/// 在 Scene 视图里给 RAID 出发点画一个彩色醒目的标记。
/// 用 SceneView.duringSceneGui 画，不走 OnDrawGizmos——Scene 视图里 Gizmos 开关是关着的时候，
/// OnDrawGizmos 一个都不会画，出发点就完全看不见了。这里不受那个开关影响。
/// 画的内容：实心青色圆盘 + 加粗双圈 + 竖线 + 顶端恒定屏幕大小的圆标「R」+ 大号文字，全部关深度测试（隔墙也看得见）。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonRaidDeployPointSceneMarker
{
    static SkyPrisonRaidDeployPointSceneMarker()
    {
        SceneView.duringSceneGui -= OnSceneGui;
        SceneView.duringSceneGui += OnSceneGui;
    }

    static void OnSceneGui(SceneView sv)
    {
        if (Event.current.type != EventType.Repaint || Application.isPlaying) return;
        var points = Object.FindObjectsByType<SkyPrisonRaidDeployPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (points.Length == 0) return;

        var oldZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        foreach (var dp in points) Draw(dp, sv);
        Handles.zTest = oldZ;
    }

    static void Draw(SkyPrisonRaidDeployPoint dp, SceneView sv)
    {
        var c = new Color(0.25f, 0.95f, 1f, 1f);
        Vector3 p = dp.transform.position + Vector3.up * 0.2f;
        float top = Mathf.Max(dp.beamHeight, 6f);
        float r = dp.radius;

        Handles.color = new Color(c.r, c.g, c.b, 0.2f);
        Handles.DrawSolidDisc(p, Vector3.up, r);
        Handles.color = new Color(c.r, c.g, c.b, 0.45f);
        Handles.DrawSolidDisc(p, Vector3.up, r * 0.22f);
        Handles.color = c;
        Handles.DrawWireDisc(p, Vector3.up, r, 1.2f);
        Handles.DrawWireDisc(p, Vector3.up, r * 0.62f, 0.8f);
        Handles.DrawLine(p, p + Vector3.up * top, 1f);

        Vector3 tip = p + Vector3.up * (top + 1.2f);
        Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(Vector3.down), 0.8f, EventType.Repaint);

        // 恒定屏幕大小的圆标：镜头拉多远都一样大
        Vector3 toCam = -sv.camera.transform.forward;
        float s = HandleUtility.GetHandleSize(tip) * 0.17f;
        Handles.color = Color.white;
        Handles.DrawSolidDisc(tip, toCam, s * 1.18f);
        Handles.color = new Color(0.1f, 0.85f, 1f, 1f);
        Handles.DrawSolidDisc(tip, toCam, s);

        var rs = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        rs.normal.textColor = new Color(0.02f, 0.1f, 0.2f, 1f);
        Handles.Label(tip, "R", rs);
        var ts = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        ts.normal.textColor = c;
        Handles.Label(tip + Vector3.up * (s * 2.1f), "RAID 出发点", ts);
    }
}
