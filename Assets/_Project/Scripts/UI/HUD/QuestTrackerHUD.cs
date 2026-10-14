using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 左侧常驻的"追踪中任务"HUD——显示当前追踪任务的名字+当前目标要求(跟任务日志
    /// "当前目标"那一行同一份数据源，QuestObjectiveTextResolver现取，不额外存文本、
    /// 不写第二套格式规则)。没有追踪任何任务时整体隐藏(淡出)。
    ///
    /// 三种数据变化，三种不同表现：
    /// 1. 目标进度数字变了(比如0/3→1/3)、目标本身没变——原地更新文字，不做动画。
    /// 2. 同一个任务推进到下一个节点(目标引用变了)——旧的目标文字先闪烁一下提示
    ///    "这条满足了"，再整体上滑淡出，新节点的文字从下方滑入接替。任务名那一行
    ///    不动(同一个任务推进节点时名字没变，没必要跟着抖)。
    /// 3. 追踪的任务本身换了(trackedQuestId指向了另一条任务，比如玩家在任务日志
    ///    按T切换)——两行内容一起简单淡出再淡入，不套用②那套"上滑+闪烁"演出，
    ///    那套是专门对应"完成了当前要求"这个语义的，换任务不是这个语义。
    ///
    /// 自建挂在SkyPrisonRuntimeUIDriver.HudInstance下(不是独立Canvas)，模式照抄
    /// SkyPrisonWeaponSwitchHUD/PlayerHUDStatusIconBar这类"自己找HUD、自己挂内容"
    /// 的运行时单例写法，不走prefab。
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestTrackerHUD : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!Application.isPlaying) return;
            EnsureInScene();
        }

        public static QuestTrackerHUD EnsureInScene()
        {
            if (FindObjectOfType<QuestTrackerHUD>() is { } existing) return existing;
            var go = new GameObject("[QuestTrackerHUD_Runtime]");
            DontDestroyOnLoad(go);
            return go.AddComponent<QuestTrackerHUD>();
        }

        [Header("布局(相对HUD左上角，参考分辨率3840x2160)")]
        [SerializeField] private float marginLeft = 90f;
        [SerializeField] private float topOffset = 720f; // 画面高度2160的1/3
        [SerializeField] private float rowWidth = 1200f;
        [SerializeField] private float nameFontSize = 26f;
        [SerializeField] private float objectiveFontSize = 34f;
        [SerializeField] private float rowGap = 6f;
        [SerializeField] private float nameRowHeight = 44f;
        [SerializeField] private float objectiveRowHeight = 60f;

        [Header("动画")]
        [SerializeField] private float outerFadeSpeed = 7f;
        [SerializeField] private float questSwitchFadeDuration = 0.16f;
        [SerializeField] private float flashDuration = 0.6f;
        [SerializeField] private int flashPulses = 1;
        [SerializeField] private float slideDuration = 0.28f;

        private RectTransform _contentRoot;
        private CanvasGroup _rootGroup;
        private TMP_Text _nameText;
        private RectTransform _objectiveClipRt;
        private RectTransform _objectiveSlotA;
        private RectTransform _objectiveSlotB;
        private CanvasGroup _objectiveGroupA;
        private CanvasGroup _objectiveGroupB;
        private TMP_Text _objectiveTextA;
        private TMP_Text _objectiveTextB;
        private bool _showingA = true;
        private bool _suppressAmbientFade;
        private float _rowH;

        private float _targetAlpha;
        private float _currentAlpha;

        private string _lastTrackedQuestId = "";
        private string _lastObjectiveLine = "";
        private QuestObjective _lastObjective;
        private Coroutine _transitionRoutine;
        private Coroutine _progressFlashRoutine;

        private float _searchCooldown;
        private const float SearchInterval = 1.5f;
        private float _pollTimer;
        private const float PollInterval = 0.3f;

        private void OnEnable()
        {
            LocalizationRuntime.OnLanguageChanged += HandleLanguageChanged;
        }

        private void OnDisable()
        {
            LocalizationRuntime.OnLanguageChanged -= HandleLanguageChanged;
        }

        // 任务名/目标文字只在"追踪的任务换了"或"目标推进了"这两个时机才会重新读取
        // (RefreshTrackedState里靠trackedId/objective引用比对判断)——切语言不属于这
        // 两种情况，任务本身/目标都没变，之前会一直显示成切语言前的那个语言，直到
        // 玩家凑巧推进节点或换追踪目标才连带刷新回来。订阅语言切换事件，强制立刻
        // 用新语言重新取一次文字，不等这两个不相关的触发点。这里是"瞬间订正"，不是
        // 内容变化，不走闪烁/上滑那套演出，也不影响_lastObjectiveLine的比对基准
        // (直接同步更新，下一次轮询不会把这次语言导致的文字变化误判成"进度变化"
        // 又闪一次)。
        private void HandleLanguageChanged(string _)
        {
            if (_contentRoot == null) return;

            QuestRuntime runtime = QuestRuntime.Instance;
            string trackedId = runtime != null ? runtime.GetTrackedQuestId() : "";
            if (string.IsNullOrEmpty(trackedId)) return;

            QuestDefinition quest = runtime.GetQuestById(trackedId);
            QuestObjective current = quest?.GetCurrentObjective();
            if (quest == null || current == null) return;

            if (_transitionRoutine != null) { StopCoroutine(_transitionRoutine); _transitionRoutine = null; _suppressAmbientFade = false; }
            if (_progressFlashRoutine != null) { StopCoroutine(_progressFlashRoutine); _progressFlashRoutine = null; }

            ApplyLineText(_nameText, quest.GetLocalizedTitle(quest.questId), nameFontSize, nameFontSize * MinFontSizeRatio);

            string objectiveLine = ResolveObjectiveLine(quest, current);
            _lastObjectiveLine = objectiveLine;
            TMP_Text visibleText = _showingA ? _objectiveTextA : _objectiveTextB;
            ApplyObjectiveText(visibleText, objectiveLine);
            visibleText.color = Color.white;
        }

        private void Update()
        {
            if (_contentRoot == null)
            {
                _searchCooldown -= Time.unscaledDeltaTime;
                if (_searchCooldown <= 0f) { TryBuildContent(); _searchCooldown = SearchInterval; }
                return;
            }

            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = PollInterval;
                RefreshTrackedState();
            }

            // 换追踪任务(SwitchQuestRoutine)期间这里的常规淡入淡出要让路——那个协程
            // 自己精确控制"先淡出到0、换内容、再淡入到1"这套节奏，如果这里同时也在
            // 每帧把_rootGroup.alpha往_targetAlpha(已经提前设成1了)方向拉，两边会
            // 互相打架，表现成刚淡出一半又被拉回去。
            if (!_suppressAmbientFade)
            {
                _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, 1f - Mathf.Exp(-outerFadeSpeed * Time.unscaledDeltaTime));
                if (_rootGroup != null) _rootGroup.alpha = _currentAlpha;
            }
        }

        // ── 挂到真正的 HudInstance 下 ─────────────────────────────────────────

        private void TryBuildContent()
        {
            var driver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
            GameObject hud = driver != null ? driver.HudInstance : null;
            if (hud == null) return;

            TMP_FontAsset font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");

            var rootGo = new GameObject("QuestTrackerHUD", typeof(RectTransform));
            rootGo.transform.SetParent(hud.transform, false);

            _contentRoot = rootGo.GetComponent<RectTransform>();
            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(0f, 1f);
            _contentRoot.pivot = new Vector2(0f, 1f);
            _contentRoot.anchoredPosition = new Vector2(marginLeft, -topOffset);
            _contentRoot.sizeDelta = new Vector2(rowWidth, nameRowHeight + rowGap + objectiveRowHeight);
            _rowH = objectiveRowHeight;

            _rootGroup = rootGo.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 0f;
            _rootGroup.blocksRaycasts = false;
            _rootGroup.interactable = false;
            _currentAlpha = 0f;
            _targetAlpha = 0f;

            // 任务名——固定不动的一行，同一个任务推进节点时不需要跟着抖，只有换追踪
            // 任务(③)时才会整体淡出淡入带着一起变。
            var nameGo = new GameObject("NameText", typeof(RectTransform));
            var nameRt = (RectTransform)nameGo.transform;
            nameRt.SetParent(_contentRoot, false);
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0f, 1f);
            nameRt.anchoredPosition = Vector2.zero;
            nameRt.sizeDelta = new Vector2(0f, nameRowHeight);
            _nameText = nameGo.AddComponent<TextMeshProUGUI>();
            _nameText.alignment = TextAlignmentOptions.TopLeft;
            _nameText.fontSize = nameFontSize;
            _nameText.color = SkyPrisonUIPalette.ColdGreen;
            _nameText.raycastTarget = false;
            _nameText.enableWordWrapping = false;
            _nameText.overflowMode = TextOverflowModes.Overflow;
            if (font != null) _nameText.font = font;

            // 目标文字用一个裁切容器(RectMask2D)，高度正好一行——旧文字上滑超出这个
            // 容器上边缘就会被裁掉(视觉上"消失")，新文字从容器下边缘外(y=-一行高)
            // 滑进来正好卡在容器下边缘时开始"露出来"，两个槽位(A/B)轮流当"当前显示"
            // 跟"下一个待滑入"，动画结束互换角色，跟SkyPrisonWeaponSwitchHUD的
            // (_cardA,_cardB)互换写法同一个思路。
            var clipGo = new GameObject("ObjectiveClip", typeof(RectTransform));
            _objectiveClipRt = (RectTransform)clipGo.transform;
            _objectiveClipRt.SetParent(_contentRoot, false);
            _objectiveClipRt.anchorMin = new Vector2(0f, 1f);
            _objectiveClipRt.anchorMax = new Vector2(1f, 1f);
            _objectiveClipRt.pivot = new Vector2(0f, 1f);
            _objectiveClipRt.anchoredPosition = new Vector2(0f, -(nameRowHeight + rowGap));
            _objectiveClipRt.sizeDelta = new Vector2(0f, objectiveRowHeight);
            clipGo.AddComponent<RectMask2D>();

            _objectiveSlotA = BuildObjectiveSlot(_objectiveClipRt, "SlotA", font, out _objectiveGroupA, out _objectiveTextA);
            _objectiveSlotB = BuildObjectiveSlot(_objectiveClipRt, "SlotB", font, out _objectiveGroupB, out _objectiveTextB);
            _objectiveSlotB.anchoredPosition = new Vector2(0f, -objectiveRowHeight);

            RefreshTrackedState();
        }

        private RectTransform BuildObjectiveSlot(RectTransform parent, string name, TMP_FontAsset font, out CanvasGroup group, out TMP_Text text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, objectiveRowHeight);

            group = go.AddComponent<CanvasGroup>();
            group.alpha = 1f;

            text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.fontSize = objectiveFontSize;
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            if (font != null) text.font = font;

            return rt;
        }

        // ── 追踪状态轮询(0.3秒一次，跟这份项目里其它靠轮询刷新的HUD/窗口同一个数量级) ──

        private void RefreshTrackedState()
        {
            QuestRuntime runtime = QuestRuntime.Instance;
            string trackedId = runtime != null ? runtime.GetTrackedQuestId() : "";

            if (string.IsNullOrEmpty(trackedId))
            {
                if (!string.IsNullOrEmpty(_lastTrackedQuestId))
                {
                    _lastTrackedQuestId = "";
                    _lastObjective = null;
                    _targetAlpha = 0f;
                }
                return;
            }

            QuestDefinition quest = runtime.GetQuestById(trackedId);
            QuestObjective current = quest?.GetCurrentObjective();
            if (quest == null || current == null)
            {
                // 任务不存在/全部目标都已完成(理论上CompleteQuest会同时摘掉追踪标记，
                // 这里只是兜个底，不强求跟那一帧完全同步)——直接隐藏，不硬凑文字。
                if (!string.IsNullOrEmpty(_lastTrackedQuestId))
                {
                    _lastTrackedQuestId = "";
                    _lastObjective = null;
                    _targetAlpha = 0f;
                }
                return;
            }

            string objectiveLine = ResolveObjectiveLine(quest, current);

            if (trackedId != _lastTrackedQuestId)
            {
                // 追踪的任务本身换了——两行内容一起简单淡出/淡入，不是"完成当前要求"
                // 那套上滑+闪烁演出。
                _lastTrackedQuestId = trackedId;
                _lastObjective = current;
                _lastObjectiveLine = objectiveLine;
                StopAllContentRoutines();
                _transitionRoutine = StartCoroutine(SwitchQuestRoutine(quest.GetLocalizedTitle(quest.questId), objectiveLine));
                _targetAlpha = 1f;
                return;
            }

            _targetAlpha = 1f;

            if (!ReferenceEquals(current, _lastObjective))
            {
                // 同一个任务推进到了下一个节点——旧文字闪烁+上滑淡出，新文字下方滑入。
                _lastObjective = current;
                _lastObjectiveLine = objectiveLine;
                StopAllContentRoutines();
                _transitionRoutine = StartCoroutine(TransitionObjectiveRoutine(objectiveLine));
                return;
            }

            // 目标本身没变——但用户明确要求"哪怕只是进度数字变了(比如捡到一个物品，
            // 0/3→1/3)也要闪烁提示玩家"，不能悄无声息地把文字换掉。用字符串比较
            // 判断"真的变了没"，不能每次轮询(0.3秒一次)都无条件触发，不然哪怕数值
            // 压根没动也会一直闪。
            if (_transitionRoutine == null && objectiveLine != _lastObjectiveLine)
            {
                _lastObjectiveLine = objectiveLine;
                TMP_Text visibleText = _showingA ? _objectiveTextA : _objectiveTextB;
                ApplyObjectiveText(visibleText, objectiveLine);
                if (_progressFlashRoutine != null) StopCoroutine(_progressFlashRoutine);
                // 单纯进度数字变化(还没完成)用金黄——绿色是"完成/成功"的语义，这里
                // 只是"有动静了"，用户明确要求两种场合区分颜色，不能共用冷绿。
                _progressFlashRoutine = StartCoroutine(FlashColorRoutine(visibleText, Color.white, ProgressFlashColor, flashDuration, flashPulses));
            }
        }

        // 打断正在播的上一段协程时顺手清一下_suppressAmbientFade/_progressFlashRoutine——
        // StopCoroutine不会跑完协程剩下的代码，如果被打断的正好是SwitchQuestRoutine
        // (它自己开头会置true)，不在这里补清的话这个标记会永远卡在true，之后的常规
        // 淡入淡出全部失效；同理，进度闪烁如果正巧跟节点推进撞在一起，也要先停掉，
        // 不能让两个协程同时抢着改同一个TMP_Text的颜色。
        private void StopAllContentRoutines()
        {
            if (_transitionRoutine != null) { StopCoroutine(_transitionRoutine); _transitionRoutine = null; _suppressAmbientFade = false; }
            if (_progressFlashRoutine != null) { StopCoroutine(_progressFlashRoutine); _progressFlashRoutine = null; }
        }

        private const float MinFontSizeRatio = 0.7f;

        // 单纯进度更新(未完成)用的金黄——跟SkyPrisonUIPalette.ColdGreen(完成/成功语义)
        // 分开，用户明确要求区分"有动静了"和"这条真的完成了"两种不同性质的反馈。
        private static readonly Color ProgressFlashColor = new Color(1f, 0.82f, 0.25f, 1f);

        // 目标文字有时候(尤其日/英翻译，比如"交付xx给xx")比中文自然长得多，1200宽的
        // 容器仍然可能装不下——装不下的部分会被ObjectiveClip上的RectMask2D直接裁掉
        // (视觉上表现成"后半句/进度括号凭空消失"，之前就是这么被发现的)。跟节点链
        // 标签(QuestLogController.ShrinkTextToMaxLines)同一个思路，只是这里判断条件
        // 换成"测量宽度"而不是"行数"(HUD只有一行，不能换行)。每次设置文字都先复位
        // 回基准字号再重新量，不然反复调用会越缩越小、缩了就再也放不大。
        private void ApplyLineText(TMP_Text text, string content, float baseFontSize, float minFontSize)
        {
            text.text = content;
            text.fontSize = baseFontSize;
            text.ForceMeshUpdate();
            for (int i = 0; i < 20 && text.fontSize > minFontSize; i++)
            {
                float width = text.GetPreferredValues(content, 0f, 0f).x;
                if (width <= rowWidth) break;

                float size = text.fontSize - 2f;
                if (size < minFontSize) size = minFontSize;
                text.fontSize = size;
                text.ForceMeshUpdate();
            }
        }

        // baseColor→highlightColor→baseColor的颜色脉冲(不是透明度闪烁)——用户明确
        // 问过"闪烁用的是什么颜色"，之前用的是纯透明度忽明忽暗，不够醒目也没有区分
        // 场合的语义。这个方法两处场合共用(进度更新传金黄、节点完成传冷绿)，颜色由
        // 调用方决定，节奏(几次脉冲/多长时间)保持一致。
        // FlashMaxBlend——用户反馈"颜色不用那么深，有意识地感受到就行"，如果k真的
        // 0→1打满会变成纯高亮色，太扎眼。封个顶只淡淡地往高亮色偏一点，不是完全变色。
        private const float FlashMaxBlend = 0.45f;

        private static IEnumerator FlashColorRoutine(TMP_Text text, Color baseColor, Color highlightColor, float duration, int pulses)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float phase = (t / duration) * pulses * Mathf.PI * 2f;
                float k = (0.5f - 0.5f * Mathf.Cos(phase)) * FlashMaxBlend;
                text.color = Color.Lerp(baseColor, highlightColor, k);
                yield return null;
            }
            text.color = baseColor;
        }

        /// <summary>进度括号靠右对齐的位置——用TMP的&lt;pos&gt;标签按容器宽度百分比定位，
        /// 不是靠空格凑。多行时每行各自的括号会因此对齐成一列。</summary>
        /// <summary>
        /// 计数器和目标文字之间的间隔。
        ///
        /// 原来是 &lt;pos=84%&gt;——把计数器钉在行宽的固定比例处，跟文字多长无关。
        /// 好处是多行目标时计数器能对齐成一列，代价是短文字后面空出一大片，
        /// 视觉上计数器和它描述的目标断开了。
        ///
        /// 改成固定间距紧跟文字。em 是相对字号的单位，改字号时间距会跟着走。
        /// </summary>
        private const string ProgressColumnTag = "<space=1.2em>";

        /// <summary>多条件目标要逐条一行、每行右侧各自带自己的进度，不能挤成一行再给
        /// 一个聚合的(0/2)——那样玩家看不出到底是哪一件还没凑齐、还差几个。
        /// 单条件维持原样，只是括号也挪到同一列上，两种情况视觉一致。</summary>
        private static string ResolveObjectiveLine(QuestDefinition quest, QuestObjective objective)
        {
            if (objective.completionConditions.Count > 1)
            {
                var sb = new System.Text.StringBuilder();
                bool first = true;
                foreach (LogicSentenceInstance c in objective.completionConditions)
                {
                    QuestObjectiveTextResolver.ConditionLine line =
                        QuestObjectiveTextResolver.ResolveConditionLine(c, quest.questId, quest.giverNpc, withLinks: false);
                    if (string.IsNullOrWhiteSpace(line.Text)) continue;

                    if (!first) sb.Append('\n');
                    first = false;

                    sb.Append(line.Text);
                    if (line.Progress.Resolved)
                        sb.Append(ProgressColumnTag)
                          .Append('(').Append(Mathf.Min(line.Progress.Current, line.Progress.Target))
                          .Append('/').Append(line.Progress.Target).Append(')');
                }
                if (sb.Length > 0) return sb.ToString();

                // 一条都解析不出来(全是通用句型条件)时退回概括标题+聚合进度。
                QuestObjectiveTextResolver.Progress aggregate = QuestObjectiveTextResolver.GetAggregateProgress(objective, quest.questId);
                string header = QuestObjectiveTextResolver.ResolveTitle(objective, quest.giverNpc);
                if (aggregate.Resolved) header += $"{ProgressColumnTag}({aggregate.Current}/{aggregate.Target})";
                return header;
            }

            string title = QuestObjectiveTextResolver.ResolveTitle(objective, quest.giverNpc);
            QuestObjectiveTextResolver.Progress progress = QuestObjectiveTextResolver.GetProgress(objective, quest.questId);
            if (progress.Resolved) title += $"{ProgressColumnTag}({Mathf.Min(progress.Current, progress.Target)}/{progress.Target})";
            return title;
        }

        /// <summary>目标文字统一走这里——设文字之前先把目标区高度按行数撑开，
        /// 不然多行内容会被ObjectiveClip上的RectMask2D从第二行开始整片裁掉。</summary>
        private void ApplyObjectiveText(TMP_Text text, string content)
        {
            ApplyObjectiveRowHeight(CountLines(content));
            ApplyLineText(text, content, objectiveFontSize, objectiveFontSize * MinFontSizeRatio);
        }

        private static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 1;
            int n = 1;
            foreach (char ch in s) if (ch == '\n') n++;
            return n;
        }

        /// <summary>目标区的高度按行数长——滑入/滑出演出的位移量也用这个值，
        /// 固定用单行高度的话，两行内容滑动时会只走一半、露出上一条的残影。</summary>
        private void ApplyObjectiveRowHeight(int lineCount)
        {
            _rowH = objectiveRowHeight * Mathf.Max(1, lineCount);

            if (_contentRoot != null)
                _contentRoot.sizeDelta = new Vector2(rowWidth, nameRowHeight + rowGap + _rowH);
            if (_objectiveClipRt != null)
                _objectiveClipRt.sizeDelta = new Vector2(0f, _rowH);
            if (_objectiveSlotA != null)
                _objectiveSlotA.sizeDelta = new Vector2(0f, _rowH);
            if (_objectiveSlotB != null)
                _objectiveSlotB.sizeDelta = new Vector2(0f, _rowH);
        }

        // ── 演出①：换追踪任务，整体简单淡出/淡入 ────────────────────────────────

        private IEnumerator SwitchQuestRoutine(string newName, string newObjectiveLine)
        {
            _suppressAmbientFade = true;
            float startAlpha = _currentAlpha;
            float half = questSwitchFadeDuration * 0.5f;
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                _rootGroup.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(t / half));
                yield return null;
            }

            ApplyLineText(_nameText, newName, nameFontSize, nameFontSize * MinFontSizeRatio);
            ApplyObjectiveText(_showingA ? _objectiveTextA : _objectiveTextB, newObjectiveLine);
            (_showingA ? _objectiveSlotA : _objectiveSlotB).anchoredPosition = Vector2.zero;
            (_showingA ? _objectiveGroupA : _objectiveGroupB).alpha = 1f;
            (_showingA ? _objectiveSlotB : _objectiveSlotA).anchoredPosition = new Vector2(0f, -_rowH);
            (_showingA ? _objectiveGroupB : _objectiveGroupA).alpha = 1f;

            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                _rootGroup.alpha = Mathf.Lerp(0f, 1f, Mathf.Clamp01(t / half));
                yield return null;
            }

            _currentAlpha = 1f;
            _targetAlpha = 1f;
            _suppressAmbientFade = false;
            _transitionRoutine = null;
        }

        // ── 演出②：同任务推进节点，旧文字闪烁+上滑淡出，新文字下方滑入 ────────────

        private IEnumerator TransitionObjectiveRoutine(string newObjectiveLine)
        {
            RectTransform oldSlot = _showingA ? _objectiveSlotA : _objectiveSlotB;
            RectTransform newSlot = _showingA ? _objectiveSlotB : _objectiveSlotA;
            CanvasGroup oldGroup = _showingA ? _objectiveGroupA : _objectiveGroupB;
            CanvasGroup newGroup = _showingA ? _objectiveGroupB : _objectiveGroupA;
            TMP_Text oldText = _showingA ? _objectiveTextA : _objectiveTextB;
            TMP_Text newText = _showingA ? _objectiveTextB : _objectiveTextA;

            ApplyObjectiveText(newText, newObjectiveLine);
            newSlot.anchoredPosition = new Vector2(0f, -_rowH);
            newGroup.alpha = 1f;

            // 闪烁：提示玩家"这一条刚刚被满足了"，再开始上滑，不是直接无声无息地划走。
            // 冷绿=这条真的完成了(成功语义)，跟单纯进度更新(FlashColorRoutine传金黄
            // 那处)特意区分开。
            yield return FlashColorRoutine(oldText, Color.white, SkyPrisonUIPalette.ColdGreen, flashDuration, flashPulses);

            Vector2 oldEnd = new Vector2(0f, _rowH);
            float t = 0f;
            while (t < slideDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / slideDuration));
                oldSlot.anchoredPosition = Vector2.Lerp(Vector2.zero, oldEnd, k);
                oldGroup.alpha = Mathf.Lerp(1f, 0f, k);
                newSlot.anchoredPosition = Vector2.Lerp(new Vector2(0f, -_rowH), Vector2.zero, k);
                yield return null;
            }
            oldSlot.anchoredPosition = oldEnd;
            oldGroup.alpha = 0f;
            newSlot.anchoredPosition = Vector2.zero;

            _showingA = !_showingA;
            _transitionRoutine = null;
        }
    }
}
