using System.Collections;
using UnityEngine;

/// <summary>
/// 复活后无敌保护：计时期间角色半透明闪烁，任何伤害被拦截。
/// 由 PlayerDeathDecisionController.ConfirmUseReviveItem 激活。
/// UnitHealthController 在 ApplyDamage 前检查 IsProtected。
/// 不关闭 Renderer（避免战争迷雾等依赖渲染器激活状态的系统跟着闪烁），
/// 改用 MaterialPropertyBlock 改变 alpha。
/// </summary>
[DisallowMultipleComponent]
public class PlayerReviveProtection : MonoBehaviour
{
    [Header("闪烁")]
    [SerializeField] private float blinkInterval = 0.22f;  // 每次切换间隔；越大越慢
    [SerializeField] private float dimAlpha      = 0.15f;  // 暗相时的 alpha

    [Header("调试（只读）")]
    [SerializeField] private bool  isProtected;
    [SerializeField] private float remainingTime;

    public bool IsProtected => isProtected;

    private Coroutine           _routine;
    private Renderer[]          _renderers;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public void Activate(float duration)
    {
        if (duration <= 0f) return;

        if (_routine != null) StopCoroutine(_routine);
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb ??= new MaterialPropertyBlock();
        _routine = StartCoroutine(ProtectionRoutine(duration));
    }

    private IEnumerator ProtectionRoutine(float duration)
    {
        isProtected   = true;
        remainingTime = duration;

        bool  bright    = true;
        float blinkTimer = 0f;

        while (remainingTime > 0f)
        {
            remainingTime -= Time.deltaTime;
            blinkTimer    += Time.deltaTime;

            if (blinkTimer >= blinkInterval)
            {
                blinkTimer = 0f;
                bright = !bright;
                SetAlpha(bright ? 1f : dimAlpha);
            }

            yield return null;
        }

        // 结束：恢复完全不透明
        SetAlpha(1f);
        isProtected   = false;
        remainingTime = 0f;
        _routine      = null;
    }

    private void SetAlpha(float a)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            Color c = _mpb.GetVector(ColorId);
            // GetPropertyBlock 在未赋值时返回零向量；安全取值
            if (c == default) c = Color.white;
            c.a = a;
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }
    }

    private void OnDisable()
    {
        SetAlpha(1f);
        isProtected = false;
    }
}
