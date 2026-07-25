using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 角色属性/装备面板 —— 底层框架先行，视觉样式还没定（用户明确要求先不写窗口样式，
/// 布局细节后续再单独确定），这里只做：数据接口打通 + 开关/输入接入 + 能验证功能的
/// 占位视觉（一块空面板 + 一行提示文字，不代表最终设计）。
///
/// 跟背包窗口是同一类"可与其它系统共存的悬浮窗口"，不是暂停菜单那种整屏接管+
/// 冻结时间的模态窗口——所以只设 ExternalBlock，不动 Time.timeScale，这样点开装备槽
/// 弹出背包时两者能一起开着，不用互相关闭。
/// </summary>
public class CharacterPanelController : MonoBehaviour
{
    private const int CharacterPanelSortingOrder = 31900;
    // 背包被这个面板呼出来选装备/快捷物品时，语义上是子级弹窗，该盖在角色面板
    // 上面——允许重叠但不能被压住/拖不动，见 BumpEquipPopupSortingOrder。
    private const int EquipPopupSortingOrder = 31950;

    private static CharacterPanelController _instance;
    public static bool IsOpen => _instance != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        _instance = null;
    }

    public static void Show()
    {
        if (_instance != null) return;

        var go = new GameObject("[CharacterPanel]");
        var ui = go.AddComponent<CharacterPanelController>();
        _instance = ui;
        ui.Build();
    }

    public static void Hide()
    {
        if (_instance != null) _instance.Close();
    }

    public static void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    // ── 运行时引用 ────────────────────────────────────────────────────────
    private UnitHealthController _health;
    private UnitBattleStatRuntime _battleStats;
    private EquipmentWeightController _equipWeight;
    private UnitLoadRuntime _loadRuntime;
    private InventoryRuntime _inventory;
    private EquipmentRuntime Equipment => EquipmentRuntime.Instance;

    private bool _savedExternalBlock;

    // 占位视觉用到的最简引用，真正布局定下来之后这一块会整个重写
    private RectTransform _rootRt;
    private RectTransform _boxRt;
    private TMP_FontAsset _font;
    // "NOT EQUIPPED"/"NOT SET" 这类占位文字用的"东亚重工"风格字体——项目里已经在
    // SaveSlotSelectorUI 的编号徽标上用过同一个资产（文件名因编码问题显示乱码，
    // 用 GUID 精确定位，不靠文件名字符串），这里复用同一个 GUID。
    private TMP_FontAsset _placeholderFont;
    private SkyPrisonBlurUVTracker _blurUvTracker;

    // 文字统一接字典表，别再写死中文——跟设置窗口/背包同一套用法：
    // Resources.Load<UILocalizationTable>("UILocalizationTable") + Get(key, fallback)。
    // 这里的 key 全部是这个面板专属的（charpanel_ 前缀），不跟别处共用同一个 key——
    // 比如 stat_hp_name 在别的地方查出来是"生命值"，但这个面板要的是"HP"这种终端读数
    // 风格的缩写，混用会导致这个面板显示的文字变成别处定义的样子。
    private UILocalizationTable _locTable;

    private string L(string key, string fallback) =>
        _locTable != null ? _locTable.Get(key, fallback) : fallback;

    // 窗口盖住底部按键提示条时的避让——统一走 SkyPrisonFloatingWindowKit，
    // 跟其它悬浮窗共用同一套判定/缓冲区逻辑，不再各写一份。
    private readonly Vector3[] _boxWorldCorners = new Vector3[4];

    // ── 手柄光标导航（装备槽）───────────────────────────────────────────────
    // 装备槽之前完全没有手柄支持，纯靠鼠标 Button.onClick——手柄玩家打不开任何装备槽。
    // 只读 D-pad 虚拟轴 + JoystickButton，不碰任何键盘按键/MoveUp&MoveDown action——
    // 那两个 action 的主键就是 W/S，键鼠玩家本来就能直接用鼠标点任意一个装备槽，不需要
    // 方向键，读了反而会跟角色走路冲突。
    private readonly List<(EquipmentSlotType slot, CharacterPanelEquipRowHover hover)> _equipNavOrder = new();
    private int _equipCursorIndex = -1;
    private float _prevNavDpadY;
    private float _navCooldown;
    private const float NavAxisThreshold = 0.6f;
    private const float NavDelay = 0.20f;
    private const float NavRepeat = 0.12f;

    private static float SafeAxis(string name)
    {
        try { return Input.GetAxisRaw(name); }
        catch { return 0f; }
    }

    private void Update()
    {
        SkyPrisonFloatingWindowKit.DriveHintBarAvoidance(_boxRt, _boxWorldCorners);
        PollEquipInventoryStillOpen();
        HandleEquipCursorNavigation();
        PollEquipPreview();

        // 装备栏文字/颜色偶发对不上（具体是哪个环节的时序问题目前没能精确定位，
        // 两次针对性修复都没能根治）——与其继续猜哪一步抢跑了，不如让这个刷新
        // 本身变成每帧持续校正：只是几个属性赋值+一次小字典遍历，开销可以忽略，
        // 但能保证不管背后是什么时序问题，下一帧必然会被纠正回真实装备状态。
        if (_equipSlotsBuilt) RefreshEquipmentSlotColors();
    }

    // 光标在背包里悬浮武器/装备格子时，实时算"换上它会怎么变化"——轮询式，跟
    // PollEquipInventoryStillOpen 同一个思路，不额外接事件。只在"悬浮的东西变了"
    // 的那一帧才重建属性区（BuildStatsRows 每次都会整个重来一遍，带播放动画，
    // 不能每帧无条件调）。
    private void PollEquipPreview()
    {
        InventoryItemEntry hovered = null;
        if (_equipInventoryWindow != null && _equipInventoryCurrentSlot.HasValue)
        {
            var invController = _equipInventoryWindow.GetComponentInChildren<InventoryWindowController>(true);
            hovered = invController != null ? invController.HoveredEntry : null;
        }

        if (hovered == _previewHoveredEntry) return;
        _previewHoveredEntry = hovered;

        // ItemDefinition.equipment 这个字段所有物品都会自动实例化（不管是不是装备类），
        // 非装备物品从来没人填它，里面全是默认值——EquipmentSlotType.Weapon 又刚好是
        // 枚举默认值0，所以只判断 equipment!=null 形同虚设，必须先确认这真的是装备类
        // 物品（IsEquipmentItem），不然悬浮消耗品/材料这些也会被误判成"能装进当前槽"，
        // 拿一堆全零的假装备数据去算预览。
        // 武器一/武器二在配置表里是同一个槽位（EquipmentSlotType.Weapon），靠
        // TryEquipFromInventory 的 targetSlotOverride 分别塞进两个物理槽——所以这里
        // 不能只比配置表槽位是否完全相等，武器类槽位（Weapon/WeaponSecondary）互相
        // 都算"能装进去"，跟 TryEquipFromInventory 里判断override是否生效的规则一致。
        EquipmentSlotType openSlot = _equipInventoryCurrentSlot.Value;
        EquipmentSlotType itemSlot = hovered?.definition?.equipment.slot ?? EquipmentSlotType.Weapon;
        bool bothWeaponSlots = (openSlot == EquipmentSlotType.Weapon || openSlot == EquipmentSlotType.WeaponSecondary)
                            && (itemSlot == EquipmentSlotType.Weapon || itemSlot == EquipmentSlotType.WeaponSecondary);
        bool valid = hovered?.definition != null && hovered.definition.IsEquipmentItem
                  && (itemSlot == openSlot || bothWeaponSlots);
        _previewDeltas = valid
            ? EquipStatPreview.ComputeDelta(hovered, GetEquippedItem(_equipInventoryCurrentSlot.Value))
            : null;
        BuildStatsRows(animate: false);
    }

    // 装备对比预览箭头用的是真正画出来的图形（Image），不是"→"这种文字符号——字符
    // 再怎么挑字体都是"字"，用户明确要的是图形。之前用实心三角形，左边那条垂直的
    // "底边"看着像多出来一条无关的竖线，改成雪佛龙（">"的线条形态，两条斜线在右边
    // 收成一个尖角，没有垂直背边）更像箭头图标。贴图只在第一次用到时生成一次，
    // 之后全局复用同一张（跟字体无关，不存在缺字问题）。
    private static Sprite _previewArrowSprite;
    private static Sprite GetPreviewArrowSprite()
    {
        if (_previewArrowSprite != null) return _previewArrowSprite;

        const int size = 64;
        const float strokeHalf = 0.11f; // 线条粗细（半宽，0~0.5之间的比例）
        const float amp = 0.42f;        // 左边两个端点张开的幅度（离中线的距离）
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1);
                // 两条斜线：左边张开、右边收拢到中线同一点（尖角），中间没有竖直背边。
                float topLineY    = 0.5f + amp * (1f - nx);
                float bottomLineY = 0.5f - amp * (1f - nx);
                bool inside = Mathf.Abs(ny - topLineY) <= strokeHalf || Mathf.Abs(ny - bottomLineY) <= strokeHalf;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        _previewArrowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return _previewArrowSprite;
    }

    // 数值行的统一构建入口：没有装备预览时就是普通一行数值文字；有预览时在后面接一个
    // 三角形箭头图形 + 变化后的新数值（颜色跟原来一致：变强冷绿/变弱淡红，
    // invertedGood=true 时相反）。rightAligned=true 对应"标签靠左数值靠右"的表格式行
    // （整组贴住数值区右边界）；false 对应"大字冲击"那种左对齐、数值换行紧跟标签的行
    // （箭头贴在数值文字右边接着长）。
    private void BuildStatValueWithPreviewArrow(
        RectTransform valueRt, string baseValue, float fontSize, FontStyles style, bool rightAligned,
        string statKey, bool invertedGood, bool isPercent, float currentValue, Color? baseColor = null)
    {
        // delta 得先给个默认值——短路求值时 TryGetValue 那一段可能压根不会执行，
        // 编译器没法确定 delta 一定被赋值过（CS0165），后面 if(!hasDelta) 里那次读取
        // 编译器看不出跟 hasDelta 的真假绑在一起，必须显式初始化。
        float delta = 0f;
        bool hasDelta = !string.IsNullOrEmpty(statKey) && _previewDeltas != null
            && _previewDeltas.TryGetValue(statKey, out delta) && Mathf.Abs(delta) >= 0.001f;

        var baseText = MakeText(valueRt, "Value", baseValue, fontSize, style, _font);
        baseText.enableWordWrapping = false;
        baseText.raycastTarget = false;
        if (baseColor.HasValue) baseText.color = baseColor.Value;

        if (!hasDelta)
        {
            baseText.alignment = rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.TopLeft;
            return;
        }

        bool isGood = invertedGood ? delta < 0f : delta > 0f;
        Color previewColor = isGood ? PreviewGoodColor : PreviewBadColor;
        float newValue = currentValue + delta;
        string numStr = isPercent ? $"{newValue:0.#}%" : $"{newValue:0.#}";

        float rowH  = fontSize * 1.3f;
        float gap   = fontSize * 0.55f; // 之前0.3f挤得慌，拉开点呼吸感
        float arrowW = fontSize * 0.5f;
        float arrowH = fontSize * 0.42f;

        baseText.ForceMeshUpdate();
        float baseW = baseText.GetPreferredValues(baseValue, 0f, 0f).x;

        var newValueText = MakeText(valueRt, "PreviewValue", numStr, fontSize, style, _font);
        newValueText.color = previewColor;
        newValueText.enableWordWrapping = false;
        newValueText.raycastTarget = false;
        newValueText.ForceMeshUpdate();
        float newW = newValueText.GetPreferredValues(numStr, 0f, 0f).x;

        float groupW = arrowW + gap * 2f + newW;

        var baseRt = baseText.rectTransform;
        var newRt  = newValueText.rectTransform;

        var arrowRt = MakeRect("PreviewArrow", valueRt, Vector2.zero, Vector2.zero);
        arrowRt.sizeDelta = new Vector2(arrowW, arrowH);
        var arrowImg = arrowRt.gameObject.AddComponent<Image>();
        arrowImg.sprite = GetPreviewArrowSprite();
        arrowImg.color = previewColor;
        arrowImg.raycastTarget = false;

        if (rightAligned)
        {
            // 整组（基础值 + 箭头 + 新值）右对齐贴住数值区右边界——从右往左推：
            // newValue贴右边界 → 箭头右边缘留gap → 箭头左边缘再留gap贴base右边缘。
            // （之前这里箭头位置漏算了arrowW、base位置多算了一个gap，导致左右两条缝
            // 宽度对不上，这次重新按"右边界为0"的坐标手动推一遍，两条缝都精确等于gap。）
            baseText.alignment = TextAlignmentOptions.MidlineRight;
            baseRt.anchorMin = baseRt.anchorMax = new Vector2(1f, 0.5f);
            baseRt.pivot     = new Vector2(1f, 0.5f);
            baseRt.sizeDelta = new Vector2(baseW, rowH);
            baseRt.anchoredPosition = new Vector2(-groupW, 0f);

            arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(1f, 0.5f);
            arrowRt.pivot = new Vector2(0f, 0.5f);
            arrowRt.anchoredPosition = new Vector2(-(newW + gap + arrowW), 0f);

            newValueText.alignment = TextAlignmentOptions.MidlineRight;
            newRt.anchorMin = newRt.anchorMax = new Vector2(1f, 0.5f);
            newRt.pivot     = new Vector2(1f, 0.5f);
            newRt.sizeDelta = new Vector2(newW, rowH);
            newRt.anchoredPosition = Vector2.zero;
        }
        else
        {
            // 左对齐：基础值文字钉在数值区左上角原位，箭头和新值依次紧跟在它右边。
            baseText.alignment = TextAlignmentOptions.TopLeft;
            baseRt.anchorMin = baseRt.anchorMax = new Vector2(0f, 1f);
            baseRt.pivot     = new Vector2(0f, 1f);
            baseRt.sizeDelta = new Vector2(baseW, rowH);
            baseRt.anchoredPosition = Vector2.zero;

            arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0f, 1f);
            arrowRt.pivot = new Vector2(0f, 0.5f);
            arrowRt.anchoredPosition = new Vector2(baseW + gap, -rowH * 0.5f);

            newValueText.alignment = TextAlignmentOptions.TopLeft;
            newRt.anchorMin = newRt.anchorMax = new Vector2(0f, 1f);
            newRt.pivot     = new Vector2(0f, 1f);
            newRt.sizeDelta = new Vector2(newW, rowH);
            newRt.anchoredPosition = new Vector2(baseW + gap * 2f + arrowW, 0f);
        }
    }

    // 背包窗口可能不是通过这个面板自己的逻辑关掉的（比如玩家在背包里按了Esc、或者
    // 背包自己的关闭按钮）——这种情况这个面板收不到任何通知，只能每帧自己查一下
    // "我叫出来的那个背包窗口是不是还开着"，不是了就把冻结/变暗状态还原，不然玩家
    // 从背包退出后会发现角色面板卡在"暗着、光标不动"的状态出不来。
    private void PollEquipInventoryStillOpen()
    {
        if (_equipInventoryWindow == null) return;
        var windowManager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        bool stillOpen = windowManager != null && windowManager.IsOpen("inventory");

        // 背包窗口是全局共享的单例（"inventory"这个ID同一时刻只有一个实例）——玩家
        // 自己按快捷键开背包走的是另一条完全独立的代码路径（SkyPrisonPlayerInputRouter），
        // 不会经过这里，也不会清空 _equipInventoryWindow。之前只判断"这个ID的窗口是不是
        // 还开着"，如果玩家用装备槽选完东西后不是靠"再点一次同一个槽"关掉、而是直接按了
        // 快捷键重新打开背包查看别的东西，这里会一直误判成"我的装备选择流程还在进行"，
        // 导致之后随便一次装备变化都被 SetEquipAreaDimmed(true) 错误压暗一整块装备栏。
        // 加一层校验：背包如果还开着，必须确认它当前过滤的槽位还是我这边记的这个槽——
        // 不是的话说明这个共享窗口已经被别的流程接管，跟我们没关系了，一样当作"结束"处理。
        if (stillOpen)
        {
            var controller = _equipInventoryWindow.GetComponentInChildren<InventoryWindowController>(true);
            bool stillOurs = controller != null && controller.ForcedEquipSlot == _equipInventoryCurrentSlot;
            stillOpen = stillOurs;
        }

        if (!stillOpen)
        {
            _equipInventoryWindow = null;
            _equipInventoryCurrentSlot = null;
            _equipInventoryCurrentQuickSlot = null;
            SetEquipAreaDimmed(false);
            _dragHandle?.SetLocked(false);
        }
    }

    // 只认手柄的 D-pad/JoystickButton，完全不碰键盘上下键/WS/MoveUp&MoveDown 这两个
    // action——MoveUp/MoveDown 的主键就是 W/S，键鼠玩家本来就能直接用鼠标点任意一个
    // 装备槽，根本不需要方向键切换；如果这里也读键盘上下键，就会跟角色走路的 W/S
    // 冲突（要么按住 S 移动的同时装备槽光标也在跟着切换，要么角色直接走不了）。
    private void HandleEquipCursorNavigation()
    {
        // 背包窗口（被这个装备槽流程叫出来的）开着的时候，焦点整个交给背包——装备槽这边
        // 的D-pad光标停止响应，不然玩家在背包里挑东西的同时装备槽光标还在自己动，两个
        // "活跃区域"同时抢注意力。
        if (_equipInventoryWindow != null) return;
        if (_equipNavOrder.Count == 0) return;

        bool confirm = Input.GetKeyDown(KeyCode.JoystickButton0);
        if (confirm && _equipCursorIndex >= 0 && _equipCursorIndex < _equipNavOrder.Count)
        {
            OpenInventoryToEquip(_equipNavOrder[_equipCursorIndex].slot);
            return;
        }

        // X / □——跟背包里"整理"用同一个按钮位置（SkyPrisonInventoryGamepad.BtnX），
        // 卸下光标当前停留的这个装备槽。
        bool unequip = Input.GetKeyDown(KeyCode.JoystickButton2);
        if (unequip && _equipCursorIndex >= 0 && _equipCursorIndex < _equipNavOrder.Count)
        {
            TryUnequip(_equipNavOrder[_equipCursorIndex].slot);
            return;
        }

        float dpadY = SafeAxis("DPadVertical");
        bool axisUpEdge   = dpadY >  NavAxisThreshold && _prevNavDpadY <=  NavAxisThreshold;
        bool axisDownEdge = dpadY < -NavAxisThreshold && _prevNavDpadY >= -NavAxisThreshold;
        _prevNavDpadY = dpadY;

        bool up   = axisUpEdge;
        bool down = axisDownEdge;
        bool upHeld   = dpadY > NavAxisThreshold;
        bool downHeld = dpadY < -NavAxisThreshold;

        if (up || down)
        {
            MoveEquipCursor(up ? -1 : 1);
            _navCooldown = NavDelay;
        }
        else if (upHeld || downHeld)
        {
            _navCooldown -= Time.unscaledDeltaTime;
            if (_navCooldown <= 0f)
            {
                MoveEquipCursor(upHeld ? -1 : 1);
                _navCooldown = NavRepeat;
            }
        }
        else
        {
            _navCooldown = 0f;
        }
    }

    private void MoveEquipCursor(int dir)
    {
        if (_equipNavOrder.Count == 0) return;
        int next = _equipCursorIndex < 0
            ? (dir > 0 ? 0 : _equipNavOrder.Count - 1)
            : (_equipCursorIndex + dir + _equipNavOrder.Count) % _equipNavOrder.Count;
        if (next == _equipCursorIndex) return;

        if (_equipCursorIndex >= 0 && _equipCursorIndex < _equipNavOrder.Count)
            _equipNavOrder[_equipCursorIndex].hover?.OnPointerExit(null);

        _equipCursorIndex = next;
        _equipNavOrder[_equipCursorIndex].hover?.OnPointerEnter(null);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
    }

    private void Build()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        _savedExternalBlock = SkyPrisonWindowManager_V1.ExternalBlock;
        SkyPrisonWindowManager_V1.ExternalBlock = true;
        HideCombatHud();

        // 背包窗口的色差效果每隔一段时间会做一次全屏 ScreenCapture 抓屏（真色收差快照，
        // 见 SkyPrisonInventoryChromatic.CapturePump）——这是个不便宜的整屏读回操作。
        // 角色面板开着时点装备槽会把背包也叫出来，两个窗口同时开着，一旦背包那次抓屏
        // 撞上，就会有一帧的显示异常（本面板装备槽背景短暂消失）。背包自己有个
        // PushGlobalSuspend 机制在开关窗动画期间临时挂起这个抓屏、改走不会抓屏的实时
        // 模糊层；角色面板开着的整段时间也算进去，避免两个窗口的开销撞在同一帧。
        SkyPrisonInventoryChromatic.PushGlobalSuspend();

        ResolvePlayerReferences();

        EquipmentRuntime.OnEquipped += HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped += HandleEquipmentChanged;
        QuickSlotRuntime.OnSlotChanged += HandleQuickSlotChanged;
        // 快捷物品行要感知"数量用没了"——绑定关系本身不会因为用光而改变（见
        // QuickSlotRuntime 头注释），只有背包数量真的变了（用掉/捡到）才需要重新刷这一行
        // 的灰显状态，接 InventoryRuntime.OnInventoryChanged，不用每帧轮询。
        if (_inventory != null) _inventory.OnInventoryChanged += RefreshQuickSlotRows;
        if (_health != null)
        {
            _health.OnDamaged += HandleHealthChanged;
            _health.OnHealed += HandleHealthChanged;
        }
        if (_battleStats != null)
            _battleStats.StatsRebuilt += HandleStatsRebuilt;

        BuildPlaceholderVisual();

        SkyPrisonWindowHintBar.GetOrCreate().Show(new[]
        {
            SkyPrisonWindowHint.Icon("mouse/left", "点击", L("ui_hint_open_equip", "选择装备")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/up",     "↕", L("ui_hint_move",        "移动")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A", L("ui_hint_open_equip",  "打开装备")),
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.CharacterPanel, L("ui_hint_close", "关闭")),
        });
    }

    // ── 战斗 HUD 隐藏：分两条路，别再犯"两套系统抢同一个 CanvasGroup 各跑各的淡入淡出"
    // 这个错——之前 HP/LP 条这个目标我自己单独开协程淡出淡入，跟 SkyPrisonWindowManager_V1
    // 处理背包开关时的淡入淡出协程是同一个 CanvasGroup、两条各不知道对方存在的协程，谁跑到
    // 最后算谁赢：背包关闭触发的"淡回"经常在角色面板的"淡出"之后才跑完，表现成"背包关了
    // 再开角色面板，HUD 没被藏住"。现在 HP/LP 条这个目标交给 windowManager 统一的
    // ExternalHudHideRequests 计数器处理（跟背包共用同一条淡入淡出逻辑，不会再打架）；
    // 背包快捷键提示条不在 windowManager 的管辖范围内（它是完全独立的根 Canvas，windowManager
    // 那套只覆盖 HudInstance + PlayerHUDStatusIconBar），继续自己单独淡出淡入。
    private const float HudFadeDuration = 0.15f;
    private CanvasGroup _quickItemFadeTarget;
    private Coroutine _quickItemFadeRoutine;
    private SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1 _windowManagerForHud;

    // 关卡刚加载的极早期（比如自动保存"完成"提示还没消失那几帧），HUD/快捷道具条
    // 这些运行时对象可能还没初始化完，FindObjectOfType 在那个瞬间找到的是 null。
    // 之前只在这里找一次，找不到就永久放弃、这次开面板 HUD 压根不会被藏起来——
    // 表现成"游戏刚进去马上按C，战斗HUD没被收掉"。改成找不到就重试几帧，只要面板
    // 还处于"应该隐藏HUD"的状态（没被关闭/没被下一次开关状态覆盖）就补上这次隐藏。
    private bool _hudShouldBeHidden;
    // ExternalHudHideRequests 是计数器，一次"隐藏请求"必须对应一次"恢复请求"的
    // 递减——TryResolveAndHideHud 现在会在重试期间反复调用（见 RetryResolveHudReferences
    // 的改动），这个标记保证整个 HideCombatHud→RestoreCombatHud 周期里只真正++一次，
    // 不会因为重试跑了60次就把计数器加到60，导致 RestoreCombatHud 一次减不完、
    // HUD 永远卡在隐藏状态。
    private bool _hudHideRequestCounted;

    private void HideCombatHud()
    {
        _hudShouldBeHidden = true;
        TryResolveAndHideHud();
        if (_windowManagerForHud == null || _quickItemFadeTarget == null)
            StartCoroutine(RetryResolveHudReferences());
    }

    private void TryResolveAndHideHud()
    {
        if (_windowManagerForHud == null)
            _windowManagerForHud = FindObjectOfType<SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1>();
        if (_windowManagerForHud != null)
        {
            if (!_hudHideRequestCounted)
            {
                SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.ExternalHudHideRequests++;
                _hudHideRequestCounted = true;
            }
            _windowManagerForHud.RefreshHudVisibility();
        }

        if (_quickItemFadeTarget == null)
        {
            var quickItemStrip = FindObjectOfType<SkyPrisonQuickItemPromptStrip>();
            _quickItemFadeTarget = quickItemStrip != null ? GetOrAddCanvasGroup(quickItemStrip.gameObject) : null;
        }
        FadeQuickItemBar(0f);
    }

    private IEnumerator RetryResolveHudReferences()
    {
        // 最多重试1秒（60帧@60fps），够等关卡初始化完成了；面板已经关闭
        // （_hudShouldBeHidden 变回 false）就直接停手，不要在面板关掉之后突然又把
        // HUD 藏起来。
        //
        // 之前这里"两个引用都找到了就提前退出"——但 _windowManagerForHud 是场景常驻的
        // 管理器，游戏一开始就存在，几乎第一帧就能找到；真正的战斗HUD实例
        // （SkyPrisonRuntimeUIDriver.EnsureBattleHUDInstance 创建）却是揭幕流程里更晚
        // 才生成的。windowManager.ResolveHudGroup() 内部找不到HUD实例时只是静默返回
        // null（不缓存失败），SetHudHidden 什么都不做——但因为这里提前退出了，永远不会
        // 再调用一次 RefreshHudVisibility() 去补这次隐藏，表现成"游戏刚进去第一次开
        // 角色面板，战斗HUD没被藏住"。改成不管两个引用是否已经解析到，都老老实实跑满
        // 重试窗口，每帧都重新调一次 TryResolveAndHideHud()（成本很低），保证HUD实例
        // 真正建好之后这次隐藏请求最终会补上。
        for (int i = 0; i < 60 && _hudShouldBeHidden; i++)
        {
            yield return null;
            TryResolveAndHideHud();
        }
    }

    private void RestoreCombatHud()
    {
        _hudShouldBeHidden = false; // 停掉还在重试的 RetryResolveHudReferences，别关完面板之后又把HUD藏起来

        if (_windowManagerForHud != null && _hudHideRequestCounted)
        {
            SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.ExternalHudHideRequests =
                Mathf.Max(0, SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.ExternalHudHideRequests - 1);
            _hudHideRequestCounted = false;
            _windowManagerForHud.RefreshHudVisibility();
        }

        FadeQuickItemBar(1f);
    }

    private void FadeQuickItemBar(float target)
    {
        if (_quickItemFadeTarget == null) return;
        if (_quickItemFadeRoutine != null) StopCoroutine(_quickItemFadeRoutine);
        _quickItemFadeRoutine = StartCoroutine(FadeHudRoutine(_quickItemFadeTarget, target));
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private IEnumerator FadeHudRoutine(CanvasGroup cg, float target)
    {
        cg.interactable = target > 0.5f;
        cg.blocksRaycasts = target > 0.5f;

        float start = cg.alpha;
        float t = 0f;
        while (t < HudFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            if (cg == null) yield break;
            cg.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / HudFadeDuration));
            yield return null;
        }
        if (cg != null) cg.alpha = target;
    }

    private void ResolvePlayerReferences()
    {
        GameObject player = SkyPrisonPlayerAuthority.CurrentPlayerUnit != null
            ? SkyPrisonPlayerAuthority.CurrentPlayerUnit.gameObject
            : null;
        if (player != null)
        {
            _health = player.GetComponentInChildren<UnitHealthController>(true);
            _battleStats = player.GetComponentInChildren<UnitBattleStatRuntime>(true);
            _equipWeight = player.GetComponentInChildren<EquipmentWeightController>(true);
            _loadRuntime = player.GetComponentInChildren<UnitLoadRuntime>(true);
        }

        _inventory = InventoryRuntimeBootstrap.Instance != null
            ? InventoryRuntimeBootstrap.Instance.Inventory
            : null;
    }

    // ── 对外数据接口（未来 UI 布局直接读这些，不用重新写一遍数据获取逻辑）───────

    public float CurrentHP => _health != null ? _health.CurrentHealth : 0f;
    public float MaxHP => _health != null ? _health.MaxHealth : 0f;
    public float HealthPercent01 => _health != null ? _health.HealthPercent01 : 0f;

    public float CurrentLP => _loadRuntime != null ? _loadRuntime.CurrentLoad : 0f;
    public float MaxLP => _loadRuntime != null ? _loadRuntime.MaxLoad : 0f;

    public EquipmentWeightController.WeightTier WeightTier =>
        _equipWeight != null ? _equipWeight.CurrentTier : EquipmentWeightController.WeightTier.Normal;
    public float TotalEquipWeight => _equipWeight != null ? _equipWeight.TotalEquipWeight : 0f;

    /// <summary>读某个数值键当前生效值（装备/科技树加成都已经算进去了），比如攻击力、
    /// 负重上限——只要 UnitBattleStatRuntime 里有这个 key 就能读到。</summary>
    public float GetFinalStat(string key) => _battleStats != null ? _battleStats.GetFinalValue(key) : 0f;

    /// <summary>指定槽位当前装备的物品，没装备返回 null。</summary>
    public InventoryItemEntry GetEquippedItem(EquipmentSlotType slot) =>
        Equipment != null ? Equipment.GetEquipped(slot) : null;

    /// <summary>卸下指定槽位装备、还回背包；背包满会失败并播放禁止音效。</summary>
    public bool TryUnequip(EquipmentSlotType slot)
    {
        if (Equipment == null || _inventory == null) return false;

        bool ok = Equipment.TryUnequipToInventory(_inventory, slot);
        if (!ok)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return false;
        }

        // 这里是右键卸装/手柄JoystickButton2卸装的入口，跟背包菜单里"卸装"那一行
        // （SkyPrisonInventoryInteraction.UnequipItem）是两条独立代码路径，之前漏了同步
        // 加武器/防具专属音效，一直在放旧的通用Confirm——现在跟那边保持一致。
        bool isWeaponSlot = slot == EquipmentSlotType.Weapon || slot == EquipmentSlotType.WeaponSecondary;
        SkyPrisonSystemSEPlayer.Play(isWeaponSlot ? SkyPrisonSystemSEType.UnequipWeapon : SkyPrisonSystemSEType.UnequipArmor);
        return true;
    }

    // 由某个装备槽/快捷物品行点开的背包窗口——角色面板关闭时要跟着一起关，不能留着
    // 孤零零的背包窗口。装备槽和快捷物品槽共用同一个窗口实例，同一时刻只会有其中
    // 一种过滤生效（EquipmentSlotType 和快捷槽序号互斥，见 InventoryGridView）。
    private GameObject _equipInventoryWindow;
    private EquipmentSlotType? _equipInventoryCurrentSlot; // 当前背包过滤对应哪个装备槽——再点同一个槽就关窗
    private int? _equipInventoryCurrentQuickSlot; // 当前背包过滤对应哪个快捷物品槽——再点同一行就关窗
    private SkyPrisonUIWindowDragHandle _dragHandle; // 呼出背包选装备期间锁定，不让角色面板被拖动

    /// <summary>背包prefab自己的Canvas固定sortingOrder=1100，角色面板是31900——
    /// 背包被这个面板呼出来选装备/快捷物品时，天生会被压在角色面板下面、拖不动。
    /// 只在这个"作为角色面板子级弹窗"的场景把这次实例的排序提到角色面板之上，
    /// 允许重叠但不会被压住。普通从背包快捷键直接打开的背包不受影响。</summary>
    private void BumpEquipPopupSortingOrder(GameObject inventoryWindow)
    {
        if (inventoryWindow == null) return;
        var canvas = inventoryWindow.GetComponentInChildren<Canvas>(true);
        if (canvas == null) return;
        canvas.overrideSorting = true;
        canvas.sortingOrder = EquipPopupSortingOrder;
    }

    /// <summary>打开背包窗口去挑一件装备——用背包已有的"装备"操作装上就行，不用额外
    /// 做一套"选取模式"：背包自己触发 EquipmentRuntime.OnEquipped 时这个面板会自动刷新。
    /// 传入具体点的是哪个槽，好让背包强制只亮起能装进这个槽的物品（武器槽→只亮武器，
    /// 手部槽→只亮手套……）。</summary>
    public void OpenInventoryToEquip(EquipmentSlotType slot)
    {
        var windowManager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (windowManager == null) return;

        // 背包已经是被这个装备槽流程叫出来的：再点同一个槽 = 关掉背包（原本"点一下开、
        // 再点一下关"的语义要保留）；点别的槽 = 只切换过滤目标，背包不用关了重开——那样
        // 每次切槽都要经历一次开关窗动画，体验很差，也没必要为了换个过滤条件重跑一遍。
        if (windowManager.IsOpen("inventory") && _equipInventoryWindow != null)
        {
            if (_equipInventoryCurrentSlot == slot)
            {
                windowManager.Close("inventory");
                _equipInventoryWindow = null;
                _equipInventoryCurrentSlot = null;
                SetEquipAreaDimmed(false);
                _dragHandle?.SetLocked(false);
                return;
            }

            var existingController = _equipInventoryWindow.GetComponentInChildren<InventoryWindowController>(true);
            existingController?.SetForcedEquipSlotFilter(slot);
            _equipInventoryCurrentSlot = slot;
            _equipInventoryCurrentQuickSlot = null;
            return;
        }

        // 背包是玩家自己用热键开的（不是这套装备流程叫出来的）：这种情况维持原本"再点
        // 一下就关掉"的语义，不去抢玩家自己打开的背包。
        if (windowManager.IsOpen("inventory"))
        {
            windowManager.Close("inventory");
            _equipInventoryWindow = null;
            _equipInventoryCurrentSlot = null;
            return;
        }

        GameObject prefab = Resources.Load<GameObject>("UI/Window/PF_SkyPrisonInventory");
        if (prefab == null) return;

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
        _equipInventoryWindow = windowManager.Open(prefab);
        BumpEquipPopupSortingOrder(_equipInventoryWindow);

        var invController = _equipInventoryWindow != null
            ? _equipInventoryWindow.GetComponentInChildren<InventoryWindowController>(true)
            : null;
        invController?.SetForcedEquipSlotFilter(slot);
        _equipInventoryCurrentSlot = slot;
        _equipInventoryCurrentQuickSlot = null;
        // 只有真的打开成功才压暗——之前这里没判空就直接压暗，Open() 静默失败时
        // （比如你截图里那次）会出现"背包没弹出来、但角色面板已经暗掉"的诡异状态，
        // 掩盖了 Open() 失败这个真正的问题。
        if (_equipInventoryWindow != null)
        {
            SetEquipAreaDimmed(true);
            // 背包这次是子级弹窗，允许跟角色面板重叠显示，但角色面板本身不能再被
            // 拖动——不然拖来拖去容易让人搞不清两个窗口谁跟着谁动。
            _dragHandle?.SetLocked(true);
        }
        else
            Debug.LogWarning("[CharacterPanel] windowManager.Open(\"inventory\" prefab) 返回了 null，背包没打开成功。");
    }

    /// <summary>打开背包窗口去挑一件消耗品绑定到快捷物品槽——跟 OpenInventoryToEquip
    /// 是同一套逻辑，只是过滤条件换成"可用消耗品、非复活道具"（SetForcedQuickSlotFilter），
    /// 绑定动作本身在背包的物品右键菜单里（"指定为快捷物品"，见
    /// SkyPrisonInventoryInteraction.AssignQuickSlot），不在这里直接处理。</summary>
    public void OpenInventoryToQuickSlot(int quickSlotIndex)
    {
        var windowManager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (windowManager == null) return;

        if (windowManager.IsOpen("inventory") && _equipInventoryWindow != null)
        {
            if (_equipInventoryCurrentQuickSlot == quickSlotIndex)
            {
                windowManager.Close("inventory");
                _equipInventoryWindow = null;
                _equipInventoryCurrentQuickSlot = null;
                SetEquipAreaDimmed(false);
                _dragHandle?.SetLocked(false);
                return;
            }

            var existingController = _equipInventoryWindow.GetComponentInChildren<InventoryWindowController>(true);
            existingController?.SetForcedQuickSlotFilter(quickSlotIndex);
            _equipInventoryCurrentQuickSlot = quickSlotIndex;
            _equipInventoryCurrentSlot = null;
            return;
        }

        if (windowManager.IsOpen("inventory"))
        {
            windowManager.Close("inventory");
            _equipInventoryWindow = null;
            _equipInventoryCurrentQuickSlot = null;
            return;
        }

        GameObject prefab = Resources.Load<GameObject>("UI/Window/PF_SkyPrisonInventory");
        if (prefab == null) return;

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
        _equipInventoryWindow = windowManager.Open(prefab);
        BumpEquipPopupSortingOrder(_equipInventoryWindow);

        var invController = _equipInventoryWindow != null
            ? _equipInventoryWindow.GetComponentInChildren<InventoryWindowController>(true)
            : null;
        invController?.SetForcedQuickSlotFilter(quickSlotIndex);
        _equipInventoryCurrentQuickSlot = quickSlotIndex;
        _equipInventoryCurrentSlot = null;
        if (_equipInventoryWindow != null)
        {
            SetEquipAreaDimmed(true);
            _dragHandle?.SetLocked(true);
        }
        else
            Debug.LogWarning("[CharacterPanel] windowManager.Open(\"inventory\" prefab) 返回了 null，背包没打开成功。");
    }

    // 背包（被装备槽流程叫出来的那次）打开期间，角色面板的属性区/装备区整体压暗、
    // 停止接收射线（鼠标点不到、手柄光标也冻结在 HandleEquipCursorNavigation 里），
    // 视觉上明确告诉玩家"现在操作焦点在背包"。
    private void SetEquipAreaDimmed(bool dimmed)
    {
        float alpha = dimmed ? 0.35f : 1f;
        if (_statsRootCg != null)
        {
            _statsRootCg.alpha = alpha;
            _statsRootCg.blocksRaycasts = !dimmed;
            _statsRootCg.interactable = !dimmed;
        }
        if (_equipColumnCg != null)
        {
            _equipColumnCg.alpha = alpha;
            _equipColumnCg.blocksRaycasts = !dimmed;
            _equipColumnCg.interactable = !dimmed;
        }
    }

    // ── 事件回调：数值/装备变了就整块重建一次属性区（不做增量刷新，逻辑简单，
    // 装备切换这种低频操作重建一次没有性能问题）。重建时不重放开窗动画，直接是
    // 展开完成后的最终状态。────────────────────────────────────────────────

    // 装备槽（右侧圆形全息槽）单独只在装备真的变化时重建——之前 HP/属性一变就连
    // 装备槽一起摧毁重建，如果角色身上挂着持续跳的 HP/LP 恢复，鼠标正悬停旋转到
    // 一半的弧线会被这个重建打断、瞬间弹回初始角度再重新转起来，看着就是"转一圈
    // 抽搐一下"。HP/属性变化只需要重建文字这几行，跟装备槽的悬停旋转状态无关。
    private void HandleEquipmentChanged(EquipmentSlotType slot, InventoryItemEntry entry)
    {
        BuildStatsAndEquipment(animate: false);
        // 卸装那一刻文字/颜色偶发不同步（文字已经变成"NOT EQUIPPED"，颜色却还是
        // 已装备时的亮白色）——保险起见重建完之后再显式按当前真实装备状态刷一遍
        // 颜色，两边永远用同一份判断结果，不会再看到文字跟颜色对不上的状态。
        RefreshEquipmentSlotColors();
        // BuildStatsRows 每次都整块销毁重建 _statsRoot（连带新的 CanvasGroup，alpha
        // 默认1）——如果玩家是在筛选背包开着的状态下装备了东西触发的这次重建，压暗
        // 状态得重新套一遍，不然属性区会突然跳回全亮，跟背包还开着的事实不符。
        if (_equipInventoryWindow != null) SetEquipAreaDimmed(true);

        // 两个武器槽都有装备时，卸下当前生效那把会连带触发 EquipmentRuntime 内部
        // "自动切到另一把继续用"的连锁（切 ActiveWeaponSlot、通知战斗模组换武器），
        // 这条连锁在这次事件回调之后才跑完——极偶尔会跟这里的刷新前后脚发生，导致
        // 文字和颜色对不上（重新开关面板会恢复正常，说明只是这一帧的时序问题，不是
        // 数据本身错了）。补一次下一帧的刷新，把连锁结束之后真正稳定的状态再校一遍。
        StartCoroutine(RefreshEquipmentSlotColorsNextFrame());
    }

    private System.Collections.IEnumerator RefreshEquipmentSlotColorsNextFrame()
    {
        yield return null;
        RefreshEquipmentSlotColors();
    }
    private void HandleQuickSlotChanged(int index, ItemDefinition definition) => RefreshQuickSlotRows();
    private void HandleHealthChanged(UnitHealthController source, float amount) => BuildStatsRows(animate: false);
    private void HandleStatsRebuilt() => BuildStatsRows(animate: false);

    // ── 属性/装备展示：核心/物理/属性 三个区块 + 装备列表，统一用"节点方块+文字"
    // 这套视觉——节点方块贴着左侧那条常驻竖线，文字紧跟其后。开窗时先播一次
    // "扫描线从右扫到左→停在左边→逐行弹出节点+同步淡入文字"的揭示动画；后续
    // 数据变化时（装备/血量/属性重算）直接用最终状态重建，不重放这套动画。────

    // 字号对标背包的 28pt（SkyPrisonFloatingWindowKit.PrimaryFontSize），不再是之前
    // 自己放大的 56~64pt——尺寸相应跟着字号缩回去，不然一堆大留白配小字看着很空。
    // 装备槽换成右侧圆形全息槽之后不再占用文字区，右边给装备槽留出一条
    // EquipColumnReserve 宽的带子，属性两列布局宽度相应收窄。
    // 面板要求横向比纵向宽——之前 1000x1200 是竖着的，改成 1500x950 明显偏横版。
    // 布局尺寸跟着 kit 的 StandardScaleMultiplier(1.3) 一起放大——字号从28pt变成36.4pt
    // 之后，还按老尺寸给的行高/列宽会显得太挤，这几个内部排布数字乘同一个倍率，跟字号
    // 保持相同的放大节奏（Box 本身的 1500x950 是你直接指定的目标大小，不跟着这个倍率变）。
    private const float M                    = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;
    // W：角色信息面板自己额外再放大一档（1.4倍），只影响这个窗口的整体尺寸和内部排布
    // 间距，不动字号（字号走 kit 共享标准，不应该这个窗口单独变大，不然又跟背包对不上）。
    private const float W                    = 1.4f;
    // 窗口大小恢复回原来的规格，不再为了百分比条额外加宽/加高。
    private const float BoxWidth             = 1700f * W;
    private const float BoxHeight            = 1050f * W;
    private const float StatRowHeight        = 46f * M * W;
    private const float StatGroupGap         = 24f * M * W;
    private const float StatNodeSize         = 12f * M * W;
    // 之前是裸数字 0.5，没有跟着 M*W 一起缩放——在4K基准画布缩小到实际屏幕分辨率后
    // 直接被缩没了，看着像整条线消失。改成跟其它尺寸一样乘 M*W，保证任何分辨率下
    // 都还有实际可见的粗细。
    // 2.0在4K原生屏幕上实际渲染出来约3.6px，用户反馈太粗。降到1.4（4K下约2.5px，
    // 1080p缩放后约1.3px）——仍然比0.9那版粗到能在非4K屏幕看见，但不到3px，视觉上
    // 是"细线"而不是"条"。
    private const float StatLineWidth        = 1.4f * M * W;
    private const float StatLineInset        = 8f * M * W;   // 扫描线离内容区左边的距离
    private const float StatTextInset        = 28f * M * W;  // 文字离扫描线的距离
    // 装备区改成列表式布局后一度把这两列压太窄——实际排完发现右边留白还够，往回放宽。
    private const float StatColumnWidth      = 350f * M * W;
    private const float StatColumnGap        = 50f * M * W;

    // 核心区（HP/LP/攻击/防御）用"大字冲击"风格——标签一行小字在上，数值另起一行
    // 用更大字号、左对齐紧跟标签下方，不再是标签靠左数值靠右的表格式单行。这里用固定值，
    // 不跟着 StatRowHeight 走（乘数关系会被 StatRowHeight 的调整连带放大到离谱）。
    private const float CoreStatRowHeight    = 100f * M * W;
    private const float CoreLabelFontSize    = SkyPrisonFloatingWindowKit.DecorativeFontSize;
    private const float CoreValueFontSize    = SkyPrisonFloatingWindowKit.PrimaryFontSize * 2.2f;
    private const float StatLabelWidth       = 140f * M * W; // 标签固定宽度，数值在剩余空间里右对齐
    private const float EquipColumnReserve   = 480f * M * W; // 右侧留给装备列表区的宽度（列表式布局比原来的圆形槽位列宽很多）
    private const float ContentAreaWidth     = BoxWidth - 40f - EquipColumnReserve; // Box 宽度减左边留白、右边装备槽预留
    // 标题栏预留 = kit 的标题栏高度 + 一点间距，别再自己写死一个数字跟标题栏脱钩——
    // 之前这里固定写 88，标题栏改成对标背包的 64 之后这个数字忘了同步，内容区顶部
    // 会跟标题栏对不上。
    private const float TopReserve           = SkyPrisonFloatingWindowKit.TitleBarHeight + 16f;
    // Box 高度(1200) 减标题栏预留 减底部留白(40)——这是内容区在竖直方向能用的全部
    // 高度，跟属性行数无关。扫描线长度、装备槽纵向排布都用这个数，保证不会溢出到
    // 标题栏/底边之外。
    private const float StatsAreaHeight      = BoxHeight - TopReserve - 40f;
    private const float ScanSweepDuration    = 0.22f;
    private const float NodeRevealStagger    = 0.05f;  // 每一行之间的间隔
    private const float GroupBoundaryExtraDelay = 0.12f; // 新区块开头额外多等一点，视觉上跟上一区块断开

    private readonly struct StatEntry
    {
        public readonly string Label;
        public readonly string Value;
        // 非 null = 这一项是百分比数值，数值区用分段条渲染（不再显示 Value 那串文字），
        // 数值本身按 raw 百分比存（可能超过100，比如暴击倍率150%）。
        public readonly float? Percent;
        // 装备对比预览用：这一项对应的底层属性key（GetFinalStat/StatModifier同一套key），
        // 用来去 _previewDeltas 里查"如果换上悬浮中的装备会变化多少"。null=这一项不参与
        // 预览（大部分非装备相关的显示项，比如HP/LP）。
        public readonly string StatKey;
        // 大部分属性"数值升高=变强"（冷绿），但负暴击率/负暴击伤害是"数值升高=变弱"
        // （这两个描述的是"被打出负暴击的概率/伤害倍率"，本身就是负面效果，数值越高越糟）
        // ——true 时颜色规则反过来判。
        public readonly bool InvertedGood;
        // 装备对比预览要用"当前显示的这个数字"去加 _previewDeltas 里的偏移量，算出
        // "换上悬浮中的装备之后会变成多少"——Value 是格式化好的文字（可能带百分号/
        // 已经减过100基准），没法从字符串反解，所以额外存一份原始数值。只有 StatKey
        // 非空的行才有意义，其它行不参与预览、留 0 即可。
        public readonly float RawValue;
        // Value 文字本身带不带"%"——BuildDelta100 那批（暴击伤害/负暴击伤害）虽然不走
        // Percent 分段条，但显示的文字里手动拼了"%"（"+50%"这种），预览箭头的新数值
        // 也得带上这个后缀，不然会出现"-50% > 30"这种基础值有%、新值没有的不一致。
        public readonly bool IsPercentText;

        public StatEntry(string label, string value) { Label = label; Value = value; Percent = null; StatKey = null; InvertedGood = false; RawValue = 0f; IsPercentText = false; }
        public StatEntry(string label, string value, float percent) { Label = label; Value = value; Percent = percent; StatKey = null; InvertedGood = false; RawValue = 0f; IsPercentText = false; }
        public StatEntry(string label, string value, string statKey, bool invertedGood = false) { Label = label; Value = value; Percent = null; StatKey = statKey; InvertedGood = invertedGood; RawValue = 0f; IsPercentText = false; }
        public StatEntry(string label, string value, float percent, string statKey, bool invertedGood = false) { Label = label; Value = value; Percent = percent; StatKey = statKey; InvertedGood = invertedGood; RawValue = 0f; IsPercentText = false; }
        public StatEntry(string label, string value, string statKey, float rawValue, bool invertedGood, bool isPercentText = false) { Label = label; Value = value; Percent = null; StatKey = statKey; InvertedGood = invertedGood; RawValue = rawValue; IsPercentText = isPercentText; }
    }

    // 装备对比预览：光标悬浮在能装进当前打开槽位的武器/装备上时，算出"换上它之后每项
    // 属性会变化多少"，key跟GetFinalStat/StatModifier同一套（"atk"、"critRate"……）。
    // null = 当前没有预览（没悬浮/悬浮的东西装不进这个槽）。
    private Dictionary<string, float> _previewDeltas;
    private InventoryItemEntry _previewHoveredEntry;
    private static readonly Color PreviewGoodColor = SkyPrisonUIPalette.ColdGreen;
    private static readonly Color PreviewBadColor = new Color(0.85f, 0.40f, 0.38f, 1f); // 跟AutoSaveIndicatorUI.WarnRed同一个"系统淡红"

    private RectTransform _statsRoot;
    private CanvasGroup _statsRootCg;
    private RectTransform _scanLineRt;
    private readonly List<RectTransform> _rowNodes = new List<RectTransform>();
    private readonly List<CanvasGroup> _rowTextGroups = new List<CanvasGroup>();
    private readonly List<bool> _rowIsGroupStart = new List<bool>();
    private float _statsContentHeight;
    private Coroutine _statsRevealRoutine;

    // 装备变化时两块都要重来（装备本身也可能改属性数值）；HP/属性变化只调 BuildStatsRows。
    private void BuildStatsAndEquipment(bool animate)
    {
        BuildStatsRows(animate);
        BuildEquipmentSlots();
    }

    private void BuildStatsRows(bool animate)
    {
        if (_statsRevealRoutine != null) { StopCoroutine(_statsRevealRoutine); _statsRevealRoutine = null; }
        if (_statsRoot != null) Destroy(_statsRoot.gameObject);
        _rowNodes.Clear();
        _rowTextGroups.Clear();
        _rowIsGroupStart.Clear();

        var rootRt = MakeRect("StatsRoot", _boxRt, Vector2.zero, Vector2.one);
        rootRt.offsetMin = new Vector2(40f, 40f);
        rootRt.offsetMax = new Vector2(-EquipColumnReserve, -TopReserve); // 顶上让出标题栏高度，右边让出装备槽
        _statsRoot = rootRt;
        _statsRootCg = rootRt.gameObject.AddComponent<CanvasGroup>();

        _scanLineRt = MakeRect("ScanLine", rootRt, new Vector2(0f, 1f), new Vector2(0f, 1f));
        _scanLineRt.pivot = new Vector2(0.5f, 1f);
        var scanLineImg = _scanLineRt.gameObject.AddComponent<Image>();
        // 跟装备区外框线（AddOutline，白色25%透明度）像素宽度其实一样，之前满不透明的
        // 白色看着还是明显更粗——纯白满不透明对比度太高，视觉上会比同宽度、低透明度
        // 的线"胖"一圈。降到跟外框线接近的透明度，两条线才看着一样细。
        scanLineImg.color = new Color(1f, 1f, 1f, 0.5f);
        scanLineImg.raycastTarget = false;

        var groups = BuildAllGroups();

        float y = 0f;
        bool firstGroup = true;
        foreach (var group in groups)
        {
            if (group.rows.Count == 0) continue;
            if (!firstGroup) y += StatGroupGap;
            firstGroup = false;

            bool bigStyle = group.name == "core";
            float rowHeight = bigStyle ? CoreStatRowHeight : StatRowHeight;
            for (int i = 0; i < group.rows.Count; i++)
            {
                var row = group.rows[i];
                CreateStatRow(rootRt, y, row.a, row.b, isGroupStart: i == 0, bigStyle);
                y += rowHeight;
            }
        }
        _statsContentHeight = y;

        _scanLineRt.sizeDelta = new Vector2(StatLineWidth, StatsAreaHeight);

        if (animate)
        {
            _scanLineRt.anchoredPosition = new Vector2(ContentAreaWidth, 0f);
            for (int i = 0; i < _rowNodes.Count; i++)
            {
                _rowNodes[i].localScale = Vector3.zero;
                _rowTextGroups[i].alpha = 0f;
            }
            _statsRevealRoutine = StartCoroutine(StatsRevealAnimation());
        }
        else
        {
            _scanLineRt.anchoredPosition = new Vector2(StatLineInset, 0f);
            for (int i = 0; i < _rowNodes.Count; i++)
            {
                _rowNodes[i].localScale = Vector3.one;
                _rowTextGroups[i].alpha = 1f;
            }
        }
    }

    private void CreateStatRow(RectTransform parent, float topY, StatEntry a, StatEntry? b, bool isGroupStart, bool bigStyle = false)
    {
        float rowHeight = bigStyle ? CoreStatRowHeight : StatRowHeight;
        var rowRt = MakeRect("Row", parent, new Vector2(0f, 1f), new Vector2(1f, 1f));
        rowRt.pivot = new Vector2(0f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -topY);
        rowRt.sizeDelta = new Vector2(0f, rowHeight);

        var nodeRt = MakeRect("Node", rowRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        nodeRt.pivot = new Vector2(0.5f, 0.5f);
        nodeRt.anchoredPosition = new Vector2(StatLineInset, 0f);
        nodeRt.sizeDelta = new Vector2(StatNodeSize, StatNodeSize);
        var nodeImg = nodeRt.gameObject.AddComponent<Image>();
        nodeImg.color = Color.white;
        nodeImg.raycastTarget = false;

        var textRt = MakeRect("Text", rowRt, Vector2.zero, Vector2.one);
        textRt.offsetMin = new Vector2(StatTextInset, 0f);
        textRt.offsetMax = Vector2.zero;
        var textCg = textRt.gameObject.AddComponent<CanvasGroup>();

        // 单列行（比如暴击倍率/负暴击倍率）之前用整行宽度当列宽，数值右对齐的位置
        // 就跟着拉到了行尾，跟两列行里"a"列数值右对齐的位置对不上——整份属性表看下来
        // 数字完全不在一条竖线上。改成单列行也用跟两列行"a"列一样的窄列宽，所有数值
        // （不管百分号还是纯数字）右边缘都对在同一条竖线上。
        CreateStatColumn(textRt, 0f, a, StatColumnWidth, bigStyle);
        if (b.HasValue)
            CreateStatColumn(textRt, StatColumnWidth + StatColumnGap, b.Value, StatColumnWidth, bigStyle);

        _rowNodes.Add(nodeRt);
        _rowTextGroups.Add(textCg);
        _rowIsGroupStart.Add(isGroupStart);
    }

    // 标签靠左、数值靠右分两个文本对象——之前拼成一个"标签: 数值"字符串，标签长短
    // 不一（"HP" vs "暴击倍率"）导致数值完全对不齐。不用冒号分隔，靠左右对齐本身
    // 就能分清标签和数值，视觉上更像一份对齐工整的读数表。
    private void CreateStatColumn(RectTransform parent, float xOffset, StatEntry entry, float columnWidth, bool bigStyle = false)
    {
        if (bigStyle)
        {
            CreateBigStatColumn(parent, xOffset, entry, columnWidth);
            return;
        }

        var colRt = MakeRect("Col", parent, new Vector2(0f, 0f), new Vector2(0f, 1f));
        colRt.pivot = new Vector2(0f, 0.5f);
        colRt.anchoredPosition = new Vector2(xOffset, 0f);
        colRt.sizeDelta = new Vector2(columnWidth, 0f);

        var labelRt = MakeRect("Label", colRt, new Vector2(0f, 0f), new Vector2(0f, 1f));
        labelRt.pivot = new Vector2(0f, 0.5f);
        labelRt.sizeDelta = new Vector2(StatLabelWidth, 0f);
        var labelText = MakeText(labelRt, "Label", entry.Label, SkyPrisonFloatingWindowKit.PrimaryFontSize, FontStyles.Normal, _font);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.enableWordWrapping = false;
        labelText.raycastTarget = false;

        var valueRt = MakeRect("Value", colRt, new Vector2(0f, 0f), new Vector2(1f, 1f));
        valueRt.offsetMin = new Vector2(StatLabelWidth, 0f);
        valueRt.offsetMax = Vector2.zero;

        if (entry.Percent.HasValue)
        {
            // 用 TMP 的 textBounds/textInfo 去量文字实际笔画底边，来回踩了两次坑
            // （世界坐标当本地坐标用；换算成本地又搞错了参照点是几何中心还是底边），
            // 而且这行字所在的容器比字本身高很多，量出来的"精确底边"其实没那么可靠。
            // 改成一个可以直接肉眼调的固定量——先在"整体居中"的基础上再往上挪
            // PercentBarBaselineLift 这么多像素，跟用户来回确认微调，不再猜运行时坐标。
            float blockHeight = PercentBarSegmentHeight + PercentBarNumberGap + PercentBarNumberHeight;
            float blockBottomY = Mathf.Max(0f, (valueRt.rect.height - blockHeight) * 0.5f) + PercentBarBaselineLift;
            BuildPercentBar(valueRt, columnWidth - StatLabelWidth, entry.Percent.Value, blockBottomY, entry.StatKey, entry.InvertedGood);
            return;
        }

        BuildStatValueWithPreviewArrow(valueRt, entry.Value, SkyPrisonFloatingWindowKit.PrimaryFontSize, FontStyles.Normal,
            rightAligned: true, entry.StatKey, entry.InvertedGood, entry.IsPercentText, entry.RawValue);
    }

    // 核心区"大字冲击"样式：标签一行小字在上，数值换行、用更大字号、左对齐紧跟在
    // 标签下方——不是标签靠左数值靠右的表格式单行，是参考图那种"总合评价"式排版。
    private void CreateBigStatColumn(RectTransform parent, float xOffset, StatEntry entry, float columnWidth)
    {
        var colRt = MakeRect("Col", parent, new Vector2(0f, 0f), new Vector2(0f, 1f));
        colRt.pivot = new Vector2(0f, 0.5f);
        colRt.anchoredPosition = new Vector2(xOffset, 0f);
        colRt.sizeDelta = new Vector2(columnWidth, 0f);

        var labelRt = MakeRect("Label", colRt, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
        var labelText = MakeText(labelRt, "Label", entry.Label, CoreLabelFontSize, FontStyles.Normal, _font);
        labelText.alignment = TextAlignmentOptions.BottomLeft;
        labelText.enableWordWrapping = false;
        labelText.color = new Color(1f, 1f, 1f, 0.6f);
        labelText.raycastTarget = false;

        var valueRt = MakeRect("Value", colRt, new Vector2(0f, 0f), new Vector2(1f, 0.5f));
        BuildStatValueWithPreviewArrow(valueRt, entry.Value, CoreValueFontSize, FontStyles.Bold,
            rightAligned: false, entry.StatKey, entry.InvertedGood, entry.IsPercentText, entry.RawValue);
    }

    private const int PercentBarSegmentCount = 10;
    private const float PercentBarSegmentGap = 3f * M * W;
    private const float PercentBarSegmentHeight = SkyPrisonFloatingWindowKit.PrimaryFontSize * 0.6f;
    private const float PercentBarNumberFontSize = SkyPrisonFloatingWindowKit.DecorativeFontSize * 1.25f;
    private const float PercentBarNumberHeight = PercentBarNumberFontSize * 1.3f;
    private const float PercentBarNumberGap = 2f * M * W; // 数字紧贴在条上方，别隔太开
    private const float PercentBarBaselineLift = 10f * M * W; // 在居中基础上再往上挪，贴近文字视觉底边，肉眼调

    // 百分比数值用分段条代替数字——一格=10%，超过100%时10格全部点亮并且换成更亮的
    // 颜色表示"已经封顶"（不做超过10格的第二条，那样看着会很乱）。数值挪到条的正上方
    // （不再占在条右边），这样条能用满整个数值区宽度，不用另外分出一块给数字。
    private void BuildPercentBar(RectTransform valueRt, float valueAreaWidth, float percent, float blockBottomY, string statKey = null, bool invertedGood = false)
    {
        bool overflow = percent > 100.0001f;
        int filled = overflow
            ? PercentBarSegmentCount
            : Mathf.Clamp(Mathf.RoundToInt(percent / 100f * PercentBarSegmentCount), 0, PercentBarSegmentCount);

        float barWidth = valueAreaWidth;
        float segW = (barWidth - (PercentBarSegmentCount - 1) * PercentBarSegmentGap) / PercentBarSegmentCount;

        // blockBottomY 是调用方量出来的、同一行标签文字笔画实际下边界（不是行高居中，
        // 也不是死贴在行最底部）——条的底边贴这个Y，才能跟旁边文本列的文字底边真正持平。
        var barRt = MakeRect("Bar", valueRt, new Vector2(0f, 0f), new Vector2(0f, 0f));
        barRt.pivot = new Vector2(0f, 0f);
        barRt.anchoredPosition = new Vector2(0f, blockBottomY);
        barRt.sizeDelta = new Vector2(barWidth, PercentBarSegmentHeight);

        // 数字紧贴在条正上方（不是钉在数值区最顶上）——两者中间不留一大截空隙。
        var numberRt = MakeRect("Number", valueRt, new Vector2(0f, 0f), new Vector2(1f, 0f));
        numberRt.pivot = new Vector2(1f, 0f);
        numberRt.anchoredPosition = new Vector2(0f, blockBottomY + PercentBarSegmentHeight + PercentBarNumberGap);
        numberRt.sizeDelta = new Vector2(0f, PercentBarNumberHeight);
        // 溢出时直接显示真实完整数值（比如150%），不再显示"超出封顶多少"的+50%——
        // 像暴击倍率这种本来就是"越高越好"的加成型数值，超过100%是正常表现，不是
        // 真的"顶到上限浪费掉了"，显示完整数值更直观。
        // 之前"静态显示的数字/进度条不区分好坏属性"这条规则对负暴击率/负暴击伤害这类
        // "数值越高越差"的属性不成立——用统一的冷绿色填充条，视觉上看起来完全像是
        // 正面加成，用户反馈这明显是误导（负暴击率30%实际是亏损，不该跟暴击率一样
        // 显示成绿色）。invertedGood 的条改用暖红色，其它正常属性维持原样。
        Color overflowColor = new Color(1f, 1f, 1f, 0.95f);
        Color fillColor = invertedGood ? SkyPrisonUIPalette.WarmRed : SkyPrisonUIPalette.ColdGreen;
        Color numberColor = overflow ? overflowColor : new Color(1f, 1f, 1f, 0.75f);
        BuildStatValueWithPreviewArrow(numberRt, $"{percent:0.#}%", PercentBarNumberFontSize, FontStyles.Normal,
            rightAligned: true, statKey, invertedGood, isPercent: true, percent, numberColor);

        Color litColor = overflow ? overflowColor : fillColor;
        Color dimColor = new Color(1f, 1f, 1f, 0.12f);

        for (int i = 0; i < PercentBarSegmentCount; i++)
        {
            var segRt = MakeRect("Seg" + i, barRt, Vector2.zero, Vector2.one);
            segRt.anchorMin = segRt.anchorMax = new Vector2(0f, 0.5f);
            segRt.pivot = new Vector2(0f, 0.5f);
            segRt.sizeDelta = new Vector2(segW, PercentBarSegmentHeight);
            segRt.anchoredPosition = new Vector2(i * (segW + PercentBarSegmentGap), 0f);
            var segImg = segRt.gameObject.AddComponent<Image>();
            segImg.color = i < filled ? litColor : dimColor;
            segImg.raycastTarget = false;
        }
    }

    // 每个区块的配对关系是明确指定的，不再是"列表按顺序两两自动配对"——
    // HP/LP 各自单独一行（不放一行），攻击/防御一行；属性区按"同一种属性的伤害+抗性"
    // 配对（比如电磁伤害跟电磁抗性一行），不是按伤害/抗性分两批各自排列。
    private (string name, List<(StatEntry a, StatEntry? b)> rows)[] BuildAllGroups()
    {
        // 核心区：始终显示，不管数值是不是0——这几个是角色的基本身份数值。
        var core = new List<(StatEntry, StatEntry?)>
        {
            (new StatEntry(L("charpanel_stat_hp", "HP"), $"{CurrentHP:0}/{MaxHP:0}"),
             new StatEntry(L("charpanel_stat_lp", "LP"), $"{CurrentLP:0}/{MaxLP:0}")),
            (RawEntry(L("charpanel_stat_atk", "攻击"), GetFinalStat("atk"), "atk"),
             RawEntry(L("charpanel_stat_def", "防御"), GetFinalStat("def"), "def")),
        };
        float atkSpeed = GetFinalStat("atkSpeed");
        if (Mathf.Abs(atkSpeed) > 0.001f)
            core.Add((new StatEntry(L("charpanel_stat_atkspeed", "攻速"), $"{atkSpeed:0.#}"), null));

        // 物理区：暴击类是对所有伤害类型统一生效的通用加成，只有非零才展示（大部分角色
        // 没吃到这些加成，不用占地方）；下面三种物理类型抗性用户要求始终显示，不管是不是0。
        // 暴击率/负暴击率是真正的0-100%概率，做成分段条有意义；暴击伤害/负暴击伤害是
        // 倍率（基准是100%=原本伤害，没有"越接近100%越好/封顶"这种概念，150%就是单纯的
        // 1.5倍，不是"超出封顶50%"），做成条反而会让人误以为100%是个上限——这两个走
        // 普通文字数值，不做条。
        var physical = new List<(StatEntry, StatEntry?)>();
        // 暴击率/暴击伤害、负暴击率/负暴击伤害——之前是"非零才显示"，但暴击率恰好是0
        // 的时候（没吃到任何暴击加成）整行连带旁边的暴击伤害一起消失，暴击伤害单独
        // 挤成一整行，配对关系看着很奇怪。改成跟抗性一样"始终显示"，配对关系固定。
        StatEntry critRate = PercentEntry(L("charpanel_stat_critrate", "暴击率"), GetFinalStat("critRate"), "critRate");
        StatEntry critMult = BuildDelta100(L("charpanel_stat_critmult", "暴击伤害"), GetFinalStat("critDamageMultiplier"), "critDamageMultiplier");
        physical.Add((critRate, critMult));
        // 负暴击率/负暴击伤害：数值越高越糟（被打出负暴击的概率/倍率），颜色规则反过来。
        StatEntry negCritRate = PercentEntry(L("charpanel_stat_negcritrate", "负暴击率"), GetFinalStat("negativeCritRate"), "negativeCritRate", invertedGood: true);
        StatEntry negCritMult = BuildDelta100(L("charpanel_stat_negcritmult", "负暴击伤害"), GetFinalStat("negativeCritDamageMultiplier"), "negativeCritDamageMultiplier", invertedGood: true);
        physical.Add((negCritRate, negCritMult));
        physical.Add((
            PercentEntry(L("charpanel_stat_slashresist", "斩击抗性"), GetFinalStat("slashResist"), "slashResist"),
            PercentEntry(L("charpanel_stat_strikeresist", "打击抗性"), GetFinalStat("strikeResist"), "strikeResist")));
        physical.Add((PercentEntry(L("charpanel_stat_impactresist", "冲击抗性"), GetFinalStat("impactResist"), "impactResist"), null));

        // 属性区：灼热/电磁/腐蚀/冻结——每种属性的伤害（也就是这种属性的"攻击力"）跟它
        // 对应的抗性配一行，不再按"全部伤害排一批、全部抗性排另一批"。
        var attribute = new List<(StatEntry, StatEntry?)>
        {
            (RawEntry(L("charpanel_stat_heatdamage", "灼热伤害"), GetFinalStat("heatDamage"), "heatDamage"),
             PercentEntry(L("charpanel_stat_heatresist", "灼热抗性"), GetFinalStat("heatResist"), "heatResist")),
            (RawEntry(L("charpanel_stat_shockdamage", "电磁伤害"), GetFinalStat("shockDamage"), "shockDamage"),
             PercentEntry(L("charpanel_stat_shockresist", "电磁抗性"), GetFinalStat("shockResist"), "shockResist")),
            (RawEntry(L("charpanel_stat_corrosiondamage", "腐蚀伤害"), GetFinalStat("corrosionDamage"), "corrosionDamage"),
             PercentEntry(L("charpanel_stat_corrosionresist", "腐蚀抗性"), GetFinalStat("corrosionResist"), "corrosionResist")),
            (RawEntry(L("charpanel_stat_freezedamage", "冻结伤害"), GetFinalStat("freezeDamage"), "freezeDamage"),
             PercentEntry(L("charpanel_stat_freezeresist", "冻结抗性"), GetFinalStat("freezeResist"), "freezeResist")),
        };

        // 装备不再放在这套文字行列表里——改成右侧圆形全息槽（BuildEquipmentSlots），
        // 负重也不放这里，沿用之前"负重是背包的东西，不是角色属性"的决定。

        return new[]
        {
            ("core", core),
            ("physical", physical),
            ("attribute", attribute),
        };
    }

    // 百分比数值统一走这两个构造入口——Value 字段仍然保留格式化好的文字（万一以后
    // 哪里还要用纯文字兜底），但 CreateStatColumn 看到 Percent 有值时会改画分段条。
    // statKey=null 的话这一项不参与装备对比预览（比如HP/LP这类非装备属性）。
    private static StatEntry PercentEntry(string label, float raw, string statKey = null, bool invertedGood = false) =>
        new StatEntry(label, $"{raw:0.#}%", raw, statKey, invertedGood);

    // 非百分比的普通数值（攻击/防御/各元素伤害）——跟 PercentEntry 一样把原始值存进
    // RawValue，装备对比预览要用这个值去加偏移量。
    private static StatEntry RawEntry(string label, float raw, string statKey = null, bool invertedGood = false) =>
        new StatEntry(label, $"{raw:0.#}", statKey, raw, invertedGood);

    // 倍率类数值（暴击伤害/负暴击伤害）走这个入口——普通文字，不做分段条。以100%为
    // 基准线，显示的是相对基准的变化量而不是原始值：150%显示成"+50%"（多打50%
    // 伤害），50%显示成"-50%"（只打一半），这样才是玩家真正关心的"变化了多少"，
    // 不用自己心算"减掉100%"。始终显示（不管是不是100%基准线），跟旁边的暴击率
    // 配对关系保持固定，不会因为数值恰好是0/100而单独消失导致配对错位。
    private static StatEntry BuildDelta100(string label, float raw, string statKey = null, bool invertedGood = false)
    {
        float delta = raw - 100f;
        string sign = delta > 0f ? "+" : ""; // 负数自带负号，不用额外加
        // RawValue 存这个"相对100基准的差值"（不是原始raw）——_previewDeltas 里的偏移量
        // 单位跟GetFinalStat一致，是个纯加法偏移，加在delta上和加在raw上效果一样
        // （100这个常数偏移抵消掉了），但显示的时候要用delta做基准才对得上这一行显示的数字。
        // isPercentText=true——这里显示的文字自己拼了"%"，预览箭头的新数值也要带上，
        // 不然会出现"当前值有%、箭头新值没有%"这种不一致。
        return new StatEntry(label, $"{sign}{delta:0.#}%", statKey, delta, invertedGood, isPercentText: true);
    }

    private IEnumerator StatsRevealAnimation()
    {
        // 先等开窗那个 scaleY 展开动画播完，不要跟窗口本身展开的动作抢时间。
        yield return WaitUnscaled(SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);

        RectTransform lineRt = _scanLineRt;
        if (lineRt != null)
        {
            float t = 0f;
            while (t < ScanSweepDuration)
            {
                t += Time.unscaledDeltaTime;
                if (lineRt == null) yield break;
                float p = Mathf.Clamp01(t / ScanSweepDuration);
                float x = Mathf.Lerp(ContentAreaWidth, StatLineInset, p);
                lineRt.anchoredPosition = new Vector2(x, lineRt.anchoredPosition.y);
                yield return null;
            }
            lineRt.anchoredPosition = new Vector2(StatLineInset, lineRt.anchoredPosition.y);
        }

        // 扫描线停住之后，逐行弹出节点方块，文字跟这一行的节点同步淡入——不是跟着
        // 扫描线走的时候提前露出来。
        for (int i = 0; i < _rowNodes.Count; i++)
        {
            if (_rowIsGroupStart[i] && i > 0)
                yield return WaitUnscaled(GroupBoundaryExtraDelay);

            StartCoroutine(PopRow(_rowNodes[i], _rowTextGroups[i]));
            yield return WaitUnscaled(NodeRevealStagger);
        }
    }

    private static IEnumerator PopRow(RectTransform node, CanvasGroup textCg)
    {
        const float dur = 0.12f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            if (node == null) yield break;
            float p = Mathf.Clamp01(t / dur);
            float k = 1f - (1f - p) * (1f - p); // ease-out
            node.localScale = Vector3.one * k;
            if (textCg != null) textCg.alpha = p;
            yield return null;
        }
        if (node != null) node.localScale = Vector3.one;
        if (textCg != null) textCg.alpha = 1f;
    }

    private static IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    // ── 装备区：机体列表式布局（图标+槽位名/物品名+右侧大半透底图），分"武器"/"装备"/
    // "快捷物品"三组，每组一个小标题条，下面是一行行矩形列表条——取代原来右侧一列
    // 圆形/菱形全息槽位。点击一行 = 打开背包按对应槽位类型过滤（跟之前一样）。────

    // 11行（2武器+5装备+4快捷物品）加3个组标题要塞进 StatsAreaHeight，逐级压缩到
    // 能放下（原108/34/10/30那版快捷物品那组会溢出窗口底部）。
    private const float EquipRowHeight        = 50f * M * W;
    private const float EquipRowGap           = 5f * M * W;
    private const float EquipGroupGap         = 14f * M * W; // 组与组之间的额外间距
    private const float EquipHeaderHeight     = 24f * M * W;
    private const float EquipColumnRightMargin = 25f * W;
    private const float EquipIconSize         = 42f * M * W;
    private const float EquipIconLeftMargin   = 12f * M * W;
    private const float EquipTextLeftMargin   = 12f * M * W; // 图标右边到文字的距离
    private const float EquipGhostSize        = 110f * M * W; // 右侧大半透底图

    // labelKey/fallback 分开存——EquipGroups 是 static readonly（类加载时就初始化），
    // 这时候还没有实例，调不了 L() 这个实例方法，所以这里只存 key+兜底文字，真正的
    // L() 调用挪到 BuildEquipmentSlots 里用的时候再做。
    private static readonly (string labelKey, string fallback, EquipmentSlotType[] slots)[] EquipGroups =
    {
        ("charpanel_group_weapon", "武器", new[] { EquipmentSlotType.Weapon, EquipmentSlotType.WeaponSecondary }),
        ("charpanel_group_equipment", "装备", new[] { EquipmentSlotType.Head, EquipmentSlotType.UpperBody, EquipmentSlotType.Hands, EquipmentSlotType.LowerBody, EquipmentSlotType.Shoes }),
    };
    private const int QuickItemSlotCount = 4; // 快捷物品目前没有真实绑定数据源，先占位显示"未设置"

    private RectTransform _equipColumnRt;
    private CanvasGroup _equipColumnCg;
    private static Sprite _horizontalFadeSpriteCache;

    private readonly Dictionary<EquipmentSlotType, Image> _equipRowIcons = new Dictionary<EquipmentSlotType, Image>();
    private readonly Dictionary<EquipmentSlotType, Image> _equipRowGhosts = new Dictionary<EquipmentSlotType, Image>();
    private readonly Dictionary<EquipmentSlotType, TMP_Text> _equipRowNameTexts = new Dictionary<EquipmentSlotType, TMP_Text>();
    private readonly Dictionary<int, Image> _quickSlotRowIcons = new Dictionary<int, Image>();
    private readonly Dictionary<int, Image> _quickSlotRowGhosts = new Dictionary<int, Image>();
    private readonly Dictionary<int, TMP_Text> _quickSlotRowNameTexts = new Dictionary<int, TMP_Text>();
    private bool _equipSlotsBuilt;

    private string SlotDisplayName(EquipmentSlotType t) => t switch
    {
        EquipmentSlotType.Weapon          => L("charpanel_slot_weapon1", "武器一"),
        EquipmentSlotType.WeaponSecondary => L("charpanel_slot_weapon2", "武器二"),
        EquipmentSlotType.Head            => L("charpanel_slot_head", "头部"),
        EquipmentSlotType.UpperBody       => L("charpanel_slot_upperbody", "上装"),
        EquipmentSlotType.LowerBody       => L("charpanel_slot_lowerbody", "下装"),
        EquipmentSlotType.Hands           => L("charpanel_slot_hands", "手部"),
        EquipmentSlotType.Shoes           => L("charpanel_slot_shoes", "鞋子"),
        _                                 => t.ToString(),
    };

    // 装备名字要跟背包详情面板（InventoryItemDetailPanel）用同一套"按物品等级(itemLevel)
    // 决定品质颜色"的规则——不是随手挑几个颜色摆样子，Lv.9 还要用 RainbowTextEffect
    // 逐字符流动彩虹，两边必须共用同一份映射（QualityHex），不能各写一份自己猜的配色。
    private static void ApplyQualityNameText(TMP_Text text, ItemDefinition def)
    {
        string name = string.IsNullOrEmpty(def.displayName) ? def.itemKey : def.GetLocalizedDisplayName();
        if (def.itemLevel == 9)
        {
            text.text = name;
            text.color = Color.white;
            var rainbow = text.GetComponent<RainbowTextEffect>();
            if (rainbow == null) rainbow = text.gameObject.AddComponent<RainbowTextEffect>();
            rainbow.enabled = true;
        }
        else
        {
            var rainbow = text.GetComponent<RainbowTextEffect>();
            if (rainbow != null) rainbow.enabled = false;
            text.color = Color.white;
            text.text = $"<color=#{InventoryItemDetailPanel.QualityHex(def.itemLevel)}>{name}</color>";
        }
    }

    private void RefreshEquipmentSlotColors()
    {
        foreach (var kv in _equipRowNameTexts)
        {
            var entry = GetEquippedItem(kv.Key);
            bool hasItem = entry?.definition != null;

            if (kv.Value != null)
            {
                if (hasItem)
                {
                    ApplyQualityNameText(kv.Value, entry.definition);
                    kv.Value.font = _font;
                }
                else
                {
                    kv.Value.text = "NOT EQUIPPED"; // 固定用英文，不跟语言走
                    kv.Value.color = new Color(1f, 1f, 1f, 0.4f);
                    kv.Value.font  = _placeholderFont;
                    var rainbow = kv.Value.GetComponent<RainbowTextEffect>();
                    if (rainbow != null) rainbow.enabled = false;
                }
            }

            if (_equipRowIcons.TryGetValue(kv.Key, out var icon) && icon != null)
            {
                icon.sprite = hasItem ? entry.definition.icon : null;
                icon.enabled = hasItem && entry.definition.icon != null;
            }
            if (_equipRowGhosts.TryGetValue(kv.Key, out var ghost) && ghost != null)
            {
                ghost.sprite = hasItem ? entry.definition.icon : null;
                ghost.enabled = hasItem && entry.definition.icon != null;
            }
        }
        RefreshQuickSlotRows();
    }

    private static readonly Color QuickSlotOutOfStockTint = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    private void RefreshQuickSlotRows()
    {
        var runtime = QuickSlotRuntime.Instance;
        foreach (var kv in _quickSlotRowNameTexts)
        {
            ItemDefinition def = runtime != null ? runtime.GetSlot(kv.Key) : null;
            bool hasItem = def != null;
            // 绑定关系不会因为用光了自动解除（见 QuickSlotRuntime 头注释），数量为0时
            // 这一行还是要显示，但整体变灰——不然玩家会以为这个快捷物品还能正常用。
            bool outOfStock = hasItem && QuickSlotRuntime.GetTotalCountAndFullState(def).total <= 0;
            Color iconTint = outOfStock ? QuickSlotOutOfStockTint : Color.white;

            if (kv.Value != null)
            {
                if (hasItem && !outOfStock)
                {
                    ApplyQualityNameText(kv.Value, def);
                    kv.Value.font = _font;
                }
                else
                {
                    var rainbow = kv.Value.GetComponent<RainbowTextEffect>();
                    if (rainbow != null) rainbow.enabled = false;
                    kv.Value.text = hasItem ? def.GetLocalizedDisplayName() : "NOT SET";
                    kv.Value.color = !hasItem ? new Color(1f, 1f, 1f, 0.4f) : QuickSlotOutOfStockTint;
                    kv.Value.font  = hasItem ? _font : _placeholderFont;
                }
            }
            if (_quickSlotRowIcons.TryGetValue(kv.Key, out var icon) && icon != null)
            {
                icon.sprite = hasItem ? def.icon : null;
                icon.enabled = hasItem && def.icon != null;
                icon.color = iconTint;
            }
            if (_quickSlotRowGhosts.TryGetValue(kv.Key, out var ghost) && ghost != null)
            {
                // ghost是淡淡的背景大图标（烤进prefab的alpha=0.16），不跟着变灰——变灰会
                // 把这点透明度也一起干掉，反而看不出"这是个装饰性背景"了。灰显只需要
                // 主图标+文字变灰就够传达"没有了"，不用背景图标也跟着改。
                ghost.sprite = hasItem ? def.icon : null;
                ghost.enabled = hasItem && def.icon != null;
            }
        }
    }

    private void BuildEquipmentSlots()
    {
        if (_equipSlotsBuilt)
        {
            RefreshEquipmentSlotColors();
            return;
        }
        _equipSlotsBuilt = true;
        _equipNavOrder.Clear();
        _equipCursorIndex = -1;

        if (_equipColumnRt != null) Destroy(_equipColumnRt.gameObject);
        _equipRowIcons.Clear();
        _equipRowGhosts.Clear();
        _equipRowNameTexts.Clear();
        _quickSlotRowIcons.Clear();
        _quickSlotRowGhosts.Clear();
        _quickSlotRowNameTexts.Clear();

        var columnRt = MakeRect("EquipColumn", _boxRt, new Vector2(1f, 0f), new Vector2(1f, 1f));
        columnRt.pivot = new Vector2(1f, 0.5f);
        columnRt.offsetMin = new Vector2(-(EquipColumnReserve), 40f);
        columnRt.offsetMax = new Vector2(-EquipColumnRightMargin, -TopReserve);
        _equipColumnRt = columnRt;
        _equipColumnCg = columnRt.gameObject.AddComponent<CanvasGroup>();

        float y = 0f;
        bool firstGroup = true;
        foreach (var (labelKey, fallback, slots) in EquipGroups)
        {
            if (!firstGroup) y += EquipGroupGap;
            firstGroup = false;

            BuildEquipGroupHeader(columnRt, y, L(labelKey, fallback));
            y += EquipHeaderHeight;

            foreach (var slot in slots)
            {
                BuildEquipRow(columnRt, y, SlotDisplayName(slot), slot, null);
                y += EquipRowHeight + EquipRowGap;
            }
            y -= EquipRowGap;
        }

        // 快捷物品——现在接了 QuickSlotRuntime，点一行会打开背包按"可用消耗品、非
        // 复活道具"过滤，选中之后绑定到这个槽（见 SkyPrisonInventoryInteraction 里
        // 新增的"指定为快捷物品"菜单行）。
        y += EquipGroupGap;
        BuildEquipGroupHeader(columnRt, y, L("charpanel_group_quickitem", "快捷物品"));
        y += EquipHeaderHeight;
        for (int i = 0; i < QuickItemSlotCount; i++)
        {
            BuildEquipRow(columnRt, y, $"{L("charpanel_group_quickitem", "快捷物品")} {i + 1}", null, i);
            y += EquipRowHeight + EquipRowGap;
        }
    }

    private const float EquipRowCornerSize = 6f * M * W;
    private const float EquipRowEdgeLineWidth = 0.9f * M * W;

    // 每行四个角落一个小方块■，左右两边各一条很细的竖线——参考图那种终端/HUD边框
    // 装饰，比整圈实线边框克制，只强调四角和左右两条边。
    private void BuildEquipRowFrame(RectTransform rowRt)
    {
        var cornerColor = new Color(1f, 1f, 1f, 0.4f);
        AddCorner(rowRt, new Vector2(0f, 0f), new Vector2(0f, 0f), cornerColor);
        AddCorner(rowRt, new Vector2(1f, 0f), new Vector2(1f, 0f), cornerColor);
        AddCorner(rowRt, new Vector2(0f, 1f), new Vector2(0f, 1f), cornerColor);
        AddCorner(rowRt, new Vector2(1f, 1f), new Vector2(1f, 1f), cornerColor);

        var edgeColor = new Color(1f, 1f, 1f, 0.18f);
        var leftLineRt = MakeRect("EdgeL", rowRt, new Vector2(0f, 0f), new Vector2(0f, 1f));
        leftLineRt.pivot = new Vector2(0f, 0.5f);
        leftLineRt.sizeDelta = new Vector2(EquipRowEdgeLineWidth, 0f);
        var leftLineImg = leftLineRt.gameObject.AddComponent<Image>();
        leftLineImg.color = edgeColor;
        leftLineImg.raycastTarget = false;

        var rightLineRt = MakeRect("EdgeR", rowRt, new Vector2(1f, 0f), new Vector2(1f, 1f));
        rightLineRt.pivot = new Vector2(1f, 0.5f);
        rightLineRt.sizeDelta = new Vector2(EquipRowEdgeLineWidth, 0f);
        var rightLineImg = rightLineRt.gameObject.AddComponent<Image>();
        rightLineImg.color = edgeColor;
        rightLineImg.raycastTarget = false;
    }

    private static void AddCorner(RectTransform rowRt, Vector2 anchor, Vector2 pivot, Color color)
    {
        var rt = MakeRect("Corner", rowRt, anchor, anchor);
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(EquipRowCornerSize, EquipRowCornerSize);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    private void BuildEquipGroupHeader(RectTransform column, float y, string label)
    {
        var headerRt = MakeRect("Header_" + label, column, new Vector2(0f, 1f), new Vector2(1f, 1f));
        headerRt.pivot = new Vector2(0f, 1f);
        headerRt.anchoredPosition = new Vector2(0f, -y);
        headerRt.sizeDelta = new Vector2(0f, EquipHeaderHeight);

        var text = MakeText(headerRt, "Label", label, SkyPrisonFloatingWindowKit.DecorativeFontSize, FontStyles.Bold, _font);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = new Color(1f, 1f, 1f, 0.7f);
        text.raycastTarget = false;

        // 标题下面一条细线，跟属性区的风格呼应。
        var lineRt = MakeRect("Line", headerRt, new Vector2(0f, 0f), new Vector2(1f, 0f));
        lineRt.pivot = new Vector2(0f, 0f);
        lineRt.sizeDelta = new Vector2(0f, 1.4f * M * W);
        var lineImg = lineRt.gameObject.AddComponent<Image>();
        lineImg.color = new Color(1f, 1f, 1f, 0.25f);
        lineImg.raycastTarget = false;
    }

    private void BuildEquipRow(RectTransform column, float y, string label, EquipmentSlotType? slot, int? quickSlotIndex)
    {
        var rowRt = MakeRect("Row_" + label, column, new Vector2(0f, 1f), new Vector2(1f, 1f));
        rowRt.pivot = new Vector2(0f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -y);
        rowRt.sizeDelta = new Vector2(0f, EquipRowHeight);

        var bgImg = rowRt.gameObject.AddComponent<Image>();
        bgImg.color = new Color(1f, 1f, 1f, 0.05f);
        bgImg.raycastTarget = true; // 必须能接收射线检测，否则整行收不到悬停/点击事件（不变绿、没有SE）

        BuildEquipRowFrame(rowRt);

        InventoryItemEntry entry = slot.HasValue ? GetEquippedItem(slot.Value) : null;
        ItemDefinition quickDef = quickSlotIndex.HasValue && QuickSlotRuntime.Instance != null
            ? QuickSlotRuntime.Instance.GetSlot(quickSlotIndex.Value) : null;
        bool hasItem = slot.HasValue ? entry?.definition != null : quickDef != null;
        ItemDefinition displayDef = slot.HasValue ? entry?.definition : quickDef;

        // 右侧大半透底图——同一张物品图标放大、左边用渐变遮罩淡出，参考图那种
        // "机体半透明剪影从左边虚化进来"的效果。用 Mask + 一张程序化的水平渐变贴图
        // 当模板，不需要额外写 shader。
        var ghostMaskRt = MakeRect("GhostMask", rowRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        ghostMaskRt.pivot = new Vector2(1f, 0.5f);
        ghostMaskRt.sizeDelta = new Vector2(EquipGhostSize, EquipRowHeight);
        ghostMaskRt.anchoredPosition = Vector2.zero;
        var ghostMaskImg = ghostMaskRt.gameObject.AddComponent<Image>();
        ghostMaskImg.sprite = GetHorizontalFadeSprite();
        ghostMaskImg.raycastTarget = false;
        var ghostMask = ghostMaskRt.gameObject.AddComponent<Mask>();
        ghostMask.showMaskGraphic = false;

        // 图标本身故意做得比裁剪框(ghostMaskRt)大很多——露出一大半也没关系，就是要那种
        // "巨大机体剪影塞不下、被裁切"的氛围感，不是要把整个图标都看全。Mask 会按
        // ghostMaskRt 的范围裁掉多出来的部分，不会影响到上下相邻的行。
        var ghostRt = MakeRect("Ghost", ghostMaskRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        ghostRt.pivot = new Vector2(0.5f, 0.5f);
        ghostRt.sizeDelta = new Vector2(EquipRowHeight * 2.4f, EquipRowHeight * 2.4f);
        var ghostImg = ghostRt.gameObject.AddComponent<Image>();
        ghostImg.preserveAspect = true;
        ghostImg.color = new Color(1f, 1f, 1f, 0.16f);
        ghostImg.raycastTarget = false;
        ghostImg.sprite = hasItem ? displayDef.icon : null;
        ghostImg.enabled = hasItem && displayDef.icon != null;
        if (slot.HasValue) _equipRowGhosts[slot.Value] = ghostImg;
        if (quickSlotIndex.HasValue) _quickSlotRowGhosts[quickSlotIndex.Value] = ghostImg;

        // 左侧小图标。
        var iconRt = MakeRect("Icon", rowRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.sizeDelta = new Vector2(EquipIconSize, EquipIconSize);
        iconRt.anchoredPosition = new Vector2(EquipIconLeftMargin, 0f);
        var iconImg = iconRt.gameObject.AddComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        iconImg.sprite = hasItem ? displayDef.icon : null;
        iconImg.enabled = hasItem && displayDef.icon != null;
        if (slot.HasValue) _equipRowIcons[slot.Value] = iconImg;
        if (quickSlotIndex.HasValue) _quickSlotRowIcons[quickSlotIndex.Value] = iconImg;

        // 文字区：槽位名/序号（小字，暗）在上，物品名（大字）在下——图标右边、渐变
        // 底图左边之间的这段空间。
        var textRt = MakeRect("Text", rowRt, new Vector2(0f, 0f), new Vector2(1f, 1f));
        textRt.offsetMin = new Vector2(EquipIconLeftMargin + EquipIconSize + EquipTextLeftMargin, 0f);
        textRt.offsetMax = new Vector2(-EquipGhostSize * 0.5f, 0f);

        var labelRt = MakeRect("Label", textRt, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
        var labelText = MakeText(labelRt, "Label", label, SkyPrisonFloatingWindowKit.DecorativeFontSize * 0.85f, FontStyles.Normal, _font);
        labelText.alignment = TextAlignmentOptions.BottomLeft;
        labelText.color = new Color(1f, 1f, 1f, 0.55f);
        labelText.enableWordWrapping = false;
        labelText.raycastTarget = false;

        var nameRt = MakeRect("Name", textRt, new Vector2(0f, 0f), new Vector2(1f, 0.5f));
        string placeholderLabel = slot.HasValue ? "NOT EQUIPPED" : "NOT SET"; // 固定用英文，不跟语言走
        var nameText = MakeText(nameRt, "Name", hasItem ? displayDef.GetLocalizedDisplayName() : placeholderLabel, SkyPrisonFloatingWindowKit.PrimaryFontSize, FontStyles.Bold, _font);
        nameText.alignment = TextAlignmentOptions.TopLeft;
        nameText.enableWordWrapping = false;
        nameText.raycastTarget = false;
        // 未装备/未设置的占位字用统一的暗淡半透明白 + "东亚重工"风格字体，跟已装备
        // 的实际物品名（正常颜色+正常字体）区分开。
        var placeholderColor = new Color(1f, 1f, 1f, 0.4f);
        if (!hasItem)
        {
            nameText.color = placeholderColor;
            nameText.font = _placeholderFont;
        }
        if (slot.HasValue) _equipRowNameTexts[slot.Value] = nameText;
        if (quickSlotIndex.HasValue) _quickSlotRowNameTexts[quickSlotIndex.Value] = nameText;

        var hover = rowRt.gameObject.AddComponent<CharacterPanelEquipRowHover>();
        // 耐久报废（0耐久）的装备行悬停时用淡红色，跟正常装备的冷绿悬停区分开，
        // 提醒玩家这件东西已经不提供属性加成了（不强制卸下来，只是视觉提示）。
        bool isDestroyed = slot.HasValue && entry != null && DurabilitySystem.IsDestroyed(entry);
        hover.Bind(bgImg, isDestroyed);

        // Button + SkyPrisonUIButtonFeedback 都统一加在所有行上，鼠标/手柄悬停反馈
        // 一致；点击行为按这一行到底是装备槽还是快捷物品槽分别接。
        var button = rowRt.gameObject.AddComponent<Button>();
        button.targetGraphic = bgImg;
        SkyPrisonUIButtonFeedback.Attach(rowRt.gameObject);

        if (slot.HasValue)
        {
            var capturedSlot = slot.Value;
            button.onClick.AddListener(() => OpenInventoryToEquip(capturedSlot));
            hover.OnRightClick = () => TryUnequip(capturedSlot);
            _equipNavOrder.Add((capturedSlot, hover));
        }
        else if (quickSlotIndex.HasValue)
        {
            var capturedIndex = quickSlotIndex.Value;
            button.onClick.AddListener(() => OpenInventoryToQuickSlot(capturedIndex));
            // 之前只有装备槽行接了 OnRightClick（右键卸装），快捷物品行漏了——右键
            // 应该等价于"取消这个槽位的指定"，音效由 QuickSlotRuntime.ClearSlot 内部
            // 统一播放（UnassignQuickSlot），这里不用重复播。
            hover.OnRightClick = () => QuickSlotRuntime.Instance?.ClearSlot(capturedIndex);
        }
    }

    // 水平渐变贴图（左透明→右不透明）——给右侧大半透底图当遮罩模板，做"从左边虚化
    // 进来"的效果，不需要额外写shader。
    private static Sprite GetHorizontalFadeSprite()
    {
        if (_horizontalFadeSpriteCache != null) return _horizontalFadeSpriteCache;
        const int w = 64, h = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[w * h];
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            float a = Mathf.SmoothStep(0f, 1f, t);
            for (int y = 0; y < h; y++) pixels[y * w + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        _horizontalFadeSpriteCache = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        return _horizontalFadeSpriteCache;
    }

    // 悬停行高亮——鼠标/手柄导航到某一行时把背景稍微提亮，离开恢复，纯视觉反馈，
    // 不做缩放/色收差那些更重的效果。
    private class CharacterPanelEquipRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Image _background;
        private bool _isDestroyed;
        private static readonly Color BaseColor = new Color(1f, 1f, 1f, 0.05f);
        // 鼠标/手柄光标停在这一行时整条变成系统冷绿——跟窗口其它地方"选中/激活"用的
        // 同一个强调色（SkyPrisonUIPalette.ColdGreen），不是简单提亮白色底。
        private static readonly Color HoverColor = new Color(
            SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 0.28f);

        // 耐久报废的装备行悬停时用这个淡红色代替上面的冷绿——跟HUD武器剪影低耐久
        // 变红是同一套"红=有问题"的视觉语言，但报废(0耐久)比"低耐久"更严重，颜色
        // 也更饱和一些区分开。
        private static readonly Color DestroyedHoverColor = new Color(0.95f, 0.35f, 0.35f, 0.32f);

        // 右键卸装——只有装备槽行会绑这个（快捷物品行没有"卸下"这个概念），null=不响应右键。
        public System.Action OnRightClick;

        public void Bind(Image background, bool isDestroyed = false)
        {
            _background = background;
            _isDestroyed = isDestroyed;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetColor(_isDestroyed ? DestroyedHoverColor : HoverColor);
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); // 跟主菜单/存档选择同一套悬停提示音
        }

        public void OnPointerExit(PointerEventData eventData) => SetColor(BaseColor);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                OnRightClick?.Invoke();
        }

        private void SetColor(Color c)
        {
            if (_background != null) _background.color = c;
        }
    }

    // 左下角一小块"系统日志"装饰——纯氛围用的假英文代码/状态行，不是真数据，不接收
    // 点击也不会被读取，纯粹参考图那种终端HUD"到处飘着看不懂的系统字符"的味道。
    private static readonly string[] SystemCodeLines =
    {
        "SYS.CORE//OK :: uptime=004:12:07 :: watchdog=armed",
        "ADDR 0x7F3A::LINK  ttl=64  pkt_loss=0.00%  latency=3ms",
        "UNIT.STATE=NOMINAL  power_draw=41.2kW  coolant=OK",
        "BIO.SYNC..92.4%   pulse=stable   O2_sat=98%",
        "> tracking_target  lock_conf=0.83  bearing=214.6deg",
    };

    private void BuildSystemCodeDecoration(RectTransform box)
    {
        // 之前的行数/字号跟属性表底部几行撞在一起了——缩小整块（更少行、更小字、
        // 更紧的行距），紧贴在box最底部窄窄一条，不再往上侵占属性表的地盘。左边距
        // 也加大，之前贴太近跟左侧的竖线撞在一起了。去掉了之前那个位置对不上、
        // 单独飘出去的方括号装饰，只留文字。
        int lineCount = SystemCodeLines.Length;
        float lineH = 15f * M * W;
        float blockWidth = 900f * M * W;
        float blockHeight = lineCount * lineH;

        var rt = MakeRect("SystemCode", box, new Vector2(0f, 0f), new Vector2(0f, 0f));
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(70f * M * W, 10f * M * W);
        rt.sizeDelta = new Vector2(blockWidth, blockHeight);

        var dimWhite = new Color(1f, 1f, 1f, 0.22f);

        for (int i = 0; i < lineCount; i++)
        {
            var lineRt = MakeRect("Line" + i, rt, new Vector2(0f, 0f), new Vector2(1f, 0f));
            lineRt.pivot = new Vector2(0f, 0f);
            lineRt.anchoredPosition = new Vector2(0f, i * lineH);
            lineRt.sizeDelta = new Vector2(0f, lineH);
            var text = MakeText(lineRt, "Text", SystemCodeLines[i], 11f * M * W, FontStyles.Normal, _font);
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.color = dimWhite;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
        }
    }


    // ── 占位视觉：只用来证明开关/数据链路是通的，不是最终设计 ───────────────────

    private void BuildPlaceholderVisual()
    {
        _font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        _placeholderFont = LoadFontByGuid("ea35a3e4c89493f44bb2e5cbe046505c") ?? _font;
        _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CharacterPanelSortingOrder; // 跟背包/地图这类悬浮窗同级，压过 HUD 但不用抢暂停菜单/设置的顶层
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _rootRt = (RectTransform)transform;

        var boxGo = new GameObject("Box", typeof(RectTransform));
        boxGo.transform.SetParent(_rootRt, false);
        _boxRt = (RectTransform)boxGo.transform;
        _boxRt.anchorMin = new Vector2(0f, 0.5f);
        _boxRt.anchorMax = new Vector2(0f, 0.5f);
        _boxRt.pivot = new Vector2(0f, 0.5f);
        _boxRt.anchoredPosition = new Vector2(80f, 0f);
        _boxRt.sizeDelta = new Vector2(BoxWidth, BoxHeight);

        // 磨砂背景、角标、标题栏+拖拽、关闭按钮——全部走统一入口 SkyPrisonFloatingWindowKit，
        // 保证跟其它悬浮窗（以后新建的）用的是同一套角标大小/关闭按钮规格，不再各写一份数字。
        // 磨砂背景直接挂在 Box 上，跟着 Box 一起做横线展开动画——展开过程中还没展开到的
        // 部分是透明的（能看到背后场景），这是明确接受的取舍：不想要静态背景/纯黑填充，
        // 只要磨砂背景本身看起来真的在展开。
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, _boxRt, out _blurUvTracker);
        SkyPrisonFloatingWindowKit.AddCornerBrackets(_boxRt);
        var titleBarRt = SkyPrisonFloatingWindowKit.BuildTitleBar(_boxRt, L("charpanel_title", "角色信息"), _font, out _);
        _dragHandle = titleBarRt.GetComponent<SkyPrisonUIWindowDragHandle>();
        SkyPrisonFloatingWindowKit.BuildCloseButton(_boxRt, Hide, _font);
        BuildSystemCodeDecoration(_boxRt);

        BuildStatsAndEquipment(animate: true);

        // 开窗动画：从中间一条横线（scaleY=0）展开到完整高度。
        _boxRt.localScale = new Vector3(1f, 0f, 1f);
        StartCoroutine(OpenBoxAnimation());
    }

    private IEnumerator OpenBoxAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_boxRt == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p); // ease-out：越展开越慢，有"弹出感"
            _boxRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_boxRt != null) _boxRt.localScale = Vector3.one;
    }

    // 关窗动画：压缩成中间一条横线再销毁，跟背包窗口 CompressOut 的第一阶段一致。
    // 动画期间冻结 UVTracker，不然它会跟着缩放重算采样区，磨砂图会跟着扭曲变形。
    private IEnumerator CloseBoxAnimation()
    {
        if (_blurUvTracker != null) _blurUvTracker.Frozen = true;

        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_boxRt == null) break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p; // ease-in：越收越快
            _boxRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }

        Destroy(gameObject);
    }

    // ── 几何/文字 helper：全部转发到 SkyPrisonFloatingWindowKit，保持"只有一份实现"——
    // 这里留名字不变只是为了不用去改下面几十处调用点。────────────────────────────

    private static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax) =>
        SkyPrisonFloatingWindowKit.MakeRect(name, parent, anchorMin, anchorMax);

    private static void AddOutline(RectTransform rt, Color c, float px) =>
        SkyPrisonFloatingWindowKit.AddOutline(rt, c, px);

    private static TMP_Text MakeText(Transform parent, string name, string text, float size, FontStyles style, TMP_FontAsset font) =>
        SkyPrisonFloatingWindowKit.MakeText(parent, name, text, size, style, font);

    // 跟 SaveSlotSelectorUI 里同一个 GUID 查找方式——只在编辑器里能查到路径，Build
    // 里没有 AssetDatabase，会退回 null，调用方自己 ?? _font 兜底成默认字体。
    private static TMP_FontAsset LoadFontByGuid(string guid)
    {
#if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
#endif
        return null;
    }

    private void Close()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);

        SkyPrisonInventoryChromatic.PopGlobalSuspend(); // 对应 Build() 里的 PushGlobalSuspend

        // 装备槽呼出的背包窗口是角色面板的附属，角色面板一关就该跟着关掉，不然会留下
        // 一个孤零零、还带着"强制只显示武器/手套"过滤状态的背包窗口。
        if (_equipInventoryWindow != null)
        {
            var windowManager = FindObjectOfType<SkyPrisonWindowManager_V1>();
            windowManager?.Close("inventory");
            _equipInventoryWindow = null;
            _equipInventoryCurrentSlot = null;
            _equipInventoryCurrentQuickSlot = null;
            _dragHandle?.SetLocked(false);
        }

        EquipmentRuntime.OnEquipped -= HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped -= HandleEquipmentChanged;
        QuickSlotRuntime.OnSlotChanged -= HandleQuickSlotChanged;
        if (_inventory != null) _inventory.OnInventoryChanged -= RefreshQuickSlotRows;
        if (_health != null)
        {
            _health.OnDamaged -= HandleHealthChanged;
            _health.OnHealed -= HandleHealthChanged;
        }
        if (_battleStats != null)
            _battleStats.StatsRebuilt -= HandleStatsRebuilt;

        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        SkyPrisonWindowHintBar.SetEdgeTop(false); // 复位提示条到底部，别管重叠检测已经跟着窗口一起没了
        SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
        RestoreCombatHud();
        _instance = null;
        // 关窗动画（压缩成一条横线）结束后再真正销毁——顺带修了一个问题：之前这里是
        // RestoreCombatHud() 后紧接着 Destroy(gameObject)，HUD 淡入协程还没跑完就被
        // 销毁宿主物体连带掐断了，HUD 淡入动画基本没机会真正播完。
        StartCoroutine(CloseBoxAnimation());
    }
}
