#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene 视图坐标显示辅助。
/// 用于 AI 行动句、地图点位、巡逻点调试：在 SceneView 中显示 X/Z 坐标网格。
/// 放到任意 Editor 文件夹下即可，例如：
/// Assets/_Project/Scripts/Editor/SkyPrisonEditor/Scene/SkyPrisonSceneCoordinateOverlay.cs
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonSceneCoordinateOverlay
{
    private const string PrefEnabled = "SkyPrison.SceneCoordinateOverlay.Enabled";
    private const string PrefStep = "SkyPrison.SceneCoordinateOverlay.Step";
    private const string PrefRange = "SkyPrison.SceneCoordinateOverlay.Range";
    private const string PrefUseMapBounds = "SkyPrison.SceneCoordinateOverlay.UseMapBounds";

    private static readonly Color GridLineColor = new Color(0.25f, 0.85f, 1.00f, 0.20f);
    private static readonly Color AxisLineColor = new Color(0.35f, 1.00f, 0.65f, 0.55f);
    private static readonly Color LabelColor = new Color(0.82f, 0.96f, 1.00f, 0.92f);
    private static readonly Color OriginLabelColor = new Color(0.55f, 1.00f, 0.70f, 1.00f);

    private static GUIStyle labelStyle;
    private static GUIStyle originLabelStyle;

    static SkyPrisonSceneCoordinateOverlay()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefEnabled, true);
        set
        {
            EditorPrefs.SetBool(PrefEnabled, value);
            SceneView.RepaintAll();
        }
    }

    private static bool UseMapBounds
    {
        get => EditorPrefs.GetBool(PrefUseMapBounds, true);
        set
        {
            EditorPrefs.SetBool(PrefUseMapBounds, value);
            SceneView.RepaintAll();
        }
    }

    private static float Step
    {
        get => Mathf.Max(1f, EditorPrefs.GetFloat(PrefStep, 8f));
        set
        {
            EditorPrefs.SetFloat(PrefStep, Mathf.Max(1f, value));
            SceneView.RepaintAll();
        }
    }

    private static float FallbackRange
    {
        get => Mathf.Max(16f, EditorPrefs.GetFloat(PrefRange, 128f));
        set
        {
            EditorPrefs.SetFloat(PrefRange, Mathf.Max(16f, value));
            SceneView.RepaintAll();
        }
    }

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/显示 Scene 坐标 %#g")]
    private static void ToggleEnabled()
    {
        Enabled = !Enabled;
    }

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/显示 Scene 坐标 %#g", true)]
    private static bool ToggleEnabledValidate()
    {
        Menu.SetChecked("Tools/Sky Prison/Scene/坐标网格/显示 Scene 坐标 %#g", Enabled);
        return true;
    }

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/优先使用 MapBounds")]
    private static void ToggleUseMapBounds()
    {
        UseMapBounds = !UseMapBounds;
    }

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/优先使用 MapBounds", true)]
    private static bool ToggleUseMapBoundsValidate()
    {
        Menu.SetChecked("Tools/Sky Prison/Scene/坐标网格/优先使用 MapBounds", UseMapBounds);
        return true;
    }

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/网格间隔 4")]
    private static void SetStep4() => Step = 4f;

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/网格间隔 8")]
    private static void SetStep8() => Step = 8f;

    [MenuItem("Tools/Sky Prison/Scene/坐标网格/网格间隔 16")]
    private static void SetStep16() => Step = 16f;

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!Enabled || sceneView == null)
            return;

        EnsureStyles();

        Vector3 center;
        Vector3 size;
        if (!TryGetMapBounds(out center, out size))
        {
            center = sceneView.pivot;
            size = new Vector3(FallbackRange, 0f, FallbackRange);
        }

        DrawCoordinateGrid(center, size, Step);
    }

    private static bool TryGetMapBounds(out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;

        if (!UseMapBounds)
            return false;

        // 避免直接依赖 SkyPrisonMapBounds 类型字段名。
        // 只要场景里有名为 MapBounds 的对象，就先按 Transform/BoxCollider 估算。
        GameObject mapBoundsObject = GameObject.Find("MapBounds");
        if (mapBoundsObject == null)
            return false;

        center = mapBoundsObject.transform.position;

        BoxCollider box = mapBoundsObject.GetComponent<BoxCollider>();
        if (box != null)
        {
            center = mapBoundsObject.transform.TransformPoint(box.center);
            size = Vector3.Scale(box.size, mapBoundsObject.transform.lossyScale);
        }
        else
        {
            // 兼容你当前的 MapBounds 可视对象：没有 Collider 时用 Transform 缩放估算。
            Vector3 scale = mapBoundsObject.transform.lossyScale;
            size = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        if (size.x <= 0.01f || size.z <= 0.01f)
        {
            size.x = FallbackRange;
            size.z = FallbackRange;
        }

        return true;
    }

    private static void DrawCoordinateGrid(Vector3 center, Vector3 size, float step)
    {
        float minX = Mathf.Floor((center.x - size.x * 0.5f) / step) * step;
        float maxX = Mathf.Ceil((center.x + size.x * 0.5f) / step) * step;
        float minZ = Mathf.Floor((center.z - size.z * 0.5f) / step) * step;
        float maxZ = Mathf.Ceil((center.z + size.z * 0.5f) / step) * step;
        float y = center.y + 0.05f;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        for (float x = minX; x <= maxX + 0.001f; x += step)
        {
            bool isAxis = Mathf.Abs(x) < 0.001f;
            Handles.color = isAxis ? AxisLineColor : GridLineColor;
            Handles.DrawLine(new Vector3(x, y, minZ), new Vector3(x, y, maxZ));

            Vector3 labelPos = new Vector3(x, y, minZ);
            DrawSceneLabel(labelPos, $"X {FormatNumber(x)}", isAxis);
        }

        for (float z = minZ; z <= maxZ + 0.001f; z += step)
        {
            bool isAxis = Mathf.Abs(z) < 0.001f;
            Handles.color = isAxis ? AxisLineColor : GridLineColor;
            Handles.DrawLine(new Vector3(minX, y, z), new Vector3(maxX, y, z));

            Vector3 labelPos = new Vector3(minX, y, z);
            DrawSceneLabel(labelPos, $"Z {FormatNumber(z)}", isAxis);
        }

        DrawSceneLabel(new Vector3(0f, y, 0f), "Origin  X 0 / Z 0", true);
        DrawSceneLabel(center + Vector3.up * 0.2f, $"Map Center  X {FormatNumber(center.x)} / Z {FormatNumber(center.z)}", false);
    }

    private static void DrawSceneLabel(Vector3 worldPos, string text, bool origin)
    {
        Handles.BeginGUI();
        Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldPos);
        GUIStyle style = origin ? originLabelStyle : labelStyle;
        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect rect = new Rect(guiPoint.x + 4f, guiPoint.y - size.y * 0.5f, size.x + 8f, size.y + 4f);

        Color bg = origin ? new Color(0f, 0.22f, 0.12f, 0.72f) : new Color(0f, 0f, 0f, 0.45f);
        EditorGUI.DrawRect(rect, bg);
        GUI.Label(rect, text, style);
        Handles.EndGUI();
    }

    private static void EnsureStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = LabelColor },
                padding = new RectOffset(4, 4, 1, 1)
            };
        }

        if (originLabelStyle == null)
        {
            originLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = OriginLabelColor },
                padding = new RectOffset(4, 4, 1, 1)
            };
        }
    }

    private static string FormatNumber(float value)
    {
        if (Mathf.Abs(value) < 0.001f)
            value = 0f;

        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString()
            : value.ToString("0.##");
    }
}
#endif
