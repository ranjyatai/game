using System.Collections;
using UnityEngine;

/// <summary>
/// 丢弃物品时的极小抛出弧线动画。
/// 挂载后自动运行，落地后自毁并启用 LootDropVisual 的悬浮。
/// </summary>
[DisallowMultipleComponent]
public sealed class LootDropTossEffect : MonoBehaviour
{
    /// <summary>从 origin 抛向 target，弧高 arcHeight，用时 duration 秒。</summary>
    public static void Apply(GameObject go, Vector3 origin, Vector3 target,
                             float arcHeight = 0.45f, float duration = 0.32f)
    {
        var fx = go.AddComponent<LootDropTossEffect>();
        fx._origin    = origin;
        fx._target    = target;
        fx._arcHeight = arcHeight;
        fx._duration  = duration;

        // 暂时禁用 Visual，落地后再启用
        var visual = go.GetComponent<LootDropVisual>();
        if (visual != null) visual.enabled = false;
    }

    private Vector3 _origin;
    private Vector3 _target;
    private float   _arcHeight;
    private float   _duration;

    private void Start() => StartCoroutine(Toss());

    private IEnumerator Toss()
    {
        float elapsed = 0f;
        while (elapsed < _duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _duration);
            // 线性插值 + 正弦弧高
            Vector3 pos = Vector3.Lerp(_origin, _target, t);
            pos.y += _arcHeight * Mathf.Sin(Mathf.PI * t);
            transform.position = pos;
            yield return null;
        }

        transform.position = _target;

        // 落地后启用悬浮
        var visual = GetComponent<LootDropVisual>();
        if (visual != null) visual.enabled = true;

        Destroy(this);
    }
}
