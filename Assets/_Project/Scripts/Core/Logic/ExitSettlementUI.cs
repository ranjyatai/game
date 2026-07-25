using System.Collections;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家正常撤离结算界面（非死亡）。
/// 显示：在场时间、击杀数、获得物品数、获得货币数。
/// 由触发撤离出口的逻辑调用 ExitSettlementUI.Show(...)。
/// 玩家确认后回基地，物品全部保留。
/// </summary>
public class ExitSettlementUI : MonoBehaviour
{
    private static ExitSettlementUI _instance;

    private CanvasGroup _group;
    private TMP_Text    _titleText;
    private TMP_Text    _timeText;
    private TMP_Text    _killText;
    private TMP_Text    _itemsText;
    private TMP_Text    _currencyText;
    private Button      _confirmBtn;

    private string _hubScene = "Still Vault";

    // ── 静态入口 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 触发撤离结算。所有统计值由调用方（撤离触发器）汇总后传入。
    /// </summary>
    public static void Show(
        int    killCount,
        int    itemsGained,
        long   currencyGained,
        string currencyId   = "token",
        string hubScene     = "Still Vault")
    {
        if (_instance == null) _instance = Create();
        _instance._hubScene = hubScene;
        _instance.gameObject.SetActive(true);
        _instance.StartCoroutine(_instance.ShowRoutine(killCount, itemsGained, currencyGained, currencyId));
    }

    private static ExitSettlementUI Create()
    {
        var go = new GameObject("[ExitSettlementUI]");
        DontDestroyOnLoad(go);
        return go.AddComponent<ExitSettlementUI>();
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        gameObject.SetActive(false);
    }

    private IEnumerator ShowRoutine(int killCount, int itemsGained, long currencyGained, string currencyId)
    {
        string elapsed = FormatSessionTime();

        if (_titleText    != null) _titleText.text    = "— 撤离成功 —";
        if (_timeText     != null) _timeText.text     = $"在场时间　　{elapsed}";
        if (_killText     != null) _killText.text     = $"击杀数　　　{killCount}";
        if (_itemsText    != null) _itemsText.text    = $"获得物品　　{itemsGained} 件";
        if (_currencyText != null) _currencyText.text = currencyGained > 0
            ? $"获得货币　　{currencyGained} {currencyId}"
            : "";

        // 淡入
        if (_group != null)
        {
            _group.alpha = 0f;
            float t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime * 2f; _group.alpha = Mathf.Clamp01(t); yield return null; }
        }

        if (_confirmBtn != null)
        {
            _confirmBtn.interactable = true;
            _confirmBtn.onClick.RemoveAllListeners();
            _confirmBtn.onClick.AddListener(OnConfirm);
        }
    }

    private void OnConfirm()
    {
        // 重置本局统计
        var player = SkyPrisonPlayerAuthority.Instance?.CurrentPlayerGameObject;
        player?.GetComponent<SessionStatsTracker>()?.ResetSession();

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
        gameObject.SetActive(false);
        // 章节完成 + 会话清空在 ExtractionTerminal.MarkChapterCleared() 里已经做过了；
        // 真正的存档（含存档提示 UI）要等读条揭幕结束、玩家已经站在基地里才触发。
        SceneLoader.LoadScene(_hubScene, () =>
        {
            SaveManager.MarkHubUnlocked();
            SaveManager.AutoSave(false);
        });
    }

    // ── UI 构建（程序化，和 DeathSettlementUI 同模式）────────────────────

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();

        var bg = CreateRect("BG", transform);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

        var panel = CreateRect("Panel", transform);
        panel.anchorMin = new Vector2(0.3f, 0.25f);
        panel.anchorMax = new Vector2(0.7f, 0.75f);
        panel.offsetMin = panel.offsetMax = Vector2.zero;

        _titleText    = CreateTMP("Title",    panel, new Vector2(0.5f, 0.85f), 32, Color.white);
        _timeText     = CreateTMP("Time",     panel, new Vector2(0.5f, 0.68f), 22, Color.white);
        _killText     = CreateTMP("Kill",     panel, new Vector2(0.5f, 0.56f), 22, Color.white);
        _itemsText    = CreateTMP("Items",    panel, new Vector2(0.5f, 0.44f), 22, Color.white);
        _currencyText = CreateTMP("Currency", panel, new Vector2(0.5f, 0.32f), 22, new Color(1f, 0.9f, 0.4f));

        var btnGo = CreateRect("ConfirmBtn", panel);
        btnGo.anchorMin = new Vector2(0.25f, 0.08f);
        btnGo.anchorMax = new Vector2(0.75f, 0.20f);
        btnGo.offsetMin = btnGo.offsetMax = Vector2.zero;
        btnGo.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.8f, 0.55f, 0.85f);
        _confirmBtn = btnGo.gameObject.AddComponent<Button>();
        var btnLabel = CreateTMP("Label", btnGo, new Vector2(0.5f, 0.5f), 20, Color.black);
        if (btnLabel != null) btnLabel.text = "返回基地";
        _confirmBtn.interactable = false;
    }

    private static string FormatSessionTime()
    {
        float t = Time.timeSinceLevelLoad;
        int m = (int)(t / 60f);
        int s = (int)(t % 60f);
        return $"{m:D2}:{s:D2}";
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text CreateTMP(string name, RectTransform parent, Vector2 anchorCenter, float fontSize, Color color)
    {
        var rt = CreateRect(name, parent);
        rt.anchorMin = rt.anchorMax = anchorCenter;
        rt.sizeDelta = new Vector2(500f, 40f);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }
}
