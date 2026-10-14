using UnityEngine;

/// <summary>沙盘上的小装饰：上下起伏（旋转木马的马）。</summary>
public sealed class SkyPrisonRaidDioramaBob : MonoBehaviour
{
    public float amplitude = 0.18f, speed = 2.2f, phase;
    float baseY; bool init;

    void LateUpdate()
    {
        if (!init) { baseY = transform.localPosition.y; init = true; }
        var p = transform.localPosition; p.y = baseY + Mathf.Sin(Time.unscaledTime * speed + phase) * amplitude; transform.localPosition = p;
    }
}
