using System;

/// <summary>
/// 项目级渲染/预览档位。
/// 注意：这不是单纯玩家画质，而是“制作阶段协议”。
/// </summary>
public enum SkyPrisonRenderQualityTier
{
    /// <summary>
    /// L0：安全编辑档。不卡、能救场、能操作。允许明显牺牲视觉。
    /// </summary>
    Safe = 0,

    /// <summary>
    /// L1：编辑预览档。地图编辑、刷地、摆放物体时使用。
    /// </summary>
    EditPreview = 1,

    /// <summary>
    /// L2：运行预览档。编辑器 Play 测试时使用，优先快速进入 Play。
    /// </summary>
    RuntimePreview = 2,

    /// <summary>
    /// L3：正式发布档。正式出包、宣传录制、最终验收时使用。
    /// </summary>
    Final = 3,
}
