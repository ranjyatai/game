using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>各放置页共用的"多选类型筛选"：下拉面板里逐项打勾，点选后面板不关；空集合 = 全部。</summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private readonly HashSet<string> grayboxFilter = new HashSet<string>();
    private readonly HashSet<string> terrainCategoryFilter = new HashSet<string>();
    private readonly HashSet<string> surfaceCategoryFilter = new HashSet<string>();
    private readonly HashSet<string> itemCategoryFilter = new HashSet<string>();
    private readonly HashSet<string> unitTypeFilter = new HashSet<string>();

    private static bool PassMultiFilter(HashSet<string> filter, string value)
    {
        return filter.Count == 0 || filter.Contains(value ?? "");
    }

    /// <summary>枚举的显示名：优先 [InspectorName]，没有就用枚举名。</summary>
    private static string EnumFilterLabel(System.Enum e)
    {
        var f = e.GetType().GetField(e.ToString());
        if (f != null)
        {
            var attrs = f.GetCustomAttributes(typeof(InspectorNameAttribute), false);
            if (attrs.Length > 0) return ((InspectorNameAttribute)attrs[0]).displayName;
        }
        return e.ToString();
    }

    private void DrawMultiFilter(string title, HashSet<string> filter, List<string> options)
    {
        options = options.Where(o => !string.IsNullOrEmpty(o)).Distinct().OrderBy(o => o).ToList();
        // 选项里已经不存在的旧勾选清掉，免得列表被一个看不见的条件筛空。
        filter.RemoveWhere(f => !options.Contains(f));
        string label = filter.Count == 0 ? "全部" : string.Join("、", filter.OrderBy(x => x));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(title);
        Rect btn = GUILayoutUtility.GetRect(new GUIContent(label), EditorStyles.popup);
        if (EditorGUI.DropdownButton(btn, new GUIContent(label), FocusType.Keyboard))
            PopupWindow.Show(btn, new MultiSelectPopup(options, filter, btn.width, this));
        EditorGUILayout.EndHorizontal();
    }

    private sealed class MultiSelectPopup : PopupWindowContent
    {
        private readonly List<string> options; private readonly HashSet<string> sel;
        private readonly float width; private readonly EditorWindow owner;
        public MultiSelectPopup(List<string> o, HashSet<string> s, float w, EditorWindow ow) { options = o; sel = s; width = w; owner = ow; }
        public override Vector2 GetWindowSize() { return new Vector2(Mathf.Max(160f, width), 22f * (options.Count + 1) + 8f); }
        public override void OnGUI(Rect rect)
        {
            bool all = sel.Count == 0;
            if (EditorGUILayout.ToggleLeft("全部", all, EditorStyles.boldLabel) && !all) { sel.Clear(); owner.Repaint(); }
            foreach (string o in options)
            {
                bool on = sel.Contains(o);
                bool now = EditorGUILayout.ToggleLeft(o, on);
                if (now != on) { if (now) sel.Add(o); else sel.Remove(o); owner.Repaint(); }
            }
        }
    }
}
