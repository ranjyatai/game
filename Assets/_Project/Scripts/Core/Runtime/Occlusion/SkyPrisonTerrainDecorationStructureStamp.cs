using UnityEngine;

/// <summary>
/// Runtime/Editor marker written by SkyPrisonTerrainDecorationInstanceBuilder.
/// It is not gameplay logic. It exists so occlusion structure problems can be audited by source,
/// instead of guessing whether a scene object was rebuilt by the current builder.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonTerrainDecorationStructureStamp : MonoBehaviour
{
    [Header("Builder")]
    [SerializeField] private string builderVersion;
    [SerializeField] private string lastBuildUtc;
    [SerializeField] private string definitionName;
    [SerializeField] private string definitionId;

    [Header("Required Nodes")]
    [SerializeField] private bool hasVisualRoot;
    [SerializeField] private bool hasRuleRoot;
    [SerializeField] private bool hasCollisionRoot;
    [SerializeField] private bool hasBackTrigger;
    [SerializeField] private bool hasFrontTrigger;
    [SerializeField] private bool hasFrontOccluderRoot;
    [SerializeField] private bool hasFrontOccluderTrigger;
    [SerializeField] private int backTriggerMissingScriptCount;

    public string BuilderVersion => builderVersion;
    public string LastBuildUtc => lastBuildUtc;
    public string DefinitionName => definitionName;
    public string DefinitionId => definitionId;
    public bool HasBackTrigger => hasBackTrigger;
    public bool HasFrontOccluderTrigger => hasFrontOccluderTrigger;
    public int BackTriggerMissingScriptCount => backTriggerMissingScriptCount;

    public void Write(
        string version,
        TerrainDecorationDefinition definition,
        Transform visualRoot,
        Transform ruleRoot,
        Transform collisionRoot,
        Transform backTrigger,
        Transform frontTrigger,
        Transform frontOccluderRoot,
        bool triggerExists,
        int missingScripts)
    {
        builderVersion = version;
        lastBuildUtc = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        definitionName = definition != null ? definition.name : "<null>";
        definitionId = definition != null ? definition.decorationId : "";

        hasVisualRoot = visualRoot != null;
        hasRuleRoot = ruleRoot != null;
        hasCollisionRoot = collisionRoot != null;
        hasBackTrigger = backTrigger != null;
        hasFrontTrigger = frontTrigger != null;
        hasFrontOccluderRoot = frontOccluderRoot != null;
        hasFrontOccluderTrigger = triggerExists;
        backTriggerMissingScriptCount = Mathf.Max(0, missingScripts);
    }
}
