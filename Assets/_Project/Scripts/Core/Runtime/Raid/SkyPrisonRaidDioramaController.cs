using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 选关界面的微缩沙盘（权游片头那种机械沙盘）的运行时控制：
/// - 升起动画：塔先起，街区按离塔的距离一圈圈往外升，据点（旧工厂等）最后升起。用 Evaluate(t) 按时间点求值，所以可以在编辑器里固定在任意帧截图。
/// - 机械环：围着塔的几圈环以不同速度转。
/// - 镜头：围绕目标点的 轨道相机（距离 / 俯仰 / 方位），FlyTo 平滑飞过去；景深焦点跟着目标距离走。
/// 只负责表现，不碰游戏流程。builder（SkyPrisonRaidDioramaProtoBuilder）在编辑器里把引用填好。
/// </summary>
public sealed class SkyPrisonRaidDioramaController : MonoBehaviour
{
    [Serializable] public class RiseGroup { public Transform root; public float delay; public float duration = 1.1f; public float overshoot = 1.5f; public float baseY = 1f;
        public bool onView;                 // true：进入画面后才开始升起（据点）
        [NonSerialized] public float startedAt = -1f, progress; [NonSerialized] public Vector3 centerLocal; }
    [Serializable] public class Spinner { public Transform t; public float degPerSec; }
    [Serializable] public class Shot { public string name; public Vector3 target; public float distance = 120f, elevation = 48f, azimuth = -20f; }

    public Camera cam;
    public Light followLight;           // 舞台聚光：始终打在镜头看的那一点上，随平移 / 缩放移动
    public float followBase = 200f;
    public Volume volume;
    public List<RiseGroup> rises = new List<RiseGroup>();
    public List<Spinner> spinners = new List<Spinner>();
    public List<Shot> shots = new List<Shot>();
    public bool autoPlay = true;
    [Min(0.05f)] public float flySmooth = 0.7f;

    public bool applyAmbient; public Color ambient = new Color(0.2f, 0.23f, 0.32f);
    public float Clock => t;
    float t;
    Color prevAmbient; UnityEngine.Rendering.AmbientMode prevMode; bool ambientSaved;
    Vector3 target, targetVel; float dist, distVel, elev, elevVel, azim, azimVel;
    /// <summary>镜头当前（平滑后）对着的点，沙盘本地坐标。选关界面右侧的竖向刻度读它的 Z 来滚动。</summary>
    public Vector3 TargetLocal => target;
    /// <summary>镜头正在飞向的目标点（沙盘本地坐标）；没有目标时就是当前点。</summary>
    public Vector3 GoalTargetLocal => goal != null ? goal.target : target;
    Shot goal;
    DepthOfField dof;

    void OnEnable()
    {
        if (!applyAmbient || ambientSaved) return;
        prevMode = RenderSettings.ambientMode; prevAmbient = RenderSettings.ambientLight; ambientSaved = true;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = ambient;
    }

    void OnDisable()
    {
        if (!ambientSaved) return;
        RenderSettings.ambientMode = prevMode; RenderSettings.ambientLight = prevAmbient; ambientSaved = false;
    }

    /// <summary>沙盘里局部坐标 → 世界坐标（沙盘可以放在世界任意位置）。</summary>
    /// <summary>某个升起物（据点）已经升起了多少（0~1）；没有登记的当作已升完。</summary>
    public float RiseProgress(Transform root)
    {
        foreach (var g in rises) if (g.root == root) return g.progress;
        return 1f;
    }

    /// <summary>让某个据点立刻开始升起（镜头切到它 / 玩家选中它时调用），不用再等"镜头够近 + 在画面中央"的条件。</summary>
    public void RiseNow(Transform root)
    {
        foreach (var g in rises)
            if (g.root == root && g.onView && g.startedAt < 0f) g.startedAt = Time.unscaledTime;
    }

    public Vector3 ToWorld(Vector3 local) { return transform.TransformPoint(local); }

    void Start()
    {
        foreach (var g in rises)
        {
            if (g.root == null || !g.onView) continue;
            Bounds b = default; bool first = true;
            foreach (var r in g.root.GetComponentsInChildren<Renderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            g.centerLocal = first ? transform.InverseTransformPoint(g.root.position) : transform.InverseTransformPoint(b.center);
        }
        if (volume != null && volume.profile != null) volume.profile.TryGet(out dof);
        if (shots.Count > 0) SnapTo(0);
        Evaluate(autoPlay ? 0f : 999f);
    }

    public void SnapTo(int i) { var s = shots[i]; goal = s; target = s.target; dist = s.distance; elev = s.elevation; azim = s.azimuth; Apply(); }
    public void FlyTo(int i) { goal = shots[i]; }
    public void FlyTo(string name) { var s = shots.Find(x => x.name == name); if (s != null) goal = s; }
    public void Restart() { t = 0f; }

    // ---- 手动操作（选关界面的缩放 / 拖动）：改的是相机的目标点和距离，景深焦点始终跟着屏幕中心走 ----
    Shot manual;
    public float orbitSpeed = 0.055f;   // 右键拖动转动的速度（度 / 像素）
    public float RiseMaxDist = 90f;     // 据点只有在镜头拉近到这个距离以内、并且在画面中央区域时才升起
    public float panSpeed = 0.55f;   // 拖动平移的速度倍率（1 = 地面跟手 1:1）
    public float MinDist = 22f, MaxDist = 100f;   // 最远：整张地图刚好装满画面，看不到边界
    Shot Manual()
    {
        if (manual == null) manual = new Shot { name = "manual" };
        if (goal != manual) { manual.target = target; manual.distance = dist; manual.elevation = elev; manual.azimuth = azim; goal = manual; }
        return manual;
    }
    public void ZoomBy(float factor) { var m = Manual(); m.distance = Mathf.Clamp(m.distance * factor, MinDist, MaxDist); ClampTarget(m); }

    /// <summary>目标点限制在地图范围内（留一点边距）。地图外有地板和低矮的郊区六边形，推到边上也不会露出黑底。</summary>
    void ClampTarget(Shot m)
    {
        float cx = Mathf.Clamp(m.target.x, -72f, 64f);
        // 北边（+z）的上限随 x 变化：东侧（黄金乡 z≈25、废病院 z≈14 所在）到 26，往西逐渐收到 -2——塔以西偏北那一大片没有任何据点，
        // 镜头推到那里只会看到空地和沙盘边缘。
        float zMax = Mathf.Lerp(-2f, 26f, Mathf.InverseLerp(-72f, 8f, cx));
        m.target = new Vector3(cx, 0f, Mathf.Clamp(m.target.z, -26f, zMax));   // 右边放宽到能把最右的黄金乡（x≈48）拉到右侧窗口左边，不再贴着边被窗口压住
    }
    public void PanByPixels(Vector2 d)
    {
        var m = Manual(); if (cam == null) return;
        float upp = 2f * m.distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Screen.height) * panSpeed;
        float az = m.azimuth * Mathf.Deg2Rad; var fwd = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az)); var right = new Vector3(Mathf.Cos(az), 0f, -Mathf.Sin(az));
        var tg = m.target - right * d.x * upp - fwd * d.y * upp / Mathf.Max(0.3f, Mathf.Sin(m.elevation * Mathf.Deg2Rad));
        m.target = new Vector3(tg.x, 0f, tg.z); ClampTarget(m);
    }
    /// <summary>略微调整观看角度：左右转（方位）和上下俯仰，都限制在小范围内。</summary>
    public void Orbit(Vector2 d)
    {
        var m = Manual();
        m.azimuth = Mathf.Clamp(m.azimuth + d.x * orbitSpeed, -35f, 35f);
        m.elevation = Mathf.Clamp(m.elevation - d.y * orbitSpeed * 0.65f, 40f, 62f);
        ClampTarget(m);
    }
    /// <summary>把镜头移到某个世界坐标正上方（据点中央），保持当前角度；距离收到合适的近景。</summary>
    public void FocusOn(Vector3 worldPos, float distance)
    {
        var m = Manual();
        var lp = transform.InverseTransformPoint(worldPos);
        m.target = new Vector3(lp.x, 0f, lp.z);
        if (distance > 0f) m.distance = Mathf.Clamp(distance, MinDist, MaxDist);   // distance <= 0：只平移、保持当前缩放
        ClampTarget(m);
    }
    public void ResetView() { if (shots.Count > 0) goal = shots[0]; }
    public void SetTime(float time) { t = time; Evaluate(t); }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (cam != null)
            foreach (var g in rises)
            {
                if (!g.onView || g.startedAt >= 0f) continue;
                var vp = cam.WorldToViewportPoint(transform.TransformPoint(g.centerLocal));
                if (vp.z > 0f && dist <= Mathf.Max(RiseMaxDist, 160f) && vp.x > 0.18f && vp.x < 0.78f && vp.y > 0.15f && vp.y < 0.85f) g.startedAt = Time.unscaledTime;   // 据点移到画面中央区域就升起，不看镜头远近（默认缩放离据点约 105，之前 ≤90 的条件永远满足不了）   // 进入画面后稍等一下再升
            }
        if (autoPlay) { t += dt; Evaluate(t); }
        foreach (var sp in spinners) if (sp.t != null) sp.t.Rotate(0f, sp.degPerSec * dt, 0f, Space.Self);
        if (goal != null)
        {
            target = Vector3.SmoothDamp(target, goal.target, ref targetVel, flySmooth, Mathf.Infinity, dt);
            dist = Mathf.SmoothDamp(dist, goal.distance, ref distVel, flySmooth, Mathf.Infinity, dt);
            elev = Mathf.SmoothDampAngle(elev, goal.elevation, ref elevVel, flySmooth, Mathf.Infinity, dt);
            azim = Mathf.SmoothDampAngle(azim, goal.azimuth, ref azimVel, flySmooth, Mathf.Infinity, dt);
        }
        Apply();
    }

    void Apply()
    {
        if (cam == null) return;
        var rot = Quaternion.Euler(elev, azim, 0f);
        cam.transform.SetPositionAndRotation(transform.TransformPoint(target) - rot * Vector3.forward * dist, rot);
        if (followLight != null)
        {
            var tw = transform.TransformPoint(target);
            float h = Mathf.Clamp(dist * 0.55f, 12f, 60f);                                                 // 灯高随距离，但上限压住，光圈保持"一小圈"
            var back = Quaternion.Euler(0f, azim, 0f) * Vector3.back;
            followLight.transform.position = tw + Vector3.up * h + back * (h * 0.25f);
            followLight.transform.rotation = Quaternion.LookRotation(tw + Vector3.up * 1.5f - followLight.transform.position);
            followLight.range = h * 3f; followLight.intensity = followBase * (h / 17f) * (h / 17f);     // 距离平方补偿，亮度保持不变
        }
        if (dof != null && dof.active)
        {
            dof.focusDistance.value = dist;                                   // Bokeh 模式：焦点始终在目标上
            dof.gaussianStart.value = dist * 1.05f; dof.gaussianEnd.value = dist * 2.4f;   // Gaussian 模式
        }
    }

    /// <summary>按时间点 time（秒）求所有升起物的缩放。起点缩放 0，终点 1，带一点回弹。</summary>
    public void Evaluate(float time)
    {
        foreach (var g in rises)
        {
            if (g.root == null) continue;
            float k = g.onView
                ? (time >= 90f ? 1f : (g.startedAt < 0f ? 0f : Mathf.Clamp01((Time.unscaledTime - g.startedAt) / Mathf.Clamp(g.duration, 0.01f, 1.2f))))
                : Mathf.Clamp01((time - g.delay) / Mathf.Max(0.01f, g.duration));
            g.progress = k;
            float y = (k <= 0f ? 0.0001f : EaseOutBack(k, g.overshoot)) * g.baseY;
            var s = g.root.localScale; g.root.localScale = new Vector3(s.x, Mathf.Max(0.0001f, y), s.z);
            g.root.gameObject.SetActive(k > 0f);
        }
    }

    static float EaseOutBack(float x, float c1) { float c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }
}
