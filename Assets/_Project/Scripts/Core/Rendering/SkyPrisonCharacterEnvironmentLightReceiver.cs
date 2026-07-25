using UnityEngine;

/// <summary>
/// 把地图场景实际的主光源（Main_DirectionalLight，地图环境结构里固定命名的那个）
/// 亮度/颜色喂给Spine角色shader已有的"环境色调接收接口"（_SkyPrison_EnvTint/
/// _SkyPrison_EnvExposure/_SkyPrison_EnvDarken——Spine-Skeleton.shader里本来就有，
/// 一直没人接），让角色明暗跟着地图场景的光照氛围走，避免"背景很暗、角色却是
/// 死白的"这种割裂感。风格化控制、非物理模拟，跟之前废弃掉的法线贴图实时光照
/// 是两套不同的东西。
/// 通过MaterialPropertyBlock喂参数，不创建材质实例、不影响SRP Batching。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SkyPrisonCharacterEnvironmentLightReceiver : MonoBehaviour
{
    private const string MainLightName = "Main_DirectionalLight";

    private static readonly int EnvTintId = Shader.PropertyToID("_SkyPrison_EnvTint");
    private static readonly int EnvTintStrengthId = Shader.PropertyToID("_SkyPrison_EnvTintStrength");
    private static readonly int EnvDarkenId = Shader.PropertyToID("_SkyPrison_EnvDarken");
    private static readonly int EnvExposureId = Shader.PropertyToID("_SkyPrison_EnvExposure");

    [Header("场景主光源")]
    [Tooltip("留空则自动按名字查找场景里的 Main_DirectionalLight")]
    public Light sceneMainLight;

    [Header("响应参数")]
    [Tooltip("场景主光强度达到这个值时，角色按满亮度显示，不再继续变暗")]
    public float referenceLightIntensity = 1f;
    [Tooltip("场景主光强度为0时，角色最多压暗到这个程度")]
    [Range(0f, 1f)] public float maxDarken = 0.55f;
    [Tooltip("场景光颜色带入角色身上的强度，不用调太高，避免整个角色变色")]
    [Range(0f, 1f)] public float tintStrength = 0.3f;

    [Header("作用目标")]
    [Tooltip("勾选时忽略下面手动拖的列表，每秒自动重新收集自己子物体下的所有Renderer" +
        "（包括换装/运行时才生成的Spine渲染物体）。不勾选则只用手动拖的列表，自己管理。")]
    public bool autoFindRenderers = true;
    [Tooltip("Auto Find Renderers关闭时才生效——要跟随场景光照的角色渲染器")]
    public Renderer[] targetRenderers;

    private MaterialPropertyBlock _block;
    private float _rendererRefreshTimer;

    private void OnEnable()
    {
        _block = new MaterialPropertyBlock();
        if (sceneMainLight == null)
        {
            GameObject found = GameObject.Find(MainLightName);
            if (found != null)
                sceneMainLight = found.GetComponent<Light>();
        }
        RefreshTargetRenderers();
    }

    private void LateUpdate()
    {
        // Spine角色的实际渲染物体可能是换装/初始化流程运行时才创建的子物体，
        // 挂点这个组件的时候未必已经存在——持续每秒重新收集一次，保证换装/
        // 重新生成之后也能自动跟上，不用手动重新拖引用。
        _rendererRefreshTimer -= Time.unscaledDeltaTime;
        if (_rendererRefreshTimer <= 0f)
        {
            _rendererRefreshTimer = 1f;
            RefreshTargetRenderers();
        }

        Apply();
    }

    private void RefreshTargetRenderers()
    {
        if (!autoFindRenderers)
            return;

        targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Apply()
    {
        if (sceneMainLight == null || targetRenderers == null || targetRenderers.Length == 0)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        float lightRatio = referenceLightIntensity > 0.0001f
            ? Mathf.Clamp01(sceneMainLight.intensity / referenceLightIntensity)
            : 1f;
        float darken = Mathf.Lerp(maxDarken, 0f, lightRatio);

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_block);
            _block.SetColor(EnvTintId, sceneMainLight.color);
            _block.SetFloat(EnvTintStrengthId, tintStrength);
            _block.SetFloat(EnvDarkenId, darken);
            _block.SetFloat(EnvExposureId, 0f);
            r.SetPropertyBlock(_block);
        }
    }
}
