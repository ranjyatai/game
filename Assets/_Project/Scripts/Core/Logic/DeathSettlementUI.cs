using System.Collections;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家死亡结算界面。
/// 显示本次关卡统计（时长、击杀数、丢失物品数），
/// 玩家确认后回基地。
/// 由 PlayerDeathDecisionController.DeclineReviveAndDie() 调用 Show()。
/// </summary>
public class DeathSettlementUI : MonoBehaviour
{
    private static DeathSettlementUI _instance;

    private CanvasGroup _group;
    private TMP_Text    _titleText;
    private TMP_Text    _timeText;
    private TMP_Text    _killText;
    private TMP_Text    _lostText;
    private Button      _confirmBtn;

    private string _hubScene = "Still Vault";

    // ── 静态入口 ──────────────────────────────────────────────────────────

    public static void Show(int killCount, int lostItemCount, string hubScene = "Still Vault")
    {
        if (_instance == null)
            _instance = Create();
        _instance._hubScene = hubScene;
        _instance.gameObject.SetActive(true);
        _instance.StartCoroutine(_instance.ShowRoutine(killCount, lostItemCount));
    }

    private static DeathSettlementUI Create()
    {
        var go = new GameObject("[DeathSettlementUI]");
        DontDestroyOnLoad(go);
        return go.AddComponent<DeathSettlementUI>();
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        gameObject.SetActive(false);
    }

    private IEnumerator ShowRoutine(int killCount, int lostItemCount)
    {
        // 统计本次时长
        string elapsed = FormatSessionTime();

        if (_titleText != null) _titleText.text  = "— 任务失败 —";
        if (_timeText  != null) _timeText.text   = $"在场时间　{elapsed}";
        if (_killText  != null) _killText.text   = $"击杀数　　{killCount}";
        if (_lostText  != null) _lostText.text   = $"丢失物品　{lostItemCount} 件";

        // 淡入
        yield return StartCoroutine(Fade(0f, 1f, 0.6f));

        // 延迟后激活确认按钮（防止手抖误触）
        yield return new WaitForSecondsRealtime(1.0f);
        if (_confirmBtn != null) _confirmBtn.interactable = true;
    }

    private void OnConfirm()
    {
        if (_confirmBtn != null) _confirmBtn.interactable = false;

        // 清背包 + 清会话立刻做（内存状态），但真正落盘存档（含存档提示 UI）
        // 要等读条揭幕结束、玩家已经站在基地里才触发——读条时候只做读条，不做别的事。
        SaveManager.ClearOnDeath();

        gameObject.SetActive(false);

        // 剧情没解锁到余穹基地之前，死亡不能往那边送——玩家根本没去过，场景里那个
        // 默认出生点可能压根没配。这种情况退回新游戏的初始出生点（同一套数据，
        // MainMenuSettings 里配的那个）。已经解锁就照常回基地，走场景自己的默认出生点。
        string targetScene = _hubScene;
        if (!SaveManager.IsHubUnlocked)
        {
            var settings = Resources.Load<MainMenuSettings>("MainMenuSettings");
            if (settings != null && !string.IsNullOrEmpty(settings.newGameScene))
            {
                targetScene = settings.newGameScene;
                SaveManager.SetSpawnOverride(settings.newGameScene, settings.newGameSpawnPosition, settings.newGameSpawnRotationY);
            }
        }

        // 死亡清背包这个惩罚必须落盘，不受自动保存开关影响——不然关掉开关就能靠强退
        // 找回死前的背包，等于绕过了死亡惩罚（跟古物街 mod 词条锁定在拾取时同一个道理）。
        bool goingToHub = targetScene == _hubScene;
        SceneLoader.LoadScene(targetScene, () =>
        {
            SaveManager.ApplyPendingSpawnOverride();
            if (goingToHub) SaveManager.MarkHubUnlocked();
            SaveManager.Save(false);
        });
    }

    // ── 时间格式 ──────────────────────────────────────────────────────────

    private static string FormatSessionTime()
    {
        var session = SaveManager.Player?.activeSession;
        if (session == null) return "--:--";
        // activeSession 里没存入场时间，这里用系统时间占位；
        // 后续可在 BeginChapter 时记录 enterTime 再算差值
        return "--:--";
    }

    // ── 淡入淡出 ──────────────────────────────────────────────────────────

    private IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null) _group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        if (_group != null) _group.alpha = to;
    }

    // ── UI 构建 ───────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9998;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight  = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // 全屏暗底
        var bg = MakeRect("BG", transform);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 0.92f);

        // 中央卡片
        var card = MakeCard("Card", transform, new Vector2(680f, 800f));

        _titleText = MakeLabel("Title", card, "", 72f, FontStyles.Bold, new Vector2(0f, -80f), new Vector2(560f, 96f));
        _titleText.color = new Color(0.90f, 0.35f, 0.30f, 1f);

        _timeText  = MakeLabel("Time",  card, "", 44f, FontStyles.Normal, new Vector2(0f, -240f), new Vector2(560f, 64f));
        _killText  = MakeLabel("Kill",  card, "", 44f, FontStyles.Normal, new Vector2(0f, -324f), new Vector2(560f, 64f));
        _lostText  = MakeLabel("Lost",  card, "", 44f, FontStyles.Normal, new Vector2(0f, -408f), new Vector2(560f, 64f));

        // 确认按钮
        _confirmBtn = MakeButton("Confirm", card, "返回基地", new Vector2(0f, -620f), new Vector2(440f, 104f));
        _confirmBtn.interactable = false;
        _confirmBtn.onClick.AddListener(OnConfirm);

        TryBindFonts(transform);
    }

    private static RectTransform MakeRect(string n, Transform parent)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static RectTransform MakeCard(string n, Transform parent, Vector2 size)
    {
        var rt = MakeRect(n, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.08f, 0.10f, 0.09f, 0.95f);
        AddBorder(rt, new Color(0.52f, 0.78f, 0.60f, 0.45f));
        return rt;
    }

    private static void AddBorder(RectTransform parent, Color c)
    {
        void Line(string ln, Vector2 aMin, Vector2 aMax, Vector2 oMax)
        {
            var go = new GameObject(ln); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = oMax;
            go.AddComponent<Image>().color = c;
        }
        Line("T", new Vector2(0,1), new Vector2(1,1), new Vector2(0,-1));
        Line("B", new Vector2(0,0), new Vector2(1,0), new Vector2(0, 1));
        Line("L", new Vector2(0,0), new Vector2(0,1), new Vector2(1, 0));
        Line("R", new Vector2(1,0), new Vector2(1,1), new Vector2(-1,0));
    }

    private static TMP_Text MakeLabel(string n, Transform parent, string text,
        float size, FontStyles style, Vector2 pos, Vector2 sizeDelta)
    {
        var rt = MakeRect(n, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.color     = new Color(0.88f, 0.90f, 0.86f, 1f);
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        return tmp;
    }

    private static Button MakeButton(string n, Transform parent, string label, Vector2 pos, Vector2 size)
    {
        var rt = MakeRect(n, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.05f);
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = new Color(1f, 1f, 1f, 0.05f);
        colors.highlightedColor = new Color(0.52f, 0.78f, 0.60f, 0.20f);
        colors.pressedColor     = new Color(0.52f, 0.78f, 0.60f, 0.35f);
        colors.disabledColor    = new Color(1f, 1f, 1f, 0.02f);
        btn.colors = colors;
        btn.targetGraphic = img;
        var txt = MakeRect("Label", rt.transform);
        txt.anchorMin = Vector2.zero; txt.anchorMax = Vector2.one;
        txt.offsetMin = txt.offsetMax = Vector2.zero;
        var tmp = txt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 40f;
        tmp.color     = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        return btn;
    }

    private static void TryBindFonts(Transform root)
    {
        var style = FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
        TMPro.TMP_FontAsset font = style?.defaultTextFont;
#if UNITY_EDITOR
        if (font == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("ZhouFangRiMingTi-2 SDF t:TMP_FontAsset");
            if (guids.Length > 0)
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif
        if (font == null) font = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/ZhouFangRiMingTi-2 SDF");
        if (font == null) return;
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            t.font = font;
    }
}
