using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 灰色损失越多，整屏胶片颗粒越明显，制造紧张感。
///   · 强度由 SkyPrisonGrayLoss.Ratio01 经分档曲线得到：前 15% 几乎无，之后逐级加重，快满时很强；
///   · 涨得快、退得慢：灰色一多颗粒立刻跟上，离开瘴气后即使灰色在恢复，颗粒也是一点点退掉。
/// 做法：运行时建一个全局 Volume（FilmGrain），不动相机栈（后处理只能开在栈尾相机上，见
/// SkyPrisonCameraStackPostProcessAuthority）。URP 的胶片颗粒是整屏均匀的，做不到"先从四周出现"。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonGrayLossScreenGrain : MonoBehaviour
{
    [Header("强度")]
    [Tooltip("灰色占比达到 100% 时的最大颗粒强度（0~1）。")]
    [SerializeField] private float maxIntensity = 1f;
    [Tooltip("涨起来的时间常数(秒)：越小追得越快。")]
    [SerializeField] private float riseTau = 0.3f;
    [Tooltip("退掉的时间常数(秒)：指数衰减，越到后面越慢，形成长尾；约每 tau 秒衰减掉 63%。")]
    [SerializeField] private float fallTau = 3f;
    [Tooltip("输出的感知校正指数：<1 会把低强度抬高，让淡的颗粒也看得见（后半段的长尾不至于看不出来）。")]
    [SerializeField] private float perceptualPower = 0.6f;

    private SkyPrisonGrayLoss _gray;
    private Volume _volume;
    private VolumeProfile _profile;
    private FilmGrain _grain;
    private float _shown;

    // 临死雪花屏叠层：灰色占比超过 staticStart 才出现，满灰时不透明度达到 staticMaxAlpha
    [Header("临死雪花屏叠层")]
    [SerializeField] private float staticStart = 0.65f;
    [SerializeField] private float staticMaxAlpha = 0.4f;
    private Canvas _staticCanvas;
    private UnityEngine.UI.RawImage _staticImage;
    private Material _staticMat;

    private void Start()
    {
        _gray = GetComponent<SkyPrisonGrayLoss>();

        var go = new GameObject("GrayLossGrainVolume");
        go.layer = 0;                       // Default 层：相机的体积层遮罩默认包含它
        go.transform.SetParent(transform, false);

        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 40f;
        _volume.weight = 1f;

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _grain = _profile.Add<FilmGrain>(true);
        _grain.type.Override(FilmGrainLookup.Medium2);
        _grain.intensity.Override(0f);
        _grain.response.Override(0.85f);
        _volume.sharedProfile = _profile;
        BuildStaticOverlay();
    }

    private void BuildStaticOverlay()
    {
        var sh = Shader.Find("UI/SkyPrison/TVStatic");
        if (sh == null) return;

        var cg = new GameObject("GrayLossStaticOverlay");
        cg.transform.SetParent(transform, false);
        _staticCanvas = cg.AddComponent<Canvas>();
        _staticCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _staticCanvas.sortingOrder = 20;       // 比 HUD 低：不盖住血条、准星
        var ig = new GameObject("Static", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.RawImage));
        ig.transform.SetParent(cg.transform, false);
        var rt = (RectTransform)ig.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        _staticImage = ig.GetComponent<UnityEngine.UI.RawImage>();
        _staticImage.raycastTarget = false;
        _staticMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _staticMat.SetFloat("_NoiseScale", 700f);
        _staticMat.SetFloat("_NoiseSpeed", 22f);
        _staticMat.SetFloat("_Brightness", 0.9f);
        _staticMat.SetFloat("_ScanlineStrength", 0.15f);
        _staticMat.SetFloat("_WiggleAmount", 0.03f);
        _staticImage.material = _staticMat;
        _staticImage.color = new Color(1f, 1f, 1f, 0f);
        cg.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_profile != null) Destroy(_profile);
        if (_staticMat != null) Destroy(_staticMat);
    }

    private void Update()
    {
        if (_gray == null || _grain == null) return;

        float target = Mathf.Clamp01(Curve(_gray.Ratio01));
        float tau = target > _shown ? riseTau : fallTau;
        // 指数趋近目标：退的时候越到后面越慢，自然有长尾
        _shown = Mathf.Lerp(_shown, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, tau)));
        if (Mathf.Abs(_shown - target) < 0.002f) _shown = target;
        _grain.intensity.value = Mathf.Pow(Mathf.Clamp01(_shown), perceptualPower) * maxIntensity;

        // 临死（强度档位超过 ~0.45，对应灰色约 60% 以上）：颗粒换粗
        _grain.type.value = _shown > 0.45f ? FilmGrainLookup.Large02 : FilmGrainLookup.Medium2;

        UpdateStaticOverlay();
    }

    private void UpdateStaticOverlay()
    {
        if (_staticCanvas == null || _staticImage == null) return;
        // 用"已平滑的强度"换算回灰色占比附近，让雪花屏跟颗粒同步淡入淡出（退的时候一起慢慢退）
        float level = Mathf.InverseLerp(Curve(staticStart), 1f, _shown);
        float a = Mathf.SmoothStep(0f, 1f, level) * staticMaxAlpha;
        bool on = a > 0.003f;
        if (_staticCanvas.gameObject.activeSelf != on) _staticCanvas.gameObject.SetActive(on);
        if (on) _staticImage.color = new Color(1f, 1f, 1f, a);
    }

    // 灰色占比 → 颗粒强度（分档）：<15% 无；15~50% 轻微；50~80% 明显；80~100% 很强
    private static float Curve(float r)
    {
        if (r <= 0.15f) return 0f;
        if (r <= 0.5f) return Mathf.Lerp(0f, 0.3f, Mathf.InverseLerp(0.15f, 0.5f, r));
        if (r <= 0.8f) return Mathf.Lerp(0.3f, 0.65f, Mathf.InverseLerp(0.5f, 0.8f, r));
        return Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(0.8f, 1f, r));
    }
}
