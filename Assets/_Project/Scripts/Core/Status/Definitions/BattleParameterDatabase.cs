using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "BattleParameterDatabase",
    menuName = "Sky Prison/Battle Parameter Database",
    order = 1200)]
public class BattleParameterDatabase : ScriptableObject
{
    public string databaseId = "battle_parameters_main";
    public string displayName = "战斗参数";
    [TextArea(2, 4)]
    public string note = "";

    [Header("Runtime")]
    public bool isRuntimeActive = true;

    [Header("Anomaly Runtime Defaults")]
    public float anomalyDecayDelay = 0.75f;
    public float anomalyDecayPerSecond = 0f;

    public List<CoreAttributeDefinition> coreAttributes = new List<CoreAttributeDefinition>()
    {
        new CoreAttributeDefinition { key = "maxHp", displayName = "最大生命值", valueType = BattleValueType.Integer, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 100f },
        new CoreAttributeDefinition { key = "hp", displayName = "生命值HP", valueType = BattleValueType.Integer, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 0f },
        new CoreAttributeDefinition { key = "maxLp", displayName = "最大负荷值", valueType = BattleValueType.Integer, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 100f },
        new CoreAttributeDefinition { key = "lp", displayName = "负荷值LP", valueType = BattleValueType.Integer, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 0f },
        new CoreAttributeDefinition { key = "lpRecoveryRate", displayName = "负荷值回复率", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 20f },
        new CoreAttributeDefinition { key = "lpRecoveryDelay", displayName = "恢复延迟时间", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 0.65f },
        new CoreAttributeDefinition { key = "exhaustedLpRecoveryDelay", displayName = "负荷耗尽恢复延迟", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 4.5f },
        new CoreAttributeDefinition { key = "sprintResumeLpAfterExhausted", displayName = "耗尽后奔跑恢复阈值", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 5f },
        new CoreAttributeDefinition { key = "dodgeLpCost", displayName = "闪避消耗", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 25f },
        new CoreAttributeDefinition { key = "sprintLpCost", displayName = "冲刺消耗/秒", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 15f },
        new CoreAttributeDefinition { key = "atk", displayName = "攻击力", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 10f },
        new CoreAttributeDefinition { key = "def", displayName = "防御", valueType = BattleValueType.Float, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 0f },
        new CoreAttributeDefinition { key = "critRate", displayName = "暴击率", valueType = BattleValueType.Percentage, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 5f },
        new CoreAttributeDefinition { key = "critDamageMultiplier", displayName = "暴击伤害倍率", valueType = BattleValueType.Percentage, scope = BattleDefinitionScope.Unit, isStandard = true, defaultValue = 150f },
    };

    public List<ActionModuleDefinition> actionModules = new List<ActionModuleDefinition>()
    {
        new ActionModuleDefinition
        {
            key = "one_hand_sword", displayName = "单手剑模组", isStandard = true,
            moveSpeed = 5.4f, sprintSpeed = 7.6f, attackSpeedBase = 1.0f,
            lightAttackStartup = 0.10f, lightAttackRecovery = 0.16f,
            heavyAttackStartup = 0.22f, heavyAttackRecovery = 0.30f,
            comboInterval = 0.08f, chargeTime = 0.35f,
        },
        new ActionModuleDefinition
        {
            key = "hammer", displayName = "大锤模组", isStandard = true,
            moveSpeed = 4.5f, sprintSpeed = 6.4f, attackSpeedBase = 0.78f,
            lightAttackStartup = 0.18f, lightAttackRecovery = 0.28f,
            heavyAttackStartup = 0.36f, heavyAttackRecovery = 0.46f,
            comboInterval = 0.14f, chargeTime = 0.62f, poiseBase = 10f,
        },
    };

    public List<DamageTypeDefinition> damageTypes = new List<DamageTypeDefinition>()
    {
        new DamageTypeDefinition { key = "slash", displayName = "斩击", isStandard = true },
        new DamageTypeDefinition { key = "strike", displayName = "打击", isStandard = true },
        new DamageTypeDefinition { key = "impact", displayName = "冲击", isStandard = true },
        new DamageTypeDefinition { key = "heat", displayName = "灼热", isStandard = true, color = new Color(1f, 0.45f, 0.25f, 1f) },
        new DamageTypeDefinition { key = "shock", displayName = "电磁", isStandard = true, color = new Color(0.45f, 0.75f, 1f, 1f) },
        new DamageTypeDefinition { key = "corrosion", displayName = "腐蚀", isStandard = true, color = new Color(0.45f, 1f, 0.55f, 1f) },
        new DamageTypeDefinition { key = "freeze", displayName = "冻结", isStandard = true, color = new Color(0.65f, 0.9f, 1f, 1f) },
    };

    public List<ElementAnomalyDefinition> elementAnomalies = new List<ElementAnomalyDefinition>()
    {
        new ElementAnomalyDefinition { key = "heat", displayName = "灼热", anomalyDisplayName = "灼热异常", isStandard = true, defaultAnomalyAccumulationRate = 1f, color = new Color(1f, 0.45f, 0.25f, 1f), damageTypeKey = "heat", dotDamageTypeKey = "heat" },
        new ElementAnomalyDefinition { key = "shock", displayName = "电磁", anomalyDisplayName = "电磁异常", isStandard = true, defaultAnomalyAccumulationRate = 1f, color = new Color(0.45f, 0.75f, 1f, 1f), damageTypeKey = "shock", dotDamageTypeKey = "shock" },
        new ElementAnomalyDefinition { key = "corrosion", displayName = "腐蚀", anomalyDisplayName = "腐蚀异常", isStandard = true, defaultAnomalyAccumulationRate = 1f, color = new Color(0.45f, 1f, 0.55f, 1f), damageTypeKey = "corrosion", dotDamageTypeKey = "corrosion" },
        new ElementAnomalyDefinition { key = "freeze", displayName = "冻结", anomalyDisplayName = "冻结异常", isStandard = true, defaultAnomalyAccumulationRate = 1f, color = new Color(0.65f, 0.9f, 1f, 1f), damageTypeKey = "freeze", dotDamageTypeKey = "freeze" },
    };

    public List<StatusDisplayDefinition> statusDisplays = new List<StatusDisplayDefinition>()
    {
        new StatusDisplayDefinition
        {
            key = "default_status_display",
            displayName = "默认状态显示",
            refreshDirection = StatusDisplayDirection.BottomToTop,
            overflowMode = StatusDisplayOverflowMode.Wrap,
            maxLines = 1,
            iconSize = new Vector2(18f, 18f),
            spacing = new Vector2(2f, 2f),
            rotatePageInterval = 1.25f,
            exposeToUnitUI = true,
            uiSemanticKey = "overhead_status",
            isStandard = true,
        },
    };

    public List<FormulaRuleDefinition> formulaRules = new List<FormulaRuleDefinition>();

    public CoreAttributeDefinition FindCoreAttribute(string key)
    {
        return FindByKey(coreAttributes, key);
    }

    public ActionModuleDefinition FindActionModule(string key)
    {
        return FindByKey(actionModules, key);
    }

    public DamageTypeDefinition FindDamageType(string key)
    {
        return FindByKey(damageTypes, key);
    }

    public ElementAnomalyDefinition FindElementAnomaly(string key)
    {
        return FindByKey(elementAnomalies, key);
    }

    public ElementAnomalyDefinition ResolveCategoryAnomaly(string categoryKey)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
            return null;

        ElementAnomalyDefinition exact = FindElementAnomaly(categoryKey);
        if (exact != null)
            return exact;

        string normalized = categoryKey.Trim().ToLowerInvariant();
        if (normalized.Contains("heat") || normalized.Contains("fire") || normalized.Contains("burn"))
            return FindElementAnomaly("heat");
        if (normalized.Contains("shock") || normalized.Contains("electric") || normalized.Contains("electro"))
            return FindElementAnomaly("shock");
        if (normalized.Contains("corrosion") || normalized.Contains("poison") || normalized.Contains("acid"))
            return FindElementAnomaly("corrosion");
        if (normalized.Contains("freeze") || normalized.Contains("ice") || normalized.Contains("frost"))
            return FindElementAnomaly("freeze");

        return null;
    }

    public ElementAnomalyDefinition ResolveCategoryAnomaly(DamageTypeDefinition damageType)
    {
        return damageType != null ? ResolveCategoryAnomaly(damageType.key) : null;
    }

    public ElementAnomalyDefinition ResolveCategoryAnomaly(object category)
    {
        return category != null ? ResolveCategoryAnomaly(category.ToString()) : null;
    }

    public float ResolveCategoryAnomalyReceiveMultiplier(string categoryKey)
    {
        ElementAnomalyDefinition anomaly = ResolveCategoryAnomaly(categoryKey);
        if (anomaly == null)
            return 1f;

        return Mathf.Max(0f, anomaly.defaultAnomalyAccumulationRate);
    }

    public float ResolveCategoryAnomalyReceiveMultiplier(DamageTypeDefinition damageType)
    {
        return damageType != null ? ResolveCategoryAnomalyReceiveMultiplier(damageType.key) : 1f;
    }

    public float ResolveCategoryAnomalyReceiveMultiplier(ElementAnomalyDefinition anomaly)
    {
        if (anomaly == null)
            return 1f;

        return Mathf.Max(0f, anomaly.defaultAnomalyAccumulationRate);
    }

    public float ResolveCategoryAnomalyReceiveMultiplier(object category)
    {
        if (category == null)
            return 1f;

        ElementAnomalyDefinition anomaly = category as ElementAnomalyDefinition;
        if (anomaly != null)
            return ResolveCategoryAnomalyReceiveMultiplier(anomaly);

        DamageTypeDefinition damageType = category as DamageTypeDefinition;
        if (damageType != null)
            return ResolveCategoryAnomalyReceiveMultiplier(damageType);

        return ResolveCategoryAnomalyReceiveMultiplier(category.ToString());
    }

    public StatusDisplayDefinition FindStatusDisplay(string key)
    {
        if (statusDisplays == null || statusDisplays.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(key))
        {
            for (int i = 0; i < statusDisplays.Count; i++)
            {
                StatusDisplayDefinition item = statusDisplays[i];
                if (item != null && item.key == key)
                    return item;
            }
        }

        for (int i = 0; i < statusDisplays.Count; i++)
        {
            StatusDisplayDefinition item = statusDisplays[i];
            if (item != null && item.exposeToUnitUI)
                return item;
        }

        return statusDisplays[0];
    }

    private static T FindByKey<T>(List<T> list, string key) where T : class
    {
        if (list == null || string.IsNullOrWhiteSpace(key))
            return null;

        for (int i = 0; i < list.Count; i++)
        {
            T item = list[i];
            if (item == null)
                continue;

            string itemKey = TryGetKey(item);
            if (itemKey == key)
                return item;
        }

        return null;
    }

    private static string TryGetKey(object item)
    {
        if (item == null)
            return string.Empty;

        System.Type type = item.GetType();
        System.Reflection.FieldInfo field = type.GetField("key");
        if (field != null && field.FieldType == typeof(string))
            return field.GetValue(item) as string ?? string.Empty;

        System.Reflection.PropertyInfo prop = type.GetProperty("key");
        if (prop != null && prop.PropertyType == typeof(string) && prop.CanRead)
            return prop.GetValue(item, null) as string ?? string.Empty;

        return string.Empty;
    }
}
