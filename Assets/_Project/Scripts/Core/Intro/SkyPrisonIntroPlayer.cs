using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 开头商标 / 片头播放器：按 SkyPrisonIntroPlaylist 的顺序依次全屏播放。
/// 玩家点一下鼠标 / 按任意键 / 触屏，直接跳过全部，进入主界面。
/// 用法：SkyPrisonIntroPlayer.PlayThen(() => SceneLoader.LoadScene("MainMenu"));
/// 列表为空或没有这个资产时，立刻回调（不影响启动）。
/// </summary>
public sealed class SkyPrisonIntroPlayer : MonoBehaviour
{
    public static bool IsPlaying { get; private set; }

    List<SkyPrisonIntroEntry> entries;
    Action onDone;
    int index;
    float t, duration;
    bool finished;
    int frames;
    bool warmedUp;       // 热身完成才开始计时和放声音；热身期间一直是纯黑
    float waited;
    int stableFrames;
    double dspStart;     // 这一项开始的音频时钟时刻：画面时间直接跟着音频时钟走，声音和画面就不会越差越远
    bool useDsp;
    float realT;
    bool videoStarted;
    bool videoEnded;

    GameObject canvasGo;
    Image bg;
    RawImage raw;
    RectTransform rawRt;
    AudioSource audioSrc;
    Material shaderMat;
    VideoPlayer vp;
    RenderTexture vrt;

    static bool s_startupPlayed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { s_startupPlayed = false; IsPlaying = false; }

    /// <summary>
    /// 游戏启动时自动播一次：第一个场景（Build Settings 里序号 0，也就是 MainMenu）加载完成后触发。
    /// 编辑器里从别的场景直接按 Play 不会播，只有从第一个场景启动才播，免得每次调试地图都要看片头。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoPlayAtStartup()
    {
        if (s_startupPlayed) return;
        s_startupPlayed = true;
        var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (sc.buildIndex != 0)
        {
            Debug.Log("[SkyPrisonIntro] 启动检查：当前场景「" + sc.name + "」buildIndex=" + sc.buildIndex + "，不是第一个场景（Build Settings 序号 0），本次不播片头。想看就从 MainMenu 场景按 Play。");
            return;
        }
        Debug.Log("[SkyPrisonIntro] 启动检查：场景「" + sc.name + "」是第一个场景，开始播片头。");
        PlayThen(null);
    }

    public static void PlayThen(Action onDone)
    {
        s_startupPlayed = true;
        var list = Resources.Load<SkyPrisonIntroPlaylist>(SkyPrisonIntroPlaylist.ResourcePath);
        var playable = new List<SkyPrisonIntroEntry>();
        if (list != null && list.entries != null)
            foreach (var e in list.entries) if (e != null && e.enabled && e.IsPlayable()) playable.Add(e);
#if UNITY_EDITOR
        if (list != null && list.skipInEditor) playable.Clear();
#endif
        if (playable.Count == 0)
        {
            Debug.Log("[SkyPrisonIntro] 没有可播放的片头（列表为空、全部关闭、缺内容，或勾了「编辑器里跳过片头」），直接继续。");
            if (onDone != null) onDone(); return;
        }

        var go = new GameObject("SkyPrisonIntroPlayer");
        DontDestroyOnLoad(go);
        go.AddComponent<SkyPrisonIntroPlayer>().Begin(playable, onDone);
    }

    void Begin(List<SkyPrisonIntroEntry> list, Action done)
    {
        entries = list; onDone = done; IsPlaying = true;

        canvasGo = new GameObject("IntroCanvas", typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var cv = canvasGo.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 32000;

        var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false); Stretch(bgGo.GetComponent<RectTransform>());
        bg = bgGo.GetComponent<Image>(); bg.color = Color.black; bg.raycastTarget = true;     // 挡住下面的界面点击

        var rawGo = new GameObject("Content", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(canvasGo.transform, false);
        rawRt = rawGo.GetComponent<RectTransform>(); Stretch(rawRt);
        raw = rawGo.GetComponent<RawImage>(); raw.raycastTarget = false;

        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false; audioSrc.spatialBlend = 0f; audioSrc.ignoreListenerPause = true;

        StartEntry(0);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    void CleanupEntry()
    {
        if (audioSrc != null) { audioSrc.Stop(); audioSrc.clip = null; }
        if (vp != null) { vp.Stop(); Destroy(vp); vp = null; }
        if (vrt != null) { vrt.Release(); Destroy(vrt); vrt = null; }
        if (shaderMat != null) { Destroy(shaderMat); shaderMat = null; }
        if (raw != null) { raw.material = null; raw.texture = null; }
    }

    void StartEntry(int i)
    {
        CleanupEntry();
        if (i >= entries.Count) { Finish(); return; }
        index = i; t = 0f; realT = 0f; useDsp = false; videoStarted = false; videoEnded = false;
        var e = entries[i];
        Stretch(rawRt);

        switch (e.type)
        {
            case SkyPrisonIntroType.BlackFlameShader:
            {
                var sh = Resources.Load<Shader>("Shaders/IntroBlackFlame");
                if (sh == null) { Debug.LogWarning("[SkyPrisonIntro] 找不到着色器 Resources/Shaders/IntroBlackFlame，这一项被跳过。"); StartEntry(i + 1); return; }
                shaderMat = new Material(sh);
                shaderMat.SetTexture("_Mask", e.mask);
                shaderMat.SetFloat("_Duration", SkyPrisonBlackFlameTimeline.ShaderDuration);
                shaderMat.SetFloat("_T", 0f); shaderMat.SetFloat("_Aspect", Screen.width / (float)Mathf.Max(1, Screen.height));   // T=0 画出来是纯黑：热身期间就把着色器和遮罩贴图预热掉
                raw.material = shaderMat; raw.texture = null; raw.color = Color.white;
                duration = SkyPrisonBlackFlameTimeline.Duration;
                break;
            }
            case SkyPrisonIntroType.Image:
            {
                raw.texture = e.image; raw.color = new Color(1f, 1f, 1f, 0f);
                FitContain(e.image.width, e.image.height);
                duration = e.Duration();
                break;
            }
            case SkyPrisonIntroType.Video:
            {
                vrt = new RenderTexture(Mathf.Max(16, (int)e.video.width), Mathf.Max(16, (int)e.video.height), 0);
                vp = gameObject.AddComponent<VideoPlayer>();
                vp.playOnAwake = false; vp.isLooping = false; vp.clip = e.video;
                vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = vrt;
                vp.audioOutputMode = VideoAudioOutputMode.Direct;
                vp.loopPointReached += OnVideoEnd;              // 用视频自己的“播放结束”事件判断，不用 isPlaying（缓冲时它会短暂为 false）
                vp.Prepare();                                   // 先预载，等热身完成才 Play：视频自带声音，音画同步由视频文件保证
                raw.texture = vrt; raw.color = Color.white;
                FitContain((int)e.video.width, (int)e.video.height);
                duration = e.Duration() + 1.0f;       // 兜底，正常以播放结束为准
                break;
            }
        }

        if (e.audio != null && e.type != SkyPrisonIntroType.Video)
        {
            audioSrc.clip = e.audio; audioSrc.volume = e.audioVolume;
            e.audio.LoadAudioData();                                  // 预加载，免得第一次播放时才读盘（导入设置里没勾预加载）
            if (warmedUp) PlayEntryAudio(e);
        }
    }

    void OnVideoEnd(VideoPlayer source) { videoEnded = true; }

    void PlayEntryAudio(SkyPrisonIntroEntry e)
    {
        if (e.audio == null || e.type == SkyPrisonIntroType.Video) return;
        audioSrc.clip = e.audio; audioSrc.volume = e.audioVolume;
        dspStart = AudioSettings.dspTime + 0.1;                                   // 先留 0.1 秒（画面是黑的），让音频和画面一起从同一个时刻起算
        audioSrc.PlayScheduled(dspStart + Mathf.Max(0f, e.audioDelay));
        useDsp = true;
    }

    void FitContain(int w, int h)
    {
        float sw = Screen.width, sh = Screen.height;
        float s = Mathf.Min(sw / w, sh / h);
        rawRt.anchorMin = rawRt.anchorMax = new Vector2(0.5f, 0.5f); rawRt.pivot = new Vector2(0.5f, 0.5f);
        rawRt.anchoredPosition = Vector2.zero; rawRt.sizeDelta = new Vector2(w * s, h * s);
    }

    static bool SkipPressed()
    {
        if (Input.anyKeyDown) return true;                    // 含鼠标按键
        for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
        return false;
    }

    void Update()
    {
        if (finished) return;
        frames++;
        float dt = Time.unscaledDeltaTime;
        if (frames > 3 && SkipPressed()) { Finish("玩家点击/按键跳过"); return; }     // 点一下 = 跳过全部（前 3 帧忽略，免得把进游戏那一下点击算进去）

        // ── 热身：启动那几帧主菜单还在加载、着色器第一次绘制、音频还没载入，会很卡。
        //    先停在纯黑里，等到：连续 6 帧不卡、音频已载入、场景里有 AudioListener（最多等 3 秒），再开始计时和放声音。
        if (!warmedUp)
        {
            waited += dt;
            var w = entries[index];
            bool audioReady = w.type == SkyPrisonIntroType.Video
                ? (vp != null && vp.isPrepared)
                : (w.audio == null || w.audio.loadState == AudioDataLoadState.Loaded || w.audio.loadState == AudioDataLoadState.Failed);
            bool listener = FindFirstObjectByType<AudioListener>() != null;
            stableFrames = dt < 0.06f ? stableFrames + 1 : 0;
            if ((stableFrames >= 4 && audioReady && listener) || waited > 3f)
            {
                warmedUp = true; t = 0f;
                Debug.Log("[SkyPrisonIntro] 热身完成（等了 " + waited.ToString("0.00") + "s，音频就绪=" + audioReady + "，AudioListener=" + listener + "），开始播放。");
                if (w.type == SkyPrisonIntroType.Video) { if (vp != null && vp.isPrepared) { vp.Play(); videoStarted = true; } }
                else PlayEntryAudio(w);
            }
            else return;
        }
        realT += dt;
        if (entries[index].type == SkyPrisonIntroType.Video && !videoStarted)
        {
            if (vp != null && vp.isPrepared) { vp.Play(); videoStarted = true; t = 0f; }
            else if (realT > 5f) { StartEntry(index + 1); }
            return;
        }
        if (useDsp)
        {
            // 画面时间 = 音频时钟：卡一帧就是画面跳过去，不会越拖越慢、让声音跑到前面。
            float td = Mathf.Max(0f, (float)(AudioSettings.dspTime - dspStart));
            if (td < realT - 0.75f) td = realT;             // 音频时钟没在走（没有声卡 / 被暂停）就退回真实时间，不会卡死在片头
            t = td;
        }
        else t += Mathf.Min(dt, 0.05f);     // 没有音效的项：偶尔再卡一下，每帧也最多只算 1/20 秒

        var e = entries[index];
        switch (e.type)
        {
            case SkyPrisonIntroType.BlackFlameShader:
                shaderMat.SetFloat("_T", SkyPrisonBlackFlameTimeline.Warp(t));
                shaderMat.SetFloat("_Aspect", Screen.width / (float)Mathf.Max(1, Screen.height));
                break;
            case SkyPrisonIntroType.Image:
            {
                float a = 1f;
                if (e.fadeIn > 0f) a = Mathf.Min(a, t / e.fadeIn);
                if (e.fadeOut > 0f) a = Mathf.Min(a, (duration - t) / e.fadeOut);
                raw.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                break;
            }
            case SkyPrisonIntroType.Video:
                if (videoStarted && videoEnded) { StartEntry(index + 1); return; }
                break;
        }

        if (t >= duration) StartEntry(index + 1);
    }

    void Finish(string reason = "播完")
    {
        if (finished) return;
        Debug.Log("[SkyPrisonIntro] 结束：" + reason + "（第 " + (index + 1) + " 项，t=" + t.ToString("0.00") + "s，共 " + frames + " 帧）");
        finished = true; IsPlaying = false;
        CleanupEntry();
        var cb = onDone; onDone = null;
        Destroy(gameObject);
        if (cb != null) cb();
    }

    void OnDestroy() { IsPlaying = false; CleanupEntry(); }
}
