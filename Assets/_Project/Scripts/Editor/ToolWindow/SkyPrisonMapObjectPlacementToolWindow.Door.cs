using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 放置设置面板里的"门 / 开关属性"区：Scene 里选中已放置的门或开关时，直接在这里编辑它的全部属性，
/// 并能选择由哪个开关控制这扇门。选的是门的"定义"（还没放）时只给出提示。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private void OnSelectionChange() { Repaint(); }

    // 优先 Unity 的选中；已摆放列表里的高亮行不一定同步到 Unity Selection，所以再看列表的选中集合。
    private GameObject GetPlacedSelectionGameObject()
    {
        var go = Selection.activeGameObject;
        if (go != null) return go;
        if (placedSelectionIds.Count == 1)
            foreach (int id in placedSelectionIds)
                return EditorUtility.InstanceIDToObject(id) as GameObject;
        return null;
    }

    /// <summary>已摆放页：有选中物体时给一个"属性窗口"按钮。</summary>
    private void DrawDoorPropertiesOnPlacedPage()
    {
        var go = GetPlacedSelectionGameObject();
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(go == null))
        {
            if (GUILayout.Button(go != null ? "属性窗口：" + go.name : "属性窗口（先选中一个物体）", GUILayout.Height(24f)))
                ObjectPropertiesWindow.Open(this);
        }
        EditorGUILayout.EndHorizontal();
    }

    // 独立窗口：跟随当前选中物体，列出它（含子节点）上所有脚本组件的属性；可锁定。
    private sealed class ObjectPropertiesWindow : EditorWindow
    {
        private SkyPrisonMapObjectPlacementToolWindow owner;
        private GameObject lockedGo;
        private bool locked;
        private Vector2 scroll;
        private readonly System.Collections.Generic.Dictionary<int, Editor> editors = new System.Collections.Generic.Dictionary<int, Editor>();
        private readonly System.Collections.Generic.Dictionary<int, bool> folds = new System.Collections.Generic.Dictionary<int, bool>();

        public static void Open(SkyPrisonMapObjectPlacementToolWindow o)
        {
            var w = GetWindow<ObjectPropertiesWindow>(true, "物体属性", true);
            w.owner = o;
            w.minSize = new Vector2(380f, 460f);
            w.Show();
        }

        private void OnSelectionChange() { Repaint(); }
        private void OnDisable() { ClearEditors(); }

        private void ClearEditors()
        {
            foreach (var e in editors.Values) if (e != null) DestroyImmediate(e);
            editors.Clear();
        }

        private void OnGUI()
        {
            if (owner == null) { Close(); return; }
            var go = locked && lockedGo != null ? lockedGo : owner.GetPlacedSelectionGameObject();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(go != null ? go.name : "（未选中物体）", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                bool nl = GUILayout.Toggle(locked, "锁定", EditorStyles.toolbarButton, GUILayout.Width(50f));
                if (nl != locked) { locked = nl; lockedGo = nl ? go : null; }
            }
            if (go == null) return;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            var comps = go.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null).ToArray();
            bool any = false;
            foreach (var c in comps)
            {
                if (c is TerrainDecorationRuntimeBinder) continue;
                int id = c.GetInstanceID();
                if (!folds.TryGetValue(id, out bool open)) open = c is SkyPrisonSlidingDoor || c is SkyPrisonDoorSwitch;
                bool hasProps = new SerializedObject(c).GetIterator().NextVisible(true);
                if (!hasProps) continue;
                any = true;
                open = EditorGUILayout.Foldout(open, ComponentTitle(c.GetType()), true, EditorStyles.foldoutHeader);
                folds[id] = open;
                if (open)
                {
                    if (!editors.TryGetValue(id, out var ed) || ed == null || ed.target != c)
                    {
                        ed = Editor.CreateEditor(c);
                        editors[id] = ed;
                    }
                    if (c is SkyPrisonSlidingDoor || c is SkyPrisonDoorSwitch) DrawChineseInspector(ed.serializedObject, c.GetType());
                    else ed.OnInspectorGUI();
                    if (c is SkyPrisonSlidingDoor door) owner.DrawDoorBindingSection(door);
                    else if (c is SkyPrisonDoorSwitch sw) owner.DrawSwitchBindingHint(sw);
                }
                EditorGUILayout.Space(2f);
            }
            if (!any) EditorGUILayout.HelpBox("这个物体上没有可编辑的脚本属性。", MessageType.None);
            EditorGUILayout.EndScrollView();
        }
    }

    private static string ComponentTitle(System.Type t)
    {
        if (t == typeof(SkyPrisonSlidingDoor)) return "门（SkyPrisonSlidingDoor）";
        if (t == typeof(SkyPrisonDoorSwitch)) return "门开关（SkyPrisonDoorSwitch）";
        return t.Name;
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> FieldLabelsZh = new System.Collections.Generic.Dictionary<string, string>
    {
        // 门
        { "triggerMode", "触发方式" }, { "closeMode", "关闭方式" }, { "holdSeconds", "定时关秒数" },
        { "triggerRadius", "触发半径" }, { "closeRadius", "关门半径" }, { "closeDelay", "关门延迟" },
        { "openLabel", "开门提示文字" }, { "closeLabel", "关门提示文字" },
        { "locked", "锁住" }, { "unlockConditions", "解锁条件" }, { "conditionMatch", "条件关系" }, { "lockedLabel", "锁定提示文字" },
        { "slideSign", "滑动方向（±1）" }, { "slideAxisLocal", "滑动轴" }, { "openDistanceOverride", "开门距离" },
        { "slideSpeed", "滑动速度" }, { "colliderOffAfter01", "提前关碰撞比例" },
        { "audioProfile", "音效配置" }, { "persistId", "存档 ID" },
        { "onOpenedTrigger", "开门到位触发包" }, { "onClosedTrigger", "关门到位触发包" }, { "startOpen", "初始开启" },
        // 开关
        { "doors", "绑定的门" }, { "command", "命令" }, { "seconds", "持续秒数" },
        { "unlockDoors", "同时解锁门" }, { "singleUse", "只能按一次" }, { "label", "提示文字" },
    };

    private static string TooltipOf(System.Type t, string field)
    {
        var f = t.GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (f == null) return "";
        var attrs = f.GetCustomAttributes(typeof(TooltipAttribute), false);
        return attrs.Length > 0 ? ((TooltipAttribute)attrs[0]).tooltip : "";
    }

    /// <summary>门/开关专用：字段名换成中文，其余照常（Header、Tooltip、自定义绘制器都保留）。</summary>
    private static void DrawChineseInspector(SerializedObject so, System.Type type)
    {
        so.Update();
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.propertyPath == "m_Script") continue;
            string zh;
            var content = FieldLabelsZh.TryGetValue(it.name, out zh)
                ? new GUIContent(zh, TooltipOf(type, it.name))
                : new GUIContent(it.displayName, TooltipOf(type, it.name));
            EditorGUILayout.PropertyField(it, content, true);
        }
        so.ApplyModifiedProperties();
    }

    private void DrawDoorBindingSection(SkyPrisonSlidingDoor door)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("控制此门的开关", EditorStyles.boldLabel);

        var all = FindObjectsByType<SkyPrisonDoorSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var bound = all.Where(s => s.doors != null && s.doors.Contains(door)).ToArray();

        if (bound.Length == 0)
            EditorGUILayout.HelpBox("没有开关绑定这扇门。触发方式选\"由远处的开关控制\"后，在下面绑定一个开关。", MessageType.None);

        foreach (var s in bound)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(s.name, GUILayout.MinWidth(40f));
                if (GUILayout.Button("选中", GUILayout.Width(44f)))
                {
                    Selection.activeGameObject = s.gameObject;
                    SceneView.lastActiveSceneView?.Frame(new Bounds(s.transform.position, Vector3.one * 4f), false);
                }
                if (GUILayout.Button("解绑", GUILayout.Width(44f)))
                {
                    Undo.RecordObject(s, "解绑门开关");
                    s.doors.Remove(door);
                    EditorUtility.SetDirty(s);
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("绑定已有开关…"))
            {
                var menu = new GenericMenu();
                foreach (var s in all.OrderBy(x => x.name))
                {
                    var sCopy = s;
                    bool already = s.doors != null && s.doors.Contains(door);
                    if (already) menu.AddDisabledItem(new GUIContent(s.name + "（已绑定）"));
                    else menu.AddItem(new GUIContent(s.name), false, () =>
                    {
                        Undo.RecordObject(sCopy, "绑定门开关");
                        if (sCopy.doors == null) sCopy.doors = new System.Collections.Generic.List<SkyPrisonSlidingDoor>();
                        sCopy.doors.Add(door);
                        EditorUtility.SetDirty(sCopy);
                        // 绑定后门应该听开关的，不然开关按了没反应。
                        if (door.TriggerMode != DoorTriggerMode.Switch)
                        {
                            Undo.RecordObject(door, "门改为开关控制");
                            door.SetTriggerMode(DoorTriggerMode.Switch);
                            EditorUtility.SetDirty(door);
                        }
                    });
                }
                if (all.Length == 0) menu.AddDisabledItem(new GUIContent("场景里还没有开关（摆放工具选 机关 → 门开关）"));
                menu.ShowAsContext();
            }
        }
    }

    private void DrawSwitchBindingHint(SkyPrisonDoorSwitch sw)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox("把门拖进上面的 Doors 列表，或选中门后在这里面板用\"绑定已有开关\"。被绑定的门会自动改为\"由远处的开关控制\"，Scene 里选中开关会画出到各扇门的连线。", MessageType.None);
        if (GUILayout.Button("选中所有被控制的门"))
        {
            var gos = sw.doors.Where(d => d != null).Select(d => (Object)d.gameObject).ToArray();
            if (gos.Length > 0) Selection.objects = gos;
        }
    }
}
