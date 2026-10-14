using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 挂在有对话的NPC身上，注册为可交互对象——跟 FacilityInteractable 是同一套模式
/// (IInteractable + InteractableRegistry)，只是打开的不是固定窗口，而是通用对话
/// 窗口，并把这份NPC自己的 NPCDialogueDefinition 传进去决定显示什么。
///
/// 一般不用手动挂：UnitDefinition.dialogue 配好之后，
/// UnitDefinitionRuntimeApplier 生成单位时会自动挂上并把 dialogue 字段设好，
/// 不需要在地图上一个个手动拖组件。
/// </summary>
public sealed class NPCDialogueInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private NPCDialogueDefinition dialogue;
    [SerializeField] private string customLabel; // 留空则用默认标签
    [SerializeField] private UnitDefinition npcUnitDefinition; // 用于"显示任务列表"按归属NPC筛选

    [Header("头顶名字淡入淡出")]
    [Tooltip("玩家进入这个范围，头顶名字淡入；离开则淡出。用户明确要求这个节奏" +
             "\"靠近淡入、远离淡出\"，不是瞬间切换。")]
    [SerializeField] private float nameFadeRange = 4f;

    private UnitOverheadUIView _overheadView;
    private bool _nameFadedIn;

    // Resources 路径——跟 SkyPrisonShopDebugHotkey/WorkshopRepairDebugHotkey
    // 同一套"Resources镜像"约定，运行时不依赖 AssetDatabase。
    private const string DialogueWindowResourcesPath = "UI/Window/PF_NPCDialogue";

    public void SetDialogue(NPCDialogueDefinition def) => dialogue = def;
    public void SetNpcUnitDefinition(UnitDefinition ud) => npcUnitDefinition = ud;

    // ── IInteractable ─────────────────────────────────────────────────────

    public Vector3 InteractPosition => transform.position;

    public string InteractLabel
    {
        get
        {
            if (!string.IsNullOrEmpty(customLabel)) return customLabel;

            // 之前这里是硬编码中文拼字符串，完全没接字典表——切到日文/英文，交互
            // 提示还是显示"与xx对话"。改成跟任务系统那批文字一样查UILocalizationTable。
            string npcDisplayName = ResolveNpcDisplayName();
            var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");

            if (string.IsNullOrEmpty(npcDisplayName))
                return locTable != null ? locTable.Get("npc_interact_talk_generic", "交谈") : "交谈";

            string template = locTable != null ? locTable.Get("npc_interact_talk_with", "与{0}对话") : "与{0}对话";
            try { return string.Format(template, npcDisplayName); }
            catch { return $"与{npcDisplayName}对话"; } // 模板占位符被手滑写错时别崩，退回原样
        }
    }

    public bool CanInteract => dialogue != null;

    private string ResolveNpcDisplayName()
    {
        // 这个提示条用的是旧版 UnityEngine.UI.Text，接不上 TMP 的注音预处理器——
        // 名字字段里可能带 {A|B} 这种给注音用的内部标记，不去掉的话玩家会直接
        // 看到原始花括号语法。这里不需要真的标出注音，只要干净文本。
        if (dialogue != null)
        {
            string name = dialogue.GetLocalizedName("");
            if (!string.IsNullOrEmpty(name)) return SkyPrisonRubyTextProcessor.StripToPlainText(name);
        }

        if (npcUnitDefinition != null)
        {
            string name = npcUnitDefinition.GetLocalizedDisplayName();
            if (!string.IsNullOrEmpty(name)) return SkyPrisonRubyTextProcessor.StripToPlainText(name);
        }

        return "";
    }

    public void Interact()
    {
        if (dialogue == null)
        {
            Debug.LogWarning($"[NPCDialogueInteractable] {name} 没有配置 NPCDialogueDefinition。");
            return;
        }

        var manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (manager == null) return;

        var prefab = Resources.Load<GameObject>(DialogueWindowResourcesPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[NPCDialogueInteractable] 找不到对话窗口 prefab（Resources/{DialogueWindowResourcesPath}）——" +
                "先在编辑器菜单 Tools/Sky Prison/UI/Create NPC Dialogue Window 生成一次。");
            return;
        }

        // Open 音效以前是统一交互控制器按下E键时无差别硬播的，跟物品/设施合并成
        // 一套控制器之后那个硬播的调用被去掉了，改成各类型自己在 Interact() 里播，
        // 保持原有的"按E开对话有声音"体验不受统一影响。
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        GameObject instance = manager.Open(prefab);
        var controller = instance != null ? instance.GetComponent<NPCDialogueWindowController>() : null;
        controller?.Begin(dialogue, npcUnitDefinition);

        // "对话"类任务目标(cond_has_talked_to_npc)要用的计数器——跟击杀计数
        // (kill_unit_{unitId})是同一套"存档永久累加"惯例，只关心"发生过没有"，
        // 这里只要对话窗口真的打开了就记一次，不用等玩家把对话聊完。
        if (controller != null && npcUnitDefinition != null &&
            !string.IsNullOrWhiteSpace(npcUnitDefinition.unitId) && SaveManager.Player != null)
        {
            SaveManager.Player.IncrementCounter($"talked_npc_{npcUnitDefinition.unitId}", 1);

            // 结算界面不在这里(对话一打开)就自动弹出——用户明确要求要跟接任务一样，
            // 走"点开任务相关选项→在列表里选中那条具体任务"的路径，见
            // NPCDialogueWindowController.OnQuestSelected。这里只负责记对话计数器。
        }
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void OnEnable()  => InteractableRegistry.Register(this);
    private void OnDisable() => InteractableRegistry.Unregister(this);

    private void Update()
    {
        if (dialogue == null) return;

        if (_overheadView == null)
        {
            _overheadView = GetComponent<UnitOverheadUIView>();
            if (_overheadView == null)
                _overheadView = GetComponentInChildren<UnitOverheadUIView>(true);
            if (_overheadView == null) return;
            _overheadView.SetNameProximityFadeMode(true);
        }

        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return;

        float sqrDist = (player.transform.position - transform.position).sqrMagnitude;
        bool inRange = sqrDist <= nameFadeRange * nameFadeRange;

        if (inRange == _nameFadedIn) return;
        _nameFadedIn = inRange;
        _overheadView.SetNameFadeTarget(inRange);
    }
}
