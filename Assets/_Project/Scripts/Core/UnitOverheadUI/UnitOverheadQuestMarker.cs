using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NPC 名字前面的任务标记图标。
///
/// 单独一个组件而不是塞进 UnitOverheadUIView：那个类已经在管名字淡入淡出、血条、
/// 状态区、逐相机转向、样式资产应用，再加一块任务逻辑只会让它更难改。
/// 这里只依赖它三个公开字段（unitDefinition / nameRoot / nameText），耦合很浅。
///
/// 图标节点在运行时生成，不需要改预制体——头顶 UI 本来就是按单位动态搭的，
/// 要求美术在每个单位预制体里手加一个 Image 才是不现实的。
/// </summary>
[DisallowMultipleComponent]
public class UnitOverheadQuestMarker : MonoBehaviour
{
    private const string IconObjectName = "QuestMarkerIcon";

    [Tooltip("图标边长（像素）。")]
    // 18 -> 23.4（放大 1.3 倍）。按实际画面调的：世界空间 UI 在 2.5D 正交视角下
    // 离相机固定距离，18 在名字旁边偏小。
    [SerializeField] private float iconSize = 23.4f;

    [Tooltip("图标右边缘和名字左边缘之间的间距。")]
    [SerializeField] private float gap = 4f;

    [Tooltip("重新判定任务状态的间隔。任务状态变化很低频，不需要每帧算。")]
    [SerializeField] private float refreshInterval = 0.5f;

    private UnitOverheadUIView view;
    private Image icon;
    private RectTransform iconRect;
    private float nextRefreshTime;
    private QuestMarker lastMarker;
    private string lastNameText;

    private void Awake()
    {
        view = GetComponent<UnitOverheadUIView>();
    }

    private void OnDisable()
    {
        // 下次启用时强制重算，否则会沿用隐藏期间的旧状态。
        nextRefreshTime = 0f;
        lastMarker = QuestMarker.None;
        lastNameText = null;
    }

    private void LateUpdate()
    {
        if (view == null || view.nameRoot == null || view.nameText == null)
            return;

        bool due = Time.unscaledTime >= nextRefreshTime;

        // 名字变了要立刻重排——图标位置是按文字宽度算的，等下一个刷新周期会看到错位。
        bool nameChanged = view.nameText.text != lastNameText;

        if (!due && !nameChanged)
            return;

        if (due)
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);

        lastNameText = view.nameText.text;
        Refresh();
    }

    private void Refresh()
    {
        QuestMarker marker = QuestMarkerResolver.ResolveForNpc(view.unitDefinition);
        QuestMarkerIconSet set = QuestMarkerIconSet.Instance;

        Sprite sprite = set != null ? set.GetSprite(marker) : null;

        if (!marker.HasValue || sprite == null)
        {
            if (icon != null)
                icon.enabled = false;
            lastMarker = marker;
            return;
        }

        EnsureIcon();
        if (icon == null)
            return;

        icon.enabled = true;
        icon.sprite = sprite;
        icon.color = set.GetColor(marker);

        LayoutIcon();
        lastMarker = marker;
    }

    private void EnsureIcon()
    {
        if (icon != null)
            return;

        Transform existing = view.nameRoot.Find(IconObjectName);
        if (existing != null)
        {
            icon = existing.GetComponent<Image>();
            iconRect = existing as RectTransform;
        }

        if (icon != null)
            return;

        var go = new GameObject(IconObjectName, typeof(RectTransform), typeof(Image));
        go.layer = view.nameRoot.gameObject.layer;

        iconRect = go.GetComponent<RectTransform>();
        iconRect.SetParent(view.nameRoot, false);
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);

        icon = go.GetComponent<Image>();
        icon.raycastTarget = false;

        // 头顶 UI 走 AlwaysOnTop 材质，图标不跟着会被场景几何挡住、而名字不会，
        // 两者就一个可见一个不可见。
        //
        // 必须用 Graphic 版本，不能拿 nameText.materialForRendering——那是 TMP 的
        // 字体材质，着色器采样字体图集、完全不读 sprite，套到 Image 上的结果是一个
        // 实心方块（图标颜色对、形状没了）。
        UnitOverheadUIView.ApplyAlwaysOnTopMaterial(icon);
    }

    /// <summary>
    /// 按文字的实际宽度把图标放到名字左边。
    /// nameText 是居中对齐的，所以文字左边缘在 -preferredWidth/2 处。
    /// </summary>
    private void LayoutIcon()
    {
        if (iconRect == null || view.nameText == null)
            return;

        float textWidth = view.nameText.preferredWidth;
        float x = -(textWidth * 0.5f + gap + iconSize * 0.5f);

        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        iconRect.anchoredPosition = new Vector2(x, 0f);
    }
}
