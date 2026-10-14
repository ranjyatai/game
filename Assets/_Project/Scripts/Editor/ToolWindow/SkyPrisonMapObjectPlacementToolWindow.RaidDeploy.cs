using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地图放置工具的「RAID出发点」模块：摆一个地面特效点，玩家走近按交互键打开选关界面（SkyPrisonRaidMapScreen）。
/// 一张地图只需要一个：场景里已经有就不再摆第二个（要换位置用「已摆放」页的「重新放置」）。
/// 新摆下去的出发点默认挂上项目里全部 RaidDefinition（按 order 排），到属性里删减即可。
/// 出发点是普通场景物体，统一挂在 WorldRoot/RaidRoot 下。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private const string RaidDeployParentPath = "WorldRoot/RaidRoot";

    private float raidDeployRadius = 2.5f;
    private Vector2 raidDeployPlacedScroll;
    // 「重新放置」：下一次在 Scene 里左键点击，就把这个已有的出发点挪到点击处。
    private SkyPrisonRaidDeployPoint raidDeployRelocateTarget;

    private static List<RaidDefinition> LoadAllRaidDefinitions()
    {
        var list = new List<RaidDefinition>();
        foreach (string guid in AssetDatabase.FindAssets("t:RaidDefinition"))
        {
            var r = AssetDatabase.LoadAssetAtPath<RaidDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (r != null) list.Add(r);
        }
        return list.OrderBy(r => r.order).ThenBy(r => r.name).ToList();
    }

    private static SkyPrisonRaidDeployPoint[] FindRaidDeployPoints()
    {
        return FindObjectsByType<SkyPrisonRaidDeployPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private bool RaidDeployExists()
    {
        return FindRaidDeployPoints().Length > 0;
    }

    private bool CanPlaceRaidDeploy()
    {
        if (raidDeployRelocateTarget != null)
            return true;   // 重新放置：移动已有的出发点，不算"再摆一个"
        return !RaidDeployExists();
    }

    private void DrawRaidDeployPlacePage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("RAID 出发点", EditorStyles.boldLabel);
        raidDeployRadius = Mathf.Max(0.5f, EditorGUILayout.FloatField("触发半径", raidDeployRadius));
        EditorGUILayout.LabelField("父节点", RaidDeployParentPath);
        EditorGUILayout.EndVertical();

        if (RaidDeployExists())
        {
            EditorGUILayout.HelpBox("这张地图已经有 RAID 出发点了，一个就够，不能再摆第二个。\n要换位置，到「已摆放」页点「重新放置」。", MessageType.Warning);
            if (GUILayout.Button("去已摆放页看它"))
                currentPage = ToolPage.Placed;
        }
        else
        {
            EditorGUILayout.HelpBox(
                "点上方「放置」进入摆放模式：Scene 视图里左键点击摆放，右键或 Esc 退出。\n" +
                "摆下去以后自动挂上项目里全部 RAID（旧工厂、学校……按 order 排），到「已摆放」页选中它可以删减。\n" +
                "玩家走进光环按交互键，打开选关界面，ESC 关闭。",
                MessageType.Info);
        }
    }

    private void OnRaidDeployPlacementSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1))
        {
            raidDeployRelocateTarget = null;
            SetPlacementMode(enabled: false);
            e.Use();
            return;
        }

        if (!TryRaycastGround(e.mousePosition, out Vector3 hit))
            return;

        // 预览：触发半径外圈 + 内圈 + 竖线 + 文字，和放下后的光环一致。
        Handles.color = new Color(0.25f, 0.85f, 1f, 1f);
        Handles.DrawWireDisc(hit, Vector3.up, raidDeployRadius);
        Handles.DrawWireDisc(hit, Vector3.up, raidDeployRadius * 0.62f);
        Handles.DrawLine(hit, hit + Vector3.up * 4.2f);
        Handles.Label(hit + Vector3.up * 4.5f, "RAID 出发点");

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (raidDeployRelocateTarget != null)
                RelocateRaidDeploy(raidDeployRelocateTarget, hit);
            else
                PlaceRaidDeploy(hit);
            e.Use();
        }

        sceneView.Repaint();
    }

    private void PlaceRaidDeploy(Vector3 position)
    {
        // 最后一道闸：不管从哪里调进来，场景里已经有出发点就不摆。
        if (RaidDeployExists())
        {
            EditorUtility.DisplayDialog("RAID 出发点只需要一个", "这张地图已经有 RAID 出发点了。", "知道了");
            SetPlacementMode(enabled: false);
            return;
        }

        Transform parent = GetOrCreateParent(RaidDeployParentPath);
        var go = new GameObject("RAID出发点");
        Undo.RegisterCreatedObjectUndo(go, "Place RAID Deploy Point");
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        var dp = Undo.AddComponent<SkyPrisonRaidDeployPoint>(go);
        dp.radius = raidDeployRadius;
        dp.raids = LoadAllRaidDefinitions();
        EditorUtility.SetDirty(dp);
        // 摆下去了就退出摆放模式：一个就够，不需要连续放。
        SetPlacementMode(enabled: false);

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
    }

    private void RelocateRaidDeploy(SkyPrisonRaidDeployPoint point, Vector3 position)
    {
        if (point != null)
        {
            Undo.RecordObject(point.transform, "Relocate RAID Deploy Point");
            point.transform.position = position;
            EditorUtility.SetDirty(point);
            Selection.activeGameObject = point.gameObject;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(point.gameObject.scene);
        }
        raidDeployRelocateTarget = null;
        SetPlacementMode(enabled: false);
    }

    private void DrawRaidDeployPlacedPage()
    {
        EditorGUILayout.LabelField("已摆放出发点", EditorStyles.boldLabel);

        var points = FindRaidDeployPoints().OrderBy(p => p.name).ToList();
        if (points.Count == 0)
        {
            EditorGUILayout.HelpBox("场景里还没有 RAID 出发点。到「放置」页摆一个（一张地图一个就够）。", MessageType.Info);
            return;
        }
        if (points.Count > 1)
            EditorGUILayout.HelpBox("场景里有 " + points.Count + " 个出发点，一个就够，请删掉多余的。", MessageType.Error);
        if (points.Any(p => p.raids == null || p.raids.Count == 0))
            EditorGUILayout.HelpBox("有出发点没有挂任何 RAID，玩家走上去也不会触发（选中它，在属性里加）。", MessageType.Warning);

        raidDeployPlacedScroll = EditorGUILayout.BeginScrollView(raidDeployPlacedScroll);
        SkyPrisonRaidDeployPoint toDelete = null;
        foreach (var p in points)
        {
            bool selected = Selection.activeGameObject == p.gameObject;
            Rect row = EditorGUILayout.GetControlRect(false, 46f);
            if (selected)
                EditorGUI.DrawRect(row, new Color(accent.r, accent.g, accent.b, 0.35f));
            GUI.Label(new Rect(row.x + 6f, row.y + 4f, row.width - 262f, 20f), p.name, EditorStyles.boldLabel);
            Vector3 pos = p.transform.position;
            GUI.Label(new Rect(row.x + 6f, row.y + 24f, row.width - 262f, 18f),
                $"RAID {(p.raids != null ? p.raids.Count : 0)} 个  /  X {pos.x:0.#}  Z {pos.z:0.#}  半径 {p.radius:0.#}", EditorStyles.miniLabel);
            if (GUI.Button(new Rect(row.xMax - 186f, row.y + 12f, 58f, 22f), "选中"))
                Selection.activeGameObject = p.gameObject;
            if (GUI.Button(new Rect(row.xMax - 124f, row.y + 12f, 58f, 22f), "定位"))
            {
                Selection.activeGameObject = p.gameObject;
                SceneView.lastActiveSceneView?.Frame(new Bounds(pos, Vector3.one * 8f), false);
            }
            if (GUI.Button(new Rect(row.xMax - 62f, row.y + 12f, 58f, 22f), "删除"))
                toDelete = p;
            if (GUI.Button(new Rect(row.xMax - 250f, row.y + 12f, 62f, 22f), new GUIContent("重新放置", "点一下，然后在 Scene 里左键点新位置，出发点就挪过去（右键 / Esc 取消）。")))
            {
                raidDeployRelocateTarget = p;
                Selection.activeGameObject = p.gameObject;
                SetPlacementMode(enabled: true);
            }
        }
        EditorGUILayout.EndScrollView();

        if (toDelete != null && EditorUtility.DisplayDialog("删除确认", $"删除出发点「{toDelete.name}」？", "删除", "取消"))
            Undo.DestroyObjectImmediate(toDelete.gameObject);
    }
}
