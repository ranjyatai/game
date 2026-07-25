using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class LootDropBeaconVFX : MonoBehaviour
{
    [Header("光柱")]
    [SerializeField] public Material beamMaterial;
    [SerializeField] private float   beamHeight    = 3.2f;
    [SerializeField] private float   beamWidthBase = 0.05f;
    [SerializeField] private float   beamPulseAmp  = 0.4f;
    [SerializeField] private float   beamPulseSpeed= 2.0f;

    [Header("粒子拖尾")]
    [SerializeField] public Material particleMaterial;
    [SerializeField] private float   particleRate     = 18f;
    [SerializeField] private float   particleLifetime = 0.7f;
    [SerializeField] private float   particleSpeed    = 2.2f;
    [SerializeField] private float   particleSize     = 0.055f;

    private LineRenderer   _beam;
    private ParticleSystem _sparks;
    private Color          _color;
    private float          _phase;
    private bool           _rainbow;

    // 普通单色初始化
    public void Setup(Color color, int sortingOrder)
    {
        _color   = color;
        _rainbow = false;
        _phase   = Random.Range(0f, Mathf.PI * 2f);
        BuildBeam(sortingOrder);
        BuildSparks(sortingOrder);
    }

    // LV9 七彩初始化：梁 + 粒子同时显示多色
    public void SetupRainbow(int sortingOrder)
    {
        _color   = Color.white;
        _rainbow = true;
        _phase   = Random.Range(0f, Mathf.PI * 2f);
        BuildBeam(sortingOrder);
        BuildSparks(sortingOrder);
        ApplyRainbowBeam();
        ApplyRainbowParticles();
    }

    // 外部单色覆盖（非七彩时使用）
    public void SetColor(Color color)
    {
        if (_rainbow) return;
        _color = color;
        if (_beam != null)
        {
            _beam.startColor = new Color(color.r, color.g, color.b, 0.75f);
            _beam.endColor   = Color.clear;
        }
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_beam == null) return;

        float pulse = 1f + beamPulseAmp * Mathf.Sin(Time.time * beamPulseSpeed + _phase);
        _beam.startWidth = beamWidthBase * pulse;
        _beam.endWidth   = beamWidthBase * 0.12f * pulse;

        if (!_rainbow)
        {
            float bright = 0.85f + 0.15f * pulse;
            _beam.startColor = new Color(_color.r * bright, _color.g * bright, _color.b * bright, 0.75f);
        }
        // rainbow 模式：colorGradient 已设置，不需要每帧更新
    }

    // ── 彩虹梯度 ─────────────────────────────────────────────────────────────

    private void ApplyRainbowBeam()
    {
        // 光柱从底到顶显示完整彩虹梯度（同一帧可见所有颜色）
        const int N = 8;
        var cKeys = new GradientColorKey[N];
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1);
            cKeys[i] = new GradientColorKey(Color.HSVToRGB(t, 0.5f, 1f), t);
        }
        var aKeys = new GradientAlphaKey[]
        {
            new GradientAlphaKey(0.8f, 0f),
            new GradientAlphaKey(0f,   1f),
        };
        var grad = new Gradient();
        grad.SetKeys(cKeys, aKeys);
        _beam.colorGradient = grad;
    }

    private void ApplyRainbowParticles()
    {
        // 粒子 Color over Lifetime → 不同生命周期阶段颜色不同
        // 同一帧中存活的粒子跨越不同生命阶段，因此同时可见七色
        const int N = 8;
        var cKeys = new GradientColorKey[N];
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1);
            cKeys[i] = new GradientColorKey(Color.HSVToRGB(t, 0.45f, 1f), t);
        }
        var aKeys = new GradientAlphaKey[]
        {
            new GradientAlphaKey(1f,   0f),
            new GradientAlphaKey(0.6f, 0.6f),
            new GradientAlphaKey(0f,   1f),
        };
        var grad = new Gradient();
        grad.SetKeys(cKeys, aKeys);

        var col = _sparks.colorOverLifetime;
        col.enabled = true;
        col.color   = grad;

        // 中性白色起始色，让梯度完整显示
        var main = _sparks.main;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);

        // 拖尾也用彩虹梯度
        var trails = _sparks.trails;
        trails.colorOverTrail = grad;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildBeam(int sortOrder)
    {
        var go = new GameObject("BeamLine");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.25f, 0f);

        _beam = go.AddComponent<LineRenderer>();
        _beam.useWorldSpace = false;
        _beam.positionCount = 2;
        _beam.SetPosition(0, Vector3.zero);
        _beam.SetPosition(1, new Vector3(0f, beamHeight, 0f));
        _beam.startWidth   = beamWidthBase;
        _beam.endWidth     = beamWidthBase * 0.12f;
        _beam.startColor   = new Color(_color.r, _color.g, _color.b, 0.75f);
        _beam.endColor     = Color.clear;
        _beam.shadowCastingMode          = ShadowCastingMode.Off;
        _beam.receiveShadows             = false;
        _beam.allowOcclusionWhenDynamic  = false;
        _beam.sortingOrder               = sortOrder;

        if (beamMaterial != null) _beam.material = beamMaterial;
    }

    private void BuildSparks(int sortOrder)
    {
        var go = new GameObject("BeamSparks");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.25f, 0f);

        _sparks = go.AddComponent<ParticleSystem>();

        var main = _sparks.main;
        main.loop            = true;
        main.playOnAwake     = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(particleLifetime * 0.7f, particleLifetime);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(particleSpeed * 0.6f, particleSpeed);
        main.startSize       = new ParticleSystem.MinMaxCurve(particleSize * 0.5f, particleSize);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            Color.white, new Color(_color.r, _color.g, _color.b, 1f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 60;

        var emission = _sparks.emission;
        emission.rateOverTime = particleRate;

        var shape = _sparks.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.04f;
        shape.arc       = 360f;
        shape.rotation  = new Vector3(-90f, 0f, 0f);

        var col = _sparks.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.4f), new GradientColorKey(_color, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sz = _sparks.sizeOverLifetime;
        sz.enabled = true;
        sz.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var noise = _sparks.noise;
        noise.enabled     = true;
        noise.strength    = 0.05f;
        noise.frequency   = 0.6f;
        noise.scrollSpeed = 0.2f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;

        var trails = _sparks.trails;
        trails.enabled              = true;
        trails.mode                 = ParticleSystemTrailMode.PerParticle;
        trails.ratio                = 1.0f;
        trails.lifetime             = new ParticleSystem.MinMaxCurve(0.25f);
        trails.minVertexDistance    = 0.02f;
        trails.worldSpace           = true;
        trails.dieWithParticles     = true;
        trails.sizeAffectsWidth     = false;
        trails.inheritParticleColor = true;
        var trailGrad = new Gradient();
        trailGrad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        trails.colorOverTrail = trailGrad;

        var r = _sparks.GetComponent<ParticleSystemRenderer>();
        r.renderMode        = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;
        r.sortingOrder      = sortOrder;

        Material mat = particleMaterial ?? beamMaterial;
        if (mat != null) { r.material = mat; r.trailMaterial = mat; }
    }
}
