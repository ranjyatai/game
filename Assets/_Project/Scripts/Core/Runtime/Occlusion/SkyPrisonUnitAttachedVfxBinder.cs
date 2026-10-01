using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把「挂在单位身上的特效」（枪口火光、弹道拖尾）接进角色本体那套落地深度遮挡判定。
///
/// 问题：外购 RVFX 的 Additive / BulletTrail 两张 Shader Graph 走普通 ZTest（另外
/// Additive 还有软粒子淡出）。45 度视角下角色侧身贴墙开枪，枪口在几何深度上插进
/// 身后的墙，火光被墙直接吃掉；而角色本体按「脚底谁在前面」判定，不会被吃——两边
/// 规则不一致。之前用 MuzzleVisualCameraBias 往相机方向顶 0.35 的做法兜不住。
///
/// 做法：把这两种材质换成项目版着色器（SkyPrison/VFX/UnitAttached*，ZTest Always +
/// 用持有者 Spine 根节点比较落地深度图），持有者锚点通过 MaterialPropertyBlock 按
/// 渲染器写入。属性名与原 Shader Graph 完全一致，材质上的数值原样沿用。
///
/// 粒子材质是共享的，按原材质缓存一份替换版，不会每开一枪泄漏一个材质实例。
/// 拖尾的 LineRenderer 材质本来就是 BulletTrailEmitter 每发 new 出来的独立实例，
/// 直接改它的 shader 即可。
/// </summary>
public static class SkyPrisonUnitAttachedVfxBinder
{
    private const string AdditiveShaderName = "SkyPrison/VFX/UnitAttachedAdditive";
    private const string BulletTrailShaderName = "SkyPrison/VFX/UnitAttachedBulletTrail";

    private static readonly int AnchorId = Shader.PropertyToID("_SkyPrison_VfxAnchorWS");
    private static readonly int ColorPowerId = Shader.PropertyToID("_Color_Power");
    private static readonly int AngleMaskFactorId = Shader.PropertyToID("_AngleMaskFactor");
    private static readonly int TrailLengthId = Shader.PropertyToID("_TrailLength");

    private static readonly Dictionary<Material, Material> AdditiveVariants = new Dictionary<Material, Material>();
    private static readonly List<Material> SharedMaterialsScratch = new List<Material>();

    private static Shader _additiveShader;
    private static Shader _bulletTrailShader;
    private static bool _shadersResolved;
    private static MaterialPropertyBlock _mpb;

    public static void Bind(GameObject vfxRoot, Vector3 anchorWorldPosition)
    {
        if (vfxRoot == null || !ResolveShaders())
            return;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        var anchor = new Vector4(anchorWorldPosition.x, anchorWorldPosition.y, anchorWorldPosition.z, 1f);

        var particleRenderers = vfxRoot.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var r in particleRenderers)
        {
            r.GetSharedMaterials(SharedMaterialsScratch);
            bool changed = false;
            for (int i = 0; i < SharedMaterialsScratch.Count; i++)
            {
                Material variant = GetAdditiveVariant(SharedMaterialsScratch[i]);
                if (variant != SharedMaterialsScratch[i])
                {
                    SharedMaterialsScratch[i] = variant;
                    changed = true;
                }
            }

            if (changed)
                r.SetSharedMaterials(SharedMaterialsScratch);

            WriteAnchor(r, anchor);
        }

        var lineRenderers = vfxRoot.GetComponentsInChildren<LineRenderer>(true);
        foreach (var lr in lineRenderers)
        {
            Material m = lr.sharedMaterial;
            if (m != null && m.shader != _bulletTrailShader && m.HasProperty(TrailLengthId))
                m.shader = _bulletTrailShader;

            WriteAnchor(lr, anchor);
        }

        SharedMaterialsScratch.Clear();
    }

    /// <summary>
    /// 给换不了着色器的特效（Effekseer：挥砍、蓄力冲刺、位移特效）用的遮挡修正：
    /// 沿相机视线把特效推到持有者脚底的深度。
    ///
    /// 挂在 Spine 上的锚点（刀尖等）位于垂直于视线的倾斜平面上，离地越高，世界 Z 越往
    /// 身后深（实测枪口高 2.27 时比脚底深 ~1.8），贴墙时直接落进墙后被深度测试吃掉。
    ///
    /// 沿视线移动在正交相机下不改变屏幕位置，画面完全不变；移到脚底深度之后：
    ///   - 脚底身后的墙（深度 ≥ 脚底）挡不住它；
    ///   - 脚底前面的遮挡物（深度 < 脚底）照样挡住它——
    /// 正好是角色本体「按脚底判前后」的 2.5D 规则，而且用的是普通深度测试，不需要改着色器。
    ///
    /// 只往相机方向推，不往后推：本来就在脚底前面的不动。margin 留给特效自身的体积——
    /// 特效不是一个点，以锚点为中心往外铺开，上半部分在倾斜平面上同样更深。
    /// </summary>
    public static Vector3 SlideToOwnerFootDepth(Vector3 worldPosition, Vector3 ownerFootPosition, float margin = 1f)
    {
        return SlideToOwnerFootDepth(worldPosition, ownerFootPosition, margin, out _);
    }

    /// <param name="scaleCompensation">
    /// 透视相机下特效被拉近镜头会变大——乘到特效缩放上可保持屏幕尺寸不变（正交下恒为 1）。
    /// 运行时的 Camera.main 是透视的（场景里 Main Camera FOV 30），这一项不能省：
    /// 实测余量 10 米时不补偿，挥砍特效会明显放大。
    /// </param>
    public static Vector3 SlideToOwnerFootDepth(Vector3 worldPosition, Vector3 ownerFootPosition, float margin, out float scaleCompensation)
    {
        scaleCompensation = 1f;
        Camera cam = Camera.main;
        if (cam == null)
            return worldPosition;

        // 正交：沿相机朝向；透视：沿相机到该点的射线——两种情况下都保证屏幕位置不变。
        Vector3 viewDir = cam.orthographic
            ? cam.transform.forward
            : (worldPosition - cam.transform.position).normalized;

        // 「深度」按地面上的前后方向衡量（相机朝向的水平分量），跟落地深度图同一个语义。
        Vector3 groundForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (groundForward.sqrMagnitude < 1e-6f)
            return worldPosition;
        groundForward.Normalize();

        float along = Vector3.Dot(viewDir, groundForward);
        if (along < 1e-4f)
            return worldPosition;

        float targetDepth = Vector3.Dot(ownerFootPosition, groundForward) - margin;
        float currentDepth = Vector3.Dot(worldPosition, groundForward);
        if (currentDepth <= targetDepth)
            return worldPosition;

        Vector3 slid = worldPosition - viewDir * ((currentDepth - targetDepth) / along);

        if (!cam.orthographic)
        {
            Vector3 camPos = cam.transform.position;
            float before = Vector3.Dot(worldPosition - camPos, cam.transform.forward);
            float after = Vector3.Dot(slid - camPos, cam.transform.forward);
            if (before > 1e-3f && after > 1e-3f)
                scaleCompensation = after / before;
        }

        return slid;
    }

    private static void WriteAnchor(Renderer r, Vector4 anchor)
    {
        r.GetPropertyBlock(_mpb);
        _mpb.SetVector(AnchorId, anchor);
        r.SetPropertyBlock(_mpb);
    }

    private static Material GetAdditiveVariant(Material original)
    {
        if (original == null || original.shader == _additiveShader)
            return original;

        // 只认 RVFX Additive.shadergraph 的属性组合，别的材质（烟雾、AlphaBlended 等）原样放过。
        if (!original.HasProperty(ColorPowerId) || !original.HasProperty(AngleMaskFactorId))
            return original;

        if (!AdditiveVariants.TryGetValue(original, out Material variant) || variant == null)
        {
            variant = new Material(original)
            {
                name = original.name + " (SkyPrison UnitAttached)",
                shader = _additiveShader,
                hideFlags = HideFlags.DontSave
            };
            AdditiveVariants[original] = variant;
        }

        return variant;
    }

    private static bool ResolveShaders()
    {
        if (_shadersResolved)
            return _additiveShader != null && _bulletTrailShader != null;

        _shadersResolved = true;
        _additiveShader = Shader.Find(AdditiveShaderName);
        _bulletTrailShader = Shader.Find(BulletTrailShaderName);

        if (_additiveShader == null || _bulletTrailShader == null)
        {
            Debug.LogError($"[SkyPrisonUnitAttachedVfxBinder] 找不到着色器 " +
                           $"{AdditiveShaderName}={_additiveShader != null}, {BulletTrailShaderName}={_bulletTrailShader != null}，" +
                           "枪口特效保持原版材质（贴墙时会被墙吃掉）。");
            return false;
        }

        return true;
    }
}
