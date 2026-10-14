using UnityEngine;

/// <summary>
/// 任务标记的图标和配色。NPC 头顶和任务窗口共用一份，改一处两边都变。
///
/// 放在 Resources 下按名字加载——头顶 UI 是运行时按单位动态生成的，没有一个
/// 稳定的地方去序列化这些引用；任务窗口倒是可以拖，但两边拖两份迟早会不一致。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/任务/任务标记图标集", fileName = "QuestMarkerIconSet")]
public class QuestMarkerIconSet : ScriptableObject
{
    public const string ResourcesPath = "QuestMarkerIconSet";

    [Header("主线")]
    public Sprite mainStoryAcceptable;
    public Sprite mainStoryReport;
    [Tooltip("冷绿。")]
    public Color mainStoryColor = new Color(0.37f, 0.84f, 0.65f, 1f);

    [Header("人物")]
    public Sprite personAcceptable;
    public Sprite personReport;
    [Tooltip("淡紫。")]
    public Color personColor = new Color(0.79f, 0.65f, 0.91f, 1f);

    [Header("支线")]
    public Sprite subStoryAcceptable;
    public Sprite subStoryReport;
    [Tooltip("白。")]
    public Color subStoryColor = Color.white;

    [Header("暗淡")]
    [Tooltip("未满足提交条件时，颜色乘这个系数。")]
    [Range(0f, 1f)] public float dimmedBrightness = 0.45f;

    [Tooltip("未满足提交条件时的不透明度。")]
    [Range(0f, 1f)] public float dimmedAlpha = 0.55f;

    private static QuestMarkerIconSet _instance;
    private static bool _loadAttempted;

    public static QuestMarkerIconSet Instance
    {
        get
        {
            if (!_loadAttempted)
            {
                _loadAttempted = true;
                _instance = Resources.Load<QuestMarkerIconSet>(ResourcesPath);
            }
            return _instance;
        }
    }

    /// <summary>编辑器里重建资源之后需要让运行时重新加载。</summary>
    public static void ClearCache()
    {
        _loadAttempted = false;
        _instance = null;
    }

    public Sprite GetSprite(QuestMarker marker)
    {
        switch (marker.kind)
        {
            case QuestMarkerKind.MainStory:
                return marker.report ? mainStoryReport : mainStoryAcceptable;
            case QuestMarkerKind.Person:
                return marker.report ? personReport : personAcceptable;
            case QuestMarkerKind.SubStory:
                return marker.report ? subStoryReport : subStoryAcceptable;
            default:
                return null;
        }
    }

    public Color GetColor(QuestMarker marker)
    {
        Color baseColor;
        switch (marker.kind)
        {
            case QuestMarkerKind.MainStory: baseColor = mainStoryColor; break;
            case QuestMarkerKind.Person:    baseColor = personColor;    break;
            case QuestMarkerKind.SubStory:  baseColor = subStoryColor;  break;
            default: return Color.clear;
        }

        if (!marker.dimmed)
            return baseColor;

        // 只压亮度和不透明度，不动色相——压暗之后仍然要看得出是哪一类任务。
        return new Color(
            baseColor.r * dimmedBrightness,
            baseColor.g * dimmedBrightness,
            baseColor.b * dimmedBrightness,
            baseColor.a * dimmedAlpha);
    }
}
