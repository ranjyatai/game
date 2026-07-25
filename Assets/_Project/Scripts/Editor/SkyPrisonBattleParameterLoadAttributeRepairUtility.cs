#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot editor utility for formal LP / Load attributes.
///
/// Purpose:
/// - Existing BattleParameterDatabase assets do not automatically receive new default list entries
///   when the C# constructor/default field initializer changes.
/// - Existing UnitDefinition.parameterValues entries may already exist as 0.
///
/// This utility adds the formal load attributes to all BattleParameterDatabase assets and fills
/// missing / zero config values in UnitDefinition assets with safe defaults.
/// It does not overwrite current LP itself; lp=0 remains the authoring meaning of "spawn full".
/// </summary>
public static class SkyPrisonBattleParameterLoadAttributeRepairUtility_V1
{
    private struct LoadAttributeSpec
    {
        public string key;
        public string displayName;
        public BattleValueType valueType;
        public BattleDefinitionScope scope;
        public float defaultValue;

        public LoadAttributeSpec(string key, string displayName, BattleValueType valueType, float defaultValue)
        {
            this.key = key;
            this.displayName = displayName;
            this.valueType = valueType;
            this.scope = BattleDefinitionScope.Unit;
            this.defaultValue = defaultValue;
        }
    }

    private static readonly LoadAttributeSpec[] Specs =
    {
        new LoadAttributeSpec("maxLp", "最大负荷值", BattleValueType.Integer, 100f),
        new LoadAttributeSpec("lp", "负荷值LP", BattleValueType.Integer, 0f),
        new LoadAttributeSpec("lpRecoveryRate", "负荷值回复率", BattleValueType.Float, 20f),
        new LoadAttributeSpec("lpRecoveryDelay", "恢复延迟时间", BattleValueType.Float, 0.65f),
        new LoadAttributeSpec("exhaustedLpRecoveryDelay", "负荷耗尽恢复延迟", BattleValueType.Float, 4.5f),
        new LoadAttributeSpec("sprintResumeLpAfterExhausted", "耗尽后奔跑恢复阈值", BattleValueType.Float, 5f),
        new LoadAttributeSpec("dodgeLpCost", "闪避消耗", BattleValueType.Float, 25f),
        new LoadAttributeSpec("sprintLpCost", "冲刺消耗/秒", BattleValueType.Float, 15f),
    };

    [MenuItem("Tools/Sky Prison/Battle Parameters/Repair Load Attributes V1")]
    public static void RepairLoadAttributes()
    {
        int dbCount = RepairAllBattleParameterDatabases();
        int unitCount = RepairAllUnitDefinitions();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SkyPrisonBattleParameterLoadAttributeRepairUtility_V1] Finished. Repaired databases: {dbCount}, repaired unit definitions: {unitCount}.");
    }

    private static int RepairAllBattleParameterDatabases()
    {
        int repaired = 0;
        string[] guids = AssetDatabase.FindAssets("t:BattleParameterDatabase");
        if (guids == null)
            return repaired;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            BattleParameterDatabase db = AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(path);
            if (db == null)
                continue;

            if (db.coreAttributes == null)
                db.coreAttributes = new List<CoreAttributeDefinition>();

            bool changed = false;
            for (int s = 0; s < Specs.Length; s++)
            {
                changed |= EnsureCoreAttribute(db, Specs[s]);
            }

            if (changed)
            {
                EditorUtility.SetDirty(db);
                repaired++;
            }
        }

        return repaired;
    }

    private static bool EnsureCoreAttribute(BattleParameterDatabase db, LoadAttributeSpec spec)
    {
        CoreAttributeDefinition def = FindCoreAttribute(db, spec.key);
        if (def == null)
        {
            db.coreAttributes.Add(new CoreAttributeDefinition
            {
                key = spec.key,
                displayName = spec.displayName,
                valueType = spec.valueType,
                scope = spec.scope,
                showInUnitDefinition = true,
                showInBuildEvaluation = true,
                readOnlyInUnitDefinition = false,
                defaultValue = spec.defaultValue,
            });
            return true;
        }

        bool changed = false;

        // Only normalize the formal load rows. Do not touch unrelated designer-defined rows.
        if (string.IsNullOrWhiteSpace(def.displayName))
        {
            def.displayName = spec.displayName;
            changed = true;
        }

        if (def.valueType != spec.valueType)
        {
            def.valueType = spec.valueType;
            changed = true;
        }

        if (def.scope != spec.scope)
        {
            def.scope = spec.scope;
            changed = true;
        }

        if (!def.showInUnitDefinition)
        {
            def.showInUnitDefinition = true;
            changed = true;
        }

        if (def.defaultValue <= 0f && spec.defaultValue > 0f && spec.key != "lp")
        {
            def.defaultValue = spec.defaultValue;
            changed = true;
        }

        return changed;
    }

    private static CoreAttributeDefinition FindCoreAttribute(BattleParameterDatabase db, string key)
    {
        if (db == null || db.coreAttributes == null || string.IsNullOrWhiteSpace(key))
            return null;

        for (int i = 0; i < db.coreAttributes.Count; i++)
        {
            CoreAttributeDefinition def = db.coreAttributes[i];
            if (def == null)
                continue;

            if (string.Equals(def.key, key, System.StringComparison.OrdinalIgnoreCase))
                return def;
        }

        return null;
    }

    private static int RepairAllUnitDefinitions()
    {
        int repaired = 0;
        string[] guids = AssetDatabase.FindAssets("t:UnitDefinition");
        if (guids == null)
            return repaired;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            UnitDefinition unit = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);
            if (unit == null)
                continue;

            if (unit.parameterValues == null)
                unit.parameterValues = new List<UnitParameterValue>();

            bool changed = false;
            for (int s = 0; s < Specs.Length; s++)
            {
                changed |= EnsureUnitParameterValue(unit, Specs[s]);
            }

            if (changed)
            {
                EditorUtility.SetDirty(unit);
                repaired++;
            }
        }

        return repaired;
    }

    private static bool EnsureUnitParameterValue(UnitDefinition unit, LoadAttributeSpec spec)
    {
        UnitParameterValue value = FindUnitParameterValue(unit, spec.key);
        if (value == null)
        {
            unit.parameterValues.Add(new UnitParameterValue
            {
                parameterKey = spec.key,
                value = spec.defaultValue,
            });
            return true;
        }

        // Current LP is special: lp=0 means "spawn full" in UnitLoadRuntime.
        if (spec.key == "lp")
            return false;

        // Existing auto-created config rows often contain 0. For formal load fields this usually means
        // "not initialized yet", so fill a safe default. Designers can still manually change these later.
        if (value.value <= 0f && spec.defaultValue > 0f)
        {
            value.value = spec.defaultValue;
            return true;
        }

        return false;
    }

    private static UnitParameterValue FindUnitParameterValue(UnitDefinition unit, string key)
    {
        if (unit == null || unit.parameterValues == null || string.IsNullOrWhiteSpace(key))
            return null;

        for (int i = 0; i < unit.parameterValues.Count; i++)
        {
            UnitParameterValue value = unit.parameterValues[i];
            if (value == null)
                continue;

            if (string.Equals(value.parameterKey, key, System.StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }
}
#endif
