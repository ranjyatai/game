using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 灰色损失：单位「已被侵蚀掉的最大生命」。灰炉的瘴气每秒往里加，血条上显示为右端不断变长的灰色段，
/// 损失占满（≥ 基础最大生命）就直接死亡。
/// 不做成状态：它是连续累加、离开范围也不消失的量，只有医疗站/道具才能恢复，没有持续时间。
/// 实现上走现成的战斗属性外部修正（UnitBattleStatRuntime.SetExternalModifiers，对 maxHp 加负值），
/// 所以装备/血量上限显示/受击判定都沿用同一套血量逻辑，不另写一份。
/// 切场景（回据点、进下一局）自动清零。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonGrayLoss : MonoBehaviour
{
    private const string ModifierSource = "GrayLoss";

    [Header("自然恢复（很慢，医疗站/道具更快）")]
    [Tooltip("最后一次被灰色侵蚀后，多少秒才开始自然恢复。")]
    [SerializeField] private float naturalRecoverDelaySeconds = 3f;
    [Tooltip("自然恢复速度：每秒恢复「基础最大生命」的百分比（0.04 = 4%/秒，25 秒清完）。0 = 不自然恢复。")]
    [SerializeField] private float naturalRecoverPercentPerSecond = 0.04f;

    private float _lastAddTime = -999f;
    private float _recoverAccumulator;

    private UnitHealthController _health;
    private UnitBattleStatRuntime _stats;
    private float _loss;

    /// <summary>当前已损失的最大生命（绝对值）。</summary>
    public float Loss => _loss;

    /// <summary>「没有灰色时」的最大生命 = 当前上限 + 已损失。血条比例以它为分母。</summary>
    public float BaseMaxHealth => (_health != null ? _health.MaxHealth : 0f) + _loss;

    /// <summary>灰色占血条的比例 0~1。</summary>
    public float Ratio01 => BaseMaxHealth > 0.01f ? Mathf.Clamp01(_loss / BaseMaxHealth) : 0f;

    public static SkyPrisonGrayLoss EnsureOn(GameObject unitRoot)
    {
        if (unitRoot == null) return null;
        var gl = unitRoot.GetComponent<SkyPrisonGrayLoss>();
        if (gl == null) gl = unitRoot.AddComponent<SkyPrisonGrayLoss>();
        return gl;
    }

    /// <summary>读取某个单位当前的灰色比例；没有该组件就是 0。</summary>
    public static float GetRatio(GameObject unitRoot)
    {
        if (unitRoot == null) return 0f;
        var gl = unitRoot.GetComponentInParent<SkyPrisonGrayLoss>(true);
        return gl != null ? gl.Ratio01 : 0f;
    }

    private void Awake()
    {
        Resolve();
        // 灰色越多画面颗粒越明显（跟着灰色占比走，离开后慢慢退）
        if (GetComponent<SkyPrisonGrayLossScreenGrain>() == null)
            gameObject.AddComponent<SkyPrisonGrayLossScreenGrain>();
    }

    private void Update()
    {
        if (_loss <= 0f || naturalRecoverPercentPerSecond <= 0f) return;
        if (Time.time - _lastAddTime < naturalRecoverDelaySeconds) return;
        if (_health == null || _health.IsDead) return;

        // 累积一小段再恢复，避免每帧都重算一遍属性
        _recoverAccumulator += BaseMaxHealth * naturalRecoverPercentPerSecond * Time.deltaTime;
        if (_recoverAccumulator >= 0.5f || _recoverAccumulator >= _loss)
        {
            Recover(_recoverAccumulator);
            _recoverAccumulator = 0f;
        }
    }

    private void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;
    private void OnDisable() => SceneManager.activeSceneChanged -= OnSceneChanged;

    private void OnSceneChanged(Scene a, Scene b) => Clear();

    private void Resolve()
    {
        if (_health == null) _health = GetComponent<UnitHealthController>() ?? GetComponentInChildren<UnitHealthController>(true);
        if (_stats == null) _stats = UnitBattleStatRuntime.EnsureOnRoot(gameObject);
    }

    /// <summary>增加灰色损失；占满即死。</summary>
    public void AddLoss(float amount)
    {
        if (amount <= 0f) return;
        Resolve();
        if (_health == null || _health.IsDead) return;

        float baseMax = BaseMaxHealth;
        _loss = Mathf.Min(_loss + amount, baseMax);
        _lastAddTime = Time.time;
        Apply();

        // 最大生命被压到 1 以下无意义（属性层最小为 1）：占满按死亡处理
        if (_loss >= baseMax - 1f)
            _health.ApplyDamage(_health.MaxHealth + _health.CurrentHealth + 1f);
    }

    /// <summary>恢复灰色（医疗站/道具接口）。</summary>
    public void Recover(float amount)
    {
        if (amount <= 0f || _loss <= 0f) return;
        _loss = Mathf.Max(0f, _loss - amount);
        Apply();
    }

    public void Clear()
    {
        if (_loss <= 0f) return;
        _loss = 0f;
        Apply();
    }

    private void Apply()
    {
        Resolve();
        if (_stats == null) return;
        if (_loss <= 0f) { _stats.ClearExternalModifiers(ModifierSource); return; }
        _stats.SetExternalModifiers(ModifierSource, new List<StatModifier> { new StatModifier("maxHp", -_loss, StatModifierOp.Add) });
    }
}
