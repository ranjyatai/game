using System;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 玩家快捷物品槽运行时。单例，DontDestroyOnLoad。
/// 跟 EquipmentRuntime 不一样：装备是"穿在身上"，物理上从背包挪走；快捷物品只是个
/// "快捷方式"——绑的是 ItemDefinition（物品种类），不是某一个具体的 InventoryItemEntry。
/// 物品本身还留在背包里，会被消耗、叠加、排序，快捷槽只是记着"槽位N对应哪种道具"，
/// 真正使用时再去背包里找同类物品来消耗。这样绑定关系不会因为背包整理/排序失效。
/// </summary>
public class QuickSlotRuntime : MonoBehaviour
{
    public static QuickSlotRuntime Instance { get; private set; }

    public const int SlotCount = 4;

    /// <summary>槽位绑定变化（index, 新绑定的物品定义，null=清空）。</summary>
    public static event Action<int, ItemDefinition> OnSlotChanged;

    private readonly ItemDefinition[] _slots = new ItemDefinition[SlotCount];

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public ItemDefinition GetSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return null;
        return _slots[index];
    }

    /// <summary>某个物品种类当前在背包里的总数量，以及是否有任意一堆已经堆叠满——
    /// 绑定关系按种类存，用光了不会自动解除绑定，UI（HUD图标变灰/角色面板行）靠这个
    /// 方法判断"这个槽位现在还有没有东西可用"。QuickSlotUseController 和
    /// CharacterPanelController 共用同一份口径，不各自算一遍。</summary>
    public static (int total, bool anyStackFull) GetTotalCountAndFullState(ItemDefinition def)
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance != null ? InventoryRuntimeBootstrap.Instance.Inventory : null;
        if (inv == null || def == null) return (0, false);

        int total = 0;
        bool anyStackFull = false;
        var slots = inv.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            InventoryItemEntry entry = slots[i];
            if (entry?.definition != def) continue;
            total += entry.count;
            if (entry.IsStackFull) anyStackFull = true;
        }
        return (total, anyStackFull);
    }

    public void AssignSlot(int index, ItemDefinition definition)
    {
        if (index < 0 || index >= SlotCount || definition == null) return;

        // 同一种物品最多只占一个快捷槽——不然背包格子右上角的"绑在几号"角标没法优雅
        // 显示（同种物品可能同时绑1/2/3/4，角标要塞下"1,2,3,4"这种文字，怎么调都难看）。
        // 指定到新槽位时，把这种物品从其它已经绑的槽位上挪走（清空），相当于"移动"
        // 绑定而不是"追加"绑定。
        for (int i = 0; i < SlotCount; i++)
        {
            if (i == index || _slots[i] != definition) continue;
            _slots[i] = null;
            OnSlotChanged?.Invoke(i, null);
            PushToHud(i, null);
        }

        _slots[index] = definition;
        OnSlotChanged?.Invoke(index, definition);
        PushToHud(index, definition);
    }

    public void ClearSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        if (_slots[index] == null) return; // 本来就是空的，不用重复触发音效/事件

        _slots[index] = null;
        OnSlotChanged?.Invoke(index, null);
        PushToHud(index, null);
        // 放在这个唯一的清空入口里，不用等每个未来调用它的UI各自去补音效——跟
        // AssignSlot那边的 QuickSlotAssign 是同一动作的另一个方向。
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.UnassignQuickSlot);
    }

    // ── 战斗HUD图标 ───────────────────────────────────────────────────────
    // 原计划走 SkyPrisonPlayerHUDView_V4_StyleDriven.SetQuickSlotIcon（组件自带的方法，
    // 靠 Inspector 里手动接好的 quickSlotIcons 数组）——诊断了很久，确认那条链路上
    // FindObjectOfType 能正确找到实例、方法本身也确实被调用到了，但方法内部的任何
    // 分支（无论警告还是成功日志）都没有任何反应，原因没能查清。放弃排查这个诡异
    // 现象，改成完全绕开它：直接从找到的 HUD 实例出发，往下找到 Slot_0X/Icon 这个
    // 具体的 Image 物体，自己设置 sprite/enabled，不经过那个方法。
    private static void PushToHud(int index, ItemDefinition definition)
    {
        var view = UnityEngine.Object.FindObjectOfType<SkyPrisonPlayerHUDView_V4_StyleDriven>();
        if (view == null)
        {
            Debug.LogWarning($"[QuickSlotRuntime] PushToHud({index})：场景里没找到 SkyPrisonPlayerHUDView_V4_StyleDriven，图标推不过去。");
            return;
        }

        // 图标/柔光/冷却遮罩都不再挂在 Slot_0X 底下——PatchQuickSlotsHUDCorners 已经把它们
        // 挪成 QuickSlotsArea 的直接子物体 CleanFG_Slot_XX（"不进色收差RT捕获"的排除命名
        // 规则，跟 CleanBG_ 同一套机制；三层内容合并进同一个物体是为了避免多个 CleanFG_
        // 顶层物体抢"排到最后一层"导致的闪烁），所以直接从 view.transform 本身找。
        string wrapperName = $"CleanFG_Slot_{index + 1:00}";
        Transform wrapper = view.transform.Find(wrapperName);
        if (wrapper == null)
        {
            Debug.LogWarning($"[QuickSlotRuntime] PushToHud({index})：在 {view.name} 底下没找到 {wrapperName}。");
            return;
        }

        Transform iconTf = wrapper.Find("IconImage");
        Image icon = iconTf != null ? iconTf.GetComponent<Image>() : null;
        if (icon == null)
        {
            Debug.LogWarning($"[QuickSlotRuntime] PushToHud({index})：{wrapperName} 底下没找到 IconImage 的 Image 组件。");
            return;
        }

        Sprite sprite = definition != null ? definition.icon : null;
        bool hasItem = sprite != null;
        icon.sprite = sprite;
        icon.enabled = hasItem;

        // preserveAspect 是"完整装进框内"（长边对齐、短边留白），不是"撑满裁切"——道具
        // 图标只要不是正方形，短的那个方向就会露出两边背景。这里改成按图标真实宽高比
        // 手动算一个"至少盖满槽位两个方向"的尺寸（哪个方向缩得更小就以哪个为准），交给
        // CleanFG_Slot_XX 上的 RectMask2D 把多出来的部分裁掉，preserveAspect 保持
        // true 只是保险（尺寸已经是按原比例算的，不会再被它二次收缩）。
        if (hasItem)
        {
            RectTransform wrapperRt = wrapper as RectTransform;
            RectTransform iconRt = icon.rectTransform;
            Vector2 boxSize = wrapperRt != null ? wrapperRt.sizeDelta : Vector2.zero;
            Vector2 spriteSize = sprite.rect.size;
            if (boxSize.x > 0f && boxSize.y > 0f && spriteSize.x > 0f && spriteSize.y > 0f)
            {
                // 1.35：在"刚好盖满槽位"的基础上再放大一点，图标看着更有分量。
                const float ZoomFactor = 1.35f;
                float coverScale = Mathf.Max(boxSize.x / spriteSize.x, boxSize.y / spriteSize.y) * ZoomFactor;
                iconRt.sizeDelta = spriteSize * coverScale;
            }
        }

        Debug.Log($"[QuickSlotRuntime] PushToHud({index}) 直接设置成功：icon={icon.name} sprite={(sprite != null ? sprite.name : "null")} enabled={icon.enabled}");
    }

    // 读档/开局把4个槽位一次性同步给HUD——HUD 模块自己可能在这之后才建好，所以也在
    // SkyPrisonRuntimeUIDriver 里的HUD实例化流程走完之后再补一次（见该文件调用点）。
    public void PushAllToHud()
    {
        for (int i = 0; i < SlotCount; i++) PushToHud(i, _slots[i]);
    }

    // ── 存档 ─────────────────────────────────────────────────────────────
    // 跟 InventoryRuntime.Serialize 一样按 itemKey 存字符串，读档时通过 ItemRegistry
    // 查回 ItemDefinition——存的是"绑定关系"，不是物品数量，物品数量本来就在
    // InventoryRuntime 自己的存档里。

    public string[] Serialize()
    {
        var result = new string[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            result[i] = _slots[i] != null ? _slots[i].itemKey : null;
        return result;
    }

    public void Deserialize(string[] itemKeys, ItemRegistry registry)
    {
        for (int i = 0; i < SlotCount; i++) _slots[i] = null;
        if (itemKeys == null || registry == null) return;

        for (int i = 0; i < SlotCount && i < itemKeys.Length; i++)
        {
            if (string.IsNullOrEmpty(itemKeys[i])) continue;
            var def = registry.Find(itemKeys[i]);
            if (def != null) _slots[i] = def;
        }
        PushAllToHud();
    }
}
