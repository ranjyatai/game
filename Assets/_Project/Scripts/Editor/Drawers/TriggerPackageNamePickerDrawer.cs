using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>TriggerPackage 引用字段：按触发器页里的"显示名称"选择（没有显示名称才用文件名），并保留定位和清空。</summary>
[CustomPropertyDrawer(typeof(TriggerPackageNamePickerAttribute))]
public class TriggerPackageNamePickerDrawer : PropertyDrawer
{
    private static string NameOf(TriggerPackage p)
    {
        return string.IsNullOrWhiteSpace(p.displayName) ? p.name : p.displayName;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var cur = property.objectReferenceValue as TriggerPackage;
        Rect r = EditorGUI.PrefixLabel(position, label);
        float small = 22f;
        Rect btn = new Rect(r.x, r.y, r.width - small * 2f - 4f, r.height);
        Rect ping = new Rect(btn.xMax + 2f, r.y, small, r.height);
        Rect clear = new Rect(ping.xMax + 2f, r.y, small, r.height);

        string text = cur != null ? NameOf(cur) : "（无）";
        if (EditorGUI.DropdownButton(btn, new GUIContent(text, cur != null ? AssetDatabase.GetAssetPath(cur) : ""), FocusType.Keyboard))
        {
            var so = property.serializedObject;
            string path = property.propertyPath;
            var dd = new PackageDropdown(new AdvancedDropdownState(), cur, p => Set(so, path, p));
            dd.Show(btn);
        }
        using (new EditorGUI.DisabledScope(cur == null))
        {
            if (GUI.Button(ping, new GUIContent("◎", "在项目里定位"), EditorStyles.miniButton)) EditorGUIUtility.PingObject(cur);
            if (GUI.Button(clear, new GUIContent("×", "清空"), EditorStyles.miniButton)) Set(property.serializedObject, property.propertyPath, null);
        }
    }

    private static void Set(SerializedObject so, string path, TriggerPackage value)
    {
        so.Update();
        so.FindProperty(path).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }

    // 带搜索框的选择器：打字直接过滤（搜索时按显示名称平铺），不搜索时按资源所在文件夹分组。
    private sealed class PackageDropdown : AdvancedDropdown
    {
        private sealed class Item : AdvancedDropdownItem
        {
            public TriggerPackage pkg;
            public Item(string name, TriggerPackage p) : base(name) { pkg = p; }
        }

        private readonly TriggerPackage current;
        private readonly System.Action<TriggerPackage> onPick;

        public PackageDropdown(AdvancedDropdownState state, TriggerPackage cur, System.Action<TriggerPackage> pick) : base(state)
        {
            current = cur; onPick = pick;
            minimumSize = new Vector2(280f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("触发器");
            root.AddChild(new Item("（无）", null));
            var all = AssetDatabase.FindAssets("t:TriggerPackage")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(path => (path, pkg: AssetDatabase.LoadAssetAtPath<TriggerPackage>(path)))
                .Where(x => x.pkg != null).ToList();

            // 去掉所有触发包共有的文件夹前缀，只留有区分度的部分当分组名。
            string common = "";
            if (all.Count > 0)
            {
                var dirs = all.Select(x => System.IO.Path.GetDirectoryName(x.path).Replace("\\", "/")).ToList();
                var first = dirs[0].Split('/');
                int n = first.Length;
                foreach (var d in dirs)
                {
                    var parts = d.Split('/');
                    int k = 0;
                    while (k < n && k < parts.Length && parts[k] == first[k]) k++;
                    n = k;
                }
                common = string.Join("/", first.Take(n));
            }

            var groups = new Dictionary<string, AdvancedDropdownItem>();
            foreach (var x in all.OrderBy(x => NameOf(x.pkg)))
            {
                string dir = System.IO.Path.GetDirectoryName(x.path).Replace("\\", "/");
                string rel = dir.Length > common.Length ? dir.Substring(common.Length).Trim('/') : "";
                AdvancedDropdownItem parent = root;
                if (rel.Length > 0)
                {
                    string acc = "";
                    foreach (var seg in rel.Split('/'))
                    {
                        acc += "/" + seg;
                        if (!groups.TryGetValue(acc, out var g))
                        {
                            g = new AdvancedDropdownItem(seg);
                            parent.AddChild(g);
                            groups[acc] = g;
                        }
                        parent = g;
                    }
                }
                parent.AddChild(new Item(NameOf(x.pkg), x.pkg) { id = x.pkg.GetInstanceID() });
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is Item i) onPick(i.pkg);
        }
    }
}
