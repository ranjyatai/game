using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地图放置工具的「资源箱点位」模块：手摆资源箱候选点位（SkyPrisonLootPoint）。
/// 先选一种箱子（单位定义），在 Scene 里连续左键点击摆放；摆多少个都行，
/// 每一把进图时由 SkyPrisonLootDirector 按地图编辑器里的「最多几个」从同种点位里随机抽。
/// 点位挂在 WorldRoot/LootRoot 下。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private const string LootParentPath = "WorldRoot/LootRoot";

    private UnitDefinition lootPlaceBox;
    private float lootPlaceWeight = 1f;
    private string lootPlaceZone = "";
    private Vector2 lootPlacedScroll;

    private bool CanPlaceLootPoint() { return lootPlaceBox != null; }

    private static string LootBoxLabel(UnitDefinition u)
    {
        if (u == null) return "（未选箱子）";
        return string.IsNullOrEmpty(u.displayName) ? u.name : u.displayName;
    }

    private void DrawLootPointPlacePage()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("资源箱候选点位", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("箱子（单位定义）");
        if (GUILayout.Button(lootPlaceBox != null ? UnitPickerPopupContent.GetLabel(lootPlaceBox) : "（点此选择箱子）", EditorStyles.popup))
        {
            SkyPrisonItemPickerPopup.OpenFiltered(lootPlaceBox, picked => { var u = picked as UnitDefinition; if (u != null) { lootPlaceBox = u; Repaint(); } },
                o => o is UnitDefinition ud0 && ud0.defineType == UnitDefineType.Destructible, nameof(UnitDefinition));   // 只列单位里的「可破坏物品」
        }
        EditorGUILayout.EndHorizontal();
        lootPlaceWeight = Mathf.Max(0f, EditorGUILayout.FloatField("抽选权重", lootPlaceWeight));
        lootPlaceZone = EditorGUILayout.TextField("区块名（可空）", lootPlaceZone);
        EditorGUILayout.LabelField("父节点", LootParentPath);
        EditorGUILayout.EndVertical();

        int same = lootPlaceBox == null ? 0 : FindObjectsByType<SkyPrisonLootPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(p => p.box == lootPlaceBox);
        string extra = lootPlaceBox != null ? "\n\n当前场景里「" + LootBoxLabel(lootPlaceBox) + "」已有 " + same + " 个点位。" : "";
        EditorGUILayout.HelpBox(
            "先选一种箱子，点上方「进入放置」，Scene 里左键点击连续摆放，右键 / Esc 退出。\n" +
            "摆多少个都行（比如摆 144 个）——每一把进图时，会按地图编辑器里这种箱子的「最多几个」（比如 85）从这些点位里随机抽。\n" +
            "每种箱子只从自己的点位里抽，A 和 B 互不占用名额，所以别把不同种箱子的点位摆在同一个位置。" + extra,
            MessageType.Info);
    }

    private void OnLootPointPlacementSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1))
        {
            SetPlacementMode(enabled: false);
            e.Use();
            return;
        }
        if (!TryRaycastGround(e.mousePosition, out Vector3 hit))
            return;

        Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
        Handles.DrawWireDisc(hit, Vector3.up, 0.6f);
        Handles.DrawLine(hit, hit + Vector3.up * 1.4f);
        Handles.Label(hit + Vector3.up * 1.7f, "资源箱点位 · " + LootBoxLabel(lootPlaceBox));

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            PlaceLootPoint(hit);
            e.Use();
        }
        sceneView.Repaint();
    }

    private void PlaceLootPoint(Vector3 position)
    {
        if (lootPlaceBox == null)
            return;
        Transform parent = GetOrCreateParent(LootParentPath);
        var go = new GameObject("资源箱点位·" + LootBoxLabel(lootPlaceBox));
        Undo.RegisterCreatedObjectUndo(go, "Place Loot Point");
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        var lp = Undo.AddComponent<SkyPrisonLootPoint>(go);
        lp.box = lootPlaceBox;
        lp.weight = lootPlaceWeight;
        lp.zone = lootPlaceZone;
        EditorUtility.SetDirty(lp);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);   // 连续放置：不退出摆放模式
    }

    private void DrawLootPointPlacedPage()
    {
        EditorGUILayout.LabelField("已摆放资源箱点位", EditorStyles.boldLabel);
        var points = FindObjectsByType<SkyPrisonLootPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (points.Length == 0)
        {
            EditorGUILayout.HelpBox("场景里还没有资源箱点位。到「放置」页摆。", MessageType.Info);
            return;
        }

        lootPlacedScroll = EditorGUILayout.BeginScrollView(lootPlacedScroll);
        var groups = points.GroupBy(p => p.box).OrderBy(g => g.Key == null ? "" : g.Key.name).ToList();
        foreach (var grp in groups)
        {
            string name = LootBoxLabel(grp.Key);
            var list = grp.ToList();
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(name + "   × " + list.Count, EditorStyles.boldLabel);
            if (GUILayout.Button("选中这一类", GUILayout.Width(86f)))
                Selection.objects = list.Select(p => (Object)p.gameObject).ToArray();
            if (GUILayout.Button("定位", GUILayout.Width(52f)) && list.Count > 0)
            {
                var b = new Bounds(list[0].transform.position, Vector3.zero);
                foreach (var p in list) b.Encapsulate(p.transform.position);
                b.Expand(8f);
                SceneView.lastActiveSceneView?.Frame(b, false);
            }
            if (GUILayout.Button("删除这一类", GUILayout.Width(86f)) && EditorUtility.DisplayDialog("删除确认", "删除「" + name + "」的全部 " + list.Count + " 个点位？", "删除", "取消"))
            {
                foreach (var p in list) Undo.DestroyObjectImmediate(p.gameObject);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }
}
