using System;
using System.Collections.Generic;
using UnityEngine;

public enum BattleParameterSection
{
    CoreAttributes = 0,
    ActionModules = 1,
    DamageTypes = 2,
    ElementAnomalies = 3,
    StatusDisplays = 4,
    FormulaRules = 5,
    BuildEvaluation = 6,
}

// BattleValueType is defined in the standalone BattleValueType.cs.
// Do not redefine it here.

public enum BattleDefinitionScope
{
    Unit = 0,
    Weapon = 1,
    Global = 2,
}

[Serializable]
public class CoreAttributeDefinition
{
    public string key = "new_attribute";
    public string displayName = "新属性";
    public string note = "";
    public BattleValueType valueType = BattleValueType.Float;
    public BattleDefinitionScope scope = BattleDefinitionScope.Unit;
    public bool showInUnitDefinition = true;
    public bool showInBuildEvaluation = true;
    public bool readOnlyInUnitDefinition = false;
    public bool isStandard = false;

    [Tooltip("单位定义页新增该属性条目时使用的默认值。已有单位不会被自动覆盖，除非手动执行修复工具。")]
    public float defaultValue = 0f;
}

[Serializable]
public class ActionModuleDefinition
{
    public string key = "new_action_module";
    public string displayName = "新动作模组";
    public string note = "";
    public bool isStandard = false;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 7f;
    public float dodgeDistance = 3f;
    public float weightInfluence = 1f;

    [Header("Attack Rhythm")]
    public float attackSpeedBase = 1f;
    public float lightAttackStartup = 0.12f;
    public float lightAttackRecovery = 0.18f;
    public float heavyAttackStartup = 0.28f;
    public float heavyAttackRecovery = 0.36f;
    public float comboInterval = 0.10f;
    public float chargeTime = 0.45f;

    [Header("Stability")]
    public float poiseBase = 0f;
    public float staggerRecovery = 1f;
    public float hitRecovery = 1f;
}

[Serializable]
public class DamageTypeDefinition
{
    public string key = "new_damage_type";
    public string displayName = "新伤害类型";
    public string note = "";
    public bool isStandard = false;
    public Color color = new Color(0.55f, 0.75f, 1f, 1f);
    public Sprite icon;
}

[Serializable]
public class ElementAnomalyDefinition
{
    public string key = "new_element";
    public string displayName = "新属性";
    public string anomalyDisplayName = "新异常";
    public string note = "";
    public Color color = new Color(1f, 0.55f, 0.35f, 1f);
    public Sprite icon;
    public float defaultAnomalyAccumulationRate = 1f;
    public bool isStandard = false;

    [Header("Status Grant Compatibility")]
    // Existing anomaly runtime reads these names directly. Keep them even if the
    // new battle-parameter UI does not expose all of them yet.
    public bool enableAnomalyStatusOnFull = false;
    public bool enableAnomalyStatusOnResolve = false;
    public bool enableAnomalyStatusOnResolved = false;
    public bool enableAnomalyStatusOnTrigger = false;
    public StatusDefinition grantedStatusDefinition;
    public string grantedStatusKey = "";
    public string grantedStatusDisplayName = "";

    [Header("Runtime Compatibility")]
    public string statusKey = "";
    public string statusDefinitionKey = "";
    public string anomalyStatusKey = "";
    public string runtimeStatusKey = "";
    public string effectStatusKey = "";
    public string triggerStatusKey = "";
    public string buildupStatusKey = "";
    public string accumulatedStatusKey = "";
    public string resolvedStatusKey = "";
    public string damageTypeKey = "";
    public string dotDamageTypeKey = "";

    public float maxAccumulation = 100f;
    public float accumulationThreshold = 100f;
    public float maxAnomalyValue = 100f;
    public float duration = 6f;
    public float anomalyDuration = 6f;
    public float defaultDuration = 6f;
    public float decayPerSecond = 0f;
    public float recoverDelay = 0f;
    public float damageInterval = 1f;
    public float dotDamagePerSecond = 0f;
}

// StatusDisplayDirection, StatusDisplayOverflowMode, and StatusDisplayDefinition
// are defined in the standalone StatusDisplayDefinition.cs.
// Do not redefine them here.

[Serializable]
public class FormulaTermDefinition
{
    public string key = "";
    public string displayName = "";
    public string sourceAttributeKey = "";
    public string sourceDisplayName = "";
    public float defaultValue = 0f;
    public bool isStandard = false;
}

[Serializable]
public class FormulaRuleDefinition
{
    public string key = "new_formula_rule";
    public string displayName = "新公式规则";
    [TextArea(2, 4)]
    public string note = "";

    public string baseValueAttributeKey = "atk";
    public string baseValueDisplayName = "攻击力";

    public List<FormulaTermDefinition> additiveTerms = new List<FormulaTermDefinition>();
    public List<FormulaTermDefinition> multiplicativeTerms = new List<FormulaTermDefinition>();

    public float finalMultiplier = 1f;
    public float minClamp = 0f;
    public float maxClamp = 999999f;
    public float randomMinMultiplier = 1f;
    public float randomMaxMultiplier = 1f;
}
