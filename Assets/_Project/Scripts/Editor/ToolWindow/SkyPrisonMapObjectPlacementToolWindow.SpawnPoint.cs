using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地图放置工具的「出生点」模块：摆「玩家初始点」。
/// 地图里不再手摆 AXIA——摆一个初始点，选中它设置初始单位（AXIA 的初始条件为无）。
/// 进场景时由 SkyPrisonPlayerStartPoint 在这个点生成玩家。
/// 初始点是普通场景物体（不走地形装饰物那套遮挡/碰撞），统一挂在 WorldRoot/SpawnRoot 下。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private const string SpawnParentPath = "WorldRoot/SpawnRoot";

    private UnitDefinition spawnInitialUnit;
    private float spawnPlaceYaw;
    private Vector2 spawnPlacedScroll;
    // 「重新放置」：下一次在 Scene 里左键点击，就把这个已有的初始点挪到点击处。
    private SkyPrisonPlayerStartPoint spawnRelocateTarget;

    private void EnsureSpawnDefaultUnit()
    {
        if (spawnInitialUnit != null)
            return;

        // 默认选玩家身份的单位（AXIA）。
        foreach (string guid in AssetDatabase.FindAssets("t:UnitDefinition"))
        {
            var ud = AssetDatabase.LoadAssetAtPath<UnitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (ud != null && ud.defineType == UnitDefineType.Character && ud.characterIdentity == CharacterIdentity.Player && ud.prefab != null)
            {
                spawnInitialUnit = ud;
                return;
            }
        }
    }

    private static string SpawnUnitLabel(UnitDefinition ud)
    {
        if (ud == null)
            return "（未设置）";
        return string.IsNullOrEmpty(ud.displayName) ? ud.name : ud.displayName;
    }

    private void DrawSpawnPointPlacePage()
    {
        EnsureSpawnDefaultUnit();

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("玩家初始点", EditorStyles.boldLabel);
        spawnInitialUnit = (UnitDefinition)EditorGUILayout.ObjectField("初始单位", spawnInitialUnit, typeof(UnitDefinition), false);
        spawnPlaceYaw = EditorGUILayout.FloatField("朝向（Y 轴）", spawnPlaceYaw);
        EditorGUILayout.LabelField("父节点", SpawnParentPath);
        if (spawnInitialUnit != null && spawnInitialUnit.prefab == null)
            EditorGUILayout.HelpBox("这个单位没有预制体，进游戏时没法生成。", MessageType.Warning);
        EditorGUILayout.EndVertical();

        if (SpawnPointExists(out string where, out string scenePath))
        {
            EditorGUILayout.HelpBox("天空囚笼只能有一个玩家初始点，项目里已经有了：\n" + where +
                "\n不能再摆第二个。要换位置，到「已摆放」页选中它直接移动。", MessageType.Warning);
            if (GUILayout.Button("去已摆放页看它"))
                currentPage = ToolPage.Placed;
        }
        else
        {
            EditorGUILayout.HelpBox(
                "点上方「放置」进入摆放模式：Scene 视图里左键点击摆放，右键或 Esc 退出；滚轮旋转朝向。\n" +
                "天空囚笼只能有一个玩家初始点，摆下之后项目里就不能再摆第二个了。\n" +
                "放下后到「已摆放」页选中它，可改初始单位、填初始条件（AXIA 为无，留空）。\n" +
                "进场景时在这个点生成玩家；场景里已经有手摆的玩家时不会重复生成。",
                MessageType.Info);
        }
    }

    private bool SpawnPointExists(out string where, out string scenePath)
    {
        return SkyPrisonPlayerStartPointGuard.TryGetExisting(out where, out scenePath);
    }

    private bool CanPlaceSpawnPoint()
    {
        if (spawnRelocateTarget != null)
            return true;   // 重新放置：移动已有的初始点，不算"再摆一个"
        EnsureSpawnDefaultUnit();
        return spawnInitialUnit != null && !SpawnPointExists(out _, out _);
    }

    private void OnSpawnPlacementSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1))
        {
            spawnRelocateTarget = null;
            SetPlacementMode(enabled: false);
            e.Use();
            return;
        }

        if (e.type == EventType.ScrollWheel && !e.alt && !EditorGUIUtility.editingTextField)
        {
            spawnPlaceYaw = Mathf.Repeat(spawnPlaceYaw + (e.delta.y > 0f ? 15f : -15f), 360f);
            e.Use();
            Repaint();
            return;
        }

        if (!TryRaycastGround(e.mousePosition, out Vector3 hit))
            return;

        // 预览：脚下圈 + 朝向箭头，和放下后的 Gizmo 一致。
        Quaternion rot = Quaternion.Euler(0f, spawnPlaceYaw, 0f);
        Handles.color = new Color(0.2f, 1f, 0.6f, 1f);
        Handles.DrawWireDisc(hit, Vector3.up, 0.9f);
        Handles.DrawLine(hit, hit + rot * Vector3.forward * 1.8f);
        Handles.DrawLine(hit, hit + Vector3.up * 3.2f);
        Handles.Label(hit + Vector3.up * 3.5f, "玩家初始点 · " + SpawnUnitLabel(spawnInitialUnit));

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (spawnRelocateTarget != null)
                RelocateSpawnPoint(spawnRelocateTarget, hit);
            else
                PlaceSpawnPoint(hit);
            e.Use();
        }

        sceneView.Repaint();
    }

    private void PlaceSpawnPoint(Vector3 position)
    {
        // 最后一道闸：不管从哪里调进来，项目里已经有初始点就不摆。（强制重新扫一次，不用缓存。）
        SkyPrisonPlayerStartPointGuard.InvalidateDiskCache();
        if (SpawnPointExists(out string existsWhere, out _))
        {
            EditorUtility.DisplayDialog("玩家初始点只能有一个", "项目里已经有玩家初始点了：\n" + existsWhere, "知道了");
            SetPlacementMode(enabled: false);
            return;
        }

        Transform parent = GetOrCreateParent(SpawnParentPath);
        var go = new GameObject("玩家初始点·" + SpawnUnitLabel(spawnInitialUnit));
        Undo.RegisterCreatedObjectUndo(go, "Place Player Start Point");
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, spawnPlaceYaw, 0f);

        var sp = Undo.AddComponent<SkyPrisonPlayerStartPoint>(go);
        sp.initialUnit = spawnInitialUnit;
        EditorUtility.SetDirty(sp);
        // 摆下去了就退出摆放模式：只能有一个，不需要连续放。
        SetPlacementMode(enabled: false);

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
    }

    private void RelocateSpawnPoint(SkyPrisonPlayerStartPoint point, Vector3 position)
    {
        if (point != null)
        {
            Undo.RecordObject(point.transform, "Relocate Player Start Point");
            point.transform.position = position;
            point.transform.rotation = Quaternion.Euler(0f, spawnPlaceYaw, 0f);
            EditorUtility.SetDirty(point);
            Selection.activeGameObject = point.gameObject;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(point.gameObject.scene);
        }
        spawnRelocateTarget = null;
        SetPlacementMode(enabled: false);
    }

    private void DrawSpawnPlacedPage()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("已摆放初始点", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        var points = FindObjectsByType<SkyPrisonPlayerStartPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(p => p.name).ToList();

        // 属性窗口按钮：选中的是初始点时可直接改初始单位、默认、初始条件。
        DrawDoorPropertiesOnPlacedPage();

        if (points.Count == 0)
        {
            EditorGUILayout.HelpBox("场景里还没有玩家初始点。到「放置」页摆一个（全项目只能有一个）。", MessageType.Info);
            return;
        }

        if (points.Count > 1)
            EditorGUILayout.HelpBox("场景里有 " + points.Count + " 个初始点，只能有一个，请删掉多余的。", MessageType.Error);
        if (points.Any(p => p.InitialUnit == null))
            EditorGUILayout.HelpBox("有初始点没有设置初始单位，进游戏时不会生成玩家。", MessageType.Warning);

        spawnPlacedScroll = EditorGUILayout.BeginScrollView(spawnPlacedScroll);
        SkyPrisonPlayerStartPoint toDelete = null;
        foreach (var p in points)
        {
            bool selected = Selection.activeGameObject == p.gameObject;
            Rect row = EditorGUILayout.GetControlRect(false, 46f);
            if (selected)
                EditorGUI.DrawRect(row, new Color(accent.r, accent.g, accent.b, 0.35f));
            GUI.Label(new Rect(row.x + 6f, row.y + 4f, row.width - 262f, 20f), p.name, EditorStyles.boldLabel);
            Vector3 pos = p.transform.position;
            GUI.Label(new Rect(row.x + 6f, row.y + 24f, row.width - 262f, 18f),
                $"{SpawnUnitLabel(p.InitialUnit)}  /  X {pos.x:0.#}  Z {pos.z:0.#}  朝向 {p.transform.eulerAngles.y:0}°", EditorStyles.miniLabel);
            if (GUI.Button(new Rect(row.xMax - 186f, row.y + 12f, 58f, 22f), "选中"))
                Selection.activeGameObject = p.gameObject;
            if (GUI.Button(new Rect(row.xMax - 124f, row.y + 12f, 58f, 22f), "定位"))
            {
                Selection.activeGameObject = p.gameObject;
                SceneView.lastActiveSceneView?.Frame(new Bounds(pos, Vector3.one * 6f), false);
            }
            if (GUI.Button(new Rect(row.xMax - 62f, row.y + 12f, 58f, 22f), "删除"))
                toDelete = p;
            if (GUI.Button(new Rect(row.xMax - 250f, row.y + 12f, 62f, 22f), new GUIContent("重新放置", "点一下，然后在 Scene 里左键点新位置，初始点就挪过去（滚轮转朝向，右键 / Esc 取消）。")))
            {
                spawnRelocateTarget = p;
                spawnPlaceYaw = p.transform.eulerAngles.y;
                Selection.activeGameObject = p.gameObject;
                SetPlacementMode(enabled: true);
            }
        }
        EditorGUILayout.EndScrollView();

        if (toDelete != null && EditorUtility.DisplayDialog("删除确认", $"删除初始点「{toDelete.name}」？", "删除", "取消"))
            Undo.DestroyObjectImmediate(toDelete.gameObject);
    }
}
