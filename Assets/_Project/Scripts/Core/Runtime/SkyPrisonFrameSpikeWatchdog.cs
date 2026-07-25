using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// 卡顿监控——不需要打开 Unity Profiler 窗口，随便一个 Build 跑起来就会自动生效。
/// 不在屏幕上常驻显示任何东西（之前试过全程叠在屏幕上，反而信息太多干扰观感）。
/// 所有数据都只是在后台默默记录，按 F10 时才一次性整理成一份报告发到 Discord
/// （复用 SkyPrisonErrorReporter 的 webhook），要看结果去 Discord 频道看，不用截图。
///
/// 记录的内容：
///   - 本局至今最低FPS / 卡顿(超过阈值的帧)次数 / GC次数 / Mono堆内存峰值
///   - 本局至今耗时最严重的一次卡顿的详细分类耗时（脚本/物理/GC/渲染分别占多少），
///     用来定位卡顿到底花在哪一类上，不用连 Profiler
///   - 当前贴图/RenderTexture 显存占用排行前5，不用装 Memory Profiler 包
/// </summary>
public class SkyPrisonFrameSpikeWatchdog : MonoBehaviour
{
    [Tooltip("单帧耗时超过这个毫秒数才算「卡顿」，计入卡顿次数统计。默认50ms＝低于20FPS的那一帧。")]
    [SerializeField] private float spikeThresholdMs = 50f;

    [Tooltip("游戏/场景刚启动后这段时间(秒)内的卡顿不计入统计——加载资源本来就会卡一下，" +
             "不是游玩过程中的真实问题，混进去会让「最严重卡顿」变成没有诊断价值的加载耗时。")]
    [SerializeField] private float startupGracePeriod = 5f;

    [Tooltip("场景/游戏刚加载完的头几个 Update 调用一律跳过统计，不管每一帧实际花了多久——" +
             "加载卡顿常常就发生在第1、2帧本身，那一帧自己就能把上面那个「按秒数」的宽限期" +
             "烧穿（一帧卡2秒，5秒宽限期还没到就被这一帧自己吃掉大半），所以还要按帧数兜底。")]
    [SerializeField] private int startupGraceFrames = 10;
    private int _framesSinceGraceStart;

    [Tooltip("两次记录之间的最短间隔（秒）——防止连续好几帧都卡的时候重复记录同一次卡顿。")]
    [SerializeField] private float logCooldown = 0.3f;

    [Tooltip("每隔多少秒重新扫一次贴图内存排行榜。")]
    [SerializeField] private float textureScanInterval = 8f;

    [Header("无人浸测——定时自动上报")]
    [Tooltip("开启后，不用等人按F10，每隔 autoReportIntervalSeconds 自动发一份报告到Discord。" +
             "配合 SkyPrisonSoakTestDriver 挂机跑，睡一觉/挂一晚上起来就有一串报告可以看，不用人守着。")]
    [SerializeField] private bool enablePeriodicAutoReport = false;
    [Tooltip("默认600秒=10分钟发一次。")]
    [SerializeField] private float autoReportIntervalSeconds = 600f;
    [Tooltip("命令行带这个参数启动时，不管上面那个开关是不是勾了，都强制打开定时自动上报——" +
             "跟 SkyPrisonSoakTestDriver 用同一个参数，浸测Build一起跑起来就自动配好，不用分别在两处开关。")]
    [SerializeField] private string autoEnableCommandLineArg = "-soaktest";
    private float _nextAutoReportTime;

    private static SkyPrisonFrameSpikeWatchdog _instance;

    private int _lastGcCount0;
    private long _lastMonoMemory;
    private float _lastLogTime = -999f;
    private TerrainGroundMotorV5 _playerMotor;
    private float _motorSearchCooldown;

    // 用来分辨"物理追帧螺旋"（一帧渲染慢->下一帧要把好几个FixedUpdate补上->更慢->更卡）
    // 还是纯GC/内存问题——同一个Update里到底跑了几次FixedUpdate，以及Mono堆的绝对
    // 大小是不是在整场游戏里单调爬升（真泄漏）而不是原地小范围来回震荡（正常churn）。
    private int _fixedUpdateCountThisFrame;
    private long _peakMonoMemory;

    // 用 Unity 内置的分类计时器（Recorder，读的是跟 Profiler 窗口 CPU Usage 面板
    // 同一份底层数据）在后台统计各分类耗时，不需要连 Profiler、不需要认识那个界面。
    private class NamedRecorder
    {
        public string label;
        public Recorder recorder;
        public long lastCumulativeNs;  // 上一帧读到的累计值——Recorder.elapsedNanoseconds
                                        // 是"从enable以来的累计总和"，不是单帧值，必须自己
                                        // 每帧做差分，否则数字会越攒越大、看着比帧耗时还离谱。
        public float thisFrameMs;      // 差分算出来的、本帧真正的耗时
    }
    private NamedRecorder[] _recorders;

    private string _lastTextureReportLine = "(尚未采集)";
    private float _nextTextureScanTime;

    // 只保留"本局至今最严重的一次卡顿"，不再攒一整串屏幕列表——报告要的是
    // 能一眼定位问题的关键样本，不是越多越好。
    private string _worstSpikeSummary = "(本局至今没有明显卡顿)";
    private float _worstSpikeMs;

    // ── 一键性能报告 ──────────────────────────────────────────────────────────
    // 按 F10 手动把这局游戏到目前为止的整体表现打包发到 Discord——跟
    // SkyPrisonErrorReporter 报错走同一个 webhook，不用再靠人工截图/念数据。
    private float _sessionStartTime;
    private float _minFps = float.MaxValue;
    private int _totalSpikeCount;
    private int _sessionStartGcCount;

    // 每次场景加载都重新给一段"宽限期"——不止游戏刚启动那一次，中途切地图/切场景
    // 同样会有一下加载卡顿，同样不该算进"最严重卡顿"里。
    private float _graceUntilTime;

    private void FixedUpdate()
    {
        _fixedUpdateCountThisFrame++;
    }

    private void InitRecorders()
    {
        string[] markerNames =
        {
            "FixedBehaviourUpdate", // 所有脚本 FixedUpdate 合计
            "BehaviourUpdate",      // 所有脚本 Update 合计
            // 2026-07-15：加过 "BehaviourLateUpdate" 想追踪 LateUpdate（Spine 网格生成
            // 习惯上在这里做），但连续好几份报告里从没出现过这个分类的数值——大概率是
            // 这个简化别名在这个 Unity 版本里根本不存在，Recorder.Get() 静默返回 null 被
            // InitRecorders 跳过了。Unity 内置计时器很多用"阶段.子阶段"带点全名，这次把
            // 简化名和带点全名都加上，看哪个真的存在、有数值。同时加几个着色器编译相关的
            // 候选——首次进入新区域、遇到没编译过的着色器变体，是经典的"PlayerLoop巨大
            // 但没有GC支撑"型卡顿来源，跟这次报告的特征吻合。
            "BehaviourLateUpdate",
            "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "PostLateUpdate.ScriptRunBehaviourLateUpdate",
            "Shader.CreateGPUProgram",
            "Shader.ParseSurfaceShader",
            "Culling",
            "UpdateDepthTexture",
            "Physics.Simulate",
            "Physics.Processing",
            "Physics2D.Simulate",
            "Physics2D.Processing",
            "ParticleSystem.Update", // VFX（血液/落地尘土等）粒子系统更新
            "Camera.Render",
            "Canvas.SendWillRenderCanvases", // UI 布局刷新（Canvas重建）
            "Canvas.BuildBatch",
            "Animator.Update",
            "GC.Collect",
            "Gfx.WaitForPresent",        // CPU 等 GPU 把上一帧画完
            "Gfx.WaitForPresentOnGfxThread",
            "Gfx.WaitForRenderThread",
            "Gfx.PresentFrame",
            "Overhead",
            "PlayerLoop",                // 整个 PlayerLoop 总耗时，作为参照基准
            "WaitForTargetFPS",
            "RenderLoop.Draw",
            "ScriptableRenderContext.Submit",
        };

        var list = new List<NamedRecorder>(markerNames.Length);
        foreach (var name in markerNames)
        {
            var rec = Recorder.Get(name);
            if (rec == null) continue;
            rec.enabled = true;
            list.Add(new NamedRecorder { label = name, recorder = rec, lastCumulativeNs = rec.elapsedNanoseconds });
        }
        _recorders = list.ToArray();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isPlaying) return;
        if (_instance != null) return;
        var go = new GameObject("[SkyPrisonFrameSpikeWatchdog]");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<SkyPrisonFrameSpikeWatchdog>();
    }

    private void Awake()
    {
        if (!enablePeriodicAutoReport && HasAutoEnableCommandLineArg())
            enablePeriodicAutoReport = true;
        _nextAutoReportTime = Time.unscaledTime + Mathf.Max(30f, autoReportIntervalSeconds);

        _lastGcCount0 = System.GC.CollectionCount(0);
        _lastMonoMemory = System.GC.GetTotalMemory(false);
        _sessionStartTime = Time.unscaledTime;
        _sessionStartGcCount = _lastGcCount0;
        _graceUntilTime = Time.unscaledTime + startupGracePeriod;
        _framesSinceGraceStart = 0;
        InitRecorders();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        _graceUntilTime = Time.unscaledTime + startupGracePeriod;
        _framesSinceGraceStart = 0;
    }

    // 扫一遍当前所有加载中的 Texture（包含 Texture2D 和 RenderTexture），按
    // Profiler 报告的实际显存占用从大到小排序，只留前5名+总计给报告用。
    // FindObjectsOfTypeAll 本身有点重，所以只隔几秒扫一次，不放进每帧 Update。
    private void ScanTextureMemory()
    {
        Texture[] all = Resources.FindObjectsOfTypeAll<Texture>();

        long totalBytes = 0;
        var entries = new List<(string name, long bytes, string type)>(all.Length);
        foreach (var tex in all)
        {
            if (tex == null) continue;
            long bytes = Profiler.GetRuntimeMemorySizeLong(tex);
            totalBytes += bytes;
            entries.Add((tex.name, bytes, tex.GetType().Name));
        }

        entries.Sort((a, b) => b.bytes.CompareTo(a.bytes));

        var sb = new System.Text.StringBuilder();
        sb.Append("共").Append(entries.Count).Append("张/合计")
          .Append((totalBytes / 1024f / 1024f).ToString("F1")).Append("MB");

        int shown = Mathf.Min(5, entries.Count);
        for (int i = 0; i < shown; i++)
        {
            var e = entries[i];
            sb.Append("\n  ").Append(i + 1).Append(". ")
              .Append(string.IsNullOrEmpty(e.name) ? "(无名字)" : e.name)
              .Append(" [").Append(e.type).Append("] ")
              .Append((e.bytes / 1024f / 1024f).ToString("F2")).Append("MB");
        }

        _lastTextureReportLine = sb.ToString();
    }

    // 一键性能报告——把这局到目前为止的整体表现打包发到 Discord，跟 SkyPrisonErrorReporter
    // 报错走同一个 webhook（同一个 SkyPrisonErrorReportingSettings 里配的 URL）。
    private void SendPerformanceReportToDiscord()
    {
        float sessionMinutes = (Time.unscaledTime - _sessionStartTime) / 60f;
        int gcSinceStart = System.GC.CollectionCount(0) - _sessionStartGcCount;

        string detail =
            $"本局时长: {sessionMinutes:F1}分钟\n" +
            $"最低FPS: {(_minFps == float.MaxValue ? 0f : _minFps):F0}\n" +
            $"卡顿次数(超过{spikeThresholdMs:F0}ms的帧): {_totalSpikeCount}\n" +
            $"本局GC次数: {gcSinceStart}\n" +
            $"Mono堆峰值: {_peakMonoMemory / 1024f / 1024f:F1}MB\n" +
            $"最严重的一次卡顿: {_worstSpikeSummary}\n" +
            $"贴图内存: {_lastTextureReportLine}";

        SkyPrisonErrorReporter.ReportInfo("性能报告(F10手动触发)", detail);
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextTextureScanTime)
        {
            _nextTextureScanTime = Time.unscaledTime + Mathf.Max(2f, textureScanInterval);
            ScanTextureMemory();
        }

        _framesSinceGraceStart++;

        float frameMs = Time.unscaledDeltaTime * 1000f;
        float currentFps = 1000f / Mathf.Max(1f, frameMs);
        bool pastFrameGrace = _framesSinceGraceStart > startupGraceFrames;
        bool pastTimeGrace = Time.unscaledTime >= _graceUntilTime;
        if (pastFrameGrace && pastTimeGrace && currentFps < _minFps) _minFps = currentFps;

        if (Input.GetKeyDown(KeyCode.F10))
            SendPerformanceReportToDiscord();

        if (enablePeriodicAutoReport && Time.unscaledTime >= _nextAutoReportTime)
        {
            _nextAutoReportTime = Time.unscaledTime + Mathf.Max(30f, autoReportIntervalSeconds);
            SendPerformanceReportToDiscord();
        }

        if (_playerMotor == null)
        {
            _motorSearchCooldown -= Time.unscaledDeltaTime;
            if (_motorSearchCooldown <= 0f)
            {
                TryFindPlayerMotor();
                _motorSearchCooldown = 1.5f;
            }
        }

        int gcCount0Now = System.GC.CollectionCount(0);
        long monoMemNow = System.GC.GetTotalMemory(false);
        int gcDelta = gcCount0Now - _lastGcCount0;
        long memDeltaBytes = monoMemNow - _lastMonoMemory;
        _lastGcCount0 = gcCount0Now;
        _lastMonoMemory = monoMemNow;
        if (monoMemNow > _peakMonoMemory)
            _peakMonoMemory = monoMemNow;

        // 每一帧都要做差分，不能只在记录的时候才读——否则读到的就是"从上次记录
        // 到现在，好几帧的总和"，跟这一帧的真实耗时对不上。
        if (_recorders != null)
        {
            foreach (var nr in _recorders)
            {
                long cur = nr.recorder.elapsedNanoseconds;
                long delta = cur - nr.lastCumulativeNs;
                if (delta < 0) delta = cur; // Recorder 内部偶尔会自己清零，负数时按当前值算
                nr.thisFrameMs = delta / 1_000_000f;
                nr.lastCumulativeNs = cur;
            }
        }

        bool inStartupGrace = !pastFrameGrace || !pastTimeGrace;

        if (!inStartupGrace && frameMs >= spikeThresholdMs && Time.unscaledTime - _lastLogTime >= logCooldown)
        {
            _lastLogTime = Time.unscaledTime;
            _totalSpikeCount++;

            if (frameMs > _worstSpikeMs)
            {
                _worstSpikeMs = frameMs;

                string motionState = "未知/找不到玩家Motor";
                if (_playerMotor != null)
                {
                    motionState = _playerMotor.IsJumping ? "跳跃中"
                                : _playerMotor.IsFalling ? "下落中"
                                : _playerMotor.IsGrounded ? "在地面"
                                : "滑动中";
                }

                string breakdown = BuildRecorderBreakdown();
                _worstSpikeSummary = $"帧{Time.frameCount} {frameMs:F0}ms(~{1000f / Mathf.Max(1f, frameMs):F0}FPS) " +
                    $"状态={motionState} GC={gcDelta} 物理步数={_fixedUpdateCountThisFrame} 分类耗时:{breakdown}";
            }
        }

        _fixedUpdateCountThisFrame = 0;
    }

    // 只挑本帧真正有耗时的分类拼进去，凑不出数据的（这台机器/这个Unity版本没有
    // 这个内置Marker）直接跳过。
    private string BuildRecorderBreakdown()
    {
        if (_recorders == null || _recorders.Length == 0)
            return "(无可用分类数据)";

        var sb = new System.Text.StringBuilder();
        bool any = false;
        foreach (var nr in _recorders)
        {
            float ms = nr.thisFrameMs;
            if (ms < 0.05f) continue; // 太小的不计入，减少噪音
            if (any) sb.Append(" | ");
            sb.Append(nr.label).Append('=').Append(ms.ToString("F1")).Append("ms");
            any = true;
        }

        return any ? sb.ToString() : "(各分类都很小)";
    }

    private bool HasAutoEnableCommandLineArg()
    {
        if (string.IsNullOrWhiteSpace(autoEnableCommandLineArg))
            return false;

        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], autoEnableCommandLineArg, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void TryFindPlayerMotor()
    {
        var auth = FindObjectOfType<SkyPrisonPlayerAuthority>();
        var player = auth?.CurrentPlayer;
        _playerMotor = player != null
            ? player.GetComponentInChildren<TerrainGroundMotorV5>(true)
            : null;

        if (_playerMotor == null)
            _playerMotor = FindObjectOfType<TerrainGroundMotorV5>();
    }
}
