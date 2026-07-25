using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature — 仅负责创建并注册高斯模糊材质。
/// 实际模糊在 SkyPrisonInventoryBlurBackground 打开时一次性 CPU 捕获完成。
/// </summary>
public class SkyPrisonUIBackgroundBlurFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public bool   enabled = true;
        public Shader shader;
    }

    public Settings settings = new Settings();

    private Material _mat;

    public override void Create()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/SkyPrison/UIBackgroundBlur");

        if (settings.shader != null)
        {
            CoreUtils.Destroy(_mat);
            _mat = CoreUtils.CreateEngineMaterial(settings.shader);
            SkyPrisonUIBlurManager.BlurMaterial = _mat;
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) { }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_mat);
        SkyPrisonUIBlurManager.Release();
    }
}
