using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(11000)]
public class SkyPrisonPlayerAuthority : MonoBehaviour
{
    private static SkyPrisonPlayerAuthority instance;
    private static readonly List<SkyPrisonUnitRuntimeIdentity> RegisteredUnits = new List<SkyPrisonUnitRuntimeIdentity>();

    [Header("Version")]
    [SerializeField] private string scriptVersion = "V1 - 2026-06-09 - single runtime Player authority";

    [Header("Current Player")]
    [SerializeField] private SkyPrisonUnitRuntimeIdentity currentPlayer;

    [Header("Startup")]
    [SerializeField] private bool autoChoosePlayerOnStart = true;
    [SerializeField] private bool preferDefinitionPlayerOnStart = true;
    [SerializeField] private bool convertExtraRuntimePlayersToAlly = true;

    [Header("Rules")]
    [SerializeField] private CharacterIdentity previousPlayerFallbackIdentity = CharacterIdentity.Ally;
    [SerializeField] private bool forceOldPlayerToAlly = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    public static SkyPrisonPlayerAuthority Instance => instance;
    public static SkyPrisonUnitRuntimeIdentity CurrentPlayerUnit => instance != null ? instance.currentPlayer : null;
    public static event Action<SkyPrisonUnitRuntimeIdentity, SkyPrisonUnitRuntimeIdentity> CurrentPlayerChangedGlobal;

    public event Action<SkyPrisonUnitRuntimeIdentity, SkyPrisonUnitRuntimeIdentity> CurrentPlayerChanged;

    public string Version => scriptVersion;
    public SkyPrisonUnitRuntimeIdentity CurrentPlayer => currentPlayer;
    public GameObject CurrentPlayerGameObject => currentPlayer != null ? currentPlayer.gameObject : null;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning($"[SkyPrisonPlayerAuthority] Duplicate authority on '{name}'. Disable duplicate.", this);
            enabled = false;
            return;
        }

        instance = this;
        RefreshRegisteredUnitsFromScene();
    }

    private void OnEnable()
    {
        if (instance == null)
            instance = this;

        RefreshRegisteredUnitsFromScene();
    }

    private void Start()
    {
        RefreshRegisteredUnitsFromScene();

        if (autoChoosePlayerOnStart && currentPlayer == null)
            ChooseInitialPlayer();
        else
            EnforceSinglePlayer(currentPlayer);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static void RegisterUnit(SkyPrisonUnitRuntimeIdentity identity)
    {
        if (identity == null)
            return;

        if (!RegisteredUnits.Contains(identity))
            RegisteredUnits.Add(identity);

        if (instance != null)
            instance.HandleUnitRegistered(identity);
    }

    public static void UnregisterUnit(SkyPrisonUnitRuntimeIdentity identity)
    {
        if (identity == null)
            return;

        RegisteredUnits.Remove(identity);

        if (instance != null && instance.currentPlayer == identity)
            instance.ClearCurrentPlayer(identity);
    }

    public static void SetCurrentPlayerUnit(GameObject unitObject)
    {
        if (instance == null)
            instance = FindObjectOfType<SkyPrisonPlayerAuthority>();

        if (instance == null)
        {
            GameObject authorityObject = new GameObject("SkyPrisonPlayerAuthority");
            instance = authorityObject.AddComponent<SkyPrisonPlayerAuthority>();
        }

        instance.SetCurrentPlayer(unitObject);
    }

    public void SetCurrentPlayer(GameObject unitObject)
    {
        if (unitObject == null)
        {
            ClearCurrentPlayer(currentPlayer);
            return;
        }

        SkyPrisonUnitRuntimeIdentity identity = unitObject.GetComponent<SkyPrisonUnitRuntimeIdentity>();
        if (identity == null)
            identity = unitObject.AddComponent<SkyPrisonUnitRuntimeIdentity>();

        SetCurrentPlayer(identity);
    }

    public void SetCurrentPlayer(SkyPrisonUnitRuntimeIdentity nextPlayer)
    {
        if (nextPlayer == null)
        {
            ClearCurrentPlayer(currentPlayer);
            return;
        }

        if (!RegisteredUnits.Contains(nextPlayer))
            RegisteredUnits.Add(nextPlayer);

        SkyPrisonUnitRuntimeIdentity oldPlayer = currentPlayer;
        if (oldPlayer == nextPlayer)
        {
            nextPlayer.SetRuntimeIdentity(CharacterIdentity.Player, true);
            EnforceSinglePlayer(nextPlayer);
            return;
        }

        if (oldPlayer != null && forceOldPlayerToAlly)
            oldPlayer.SetRuntimeIdentity(previousPlayerFallbackIdentity, true);

        currentPlayer = nextPlayer;
        currentPlayer.SetRuntimeIdentity(CharacterIdentity.Player, true);

        EnforceSinglePlayer(currentPlayer);

        if (debugLogs)
        {
            string oldName = oldPlayer != null ? oldPlayer.name : "None";
            string newName = currentPlayer != null ? currentPlayer.name : "None";
            Debug.Log($"[SkyPrisonPlayerAuthority] Current Player: {oldName} -> {newName}", this);
        }

        CurrentPlayerChanged?.Invoke(oldPlayer, currentPlayer);
        CurrentPlayerChangedGlobal?.Invoke(oldPlayer, currentPlayer);
    }

    [ContextMenu("Authority/Refresh Registered Units From Scene")]
    public void RefreshRegisteredUnitsFromScene()
    {
        SkyPrisonUnitRuntimeIdentity[] identities = FindObjectsOfType<SkyPrisonUnitRuntimeIdentity>(true);
        for (int i = 0; i < identities.Length; i++)
        {
            if (identities[i] != null && !RegisteredUnits.Contains(identities[i]))
                RegisteredUnits.Add(identities[i]);
        }

        for (int i = RegisteredUnits.Count - 1; i >= 0; i--)
        {
            if (RegisteredUnits[i] == null)
                RegisteredUnits.RemoveAt(i);
        }
    }

    [ContextMenu("Authority/Choose Initial Player")]
    public void ChooseInitialPlayer()
    {
        RefreshRegisteredUnitsFromScene();

        SkyPrisonUnitRuntimeIdentity candidate = null;

        if (preferDefinitionPlayerOnStart)
        {
            for (int i = 0; i < RegisteredUnits.Count; i++)
            {
                SkyPrisonUnitRuntimeIdentity unit = RegisteredUnits[i];
                if (unit == null || !unit.isActiveAndEnabled)
                    continue;

                unit.InitializeFromDefinitionIfNeeded();
                if (unit.DefinitionIdentity == CharacterIdentity.Player || unit.RuntimeIdentity == CharacterIdentity.Player)
                {
                    candidate = unit;
                    break;
                }
            }
        }

        // 以前这里有一段"找不到Player身份的单位就随便抓一个已注册单位顶替"的兜底——
        // 场景里同时有多个单位时(比如测试NPC/敌人还在场)，这段代码一旦抢在真正的
        // 玩家角色完成注册之前跑，就会把随便某个单位错认成玩家，同时把真正的玩家
        // 降级成Ally(蓝色描边)。宁可"暂时没有玩家"，也不要"认错玩家"，所以删掉这个
        // 随便顶替的兜底，找不到明确是Player身份的单位就不设置，不瞎猜。

        if (candidate != null)
            SetCurrentPlayer(candidate);
        else
            EnforceSinglePlayer(null);
    }

    private void HandleUnitRegistered(SkyPrisonUnitRuntimeIdentity identity)
    {
        if (identity == null)
            return;

        identity.InitializeFromDefinitionIfNeeded();

        if (currentPlayer == null && autoChoosePlayerOnStart &&
            (identity.DefinitionIdentity == CharacterIdentity.Player || identity.RuntimeIdentity == CharacterIdentity.Player))
        {
            SetCurrentPlayer(identity);
            return;
        }

        if (convertExtraRuntimePlayersToAlly && currentPlayer != null && identity != currentPlayer && identity.RuntimeIdentity == CharacterIdentity.Player)
            identity.SetRuntimeIdentity(previousPlayerFallbackIdentity, true);
    }

    private void EnforceSinglePlayer(SkyPrisonUnitRuntimeIdentity allowedPlayer)
    {
        if (!convertExtraRuntimePlayersToAlly)
            return;

        RefreshRegisteredUnitsFromScene();

        for (int i = 0; i < RegisteredUnits.Count; i++)
        {
            SkyPrisonUnitRuntimeIdentity unit = RegisteredUnits[i];
            if (unit == null || unit == allowedPlayer)
                continue;

            unit.InitializeFromDefinitionIfNeeded();
            if (unit.RuntimeIdentity == CharacterIdentity.Player)
                unit.SetRuntimeIdentity(previousPlayerFallbackIdentity, true);
        }
    }

    private void ClearCurrentPlayer(SkyPrisonUnitRuntimeIdentity expectedOldPlayer)
    {
        SkyPrisonUnitRuntimeIdentity oldPlayer = currentPlayer;

        if (expectedOldPlayer != null && oldPlayer != null && expectedOldPlayer != oldPlayer)
            return;

        if (oldPlayer != null && forceOldPlayerToAlly)
            oldPlayer.SetRuntimeIdentity(previousPlayerFallbackIdentity, true);

        currentPlayer = null;

        CurrentPlayerChanged?.Invoke(oldPlayer, null);
        CurrentPlayerChangedGlobal?.Invoke(oldPlayer, null);
    }
}
