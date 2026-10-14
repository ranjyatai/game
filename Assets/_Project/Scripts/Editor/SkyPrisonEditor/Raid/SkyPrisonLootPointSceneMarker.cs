using UnityEditor;
using UnityEngine;

/// <summary>Scene 视图里给资源箱候选点位画小标记（不受 Gizmos 开关影响）：按箱子类型取色的小菱形 + 箱子名（只在近处显示名字）。</summary>
[InitializeOnLoad]
public static class SkyPrisonLootPointSceneMarker
{
    static SkyPrisonLootPointSceneMarker() { SceneView.duringSceneGui -= OnGui; SceneView.duringSceneGui += OnGui; }

    static void OnGui(SceneView sv)
    {
        if (Event.current.type != EventType.Repaint || Application.isPlaying) return;
        var pts = Object.FindObjectsByType<SkyPrisonLootPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (pts.Length == 0) return;
        var oldZ = Handles.zTest; Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        var ts = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
        foreach (var p in pts)
        {
            Color c = p.box == null ? Color.gray : Color.HSVToRGB(Mathf.Abs(p.box.name.GetHashCode() % 997) / 997f, 0.7f, 1f);
            Vector3 pos = p.transform.position + Vector3.up * 0.4f;
            float s = HandleUtility.GetHandleSize(pos) * 0.09f;
            Handles.color = new Color(c.r, c.g, c.b, 0.9f);
            Handles.DrawSolidDisc(pos, sv.camera.transform.forward * -1f, s);
            Handles.color = Color.white; Handles.DrawWireDisc(pos, sv.camera.transform.forward * -1f, s, 1.2f);
            if (Vector3.Distance(sv.camera.transform.position, pos) < 70f)
            { ts.normal.textColor = c; Handles.Label(pos + Vector3.up * s * 2.2f, p.box == null ? "（未选箱子）" : (string.IsNullOrEmpty(p.box.displayName) ? p.box.name : p.box.displayName), ts); }
        }
        Handles.zTest = oldZ;
    }
}
