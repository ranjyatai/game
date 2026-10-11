using System.Collections.Generic;
using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 挂在玩家身上。唯一的交互扫描/输入入口——统一了原来各扫各的两套系统
/// (物品拾取 SkyPrisonItemPickupController 自己的 Update/范围检测/E键，
/// 通用交互 IInteractable 自己的一套)，现在只有这一份 Update：合并所有
/// IInteractable（含 LootDropWorldObject 本身也实现了这个接口）按距离
/// 排序成一个列表，一次只显示/响应最近的一个，多个同时在附近时用 G 切换，
/// 而不是像以前那样物品提示和NPC提示同时弹出、E键同时触发两边。
/// 提示 UI 按类型分流：LootDropWorldObject 走 SkyPrisonItemPickupController
/// 那套带图标/品级颜色的富提示条，其余类型走 InteractionPromptUI 的朴素提示条。
/// 窗口打开期间自动隐藏提示，不响应输入。
/// </summary>
[DefaultExecutionOrder(9001)]
[DisallowMultipleComponent]
public sealed class SkyPrisonInteractionController : MonoBehaviour
{
    [SerializeField] private float interactRadius = 2.8f;
    [SerializeField] private float promptHeight   = 2.4f;

    [Tooltip("两个候选物彼此之间(不是各自到玩家)的距离在这个范围内才算\"贴在一起\"，" +
             "需要 G 才能切换选中。隔开一点(哪怕两个都还在玩家 interactRadius 内)就" +
             "不算——玩家应该靠走位直接锁定离自己近的那个，不该被更远处的另一个候选" +
             "拖着一起要求按G。取值参考掉落物出生时的随机散布半径(±0.5m)，够覆盖" +
             "\"物品刚好掉在NPC脚下\"，又明显小于\"两者隔开一段能走位分开\"的间距。")]
    [SerializeField] private float clusterRadius  = 1.2f;

    [Tooltip("排查用：候选物因为间距超过 clusterRadius 没被并入歧义簇时，把实际测出来" +
             "的间距打到 Console。怀疑候选堆积(比如上一轮测试丢的物品没捡、还留在场景" +
             "里)导致本轮判定看起来不对时打开。")]
    [SerializeField] private bool debugClusterGap = true;

    private SkyPrisonInputSettings    _input;
    private SkyPrisonWindowManager_V1 _windows;
    private SkyPrisonItemPickupController _itemPrompt;

    private readonly List<IInteractable> _inRange = new List<IInteractable>();
    private readonly List<IInteractable> _cluster = new List<IInteractable>();
    private int _selectedIdx;
    private IInteractable _current;
    private LootDropWorldObject _highlightedLoot;

    // ── 提示 UI（与 ItemPickupController 同规格）────────────────────────
    // 提示内容由 InteractionPromptUI 组件负责构建，保持本文件精简
    private InteractionPromptUI _promptUI;

    private Camera _cam;

    private void Update()
    {
        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        if (playerGo == null) { Clear(); return; }

        // HasAnyWindowOpen() 只认走 windowManager.Open() 注册过的"prefab 流程"窗口——
        // 设置窗口/暂停菜单是纯手写的全屏窗口，走的是另一套机制（只设 ExternalBlock 静态
        // 标记，从没注册进 openedWindows），漏了这一条会导致设置/按键绑定弹窗开着的时候
        // 按 E 照样能触发场景里的交互（挖到过一次：弹窗开着按 E 直接触发了撤离终端）。
        if ((Windows() != null && _windows.HasAnyWindowOpen()) || SkyPrisonWindowManager_V1.AnyBlockingWindowOpen) { Clear(); return; }

        Vector3 playerPos = playerGo.transform.position;
        RefreshInRange(playerPos);

        if (_inRange.Count == 0) { Clear(); return; }

        // 统一规则，不分物品/NPC/设施，也不分"离得远/离得近/贴在一起"——只有一套
        // 判定：候选物之间(不是到玩家)彼此贴近的才算同一个"歧义簇"(见 BuildCluster)。
        //
        //   簇内只有1个 → 没有歧义，直接锁定它，不看之前选的是谁——哪怕玩家之前在
        //   别的簇里按过G，这里也要能靠走位直接生效，不能被旧选择粘住。
        //
        //   簇内有2个以上 → 真正的歧义（比如物品刚好掉在NPC脚下，玩家没法只贴近
        //   其中一个），才需要"选中跟着具体对象走、按G才切换"这套粘滞逻辑——不然
        //   簇内两个候选距离一旦接近，每帧重新排序不保证稳定，没按G选中对象也会
        //   自己跳。
        BuildCluster();

        if (_cluster.Count <= 1)
        {
            _selectedIdx = 0;
            _current = _cluster[0];
        }
        else
        {
            int idx = _current != null ? _cluster.IndexOf(_current) : -1;
            if (idx < 0)
                idx = 0; // 没有正在生效的选中，默认选簇内离玩家最近的
            _selectedIdx = idx;
            _current = _cluster[_selectedIdx];
        }

        IInteractable target = _current;

        UpdateLootHighlight(target as LootDropWorldObject);

        SkyPrisonInputSettings input = GetInput();

        if (target is LootDropWorldObject loot)
        {
            _promptUI?.Hide();
            ItemPrompt()?.ShowItemPrompt(loot, _cluster.Count, _selectedIdx);
        }
        else
        {
            ItemPrompt()?.HideItemPrompt();
            EnsurePromptUI();
            _promptUI?.Show(target, GetCamera(), _cluster.Count, _selectedIdx);
        }

        if (input == null) return;

        if (input.GetActionDown(SkyPrisonInputAction.CyclePickup) && _cluster.Count > 1)
        {
            _selectedIdx = (_selectedIdx + 1) % _cluster.Count;
            // 立即把_current指向新选中的对象——不这样做的话，下一帧开头那句
            // "优先延续上一帧_current"会因为_current还没更新成新对象，反而把这次
            // 切换给撤销掉。
            _current = _cluster[_selectedIdx];
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        }

        if (input.GetActionDown(SkyPrisonInputAction.Interact))
        {
            if (target is LootDropWorldObject)
            {
                // 拾取的成功/失败音效由 LootDropWorldObject.Interact() 自己按实际
                // 入包结果播放（Pickup/Forbidden），这里不重复播放，也不拿 CanInteract
                // 事先拦一道——CanInteract 只用来给提示条调暗，具体能不能装、装多少
                // 以 Interact() 内部真正 AddItem 的结果为准。
                target.Interact();
            }
            else if (target.CanInteract)
            {
                // Open 音效改由各 IInteractable 自己的 Interact() 里播放
                // （NPCDialogueInteractable/FacilityInteractable），避免和物品拾取
                // 共用同一个控制器时对每种类型都硬播一次 Open 导致声音对不上。
                target.Interact();
            }
            else
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            }
        }
    }

    private void Clear()
    {
        _current = null;
        _selectedIdx = 0;
        _inRange.Clear();
        _promptUI?.Hide();
        ItemPrompt()?.HideItemPrompt();
        UpdateLootHighlight(null);
    }

    private void UpdateLootHighlight(LootDropWorldObject loot)
    {
        if (loot == _highlightedLoot) return;

        // ?. 空条件运算符只看"是不是真正的C# null"，不会走 UnityEngine.Object 重载的
        // 那套"已经Destroy就当作null"假null检测——哪怕这里是具体类型(LootDropWorldObject)
        // 不是接口，只要用 ?. 链式调用，捡起来、Destroy掉的上一个高亮对象照样会被
        // GetComponent，直接抛 MissingReferenceException(捡起唯一一件掉落物之后一直
        // 复现的那个)。换成显式 != null(会触发Object的==重载)才能真正挡住。
        if (_highlightedLoot != null)
        {
            LootDropVisual oldVisual = _highlightedLoot.GetComponent<LootDropVisual>();
            if (oldVisual != null) oldVisual.SetSelected(false);
        }

        _highlightedLoot = loot;

        if (_highlightedLoot != null)
        {
            LootDropVisual newVisual = _highlightedLoot.GetComponent<LootDropVisual>();
            if (newVisual != null) newVisual.SetSelected(true);
        }
    }

    // ── 范围检测 ──────────────────────────────────────────────────────────

    private void RefreshInRange(Vector3 pos)
    {
        _inRange.Clear();
        float effRadius = interactRadius * TechTreePerks.Mult("pickupRange");      // 科技树「拾荒者」：拾取范围
        float r2 = effRadius * effRadius;

        // 扫描场景中所有已注册的 IInteractable（物品/NPC/设施统一走这一份注册表）
        var all = InteractableRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            IInteractable it = all[i];
            if (!IsAlive(it)) continue;
            if (it is LootDropWorldObject loot && loot.Item == null)
            {
                // Item 为空的掉落物永远进不了候选名单——E键完全没反应、且没有任何报错，
                // 排查过一轮"选中/切换逻辑"没找到问题后怀疑就是卡在这一步，加一条日志
                // 好在下次复现时直接从 Console 确认，不用再靠猜。
                Debug.LogWarning($"[SkyPrisonInteractionController] 掉落物 '{loot.name}' 的 Item 为空，" +
                                  "已跳过（不会显示拾取提示/不会响应E）。", loot);
                continue;
            }
            if ((it.InteractPosition - pos).sqrMagnitude <= r2)
                _inRange.Add(it);
        }

        // 按距离排序，最近的排首位
        Vector3 refPos = pos;
        _inRange.Sort((a, b) =>
            (a.InteractPosition - refPos).sqrMagnitude
            .CompareTo((b.InteractPosition - refPos).sqrMagnitude));

        if (_current != null && (!IsAlive(_current) || !_inRange.Contains(_current)))
        {
            _current = null;
            _selectedIdx = 0;
        }
    }

    /// <summary>
    /// 从 _inRange（已按到玩家距离排序）里挑出"离玩家最近的那个 + 跟它彼此贴近
    /// 的候选"，构成当前唯一的歧义簇。簇成员判定只看候选物之间的相互距离，跟玩家
    /// 站在哪完全无关——玩家怎么走位都不会让簇忽大忽小，避免了"按了G，下一帧又
    /// 因为距离重新排序被切回去"的问题。
    /// </summary>
    private void BuildCluster()
    {
        _cluster.Clear();
        if (_inRange.Count == 0) return;

        IInteractable nearest = _inRange[0];
        _cluster.Add(nearest);

        float r2 = clusterRadius * clusterRadius;
        for (int i = 1; i < _inRange.Count; i++)
        {
            IInteractable other = _inRange[i];
            float dist = (other.InteractPosition - nearest.InteractPosition).magnitude;
            if (dist * dist <= r2)
            {
                _cluster.Add(other);
            }
            else if (debugClusterGap)
            {
                // clusterRadius 是拍的经验值，实际测出来的间距和它差多少直接打出来，
                // 不用再靠猜校准。
                Debug.Log($"[SkyPrisonInteractionController] 候选间距 {dist:F2}m > " +
                          $"clusterRadius({clusterRadius:F2}m)，'{DescribeInteractable(other)}' " +
                          $"没有并入以 '{DescribeInteractable(nearest)}' 为中心的歧义簇。");
            }
        }

        if (debugClusterGap && _inRange.Count > 1)
        {
            // 怀疑"候选堆积"(比如上一轮测试丢的物品没捡、还留在场景里，跟这一轮
            // 测试的东西凑在一起被一起算了)时，直接看这一帧 _inRange 里到底有哪些
            // 候选、各自离玩家多远，比反复猜哪个环节漏判快得多。
            var sb = new System.Text.StringBuilder();
            sb.Append($"[SkyPrisonInteractionController] 当前 _inRange 共 {_inRange.Count} 个候选：");
            for (int i = 0; i < _inRange.Count; i++)
                sb.Append($"\n  [{i}] {DescribeInteractable(_inRange[i])}");
            Debug.Log(sb.ToString());
        }
    }

    private static string DescribeInteractable(IInteractable it)
    {
        if (it == null) return "null";
        string kind = it is LootDropWorldObject ? "掉落物" : it.GetType().Name;
        string name = (it as UnityEngine.Object)?.name ?? "?";
        return $"{kind}:{name}@{it.InteractPosition:F2}";
    }

    // _current/it 是接口类型(IInteractable)。Unity 的"对象已 Destroy 但还没被
    // GC，跟它比较 == null 会返回 true"这套假null机制只在编译期静态类型是
    // UnityEngine.Object(及子类)时才生效——接口类型的 == 在这种情况下会退化成
    // 普通引用比较，检测不到"已经 Destroy"，导致后面继续读 .transform 之类
    // 需要访问原生对象的属性时抛 MissingReferenceException（捡起唯一一件掉落物
    // 后，下一帧还想读它的位置，才会炸；旁边没有别的候选可以让选中状态提前被
    // 冲掉，问题才會单独暴露出来）。真正等到 GC 之前先转成 UnityEngine.Object
    // 再判空，才能借到它重载的假null检测。
    private static bool IsAlive(IInteractable it)
    {
        if (it == null) return false;
        if (it is UnityEngine.Object uo) return uo;
        return true;
    }

    // ── 懒加载工具 ────────────────────────────────────────────────────────

    private SkyPrisonWindowManager_V1 Windows()
    {
        if (_windows == null) _windows = FindObjectOfType<SkyPrisonWindowManager_V1>();
        return _windows;
    }

    private SkyPrisonItemPickupController ItemPrompt()
    {
        if (_itemPrompt == null) _itemPrompt = FindObjectOfType<SkyPrisonItemPickupController>();
        return _itemPrompt;
    }

    private SkyPrisonInputSettings GetInput()
    {
        if (_input == null)
        {
            _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings")
                  ?? Resources.Load<SkyPrisonInputSettings>("Settings/SkyPrisonInputSettings");
            _input?.EnsureDefaults();
        }
        return _input;
    }

    private Camera GetCamera()
    {
        if (_cam == null) _cam = Camera.main;
        return _cam;
    }

    private void EnsurePromptUI()
    {
        if (_promptUI != null) return;
        _promptUI = GetComponent<InteractionPromptUI>() ?? gameObject.AddComponent<InteractionPromptUI>();
        _promptUI.Configure(promptHeight);
    }
}
