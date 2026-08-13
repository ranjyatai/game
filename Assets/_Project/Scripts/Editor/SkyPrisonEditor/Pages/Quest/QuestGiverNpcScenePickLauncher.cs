#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "归属NPC"字段的场景点选入口——自成一套(不再借用AI行为树编辑器的
/// AIScenePickCoordinator，那套要求目标必须提前手动挂 SkyPrisonSceneUnitMarker
/// 才能被扫描到，用户明确要求"只要是个单位就该能拾取"，不该有这个前置门槛)。
/// 直接扫描场景里所有 UnitDefinitionRuntimeBinder(玩家/NPC/敌人只要绑定了
/// UnitDefinition 资产就有这个组件，不需要额外准备)，点选的那一刻如果目标身上
/// 还没有 SkyPrisonSceneUnitMarker 就顺手补一个(保证以后需要稳定GUID的功能，比如
/// "引导玩家去找这个NPC"，一样能用)。
/// </summary>
public static class QuestGiverNpcScenePickLauncher
{
    public class Result
    {
        public UnitDefinition unitDefinition;
        public string sceneName;
        public string scenePath;
        public string sceneUnitGuid;
    }

    private static Action<Result> _onPicked;
    private static bool _picking;
    private static bool _hooked;

    public static bool Begin(Action<Result> onPicked)
    {
        if (onPicked == null) return false;
        if (_picking) return false;

        _onPicked = onPicked;
        _picking = true;
        EnsureHooked();

        FocusSceneView();
        return true;
    }

    public static void Cancel()
    {
        _picking = false;
        _onPicked = null;
        InternalEditorUtility_RepaintAllViews();
    }

    private static void EnsureHooked()
    {
        if (_hooked) return;
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        _hooked = true;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!_picking) return;

        Event e = Event.current;
        if (e == null) return;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            Cancel();
            e.Use();
            sceneView.Repaint();
            return;
        }

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        // 直接扫 UnitDefinitionRuntimeBinder——玩家/NPC/敌人只要绑了UnitDefinition
        // 资产就有这个组件，不要求提前手动准备任何标记组件。
        UnitDefinitionRuntimeBinder[] binders = UnityEngine.Object.FindObjectsByType<UnitDefinitionRuntimeBinder>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        Handles.BeginGUI();

        Rect hintRect = new Rect(12f, 12f, 560f, 60f);
        GUILayout.BeginArea(hintRect, "SkyPrison 场景单位选择", GUI.skin.window);
        GUILayout.Label("请在场景中点击任务归属的NPC。ESC 取消。");
        GUILayout.Label($"已找到 {binders.Length} 个单位。", EditorStyles.miniLabel);
        GUILayout.EndArea();

        Vector2 mouse = e.mousePosition;
        var labelStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

        foreach (UnitDefinitionRuntimeBinder binder in binders)
        {
            if (binder == null || binder.UnitDefinitionAsset == null) continue;
            if (!TryGetScreenRect(binder.transform, out Rect rect)) continue;

            bool hover = rect.Contains(mouse);
            DrawHotspot(rect, hover);

            Rect labelRect = new Rect(rect.x - 40f, rect.y - 20f, rect.width + 80f, 18f);
            string label = !string.IsNullOrWhiteSpace(binder.UnitDefinitionAsset.displayName)
                ? binder.UnitDefinitionAsset.displayName : binder.UnitDefinitionAsset.name;
            GUI.Label(labelRect, label, labelStyle);

            if (hover && e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Selection.activeGameObject = binder.gameObject;
                EditorGUIUtility.PingObject(binder.gameObject);

                Result result = BuildResult(binder);
                _picking = false;
                Action<Result> callback = _onPicked;
                _onPicked = null;

                e.Use();
                Handles.EndGUI();
                sceneView.Repaint();

                callback?.Invoke(result);
                return;
            }
        }

        Handles.EndGUI();
        sceneView.Repaint();
    }

    private static Result BuildResult(UnitDefinitionRuntimeBinder binder)
    {
        GameObject go = binder.gameObject;

        // 点选的那一刻如果还没挂标记组件就顺手补一个——保证SceneUnitGuid这类需要
        // 跨会话保持稳定的信息以后有得用，不需要策划再手动补一遍。
        SkyPrisonSceneUnitMarker marker = go.GetComponent<SkyPrisonSceneUnitMarker>()
            ?? go.GetComponentInParent<SkyPrisonSceneUnitMarker>();
        if (marker == null)
        {
            marker = go.AddComponent<SkyPrisonSceneUnitMarker>();
            EditorUtility.SetDirty(go);
        }
        marker.RefreshBindingCache();

        return new Result
        {
            unitDefinition = binder.UnitDefinitionAsset,
            sceneName = go.scene.name,
            scenePath = go.scene.path,
            sceneUnitGuid = marker.SceneUnitGuid
        };
    }

    private static bool TryGetScreenRect(Transform root, out Rect rect)
    {
        rect = default;
        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds;
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            bounds = new Bounds(root.position, Vector3.one);
        }

        Vector3[] corners =
        {
            bounds.min,
            new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
            new Vector3(bounds.min.x, bounds.max.y, bounds.min.z),
            new Vector3(bounds.max.x, bounds.max.y, bounds.min.z),
            new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
            new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
            new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
            bounds.max,
        };

        bool hasPoint = false;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 gui = HandleUtility.WorldToGUIPoint(corners[i]);
            if (float.IsNaN(gui.x) || float.IsNaN(gui.y)) continue;
            hasPoint = true;
            xMin = Mathf.Min(xMin, gui.x); yMin = Mathf.Min(yMin, gui.y);
            xMax = Mathf.Max(xMax, gui.x); yMax = Mathf.Max(yMax, gui.y);
        }
        if (!hasPoint) return false;

        const float padding = 8f;
        rect = Rect.MinMaxRect(xMin - padding, yMin - padding, xMax + padding, yMax + padding);
        if (rect.width < 24f) rect.width = 24f;
        if (rect.height < 24f) rect.height = 24f;
        return true;
    }

    private static void DrawHotspot(Rect rect, bool hover)
    {
        Color fill = hover ? new Color(0.20f, 0.85f, 1f, 0.18f) : new Color(0.20f, 0.85f, 1f, 0.04f);
        Color border = hover ? new Color(0.20f, 0.85f, 1f, 0.95f) : new Color(0.20f, 0.85f, 1f, 0.35f);
        EditorGUI.DrawRect(rect, fill);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), border);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), border);
    }

    private static void FocusSceneView()
    {
        EditorApplication.delayCall += () =>
        {
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Show();
                SceneView.lastActiveSceneView.Focus();
                SceneView.lastActiveSceneView.Repaint();
            }
        };
    }

    private static void InternalEditorUtility_RepaintAllViews()
    {
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
}
#endif
