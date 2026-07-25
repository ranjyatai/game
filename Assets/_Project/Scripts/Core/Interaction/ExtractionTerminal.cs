using System.Collections;
using UnityEngine;

/// <summary>
/// 撤离终端。实现 IInteractable，放在章节场景的出口处。
/// 玩家走近按 E → 弹出撤离确认 → 确认后结算并返回基地。
/// </summary>
[DisallowMultipleComponent]
public class ExtractionTerminal : MonoBehaviour, IInteractable
{
    [Header("交互")]
    [SerializeField] private string interactLabel = "撤离终端";
    [SerializeField] private float  interactRange = 2.5f;   // 仅用于 Gizmo 显示，实际范围由 InteractionController 决定

    [Header("结算")]
    [SerializeField] private string hubScene      = "Still Vault";
    [SerializeField] private string currencyId    = "token";

    // ── IInteractable ─────────────────────────────────────────────────────

    public Vector3 InteractPosition => transform.position;
    public string  InteractLabel    => interactLabel;
    public bool    CanInteract      => !_confirmOpen;

    public void Interact()
    {
        if (_confirmOpen) return;
        ShowConfirm();
    }

    // ── 生命周期 ─────────────────────────────────────────────────────────

    private void OnEnable()  => InteractableRegistry.Register(this);
    private void OnDisable() => InteractableRegistry.Unregister(this);

    // ── 确认弹窗（程序化，复用风格与 DeathSettlementUI 一致）────────────

    private bool             _confirmOpen;
    private GameObject       _confirmRoot;

    private void ShowConfirm()
    {
        if (_confirmRoot != null) return;
        _confirmOpen = true;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        // 创建 ScreenSpaceOverlay 弹窗
        _confirmRoot = new GameObject("[ExtractionConfirm]");
        var canvas = _confirmRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9990;
        _confirmRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
        _confirmRoot.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // 半透明背景遮罩
        var bg = new GameObject("BG").AddComponent<RectTransform>();
        bg.SetParent(_confirmRoot.transform, false);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.6f);

        // 面板
        var panel = MakeRect("Panel", _confirmRoot.transform,
            new Vector2(0.35f, 0.35f), new Vector2(0.65f, 0.65f));

        var panelBg = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        panelBg.color = new Color(0.06f, 0.08f, 0.10f, 0.97f);

        MakeTMP("Title", panel, new Vector2(0.5f, 0.80f), 26f, Color.white).text  = "撤离终端";
        MakeTMP("Body",  panel, new Vector2(0.5f, 0.58f), 20f,
            new Color(0.7f, 0.75f, 0.8f)).text = "确认撤离并返回基地？\n本次获得的物品将全部保留。";

        var confirmBtn = MakeButton("Confirm", panel,
            new Vector2(0.08f, 0.10f), new Vector2(0.48f, 0.30f),
            "确认撤离", new Color(0.28f, 0.75f, 0.52f, 0.95f), Color.black);
        confirmBtn.onClick.AddListener(OnConfirm);

        var cancelBtn = MakeButton("Cancel", panel,
            new Vector2(0.52f, 0.10f), new Vector2(0.92f, 0.30f),
            "取消", new Color(0.22f, 0.22f, 0.24f, 0.95f), Color.white);
        cancelBtn.onClick.AddListener(OnCancel);
    }

    private void OnConfirm()
    {
        CloseConfirm();
        StartCoroutine(ExtractRoutine());
    }

    private void OnCancel()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
        CloseConfirm();
    }

    private void CloseConfirm()
    {
        _confirmOpen = false;
        if (_confirmRoot != null) { Destroy(_confirmRoot); _confirmRoot = null; }
    }

    // ── 撤离流程 ─────────────────────────────────────────────────────────

    private IEnumerator ExtractRoutine()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);

        // 1. 标记章节完成
        MarkChapterCleared();

        // 2. 收集本局统计
        var player   = SkyPrisonPlayerAuthority.Instance?.CurrentPlayerGameObject;
        var tracker  = player?.GetComponent<SessionStatsTracker>();
        int kills    = tracker?.KillCount ?? 0;
        int items    = CountItemsGained();
        long currency = CountCurrencyGained();

        yield return new WaitForSeconds(0.3f);

        // 3. 显示撤离结算（真正落盘存档延后到玩家确认、回到基地读条揭幕之后再做）
        ExitSettlementUI.Show(kills, items, currency, currencyId, hubScene);
    }

    // ── 章节完成标记 ─────────────────────────────────────────────────────

    private static void MarkChapterCleared()
    {
        var save = SaveManager.Player;
        if (save == null) return;

        string chapterId = save.activeSession?.chapterId;
        if (string.IsNullOrEmpty(chapterId)) return;

        save.SetWorldFlag(chapterId, "cleared");
        save.activeSession = null; // 清除进行中的会话
    }

    // ── 本局统计（背包新物品数、货币增量由结算UI显示）────────────────────

    private static int CountItemsGained()
    {
        // 用背包里标记为 isNew 的物品数量作为"本次获得"
        var inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null) return 0;
        int count = 0;
        foreach (var slot in inv.Slots)
            if (slot != null && slot.isNew) count += slot.count;
        return count;
    }

    private long CountCurrencyGained()
    {
        // 暂时显示当前持有量（后续可改为进本前后差值）
        return CurrencyRuntime.Instance?.Get(currencyId) ?? 0;
    }

    // ── UI 工具 ──────────────────────────────────────────────────────────

    private static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static TMPro.TMP_Text MakeTMP(string name, RectTransform parent, Vector2 anchor, float size, Color color)
    {
        var rt = MakeRect(name, parent, anchor, anchor);
        rt.sizeDelta = new Vector2(400f, 50f);
        var tmp = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.fontSize = size; tmp.color = color;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    private static UnityEngine.UI.Button MakeButton(
        string name, RectTransform parent,
        Vector2 amin, Vector2 amax,
        string label, Color bgColor, Color textColor)
    {
        var rt  = MakeRect(name, parent, amin, amax);
        rt.gameObject.AddComponent<UnityEngine.UI.Image>().color = bgColor;
        var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        var lbl = MakeTMP("Label", rt, new Vector2(0.5f, 0.5f), 18f, textColor);
        lbl.text = label;
        return btn;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
