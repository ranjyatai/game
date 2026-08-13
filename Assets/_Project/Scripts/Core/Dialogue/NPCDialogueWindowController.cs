using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 通用NPC对话窗口——同一个prefab给所有NPC共用，Begin(NPCDialogueDefinition)喂
/// 具体是哪个NPC、说什么。上半显示NPC名字+当前句子，下半是选项按钮列表(数量不
/// 固定，运行时现搭，不走row prefab那一套)。
///
/// 这一版是"框架"阶段——先把数据流打通(对话包->窗口->选项动作)，视觉样式先简单
/// 搭一版，后面工具/样式定下来再回来精修，不是最终定稿UI。
/// </summary>
public class NPCDialogueWindowController : SkyPrisonBaseWindowController
{
    [Header("内容")]
    [SerializeField] private Text headerText; // 面板顶部NPC名字，冷绿
    [SerializeField] private Image headerBackground; // 名字背景：半透明黑，左侧干净、向右暗淡淡出
    [SerializeField] private Text sentenceText; // 仅用于任务列表浏览态文字，对话台词改走 DialogueSubtitleHUD
    [SerializeField] private RectTransform optionsContainer;
    [SerializeField] private RectTransform selectionMarker; // 跟着当前聚焦选项上下滑动的小方块

    // 自动播放：没配语音时按字数估算停留时长，最短停留时间兜底(避免超短句一闪而过)。
    private const float AutoAdvanceMinSeconds = 1.2f;
    private const float AutoAdvanceSecondsPerChar = 0.06f;

    // 选项聚焦视觉：鼠标悬停优先，没悬停时默认聚焦第一项。
    private const float FocusLerpSpeed = 14f;
    private const float OptionRowHeight = 60f;
    private static readonly Vector3 FocusedOptionScale = new Vector3(1.12f, 1.12f, 1f);
    private int _hoveredOptionIndex = -1;

    private NPCDialogueDefinition _dialogue;
    private NPCDialogueDefinition.DialogueVariant _variant;
    private string _currentGreetingSentenceId; // 本次对话随机选中的那一句，进对话时定一次，不会中途重新随机
    private UnitDefinition _npcUnitDefinition;
    private bool _inQuestListMode;

    // 多层选项分支——点了带子选项的选项，就换成显示它的subOptions而不是回到这一层
    // 原来的选项；"返回"弹栈回上一层。_currentOptions永远指向"现在应该显示的那一
    // 套选项"，栈里存的是"往回退一层应该显示哪一套"。
    private List<DialogueOption> _currentOptions;
    private readonly List<List<DialogueOption>> _optionsStack = new List<List<DialogueOption>>();

    private readonly List<GameObject> _optionButtons = new List<GameObject>();
    private SkyPrisonListGamepadNav _gamepadNav;

    private string _lastShownText = "";
    private float _idleTimer; // 停留计时——UpdateIdleChatter() 用，任何操作都会把它清零
    private AudioClip _lastShownVoice;
    private float _autoAdvanceRemaining = -1f;
    private DialogueOption _autoAdvanceOption;

    protected override string WindowId => "npc_dialogue";

    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        // 之前这两条label硬编码中文，切日文/英文底部按键提示条还是中文——改成走
        // 字典表。"关闭"复用背包等窗口已有的ui_hint_close(已经有完整中/英/日译文)，
        // "选择选项"是这个窗口独有的新key。
        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a",
                fallbackText = L("ui_hint_select", "选择"), label = L("ui_hint_select_option", "选择选项") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    protected override void OnWindowOpen()
    {
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();
        // 手柄导航/确认逻辑照常用它——但它自带的四边高亮框关掉，这个窗口的聚焦视觉
        // 用 UpdateOptionFocusVisuals() 那套(文字变绿放大+■方块跟随)，两套高亮同时
        // 出现只会显得乱。
        _gamepadNav.DrawHighlight = false;

        // 黑白+高斯模糊试过了，观感不明显、不值当这份开销——改回最简单的半透明黑，
        // 左侧干净、向右暗淡淡出的渐变还留着(单边alpha渐变Sprite，不用逐帧代码)。
        if (headerBackground != null && headerBackground.sprite == null)
        {
            headerBackground.sprite = BuildHeaderFadeSprite();
            headerBackground.type = Image.Type.Simple;
            headerBackground.color = new Color(0f, 0f, 0f, 0.2f); // 半透明黑，最高20%不透明度
        }
    }

    // ── 名字底色：左侧干净利落、向右暗淡淡出的单边渐变 ────────────────────────
    private static Sprite _headerFadeSprite;
    private static Sprite BuildHeaderFadeSprite()
    {
        if (_headerFadeSprite != null) return _headerFadeSprite;

        const int w = 256, h = 8;
        const float solidFrac = 0.4f; // 左边这一段保持完全不透明，过了这段才开始淡出
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags  = HideFlags.HideAndDontSave
        };
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)(w - 1);
            float a = u <= solidFrac ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (u - solidFrac) / (1f - solidFrac));
            var c = new Color(1f, 1f, 1f, a);
            for (int y = 0; y < h; y++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        _headerFadeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 0.5f), 100f);
        _headerFadeSprite.hideFlags = HideFlags.HideAndDontSave;
        return _headerFadeSprite;
    }

    protected override void OnWindowClose()
    {
        DialogueSubtitleHUD.Instance?.Hide();

        ClearOptions();
        _dialogue = null;
        _variant = null;
        _currentGreetingSentenceId = null;
        _npcUnitDefinition = null;
        _inQuestListMode = false;
        _currentOptions = null;
        _optionsStack.Clear();
    }

    /// <summary>由 NPCDialogueInteractable.Interact() 在打开窗口后立即调用，喂具体对话数据。
    /// npcUnitDefinition 用于"显示任务列表"选项按归属NPC筛选任务，普通对话可以不传。</summary>
    public void Begin(NPCDialogueDefinition dialogue, UnitDefinition npcUnitDefinition = null)
    {
        _dialogue = dialogue;
        _npcUnitDefinition = npcUnitDefinition;
        if (_dialogue == null) return;

        if (headerText != null)
            headerText.text = ResolveNpcName();

        _variant = _dialogue.GetActiveVariant();
        if (_variant == null)
        {
            Debug.LogWarning($"[NPCDialogueWindowController] {_dialogue.name} 没有任何条件成立的对话变体（也没有无条件的默认兜底）。");
            Close();
            return;
        }

        // 配了多句开场白就随机选一句——只在开始对话这一刻选一次，之后回到开场白
        // (比如退出任务列表)都用这句，不会每次都重新随机。
        _currentGreetingSentenceId = _variant.PickGreetingSentenceId();

        _currentOptions = _variant.options;
        _optionsStack.Clear();

        ShowSentence(_currentGreetingSentenceId);
        RefreshOptions();
    }

    /// <summary>面板顶部标题——固定用这个NPC自己的名字，不跟着"当前是谁在说话"变，
    /// 跟 DialogueSubtitleHUD 里逐句变化的说话人不是一回事。</summary>
    private string ResolveNpcName()
    {
        if (_dialogue != null)
        {
            string name = _dialogue.GetLocalizedName("");
            if (!string.IsNullOrEmpty(name)) return name;
        }

        if (_npcUnitDefinition != null)
        {
            string name = _npcUnitDefinition.GetLocalizedDisplayName();
            if (!string.IsNullOrEmpty(name)) return name;
        }

        return "";
    }

    private void ShowSentence(string sentenceId)
    {
        _idleTimer = 0f;
        string text = _dialogue.GetSentenceText(sentenceId, "");
        _lastShownText = text;

        AudioClip voice = _dialogue.sentenceLibrary != null ? _dialogue.sentenceLibrary.GetVoiceClip(sentenceId) : null;
        _lastShownVoice = voice;

        // 台词用触发器演出那套"血条上方字幕"(DialogueSubtitleHUD)显示，跟剧情演出
        // 是同一套形式，不再用窗口自己那份占位Text——persistent=true 是因为下面这层
        // 一直挂着选项列表，字幕不能自己按计时器/按键淡出，得等玩家选完/关窗口。
        if (DialogueSubtitleHUD.Instance != null && _dialogue.sentenceLibrary != null)
            DialogueSubtitleHUD.Instance.ShowLine(_dialogue.sentenceLibrary, sentenceId, persistent: true);
    }

    private void Update()
    {
        if (_autoAdvanceRemaining >= 0f)
        {
            _autoAdvanceRemaining -= Time.deltaTime;
            if (_autoAdvanceRemaining <= 0f)
            {
                DialogueOption option = _autoAdvanceOption;
                _autoAdvanceRemaining = -1f;
                _autoAdvanceOption = null;

                if (option != null)
                    OnOptionClicked(option);
            }
        }

        UpdateOptionFocusVisuals();
        UpdatePopupButtonFocusVisuals();
        UpdateIdleChatter();
        UpdateAcceptConfirmDirectiveBlink();
    }

    /// <summary>接受任务/结算/交付这三个二次确认弹窗的按钮，手柄/键盘导航时按A D
    /// 移动光标只有"咔"一声切换音效、看不到任何视觉——找到的根因：这三个弹窗的
    /// 按钮跟对话选项列表共用同一个_gamepadNav，但_gamepadNav自带的四边高亮框
    /// 在这个控制器里被整体关掉了(DrawHighlight=false，见Awake附近的设置)，理由
    /// 是"对话选项列表自己有一套更贴合的高亮视觉"(UpdateOptionFocusVisuals，文字
    /// 变冷绿+放大)——但那套自定义视觉只覆盖了_optionButtons，从来没管过这三个
    /// 弹窗的按钮，光标真的在这两个按钮间移动(音效为证)，只是完全没有画面反馈。
    /// 补一个同样"聚焦变冷绿"的最小实现，不需要新建高亮框，复用弹窗按钮自己已有
    /// 的文字组件即可。只在真的有弹窗打开时生效，没开弹窗时_gamepadNav目标是
    /// _optionButtons，交给UpdateOptionFocusVisuals处理，这里直接跳过。</summary>
    private void UpdatePopupButtonFocusVisuals()
    {
        if (_gamepadNav == null) return;

        List<Button> targets = null;
        if (_acceptConfirmRoot != null && _acceptConfirmRoot.activeSelf)
            targets = new List<Button> { _acceptConfirmStartButton, _acceptConfirmCancelButton };
        else if (_completionConfirmRoot != null && _completionConfirmRoot.activeSelf)
            targets = new List<Button> { _completionConfirmConfirmButton, _completionConfirmLaterButton };
        else if (_deliveryConfirmRoot != null && _deliveryConfirmRoot.activeSelf)
            targets = new List<Button> { _deliveryConfirmConfirmButton, _deliveryConfirmCancelButton };

        if (targets == null) return;

        Button focused = _gamepadNav.CurrentFocusedButton;
        foreach (Button btn in targets)
        {
            if (btn == null) continue;
            Text label = btn.GetComponentInChildren<Text>();
            if (label == null) continue;
            label.color = (btn == focused) ? SkyPrisonUIPalette.ColdGreen : Color.white;
        }
    }

    /// <summary>左上角"NEW DIRECTIVE"标签缓慢闪烁透明度——纯装饰，营造战术终端还在
    /// 持续读出/等待确认的感觉，只在确认弹窗真正开着的时候跑，不用协程(弹窗开关
    /// 频繁，协程启停比在Update里挂一个条件判断更麻烦)。</summary>
    private void UpdateAcceptConfirmDirectiveBlink()
    {
        if (_acceptConfirmDirectiveText == null) return;
        if (_acceptConfirmRoot == null || !_acceptConfirmRoot.activeSelf) return;

        float alpha = Mathf.Lerp(0.25f, 0.75f, (Mathf.Sin(Time.unscaledTime * 2.2f) + 1f) * 0.5f);
        Color c = _acceptConfirmDirectiveText.color;
        _acceptConfirmDirectiveText.color = new Color(c.r, c.g, c.b, alpha);
    }

    /// <summary>停在选项界面太久没操作，让NPC随机念一句闲聊台词——纯调味，不影响
    /// 对话流程/选项。计时器在任何"选了选项/悬停切换/显示新台词"的地方重置，只有
    /// 真的什么都不做才会触发；隐藏期间(挂着子窗口，比如商店)GameObject 本身是
    /// SetActive(false)，Update 根本不会跑，天然就是暂停计时，不用额外处理。
    /// 任务列表浏览态(_inQuestListMode)也要跳过——用户明确要求进了"有事情找你"
    /// 这类子菜单之后，随便念的闲聊台词不该突然插进来打断，跟"看着任务名字挑
    /// 一条"这个操作不搭。</summary>
    private void UpdateIdleChatter()
    {
        if (_inQuestListMode) return;
        if (_dialogue == null || _dialogue.sentenceLibrary == null) return;
        if (_dialogue.idleChatterSentenceIds == null || _dialogue.idleChatterSentenceIds.Count == 0) return;

        _idleTimer += Time.unscaledDeltaTime;
        if (_idleTimer < _dialogue.idleChatterDelaySeconds) return;

        _idleTimer = 0f;
        string idleId = _dialogue.PickIdleChatterSentenceId();
        if (string.IsNullOrEmpty(idleId)) return;

        DialogueSubtitleHUD.Instance?.ShowLine(_dialogue.sentenceLibrary, idleId, persistent: true);
    }

    /// <summary>鼠标悬停哪项就让哪项变冷绿放大，没悬停时默认聚焦第一项("默认选项在
    /// 第一个位置")；■标记跟着聚焦项的位置上下滑动。</summary>
    private void UpdateOptionFocusVisuals()
    {
        if (_optionButtons.Count == 0)
        {
            if (selectionMarker != null) selectionMarker.gameObject.SetActive(false);
            return;
        }

        // 优先鼠标悬停；没悬停就看手柄导航当前选中的是哪个按钮(手柄自己的高亮框
        // 已经关掉，不然这套视觉在用手柄玩的时候完全不会动，只会一直停在默认第一项)；
        // 两者都没有就默认聚焦第一项。
        int focusIndex = _hoveredOptionIndex;
        if (focusIndex < 0 && _gamepadNav != null)
        {
            Button gamepadFocused = _gamepadNav.CurrentFocusedButton;
            if (gamepadFocused != null)
                focusIndex = _optionButtons.FindIndex(go => go != null && go.GetComponent<Button>() == gamepadFocused);
        }
        if (focusIndex < 0 || focusIndex >= _optionButtons.Count) focusIndex = 0;

        float lerpT = 1f - Mathf.Exp(-FocusLerpSpeed * Time.unscaledDeltaTime);

        for (int i = 0; i < _optionButtons.Count; i++)
        {
            GameObject go = _optionButtons[i];
            if (go == null) continue;
            Text label = go.GetComponentInChildren<Text>();
            if (label == null) continue;

            bool focused = i == focusIndex;
            label.color = focused ? SkyPrisonUIPalette.ColdGreen : SkyPrisonUIPalette.White;

            Vector3 targetScale = focused ? FocusedOptionScale : Vector3.one;
            label.transform.localScale = Vector3.Lerp(label.transform.localScale, targetScale, lerpT);
        }

        GameObject focusedGo = _optionButtons[focusIndex];
        if (selectionMarker != null && focusedGo != null)
        {
            selectionMarker.gameObject.SetActive(true);
            // 方块和行都是顶对齐(pivot.y=1)，要让方块的中心跟文字所在这一行的
            // 中心对齐，得把行顶再往下让半个"行高-方块高"的差值。
            float rowTop = ((RectTransform)focusedGo.transform).anchoredPosition.y;
            float targetY = rowTop - (OptionRowHeight - selectionMarker.rect.height) * 0.5f;
            Vector2 pos = selectionMarker.anchoredPosition;
            pos.y = Mathf.Lerp(pos.y, targetY, lerpT);
            selectionMarker.anchoredPosition = pos;
        }
    }

    private void SetHoveredOption(int index)
    {
        if (_hoveredOptionIndex != index)
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); // 光标切到别的选项也要有反馈，之前只有点击才有声音
        _hoveredOptionIndex = index;
        _idleTimer = 0f;
    }

    private void ClearHoveredOption(int index)
    {
        if (_hoveredOptionIndex == index) _hoveredOptionIndex = -1;
    }

    /// <summary>挂在每个选项按钮上，把鼠标悬停事件转发回窗口控制器——用索引而不是
    /// 直接持有按钮引用，是因为按钮会随选项列表刷新被销毁重建。</summary>
    private sealed class OptionHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public NPCDialogueWindowController owner;
        public int index;

        public void OnPointerEnter(PointerEventData eventData) => owner?.SetHoveredOption(index);
        public void OnPointerExit(PointerEventData eventData) => owner?.ClearHoveredOption(index);
    }

    /// <summary>只有当前没有真正的分支(选项数量恰好1个)才自动推进——出现2个以上选项
    /// (比如"对话/购物/取消")无论如何都停下来等玩家手动选，不替玩家决定剧情走向。</summary>
    private void ScheduleAutoAdvanceIfEligible()
    {
        _autoAdvanceRemaining = -1f;
        _autoAdvanceOption = null;

        bool autoPlay = SaveManager.Settings != null && SaveManager.Settings.dialogueAutoPlay;
        if (!autoPlay) return;
        if (_inQuestListMode) return; // 任务列表是浏览态，不自动推进
        if (_currentOptions == null) return;
        if (_optionsStack.Count > 0) return; // 有"返回"按钮=有真分支，不自动推进

        List<DialogueOption> visible = GetVisibleOptions(_currentOptions);
        if (visible.Count != 1) return;

        _autoAdvanceOption = visible[0];
        _autoAdvanceRemaining = ComputeAutoAdvanceDelay();
    }

    private float ComputeAutoAdvanceDelay()
    {
        if (_lastShownVoice != null)
            return Mathf.Max(AutoAdvanceMinSeconds, _lastShownVoice.length);

        int charCount = string.IsNullOrEmpty(_lastShownText) ? 0 : _lastShownText.Length;
        return Mathf.Max(AutoAdvanceMinSeconds, charCount * AutoAdvanceSecondsPerChar);
    }

    private void RefreshOptions()
    {
        ClearOptions();
        if (optionsContainer == null || _currentOptions == null) return;

        List<DialogueOption> visibleOptions = GetVisibleOptions(_currentOptions);

        foreach (var option in visibleOptions)
        {
            var go = BuildOptionButton(optionsContainer, ResolveOptionLabel(option), _optionButtons.Count);
            _optionButtons.Add(go);

            var captured = option;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => OnOptionClicked(captured));
        }

        // 已经下钻到子选项层了——自动补一个"返回"按钮回上一层，不用在子选项里手动加。
        if (_optionsStack.Count > 0)
        {
            var backGo = BuildOptionButton(optionsContainer, L("npc_dialogue_back", "返回"), _optionButtons.Count);
            _optionButtons.Add(backGo);
            backGo.GetComponent<Button>().onClick.AddListener(GoBackOptionsLevel);
        }

        RefreshGamepadTargets();
        ScheduleAutoAdvanceIfEligible();
    }

    /// <summary>点了带子选项的选项之后，把当前这层压栈、切到它的subOptions；
    /// 没有子选项则维持原样(行为跟以前一样，选项列表不变)。</summary>
    private void EnterOptionsFor(DialogueOption clickedOption)
    {
        if (clickedOption.subOptions != null && clickedOption.subOptions.Count > 0)
        {
            _optionsStack.Add(_currentOptions);
            _currentOptions = clickedOption.subOptions;
        }

        RefreshOptions();
    }

    private void GoBackOptionsLevel()
    {
        if (_optionsStack.Count == 0) return;

        _idleTimer = 0f;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        _currentOptions = _optionsStack[_optionsStack.Count - 1];
        _optionsStack.RemoveAt(_optionsStack.Count - 1);
        RefreshOptions();
    }

    /// <summary>按 visibilityConditions 过滤——全部成立才显示，留空(无条件)永远显示。
    /// "显示任务列表"这个动作类型额外多一条隐式条件：这个NPC名下当前没有进行中的
    /// 任务就不显示这个选项，不用policy层面配visibilityConditions，用户明确要求
    /// "没任务就别让这个选项出现"。</summary>
    private List<DialogueOption> GetVisibleOptions(List<DialogueOption> options)
    {
        var result = new List<DialogueOption>();
        foreach (var option in options)
        {
            if (option == null) continue;

            bool visible = true;
            if (option.visibilityConditions != null)
            {
                foreach (var condition in option.visibilityConditions)
                {
                    if (!TriggerConditionEvaluator.Evaluate(condition))
                    {
                        visible = false;
                        break;
                    }
                }
            }

            if (visible && option.actionType == DialogueOptionActionType.ShowQuestList)
            {
                // 这个NPC名下"进行中"的任务(她是giverNpc) + "轮到来找她交付"的任务
                // (她不一定是giverNpc，比如任务是A给的、要求交给B)——两种都算这个
                // NPC身上"有事可做"，缺一种都不该把这个选项藏起来。
                int activeQuestCount = QuestRuntime.Instance != null
                    ? QuestRuntime.Instance.GetActiveQuestsForNpc(_npcUnitDefinition).Count
                        + QuestRuntime.Instance.GetActiveQuestsWithCurrentDeliveryTarget(_npcUnitDefinition).Count
                    : 0;
                if (activeQuestCount == 0)
                    visible = false;
            }

            // "接受任务"选项自动隐藏：留空 questToAccept(常见的"入口"用法，比如"有事情
            // 找你...")时，这个NPC名下只要还有至少一条可接的任务就显示，点开后由
            // ShowAcceptQuestListView 列出全部可选任务的真实名字，不需要策划手动建
            // 子选项。显式指定了 questToAccept(一个NPC同时挂好几个独立入口的场景)则
            // 只按那一条任务自己是否可接来判断。
            //
            // 这个选项同时也是任务接了之后"找发任务人确认进度/交付"的唯一入口——用户
            // 明确要求不额外建一个"显示任务列表"选项，就用这同一个"有事情找你"入口，
            // 只要这个NPC名下还有一条进行中的任务(不管是不是已经能交付)就继续显示，
            // 不能只看"能不能接"，接了之后这个选项反而不该消失。点击后走哪条分支
            // 由OnOptionClicked按"是可接还是已经在进行"再分流。
            if (visible && option.actionType == DialogueOptionActionType.AcceptQuest)
            {
                bool acceptable;
                bool activeInProgress;
                if (option.questToAccept != null)
                {
                    acceptable = QuestRuntime.Instance != null && QuestRuntime.Instance.IsQuestAcceptable(option.questToAccept);
                    activeInProgress = QuestRuntime.Instance != null && QuestRuntime.Instance.IsActive(option.questToAccept.questId);
                }
                else
                {
                    acceptable = QuestRuntime.Instance != null
                        && QuestRuntime.Instance.GetAcceptableQuestsForNpc(_npcUnitDefinition).Count > 0;
                    activeInProgress = QuestRuntime.Instance != null
                        && QuestRuntime.Instance.GetActiveQuestsForNpc(_npcUnitDefinition).Count > 0;
                }
                if (!acceptable && !activeInProgress)
                    visible = false;
            }

            if (visible) result.Add(option);
        }

        return result;
    }

    private string ResolveOptionLabel(DialogueOption option)
    {
        // "接受任务"选项永远是入口——不管有没有显式指定 questToAccept，按钮文字都
        // 保留策划自己写的静态文案(比如"有事情要谈...")，真实任务名字一律留给点开后
        // ShowAcceptQuestListView 里的每一条子按钮显示，不允许任务名字裸露在外层。
        if (LocalizationRuntime.Instance != null)
            return LocalizationRuntime.Instance.GetText(option.label, "…");
        return "…";
    }

    private void OnOptionClicked(DialogueOption option)
    {
        _idleTimer = 0f;
        // 点哪个选项都统一在这里播一次点选音效，不用每个 case 各自重复调一遍——
        // 之前 OpenWindow 这个分支就漏了，点"进行交易"没有声音，别的选项都有。
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        switch (option.actionType)
        {
            case DialogueOptionActionType.ShowSentence:
                ShowSentence(option.PickTargetSentenceId());
                if (option.closeAfterShow)
                    Close();
                else
                    EnterOptionsFor(option); // 有子选项就下钻，没有就照旧刷新当前这层
                break;

            case DialogueOptionActionType.OpenWindow:
                if (option.targetWindowPrefab != null)
                {
                    var manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
                    GameObject opened = manager?.Open(option.targetWindowPrefab);

                    // 指定了具体数据(比如哪个ShopDefinition)就喂给窗口——同样是"打开
                    // 窗口"，供给站/古董店/工坊商店不是同一个东西，不能靠窗口prefab
                    // 自己写死一份就完事。
                    if (opened != null && option.targetWindowPayload != null)
                        opened.GetComponent<IDialogueWindowPayloadReceiver>()?.ReceiveDialoguePayload(option.targetWindowPayload);

                    // 勾了"关闭子窗口后弹回对话"——之前只是不关自己，但这个窗口没有
                    // 任何遮挡/层级机制，子窗口开着的时候对话框会跟子窗口一起全部显示，
                    // 变成两个窗口叠在一起。改成真正隐藏自己，等子窗口关闭了再显示回来。
                    // 用 manager 的协程而不是自己的——SetActive(false) 会连自己身上正在跑的
                    // 协程一起冻结，永远等不到重新显示。
                    if (option.returnToDialogueAfterWindowClose && opened != null && manager != null)
                    {
                        var subMeta = opened.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
                        string subUiId = subMeta != null && !string.IsNullOrWhiteSpace(subMeta.uiId) ? subMeta.uiId : opened.name;
                        DialogueSubtitleHUD.Instance?.Hide();
                        gameObject.SetActive(false);
                        manager.StartCoroutine(WaitForSubWindowCloseThenShow(manager, subUiId));
                        return;
                    }
                }
                // 没勾"关闭子窗口后弹回对话"就直接关自己。
                if (!option.returnToDialogueAfterWindowClose)
                    Close();
                break;

            case DialogueOptionActionType.SetQuestFlag:
                if (!string.IsNullOrWhiteSpace(option.questFlag))
                    SaveManager.Player?.SetQuestFlag(option.questFlag);
                ShowSentence(option.PickTargetSentenceId());
                EnterOptionsFor(option);
                break;

            case DialogueOptionActionType.AcceptQuest:
            {
                // 这个入口现在是双向的：这个NPC名下要是已经有进行中的任务，优先切到
                // "找发任务人确认进度/交付"(复用ShowQuestListView+OnQuestSelected，
                // 见那边CheckInWithGiverForQuest)，而不是永远只走接取流程——不然任务
                // 一接完这个选项除了消失就没别的用处了。没有进行中任务时才是纯粹的
                // "接取"入口：永远先进二级列表，不允许任务名字/接取动作直接暴露在
                // 外层——留空questToAccept时列出这个NPC名下全部可接任务；显式指定了
                // 的话列表里就只有那一条，同样要点进去选一次才真正接取。
                List<QuestDefinition> activeForNpc = QuestRuntime.Instance != null
                    ? QuestRuntime.Instance.GetActiveQuestsForNpc(_npcUnitDefinition)
                    : new List<QuestDefinition>();
                bool targetIsActive = option.questToAccept != null
                    ? activeForNpc.Contains(option.questToAccept)
                    : activeForNpc.Count > 0;

                if (targetIsActive)
                    ShowQuestListView();
                else
                    ShowAcceptQuestListView(option.questToAccept);
                break;
            }

            case DialogueOptionActionType.ShowQuestList:
                ShowQuestListView();
                break;

            case DialogueOptionActionType.Close:
            default:
                Close();
                break;
        }
    }

    // ── 任务列表浏览态 ──────────────────────────────────────────────────────
    // "显示任务列表"选项进来的是一个独立的浏览子状态：按钮换成这个NPC名下进行中的
    // 任务，选一个看简介，"返回"回到正常的对话选项。任务具体怎么推进(触发/交付
    // 等)由任务系统/地图触发器自己的条件判定负责，这里只负责"翻开来看"。

    private void ShowQuestListView()
    {
        _inQuestListMode = true;

        // 进这个浏览态之前如果还有一句开场白/闲聊字幕停在屏幕上(它是persistent=true，
        // 不会自己计时淡出)，这里没有新的字幕内容顶替它(下面用的是sentenceText那个
        // 旧占位文字，不是DialogueSubtitleHUD)，不主动收起的话它会一直糊在屏幕上，
        // 跟"进任务列表看看"这个操作本身没关系，纯粹是没人告诉它该消失了。
        DialogueSubtitleHUD.Instance?.Hide();

        // 这个NPC给的进行中任务 + 轮到找她交付的任务，两份列表合并去重——一条任务
        // 理论上不会同时命中两边(giverNpc跟当前交付对象是同一个人的话，两份筛选
        // 条件都会命中它，Distinct()避免在列表里重复出现两次)。
        List<QuestDefinition> quests = new List<QuestDefinition>();
        if (QuestRuntime.Instance != null)
        {
            quests.AddRange(QuestRuntime.Instance.GetActiveQuestsForNpc(_npcUnitDefinition));
            foreach (var q in QuestRuntime.Instance.GetActiveQuestsWithCurrentDeliveryTarget(_npcUnitDefinition))
                if (!quests.Contains(q)) quests.Add(q);
        }

        if (sentenceText != null)
        {
            sentenceText.gameObject.SetActive(true);
            sentenceText.text = quests.Count > 0 ? "" : L("npc_dialogue_no_active_quests", "目前没有可处理的任务。");
        }

        ClearOptions();
        if (optionsContainer == null) return;

        foreach (var quest in quests)
        {
            var captured = quest;
            var go = BuildOptionButton(optionsContainer, captured.GetLocalizedTitle(captured.name), _optionButtons.Count);
            AttachQuestMarkerIconToOption(go, QuestMarkerResolver.ResolveForQuest(captured));
            _optionButtons.Add(go);
            go.GetComponent<Button>().onClick.AddListener(() => OnQuestSelected(captured));
        }

        var backGo = BuildOptionButton(optionsContainer, L("npc_dialogue_back", "返回"), _optionButtons.Count);
        _optionButtons.Add(backGo);
        backGo.GetComponent<Button>().onClick.AddListener(ExitQuestListView);

        RefreshGamepadTargets();
    }

    private void OnQuestSelected(QuestDefinition quest)
    {
        CheckInWithGiverForQuest(quest);
    }

    /// <summary>玩家在"有事情找你"这类任务相关选项里明确选中了一条这个NPC名下进行中
    /// 的任务——用户明确要求这必须是"一板一眼"的显式动作：任务里"与发任务人对话"
    /// 这类目标不能靠"随便进一次对话窗口"(比如只是来交易)就被动满足，必须是玩家
    /// 真的点进"有事情找你"、选中这条任务这个动作本身。
    ///
    /// 只推进"当前正卡在这一步"的那一个目标，不是无脑给整条任务加同一个计数——
    /// 踩过一次坑：如果不管当前进度到哪一步都往同一个计数器里加，玩家在目标②
    /// (收集物品)还没做完的时候，为了看看有没有反应多点了几次这个选项，会把后面
    /// "回琪亚拉处交付"那一步的计数器也顺带推过了阈值，变成明明没收集够却已经能
    /// 结算。改成只看GetCurrentObjective()——只有当前目标本身就是这种"签到型"
    /// (cond_counter_at_least)条件时才给它自己独立的那个counterId加1，其它类型的
    /// 当前目标(比如收集)点这个选项不会有任何副作用，也不会误伤后面还没轮到的目标。
    /// 每次选中都重新判一次完成条件，达成就直接弹结算界面，没达成就照旧显示任务
    /// 描述当反馈。</summary>
    private void CheckInWithGiverForQuest(QuestDefinition quest)
    {
        if (quest == null) return;
        _idleTimer = 0f;

        QuestObjective current = quest.GetCurrentObjective();

        // 交付类目标走专属的交付窗口(选物品数量对不对+确认后真的从背包扣掉)，不是
        // 简单打卡计数器——只有当前目标正好要求"交给这个NPC"才触发，这个NPC名下
        // 别的任务/别的目标类型选中只是单纯查看进度，不会弹错窗口。
        if (IsCurrentObjectiveDeliveryToThisNpc(current))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            ShowDeliveryConfirmPopup(quest, current);
            return;
        }

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        // 一个节点可能要求签到好几次(比如"分别向A和B汇报")，每条计数器各加1。
        foreach (string counterKey in GetGiverCheckinCounterKeys(current))
            SaveManager.Player?.IncrementCounter(counterKey, 1);

        // 找到了真正的bug根源：这份进行中任务列表(ShowQuestListView搭出来的选项)
        // 在台词/演出播放期间一直没清掉，玩家能在台词还没播完的时候又点一次同一个
        // 任务按钮——计数器在上面已经同步+1了，第二次点击会让GetCurrentObjective()
        // 直接跳到下一个目标(如果它也已经满足，比如"收集"是背包里本来就有)，表现成
        // "台词还没念完，直接跳到下一个节点"。播放期间必须先清掉底下的按钮，等
        // 反馈真的播完了再决定要不要重新搭出来。
        ClearOptions();

        // 这次签到如果正好让任务整个结算完成、又没配真人演出/台词(只是退回兜底的
        // 任务描述当反馈)，就不再单独飘一段字幕在窗口外面——直接把这段兜底文字
        // 嵌进结算确认框自己的Description区域里显示，跟接受任务确认框嵌着描述
        // 文字是同一个道理。用户明确反馈过"提交任务也应该显示在窗口里面"。
        // 真配了演出/台词的情况维持原样：演出/台词照常通过悬浮字幕播完，播完
        // 回调里再弹结算框(不额外嵌入文字，Description区域整块隐藏)。
        bool willComplete = quest.AreRequiredObjectivesComplete(false);
        if (willComplete && !HasRealNpcResponse(current))
        {
            ShowCompletionConfirmPopup(quest, quest.GetLocalizedDescription(quest.name));
            return;
        }

        ShowNpcResponseForObjective(quest, current, () =>
        {
            if (willComplete)
            {
                ShowCompletionConfirmPopup(quest);
            }
            else
            {
                // 没有结算(还有目标没做完)，把任务列表重新搭出来，玩家可以继续选择
                // 别的任务，或者再选一次这条(比如去收集完物品之后回来交付)。
                ShowQuestListView();
            }
        });
    }

    /// <summary>目标反馈优先级：①配了npcResponseTriggerPackage就手动立刻执行一整套
    /// 演出(主线级别，镜头/暂停/连续台词等)，不看下面两级；②没配触发包但配了
    /// npcResponseSentenceIds就按顺序连续念完全部(不是随机抽一句——配几句就完整
    /// 播几句，每句自己的自动播放/等待按键行为走DialogueSubtitleHUD.ShowLineSequence，
    /// 复用SaveManager.Settings.dialogueAutoPlay这个全局设置，不是sentenceText那种
    /// 只在浏览任务列表时用的占位文字)；③都没配就照旧退回用任务描述当反馈——旧
    /// 任务数据(这两个字段都是空的)行为完全不变。onFinished在"这次反馈真的播完了"
    /// 才回调——触发包是同步执行(ExecuteImmediately跑完这个方法自己就播完了)，
    /// 台词队列要等ShowLineSequence自己在全部念完时回调，都没配的兜底文案分支
    /// 也立刻算完成(没有要等的东西)。</summary>
    /// <summary>面板顶部"NPC名字+半透明背景"这块——演出/连续台词播放期间跟着隐藏，
    /// 播完恢复。</summary>
    private void SetHeaderVisible(bool visible)
    {
        if (headerText != null) headerText.gameObject.SetActive(visible);
        if (headerBackground != null) headerBackground.gameObject.SetActive(visible);
    }

    /// <summary>这个目标配没配真人演出/连续台词(而不是退回兜底任务描述)——
    /// CheckInWithGiverForQuest/ConfirmDeliveryPopup在结算(完成任务)的那一刻要用
    /// 同一个判断结果决定"兜底文案该不该嵌进结算确认框里显示"，跟这里
    /// ShowNpcResponseForObjective自己用来决定"该不该隐藏NPC名字牌"的判断必须是
    /// 同一套规则，抽成共用方法，不能各写各的两份容易不同步。</summary>
    private bool HasRealNpcResponse(QuestObjective objective)
    {
        return objective != null && (objective.npcResponseTriggerPackage != null
            || (objective.npcResponseSentenceIds != null && objective.npcResponseSentenceIds.Count > 0 && _dialogue?.sentenceLibrary != null));
    }

    private void ShowNpcResponseForObjective(QuestDefinition quest, QuestObjective objective, System.Action onFinished)
    {
        bool hasRealResponse = HasRealNpcResponse(objective);

        // 演出/连续台词播放期间，右上角"NPC名字+背景"这块UI也该跟着收起——用户
        // 明确要求"演出的时候右侧选项名字也隐藏，后面填充也是"，选项列表已经在
        // CheckInWithGiverForQuest里ClearOptions()清掉了，唯独这块名字牌之前没管。
        // 只在真的有演出/台词要播的时候才隐藏+之后恢复，兜底文案那种"没有要等的
        // 东西"分支不用这一套(本来就没有要遮挡的演出)。
        if (hasRealResponse)
        {
            SetHeaderVisible(false);
            System.Action originalOnFinished = onFinished;
            onFinished = () =>
            {
                SetHeaderVisible(true);
                originalOnFinished?.Invoke();
            };
        }

        if (objective != null && objective.npcResponseTriggerPackage != null)
        {
            // 协程跑——遇到act_show_dialogue_line会真的等这一句播完才继续下一条动作，
            // 不是同步跑完就算数，不然连续好几句台词会挤在同一帧里互相覆盖，表现成
            // "演出被跳过"。
            StartCoroutine(TriggerPackageRuntime.ExecuteImmediatelyCoroutine(objective.npcResponseTriggerPackage, onFinished));
            return;
        }

        if (objective != null && objective.npcResponseSentenceIds != null && objective.npcResponseSentenceIds.Count > 0
            && _dialogue?.sentenceLibrary != null)
        {
            _idleTimer = 0f;
            DialogueSubtitleHUD.Instance?.ShowLineSequence(_dialogue.sentenceLibrary, objective.npcResponseSentenceIds, onFinished);
            if (sentenceText != null) sentenceText.text = "";
            return;
        }

        if (sentenceText != null)
            sentenceText.text = quest.GetLocalizedDescription(quest.name);
        onFinished?.Invoke();
    }

    /// <summary>这个目标节点里全部"要求交付给眼前这个NPC"的条件——一个节点可以同时
    /// 要求交付好几种物品，所以返回列表而不是布尔。节点里混着别的类型的条件
    /// (比如还要求先杀几只怪)不影响，这里只挑交付那几条出来。收件人不是眼前这个
    /// NPC的交付条件会被跳过，所以"同一节点要求分别交给两个不同NPC"也能正确工作：
    /// 在谁面前就只弹给谁的那几件。</summary>
    private List<PendingDelivery> CollectDeliveriesToThisNpc(QuestObjective objective)
    {
        var result = new List<PendingDelivery>();
        if (objective == null || objective.completionConditions == null) return result;

        foreach (LogicSentenceInstance c in objective.completionConditions)
        {
            if (c == null || c.templateId != "cond_has_delivered_item_to_npc") continue;
            UnitDefinition targetNpc = c.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
            if (targetNpc != _npcUnitDefinition) continue;
            ItemDefinition item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;
            if (item == null) continue;
            int count = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
            if (count <= 0) continue;
            result.Add(new PendingDelivery(item, targetNpc, count));
        }
        return result;
    }

    private bool IsCurrentObjectiveDeliveryToThisNpc(QuestObjective objective)
        => CollectDeliveriesToThisNpc(objective).Count > 0;

    /// <summary>这个目标节点里全部"签到型"条件(cond_counter_at_least)各自的counterId——
    /// 一个节点可以要求跟好几个人对话/签到好几次，所以返回列表。每条用自己专属的
    /// counterId，不是任务全局共用同一个key，天然不会跨目标误触发。</summary>
    private static List<string> GetGiverCheckinCounterKeys(QuestObjective objective)
    {
        var keys = new List<string>();
        if (objective == null || objective.completionConditions == null) return keys;

        foreach (LogicSentenceInstance c in objective.completionConditions)
        {
            if (c == null || c.templateId != "cond_counter_at_least") continue;
            string counterId = c.GetAssignment("counterId")?.value?.EvaluateString();
            if (!string.IsNullOrWhiteSpace(counterId))
                keys.Add(counterId);
        }
        return keys;
    }

    private void ExitQuestListView()
    {
        _inQuestListMode = false;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        // 退出任务列表整个回到最顶层(开场白+顶层选项)，不只是回上一层子选项——
        // 任务列表本来就被当成一次"跳出去看看"的独立浏览态，跟正常的多层选项
        // 下钻不是一回事。
        if (sentenceText != null)
            sentenceText.gameObject.SetActive(false);

        if (_variant != null)
        {
            _currentOptions = _variant.options;
            _optionsStack.Clear();
            ShowSentence(_currentGreetingSentenceId);
        }
        RefreshOptions();
    }

    // ── 接受任务浏览态 ──────────────────────────────────────────────────────
    // "接受任务"选项点开后进的独立浏览子状态：按钮换成可接的任务真实名字，选一个
    // 才真正接取，"返回"回到正常的对话选项——不管选项有没有显式指定 questToAccept
    // 都一律走这一层，任务名字/接取动作永远不直接暴露在外层选项里。跟上面的
    // "任务列表浏览态"是同一个形状，只是列的是"可接取"而不是"进行中"。

    /// <param name="explicitQuest">选项显式指定了 questToAccept 时传入——列表里只
    /// 显示这一条(仍然要点进去选一次才接取)。留空则列出这个NPC名下全部可接任务。</param>
    private void ShowAcceptQuestListView(QuestDefinition explicitQuest)
    {
        _inQuestListMode = true;
        _acceptListExplicitQuest = explicitQuest; // 记下来，取消确认弹窗时用同一个参数重搭这份列表

        // 跟ShowQuestListView同一个道理——这个浏览态不会往DialogueSubtitleHUD设新
        // 内容，之前还停在屏幕上的开场白/闲聊字幕(persistent=true，不会自己淡出)
        // 得在这里主动收起。
        DialogueSubtitleHUD.Instance?.Hide();

        List<QuestDefinition> quests;
        if (explicitQuest != null)
        {
            quests = new List<QuestDefinition>();
            if (QuestRuntime.Instance != null && QuestRuntime.Instance.IsQuestAcceptable(explicitQuest))
                quests.Add(explicitQuest);
        }
        else
        {
            quests = QuestRuntime.Instance != null
                ? QuestRuntime.Instance.GetAcceptableQuestsForNpc(_npcUnitDefinition)
                : new List<QuestDefinition>();
        }

        if (sentenceText != null)
        {
            sentenceText.gameObject.SetActive(true);
            sentenceText.text = quests.Count > 0 ? "" : L("npc_dialogue_no_acceptable_quests", "目前没有可以接取的任务。");
        }

        ClearOptions();
        if (optionsContainer == null) return;

        foreach (var quest in quests)
        {
            var captured = quest;
            var go = BuildOptionButton(optionsContainer, captured.GetLocalizedTitle(captured.questId), _optionButtons.Count);
            AttachQuestMarkerIconToOption(go, QuestMarkerResolver.ResolveForQuest(captured));
            _optionButtons.Add(go);
            go.GetComponent<Button>().onClick.AddListener(() => OnAcceptQuestChosen(captured));
        }

        var backGo = BuildOptionButton(optionsContainer, L("npc_dialogue_back", "返回"), _optionButtons.Count);
        _optionButtons.Add(backGo);
        backGo.GetComponent<Button>().onClick.AddListener(ExitQuestListView);

        RefreshGamepadTargets();
    }

    private void OnAcceptQuestChosen(QuestDefinition quest)
    {
        if (quest == null) return;
        _idleTimer = 0f;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        // 选中任务名字还不算真正接取——先弹二次确认(任务名字+详情+HAZARD RANK+
        // 开始/取消)，点"开始任务"才真正调用TryAcceptQuest，"取消"回到还是同一份
        // 可接取列表，不强制跳回正常对话层。
        ShowAcceptConfirmPopup(quest);
    }

    // ── 接受任务二次确认弹窗 ────────────────────────────────────────────────
    // 运行时现搭，风格照抄 ShopWindowController 的稀有物品出售二次确认弹窗(半透明
    // 遮罩+四角白色L形角标+白框按钮)，box自身背景按项目窗口标配换成黑白+高斯模糊
    // (SkyPrisonFloatingWindowKit.BuildBlurBackground)，不额外发明新弹窗样式。
    private GameObject _acceptConfirmRoot;
    private Text _acceptConfirmTitleText;
    private Text _acceptConfirmHazardText;
    private Text _acceptConfirmDescText;
    private RectTransform _acceptConfirmRewardRow;
    private RectTransform _acceptConfirmBox; // Box本体——开关动画缩放的对象，跟其它窗口统一的开关规范一致
    private Coroutine _acceptConfirmAnimCoroutine;
    private Button _acceptConfirmStartButton;
    private Button _acceptConfirmCancelButton;
    private QuestDefinition _pendingAcceptQuest;
    private SkyPrisonBlurUVTracker _acceptConfirmBlurTracker;
    private QuestDefinition _acceptListExplicitQuest; // ShowAcceptQuestListView 最近一次的入参，取消确认弹窗时用它重搭列表
    private Text _acceptConfirmDirectiveText;
    private Text _acceptConfirmTrackCheckmark;
    private bool _acceptConfirmTrackChecked = true;

    // ── 任务完成结算弹窗——跟接受任务确认框同一套结构/规范，触发方式不同：
    // 不是对话选项菜单点出来的，是NPCDialogueInteractable检测到"这次对话刚好
    // 满足了最后一个与giverNpc对话的目标"时直接调ShowCompletionConfirmPopup。
    private GameObject _completionConfirmRoot;
    private RectTransform _completionConfirmBox;
    private Coroutine _completionConfirmAnimCoroutine;
    private SkyPrisonBlurUVTracker _completionConfirmBlurTracker;
    private Text _completionConfirmTitleText;
    private Text _completionConfirmDescText;
    private RectTransform _completionConfirmRewardRow;
    private RectTransform _completionConfirmStashRowRt;
    private Text _completionConfirmStashCheckmark;
    private bool _completionConfirmStashChecked;
    private Text _completionConfirmWarningText;
    private Button _completionConfirmConfirmButton;
    private Button _completionConfirmLaterButton;
    private QuestDefinition _pendingCompletionQuest;

    private void EnsureAcceptConfirmPopup()
    {
        if (_acceptConfirmRoot != null) return;

        Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;

        RectTransform popupRt = NewPopupRect("AcceptQuestConfirmPopup", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _acceptConfirmRoot = popupRt.gameObject;
        var backdrop = _acceptConfirmRoot.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.6f);
        backdrop.raycastTarget = true;

        RectTransform box = NewPopupRect("Box", popupRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840f, 826f));
        _acceptConfirmBox = box;
        // 窗口标准：黑白+高斯模糊背景，不额外叠一层纯色底——跟角色面板/任务日志
        // 同一套 SkyPrisonFloatingWindowKit.BuildBlurBackground，不要再画实色矩形。
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, box, out _acceptConfirmBlurTracker);
        AddPopupCornerBrackets(box);

        // 左上角小标签——AXIA是战略级机器人，接任务在人设上更像是"接收到一条新指令"，
        // 不只是"翻开一份任务简介"，加这行营造一点战术终端读出的味道，跟HAZARD RANK
        // 那种军事化风格是同一路子。
        Text directiveTag = NewPopupText("DirectiveTag", box, font, 20, TextAnchor.UpperLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var directiveRt = directiveTag.rectTransform;
        directiveRt.anchorMin = new Vector2(0f, 1f); directiveRt.anchorMax = new Vector2(0f, 1f);
        directiveRt.pivot = new Vector2(0f, 1f);
        directiveRt.anchoredPosition = new Vector2(28f, -20f);
        directiveRt.sizeDelta = new Vector2(300f, 28f);
        directiveTag.text = L("quest_accept_confirm_directive_tag", "NEW DIRECTIVE");
        directiveTag.color = new Color(SkyPrisonUIPalette.WarmRed.r, SkyPrisonUIPalette.WarmRed.g, SkyPrisonUIPalette.WarmRed.b, 0.55f);
        // 缓慢闪烁透明度由 UpdateAcceptConfirmDirectiveBlink() 每帧驱动，这里存个引用。
        _acceptConfirmDirectiveText = directiveTag;

        // 自上而下依次排布，每一块用"顶边距+自身高度"算，不再拿几个孤立的负数瞎凑——
        // 之前标题/HAZARD RANK/描述三块的anchoredPosition.y是各自拍的，没对齐height
        // 换算成的实际区间，结果描述整块往上超进了标题里，三行字叠在一起。
        // 标题往下让开26px，跟左上角"NEW DIRECTIVE"标签之前会重叠(标签占到y=-48，
        // 标题原本从y=-28开始，两者中间有20px是叠在一起的)——下面所有块跟着整体
        // 往下挪同样的26px，保持彼此间距不变。
        _acceptConfirmTitleText = NewPopupText("Title", box, font, 40, TextAnchor.MiddleCenter,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(54f + 28f)), new Vector2(-64f, 56f));
        _acceptConfirmTitleText.color = SkyPrisonUIPalette.ColdGreen;
        _acceptConfirmTitleText.fontStyle = FontStyle.Bold;

        // HAZARD RANK在分隔线上方(右对齐)，分隔线在它下面单独一整行——不是跟分隔线
        // 挤在同一条水平线上。
        _acceptConfirmHazardText = NewPopupText("Hazard", box, font, 26, TextAnchor.MiddleRight,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(118f + 16f)), new Vector2(-64f, 32f));
        _acceptConfirmHazardText.color = SkyPrisonUIPalette.White;

        AddPopupEdge(box, "Divider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-64f, 2f));
        var dividerRt = (RectTransform)box.Find("Divider");
        dividerRt.anchoredPosition = new Vector2(0f, -166f);
        dividerRt.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);

        _acceptConfirmDescText = NewPopupText("Description", box, font, 28, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(182f + 70f)), new Vector2(-64f, 140f));
        _acceptConfirmDescText.color = SkyPrisonUIPalette.White;
        _acceptConfirmDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _acceptConfirmDescText.verticalOverflow = VerticalWrapMode.Truncate;

        // 报酬区域不用LayoutGroup——报酬金横向排、报酬物品强制一行一个，高度不固定，
        // 改成PopulateRewardRow里手动算每块的坐标。这里留一个够高的容器(预留到能装下
        // 货币一行+物品四行)，具体每次接取不同任务时重新摆放。
        _acceptConfirmRewardRow = NewPopupRect("RewardRow", box,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(334f + 180f)), new Vector2(-64f, 360f));

        // "设为追踪中任务"复选框——放在按钮正上方，默认打勾。追踪中任务只是记一个
        // questId(QuestRuntime.SetTrackedQuest)，供以后地图画面显示追踪用，这里
        // 不负责画地图标记。
        _acceptConfirmTrackChecked = true;
        var trackRowRt = NewPopupRect("TrackToggleRow", box, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(340f, 36f));
        trackRowRt.pivot = new Vector2(0f, 0f);
        trackRowRt.anchoredPosition = new Vector2(32f, 116f);
        var trackRowBtn = trackRowRt.gameObject.AddComponent<Button>();
        trackRowBtn.transition = Selectable.Transition.None;
        var trackRowImg = trackRowRt.gameObject.AddComponent<Image>();
        trackRowImg.color = new Color(1f, 1f, 1f, 0f); // 透明，只用来接收整行点击
        trackRowBtn.onClick.AddListener(() =>
        {
            _acceptConfirmTrackChecked = !_acceptConfirmTrackChecked;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            RefreshAcceptConfirmTrackCheckboxVisual();
        });

        var trackBoxRt = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
        trackBoxRt.SetParent(trackRowRt, false);
        trackBoxRt.anchorMin = new Vector2(0f, 0.5f); trackBoxRt.anchorMax = new Vector2(0f, 0.5f);
        trackBoxRt.pivot = new Vector2(0f, 0.5f);
        trackBoxRt.anchoredPosition = Vector2.zero;
        trackBoxRt.sizeDelta = new Vector2(28f, 28f);
        trackBoxRt.gameObject.AddComponent<Image>().color = Color.clear;
        AddPopupWhiteFrame(trackBoxRt, 2f);

        _acceptConfirmTrackCheckmark = NewPopupText("Check", trackBoxRt, font, 22, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _acceptConfirmTrackCheckmark.text = "✓";
        _acceptConfirmTrackCheckmark.color = SkyPrisonUIPalette.ColdGreen;
        _acceptConfirmTrackCheckmark.raycastTarget = false;

        var trackLabel = NewPopupText("Label", trackRowRt, font, 26, TextAnchor.MiddleLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        trackLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f); trackLabel.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        trackLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
        trackLabel.rectTransform.anchoredPosition = new Vector2(36f, 0f);
        trackLabel.rectTransform.sizeDelta = new Vector2(300f, 32f);
        trackLabel.text = L("quest_accept_confirm_track_quest", "设为追踪中任务");
        trackLabel.color = SkyPrisonUIPalette.White;
        trackLabel.raycastTarget = false;

        _acceptConfirmCancelButton = MakePopupButton(L("quest_accept_confirm_cancel", "取消"), box, font, new Vector2(0.5f, 0f), new Vector2(-160f, 48f), () =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
            HideAcceptConfirmPopup();
            // 确认框把底下的列表清掉了，取消要用同一个入参重新搭出来，不能只是隐藏
            // 确认框——不然玩家会发现"取消"之后列表整个消失，卡在只剩确认框刚关掉
            // 的空白状态。
            ShowAcceptQuestListView(_acceptListExplicitQuest);
        });
        _acceptConfirmStartButton = MakePopupButton(L("quest_accept_confirm_start", "开始任务"), box, font, new Vector2(0.5f, 0f), new Vector2(160f, 48f), () =>
        {
            QuestDefinition quest = _pendingAcceptQuest;
            bool trackQuest = _acceptConfirmTrackChecked;
            HideAcceptConfirmPopup();
            if (quest == null) return;

            QuestRuntime.Instance?.TryAcceptQuest(quest);
            if (trackQuest)
                QuestRuntime.Instance?.SetTrackedQuest(quest);

            // 接完直接跳回正常对话层(不是继续停留在这份马上就要变空的可接取列表里)，
            // 用quest本身的描述当回应文案——跟任务日志共用同一份真实数据，不用另外
            // 手打一句"你接受了xxx"的静态台词。
            ExitQuestListView();
            if (sentenceText != null)
                sentenceText.text = quest.GetLocalizedDescription(quest.name);
        });

        _acceptConfirmRoot.SetActive(false);
    }

    private void RefreshAcceptConfirmTrackCheckboxVisual()
    {
        if (_acceptConfirmTrackCheckmark != null)
            _acceptConfirmTrackCheckmark.gameObject.SetActive(_acceptConfirmTrackChecked);
    }

    private void ShowAcceptConfirmPopup(QuestDefinition quest)
    {
        EnsureAcceptConfirmPopup();
        _pendingAcceptQuest = quest;

        // 每次弹出都重置成默认打勾——不是弹窗第一次建的时候设一次就完事，不然
        // 上一条任务取消勾选后，下一条任务弹出来还是沿用那个没打勾的状态。
        _acceptConfirmTrackChecked = true;
        RefreshAcceptConfirmTrackCheckboxVisual();

        SetPopupTitleWithQuestMarker(_acceptConfirmTitleText, quest);
        if (_acceptConfirmHazardText != null)
        {
            int hazardRank = Mathf.Clamp(quest.hazardRank, 1, 10);
            string dots = new string('◆', hazardRank) + new string('◇', 10 - hazardRank);
            _acceptConfirmHazardText.text = $"{L("quest_hazard_rank_label", "HAZARD RANK")} {dots}";
            _acceptConfirmHazardText.color = SkyPrisonUIPalette.White;
        }
        if (_acceptConfirmDescText != null)
            _acceptConfirmDescText.text = quest.GetLocalizedDescription("");
        Font popupFont = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;
        PopulateRewardRow(quest, popupFont);

        // 确认框开着的时候，底下那份可接取任务列表按钮必须清掉——只让确认框自己的
        // 黑白模糊背景挡住画面不够，之前底下的按钮还留着，鼠标/手柄点击被那份没被
        // 清掉的按钮抢走(点"开始任务"其实点中了背后的任务名字，重新弹出同一个确认
        // 框，表现成"点了没反应、只有声音")。取消时用 _acceptListExplicitQuest 重搭。
        ClearOptions();

        _acceptConfirmRoot.SetActive(true);
        // 独立SE通道，不跟通用"窗口打开"共用——接受任务是仪式感时刻，需要单独能调的音效。
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.QuestAcceptConfirmOpen);
        RefreshGamepadTargets();

        // 开关动画跟项目里其它窗口统一的规范一致(纵向缩放0→1/1→0)，不是瞬间
        // 出现/消失。如果上一次的关闭动画还没播完就又打开了，先打断它。
        if (_acceptConfirmAnimCoroutine != null) StopCoroutine(_acceptConfirmAnimCoroutine);
        _acceptConfirmAnimCoroutine = StartCoroutine(AcceptConfirmOpenAnimation());
    }

    private IEnumerator AcceptConfirmOpenAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_acceptConfirmBox == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p);
            _acceptConfirmBox.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_acceptConfirmBox != null) _acceptConfirmBox.localScale = Vector3.one;
        _acceptConfirmAnimCoroutine = null;
    }

    private IEnumerator AcceptConfirmCloseAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_acceptConfirmBox == null) break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p;
            _acceptConfirmBox.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_acceptConfirmRoot != null) _acceptConfirmRoot.SetActive(false);
        _acceptConfirmAnimCoroutine = null;
    }

    /// <summary>报酬预览行——直接读 QuestDefinition.GetRewardCurrencies/GetRewardItems
    /// (它们本身是从 onCompleteActions 里的 act_add_currency/act_add_item_to_bag 现读的)，
    /// 不额外维护一份"预览用"的奖励数据，改奖励只用改 onCompleteActions 一处。带图标显示，
    /// 比纯文字更有"这是真的能拿到手的东西"的仪式感。</summary>
    private const float RewardChipHeight = 40f;
    private const float RewardChipSpacing = 16f;
    private const float RewardGroupGap = 40f; // 报酬金/报酬物品两组之间额外空开的距离，比同组内chip间距更宽

    private void PopulateRewardRow(QuestDefinition quest, Font font)
        => PopulateRewardRow(quest, font, _acceptConfirmRewardRow);

    /// <summary>rewardRow参数化——完成结算弹窗(EnsureCompletionConfirmPopup)复用
    /// 同一份构建逻辑，不用另外抄一遍货币/物品排列的坐标计算。</summary>
    private void PopulateRewardRow(QuestDefinition quest, Font font, RectTransform rewardRow)
    {
        if (rewardRow == null) return;

        foreach (Transform child in rewardRow)
            Destroy(child.gameObject);

        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        var currencies = quest.GetRewardCurrencies();
        var items = quest.GetRewardItems();
        float containerWidth = rewardRow.rect.width;

        Text label = NewPopupText("Label", rewardRow, font, 26, TextAnchor.UpperLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        label.text = L("quest_accept_confirm_reward_label", "报酬");
        label.color = SkyPrisonUIPalette.White;
        PlaceRewardChild(label.rectTransform, 0f, 0f, 90f, 32f);

        if (currencies.Count == 0 && items.Count == 0)
        {
            Text noneText = NewPopupText("None", rewardRow, font, 26, TextAnchor.UpperLeft,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            noneText.text = L("quest_accept_confirm_no_reward", "无");
            noneText.color = SkyPrisonUIPalette.White;
            PlaceRewardChild(noneText.rectTransform, 0f, 36f, 120f, 32f);
            return;
        }

        // 报酬金——同一行放得下就横向排(通常也就一两种货币)，放不下才换行。
        float x = 0f, y = 36f;
        foreach (var (currency, amount) in currencies)
        {
            BuildRewardChip(rewardRow, font, currency.icon,
                $"{currency.GetDisplayName(locTable)} x{amount}", ref x, ref y, containerWidth);
        }

        // 报酬金和报酬物品必须分开成两块，不是紧挨着的一长串——不管上面那行还有没
        // 有剩余空间，物品都强制另起一整行开始。
        if (currencies.Count > 0 && items.Count > 0)
            y += RewardChipHeight + RewardGroupGap;
        else if (currencies.Count > 0)
            y += RewardChipHeight + RewardChipSpacing;

        // 报酬物品——一行一个，各自占满整行宽度，配合深浅交替底色做成清单的样子，
        // 不是跟货币一样横向挤成一串小方块。
        for (int i = 0; i < items.Count; i++)
        {
            var (item, amount) = items[i];
            BuildRewardItemRow(rewardRow, font, item.icon,
                $"{item.GetLocalizedDisplayName()} x{amount}", i, 0f, y, containerWidth);
            y += RewardChipHeight + RewardChipSpacing;
        }
    }

    private static void PlaceRewardChild(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, height);
    }

    // 深浅交替的底部填充色——用来给"报酬物品"这几行加区分度，做成清单的样子。
    private static readonly Color RewardChipFillA = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color RewardChipFillB = new Color(1f, 1f, 1f, 0.11f);

    /// <summary>一个"图标+文字"奖励小方块，用于报酬金——一行放得下就横向排(通常也
    /// 就一两种货币)，放不下才换行。不加底色，货币本来数量少，不需要清单式的区分度。
    /// x/y是手动流式换行布局的当前笔头位置，超出containerWidth就自动换行，用完之后
    /// 原地更新供下一块接着摆。</summary>
    private void BuildRewardChip(RectTransform parent, Font font, Sprite icon, string label,
        ref float x, ref float y, float containerWidth)
    {
        float chipWidth = icon != null ? 176f : 136f;
        if (x > 0f && x + chipWidth > containerWidth)
        {
            x = 0f;
            y += RewardChipHeight + RewardChipSpacing;
        }

        var chipRt = NewPopupRect("Chip", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        PlaceRewardChild(chipRt, x, y, chipWidth, RewardChipHeight);

        var chipLayout = chipRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        chipLayout.spacing = 8f;
        chipLayout.padding = new RectOffset(10, 10, 4, 4);
        chipLayout.childAlignment = TextAnchor.MiddleLeft;
        chipLayout.childControlWidth = true;
        chipLayout.childControlHeight = true;
        chipLayout.childForceExpandWidth = false;
        chipLayout.childForceExpandHeight = false;

        if (icon != null)
        {
            var iconRt = NewPopupRect("Icon", chipRt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(32f, 32f));
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconRt.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;
        }

        Text t = NewPopupText("Label", chipRt, font, 24, TextAnchor.MiddleLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        t.text = label;
        t.color = SkyPrisonUIPalette.ColdGreen;
        t.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;

        x += chipWidth + RewardChipSpacing;
    }

    /// <summary>报酬物品的一整行——一行一个，占满容器宽度，配深浅交替底色，做成
    /// 清单的样子(不是跟货币一样横向挤成小方块)。rowIndex按奇偶决定深浅底色。</summary>
    private void BuildRewardItemRow(RectTransform parent, Font font, Sprite icon, string label, int rowIndex,
        float x, float y, float width)
    {
        var rowRt = NewPopupRect("ItemRow", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        PlaceRewardChild(rowRt, x, y, width, RewardChipHeight);

        var rowBg = rowRt.gameObject.AddComponent<Image>();
        rowBg.color = rowIndex % 2 == 0 ? RewardChipFillA : RewardChipFillB;
        rowBg.raycastTarget = false;

        var rowLayout = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.padding = new RectOffset(10, 10, 4, 4);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        if (icon != null)
        {
            var iconRt = NewPopupRect("Icon", rowRt, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(32f, 32f));
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconRt.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;
        }

        Text t = NewPopupText("Label", rowRt, font, 24, TextAnchor.MiddleLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        t.text = label;
        t.color = SkyPrisonUIPalette.ColdGreen;
        t.gameObject.AddComponent<LayoutElement>().preferredWidth = width - (icon != null ? 40f + 20f : 20f);
    }

    private void HideAcceptConfirmPopup()
    {
        _pendingAcceptQuest = null;
        if (_acceptConfirmRoot == null) return;

        if (_acceptConfirmAnimCoroutine != null) StopCoroutine(_acceptConfirmAnimCoroutine);
        _acceptConfirmAnimCoroutine = StartCoroutine(AcceptConfirmCloseAnimation());
        RefreshGamepadTargets();
    }

    // ── 任务完成结算弹窗 ──────────────────────────────────────────────────
    // 结构完全照抄EnsureAcceptConfirmPopup，但更简单：没有HAZARD RANK/描述文字，
    // 多了"背包装不下时送到仓库"这个复选框(只在有物品奖励且需要用到的时候显示)。
    // 覆盖在当前对话内容之上(不调用ClearOptions清空对话选项)——玩家点"稍后"
    // 只是把这个弹窗关掉，NPC原本的问候/选项还在底下等着，不受影响；这正是
    // 用户要求的"暂时取消对话也会保留任务，随时能再提交"。

    private void EnsureCompletionConfirmPopup()
    {
        if (_completionConfirmRoot != null) return;

        Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;

        RectTransform popupRt = NewPopupRect("QuestCompletionPopup", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _completionConfirmRoot = popupRt.gameObject;
        var backdrop = _completionConfirmRoot.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.6f);
        backdrop.raycastTarget = true;

        // 840x832——比之前的760高，多出来的空间留给下面新加的Description文字块
        // (跟接受任务确认框一样，兜底反馈文字要嵌在窗口里显示，不能只飘一段字幕
        // 在窗口外面)。832是按"警告文字底边到按钮顶边留40px"反推出来的，不是拍脑袋。
        RectTransform box = NewPopupRect("Box", popupRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840f, 832f));
        _completionConfirmBox = box;
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, box, out _completionConfirmBlurTracker);
        AddPopupCornerBrackets(box);

        Text directiveTag = NewPopupText("DirectiveTag", box, font, 20, TextAnchor.UpperLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var directiveRt = directiveTag.rectTransform;
        directiveRt.anchorMin = new Vector2(0f, 1f); directiveRt.anchorMax = new Vector2(0f, 1f);
        directiveRt.pivot = new Vector2(0f, 1f);
        directiveRt.anchoredPosition = new Vector2(28f, -20f);
        directiveRt.sizeDelta = new Vector2(300f, 28f);
        directiveTag.text = L("quest_complete_confirm_directive_tag", "DIRECTIVE COMPLETE");
        directiveTag.color = new Color(SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 0.55f);

        _completionConfirmTitleText = NewPopupText("Title", box, font, 40, TextAnchor.MiddleCenter,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(54f + 28f)), new Vector2(-64f, 56f));
        _completionConfirmTitleText.color = SkyPrisonUIPalette.ColdGreen;
        _completionConfirmTitleText.fontStyle = FontStyle.Bold;

        AddPopupEdge(box, "Divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-64f, 2f));
        var dividerRt = (RectTransform)box.Find("Divider");
        dividerRt.anchoredPosition = new Vector2(0f, -160f);
        dividerRt.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);

        // NPC反馈文字——之前没有这一块，反馈只能靠悬浮字幕临时飘一句在窗口外面，
        // 用户明确要求"提交任务也应该显示在窗口里面"，照抄EnsureAcceptConfirmPopup
        // 的Description写法。只有没配演出/台词、退回兜底任务描述的情况才会真的显示
        // 内容(ShowCompletionConfirmPopup按embeddedResponseText是否为空决定
        // 显隐)——真人演出/念白已经通过悬浮字幕播过了，这里不重复再放一遍。
        _completionConfirmDescText = NewPopupText("Description", box, font, 28, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(180f + 60f)), new Vector2(-64f, 120f));
        _completionConfirmDescText.color = SkyPrisonUIPalette.White;
        _completionConfirmDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _completionConfirmDescText.verticalOverflow = VerticalWrapMode.Truncate;

        // anchoredPosition是这块矩形的中心点(NewPopupRect统一用pivot=(0.5,0.5))，
        // 顶边距320px换算成中心Y要减半高，不是直接减整个高度——写成-(320+280)会把
        // 中心点甩到280px+320px=600px深，实际顶边只到-320，反而多出140px空白。
        // 320=上面Description块底边(180+120=300)+20px间距。
        _completionConfirmRewardRow = NewPopupRect("RewardRow", box,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(320f + 280f / 2f)), new Vector2(-64f, 280f));

        // "送到仓库"复选框——只在有物品奖励、且背包装不下时才真正需要，但常驻建好，
        // 每次弹出时按ShowCompletionConfirmPopup里的判断结果决定显隐/默认勾选。
        var stashRowRt = NewPopupRect("StashToggleRow", box, new Vector2(0f, 1f), new Vector2(0f, 1f),
            Vector2.zero, new Vector2(400f, 36f));
        stashRowRt.pivot = new Vector2(0f, 1f);
        // 加了上面的Description块之后，奖励列表(RewardRow)整体往下挪了140px
        // (顶边180→320)，这里跟着往下挪同样的量(550→620)，保持"紧贴按钮上方
        // 一点、不留大片空白"这个相对关系不变。
        stashRowRt.anchoredPosition = new Vector2(32f, -620f);
        var stashRowBtn = stashRowRt.gameObject.AddComponent<Button>();
        stashRowBtn.transition = Selectable.Transition.None;
        var stashRowImg = stashRowRt.gameObject.AddComponent<Image>();
        stashRowImg.color = new Color(1f, 1f, 1f, 0f);
        stashRowBtn.onClick.AddListener(() =>
        {
            _completionConfirmStashChecked = !_completionConfirmStashChecked;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            RefreshCompletionConfirmStashVisual();
        });

        var stashBoxRt = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
        stashBoxRt.SetParent(stashRowRt, false);
        stashBoxRt.anchorMin = new Vector2(0f, 0.5f); stashBoxRt.anchorMax = new Vector2(0f, 0.5f);
        stashBoxRt.pivot = new Vector2(0f, 0.5f);
        stashBoxRt.anchoredPosition = Vector2.zero;
        stashBoxRt.sizeDelta = new Vector2(28f, 28f);
        stashBoxRt.gameObject.AddComponent<Image>().color = Color.clear;
        AddPopupWhiteFrame(stashBoxRt, 2f);

        _completionConfirmStashCheckmark = NewPopupText("Check", stashBoxRt, font, 22, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _completionConfirmStashCheckmark.text = "✓";
        _completionConfirmStashCheckmark.color = SkyPrisonUIPalette.ColdGreen;
        _completionConfirmStashCheckmark.raycastTarget = false;

        var stashLabel = NewPopupText("Label", stashRowRt, font, 26, TextAnchor.MiddleLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        stashLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f); stashLabel.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        stashLabel.rectTransform.pivot = new Vector2(0f, 0.5f);
        stashLabel.rectTransform.anchoredPosition = new Vector2(36f, 0f);
        stashLabel.rectTransform.sizeDelta = new Vector2(340f, 32f);
        stashLabel.text = L("quest_complete_confirm_send_to_stash", "背包装不下时送到仓库");
        stashLabel.color = SkyPrisonUIPalette.White;
        _completionConfirmStashRowRt = stashRowRt;

        // "送到仓库"复选框顶边在-620，行高36，底边到-656——警告文字放在它下方
        // 20px处，同样按"顶边距+自身高度一半"换算成中心Y(而不是像上面reward row
        // 那样直接减整高，见那边的踩坑说明)。
        _completionConfirmWarningText = NewPopupText("Warning", box, font, 24, TextAnchor.MiddleLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(676f + 32f / 2f)), new Vector2(-64f, 32f));
        _completionConfirmWarningText.color = SkyPrisonUIPalette.WarmRed;
        _completionConfirmWarningText.text = "";

        _completionConfirmLaterButton = MakePopupButton(L("quest_complete_confirm_later", "稍后"), box, font,
            new Vector2(0.5f, 0f), new Vector2(-160f, 48f), () =>
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
                HideCompletionConfirmPopup();
                // 跟接受任务确认框的"取消"同一个道理——确认框把底下的任务列表清掉了
                // (见ShowCompletionConfirmPopup的ClearOptions)，稍后要把列表重新搭
                // 出来，不能只是隐藏确认框、留玩家卡在一片空白的选项面板上。任务还是
                // 进行中状态，随时可以再选中它重新弹出结算界面。
                ShowQuestListView();
            });
        _completionConfirmConfirmButton = MakePopupButton(L("quest_complete_confirm_confirm", "确认领取"), box, font,
            new Vector2(0.5f, 0f), new Vector2(160f, 48f), ConfirmCompletionConfirmPopup);

        _completionConfirmRoot.SetActive(false);
    }

    private void ConfirmCompletionConfirmPopup()
    {
        QuestDefinition quest = _pendingCompletionQuest;
        if (quest == null) return;
        // 背包放不下又没勾送仓库时按钮理论上已经是disabled状态，这里再判一次兜底，
        // 防止手柄/其它输入路径绕过按钮interactable状态直接触发onClick。
        if (!_completionConfirmStashChecked && !CanFitAllRewardItems(quest))
            return;

        QuestRuntime.Instance?.CompleteQuestFromSettlement(quest, _completionConfirmStashChecked);
        HideCompletionConfirmPopup();

        // 领取完这条任务就不再是"进行中"了，不会再出现在任务列表里——直接跳回
        // 正常对话层(不是继续停留在这份马上就要少一条的进行中列表里)，跟接受任务
        // 成功后的ExitQuestListView()是同一个道理。
        ExitQuestListView();

        // 完成之后用任务描述当NPC的回应文案，跟接受任务成功那边的处理是同一个
        // 道理，不用另外手打一句"谢谢你带来的东西"的静态台词。
        if (sentenceText != null)
            sentenceText.text = quest.GetLocalizedDescription(quest.name);
    }

    private void RefreshCompletionConfirmStashVisual()
    {
        if (_completionConfirmStashCheckmark != null)
            _completionConfirmStashCheckmark.gameObject.SetActive(_completionConfirmStashChecked);
        RefreshCompletionConfirmValidity();
    }

    /// <summary>背包放不下奖励物品、又没勾"送到仓库"的话，锁住确认按钮并给出提示——
    /// 不允许在这种状态下确认(奖励物品会静默丢失，这正是这整套结算界面想避免的
    /// 问题本身)。</summary>
    private void RefreshCompletionConfirmValidity()
    {
        QuestDefinition quest = _pendingCompletionQuest;
        if (quest == null) return;

        bool blocked = !_completionConfirmStashChecked && !CanFitAllRewardItems(quest);
        if (_completionConfirmWarningText != null)
            _completionConfirmWarningText.text = blocked
                ? L("quest_complete_confirm_bag_full", "背包已满，请勾选\"送到仓库\"，或先清理背包空间")
                : "";
        if (_completionConfirmConfirmButton != null)
            _completionConfirmConfirmButton.interactable = !blocked;
    }

    /// <summary>纯只读模拟(InventoryRuntime.SimulateAdd不修改任何状态)，检查这条任务
    /// 全部物品奖励(不含货币，货币没有"装不下"的概念)是不是都能塞进背包。</summary>
    private static bool CanFitAllRewardItems(QuestDefinition quest)
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null) return true; // 拿不到背包实例时不要卡死玩家，放行

        foreach (var (item, amount) in quest.GetRewardItems())
        {
            if (inv.SimulateAdd(item, amount) > 0)
                return false;
        }
        return true;
    }

    /// <summary>embeddedResponseText——没配演出/台词、退回兜底任务描述当反馈的情况下，
    /// 调用方(CheckInWithGiverForQuest/ConfirmDeliveryPopup)会把这段兜底文字传进来，
    /// 直接显示在结算框自己的Description区域里，不再单独飘一段字幕在窗口外面。
    /// 真配了演出/台词的情况，反馈已经通过悬浮字幕播过了，这里传null，Description
    /// 区域整块隐藏，不重复显示。</summary>
    public void ShowCompletionConfirmPopup(QuestDefinition quest, string embeddedResponseText = null)
    {
        if (quest == null) return;
        EnsureCompletionConfirmPopup();
        _pendingCompletionQuest = quest;

        Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;
        SetPopupTitleWithQuestMarker(_completionConfirmTitleText, quest);

        bool hasDesc = !string.IsNullOrWhiteSpace(embeddedResponseText);
        if (_completionConfirmDescText != null)
        {
            _completionConfirmDescText.text = hasDesc ? embeddedResponseText : "";
            _completionConfirmDescText.gameObject.SetActive(hasDesc);
        }

        PopulateRewardRow(quest, font, _completionConfirmRewardRow);

        bool hasItemRewards = quest.GetRewardItems().Count > 0;
        // 只要有物品奖励就默认打勾——这不是"背包放不下就整批送仓库"的二选一开关，
        // 是"背包装得下的部分永远先进背包，只有真正溢出的部分才转仓库"(见
        // QuestRuntime.GrantRewardItems)，勾着它没有任何副作用，用户明确要求
        // 默认应该一直是勾上的状态，不要等"预判到装不下"才临时打勾。
        _completionConfirmStashChecked = hasItemRewards;
        if (_completionConfirmStashRowRt != null)
            _completionConfirmStashRowRt.gameObject.SetActive(hasItemRewards);
        RefreshCompletionConfirmStashVisual();

        // 确认框开着的时候，底下那份进行中任务列表按钮必须清掉——同一个坑之前在
        // 接受任务确认框那边踩过一次(黑白模糊背景挡不住点击，得真的ClearOptions)，
        // 不清掉的话点"确认领取"其实点中了背后的任务名字按钮，表现成"点了没反应、
        // 只有声音"。"稍后"按钮会用ShowQuestListView()把它重新搭出来。
        ClearOptions();

        _completionConfirmRoot.SetActive(true);
        // 复用接受任务确认框同一个专属音效通道——完成任务是跟接受任务同一仪式感
        // 级别的时刻，不用另外新增一个SE类型。
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.QuestAcceptConfirmOpen);
        RefreshGamepadTargets();

        if (_completionConfirmAnimCoroutine != null) StopCoroutine(_completionConfirmAnimCoroutine);
        _completionConfirmAnimCoroutine = StartCoroutine(CompletionConfirmOpenAnimation());
    }

    private void HideCompletionConfirmPopup()
    {
        _pendingCompletionQuest = null;
        if (_completionConfirmRoot == null) return;

        if (_completionConfirmAnimCoroutine != null) StopCoroutine(_completionConfirmAnimCoroutine);
        _completionConfirmAnimCoroutine = StartCoroutine(CompletionConfirmCloseAnimation());
        RefreshGamepadTargets();
    }

    private IEnumerator CompletionConfirmOpenAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_completionConfirmBox == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p);
            _completionConfirmBox.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_completionConfirmBox != null) _completionConfirmBox.localScale = Vector3.one;
        _completionConfirmAnimCoroutine = null;
    }

    private IEnumerator CompletionConfirmCloseAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_completionConfirmBox == null) break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p;
            _completionConfirmBox.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_completionConfirmRoot != null) _completionConfirmRoot.SetActive(false);
        _completionConfirmAnimCoroutine = null;
    }

    // ── 交付确认弹窗 ──────────────────────────────────────────────────────
    // 结构照抄EnsureCompletionConfirmPopup——黑白模糊背景+白色L形角标+标题+分隔线，
    // 内容比结算弹窗更简单：目标描述一行+物品图标名字一行+当前持有数量一行(数量
    // 不够时变暗红并锁住确认按钮，够了变冷绿)，"取消"/"确认交付"两个按钮。确认
    // 交付那一刻才真的从背包扣物品+给这个目标专属的计数器加count，不是"拥有就算"
    // 那种被动判定——交付是"给出去"这个动作本身，跟"收集"类目标(看背包即时快照，
    // 丢了会掉回未完成)是两种完全不同的判定方式，见cond_has_delivered_item_to_npc
    // 的注释。
    private GameObject _deliveryConfirmRoot;
    private RectTransform _deliveryConfirmBox;
    private Coroutine _deliveryConfirmAnimCoroutine;
    private SkyPrisonBlurUVTracker _deliveryConfirmBlurTracker;
    private Text _deliveryConfirmTitleText;
    private Text _deliveryConfirmObjectiveText;
    private RectTransform _deliveryConfirmItemRow;
    private Text _deliveryConfirmCountText;
    private Button _deliveryConfirmConfirmButton;
    private Button _deliveryConfirmCancelButton;
    private QuestDefinition _pendingDeliveryQuest;
    private QuestObjective _pendingDeliveryObjective;
    /// <summary>一个目标节点可以同时要求交付好几种物品(比如"缴纳压缩饼干×3和净水×2")，
    /// 所以待交付内容是一份列表而不是单件。全部凑齐才允许确认，确认时逐条扣除、
    /// 逐条推进各自的计数器。</summary>
    private readonly struct PendingDelivery
    {
        public readonly ItemDefinition Item;
        public readonly UnitDefinition Npc;
        public readonly int Count;
        public PendingDelivery(ItemDefinition item, UnitDefinition npc, int count)
        {
            Item = item; Npc = npc; Count = count;
        }
    }
    private readonly List<PendingDelivery> _pendingDeliveries = new List<PendingDelivery>();

    private void EnsureDeliveryConfirmPopup()
    {
        if (_deliveryConfirmRoot != null) return;

        Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;

        RectTransform popupRt = NewPopupRect("QuestDeliveryPopup", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _deliveryConfirmRoot = popupRt.gameObject;
        var backdrop = _deliveryConfirmRoot.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.6f);
        backdrop.raycastTarget = true;

        RectTransform box = NewPopupRect("Box", popupRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840f, 420f));
        _deliveryConfirmBox = box;
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, box, out _deliveryConfirmBlurTracker);
        AddPopupCornerBrackets(box);

        Text directiveTag = NewPopupText("DirectiveTag", box, font, 20, TextAnchor.UpperLeft,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var directiveRt = directiveTag.rectTransform;
        directiveRt.anchorMin = new Vector2(0f, 1f); directiveRt.anchorMax = new Vector2(0f, 1f);
        directiveRt.pivot = new Vector2(0f, 1f);
        directiveRt.anchoredPosition = new Vector2(28f, -20f);
        directiveRt.sizeDelta = new Vector2(300f, 28f);
        directiveTag.text = L("quest_delivery_confirm_directive_tag", "DELIVERY REQUIRED");
        directiveTag.color = new Color(SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 0.55f);

        _deliveryConfirmTitleText = NewPopupText("Title", box, font, 40, TextAnchor.MiddleCenter,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(54f + 28f)), new Vector2(-64f, 56f));
        _deliveryConfirmTitleText.color = SkyPrisonUIPalette.ColdGreen;
        _deliveryConfirmTitleText.fontStyle = FontStyle.Bold;

        AddPopupEdge(box, "Divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-64f, 2f));
        var dividerRt = (RectTransform)box.Find("Divider");
        dividerRt.anchoredPosition = new Vector2(0f, -160f);
        dividerRt.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);

        _deliveryConfirmObjectiveText = NewPopupText("Objective", box, font, 28, TextAnchor.MiddleLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(180f + 32f / 2f)), new Vector2(-64f, 32f));
        _deliveryConfirmObjectiveText.color = SkyPrisonUIPalette.White;

        _deliveryConfirmItemRow = NewPopupRect("ItemRow", box,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(220f + 40f / 2f)), new Vector2(-64f, 40f));

        _deliveryConfirmCountText = NewPopupText("Count", box, font, 26, TextAnchor.MiddleLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(272f + 32f / 2f)), new Vector2(-64f, 32f));

        _deliveryConfirmCancelButton = MakePopupButton(L("quest_delivery_confirm_cancel", "取消"), box, font,
            new Vector2(0.5f, 0f), new Vector2(-160f, 48f), () =>
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
                HideDeliveryConfirmPopup();
                // 跟结算弹窗"稍后"同一个道理——确认框把底下的任务列表清掉了，取消
                // 要把列表重新搭出来，不能留玩家卡在一片空白的选项面板上。
                ShowQuestListView();
            });
        _deliveryConfirmConfirmButton = MakePopupButton(L("quest_delivery_confirm_confirm", "确认交付"), box, font,
            new Vector2(0.5f, 0f), new Vector2(160f, 48f), ConfirmDeliveryPopup);

        _deliveryConfirmRoot.SetActive(false);
    }

    private void ConfirmDeliveryPopup()
    {
        QuestDefinition quest = _pendingDeliveryQuest;
        QuestObjective objective = _pendingDeliveryObjective;
        var deliveries = new List<PendingDelivery>(_pendingDeliveries);
        if (quest == null || deliveries.Count == 0) return;

        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null) return;

        // 按钮理论上已经按持有数量锁住了，这里再判一次兜底，防止手柄/其它输入路径
        // 绕过interactable状态直接触发onClick，或者背包在弹窗开着期间被其它途径
        // 动过(比如同时还开着背包界面丢弃了东西)。
        //
        // 多件交付必须"先全部验够、再统一扣除"，不能边验边扣——中途某一件不够就
        // 直接return的话，前面已经扣掉的物品就凭空消失了，而目标还是没完成。
        foreach (PendingDelivery d in deliveries)
        {
            if (inv.CountItem(d.Item) < d.Count)
                return;
        }

        foreach (PendingDelivery d in deliveries)
        {
            if (!inv.RemoveItem(d.Item, d.Count)) continue;
            SaveManager.Player?.IncrementCounter($"delivered_item_{d.Item.itemId}_to_{d.Npc.unitId}", d.Count);
        }
        HideDeliveryConfirmPopup();

        // 交付完这一步就不再是"当前目标"了(除非任务本身出错配了两条一模一样的
        // 交付条件)，直接跳回正常对话层，跟结算弹窗confirm之后的ExitQuestListView()
        // 是同一个道理。
        ExitQuestListView();

        // objective要在ExitQuestListView/HideDeliveryConfirmPopup清掉_pendingDelivery*
        // 之前先取出来存到局部变量(已经在方法开头做了)，不然RemoveItem+IncrementCounter
        // 之后这个目标已经锁存完成、quest.GetCurrentObjective()会指向下一个目标，
        // 再想现查就晚了。结算界面要等台词/演出真的播完再弹，同一个道理。
        //
        // 跟CheckInWithGiverForQuest同一个道理：这次交付如果正好让任务整个结算
        // 完成、又没配真人演出/台词，兜底文案直接嵌进结算确认框显示，不再单独
        // 飘一段字幕在窗口外面。
        bool willComplete = quest.AreRequiredObjectivesComplete(false);
        if (willComplete && !HasRealNpcResponse(objective))
        {
            ShowCompletionConfirmPopup(quest, quest.GetLocalizedDescription(quest.name));
            return;
        }

        ShowNpcResponseForObjective(quest, objective, () =>
        {
            if (willComplete)
                ShowCompletionConfirmPopup(quest);
        });
    }

    public void ShowDeliveryConfirmPopup(QuestDefinition quest, QuestObjective objective)
    {
        if (quest == null || objective == null) return;
        List<PendingDelivery> deliveries = CollectDeliveriesToThisNpc(objective);
        if (deliveries.Count == 0) return;

        EnsureDeliveryConfirmPopup();
        _pendingDeliveryQuest = quest;
        _pendingDeliveryObjective = objective;
        _pendingDeliveries.Clear();
        _pendingDeliveries.AddRange(deliveries);

        // 物品行数不固定，窗口和物品区都要按行数长高，否则第二件以后会溢出到
        // 按钮上面去。基准 420 高对应一行，每多一行加一个行距。
        const float RowPitch = RewardChipHeight + 6f;
        float itemAreaHeight = Mathf.Max(RewardChipHeight, deliveries.Count * RowPitch);
        float extraHeight = Mathf.Max(0f, (deliveries.Count - 1) * RowPitch);
        if (_deliveryConfirmBox != null)
            _deliveryConfirmBox.sizeDelta = new Vector2(840f, 420f + extraHeight);
        if (_deliveryConfirmItemRow != null)
            _deliveryConfirmItemRow.sizeDelta = new Vector2(_deliveryConfirmItemRow.sizeDelta.x, itemAreaHeight);
        if (_deliveryConfirmCountText != null)
            _deliveryConfirmCountText.rectTransform.anchoredPosition =
                new Vector2(0f, -(220f + itemAreaHeight + 12f + 32f / 2f));

        Font font = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;
        if (_deliveryConfirmTitleText != null)
            _deliveryConfirmTitleText.text = quest.GetLocalizedTitle(quest.questId);
        if (_deliveryConfirmObjectiveText != null)
            _deliveryConfirmObjectiveText.text = QuestObjectiveTextResolver.ResolveTitle(objective, quest.giverNpc);

        if (_deliveryConfirmItemRow != null)
        {
            foreach (Transform child in _deliveryConfirmItemRow)
                Destroy(child.gameObject);

            float rowWidth = _deliveryConfirmItemRow.rect.width;
            for (int i = 0; i < deliveries.Count; i++)
            {
                PendingDelivery d = deliveries[i];
                BuildRewardItemRow(_deliveryConfirmItemRow, font, d.Item.icon,
                    $"{d.Item.GetLocalizedDisplayName()} x{d.Count}", i, 0f, -i * RowPitch, rowWidth);
            }
        }

        RefreshDeliveryConfirmValidity();

        // 确认框开着的时候，底下那份进行中任务列表按钮必须清掉——跟结算弹窗同一个
        // 坑，不清掉的话点"确认交付"其实点中了背后的任务名字按钮。"取消"按钮会用
        // ShowQuestListView()把它重新搭出来。
        ClearOptions();

        _deliveryConfirmRoot.SetActive(true);
        // 复用结算弹窗同一个专属音效通道——交付跟结算是同一仪式感级别的时刻。
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.QuestAcceptConfirmOpen);
        RefreshGamepadTargets();

        if (_deliveryConfirmAnimCoroutine != null) StopCoroutine(_deliveryConfirmAnimCoroutine);
        _deliveryConfirmAnimCoroutine = StartCoroutine(DeliveryConfirmOpenAnimation());
    }

    /// <summary>持有数量够不够——不够就把数量文字变暗红、锁住确认按钮，够了变冷绿、
    /// 放行。物品数量是背包的即时快照，弹窗开着期间理论上不会变(没有别的界面能
    /// 同时抢背包)，这里只在打开弹窗那一刻算一次，不需要每帧刷新。</summary>
    private void RefreshDeliveryConfirmValidity()
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;

        // 多件交付时逐件列出持有情况，全部够了才放行——只要有一件不够就整体锁住，
        // 不做"能交多少交多少"的部分交付(那样会让计数器推进到一半、目标既没完成
        // 又扣了东西，玩家没法判断还差多少)。
        bool allEnough = true;
        var lines = new List<string>();
        foreach (PendingDelivery d in _pendingDeliveries)
        {
            int current = inv != null ? inv.CountItem(d.Item) : 0;
            if (current < d.Count) allEnough = false;
            lines.Add(_pendingDeliveries.Count == 1
                ? string.Format(L("quest_delivery_confirm_holding", "当前持有：{0} / {1}"), current, d.Count)
                : $"{d.Item.GetLocalizedDisplayName()}  {current} / {d.Count}");
        }

        if (_deliveryConfirmCountText != null)
        {
            _deliveryConfirmCountText.text = string.Join("    ", lines);
            _deliveryConfirmCountText.color = allEnough ? SkyPrisonUIPalette.ColdGreen : SkyPrisonUIPalette.WarmRed;
        }
        bool enough = allEnough;
        if (_deliveryConfirmConfirmButton != null)
            _deliveryConfirmConfirmButton.interactable = enough;
    }

    private void HideDeliveryConfirmPopup()
    {
        _pendingDeliveryQuest = null;
        _pendingDeliveryObjective = null;
        _pendingDeliveries.Clear();
        if (_deliveryConfirmRoot == null) return;

        if (_deliveryConfirmAnimCoroutine != null) StopCoroutine(_deliveryConfirmAnimCoroutine);
        _deliveryConfirmAnimCoroutine = StartCoroutine(DeliveryConfirmCloseAnimation());
        RefreshGamepadTargets();
    }

    private IEnumerator DeliveryConfirmOpenAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_deliveryConfirmBox == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p);
            _deliveryConfirmBox.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_deliveryConfirmBox != null) _deliveryConfirmBox.localScale = Vector3.one;
        _deliveryConfirmAnimCoroutine = null;
    }

    private IEnumerator DeliveryConfirmCloseAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_deliveryConfirmBox == null) break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p;
            _deliveryConfirmBox.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_deliveryConfirmRoot != null) _deliveryConfirmRoot.SetActive(false);
        _deliveryConfirmAnimCoroutine = null;
    }

    /// <summary>挂在 manager 身上跑(不是自己)——SetActive(false) 会连自己身上正在跑的
    /// 协程一起冻结，挂在自己身上永远等不到子窗口关闭那一刻。子窗口关闭后只把选项
    /// 面板重新显示出来，不重新播字幕/语音(不然等于NPC又把这句话说了一遍)。</summary>
    private IEnumerator WaitForSubWindowCloseThenShow(SkyPrisonWindowManager_V1 manager, string subUiId)
    {
        while (manager != null && manager.IsOpen(subUiId))
            yield return null;

        if (this == null || gameObject == null) yield break;

        // 只把选项面板显示回来，不重新播字幕——重新调 ShowLine 会把台词/语音当成
        // "新的一句"再念一遍，用户明确要求不要这样，子窗口关闭不等于NPC又说了一次话。
        gameObject.SetActive(true);
        _idleTimer = 0f; // 刚从子窗口回来给个新的停留宽限，不要一回来就立刻念闲聊
    }

    private void ClearOptions()
    {
        foreach (var go in _optionButtons) if (go) Destroy(go);
        _optionButtons.Clear();
        _hoveredOptionIndex = -1;
        _autoAdvanceRemaining = -1f;
        _autoAdvanceOption = null;
    }

    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;

        // 接受任务二次确认弹窗开着时，手柄目标收窄到只剩这两个按钮——跟稀有物品
        // 出售二次确认弹窗同一个道理，不然背后的任务列表按钮还能被手柄选中/触发。
        if (_acceptConfirmRoot != null && _acceptConfirmRoot.activeSelf)
        {
            var confirmTargets = new List<Button>();
            if (_acceptConfirmStartButton != null) confirmTargets.Add(_acceptConfirmStartButton);
            if (_acceptConfirmCancelButton != null) confirmTargets.Add(_acceptConfirmCancelButton);
            _gamepadNav.SetTargets(confirmTargets);
            return;
        }

        // 任务结算弹窗开着时同理，手柄目标收窄到只剩"确认领取"/"稍后"这两个按钮。
        if (_completionConfirmRoot != null && _completionConfirmRoot.activeSelf)
        {
            var completionTargets = new List<Button>();
            if (_completionConfirmConfirmButton != null) completionTargets.Add(_completionConfirmConfirmButton);
            if (_completionConfirmLaterButton != null) completionTargets.Add(_completionConfirmLaterButton);
            _gamepadNav.SetTargets(completionTargets);
            return;
        }

        // 交付确认弹窗开着时同理，收窄到只剩"确认交付"/"取消"这两个按钮。
        if (_deliveryConfirmRoot != null && _deliveryConfirmRoot.activeSelf)
        {
            var deliveryTargets = new List<Button>();
            if (_deliveryConfirmConfirmButton != null) deliveryTargets.Add(_deliveryConfirmConfirmButton);
            if (_deliveryConfirmCancelButton != null) deliveryTargets.Add(_deliveryConfirmCancelButton);
            _gamepadNav.SetTargets(deliveryTargets);
            return;
        }

        var targets = new List<Button>();
        foreach (var go in _optionButtons)
        {
            var btn = go != null ? go.GetComponent<Button>() : null;
            if (btn != null) targets.Add(btn);
        }
        _gamepadNav.SetTargets(targets);
    }

    // ── 运行时现搭选项按钮(不走row prefab)──────────────────────────────────
    // 悬停变绿放大 + ■标记跟随，由 UpdateOptionFocusVisuals() 统一驱动，
    // 不再用 SkyPrisonUIButtonFeedback(那份是半透明染色，跟这里要的"整字变冷绿
    // 放大"效果对不上，两边都改颜色会互相打架)。
    /// <summary>
    /// 图标边长相对字号的倍率。按实际画面调出来的：0.8 -> 1.04 -> 1.12 -> 1.16。
    /// 选项行和确认弹窗共用，避免同一个图标在两个界面上大小不一。
    /// </summary>
    private const float QuestMarkerIconSizeToFontSize = 1.16f;

    /// <summary>图标和文字之间的间距，按图标边长的比例算，放大缩小时不走形。</summary>
    private const float QuestMarkerIconGapRatio = 0.35f;

    /// <summary>
    /// 图标往下压的像素数。Label 的矩形按行高算，垂直中心比汉字的视觉中心高一点
    /// （行高包含字面上方的留白），对齐矩形中心会显得吊在上面。
    /// 用绝对像素而不是按图标边长的比例——这是字体留白造成的固定偏差，跟图标多大无关。
    /// </summary>
    private const float QuestMarkerIconVerticalNudgePixels = 1.5f;

    /// <summary>
    /// 居中标题（接取/提交确认弹窗）前面的任务标记图标。
    ///
    /// 和选项行不同：这里的标题是居中的，而且弹窗是建一次反复用的，
    /// 每次显示换的只是文字。所以图标要按名字找回来复用，并且按当前文字宽度重新定位——
    /// 换一个标题更长的任务，图标位置必须跟着变。
    /// </summary>
    private void SetPopupTitleWithQuestMarker(Text titleText, QuestDefinition quest)
    {
        if (titleText == null)
            return;

        titleText.text = quest != null ? quest.GetLocalizedTitle(quest.questId) : "";

        var titleRt = (RectTransform)titleText.transform;
        Transform existing = titleRt.Find("QuestMarkerIcon");

        QuestMarker marker = QuestMarkerResolver.ResolveForQuest(quest);
        QuestMarkerIconSet set = QuestMarkerIconSet.Instance;
        Sprite sprite = set != null ? set.GetSprite(marker) : null;

        if (!marker.HasValue || sprite == null)
        {
            if (existing != null)
                existing.gameObject.SetActive(false);
            return;
        }

        Image image;
        RectTransform iconRt;
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            iconRt = (RectTransform)existing;
            image = existing.GetComponent<Image>();
        }
        else
        {
            var iconGo = new GameObject("QuestMarkerIcon", typeof(RectTransform), typeof(Image));
            iconRt = (RectTransform)iconGo.transform;
            iconRt.SetParent(titleRt, false);
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            image = iconGo.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
        }

        if (image == null)
            return;

        image.sprite = sprite;
        image.color = set.GetColor(marker);

        float size = titleText.fontSize * QuestMarkerIconSizeToFontSize;
        float gap = size * QuestMarkerIconGapRatio;
        iconRt.sizeDelta = new Vector2(size, size);
        // 文字居中，左边缘在 -preferredWidth/2；图标贴在它左边再让出一个 gap。
        iconRt.anchoredPosition = new Vector2(
            -(titleText.preferredWidth * 0.5f + gap + size * 0.5f), 0f);
    }

    /// <summary>
    /// 给任务选项的文字前面加任务标记图标。
    ///
    /// 图标放进 Label 的坐标系并把 Label 左边距推开，而不是塞在行的最左边——
    /// 行最左边已经被聚焦方块和竖线占着，那两个是 UpdateOptionFocusVisuals 按行高
    /// 算位置的，在它们旁边再插东西会互相干扰。缩进 Label 则完全不碰那套逻辑。
    /// </summary>
    private void AttachQuestMarkerIconToOption(GameObject optionGo, QuestMarker marker)
    {
        if (optionGo == null || !marker.HasValue)
            return;

        QuestMarkerIconSet set = QuestMarkerIconSet.Instance;
        Sprite sprite = set != null ? set.GetSprite(marker) : null;
        if (sprite == null)
            return;

        Transform labelTr = optionGo.transform.Find("Label");
        if (labelTr == null)
            return;

        var labelRt = (RectTransform)labelTr;
        var text = labelTr.GetComponent<Text>();
        float size = text != null ? text.fontSize * QuestMarkerIconSizeToFontSize : 34f;
        float gap = size * QuestMarkerIconGapRatio;

        var iconGo = new GameObject("QuestMarkerIcon", typeof(RectTransform), typeof(Image));
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.SetParent(labelRt, false);
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(1f, 0.5f);
        iconRt.sizeDelta = new Vector2(size, size);
        // pivot 在右、锚点在左边缘，往左让出一个 gap，图标正好贴在文字左侧外面。
        //
        // Y 方向不是 0：Label 的矩形按行高算，垂直中心比汉字的视觉中心高一点
        // （行高包含了字面上方的留白），图标对齐矩形中心就会显得吊在上面。
        // 往下压一点点对齐字面。
        iconRt.anchoredPosition = new Vector2(-gap, -QuestMarkerIconVerticalNudgePixels);

        var image = iconGo.GetComponent<Image>();
        image.sprite = sprite;
        image.color = set.GetColor(marker);
        image.raycastTarget = false;
        image.preserveAspect = true;

        // 文字整体右移，给图标腾出位置。
        labelRt.offsetMin = new Vector2(labelRt.offsetMin.x + size + gap, labelRt.offsetMin.y);
    }

    private GameObject BuildOptionButton(RectTransform parent, string label, int index)
    {
        var go = new GameObject("Option", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        // 显式钉死顶对齐(anchor+pivot都是(_,1))——VerticalLayoutGroup 在
        // childControlHeight=false 时只会按行的"现有pivot"去挪 anchoredPosition，
        // 不会把它摆正成顶对齐。没有这一行，行默认还是 AddComponent 出来的中心pivot，
        // 导致 UpdateOptionFocusVisuals() 里"顶部对齐"的居中公式全部算错，方块永远
        // 比文字偏低一整行高的一半——这才是方块跟文字对不齐的真正原因，不是字体留白。
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, OptionRowHeight);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = OptionRowHeight; le.flexibleWidth = 1f;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);

        var btn = go.AddComponent<Button>();
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        var relay = go.AddComponent<OptionHoverRelay>();
        relay.owner = this;
        relay.index = index;

        var labelRt = new GameObject("Label", typeof(RectTransform));
        var lrt = (RectTransform)labelRt.transform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        lrt.pivot = new Vector2(0f, 0.5f); // 聚焦放大从左边缘往右扩，不然默认居中缩放会把字顶到竖线/方块那边去
        var txt = labelRt.AddComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 34;
        txt.color = SkyPrisonUIPalette.White;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.raycastTarget = false;

        return go;
    }

    // ── 弹窗小工具——跟 ShopWindowController 的同名私有方法保持同一套做法 ──────
    private static string L(string key, string fallback)
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        return locTable != null ? locTable.Get(key, fallback) : fallback;
    }

    private static RectTransform NewPopupRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
        return rt;
    }

    private static Text NewPopupText(string name, Transform parent, Font font, int size, TextAnchor align,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        RectTransform rt = NewPopupRect(name, parent, anchorMin, anchorMax, anchoredPos, sizeDelta);
        var t = rt.gameObject.AddComponent<Text>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static Button MakePopupButton(string label, RectTransform parent, Font font, Vector2 anchor, Vector2 anchoredPos, System.Action onClick)
    {
        RectTransform rt = NewPopupRect("Btn_" + label, parent, anchor, anchor, anchoredPos, new Vector2(240f, 72f));

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);
        img.raycastTarget = true;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
        btn.onClick.AddListener(() => onClick());

        AddPopupWhiteFrame(rt, 2f);

        Text t = NewPopupText("Label", rt, font, 32, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        t.text = label;
        t.color = Color.white;
        t.raycastTarget = false;

        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
        return btn;
    }

    private static void AddPopupWhiteFrame(RectTransform parent, float thickness)
    {
        AddPopupEdge(parent, "Frame_T", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness));
        AddPopupEdge(parent, "Frame_B", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness));
        AddPopupEdge(parent, "Frame_L", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f));
        AddPopupEdge(parent, "Frame_R", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f));
    }

    private static void AddPopupEdge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.85f);
        img.raycastTarget = false;
    }

    private static void AddPopupCornerBrackets(RectTransform parent, float arm = 26f, float thickness = 2f)
    {
        Vector2[] corners = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
        foreach (var c in corners)
        {
            AddPopupCornerArm(parent, "Corner_H", c, new Vector2(arm, thickness));
            AddPopupCornerArm(parent, "Corner_V", c, new Vector2(thickness, arm));
        }
    }

    private static void AddPopupCornerArm(RectTransform parent, string name, Vector2 corner, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = corner; rt.anchorMax = corner; rt.pivot = corner;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;
    }
}
