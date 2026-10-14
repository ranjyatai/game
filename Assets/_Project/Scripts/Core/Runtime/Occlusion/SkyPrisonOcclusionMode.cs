/// <summary>
/// 遮挡判定走哪条路——全局唯一开关。
///
/// true  = GPU 深度比较。角色着色器采样 _CameraDepthTexture 逐像素判定，被挡的像素
///         discard，露出的就是真实遮挡物。CPU 侧完全不参与，开销与场景里有多少
///         遮挡物无关。
/// false = 旧的 CPU 三角面射线求交 + RT 管线合成 _OcclusionTex。
///
/// 为什么默认走 GPU——F7 伸缩性基准实测（复制叉车，12 个渲染体）：
///
///     遮挡物数    CPU 触发器耗时/帧
///        10          1846 ms
///        50          4078 ms
///       100          7770 ms
///
/// 近似线性，斜率约 78ms/个。场景里现在 21 个，地图内容再翻几倍就直接不可玩。
/// 这不是判断，是测出来的。
///
/// 这个开关同时控制两侧，必须一起改，否则会出现「着色器读深度、CPU 还在空转」
/// 或者「CPU 算完没人用」的半吊子状态——切到 GPU 的第一版就是后者，
/// 结果 CPU 开销一分没省，奔跑时照样掉到 17 帧。
///
/// 材质那侧的 _SkyPrison_UseSceneDepthOcclusion 由
/// SkyPrisonSceneDepthOcclusionDefault 菜单同步；运行时可用 F9
/// （OcclusionPathCompareHUD）临时切回旧路径做对比——但那只切着色器，
/// 不会重新启用 CPU 触发器，对比的是画面而不是性能。
/// </summary>
public static class SkyPrisonOcclusionMode
{
    /// <summary>
    /// 改成 false 可立刻恢复旧的 CPU 判定路径，不需要回滚任何结构改动。
    /// 留这个逃生口是因为深度路径还在观察期。
    /// </summary>
    // 2026-08-13：「切到 GPU 后全场不遮挡」实际是三个 bug 叠加，逐个定位并验证：
    //
    //   1. 触发器 early return 把「合成材质切换」也一起停掉了 —— 那个组件同时负责
    //      逐三角面求交和材质切换，只想砍前者却把后者也砍了，合成着色器从未被绑定。
    //      症状是诊断模式 4 一片空白（诊断分支是无条件 return 的，着色器在跑就必有颜色）。
    //      修法：UnitOcclusionMaterialReceiver 在 GPU 模式下让合成材质常驻。
    //
    //   2. _SkyPrison_SceneDepthFootScale 沿用竖直 billboard 年代的 0.7。Spine 为了不被
    //      压缩是垂直于视线的，worldPos 已含高度，再补一次是重复扣减，整个上半身被判成
    //      「在场景前面」。正确值是 0。已有材质里的旧值由 SkyPrisonSceneDepthFootScaleSync
    //      在编辑器加载时自动同步。
    //
    //   3. 落地深度着色器原本用 unity_ObjectToWorld 取根节点，静态合批会把顶点烘进世界
    //      空间、该矩阵退化成单位阵，合批过的遮挡物（叉车）写进纹理的值全错 —— 回读实测
    //      匹配像素数为 0，而未合批的草一切正常。修法：CPU 在每次 DrawRenderer 前用
    //      SetGlobalVector 显式传落地坐标。
    //
    // 这个开关必须和三处保持一致，只改一处就会出现「算了没人用」或「用了没人算」：
    //   - CPU 触发器是否 early return
    //   - UnitOcclusionMaterialReceiver 的合成材质是否常驻
    //   - OcclusionPathCompareHUD 写进 MaterialPropertyBlock 的默认值
    public const bool UseGpuDepthOcclusion = true;
}
