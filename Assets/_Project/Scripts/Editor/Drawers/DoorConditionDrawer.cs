using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 门条件的编辑：先选"变量"或"任务标记"；选变量后，后面的输入按变量表里声明的类型自动变——
/// 布尔 → 真/假，整数 → 比较符 + 整数框，浮点数 → 比较符 + 小数框。
/// </summary>
[CustomPropertyDrawer(typeof(DoorCondition))]
public class DoorConditionDrawer : PropertyDrawer
{
    private const float Line = 18f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var kind = (DoorConditionKind)property.FindPropertyRelative("kind").enumValueIndex;
        return (kind == DoorConditionKind.QuestFlag ? 2 : 3) * Line + 6f;
    }

    private static LogicSlotValueType? DeclaredType(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var db = VariableDatabase.LoadOrNull();
        var d = db != null ? db.FindByName(name) : null;
        return d != null ? d.type : (LogicSlotValueType?)null;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var kindP = property.FindPropertyRelative("kind");
        var nameP = property.FindPropertyRelative("variableName");
        var boolP = property.FindPropertyRelative("boolValue");
        var opP = property.FindPropertyRelative("op");
        var numP = property.FindPropertyRelative("number");
        bool isFlag = kindP.intValue == (int)DoorConditionKind.QuestFlag;

        EditorGUI.DrawRect(new Rect(position.x, position.y + 2f, position.width, position.height - 4f), new Color(1f, 1f, 1f, 0.04f));
        float y = position.y + 3f;
        Rect Row() { var r = new Rect(position.x + 4f, y, position.width - 8f, Line - 2f); y += Line; return r; }

        // 第 1 行：条件类型（变量 / 任务标记）
        int sel = EditorGUI.Popup(Row(), "条件类型", isFlag ? 1 : 0, new[] { "变量", "任务标记" });
        if ((sel == 1) != isFlag)
        {
            kindP.intValue = sel == 1 ? (int)DoorConditionKind.QuestFlag : (int)DoorConditionKind.Variable;
            isFlag = sel == 1;
            nameP.stringValue = "";
        }

        // 第 2 行：变量名 / 标记 Key
        Rect nameRow = Row();
        Rect field = EditorGUI.PrefixLabel(nameRow, new GUIContent(isFlag ? "任务标记 Key" : "变量"));
        if (isFlag)
        {
            nameP.stringValue = EditorGUI.TextField(field, nameP.stringValue);
            return;
        }

        Rect open = new Rect(field.xMax - 22f, field.y, 22f, field.height);
        Rect drop = new Rect(open.x - 24f, field.y, 22f, field.height);
        var type = DeclaredType(nameP.stringValue);
        bool known = string.IsNullOrEmpty(nameP.stringValue) || type.HasValue;
        var oldCol = GUI.backgroundColor;
        if (!known) GUI.backgroundColor = new Color(1f, 0.5f, 0.4f);
        nameP.stringValue = EditorGUI.TextField(new Rect(field.x, field.y, field.width - 48f, field.height), nameP.stringValue);
        GUI.backgroundColor = oldCol;
        if (GUI.Button(drop, new GUIContent("▾", "从变量表选择（可搜索）"), EditorStyles.miniButton))
        {
            var so = property.serializedObject;
            string path = nameP.propertyPath;
            var dd = new VariableDropdown(new AdvancedDropdownState(), VariableDatabase.LoadOrNull(), n =>
            {
                so.Update();
                so.FindProperty(path).stringValue = n;
                so.ApplyModifiedProperties();
            });
            dd.Show(drop);
        }
        if (GUI.Button(open, new GUIContent("表", "打开变量表（新建/改名变量）"), EditorStyles.miniButton))
            SkyPrisonVariableTableWindow.Open();

        // 第 3 行：按变量类型给输入
        Rect valRow = Row();
        if (!type.HasValue)
        {
            EditorGUI.LabelField(valRow, string.IsNullOrEmpty(nameP.stringValue) ? "先选一个变量" : "变量表里找不到这个变量（当前按不满足处理）", EditorStyles.miniLabel);
            return;
        }

        string typeLabel = VariableDatabase.GetTypeLabel(type.Value);
        Rect lab = EditorGUI.PrefixLabel(valRow, new GUIContent("满足（" + typeLabel + "）"));
        if (type.Value == LogicSlotValueType.Bool)
        {
            int v = EditorGUI.Popup(lab, boolP.boolValue ? 0 : 1, new[] { "为 真 (true)", "为 假 (false)" });
            boolP.boolValue = v == 0;
        }
        else
        {
            float half = lab.width * 0.45f;
            EditorGUI.PropertyField(new Rect(lab.x, lab.y, half - 2f, lab.height), opP, GUIContent.none);
            var numRect = new Rect(lab.x + half + 2f, lab.y, lab.width - half - 2f, lab.height);
            if (type.Value == LogicSlotValueType.Int)
                numP.floatValue = EditorGUI.IntField(numRect, Mathf.RoundToInt(numP.floatValue));
            else
                numP.floatValue = EditorGUI.FloatField(numRect, numP.floatValue);
        }
    }

    private sealed class VariableDropdown : AdvancedDropdown
    {
        private sealed class Item : AdvancedDropdownItem
        {
            public string varName;
            public Item(string label, string n) : base(label) { varName = n; }
        }

        private readonly VariableDatabase db;
        private readonly System.Action<string> pick;

        public VariableDropdown(AdvancedDropdownState st, VariableDatabase d, System.Action<string> p) : base(st)
        {
            db = d; pick = p;
            minimumSize = new Vector2(260f, 280f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("变量");
            if (db == null) { root.AddChild(new AdvancedDropdownItem("找不到变量表") { enabled = false }); return root; }
            var list = db.variables.Where(v => v != null && !string.IsNullOrEmpty(v.variableName) &&
                (v.type == LogicSlotValueType.Bool || v.type == LogicSlotValueType.Int || v.type == LogicSlotValueType.Float)).ToList();
            if (list.Count == 0)
                root.AddChild(new AdvancedDropdownItem("变量表里没有布尔/整数/浮点数变量") { enabled = false });
            foreach (var v in list)
                root.AddChild(new Item(v.variableName + "  (" + VariableDatabase.GetTypeLabel(v.type) + ")", v.variableName));
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is Item i) pick(i.varName);
        }
    }
}
