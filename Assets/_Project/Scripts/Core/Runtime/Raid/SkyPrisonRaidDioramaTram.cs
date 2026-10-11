using System.Collections.Generic;
using UnityEngine;

/// <summary>沙盘上的高架电车：沿折线来回跑，几节车厢跟着路径走（位置 + 朝向）。</summary>
public sealed class SkyPrisonRaidDioramaTram : MonoBehaviour
{
    public List<Vector3> path = new List<Vector3>();   // 沙盘局部坐标
    public List<Transform> cars = new List<Transform>();
    public float speed = 5f, carSpacing = 1.25f, pause = 1.5f;

    float[] cum; float total; float head; int dir = 1; float wait;

    void Start() { Build(); head = Mathf.Min(total, (cars.Count) * carSpacing + 1f); }

    void Build()
    {
        cum = new float[path.Count]; total = 0f;
        for (int i = 1; i < path.Count; i++) { total += Vector3.Distance(path[i - 1], path[i]); cum[i] = total; }
    }

    Vector3 At(float d, out Vector3 tangent)
    {
        d = Mathf.Clamp(d, 0f, total);
        for (int i = 1; i < path.Count; i++)
            if (d <= cum[i])
            {
                float k = (d - cum[i - 1]) / Mathf.Max(0.0001f, cum[i] - cum[i - 1]);
                tangent = (path[i] - path[i - 1]).normalized; return Vector3.Lerp(path[i - 1], path[i], k);
            }
        tangent = Vector3.forward; return path[path.Count - 1];
    }

    void Update()
    {
        if (cum == null || total <= 0f) return;
        float len = cars.Count * carSpacing;
        if (wait > 0f) wait -= Time.unscaledDeltaTime;
        else
        {
            head += dir * speed * Time.unscaledDeltaTime;
            if (head >= total) { head = total; dir = -1; wait = pause; }
            else if (head <= len) { head = len; dir = 1; wait = pause; }
        }
        for (int i = 0; i < cars.Count; i++)
        {
            if (cars[i] == null) continue;
            var p = At(head - i * carSpacing, out var tan);
            cars[i].localPosition = p; if (tan.sqrMagnitude > 0.001f) cars[i].localRotation = Quaternion.LookRotation(dir > 0 ? tan : tan, Vector3.up);
        }
    }
}
