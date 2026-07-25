using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SkyPrison.Runtime.UI;

/// <summary>
/// 快捷物品按键使用。单例，DontDestroyOnLoad。按 QuickItem1~4（默认数字键1~4）时，
/// 去背包里找 QuickSlotRuntime 绑定的那种道具、消耗一个，逻辑照抄
/// SkyPrisonInventoryInteraction.UseItem（濒死道具拦截/满状态拦截/冷却），但这条路径
/// 不需要背包窗口开着——快捷键本来就是给战斗中用的，背包大概率是关着的。
/// 冷却读写走 ItemCooldownTracker（跟背包菜单"使用"共享同一份冷却状态），每帧直接找
/// HUD上 CleanFG_Slot_XX 底下的 CooldownFill/CooldownText/IconImage 更新（不走
/// SkyPrisonPlayerHUDView.SetQuickSlotCooldown——那条路径跟 SetQuickSlotIcon 一样有
/// 调用到了但完全没反应的诡异问题，原因没查清，见 QuickSlotRuntime.PushToHud 同款
/// 绕过方案）。
/// </summary>
public class QuickSlotUseController : MonoBehaviour
{
    private SkyPrisonInputSettings _inputSettings;
    private SkyPrisonPlayerHUDView_V4_StyleDriven _hudView;
    private float _hudViewRecheckAt;
    private Material _coloredMaterial;
    private Material _grayscaleMaterial;
    private bool _materialsLoaded;

    private static readonly SkyPrisonInputAction[] QuickItemActions =
    {
        SkyPrisonInputAction.QuickItem1,
        SkyPrisonInputAction.QuickItem2,
        SkyPrisonInputAction.QuickItem3,
        SkyPrisonInputAction.QuickItem4,
    };

    private void Awake()
    {
        _inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings")
                       ?? Resources.Load<SkyPrisonInputSettings>("Settings/SkyPrisonInputSettings");
        if (_inputSettings == null)
            Debug.LogWarning("[QuickSlotUseController] 没找到 SkyPrisonInputSettings 资源，1~4快捷键使用会失效（HUD显示刷新不受影响）。");
    }

    private void Update()
    {
        // HUD显示刷新不能依赖 _inputSettings 加载成功——之前这两件事挤在同一个提前
        // return 后面，_inputSettings 一旦是null（资源没找到），连带 TickCooldownDisplay
        // 也永远不会跑，冷却遮罩/读秒/数量角标全部卡死在prefab烤的默认状态，跟实际有没有
        // 在冷却毫无关系。按键使用和显示刷新分开，互不拖累。
        if (_inputSettings != null)
        {
            for (int i = 0; i < QuickSlotRuntime.SlotCount; i++)
            {
                if (_inputSettings.GetActionDown(QuickItemActions[i]))
                    TryUseSlot(i);
            }
        }

        TickCooldownDisplay();
    }

    private void TryUseSlot(int index)
    {
        var runtime = QuickSlotRuntime.Instance;
        ItemDefinition def = runtime != null ? runtime.GetSlot(index) : null;
        if (def == null) return;

        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance != null ? InventoryRuntimeBootstrap.Instance.Inventory : null;
        if (inv == null) return;

        int slotIndex = FindMatchingSlot(inv, def);
        if (slotIndex < 0)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); // 背包里没有这个道具了
            return;
        }

        InventoryItemEntry entry = inv.Slots[slotIndex];

        GameObject playerGo = SkyPrisonPlayerAuthority.Instance != null
            ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject ?? gameObject
            : gameObject;

        // 复活道具：仅在濒死决策阶段可用（快捷栏本来就过滤掉了复活道具，双重保险）
        if (def.general.isReviveItem)
        {
            var dc = playerGo.GetComponentInParent<PlayerDeathDecisionController>();
            if (dc == null || !dc.IsWaitingForChoice) return;
        }

        string blockReason = ItemEffectExecutor.GetBlockReason(def.effects, playerGo);
        if (blockReason != null)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return;
        }

        if (def.cooldown > 0f && ItemCooldownTracker.IsOnCooldown(def.itemKey, out _))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return;
        }

        if (def.cooldown > 0f) ItemCooldownTracker.StartCooldown(def.itemKey, def.cooldown);

        ItemEffectExecutor.Execute(def.effects, playerGo, def);
        if (def.general.consumeOnUse) inv.DiscardSlot(slotIndex, 1);

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
    }

    private static int FindMatchingSlot(InventoryRuntime inv, ItemDefinition def)
    {
        var slots = inv.Slots;
        for (int i = 0; i < slots.Count; i++)
            if (slots[i]?.definition == def) return i;
        return -1;
    }

    // 灰度/彩色材质固定放在 Resources 底下（见 Editor/PatchQuickSlotsHUDCorners.cs 里的
    // HologramMaterialResourcesPath/HologramGrayscaleMaterialResourcesPath 常量，两边路径
    // 字符串必须保持一致——Editor脚本不能被运行时程序集引用，只能各自硬编码一份）。
    private const string ColoredMaterialResourcesPath = "UI/HUD/M_QuickSlotHologramIcon";
    private const string GrayscaleMaterialResourcesPath = "UI/HUD/M_QuickSlotHologramIconGrayscale";

    // HUD实例可能中途被重建（场景切换）——每隔一段时间重新找一次，不用每帧都找。
    private void TickCooldownDisplay()
    {
        if (!_materialsLoaded)
        {
            _coloredMaterial = Resources.Load<Material>(ColoredMaterialResourcesPath);
            _grayscaleMaterial = Resources.Load<Material>(GrayscaleMaterialResourcesPath);
            _materialsLoaded = true;
        }

        if (_hudView == null && Time.unscaledTime >= _hudViewRecheckAt)
        {
            _hudView = FindObjectOfType<SkyPrisonPlayerHUDView_V4_StyleDriven>();
            _hudViewRecheckAt = Time.unscaledTime + 1f;
        }
        if (_hudView == null) return;

        var runtime = QuickSlotRuntime.Instance;
        if (runtime == null) return;

        for (int i = 0; i < QuickSlotRuntime.SlotCount; i++)
            UpdateSlotCooldownVisual(i, runtime.GetSlot(i));
    }

    private void UpdateSlotCooldownVisual(int index, ItemDefinition def)
    {
        Transform wrapper = _hudView.transform.Find($"CleanFG_Slot_{index + 1:00}");
        if (wrapper == null) return;

        bool onCooldown = def != null && def.cooldown > 0f && ItemCooldownTracker.IsOnCooldown(def.itemKey, out _);
        float ratio = 0f;
        float remaining = 0f;
        if (onCooldown)
        {
            remaining = ItemCooldownTracker.GetRemaining(def.itemKey);
            ratio = Mathf.Clamp01(remaining / def.cooldown);
        }

        // 绑定关系按"物品种类"存，不是某一条具体堆叠——用光了之后绑定不会自动解除（这样
        // 以后再捡到同类物品会自动回填，不用重新指定），但视觉上得让玩家看出来"这个槽位
        // 现在没东西可用"，不然图标看着还是正常彩色，按键会白按（TryUseSlot 找不到匹配
        // 堆叠会静默失败）。数量为0时用跟冷却同一套灰度材质处理。
        var (count, anyStackFull) = GetTotalCountAndFullState(def);
        bool outOfStock = def != null && count <= 0;

        Transform fillTf = wrapper.Find("CooldownFill");
        Image fill = fillTf != null ? fillTf.GetComponent<Image>() : null;
        if (fill != null)
        {
            fill.fillAmount = ratio;
            // 不只是靠 fillAmount=0 让 Filled 类型"看起来"没东西——显式关掉 Image，防止
            // 因为 Image Type 没保持住 Filled（Simple 模式会忽略 fillAmount，永远整块
            // 显示）之类的意外情况导致没冷却也一直看着是暗的。
            fill.enabled = onCooldown;
        }

        Transform textTf = wrapper.Find("CooldownText");
        TMP_Text text = textTf != null ? textTf.GetComponent<TMP_Text>() : null;
        if (text != null)
        {
            text.enabled = onCooldown;
            // 跟背包菜单"使用（3.2s）"同一套 F1（一位小数）格式。
            if (onCooldown) text.text = $"{remaining:F1}s";
        }

        Transform iconTf = wrapper.Find("IconImage");
        Image icon = iconTf != null ? iconTf.GetComponent<Image>() : null;
        if (icon != null)
        {
            Material wanted = (onCooldown || outOfStock) ? _grayscaleMaterial : _coloredMaterial;
            if (wanted != null && icon.material != wanted) icon.material = wanted;
        }

        // 数量角标——每帧刷新（不是只在assign的时候设一次），这样背包里数量变化（用掉/
        // 捡到）会自动跟着更新，不用额外接 InventoryRuntime.OnInventoryChanged 事件。
        // × 号缩小下沉、颜色规则都跟 InventorySlotView.Bind 那套一模一样，视觉上要跟
        // 背包格子长得一致。count<=0时不用显示"×0"，灰图标本身已经说明"没有了"。
        Transform countTf = wrapper.Find("CountText");
        TMP_Text countText = countTf != null ? countTf.GetComponent<TMP_Text>() : null;
        if (countText != null)
        {
            bool showCount = def != null && count > 1;
            countText.enabled = showCount;
            if (showCount)
            {
                countText.text = $"<voffset=-0.06em><size=62%>×</size></voffset>{count}";
                countText.color = anyStackFull ? CountFullColor : CountNormalColor;
            }
        }
    }

    // 跟 InventorySlotView 同一套配色：白=普通，橙=堆叠已满。
    private static readonly Color CountNormalColor = new Color(0.92f, 0.92f, 0.94f, 1f);
    private static readonly Color CountFullColor = new Color(1.00f, 0.62f, 0.22f, 1f);

    private static (int total, bool anyStackFull) GetTotalCountAndFullState(ItemDefinition def)
        => QuickSlotRuntime.GetTotalCountAndFullState(def);
}
