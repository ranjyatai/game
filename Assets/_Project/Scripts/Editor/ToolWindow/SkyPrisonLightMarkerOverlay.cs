using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene 视图里给 WorldRoot/LightRoot 下所有灯画标记：灯头小球 + 垂线到地面 + 范围示意 + 名字。
///
/// 「纯照明」灯没有任何网格和光束，放下去在 Scene 里什么也看不见，找不到它在哪、范围多大。
/// 这些标记只用 Handles 画，只存在于 Scene 视图：不是场景物体，不进 Game 视图，也不会存进场景。
/// 菜单「天空囚笼/场景视图 灯光标记」可以开关，选中的灯画得更亮。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonLightMarkerOverlay
{
    private const string PrefKey = "SkyPrison.LightMarkerOverlay";
    private const string MenuPath = "天空囚笼/场景视图 灯光标记";
    private const string LightRootPath = "WorldRoot/LightRoot";

    // 灯一多（一张地图几十盏）常驻文字会叠成一片：远处的灯只画灯头和垂线，
    // 近处或选中的才画文字、光锥。数值是 Scene 相机到灯的距离（米）。
    private const float LabelDistance = 40f;
    private const float ConeDistance = 60f;

    private static readonly List<Light> s_Lights = new List<Light>();
    private static double s_NextRefreshTime;

    static SkyPrisonLightMarkerOverlay()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    [MenuItem(MenuPath, false, 4)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        SceneView.RepaintAll();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void RefreshLights()
    {
        s_Lights.Clear();
        GameObject root = GameObject.Find(LightRootPath.Substring(LightRootPath.LastIndexOf('/') + 1));
        if (root == null)
            return;

        s_Lights.AddRange(root.GetComponentsInChildren<Light>(false));
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!Enabled || Event.current.type != EventType.Repaint)
            return;

        // 每秒重新收集一次，不在每次重绘里全场查找。
        if (EditorApplication.timeSinceStartup >= s_NextRefreshTime)
        {
            s_NextRefreshTime = EditorApplication.timeSinceStartup + 1.0;
            RefreshLights();
        }

        for (int i = 0; i < s_Lights.Count; i++)
        {
            Light l = s_Lights[i];
            if (l == null || !l.isActiveAndEnabled)
                continue;
            DrawMarker(l, IsSelected(l), sceneView.camera != null ? sceneView.camera.transform.position : l.transform.position);
        }
    }

    private static bool IsSelected(Light l)
    {
        Transform t = Selection.activeTransform;
        return t != null && (l.transform == t || l.transform.IsChildOf(t));
    }

    /// <summary>估算灯正下方地面上的亮度（URP 附加光的物理衰减：强度 ÷ 距离²，再乘范围边缘的平滑窗口，
    /// 聚光灯再乘锥角边缘）。不含颜色和材质反照率，只用来判断「这盏灯到底能不能把地面照亮」。
    /// 经验值：≥ 0.3 明显可见，0.1~0.3 偏暗，< 0.1 基本看不出来。</summary>
    public static float EstimateGroundIrradiance(float intensity, float range, float spotAngle, bool isSpot, float height, float horizontal)
    {
        float d2 = height * height + horizontal * horizontal;
        float r2 = range * range;
        if (d2 >= r2) return 0f;

        float window = Mathf.Clamp01(1f - (d2 / r2) * (d2 / r2));
        window *= window;
        float e = intensity / Mathf.Max(d2, 0.0001f) * window;

        if (isSpot)
        {
            // 光线方向和灯朝向（正下方）的夹角；只算正下方时就是 horizontal=0，夹角 0。
            float cosA = height / Mathf.Sqrt(d2);
            float cosOuter = Mathf.Cos(spotAngle * 0.5f * Mathf.Deg2Rad);
            if (cosA < cosOuter) return 0f;
            float cosInner = Mathf.Cos(spotAngle * 0.5f * 0.7f * Mathf.Deg2Rad);
            e *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cosOuter, cosInner, cosA));
        }
        return e;
    }

    private static void DrawMarker(Light l, bool selected, Vector3 cameraPosition)
    {
        Vector3 pos = l.transform.position;
        float camDistance = Vector3.Distance(cameraPosition, pos);
        bool nearLabel = selected || camDistance <= LabelDistance;
        bool nearCone = selected || camDistance <= ConeDistance;
        float size = HandleUtility.GetHandleSize(pos);
        Color c = l.color;
        float a = selected ? 1f : 0.7f;

        // 灯头：灯光颜色的小球，加一圈白边，暗背景上也看得见。
        Handles.color = new Color(1f, 1f, 1f, a);
        Handles.SphereHandleCap(0, pos, Quaternion.identity, size * 0.2f, EventType.Repaint);
        Handles.color = new Color(c.r, c.g, c.b, a);
        Handles.SphereHandleCap(0, pos, Quaternion.identity, size * 0.14f, EventType.Repaint);

        // 垂线到地面 + 地面落点圈：知道灯挂多高、正下方是哪。
        Vector3 ground = new Vector3(pos.x, 0f, pos.z);
        if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 200f))
            ground = hit.point;
        Handles.color = new Color(c.r, c.g, c.b, 0.45f * a);
        Handles.DrawDottedLine(pos, ground, 4f);
        Handles.DrawWireDisc(ground, Vector3.up, 0.25f);

        // 范围示意：点光源画三个方向的圈；聚光灯画光锥边线 + 末端圈（和摆放预览同一套画法）。
        Handles.color = new Color(c.r, c.g, c.b, (selected ? 0.9f : 0.35f));
        if (!nearCone)
        {
            // 远处：不画光锥/范围圈，避免几十盏灯的线条糊成一片。
        }
        else if (l.type == LightType.Spot)
        {
            Vector3 dir = l.transform.forward;
            Vector3 end = pos + dir * l.range;
            float er = l.range * Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad);
            Vector3 right = Vector3.Cross(dir, Vector3.up).sqrMagnitude > 0.001f
                ? Vector3.Cross(dir, Vector3.up).normalized
                : Vector3.right;
            Vector3 up = Vector3.Cross(right, dir).normalized;
            Handles.DrawWireDisc(end, dir, er);
            Handles.DrawLine(pos, end + right * er);
            Handles.DrawLine(pos, end - right * er);
            Handles.DrawLine(pos, end + up * er);
            Handles.DrawLine(pos, end - up * er);
        }
        else if (l.type == LightType.Point)
        {
            Handles.DrawWireDisc(pos, Vector3.up, l.range);
            Handles.DrawWireDisc(pos, Vector3.right, l.range);
            Handles.DrawWireDisc(pos, Vector3.forward, l.range);
        }

        // 名字（去掉 PF_Light_ 前缀和 (Clone)），固定在灯头上方。
        // 取灯自己所在的预制体实例名：以前一路往上爬到 LightRoot 的直接子物体，灯被放进分组容器后
        // 所有灯都显示成容器的名字。
        Transform nameSource = l.transform;
        GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(l.gameObject);
        if (instanceRoot != null)
            nameSource = instanceRoot.transform;
        string label = nameSource.name.Replace("(Clone)", "").Replace("PF_Light_", "");

        // 灯正下方地面的亮度估算。聚光灯朝向不一定是正下方，这里只估「如果照向正下方」的上限，够判断高度/强度是否合理。
        float height = Mathf.Max(0.01f, pos.y - ground.y);
        float e = EstimateGroundIrradiance(l.intensity, l.range, l.spotAngle, l.type == LightType.Spot, height, 0f);
        bool weak = e < 0.1f;
        // 文字只给选中的灯和离相机近的灯；远处的灯如果太暗，仍留一个短的 ⚠，问题不会被藏起来。
        if (!nearLabel && !weak)
            return;

        string text;
        if (selected)
            text = weak ? $"{label}  ⚠ 地面亮度≈{e:0.00}（太暗，降低高度或加强度）" : $"{label}  地面亮度≈{e:0.00}";
        else if (nearLabel)
            text = weak ? $"{label}  ⚠ {e:0.00}" : $"{label}  {e:0.0}";
        else
            text = "⚠";

        var style = new GUIStyle(EditorStyles.whiteBoldLabel);
        if (weak) style.normal.textColor = new Color(1f, 0.45f, 0.35f);
        Handles.Label(pos + Vector3.up * size * 0.3f, text, style);
    }
}
