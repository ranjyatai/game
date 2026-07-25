using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(10900)]
public class SkyPrisonUnitRuntimeIdentity : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V1 - 2026-06-09 - runtime player/ally authority identity";

    [Header("Definition Identity - Read Only At Runtime")]
    [SerializeField] private CharacterIdentity definitionIdentity = CharacterIdentity.Ally;
    [SerializeField] private bool definitionIdentityResolved = false;

    [Header("Runtime Identity")]
    [SerializeField] private CharacterIdentity runtimeIdentity = CharacterIdentity.Ally;
    [SerializeField] private bool initializedFromDefinition = false;
    [SerializeField] private bool enforceRuntimeAuthorityOnEnable = true;

    [Header("Player Input")]
    [SerializeField] private SkyPrisonPlayerInputRouter playerInputRouter;
    [SerializeField] private bool autoAddPlayerInputRouterWhenBecomingPlayer = true;

    [Header("AI Brain Handoff")]
    [Tooltip("AI 包 / Brain 推荐实现 ISkyPrisonUnitRuntimeBrain。这里也可以手动拖入需要随 PlayerAuthority 开关的 Brain MonoBehaviour。")]
    [SerializeField] private List<MonoBehaviour> runtimeBrainBehaviours = new List<MonoBehaviour>();
    [SerializeField] private bool autoFindInterfaceBrainsInChildren = true;

    [Header("Death Rule")]
    [SerializeField] private UnitDeathController deathController;
    [SerializeField] private bool controlRespawnablePlayerDeathFlag = true;

    [Header("Outline Runtime Override")]
    [SerializeField] private bool applyOutlineRuntimeOverride = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    public event Action<SkyPrisonUnitRuntimeIdentity, CharacterIdentity, CharacterIdentity> OnRuntimeIdentityChanged;

    public string Version => scriptVersion;
    public CharacterIdentity DefinitionIdentity => definitionIdentity;
    public CharacterIdentity RuntimeIdentity => runtimeIdentity;
    public bool IsRuntimePlayer => runtimeIdentity == CharacterIdentity.Player;
    public bool IsRuntimeAlly => runtimeIdentity == CharacterIdentity.Ally;
    public GameObject UnitObject => gameObject;

    private void Reset()
    {
        ResolveReferences();
        ResolveDefinitionIdentity();
        runtimeIdentity = definitionIdentity;
        initializedFromDefinition = true;
    }

    private void Awake()
    {
        ResolveReferences();
        InitializeFromDefinitionIfNeeded();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializeFromDefinitionIfNeeded();

        SkyPrisonPlayerAuthority.RegisterUnit(this);

        if (enforceRuntimeAuthorityOnEnable)
            ApplyRuntimeIdentitySideEffects();
    }

    private void Start()
    {
        SkyPrisonPlayerAuthority.RegisterUnit(this);
        ApplyRuntimeIdentitySideEffects();
    }

    private void OnDisable()
    {
        SkyPrisonPlayerAuthority.UnregisterUnit(this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            ResolveReferences();
            ResolveDefinitionIdentity();
        }
    }
#endif

    [ContextMenu("Runtime Identity/Auto Setup")]
    public void ResolveReferences()
    {
        if (playerInputRouter == null)
            playerInputRouter = GetComponent<SkyPrisonPlayerInputRouter>();

        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();

        CleanupBrainList();
        if (autoFindInterfaceBrainsInChildren)
            AddInterfaceBrainsInChildren();
    }

    [ContextMenu("Runtime Identity/Re-read Definition Identity")]
    public void ResolveDefinitionIdentity()
    {
        UnitDefinition definition = null;

        UnitDefinitionRuntimeBinder binder = GetComponent<UnitDefinitionRuntimeBinder>();
        if (binder != null)
            definition = binder.UnitDefinitionAsset;

        if (definition == null)
        {
            UnitDefinitionHolder holder = GetComponent<UnitDefinitionHolder>();
            if (holder != null)
                definition = holder.Definition;
        }

        if (definition != null && definition.defineType == UnitDefineType.Character)
        {
            definitionIdentity = definition.characterIdentity;
            definitionIdentityResolved = true;
        }
        else
        {
            definitionIdentityResolved = false;
        }
    }

    public void InitializeFromDefinitionIfNeeded()
    {
        if (initializedFromDefinition)
            return;

        ResolveDefinitionIdentity();
        runtimeIdentity = definitionIdentityResolved ? definitionIdentity : CharacterIdentity.Ally;
        initializedFromDefinition = true;
    }

    public void SetRuntimeIdentity(CharacterIdentity newIdentity, bool applySideEffects = true)
    {
        InitializeFromDefinitionIfNeeded();

        CharacterIdentity oldIdentity = runtimeIdentity;
        if (oldIdentity == newIdentity)
        {
            if (applySideEffects)
                ApplyRuntimeIdentitySideEffects();
            return;
        }

        runtimeIdentity = newIdentity;

        if (applySideEffects)
            ApplyRuntimeIdentitySideEffects();

        if (debugLogs)
            Debug.Log($"[SkyPrisonUnitRuntimeIdentity] {name}: {oldIdentity} -> {newIdentity}", this);

        OnRuntimeIdentityChanged?.Invoke(this, oldIdentity, newIdentity);
    }

    public void ApplyRuntimeIdentitySideEffects()
    {
        ResolveReferences();

        bool isPlayer = runtimeIdentity == CharacterIdentity.Player;

        ApplyPlayerInputState(isPlayer);
        ApplyBrainState(!isPlayer && ShouldRuntimeIdentityUseBrain(runtimeIdentity));
        ApplyDeathPlayerFlag(isPlayer);

        if (applyOutlineRuntimeOverride)
            ApplyOutlineStateByReflection(MapRuntimeIdentityToOutlineGroup(runtimeIdentity));
    }

    private void ApplyPlayerInputState(bool enabledForPlayer)
    {
        if (playerInputRouter == null && enabledForPlayer && autoAddPlayerInputRouterWhenBecomingPlayer)
            playerInputRouter = gameObject.AddComponent<SkyPrisonPlayerInputRouter>();

        if (playerInputRouter == null)
            return;

        playerInputRouter.SetPlayerInputEnabled(enabledForPlayer);
        playerInputRouter.enabled = enabledForPlayer;
    }

    private void ApplyBrainState(bool enabledForBrain)
    {
        CleanupBrainList();

        for (int i = 0; i < runtimeBrainBehaviours.Count; i++)
        {
            MonoBehaviour brain = runtimeBrainBehaviours[i];
            if (brain == null)
                continue;

            if (brain is ISkyPrisonUnitRuntimeBrain runtimeBrain)
            {
                runtimeBrain.SetBrainEnabledByPlayerAuthority(enabledForBrain);
            }
            else
            {
                // 手动拖入的普通 Brain 组件按 enabled 开关。自动扫描只收接口 Brain，不会误关普通组件。
                brain.enabled = enabledForBrain;
            }
        }
    }

    private void ApplyDeathPlayerFlag(bool isPlayer)
    {
        if (!controlRespawnablePlayerDeathFlag || deathController == null)
            return;

        MethodInfo method = deathController.GetType().GetMethod(
            "SetTreatAsRespawnablePlayerRuntime",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method != null)
        {
            method.Invoke(deathController, new object[] { isPlayer });
            return;
        }

        // 兼容当前 UnitDeathController：字段是 private，先只在运行时写入，不改 UnitDefinition 资产。
        FieldInfo field = deathController.GetType().GetField(
            "treatAsRespawnablePlayer",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field != null && field.FieldType == typeof(bool))
            field.SetValue(deathController, isPlayer);
    }

    private void ApplyOutlineStateByReflection(OutlineGroup group)
    {
        MonoBehaviour outlineStateComponent = FindComponentByTypeName(gameObject, "UnitOutlineState");
        if (outlineStateComponent == null)
            return;

        Type outlineStateType = outlineStateComponent.GetType();
        MethodInfo setEnabled = outlineStateType.GetMethod("SetOutlineEnabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo setState = outlineStateType.GetMethod("SetOutlineState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        bool enabled = group != OutlineGroup.None;
        setEnabled?.Invoke(outlineStateComponent, new object[] { enabled });

        if (setState == null)
            return;

        ParameterInfo[] parameters = setState.GetParameters();
        if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
            return;

        string enumName = group.ToString();
        object enumValue;
        try
        {
            enumValue = Enum.Parse(parameters[0].ParameterType, enumName);
        }
        catch
        {
            enumValue = Enum.Parse(parameters[0].ParameterType, "None");
        }

        setState.Invoke(outlineStateComponent, new[] { enumValue });
    }

    private static MonoBehaviour FindComponentByTypeName(GameObject root, string typeName)
    {
        if (root == null || string.IsNullOrWhiteSpace(typeName))
            return null;

        MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour component = components[i];
            if (component != null && component.GetType().Name == typeName)
                return component;
        }

        return null;
    }

    private static bool ShouldRuntimeIdentityUseBrain(CharacterIdentity identity)
    {
        return identity == CharacterIdentity.Ally ||
               identity == CharacterIdentity.Enemy ||
               identity == CharacterIdentity.Elite ||
               identity == CharacterIdentity.Boss ||
               identity == CharacterIdentity.NeutralHostile ||
               identity == CharacterIdentity.NeutralPassive ||
               identity == CharacterIdentity.Creature;
    }

    public static OutlineGroup MapRuntimeIdentityToOutlineGroup(CharacterIdentity identity)
    {
        switch (identity)
        {
            case CharacterIdentity.Player:
                return OutlineGroup.Player;
            case CharacterIdentity.Enemy:
            case CharacterIdentity.Elite:
            case CharacterIdentity.Boss:
            case CharacterIdentity.NeutralHostile:
                return OutlineGroup.Enemy;
            case CharacterIdentity.Ally:
            case CharacterIdentity.NeutralPassive:
            case CharacterIdentity.Creature:
                return OutlineGroup.Ally;
            default:
                return OutlineGroup.None;
        }
    }

    private void CleanupBrainList()
    {
        for (int i = runtimeBrainBehaviours.Count - 1; i >= 0; i--)
        {
            if (runtimeBrainBehaviours[i] == null)
                runtimeBrainBehaviours.RemoveAt(i);
        }
    }

    private void AddInterfaceBrainsInChildren()
    {
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == this)
                continue;

            if (behaviour is ISkyPrisonUnitRuntimeBrain && !runtimeBrainBehaviours.Contains(behaviour))
                runtimeBrainBehaviours.Add(behaviour);
        }
    }
}
