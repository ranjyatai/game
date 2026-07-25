using System.Collections.Generic; // List 仍在使用
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDeathDecisionController : MonoBehaviour, IDeathDecisionHandler
{
    public enum ReviveDecisionState { None, WaitingForPlayerChoice, Resolved }

    [Header("引用")]
    [SerializeField] private UnitHealthController healthController;
    [SerializeField] private UnitDeathController  deathController;

    [Header("0血不死保护（可选）")]
    [SerializeField] private bool canStayAliveAtZeroHp   = false;
    [SerializeField] private float surviveAtZeroHpHealth = 1f;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private ReviveDecisionState currentDecisionState = ReviveDecisionState.None;

    // 复活道具共享 GCD（独立于普通可使用道具 GCD）
    private static float s_ReviveSharedGCDExpiry = 0f;

    // 复活后短暂无敌帧，防止 HP=0 → Respawn → HP未回满 → 再次触发死亡的循环
    private float _reviveImmunityExpiry = 0f;

    private SkyPrisonPlayerInputRouter _cachedInputRouter;

    public bool IsWaitingForChoice => currentDecisionState == ReviveDecisionState.WaitingForPlayerChoice;
    public ReviveDecisionState CurrentDecisionState => currentDecisionState;

    private void Reset() => AutoSetup();
    private void Awake() => AutoSetup();

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (healthController == null) healthController = GetComponent<UnitHealthController>();
        if (deathController  == null) deathController  = GetComponent<UnitDeathController>();
        if (GetComponent<PlayerDeathGlitchEffect>() == null)
            gameObject.AddComponent<PlayerDeathGlitchEffect>();

        if (GetComponent<PlayerLowHealthVignetteUI>() == null)
            gameObject.AddComponent<PlayerLowHealthVignetteUI>();
    }

    // ── IDeathDecisionHandler ─────────────────────────────────────────────────

    public bool TryHandleZeroHealth(UnitHealthController controller)
    {
        if (controller == null) return false;
        if (healthController == null) healthController = controller;

        // 复活无敌期内忽略死亡判定（防止 HP 回满前的短暂 0 血再次触发死亡循环）
        if (Time.realtimeSinceStartup < _reviveImmunityExpiry) return true;

        // 已在等待玩家决策中，不重复触发
        if (currentDecisionState == ReviveDecisionState.WaitingForPlayerChoice) return true;

        // 0血不死保护
        if (canStayAliveAtZeroHp)
        {
            healthController.SetCurrentHealth(Mathf.Max(0.01f, surviveAtZeroHpHealth));
            currentDecisionState = ReviveDecisionState.Resolved;
            return true;
        }

        // 始终拦截死亡，弹窗让玩家决定
        currentDecisionState = ReviveDecisionState.WaitingForPlayerChoice;

        if (deathController != null && deathController.IsAlive)
        {
            deathController.SetTreatAsRespawnablePlayer(true);
            deathController.Kill();
        }

        // 濒死期间对敌人不可被感知
        GetComponent<UnitPerceptionRuntime>()?.SetCanBePerceived(false);

        // 清除所有敌方 AI 的感知记忆，确保没打过目标的敌人也能退出战斗状态
        foreach (var ai in FindObjectsByType<AIBehaviorPackageRuntimeController>(FindObjectsSortMode.None))
            ai.ClearPerceptionOnPlayerDeath();

        BlockPlayerInput(true);
        StartCoroutine(ShowReviveUIDelayed());

        if (debugLogs)
            Debug.Log($"[PlayerDeathDecision] waiting for decision.", this);

        return true;
    }

    [Header("弹窗延迟（等倒下动画播完）")]
    [SerializeField] private float deathPopupDelay = 1.2f;

    private System.Collections.IEnumerator ShowReviveUIDelayed()
    {
        yield return new WaitForSecondsRealtime(deathPopupDelay);
        InventoryItemEntry reviveEntry = FindReviveItem(out bool onGCD);
        PlayerDeathReviveUI.Show(this, reviveEntry, onGCD);
    }

    // ── 玩家选择：使用复活道具 ────────────────────────────────────────────────

    public void ConfirmUseReviveItem(InventoryItemEntry reviveEntry)
    {
        if (currentDecisionState != ReviveDecisionState.WaitingForPlayerChoice) return;
        currentDecisionState = ReviveDecisionState.Resolved;

        // 无敌期 = 道具设置的 reviveProtectionDuration，至少保留 0.2s 让效果生效
        float protDurationEarly = reviveEntry?.definition?.general?.reviveProtectionDuration ?? 0f;
        _reviveImmunityExpiry = Time.realtimeSinceStartup + Mathf.Max(protDurationEarly, 0.2f);

        // 给最小血量让 Respawn 后不立即再触发死亡
        if (healthController != null && healthController.CurrentHealth <= 0f)
            healthController.SetCurrentHealth(1f);

        // 复活（恢复物理/碰撞/动画）
        if (deathController != null) deathController.Respawn();

        // 复活后恢复可被感知
        GetComponent<UnitPerceptionRuntime>()?.SetCanBePerceived(true);

        // 清除死亡期间 TerrainGroundMotorV5 累积的垂直速度并重置地面状态
        // Motor 始终用 isKinematic=true + MovePosition 驱动，UnitDeathController 不会改 kinematic
        var motor = GetComponent<TerrainGroundMotorV5>();
        if (motor != null) motor.ResetVerticalVelocity();

        if (reviveEntry?.definition != null)
        {
            GameObject playerGo = SkyPrisonPlayerAuthority.Instance != null
                ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject ?? gameObject
                : gameObject;

            ItemEffectExecutor.Execute(reviveEntry.definition.effects, playerGo, reviveEntry.definition);

            if (reviveEntry.definition.general.consumeOnUse)
            {
                InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
                inv?.RemoveItem(reviveEntry.definition, 1);
            }

            // 使用后设置共享 GCD（所有复活道具共同等待）
            float cd = reviveEntry.definition.cooldown;
            if (cd > 0f)
                s_ReviveSharedGCDExpiry = Time.realtimeSinceStartup + cd;
        }
        else if (healthController != null)
        {
            // 无道具定义时兜底给 30% 血
            healthController.SetCurrentHealth(Mathf.Max(1f, healthController.MaxHealth * 0.3f));
        }

        if (protDurationEarly > 0f)
        {
            PlayerReviveProtection prot = GetComponent<PlayerReviveProtection>();
            if (prot == null) prot = gameObject.AddComponent<PlayerReviveProtection>();
            prot.Activate(protDurationEarly);
        }

        BlockPlayerInput(false);
        currentDecisionState = ReviveDecisionState.None; // 重置，允许下次死亡再触发
        if (debugLogs) Debug.Log("[PlayerDeathDecision] revive confirmed", this);
    }

    // ── 玩家选择：接受死亡（掉落全部背包）────────────────────────────────────

    public void DeclineReviveAndDie()
    {
        if (currentDecisionState != ReviveDecisionState.WaitingForPlayerChoice) return;
        currentDecisionState = ReviveDecisionState.Resolved;

        // 统计背包物品数（清空前记录，供结算界面显示）
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        int lostItemCount = 0;
        if (inv != null)
            foreach (var slot in inv.Slots)
                if (slot?.definition != null && slot.count > 0) lostItemCount += slot.count;

        DropAllInventoryItems();

        // 彻底死亡：感知状态保持关闭（单位不会再复活）
        GetComponent<UnitPerceptionRuntime>()?.SetCanBePerceived(false);

        BlockPlayerInput(false);

        if (deathController != null)
        {
            if (deathController.IsWaitingForRespawn)
                deathController.FinalizeDeathFromRespawnWait();
            else if (deathController.IsAlive)
                deathController.Kill();
        }

        if (debugLogs) Debug.Log("[PlayerDeathDecision] revive declined, inventory dropped", this);

        // 显示死亡结算界面，玩家确认后回基地
        int killCount = GetComponent<SessionStatsTracker>()?.KillCount ?? 0;
        DeathSettlementUI.Show(
            killCount:     killCount,
            lostItemCount: lostItemCount
        );
    }

    // ── 工具 ──────────────────────────────────────────────────────────────────

    // 返回所有复活道具及其 GCD 状态（供 UI 左右切换）
    public List<(InventoryItemEntry entry, bool onGCD)> FindAllReviveItems()
    {
        var result = new List<(InventoryItemEntry, bool)>();
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null) return result;

        foreach (InventoryItemEntry slot in inv.Slots)
        {
            if (slot?.definition == null || slot.count <= 0) continue;
            if (!slot.definition.general.isReviveItem) continue;

            // 所有复活道具共享同一个 GCD
            bool gcd = Time.realtimeSinceStartup < s_ReviveSharedGCDExpiry;
            result.Add((slot, gcd));
        }
        return result;
    }

    private InventoryItemEntry FindReviveItem(out bool onGCD)
    {
        onGCD = false;
        var all = FindAllReviveItems();
        if (all.Count == 0) return null;
        // 优先返回无 GCD 的
        foreach (var (e, g) in all) if (!g) return e;
        onGCD = true;
        return all[0].entry;
    }

    private void DropAllInventoryItems()
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null) return;

        Vector3 dropPos = transform.position;

        // 收集所有有效条目
        var toDrop = new List<(ItemDefinition def, int count)>();
        foreach (InventoryItemEntry slot in inv.Slots)
        {
            if (slot?.definition == null || slot.count <= 0) continue;
            toDrop.Add((slot.definition, slot.count));
        }

        // 游戏机制：玩家死亡背包清空，物品不掉落到地面
        foreach (var (def, count) in toDrop)
            inv.RemoveItem(def, count);
    }

    private void BlockPlayerInput(bool block)
    {
        if (_cachedInputRouter == null)
            _cachedInputRouter = FindObjectOfType<SkyPrisonPlayerInputRouter>();
        if (_cachedInputRouter != null)
            _cachedInputRouter.enabled = !block;
    }

    public void SetZeroHpSurvival(bool value, float remainHealth = 1f)
    {
        canStayAliveAtZeroHp   = value;
        surviveAtZeroHpHealth  = Mathf.Max(0.01f, remainHealth);
    }

    public void ClearDecisionState() => currentDecisionState = ReviveDecisionState.None;

    // 所有复活道具共享 GCD，itemId 参数保留以维持调用兼容性
    public static float GetReviveGCDRemaining(int itemId = 0)
        => Mathf.Max(0f, s_ReviveSharedGCDExpiry - Time.realtimeSinceStartup);
}
