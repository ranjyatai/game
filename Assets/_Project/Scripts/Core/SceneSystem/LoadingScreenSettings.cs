using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 读条界面配置。路径约定：Assets/_Project/Resources/LoadingScreenSettings.asset
/// 创建：Tools → 界面设置 → 读条设置
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Loading Screen Settings", fileName = "LoadingScreenSettings")]
public class LoadingScreenSettings : ScriptableObject
{
    [Header("背景底图")]
    [Tooltip("全屏背景图（PNG/Texture2D，直接拖入即可）。留空则纯黑背景。")]
    public Texture2D backgroundTexture;

    [Header("角标图层")]
    [Tooltip("叠在背景上的角标/框架图（PNG/Texture2D，直接拖入即可）。保持宽高比填满屏幕，留空则不显示。")]
    public Texture2D cornerOverlayTexture;

    [Header("3D 模型")]
    [Tooltip("读条时展示的全息模型 Prefab。留空则不显示。")]
    public GameObject modelPrefab;

    [Header("Tips")]
    [Tooltip("随机展示的 Tip 列表，每条支持多语言富文本。")]
    public List<LoadingTip> tips = new List<LoadingTip>();

    /// <summary>随机取一条 Tip（当前语言）。</summary>
    public string GetRandomTip(string langCode)
    {
        if (tips == null || tips.Count == 0) return string.Empty;
        int idx = Random.Range(0, tips.Count);
        return tips[idx].GetText(langCode);
    }
}
