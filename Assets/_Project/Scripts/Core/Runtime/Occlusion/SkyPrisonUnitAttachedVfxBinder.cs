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
