using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地图放置工具的「AFP购买区」模块：在 Scene 里按住左键拖出一块矩形地面区域，松手生成一个 SkyPrisonAfpPurchaseZone。
/// 游戏里这块区域的地面上会画出冷绿色边框 + AXIA 标志，玩家走进去按 E 触发购买（购买界面之后接）。
/// 一张地图可以有多个购买区(浅/中/深层都有)。角点吸附到 0.5 米网格。统一挂在 WorldRoot/AfpRoot 下。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private const string AfpZoneParentPath = "WorldRoot/AfpRoot";
    private const float AfpZoneSnap = 0.5f;
    private const float AfpZoneMinSize = 2f;

    private bool afpZoneDragging;
    private Vector3 afpZoneDragStart;
    private Vector2 afpZonePlacedScroll;

    private static SkyPrisonAfpPurchaseZone[] FindAfpZones()
    {
        return FindObjectsByType<SkyPrisonAfpPurchaseZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private static Vector3 SnapAfp(Vector3 p)
    {
        return new Vector3(Mathf.Round(p.x / AfpZoneSnap) * AfpZoneSnap, p.y, Mathf.Round(p.z / AfpZoneSnap) * AfpZoneSnap);
    }

    private void DrawAfpZonePlacePage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("AFP 购买区", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("父节点", AfpZoneParentPath);
        EditorGUILayout.LabelField("已摆放", FindAfpZones().Length + " 个");
        EditorGUILayout.EndVertical();

        EditorGUILayout.HelpBox(
            "点上方「放置」进入摆放模式：Scene 视图里按住左键拖出一块矩形，松手就生成购买区(角点吸附 0.5 米网格)。可以连续拖多块，右键或 Esc 退出。\n" +
            "游戏里这块地面会画出冷绿色边框 + AXIA 标志，玩家走进去出现交互提示 E，周围有敌人盯着玩家时区域变灰、不能用。\n" +
            "放好后选中物体，可以在属性里改大小(Size)和标志宽度；整体旋转用物体的 Y 旋转。",
            MessageType.Info);
    }

    private void OnAfpZonePlacementSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1))
        {
            afpZoneDragging = false;
            SetPlacementMode(enabled: false);
            e.Use();
            return;
        }

        if (!TryRaycastGround(e.mousePosition, out Vector3 raw))
            return;
        Vector3 hit = SnapAfp(raw);

        Color green = new Color(0.42f, 0.92f, 0.68f, 1f);
        if (!afpZoneDragging)
        {
            // 还没按下：光标处画个十字，提示从这里开始拖
            Handles.color = green;
            Handles.DrawLine(hit + Vector3.left * 0.8f + Vector3.up * 0.05f, hit + Vector3.right * 0.8f + Vector3.up * 0.05f);
            Handles.DrawLine(hit + Vector3.back * 0.8f + Vector3.up * 0.05f, hit + Vector3.forward * 0.8f + Vector3.up * 0.05f);
            Handles.Label(hit + Vector3.up * 1f, "按住左键拖出 AFP 购买区");
        }
        else
        {
            DrawAfpPreviewRect(afpZoneDragStart, hit, green);
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            afpZoneDragging = true;
            afpZoneDragStart = hit;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && afpZoneDragging)
        {
            afpZoneDragging = false;
            PlaceAfpZone(afpZoneDragStart, hit);
            e.Use();
        }

        sceneView.Repaint();
    }

    private static void DrawAfpPreviewRect(Vector3 a, Vector3 b, Color green)
    {
        float y = Mathf.Max(a.y, b.y) + 0.05f;
        var v = new[]
        {
            new Vector3(a.x, y, a.z), new Vector3(b.x, y, a.z),
            new Vector3(b.x, y, b.z), new Vector3(a.x, y, b.z),
        };
        Handles.DrawSolidRectangleWithOutline(v, new Color(green.r, green.g, green.b, 0.12f), green);
        float w = Mathf.Abs(b.x - a.x), d = Mathf.Abs(b.z - a.z);
        Handles.Label((a + b) * 0.5f + Vector3.up * 1f, w.ToString("0.#") + " x " + d.ToString("0.#") + " 米");
    }

    private void PlaceAfpZone(Vector3 a, Vector3 b)
    {
        float w = Mathf.Abs(b.x - a.x), d = Mathf.Abs(b.z - a.z);
        if (w < AfpZoneMinSize || d < AfpZoneMinSize)
        {
            ShowNotification(new GUIContent("矩形太小(至少 " + AfpZoneMinSize + " 米)，没有生成"));
            return;
        }

        Transform parent = GetOrCreateParent(AfpZoneParentPath);
        var go = new GameObject("AFP购买区");
        Undo.RegisterCreatedObjectUndo(go, "Place AFP Purchase Zone");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3((a.x + b.x) * 0.5f, Mathf.Min(a.y, b.y), (a.z + b.z) * 0.5f);

        var zone = Undo.AddComponent<SkyPrisonAfpPurchaseZone>(go);
        zone.size = new Vector2(w, d);
        EditorUtility.SetDirty(zone);
        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        // 留在摆放模式，可以接着拖下一块
    }

    private void DrawAfpZonePlacedPage()
    {
        EditorGUILayout.LabelField("已摆放购买区", EditorStyles.boldLabel);
        var zones = FindAfpZones().OrderBy(z => z.transform.position.x).ToList();
        if (zones.Count == 0)
        {
            EditorGUILayout.HelpBox("场景里还没有 AFP 购买区。到「放置」页拖出一块。", MessageType.Info);
            return;
        }

        afpZonePlacedScroll = EditorGUILayout.BeginScrollView(afpZonePlacedScroll);
        SkyPrisonAfpPurchaseZone toDelete = null;
        foreach (var z in zones)
        {
            bool selected = Selection.activeGameObject == z.gameObject;
            Rect row = EditorGUILayout.GetControlRect(false, 46f);
            if (selected)
                EditorGUI.DrawRect(row, new Color(accent.r, accent.g, accent.b, 0.35f));
            GUI.Label(new Rect(row.x + 6f, row.y + 4f, row.width - 200f, 20f), z.name, EditorStyles.boldLabel);
            Vector3 pos = z.transform.position;
            GUI.Label(new Rect(row.x + 6f, row.y + 24f, row.width - 200f, 18f),
                $"{z.size.x:0.#} x {z.size.y:0.#} 米  /  X {pos.x:0.#}  Z {pos.z:0.#}", EditorStyles.miniLabel);
            if (GUI.Button(new Rect(row.xMax - 186f, row.y + 12f, 58f, 22f), "选中"))
                Selection.activeGameObject = z.gameObject;
            if (GUI.Button(new Rect(row.xMax - 124f, row.y + 12f, 58f, 22f), "定位"))
            {
                Selection.activeGameObject = z.gameObject;
                SceneView.lastActiveSceneView?.Frame(new Bounds(pos, new Vector3(z.size.x, 2f, z.size.y)), false);
            }
            if (GUI.Button(new Rect(row.xMax - 62f, row.y + 12f, 58f, 22f), "删除"))
                toDelete = z;
        }
        EditorGUILayout.EndScrollView();

        if (toDelete != null && EditorUtility.DisplayDialog("删除确认", $"删除购买区「{toDelete.name}」？", "删除", "取消"))
            Undo.DestroyObjectImmediate(toDelete.gameObject);
    }
}
