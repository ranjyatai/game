using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 全项目统一的报错入口。任何"已知会失败、失败后没法优雅恢复"的地方都应该走这里，
/// 不要各自写各自的报错弹窗（SceneLoader 最早那版报错框已经改成调用这个了）。
///
/// 两档严重程度：
///   Report()      —— 非致命：只写日志文件，不打断玩家，适合"这次没成功但游戏还能继续"的情况
///                     （比如自动保存失败一次，AutoSaveIndicatorUI 已经有自己的失败提示了）。
///   ReportFatal() —— 致命：写日志文件 + 弹一个带错误编号的全屏提示，玩家点确认后安全退回主菜单。
///                     用在"继续下去只会让状态更乱"的地方（比如场景加载失败）。
///
/// 日志文件：persistentDataPath/logs/error_log.txt，每条记录带时间戳 + 错误编号 + 详情，
/// 一直追加不覆盖——玩家反馈问题时把这个文件发过来，比截图靠谱。
/// </summary>
public static class SkyPrisonErrorReporter
{
    private const string LogFileName = "error_log.txt";
    private static string LogFilePath => Path.Combine(GamePaths.Logs, LogFileName);

    /// <summary>非致命报错：写日志，不打断玩家。ex 可选——传了异常对象会把堆栈也带上，
    /// 不然只有 e.Message 那一句话，Discord 上根本没法定位是哪个文件哪一行出的问题。</summary>
    public static void Report(string errorCode, string detail, Exception ex = null)
    {
        Debug.LogError($"[{errorCode}] {detail}");
        WriteToLogFile(errorCode, detail, fatal: false, ex);
        PostToDiscord("ERROR", errorCode, detail, ex);
    }

    /// <summary>致命报错：写日志 + 弹全屏提示，玩家确认后安全退回主菜单。</summary>
    public static void ReportFatal(string errorCode, string detail, Exception ex = null)
    {
        Debug.LogError($"[{errorCode}] (FATAL) {detail}");
        WriteToLogFile(errorCode, detail, fatal: true, ex);
        PostToDiscord("FATAL", errorCode, detail, ex);
        ShowFatalDialog(errorCode, detail);
    }

    /// <summary>非报错类信息上报——目前给 SkyPrisonFrameSpikeWatchdog 的性能报告用。
    /// 只发 Discord，不写 error_log.txt（那个文件按惯例只存真报错），不弹窗。</summary>
    public static void ReportInfo(string title, string detail)
    {
        Debug.Log($"[{title}] {detail}");
        PostToDiscord("INFO", title, detail, null);
    }

    // ── Discord Webhook 自动上报（不阻塞报错弹窗，发出去就不管了）──────────

    private static void PostToDiscord(string tag, string errorCode, string detail, Exception ex)
    {
        var settings = Resources.Load<SkyPrisonErrorReportingSettings>("SkyPrisonErrorReportingSettings");
        string url = settings != null ? settings.discordWebhookUrl : null;
        if (string.IsNullOrEmpty(url)) return;

        string sceneName = SceneManager.GetActiveScene().name;
        string content = $"**[{tag}] {errorCode}**\n{detail}\n"
            + $"场景: {sceneName} | 平台: {Application.platform} | 版本: {Application.version} | 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        if (ex != null)
        {
            // Discord 单条消息硬上限 2000 字符，堆栈截断到前 800 个字符，够定位到出问题的
            // 那几行调用链就行，完整版本还是在 error_log.txt 里。
            string trace = ex.StackTrace ?? "";
            if (trace.Length > 800) trace = trace.Substring(0, 800) + "...(截断，完整堆栈见 error_log.txt)";
            content += $"\n```\n{ex.GetType().Name}: {ex.Message}\n{trace}\n```";
        }
        string json = "{\"content\":\"" + EscapeJson(content) + "\"}";

        var request = new UnityWebRequest(url, "POST");
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        var op = request.SendWebRequest();
        op.completed += _ =>
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SkyPrisonErrorReporter] Discord 上报失败：{request.error}");
            }
            request.Dispose();
        };
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                 .Replace("\n", "\\n").Replace("\r", "");
    }

    private static void WriteToLogFile(string errorCode, string detail, bool fatal, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(GamePaths.Logs);
            string sceneName = SceneManager.GetActiveScene().name;
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL" : "ERROR")} {errorCode} @{sceneName}: {detail}\n";
            if (ex != null) line += $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n";
            File.AppendAllText(LogFilePath, line);
        }
        catch (Exception e)
        {
            // 连报错日志都写不进去（磁盘满/权限问题），只能靠 Console，不能再往下抛异常
            // 打断报错流程本身。
            Debug.LogError($"[SkyPrisonErrorReporter] 写入错误日志失败：{e.Message}");
        }
    }

    // ── 致命报错弹窗：紧凑小弹框风格（跟背包"丢弃物品/拆分"数量弹窗同一类），
    //    背景是截屏+高斯模糊再转黑白、按小盒子实际占屏比例裁 UV（不是整屏接管的大窗口，
    //    也不是纯色黑底）。弹窗打开时游戏必须真正冻结（Time.timeScale=0 + ExternalBlock），
    //    键鼠/手柄都能确认，底部挂标准提示条。────────────────────────────────

    private static void ShowFatalDialog(string errorCode, string detail)
    {
        var runnerGo = new GameObject("[FatalDialogRunner]") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(runnerGo);
        var runner = runnerGo.AddComponent<FatalDialogController>();
        runner.StartCoroutine(BuildFatalDialogRoutine(runner, errorCode, detail));
    }

    /// <summary>
    /// 弹窗打开期间冻结游戏（timeScale=0 + 挡全局交互）、监听键鼠/手柄确认输入的宿主。
    /// 静态类没法自己跑 Update()/协程，弹窗存在期间借这个 MonoBehaviour 常驻。
    /// </summary>
    private class FatalDialogController : MonoBehaviour
    {
        private Button _confirmButton;
        private float _savedTimeScale;
        private bool _blocking;

        public void BeginBlocking(Button confirmButton)
        {
            _confirmButton = confirmButton;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            SkyPrisonWindowManager_V1.ExternalBlock = true;
            _blocking = true;
        }

        private void Update()
        {
            if (!_blocking || _confirmButton == null) return;
            // Time.timeScale=0 不影响 Input 轮询，键鼠/手柄确认键都能触发同一个按钮。
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.JoystickButton0))
            {
                _confirmButton.onClick.Invoke();
            }
        }

        public void EndBlocking()
        {
            if (!_blocking) return;
            _blocking = false;
            Time.timeScale = _savedTimeScale;
            SkyPrisonWindowManager_V1.ExternalBlock = false;
        }
    }

    private static IEnumerator BuildFatalDialogRoutine(FatalDialogController runner, string errorCode, string detail)
    {
        // 截屏必须等这一帧渲染完成，不然拿到的是渲染中途/上一帧画面。
        yield return new WaitForEndOfFrame();

        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;
        TMP_FontAsset font = LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        RenderTexture blurRT = CaptureAndBlurScreen();

        var root = new GameObject("[FatalErrorDialog]");
        UnityEngine.Object.DontDestroyOnLoad(root);
        runner.transform.SetParent(root.transform, false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32700; // 压过几乎所有其它窗口，报错必须能看见
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        var rootRt = (RectTransform)root.transform;

        // 半透黑背板：只负责挡交互，不做全屏模糊接管——模糊只在小盒子内部展示。
        var backdrop = MakeRect("Backdrop", rootRt, Vector2.zero, Vector2.one);
        var backdropImg = backdrop.gameObject.AddComponent<Image>();
        backdropImg.color = new Color(0f, 0f, 0f, 0.55f);
        backdropImg.raycastTarget = true;

        var box = MakeRect("Box", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(820f, 420f);

        // 盒子背景：截屏模糊图转黑白，按盒子在整屏里的实际占比裁一小块 UV——跟
        // PauseMenuController.ShowReturnMenuConfirm 同一套做法，看起来像"背景那一小片
        // 正好透在这里"，而不是整张缩略图被挤扁。
        if (blurRT != null)
        {
            var blurImg = box.gameObject.AddComponent<RawImage>();
            blurImg.texture = blurRT;
            float wFrac = box.rect.width / rootRt.rect.width;
            float hFrac = box.rect.height / rootRt.rect.height;
            blurImg.uvRect = new Rect(0.5f - wFrac * 0.5f, 0.5f - hFrac * 0.5f, wFrac, hFrac);

            var desatShader = Shader.Find("UI/SkyPrison/Desaturate");
            if (desatShader != null)
            {
                var desatMat = new Material(desatShader) { hideFlags = HideFlags.HideAndDontSave };
                desatMat.SetFloat("_Saturation", 0f); // 完全黑白
                blurImg.material = desatMat;
            }
        }
        else
        {
            box.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.10f, 1f);
        }
        var boxTint = MakeRect("Tint", box, Vector2.zero, Vector2.one);
        boxTint.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        AddCornerBrackets(box, Color.white, 26f, 2f);

        // 参考怪物猎人的"参加失败"弹窗：不用红色大标题，第一行是白色失败原因文案，
        // 第二行紧跟着同样大小的错误编号——两行都走字典表。
        var bodyTmp = MakeText(box, "Body", detail, 32f, FontStyles.Normal, font);
        bodyTmp.color = new Color(0.90f, 0.92f, 0.90f, 1f);
        bodyTmp.rectTransform.anchorMin = new Vector2(0.08f, 0.52f);
        bodyTmp.rectTransform.anchorMax = new Vector2(0.92f, 0.80f);

        string codeLine = string.Format(L("ui_fatal_error_code_format", "错误代码：{0}"), errorCode);
        var codeTmp = MakeText(box, "Code", codeLine, 32f, FontStyles.Normal, font);
        codeTmp.color = new Color(0.75f, 0.78f, 0.76f, 1f);
        codeTmp.rectTransform.anchorMin = new Vector2(0.08f, 0.38f);
        codeTmp.rectTransform.anchorMax = new Vector2(0.92f, 0.50f);

        var btnRt = MakeRect("Confirm", box, new Vector2(0.5f, 0.14f), new Vector2(0.5f, 0.14f));
        btnRt.pivot = new Vector2(0.5f, 0f);
        btnRt.sizeDelta = new Vector2(400f, 88f);
        btnRt.gameObject.AddComponent<Image>().color = Color.clear;
        AddOutline(btnRt, Color.white, 1f);
        var btn = btnRt.gameObject.AddComponent<Button>();
        var btnLabel = MakeText(btnRt, "Label", L("ui_fatal_error_confirm", "返回主菜单"), 30f, FontStyles.Normal, font);
        btnLabel.color = new Color(0.88f, 0.88f, 0.90f, 1f);
        btnLabel.rectTransform.anchorMin = Vector2.zero;
        btnLabel.rectTransform.anchorMax = Vector2.one;
        SkyPrisonUIButtonFeedback.Attach(btnRt.gameObject);
        btn.onClick.AddListener(() =>
        {
            runner.EndBlocking();
            SkyPrisonWindowHintBar.GetOrCreate().Clear();
            UnityEngine.Object.Destroy(root);
            SceneLoader.LoadMainMenu();
        });

        // 底部按键提示：确定=返回主菜单，键鼠/手柄图标都走标准提示条组件。
        SkyPrisonWindowHintBar.GetOrCreate().Show(new[]
        {
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.Interact, L("ui_fatal_error_confirm", "返回主菜单")),
        });

        // 冻结游戏放在弹窗内容搭建完之后——避免 timeScale=0 影响到上面截图/构建过程里
        // 任何依赖 Time 的逻辑（虽然目前没有，但顺序上更安全）。
        runner.BeginBlocking(btn);
    }

    // ── 截屏+逐级放大高斯模糊金字塔（跟 PauseMenuController.CaptureAndBlurScreen 同一套规范实现）──

    private static RenderTexture CaptureAndBlurScreen()
    {
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        int w = Mathf.Max(4, shot.width);
        int h = Mathf.Max(4, shot.height);

        var full = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR) { hideFlags = HideFlags.HideAndDontSave };
        full.Create();
        Graphics.Blit(shot, full);
        UnityEngine.Object.Destroy(shot);

        const int SrcLongEdge = 960;
        float aspect = (float)w / h;
        int baseW, baseH;
        if (aspect >= 1f) { baseW = SrcLongEdge; baseH = Mathf.Max(4, Mathf.RoundToInt(SrcLongEdge / aspect)); }
        else              { baseH = SrcLongEdge; baseW = Mathf.Max(4, Mathf.RoundToInt(SrcLongEdge * aspect)); }

        var temps = new List<RenderTexture>();
        var baseRT = RenderTexture.GetTemporary(baseW, baseH, 0, RenderTextureFormat.DefaultHDR);
        baseRT.filterMode = FilterMode.Bilinear;
        Graphics.Blit(full, baseRT);
        temps.Add(baseRT);

        const int MinBlurEdge = 40;
        const int MaxBlurSteps = 10;
        var downSizes = new List<Vector2Int> { new Vector2Int(baseW, baseH) };
        RenderTexture src = baseRT;
        int curW = baseW, curH = baseH;
        for (int i = 0; i < MaxBlurSteps && curW > MinBlurEdge && curH > MinBlurEdge; i++)
        {
            curW = Mathf.Max(MinBlurEdge, curW / 2);
            curH = Mathf.Max(MinBlurEdge, curH / 2);
            var down = RenderTexture.GetTemporary(curW, curH, 0, RenderTextureFormat.DefaultHDR);
            down.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, down);
            temps.Add(down);
            downSizes.Add(new Vector2Int(curW, curH));
            src = down;
        }

        for (int i = downSizes.Count - 2; i >= 0; i--)
        {
            Vector2Int size = downSizes[i];
            var up = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.DefaultHDR);
            up.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, up);
            temps.Add(up);
            src = up;
        }

        var result = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR) { hideFlags = HideFlags.HideAndDontSave };
        result.filterMode = FilterMode.Bilinear;
        result.Create();
        Graphics.Blit(src, result);

        foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
        full.Release();
        UnityEngine.Object.Destroy(full);

        return result;
    }

    // ── 字体（Build 里动态创建 TMP 文字必须走这套，否则显示方块）───────────────

    private static TMP_FontAsset LoadTMPFont(string assetName)
    {
#if UNITY_EDITOR
        string path = $"Assets/_Project/UIUX/Fonts/TMP/{assetName}.asset";
        var fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (fa != null) return fa;
        string[] guids = UnityEditor.AssetDatabase.FindAssets(assetName + " t:TMP_FontAsset");
        if (guids.Length > 0)
        {
            fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            if (fa != null) return fa;
        }
#endif
        return Resources.Load<TMP_FontAsset>($"Fonts & Materials/{assetName}");
    }

    // ── 通用 UI 构件（角标/描边/矩形/文字，跟 PauseMenuController 同一套写法）────

    private static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static TMP_Text MakeText(Transform parent, string name, string text, float size, FontStyles style, TMP_FontAsset font)
    {
        var rt = MakeRect(name, parent, Vector2.zero, Vector2.one);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = new Color(0.90f, 0.92f, 0.90f, 1f);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static void AddOutline(RectTransform rt, Color c, float px)
    {
        AddLineRT(rt, "OT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, px), c);
        AddLineRT(rt, "OB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, px), c);
        AddLineRT(rt, "OL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(px, 0f), c);
        AddLineRT(rt, "OR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(px, 0f), c);
    }

    private static void AddLineRT(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
    }

    private static void AddCornerBrackets(RectTransform panel, Color c, float len, float thick)
    {
        Vector2[] corners = { Vector2.zero, new Vector2(1, 0), new Vector2(0, 1), Vector2.one };
        foreach (var corner in corners)
        {
            var hRT = MakeRect("CB_H", panel, corner, corner);
            hRT.pivot = corner; hRT.sizeDelta = new Vector2(len, thick); hRT.anchoredPosition = Vector2.zero;
            hRT.gameObject.AddComponent<Image>().color = c;

            var vRT = MakeRect("CB_V", panel, corner, corner);
            vRT.pivot = corner; vRT.sizeDelta = new Vector2(thick, len); vRT.anchoredPosition = Vector2.zero;
            vRT.gameObject.AddComponent<Image>().color = c;
        }
    }
}
