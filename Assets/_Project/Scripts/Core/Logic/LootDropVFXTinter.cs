using UnityEngine;

/// <summary>
/// 真正的 VFX 改色：用色相替换 shader 把粒子材质的原始色相换掉，
/// 只保留贴图明度（形状细节），避免黄/橙底色混进目标颜色。
/// LineRenderer 直接改顶点色。
/// </summary>
public static class LootDropVFXTinter
{
    private const string k_HueShiftShaderName = "Hidden/SP/VFXHueShift";
    private static readonly int k_TargetColorId = Shader.PropertyToID("_TargetColor");
    private static readonly int k_MainTexId     = Shader.PropertyToID("_MainTex");
    private static readonly int k_BaseMapId     = Shader.PropertyToID("_BaseMap");

    // LV9：粒子 colorOverLifetime + LineRenderer 设为完整彩虹梯度
    public static void ApplyRainbow(GameObject vfxRoot)
    {
        const int N = 8;
        var cKeys = new GradientColorKey[N];
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1);
            cKeys[i] = new GradientColorKey(Color.HSVToRGB(t, 0.4f, 1f), t);
        }

        foreach (var ps in vfxRoot.GetComponentsInChildren<ParticleSystem>(true))
        {
            // 起始色设为白，让 colorOverLifetime 梯度完整显示
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(cKeys,
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        foreach (var r in vfxRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (r is LineRenderer lr)
            {
                var g = new Gradient();
                g.SetKeys(cKeys,
                    new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
                lr.colorGradient = g;
            }
        }
    }

    public static void Apply(GameObject vfxRoot, Color color)
    {
        // 优先从 Library 取直接引用（Build 中 Shader.Find 对 Hidden/ 不可靠）
        var lib = LootDropModelLibrary.Instance;
        Shader hueShift = (lib != null && lib.vfxHueShiftShader != null)
            ? lib.vfxHueShiftShader
            : Shader.Find(k_HueShiftShaderName);

        if (hueShift == null)
        {
            // 找不到 shader 时跳过染色但不中断，模型仍正常显示
            Debug.LogWarning("[VFXTinter] 找不到 Hidden/SP/VFXHueShift shader。请在 LootDropModelLibrary 的 vfxHueShiftShader 字段拖入 VFXHueShift.shader。");
            return;
        }

        // ── 粒子 startColor → 白色（让 shader 完全控制颜色）────────────────
        foreach (var ps in vfxRoot.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            // 只保留 alpha 通道的淡出曲线，色相交给 shader
            var orig = main.startColor;
            switch (orig.mode)
            {
                case ParticleSystemGradientMode.Color:
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(1, 1, 1, orig.color.a));
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(1, 1, 1, orig.colorMin.a),
                        new Color(1, 1, 1, orig.colorMax.a));
                    break;
                default:
                    // Gradient 模式：整体换成白色渐变，alpha 不变
                    var g = new Gradient();
                    g.SetKeys(
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        orig.gradient != null
                            ? orig.gradient.alphaKeys
                            : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    main.startColor = new ParticleSystem.MinMaxGradient(g);
                    break;
            }
        }

        // ── 粒子材质换成 HueShift shader ─────────────────────────────────────
        foreach (var r in vfxRoot.GetComponentsInChildren<Renderer>(true))
        {
            // LineRenderer 单独处理
            if (r is LineRenderer lr)
            {
                lr.startColor = new Color(color.r, color.g, color.b, lr.startColor.a);
                lr.endColor   = new Color(color.r, color.g, color.b, lr.endColor.a);
                continue;
            }

            // 用 sharedMaterials 读，不要用 materials——materials 的 getter 本身就会
            // 偷偷实例化一份材质副本塞回 renderer（Unity 内置行为），下面又对每个槽位
            // new 一个新的 Material 直接覆盖掉，那份"偷偷实例化"的副本就变成孤儿，永远
            // 没人 Destroy。这只是第一层泄漏；更大的那层是：这个 VFX 只要被复用/重新
            // 染色（比如掉落物对象池换个颜色再用一次），这里每次都会无条件 new 一个新
            // Material，旧的那个从头到尾没有任何地方调用过 Destroy——掉落物刷得越多、
            // VFX 复用得越频繁，材质对象只增不减，这是长时间游玩会越来越卡的一个真实
            // 来源，不是错觉。
            var shared = r.sharedMaterials;
            var mats = new Material[shared.Length];
            for (int i = 0; i < shared.Length; i++)
            {
                Material orig = shared[i];
                if (orig == null) { mats[i] = null; continue; }

                // 已经是上一次染色时换过的 HueShift 材质——说明这个渲染器被复用了，
                // 直接在原地改颜色，不要再 new 一个新的、把旧的丢掉不管。
                if (orig.shader == hueShift)
                {
                    orig.SetColor(k_TargetColorId, color);
                    mats[i] = orig;
                    continue;
                }

                // 取原材质的主贴图
                Texture mainTex = null;
                if (orig.HasProperty(k_MainTexId))  mainTex = orig.GetTexture(k_MainTexId);
                if (mainTex == null && orig.HasProperty(k_BaseMapId)) mainTex = orig.GetTexture(k_BaseMapId);

                // 换 shader，保留贴图——这个渲染器第一次被染色，只有这一次才真的需要
                // new 一个新材质。
                var newMat = new Material(hueShift);
                if (mainTex != null) newMat.SetTexture(k_MainTexId, mainTex);
                newMat.SetColor(k_TargetColorId, color);
                mats[i] = newMat;
            }
            r.materials = mats;
        }
    }
}
