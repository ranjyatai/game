using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UnitSpawner))]
public class UnitSpawnerEditor : Editor
{
    private SerializedProperty spawnerNoteProp;

    private SerializedProperty spawnAreaTypeProp;
    private SerializedProperty boxAreaProp;
    private SerializedProperty sphereAreaProp;

    private SerializedProperty spawnParentProp;
    private SerializedProperty useSpawnerTransformProp;
    private SerializedProperty spawnPositionOffsetProp;
    private SerializedProperty spawnEulerOffsetProp;

    private SerializedProperty currentStageProp;
    private SerializedProperty spawnGroupsProp;

    private SerializedProperty autoFindRangeOnAwakeProp;
    private SerializedProperty applyDefinitionAfterSpawnProp;
    private SerializedProperty useGroupDropProfileOnSpawnedUnitProp;

    private SerializedProperty drawSpawnAreaGizmoProp;
    private SerializedProperty drawPointGizmoProp;
    private SerializedProperty spawnAreaColorProp;
    private SerializedProperty pointColorProp;
    private SerializedProperty aliveCheckColorProp;

    private SerializedProperty debugLogsProp;
    private SerializedProperty spawnedInstancesProp;

    private bool showAdvancedModule = false;
    private bool showRuntimeDebug = false;

    private void OnEnable()
    {
        spawnerNoteProp = serializedObject.FindProperty("spawnerNote");

        spawnAreaTypeProp = serializedObject.FindProperty("spawnAreaType");
        boxAreaProp = serializedObject.FindProperty("boxArea");
        sphereAreaProp = serializedObject.FindProperty("sphereArea");

        spawnParentProp = serializedObject.FindProperty("spawnParent");
        useSpawnerTransformProp = serializedObject.FindProperty("useSpawnerTransform");
        spawnPositionOffsetProp = serializedObject.FindProperty("spawnPositionOffset");
        spawnEulerOffsetProp = serializedObject.FindProperty("spawnEulerOffset");

        currentStageProp = serializedObject.FindProperty("currentStage");
        spawnGroupsProp = serializedObject.FindProperty("spawnGroups");

        autoFindRangeOnAwakeProp = serializedObject.FindProperty("autoFindRangeOnAwake");
        applyDefinitionAfterSpawnProp = serializedObject.FindProperty("applyDefinitionAfterSpawn");
        useGroupDropProfileOnSpawnedUnitProp = serializedObject.FindProperty("useGroupDropProfileOnSpawnedUnit");

        drawSpawnAreaGizmoProp = serializedObject.FindProperty("drawSpawnAreaGizmo");
        drawPointGizmoProp = serializedObject.FindProperty("drawPointGizmo");
        spawnAreaColorProp = serializedObject.FindProperty("spawnAreaColor");
        pointColorProp = serializedObject.FindProperty("pointColor");
        aliveCheckColorProp = serializedObject.FindProperty("aliveCheckColor");

        debugLogsProp = serializedObject.FindProperty("debugLogs");
        spawnedInstancesProp = serializedObject.FindProperty("spawnedInstances");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        UnitSpawner spawner = (UnitSpawner)target;

        DrawBaseInfoSection();
        EditorGUILayout.Space(8);

        DrawSpawnAreaSection();
        EditorGUILayout.Space(8);

        DrawSpawnBehaviorSection();
        EditorGUILayout.Space(8);

        DrawGroupsSection(spawner);
        EditorGUILayout.Space(8);

        DrawAdvancedModule(spawner);
        EditorGUILayout.Space(8);

        DrawUtilityButtons(spawner);
        EditorGUILayout.Space(8);

        DrawRuntimeDebugFoldout();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawBaseInfoSection()
    {
        EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(spawnerNoteProp, new GUIContent("生成器备注"));

        EditorGUILayout.HelpBox(
            "这个孵化器现在以“刷怪组”为核心。\n" +
            "每个刷怪组可以有自己的一套单位池、时钟、数量上限和掉落配置。",
            MessageType.Info
        );
    }

    private void DrawSpawnAreaSection()
    {
        EditorGUILayout.LabelField("生成区域", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(spawnAreaTypeProp, new GUIContent("生成区域类型"));

        UnitSpawner.SpawnAreaType type = (UnitSpawner.SpawnAreaType)spawnAreaTypeProp.enumValueIndex;

        switch (type)
        {
            case UnitSpawner.SpawnAreaType.Point:
                EditorGUILayout.HelpBox("点生成：单位会直接出现在孵化器位置。", MessageType.Info);
                break;

            case UnitSpawner.SpawnAreaType.BoxArea:
                EditorGUILayout.HelpBox("矩形范围：单位会在 BoxRange 的范围里随机出现。", MessageType.Info);
                EditorGUILayout.PropertyField(boxAreaProp, new GUIContent("Box 范围"));
                break;

            case UnitSpawner.SpawnAreaType.SphereArea:
                EditorGUILayout.HelpBox("球形范围：单位会在 SphereRange 的范围里随机出现。", MessageType.Info);
                EditorGUILayout.PropertyField(sphereAreaProp, new GUIContent("Sphere 范围"));
                break;
        }

        EditorGUILayout.PropertyField(useSpawnerTransformProp, new GUIContent("使用孵化器 Transform"));
        EditorGUILayout.PropertyField(spawnPositionOffsetProp, new GUIContent("生成位置偏移"));
        EditorGUILayout.PropertyField(spawnEulerOffsetProp, new GUIContent("生成角度偏移"));
    }

    private void DrawSpawnBehaviorSection()
    {
        EditorGUILayout.LabelField("通用生成行为", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(spawnParentProp, new GUIContent("生成父级"));
        EditorGUILayout.PropertyField(autoFindRangeOnAwakeProp, new GUIContent("启动时自动寻找范围"));
        EditorGUILayout.PropertyField(applyDefinitionAfterSpawnProp, new GUIContent("生成后应用 UnitDefinition"));
        EditorGUILayout.PropertyField(useGroupDropProfileOnSpawnedUnitProp, new GUIContent("生成时预留掉落表接口"));

        EditorGUILayout.HelpBox(
            "这里是整个孵化器共用的设置。\n" +
            "更具体的刷怪规则，请到下面的“刷怪组”里设置。",
            MessageType.None
        );
    }

    private void DrawGroupsSection(UnitSpawner spawner)
    {
        EditorGUILayout.LabelField("刷怪组", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "刷怪组可以理解为“一小组一小组的敌群配置”。\n" +
            "每组都可以单独设置：刷什么、多久刷、最多存在多少、掉什么。",
            MessageType.Info
        );

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加刷怪组"))
            spawnGroupsProp.arraySize++;

        if (GUILayout.Button("清空刷怪组"))
        {
            if (EditorUtility.DisplayDialog("清空刷怪组", "确定清空所有刷怪组吗？", "确定", "取消"))
                spawnGroupsProp.ClearArray();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        for (int i = 0; i < spawnGroupsProp.arraySize; i++)
        {
            SerializedProperty group = spawnGroupsProp.GetArrayElementAtIndex(i);

            SerializedProperty enabledProp = group.FindPropertyRelative("enabled");
            SerializedProperty groupNameProp = group.FindPropertyRelative("groupName");
            SerializedProperty groupNoteProp = group.FindPropertyRelative("groupNote");

            SerializedProperty useStageLimitProp = group.FindPropertyRelative("useStageLimit");
            SerializedProperty minStageProp = group.FindPropertyRelative("minStage");
            SerializedProperty maxStageProp = group.FindPropertyRelative("maxStage");

            SerializedProperty spawnClockModeProp = group.FindPropertyRelative("spawnClockMode");
            SerializedProperty spawnCountPerTickProp = group.FindPropertyRelative("spawnCountPerTick");
            SerializedProperty spawnIntervalUnitProp = group.FindPropertyRelative("spawnIntervalUnit");
            SerializedProperty spawnIntervalValueProp = group.FindPropertyRelative("spawnIntervalValue");

            SerializedProperty useSpawnCountLimitProp = group.FindPropertyRelative("useSpawnCountLimit");
            SerializedProperty maxAliveCountProp = group.FindPropertyRelative("maxAliveCountInCheckRange");
            SerializedProperty aliveCheckBoxProp = group.FindPropertyRelative("aliveCheckBox");
            SerializedProperty aliveCheckSphereProp = group.FindPropertyRelative("aliveCheckSphere");
            SerializedProperty countOnlyBoundUnitsProp = group.FindPropertyRelative("countOnlyBoundUnits");

            SerializedProperty dropProfileProp = group.FindPropertyRelative("dropProfile");
            SerializedProperty overrideDropModeProp = group.FindPropertyRelative("overrideDropMode");
            SerializedProperty groupConditionProp = group.FindPropertyRelative("groupCondition");

            SerializedProperty candidatesProp = group.FindPropertyRelative("candidates");
            SerializedProperty runtimeSpawnTimerProp = group.FindPropertyRelative("runtimeSpawnTimer");
            SerializedProperty runtimeAliveCountProp = group.FindPropertyRelative("runtimeAliveCount");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"刷怪组 {i + 1}", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(enabledProp, new GUIContent("启用"));
            EditorGUILayout.PropertyField(groupNameProp, new GUIContent("组名称"));
            EditorGUILayout.PropertyField(groupNoteProp, new GUIContent("组备注"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("组基础规则", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(groupConditionProp, new GUIContent("组条件脚本接口"));

            EditorGUILayout.PropertyField(useStageLimitProp, new GUIContent("启用阶段限制"));
            if (useStageLimitProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "只有当当前阶段在这个范围内时，这一组才会生效。",
                    MessageType.None
                );
                EditorGUILayout.PropertyField(minStageProp, new GUIContent("最小阶段"));
                EditorGUILayout.PropertyField(maxStageProp, new GUIContent("最大阶段"));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("孵化时钟", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(spawnClockModeProp, new GUIContent("孵化模式"));
            EditorGUILayout.PropertyField(spawnCountPerTickProp, new GUIContent("每次孵化数量"));

            UnitSpawner.SpawnClockMode clockMode = (UnitSpawner.SpawnClockMode)spawnClockModeProp.enumValueIndex;
            if (clockMode == UnitSpawner.SpawnClockMode.Interval)
            {
                EditorGUILayout.HelpBox(
                    "周期孵化：按固定时间重复刷这一组的单位。",
                    MessageType.None
                );
                EditorGUILayout.PropertyField(spawnIntervalUnitProp, new GUIContent("周期单位"));
                EditorGUILayout.PropertyField(spawnIntervalValueProp, new GUIContent("周期数值"));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("孵化检测", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useSpawnCountLimitProp, new GUIContent("启用数量上限检测"));
            if (useSpawnCountLimitProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "如果检测范围里的单位已经够多，这一组会暂时停止继续孵化。",
                    MessageType.None
                );
                EditorGUILayout.PropertyField(maxAliveCountProp, new GUIContent("范围内数量上限"));
                EditorGUILayout.PropertyField(countOnlyBoundUnitsProp, new GUIContent("只统计绑定了 UnitDefinition 的单位"));
                EditorGUILayout.PropertyField(aliveCheckBoxProp, new GUIContent("检测 Box 范围"));
                EditorGUILayout.PropertyField(aliveCheckSphereProp, new GUIContent("检测 Sphere 范围"));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("候选单位池", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这就是这个刷怪组自己的单位池。\n" +
                "系统会从这里按权重抽取要生成的单位。",
                MessageType.Info
            );

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加候选"))
                candidatesProp.arraySize++;

            if (GUILayout.Button("清空候选"))
            {
                if (EditorUtility.DisplayDialog("清空候选池", "确定清空这个刷怪组的候选池吗？", "确定", "取消"))
                    candidatesProp.ClearArray();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            for (int c = 0; c < candidatesProp.arraySize; c++)
            {
                SerializedProperty candidate = candidatesProp.GetArrayElementAtIndex(c);

                SerializedProperty cEnabledProp = candidate.FindPropertyRelative("enabled");
                SerializedProperty definitionProp = candidate.FindPropertyRelative("definition");
                SerializedProperty weightProp = candidate.FindPropertyRelative("weight");
                SerializedProperty noteProp = candidate.FindPropertyRelative("note");
                SerializedProperty useFilterProp = candidate.FindPropertyRelative("useFilter");
                SerializedProperty requiredDefineTypeProp = candidate.FindPropertyRelative("requiredDefineType");
                SerializedProperty requiredCharacterIdentityProp = candidate.FindPropertyRelative("requiredCharacterIdentity");
                SerializedProperty conditionProp = candidate.FindPropertyRelative("condition");

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"候选 {c + 1}", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(cEnabledProp, new GUIContent("启用"));
                EditorGUILayout.PropertyField(definitionProp, new GUIContent("单位定义"));
                EditorGUILayout.PropertyField(weightProp, new GUIContent("权重"));
                EditorGUILayout.PropertyField(noteProp, new GUIContent("备注"));

                EditorGUILayout.PropertyField(useFilterProp, new GUIContent("启用基础过滤"));
                if (useFilterProp.boolValue)
                {
                    EditorGUILayout.PropertyField(requiredDefineTypeProp, new GUIContent("要求定义类型"));
                    UnitDefineType t = (UnitDefineType)requiredDefineTypeProp.enumValueIndex;
                    if (t == UnitDefineType.Character)
                        EditorGUILayout.PropertyField(requiredCharacterIdentityProp, new GUIContent("要求人物身份"));
                }

                EditorGUILayout.PropertyField(conditionProp, new GUIContent("条件脚本接口"));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("上移") && c > 0)
                    candidatesProp.MoveArrayElement(c, c - 1);

                if (GUILayout.Button("下移") && c < candidatesProp.arraySize - 1)
                    candidatesProp.MoveArrayElement(c, c + 1);

                if (GUILayout.Button("删除"))
                {
                    candidatesProp.DeleteArrayElementAtIndex(c);
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
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("掉落配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里不是直接编辑掉落内容，而是给这个刷怪组绑定一张掉落表。\n" +
                "这样可以把“刷什么敌人”和“敌人掉什么”分开管理。",
                MessageType.Info
            );

            EditorGUILayout.PropertyField(dropProfileProp, new GUIContent("掉落表"));
            EditorGUILayout.PropertyField(overrideDropModeProp, new GUIContent("掉落模式覆盖"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(runtimeSpawnTimerProp, new GUIContent("运行时孵化计时"));
                EditorGUILayout.PropertyField(runtimeAliveCountProp, new GUIContent("运行时范围内数量"));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("上移") && i > 0)
                spawnGroupsProp.MoveArrayElement(i, i - 1);

            if (GUILayout.Button("下移") && i < spawnGroupsProp.arraySize - 1)
                spawnGroupsProp.MoveArrayElement(i, i + 1);

            if (GUILayout.Button("删除刷怪组"))
            {
                spawnGroupsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }
    }

    private void DrawAdvancedModule(UnitSpawner spawner)
    {
        showAdvancedModule = EditorGUILayout.Foldout(showAdvancedModule, "高级关卡功能", true);
        if (!showAdvancedModule)
            return;

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.HelpBox(
            "这里是高级功能。\n" +
            "如果你只是想做一个简单孵化器，可以先不管这里。",
            MessageType.Info
        );

        EditorGUILayout.LabelField("阶段控制", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(currentStageProp, new GUIContent("当前阶段"));

        EditorGUILayout.HelpBox(
            "阶段可以理解为游戏进程。\n" +
            "例如：前期刷普通敌人，中期混精英，后期开放更强敌人。",
            MessageType.None
        );

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("阶段 +1"))
        {
            Undo.RecordObject(spawner, "Spawner Stage +1");
            spawner.IncreaseStage();
            EditorUtility.SetDirty(spawner);
        }

        if (GUILayout.Button("阶段 -1"))
        {
            Undo.RecordObject(spawner, "Spawner Stage -1");
            spawner.DecreaseStage();
            EditorUtility.SetDirty(spawner);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("Scene Gizmo", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(drawPointGizmoProp, new GUIContent("显示点位 Gizmo"));
        EditorGUILayout.PropertyField(drawSpawnAreaGizmoProp, new GUIContent("显示生成范围 Gizmo"));
        EditorGUILayout.PropertyField(pointColorProp, new GUIContent("点位颜色"));
        EditorGUILayout.PropertyField(spawnAreaColorProp, new GUIContent("生成范围颜色"));
        EditorGUILayout.PropertyField(aliveCheckColorProp, new GUIContent("数量检测颜色"));

        EditorGUILayout.EndVertical();
    }

    private void DrawRuntimeDebugFoldout()
    {
        showRuntimeDebug = EditorGUILayout.Foldout(showRuntimeDebug, "运行时调试", true);
        if (!showRuntimeDebug)
            return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.PropertyField(debugLogsProp, new GUIContent("输出日志"));

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(spawnedInstancesProp, new GUIContent("已生成实例"), true);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawUtilityButtons(UnitSpawner spawner)
    {
        EditorGUILayout.LabelField("快捷操作", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Auto Setup"))
        {
            Undo.RecordObject(spawner, "UnitSpawner Auto Setup");
            spawner.AutoSetup();
            EditorUtility.SetDirty(spawner);
        }

        if (GUILayout.Button("添加刷怪组"))
        {
            Undo.RecordObject(spawner, "Add Spawn Group");
            spawner.AddGroup();
            EditorUtility.SetDirty(spawner);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn One"))
        {
            if (Application.isPlaying)
                spawner.SpawnOne();
            else
                EditorUtility.DisplayDialog("提示", "请在运行模式下生成单位。", "确定");
        }

        if (GUILayout.Button("Clear Spawned"))
        {
            if (Application.isPlaying)
                spawner.ClearSpawned();
            else
                EditorUtility.DisplayDialog("提示", "请在运行模式下清理实例。", "确定");
        }
        EditorGUILayout.EndHorizontal();
    }
}
