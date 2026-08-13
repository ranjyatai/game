using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 触发器演出用的轻量台词字幕——屏幕血条上方居中显示一句台本句子(名字+台词)，
/// 读对应语音，按时长自动淡出，没有选项、不打断玩法。跟 NPCDialogueWindowController
/// 那种带选项的完整交互窗口是两回事，这个只是"念一句台词"。
///
/// 自带独立Canvas，在 BeforeSceneLoad 阶段自建实例(不依赖 SkyPrisonRuntimeUIDriver 的
/// Start() 顺序)——之前挂在 uiRootCanvas 下、由驱动器 Start() 里创建，结果触发器的
/// "event_initialize"(Start时触发)比驱动器先跑就会拿到 null 的 Instance，动作静默
/// 失败、画面上什么都不显示。改成场景加载前就自举，能确保任何触发器事件触发时
/// Instance 必定已存在。
/// </summary>
public class DialogueSubtitleHUD : MonoBehaviour
{
    public static DialogueSubtitleHUD Instance { get; private set; }

    private const float MinDisplaySeconds = 1.5f;
    private const float SecondsPerChar = 0.08f;
    private const float FadeSpeed = 6f;

    // 自动播放=关时，字幕读完不会自动淡出，等玩家按键——这段是防止触发这句话的
    // 那一下按键/点击被同一帧误判成"要求淡出"，导致字幕一出现就立刻被跳过。
    private const float InputGuardSeconds = 0.3f;

    // 血条模块是 BottomCenter，偏移(0,160)，高130，顶边约在 y=290——字幕放在这上面留出间距。
    private const float SubtitleAnchoredY = 320f;

    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _lineText;
    private TMP_FontAsset _font;
    private bool _fontBound;

    private float _remaining = -1f;
    private float _targetAlpha;
    private float _currentAlpha;

    private bool _waitingForInput;
    private float _inputGuardRemaining;

    // ── 连续播放队列(任务目标专属台词等场景用) ────────────────────────────────
    private DialogueSentenceLibrary _queueLibrary;
    private System.Collections.Generic.List<string> _queue;
    private int _queueIndex = -1;
    private System.Action _queueOnComplete;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("[DialogueSubtitleHUD]") { hideFlags = HideFlags.HideAndDontSave };
        Instance = go.AddComponent<DialogueSubtitleHUD>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        TryBindFont();

        // 加载揭幕/揭幕动画期间暂停计时和淡入淡出——不然字幕会在黑屏还没揭开时就
        // 悄悄播完一轮，等真正揭幕时早就淡出了，玩家什么都看不到。见 SceneLoader
        // 上"任何自己驱动alpha的系统都要在这期间让路"的说明。
        if (SceneLoader.IsAwaitingReveal || SceneLoader.IsRevealing)
            return;

        if (_waitingForInput)
        {
            if (_inputGuardRemaining > 0f)
            {
                _inputGuardRemaining -= Time.unscaledDeltaTime;
            }
            else if (Input.anyKeyDown)
            {
                _waitingForInput = false;
                _targetAlpha = 0f;
            }
        }
        else if (_remaining >= 0f)
        {
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining <= 0f)
            {
                _remaining = -1f;
                _targetAlpha = 0f;
            }
        }

        _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, 1f - Mathf.Exp(-FadeSpeed * Time.unscaledDeltaTime));
        if (_canvasGroup != null) _canvasGroup.alpha = _currentAlpha;

        // 队列播放：这一句(不管是自动播放计时结束、还是玩家按键)已经完全淡出，
        // 队列里还有下一句的话立刻接上——不看是靠计时器还是玩家按键淡出的，两条
        // 路径最终都会走到"_targetAlpha=0"，这里统一收口，不用分别在两处各自
        // 再判断一次"该不该推进队列"。
        if (_queueIndex >= 0 && _targetAlpha <= 0f && _currentAlpha < 0.01f)
        {
            _queueIndex++;
            if (_queueIndex < _queue.Count)
            {
                ShowLine(_queueLibrary, _queue[_queueIndex], persistent: false);
            }
            else
            {
                _queueIndex = -1;
                // 队列自然播完(不是被Hide()中途作废的)才回调——调用方(比如任务
                // 结算弹窗)要靠这个知道"台词真的念完了"，不是台词才刚起了个头
                // 就急着把结算界面糊上去。回调本身可能不再需要，用完立刻清空。
                var callback = _queueOnComplete;
                _queueOnComplete = null;
                callback?.Invoke();
            }
        }
    }

    /// <summary>按顺序连续播完一整串句子——每一句自己的自动播放/等待按键行为
    /// 完全复用ShowLine(persistent:false)那一套(读SaveManager.Settings.dialogueAutoPlay)，
    /// 这里只负责"上一句完全淡出之后自动接上下一句"。给任务目标专属台词这类
    /// "配好几句就要完整念完，不是随机挑一句"的场景用——跟开场白/闲聊池那种
    /// "配多句=随机抽一句"是完全不同的语义，不要混用。onComplete在整个队列自然
    /// 播完(不是中途被Hide()打断)时回调一次——调用方(比如结算弹窗)要靠这个确保
    /// "台词真的念完了才弹下一步"，不会跟还没播完的台词抢屏幕。</summary>
    public void ShowLineSequence(DialogueSentenceLibrary library, System.Collections.Generic.List<string> sentenceIds, System.Action onComplete = null)
    {
        if (library == null || sentenceIds == null || sentenceIds.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        _queueLibrary = library;
        _queue = sentenceIds;
        _queueIndex = 0;
        _queueOnComplete = onComplete;
        ShowLine(library, sentenceIds[0], persistent: false);
    }

    /// <summary>由 TriggerActionExecutor(act_show_dialogue_line) 调用——播一句台本句子。
    /// persistent=true 时不会自动计时淡出、也不会因为玩家按键(比如点选项)淡出——
    /// 调用方(比如 NPCDialogueWindowController，选项列表一直挂着)自己决定什么时候
    /// 该收起，收起时调 Hide()。</summary>
    public void ShowLine(DialogueSentenceLibrary library, string sentenceId, bool persistent = false)
    {
        if (library == null || string.IsNullOrWhiteSpace(sentenceId)) return;

        string text = library.GetText(sentenceId, "");
        string speaker = library.GetSpeakerDisplayName(sentenceId, "");
        AudioClip voice = library.GetVoiceClip(sentenceId);

        if (_nameText != null)
        {
            _nameText.text = speaker;
            _nameText.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
        }
        if (_lineText != null)
            _lineText.text = text;

        if (voice != null)
            SkyPrisonSystemSEPlayer.PlayClip(voice, 1f, 1f);

        if (persistent)
        {
            _remaining = -1f;
            _waitingForInput = false;
        }
        else
        {
            bool autoPlay = SaveManager.Settings != null && SaveManager.Settings.dialogueAutoPlay;
            if (autoPlay)
            {
                float duration = voice != null
                    ? Mathf.Max(MinDisplaySeconds, voice.length)
                    : Mathf.Max(MinDisplaySeconds, text.Length * SecondsPerChar);

                _remaining = duration;
                _waitingForInput = false;
            }
            else
            {
                // 关自动播放：读完不会自动消失，等玩家按键才淡出。
                _remaining = -1f;
                _waitingForInput = true;
                _inputGuardRemaining = InputGuardSeconds;
            }
        }

        _targetAlpha = 1f;
    }

    /// <summary>调用方明确表示这句字幕该收起了(比如对话窗口关闭)——立即开始淡出。</summary>
    public void Hide()
    {
        _remaining = -1f;
        _waitingForInput = false;
        _targetAlpha = 0f;
        // 调用方主动要求收起(比如对话窗口关闭)——正在播的队列直接作废，不能让
        // Update()看到"淡出到0了"又误以为是队列里这一句正常播完，自己接着往下播。
        // 是被打断、不是正常播完，onComplete不调用，直接扔掉。
        _queueIndex = -1;
        _queueOnComplete = null;
    }

    // ── 构建独立Canvas + UI(纯代码搭，不走prefab)───────────────────────────────

    private void BuildUI()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 510; // 主HUD Canvas是500，字幕要盖在HUD之上

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        // 加载揭幕前这个Canvas会被 SceneLoader 强制盖住(黑屏在上面)——不注册的话，
        // 揭幕安全网会晚一帧才接管，字幕如果这期间已经播完一轮淡入淡出，
        // 玩家永远不会看到(黑屏后面已经淡出完了)。
        SceneLoader.RegisterGameCanvasForReveal(_canvasGroup);

        var root = new GameObject("SubtitleRoot", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.SetParent(transform, false);
        rootRt.anchorMin = new Vector2(0.5f, 0f);
        rootRt.anchorMax = new Vector2(0.5f, 0f);
        rootRt.pivot = new Vector2(0.5f, 0f);
        rootRt.anchoredPosition = new Vector2(0f, SubtitleAnchoredY);
        rootRt.sizeDelta = new Vector2(1300f, 70f);

        // 名字固定宽度靠左，右对齐贴着台词；台词占剩余宽度靠左，两者中间留一段间距——
        // 同一行左右排布，不再是名字在上、台词在下两行。
        const float NameColumnWidth = 260f;
        const float Gap = 24f;

        var nameGo = new GameObject("NameText", typeof(RectTransform));
        var nameRt = (RectTransform)nameGo.transform;
        nameRt.SetParent(rootRt, false);
        nameRt.anchorMin = new Vector2(0f, 0f);
        nameRt.anchorMax = new Vector2(0f, 1f);
        nameRt.pivot = new Vector2(0f, 0.5f);
        nameRt.anchoredPosition = Vector2.zero;
        nameRt.sizeDelta = new Vector2(NameColumnWidth, 0f);
        _nameText = nameGo.AddComponent<TextMeshProUGUI>();
        _nameText.alignment = TextAlignmentOptions.MidlineRight;
        _nameText.fontSize = 34f;
        _nameText.color = SkyPrisonUIPalette.ColdGreen;
        _nameText.raycastTarget = false;

        var lineGo = new GameObject("LineText", typeof(RectTransform));
        var lineRt = (RectTransform)lineGo.transform;
        lineRt.SetParent(rootRt, false);
        lineRt.anchorMin = new Vector2(0f, 0f);
        lineRt.anchorMax = new Vector2(1f, 1f);
        lineRt.pivot = new Vector2(0.5f, 0.5f);
        lineRt.offsetMin = new Vector2(NameColumnWidth + Gap, 0f);
        lineRt.offsetMax = Vector2.zero;
        _lineText = lineGo.AddComponent<TextMeshProUGUI>();
        _lineText.alignment = TextAlignmentOptions.MidlineLeft;
        _lineText.fontSize = 40f;
        _lineText.color = SkyPrisonUIPalette.White;
        _lineText.raycastTarget = false;
        _lineText.enableWordWrapping = true;
    }

    // ── 字体延迟绑定(仿 ItemPickupToastUI)──────────────────────────────────────

    private void TryBindFont()
    {
        if (_fontBound) return;
        var style = FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
        if (style == null) return;

        _font = style.defaultTextFont;
        if (_font == null) _font = LoadFont("ZhouFangRiMingTi-2 SDF");

        if (_font != null)
        {
            if (_nameText != null) _nameText.font = _font;
            if (_lineText != null) _lineText.font = _font;
            _fontBound = true;
        }
    }

    private static TMP_FontAsset LoadFont(string assetName)
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
}
