using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DropProfile))]
public class DropProfileEditor : Editor
{
    private SerializedProperty profileIdProp;
    private SerializedProperty displayNameProp;
    private SerializedProperty noteProp;
    private SerializedProperty poolsProp;

    private bool showAdvancedHelp = true;

    private void OnEnable()
    {
        profileIdProp = serializedObject.FindProperty("profileId");
        displayNameProp = serializedObject.FindProperty("displayName");
        noteProp = serializedObject.FindProperty("note");
        poolsProp = serializedObject.FindProperty("pools");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawBaseInfo();
        EditorGUILayout.Space(8);

        DrawPoolSection();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawBaseInfo()
    {
        EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(profileIdProp, new GUIContent("掉落表 ID"));
        EditorGUILayout.PropertyField(displayNameProp, new GUIContent("显示名称"));
        EditorGUILayout.PropertyField(noteProp, new GUIContent("备注"));

        showAdvancedHelp = EditorGUILayout.Foldout(showAdvancedHelp, "使用说明", true);
        if (showAdvancedHelp)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.HelpBox(
                "掉落表里可以放多个“物品掉落池”。\n" +
                "每个池都像一个独立的小规则组。\n" +
                "你可以把常规物资、稀有奖励、特殊掉落分成不同池子来管理。",
                MessageType.Info
            );
            EditorGUILayout.EndVertical();
        }
    }

    private void DrawPoolSection()
    {
        EditorGUILayout.LabelField("物品掉落池", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "这里不是一条条平铺掉落，而是一池一池地配置。\n" +
            "这样更适合做搜打撤里的常规掉落池、稀有池、奖励池。",
            MessageType.Info
        );

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加掉落池"))
            poolsProp.arraySize++;

        if (GUILayout.Button("清空掉落池"))
        {
            if (EditorUtility.DisplayDialog("清空掉落池", "确定清空所有掉落池吗？", "确定", "取消"))
                poolsProp.ClearArray();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        for (int i = 0; i < poolsProp.arraySize; i++)
        {
            SerializedProperty pool = poolsProp.GetArrayElementAtIndex(i);

            SerializedProperty enabledProp = pool.FindPropertyRelative("enabled");
            SerializedProperty poolNameProp = pool.FindPropertyRelative("poolName");
            SerializedProperty poolNoteProp = pool.FindPropertyRelative("poolNote");
            SerializedProperty poolModeProp = pool.FindPropertyRelative("poolMode");
            SerializedProperty pickCountProp = pool.FindPropertyRelative("pickCount");
            SerializedProperty entriesProp = pool.FindPropertyRelative("entries");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"掉落池 {i + 1}", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(enabledProp, new GUIContent("启用"));
            EditorGUILayout.PropertyField(poolNameProp, new GUIContent("池名称"));
            EditorGUILayout.PropertyField(poolNoteProp, new GUIContent("池备注"));
            EditorGUILayout.PropertyField(poolModeProp, new GUIContent("池模式"));

            DropPoolMode mode = (DropPoolMode)poolModeProp.enumValueIndex;

            switch (mode)
            {
                case DropPoolMode.IndependentRolls:
                    EditorGUILayout.HelpBox(
                        "独立判定：池里的每个物品都会各自单独计算掉率。\n" +
                        "A 掉了不会影响 B。",
                        MessageType.Info
                    );
                    break;

                case DropPoolMode.WeightedPick:
                    EditorGUILayout.HelpBox(
                        "抽选池：会按权重从这个池里抽取结果。\n" +
                        "这是高级功能，默认不一定要用。",
                        MessageType.Warning
                    );
                    EditorGUILayout.PropertyField(pickCountProp, new GUIContent("抽取次数"));
                    break;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("池内物品", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "这里放这个池里的具体掉落物品。",
                MessageType.None
            );

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加物品"))
                entriesProp.arraySize++;

            if (GUILayout.Button("清空物品"))
            {
                if (EditorUtility.DisplayDialog("清空池内物品", "确定清空这个池里的所有物品吗？", "确定", "取消"))
                    entriesProp.ClearArray();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            for (int e = 0; e < entriesProp.arraySize; e++)
            {
                SerializedProperty entry = entriesProp.GetArrayElementAtIndex(e);

                SerializedProperty entryEnabledProp = entry.FindPropertyRelative("enabled");
                SerializedProperty itemDefinitionProp = entry.FindPropertyRelative("itemDefinition");
                SerializedProperty dropChanceProp = entry.FindPropertyRelative("dropChance");
                SerializedProperty weightProp = entry.FindPropertyRelative("weight");
                SerializedProperty minCountProp = entry.FindPropertyRelative("minCount");
                SerializedProperty maxCountProp = entry.FindPropertyRelative("maxCount");
                SerializedProperty noteProp = entry.FindPropertyRelative("note");

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"物品 {e + 1}", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(entryEnabledProp, new GUIContent("启用"));
                EditorGUILayout.PropertyField(itemDefinitionProp, new GUIContent("物品定义"));

                if (mode == DropPoolMode.IndependentRolls)
                    EditorGUILayout.PropertyField(dropChanceProp, new GUIContent("掉落率"));
                else
                    EditorGUILayout.PropertyField(weightProp, new GUIContent("抽选权重"));

                EditorGUILayout.PropertyField(minCountProp, new GUIContent("最小数量"));
                EditorGUILayout.PropertyField(maxCountProp, new GUIContent("最大数量"));
                EditorGUILayout.PropertyField(noteProp, new GUIContent("备注"));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("上移") && e > 0)
                    entriesProp.MoveArrayElement(e, e - 1);

                if (GUILayout.Button("下移") && e < entriesProp.arraySize - 1)
                    entriesProp.MoveArrayElement(e, e + 1);

                if (GUILayout.Button("删除"))
                {
                    entriesProp.DeleteArrayElementAtIndex(e);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("上移") && i > 0)
                poolsProp.MoveArrayElement(i, i - 1);

            if (GUILayout.Button("下移") && i < poolsProp.arraySize - 1)
                poolsProp.MoveArrayElement(i, i + 1);

            if (GUILayout.Button("删除掉落池"))
            {
                poolsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }
    }
}
