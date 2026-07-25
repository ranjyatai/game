using System;
using UnityEngine;

[DisallowMultipleComponent]
public class UnitHealthController : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private UnitDeathController deathController;

    [Header("生命值")]
    [SerializeField] private bool overrideDefaultHealth = false;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    [Header("Regain")]
    [SerializeField] private float regainPool = 0f;
    [SerializeField] private float regainDecayDelay = 2.0f;
    [SerializeField] private float regainDecayPerSecond = 8f;
    [SerializeField] private float regainDelayTimer = 0f;
    [SerializeField] private bool enableRegainDecay = true;

    [Header("规则")]
    [SerializeField] private bool autoFullHealOnAwake = true;
    [SerializeField] private bool clampHealth = true;
    [SerializeField] private bool ignoreDamageWhenDead = true;
    [SerializeField] private bool ignoreHealWhenDead = true;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    public event Action<UnitHealthController, float> OnDamaged;
    public event Action<UnitHealthController, float> OnHealed;
    public event Action<UnitHealthController> OnHealthZero;
    public event Action<UnitHealthController> OnFullHealed;
    public event Action<UnitHealthController, float> OnRegainApplied;
    public event Action<UnitHealthController, float> OnRegainPoolChanged;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthPercent01 => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    public float RegainPool => regainPool;
    public float RegainPoolPercent01 => maxHealth > 0f ? Mathf.Clamp01(regainPool / maxHealth) : 0f;

    public bool IsDead => deathController != null && deathController.IsDeadLike;
    public bool IsAlive => !IsDead;

    private PlayerReviveProtection _reviveProtection;

    private void Reset()
    {
        AutoSetup();
    }

    private void Awake()
    {
        AutoSetup();
        _reviveProtection = GetComponent<PlayerReviveProtection>();

        maxHealth = Mathf.Max(1f, maxHealth);

        if (autoFullHealOnAwake)
            currentHealth = maxHealth;
        else if (clampHealth)
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        ClampRegainPool();
    }

    private void Update()
    {
        TickRegain(Time.deltaTime);
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();
    }

    [ContextMenu("Full Heal")]
    public void FullHeal()
    {
        if (ignoreHealWhenDead && IsDead)
            return;

        float old = currentHealth;
        currentHealth = maxHealth;
        float delta = currentHealth - old;

        if (delta > 0f)
            OnHealed?.Invoke(this, delta);

        OnFullHealed?.Invoke(this);

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: FullHeal -> {currentHealth}/{maxHealth}", this);
    }

    public void SetMaxHealth(float value, bool keepPercent = false)
    {
        float oldPercent = HealthPercent01;
        maxHealth = Mathf.Max(1f, value);

        if (keepPercent)
            currentHealth = maxHealth * oldPercent;

        if (clampHealth)
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        ClampRegainPool();
    }

    public void SetCurrentHealth(float value)
    {
        if (ignoreHealWhenDead && IsDead && value > currentHealth)
            return;

        currentHealth = clampHealth
            ? Mathf.Clamp(value, 0f, maxHealth)
            : value;

        if (currentHealth <= 0f)
            HandleHealthZero();
    }

    public void SetMaxHealthFromStatus(float value)
    {
        // 状态修正导致上限变化：
        // 只恢复容量，不自动恢复当前值
        bool wasUninitialized = currentHealth <= 0f && maxHealth <= 1f;
        maxHealth = Mathf.Max(1f, value);

        // 首次由状态系统设定上限时（currentHealth 仍是未初始化的 0），自动填满
        if (wasUninitialized)
            currentHealth = maxHealth;

        if (clampHealth)
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        ClampRegainPool();

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: SetMaxHealthFromStatus -> {currentHealth}/{maxHealth}, regain={regainPool}", this);
    }

    [ContextMenu("Damage 10")]
    public void Damage10()
    {
        ApplyDamage(10f);
    }

    [ContextMenu("Heal 10")]
    public void Heal10()
    {
        ApplyHeal(10f);
    }

    public void ApplyDamage(float amount)
    {
        ApplyDamage(amount, 0f);
    }

    public void ApplyDamage(float amount, float regainRatio)
    {
        if (amount <= 0f)
            return;

        if (ignoreDamageWhenDead && IsDead)
            return;

        if (_reviveProtection != null && _reviveProtection.IsProtected)
            return;

        float oldHealth = currentHealth;
        currentHealth -= amount;

        if (clampHealth)
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        float actualLost = Mathf.Max(0f, oldHealth - currentHealth);
        if (actualLost > 0f)
            RegisterRegainDamage(actualLost, regainRatio);

        OnDamaged?.Invoke(this, amount);

        // 玩家受击：磨损护甲 + 手柄震动反馈
        if (runtimeBinder?.UnitDefinitionAsset?.characterIdentity == CharacterIdentity.Player)
        {
            SkyPrisonGamepadRumble.PulseOnHit();
            SkyPrisonScreenShake.Shake(intensity: 0.15f, duration: 0.25f);

            if (EquipmentRuntime.Instance != null)
                foreach (var armorEntry in EquipmentRuntime.Instance.GetAllArmor())
                    DurabilitySystem.Wear(armorEntry);
        }

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: Damage {amount} -> {currentHealth}/{maxHealth}, regain={regainPool}", this);

        if (currentHealth <= 0f)
            HandleHealthZero();
    }

    public void ApplyHeal(float amount)
    {
        if (amount <= 0f)
            return;

        if (ignoreHealWhenDead && IsDead)
            return;

        float oldHealth = currentHealth;
        currentHealth += amount;

        if (clampHealth)
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        float actualHealed = Mathf.Max(0f, currentHealth - oldHealth);
        if (actualHealed <= 0f)
            return;

        OnHealed?.Invoke(this, actualHealed);

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: Heal {actualHealed} -> {currentHealth}/{maxHealth}", this);

        if (Mathf.Approximately(currentHealth, maxHealth))
            OnFullHealed?.Invoke(this);
    }

    public float ApplyRegain(float requestedAmount)
    {
        if (requestedAmount <= 0f)
            return 0f;

        if (ignoreHealWhenDead && IsDead)
            return 0f;

        float missing = Mathf.Max(0f, maxHealth - currentHealth);
        if (missing <= 0f || regainPool <= 0f)
            return 0f;

        float actual = Mathf.Min(requestedAmount, regainPool, missing);
        if (actual <= 0f)
            return 0f;

        currentHealth += actual;
        if (clampHealth)
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        regainPool -= actual;
        ClampRegainPool();

        OnRegainApplied?.Invoke(this, actual);
        OnHealed?.Invoke(this, actual);
        OnRegainPoolChanged?.Invoke(this, regainPool);

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: Regain {actual} -> {currentHealth}/{maxHealth}, regain={regainPool}", this);

        if (Mathf.Approximately(currentHealth, maxHealth))
            OnFullHealed?.Invoke(this);

        return actual;
    }

    public void RegisterRegainDamage(float lostAmount, float regainRatio)
    {
        if (lostAmount <= 0f)
            return;

        float ratio = Mathf.Max(0f, regainRatio);
        if (ratio <= 0f)
            return;

        float add = lostAmount * ratio;
        if (add <= 0f)
            return;

        regainPool += add;
        ClampRegainPool();
        regainDelayTimer = Mathf.Max(0f, regainDecayDelay);

        OnRegainPoolChanged?.Invoke(this, regainPool);

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: RegisterRegainDamage lost={lostAmount}, ratio={ratio}, pool={regainPool}", this);
    }

    public void ClearRegainPool()
    {
        if (regainPool <= 0f)
            return;

        regainPool = 0f;
        OnRegainPoolChanged?.Invoke(this, regainPool);
    }

    public void SetRegainDecay(float delay, float perSecond)
    {
        regainDecayDelay = Mathf.Max(0f, delay);
        regainDecayPerSecond = Mathf.Max(0f, perSecond);
    }

    public void ApplyDelta(float delta)
    {
        if (delta < 0f)
            ApplyDamage(-delta);
        else if (delta > 0f)
            ApplyHeal(delta);
    }

    public void ForceKill()
    {
        currentHealth = 0f;
        HandleHealthZero();
    }

    private void TickRegain(float deltaTime)
    {
        if (!enableRegainDecay || deltaTime <= 0f)
            return;

        if (regainPool <= 0f)
            return;

        if (regainDelayTimer > 0f)
        {
            regainDelayTimer -= deltaTime;
            return;
        }

        float decay = Mathf.Max(0f, regainDecayPerSecond) * deltaTime;
        if (decay <= 0f)
            return;

        float old = regainPool;
        regainPool = Mathf.Max(0f, regainPool - decay);

        if (!Mathf.Approximately(old, regainPool))
            OnRegainPoolChanged?.Invoke(this, regainPool);
    }

    private void ClampRegainPool()
    {
        regainPool = Mathf.Clamp(regainPool, 0f, maxHealth);
    }

    private void HandleHealthZero()
    {
        if (currentHealth < 0f && clampHealth)
            currentHealth = 0f;

        OnHealthZero?.Invoke(this);

        if (debugLogs)
            Debug.Log($"[UnitHealthController] {name}: HealthZero", this);

        if (TryHandleZeroHealthByDecisionModules())
            return;

        if (deathController != null && deathController.IsAlive)
            deathController.Kill();
    }

    private bool TryHandleZeroHealthByDecisionModules()
    {
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();
        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour comp = components[i];
            if (comp == null)
                continue;

            if (ReferenceEquals(comp, this))
                continue;

            if (comp is IDeathDecisionHandler handler)
            {
                bool handled = handler.TryHandleZeroHealth(this);
                if (handled)
                {
                    if (debugLogs)
                        Debug.Log($"[UnitHealthController] {name}: zero health handled by {comp.GetType().Name}", this);
                    return true;
                }
            }
        }

        return false;
    }
}
