using System;
using UnityEngine;

/// <summary>
/// AFP 里程碑效果：「生命保险」(效果文字写「死亡豁免」；类名/节点 ID 是 LifeInsurance / seeker_afp_life_insurance)。
/// 「追加生命」在 AFP 窗口里点到 5 级(= +20 点最大生命)，且在 Seeker 科技树里点亮了对应节点，
/// 就获得一次致命保险：受到会把血量打到 0 的伤害时，改为锁在 1 点血，然后进入 300 秒冷却，冷却结束才能再次触发。
/// 通过 ILethalDamageGuard(UnitHealthController.ApplyDamage 的最后一道入口)实现，所以灼烧、触发器等任何扣血路径都有效。
/// 一局内有效：AFP 等级清零后条件不再满足，保险随之失效。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonAfpLifeInsurance : MonoBehaviour, ILethalDamageGuard
{
    public const int   RequiredHpLevel = 5;       // 追加生命 5 级 = +20 点
    public const float CooldownSeconds = 300f;
    public const string NodeId = "seeker_afp_life_insurance";

    public static event Action<SkyPrisonAfpLifeInsurance> OnTriggered;
    public static event Action<SkyPrisonAfpLifeInsurance> OnReady;

    /// <summary>冷却剩余秒数(0 = 可用)。</summary>
    public float CooldownLeft { get; private set; }
    public bool Active => ConditionMet();
    public bool Ready => ConditionMet() && CooldownLeft <= 0f;

    static bool ConditionMet()
    {
        var a = SkyPrisonAfpAbilityTable.Find("insurance");
        return a != null && SkyPrisonAfpAbilityTable.CurrentTier(a) > 0;
    }

    // ── 状态显示 ─────────────────────────────────────────────
    // 就绪：「生命保险」状态(常驻，HUD 状态栏有图标)；触发后换成「生命保险·冷却」状态(定时 300 秒，状态栏显示倒计时)；
    // 冷却结束换回就绪；条件不满足(AFP 等级清零/科技树未解锁)时两个都撤掉。状态定义是运行时创建的，不需要资产文件。
    const string ReadyStatusId = "afp_life_insurance_ready";
    const string CooldownStatusId = "afp_life_insurance_cd";
    static StatusDefinition _readyDef, _cooldownDef;

    static StatusDefinition MakeDef(string id, string nameKey, string nameFallback, string descKey, string descFallback, bool timed, bool buff)
    {
        var asset = Resources.Load<StatusDefinition>("AFP/Status/" + id);
        if (asset != null) return asset;
        var d = ScriptableObject.CreateInstance<StatusDefinition>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.statusId = id;
        d.displayName = DoorText.Get(nameKey, nameFallback);
        d.description = DoorText.Get(descKey, descFallback);
        d.isBuff = buff; d.isHidden = false; d.showInHud = true;
        d.icon = SkyPrisonAfpIcons.GetSprite(SkyPrisonAfpIcons.Insurance);
        d.durationType = timed ? StatusDurationType.Timed : StatusDurationType.Permanent;
        d.baseDuration = timed ? CooldownSeconds : 0f;
        d.canStack = false; d.baseStack = 1; d.maxStack = 1;
        d.durationUpdateMode = StatusDurationUpdateMode.Override;
        return d;
    }

    static StatusDefinition ReadyDef => _readyDef != null ? _readyDef
        : _readyDef = MakeDef(ReadyStatusId, "status_afp_insurance_ready", "生命保险", "status_afp_insurance_ready_desc", "受到致命伤害时豁免这一次死亡，血量保留 1 点，随后进入冷却。", false, true);
    static StatusDefinition CooldownDef => _cooldownDef != null ? _cooldownDef
        : _cooldownDef = MakeDef(CooldownStatusId, "status_afp_insurance_cd", "生命保险·冷却", "status_afp_insurance_cd_desc", "死亡豁免冷却中，结束后恢复可用。", true, false);

    UnitStatusController _status;

    UnitStatusController Status()
    {
        if (_status != null) return _status;
        _status = GetComponentInParent<UnitStatusController>() ?? GetComponentInChildren<UnitStatusController>(true);
        if (_status == null)
        {
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (player != null) _status = player.GetComponentInChildren<UnitStatusController>(true) ?? UnitStatusController.EnsureOnRoot(player.gameObject);
        }
        return _status;
    }

    void SyncStatuses()
    {
        var sc = Status();
        if (sc == null) return;
        bool active = ConditionMet();
        bool wantReady = active && CooldownLeft <= 0f;
        bool wantCd = active && CooldownLeft > 0f;
        if (sc.HasStatus(ReadyStatusId) != wantReady)
        {
            if (wantReady) sc.ApplyStatus(ReadyDef, gameObject);
            else sc.RemoveStatus(ReadyStatusId);
        }
        if (sc.HasStatus(CooldownStatusId) != wantCd)
        {
            if (wantCd) sc.ApplyStatus(CooldownDef, gameObject, 1, CooldownLeft);
            else sc.RemoveStatus(CooldownStatusId);
        }
    }

    void OnDisable()
    {
        var sc = _status;
        if (sc == null) return;
        sc.RemoveStatus(ReadyStatusId); sc.RemoveStatus(CooldownStatusId);
    }

    void Update()
    {
        if (CooldownLeft > 0f)
        {
            CooldownLeft -= Time.deltaTime;
            if (CooldownLeft <= 0f) { CooldownLeft = 0f; if (ConditionMet()) OnReady?.Invoke(this); }
        }
        SyncStatuses();
    }

    public float GuardLethal(float currentHealth, float amount)
    {
        if (CooldownLeft > 0f || !ConditionMet()) return amount;
        CooldownLeft = CooldownSeconds;
        OnTriggered?.Invoke(this);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityInsurance);
        return Mathf.Max(0f, currentHealth - 1f);      // 血已经只剩 1 点时变成 0 伤害：仍然活着，但这次保险用掉了
    }

    /// <summary>测试用：立刻清冷却。</summary>
    public void ResetCooldown() { CooldownLeft = 0f; }

    public static SkyPrisonAfpLifeInsurance FindOnPlayer()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        return player != null ? player.GetComponentInChildren<SkyPrisonAfpLifeInsurance>(true) : null;
    }
}
