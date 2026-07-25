using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 世界地图选择窗口。
/// UI 结构（编辑器搭建）：
///   mapImage     — 地图背景图（RawImage 或 Image）
///   nodeContainer — 与 mapImage 同大小的 RectTransform，章节节点放这里
///   detailPanel  — 右侧详情面板：名称/描述/目标列表/进入按钮
/// </summary>
public class WorldMapWindowController : SkyPrisonBaseWindowController
{
    [Header("章节列表")]
    [SerializeField] private List<ChapterDefinition> chapters = new List<ChapterDefinition>();

    [Header("地图区域")]
    [SerializeField] private RectTransform nodeContainer;   // 节点父节点，与地图图片等大
    [SerializeField] private GameObject    mapNodePrefab;   // 章节节点预制体

    [Header("详情面板")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private Text       detailName;
    [SerializeField] private Text       detailDesc;
    [SerializeField] private Text       detailDifficulty;
    [SerializeField] private Transform  objectiveContainer;
    [SerializeField] private GameObject objectiveRowPrefab;
    [SerializeField] private Button     enterButton;
    [SerializeField] private Text       enterButtonLabel;

    private ChapterDefinition _selected;
    private readonly List<GameObject> _nodeViews    = new List<GameObject>();
    private readonly List<GameObject> _objectiveRows = new List<GameObject>();
    private SkyPrisonListGamepadNav _gamepadNav;

    protected override string WindowId => "world_map";

    // 手柄确认走的是 SkyPrisonListGamepadNav 里硬编码的 A 键（JoystickButton0），不是
    // Interact 那个绑定动作——Interact 的手柄键在共享输入资源里跟"跳跃"共用 A 键，
    // 不能直接拿来配这个提示（会暗示"按 Interact 键"，但实际生效的是固定 A 键，两者
    // 未必一致）。手动给键鼠和手柄各配一个图标，不走 Action() 解析。
    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        return new[]
        {
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "选择", label = L("ui_hint_select_chapter", "选择章节") },
            SkyPrisonWindowHint.Icon("keyboard/esc", "Esc", L("ui_hint_close", "关闭")),
        };
    }

    // ── 生命周期 ─────────────────────────────────────────────────────────

    protected override void OnWindowOpen()
    {
        if (_gamepadNav == null) _gamepadNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();
        BuildNodes();
        SetDetailVisible(false);
        RefreshGamepadTargets();
    }

    // 手柄光标能选的目标——地图节点 + 进入按钮，节点/详情随时会重建，重建后都要重喂一次。
    private void RefreshGamepadTargets()
    {
        if (_gamepadNav == null) return;
        var targets = new List<Button>();
        foreach (var node in _nodeViews)
        {
            if (node == null) continue;
            var btn = node.GetComponent<Button>() ?? node.GetComponentInChildren<Button>();
            if (btn != null) targets.Add(btn);
        }
        if (enterButton != null && enterButton.gameObject.activeInHierarchy) targets.Add(enterButton);
        _gamepadNav.SetTargets(targets);
    }

    protected override void OnWindowClose()
    {
        _selected = null;
    }

    // ── 节点构建 ─────────────────────────────────────────────────────────

    private void BuildNodes()
    {
        foreach (var go in _nodeViews) if (go) Destroy(go);
        _nodeViews.Clear();

        if (nodeContainer == null || mapNodePrefab == null) return;

        foreach (var chapter in chapters)
        {
            if (chapter == null) continue;

            var node = Instantiate(mapNodePrefab, nodeContainer);
            _nodeViews.Add(node);

            // 把节点放到地图图片上的对应位置（anchoredPosition 按百分比换算）
            if (node.TryGetComponent<RectTransform>(out var rt))
            {
                rt.anchorMin = rt.anchorMax = chapter.mapNodePosition;
                rt.anchoredPosition = Vector2.zero;
            }

            // 节点名称
            SetChildText(node, "NodeLabel", chapter.displayName);

            // 完成标记
            bool cleared = IsCleared(chapter);
            SetChildActive(node, "ClearedMark", cleared);

            // 锁定状态
            bool locked = IsLocked(chapter);
            SetChildActive(node, "LockedOverlay", locked);

            // 点击
            var btn = node.GetComponent<Button>() ?? node.GetComponentInChildren<Button>();
            if (btn != null)
            {
                btn.interactable = !locked;
                var captured = chapter;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectChapter(captured));
            }
        }
    }

    // ── 章节选择 ─────────────────────────────────────────────────────────

    private void SelectChapter(ChapterDefinition chapter)
    {
        _selected = chapter;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        SetDetailVisible(true);
        PopulateDetail(chapter);
        RefreshGamepadTargets(); // 详情面板的"进入关卡"按钮这时候才出现，得重新喂一次目标
    }

    private void PopulateDetail(ChapterDefinition chapter)
    {
        if (detailName      != null) detailName.text      = chapter.displayName;
        if (detailDesc      != null) detailDesc.text      = chapter.description;
        if (detailDifficulty!= null) detailDifficulty.text = $"难度  {"★★★★★".Substring(0, chapter.difficulty)}";

        // 目标列表
        foreach (var go in _objectiveRows) if (go) Destroy(go);
        _objectiveRows.Clear();
        if (objectiveContainer != null && objectiveRowPrefab != null)
        {
            foreach (var obj in chapter.objectives)
            {
                var row = Instantiate(objectiveRowPrefab, objectiveContainer);
                _objectiveRows.Add(row);
                SetChildText(row, "ObjectiveText", $"◆  {obj}");
                var tx = row.GetComponentInChildren<Text>();
                if (tx != null) tx.text = $"◆  {obj}";
            }
        }

        // 进入按钮
        if (enterButton != null)
        {
            bool canEnter = !IsLocked(chapter) && !string.IsNullOrEmpty(chapter.sceneName);
            enterButton.interactable = canEnter;
            if (enterButtonLabel != null)
                enterButtonLabel.text = IsCleared(chapter) ? "再次进入" : "进入关卡";
            enterButton.onClick.RemoveAllListeners();
            enterButton.onClick.AddListener(OnEnterChapter);
        }
    }

    // ── 进入章节 ─────────────────────────────────────────────────────────

    private void OnEnterChapter()
    {
        if (_selected == null || string.IsNullOrEmpty(_selected.sceneName)) return;

        // 写入当前会话数据
        if (SaveManager.Player != null)
        {
            SaveManager.Player.activeSession = new ChapterSessionData
            {
                chapterId = _selected.chapterId,
                mapId     = _selected.sceneName,
            };
        }

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
        Close();

        // 加载章节场景（场景里的 UnitSpawner / LootDrop 负责实际初始化）
        SceneLoader.LoadScene(_selected.sceneName);
    }

    // ── 解锁 / 完成状态查询 ──────────────────────────────────────────────

    private static bool IsCleared(ChapterDefinition chapter)
    {
        return SaveManager.Player
            ?.HasWorldFlag(chapter.chapterId, "cleared") ?? false;
    }

    private static bool IsLocked(ChapterDefinition chapter)
    {
        if (chapter.unlockAfterChapter != null && !IsCleared(chapter.unlockAfterChapter))
            return true;
        if (chapter.requiredBaseLevel > 0)
        {
            // 以余穹中枢（CommandCenter）等级代表基地总等级
            int baseLevel = BaseManager.Instance?.GetLevel(FacilityType.CommandCenter) ?? 0;
            if (baseLevel < chapter.requiredBaseLevel) return true;
        }
        return false;
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private void SetDetailVisible(bool visible)
    {
        if (detailPanel != null) detailPanel.SetActive(visible);
    }

    private static void SetChildText(GameObject root, string childName, string text)
    {
        if (root == null) return;
        var t = root.transform.Find(childName);
        if (t != null) { var tx = t.GetComponent<Text>(); if (tx) tx.text = text; }
    }

    private static void SetChildActive(GameObject root, string childName, bool active)
    {
        if (root == null) return;
        var t = root.transform.Find(childName);
        if (t != null) t.gameObject.SetActive(active);
    }
}
