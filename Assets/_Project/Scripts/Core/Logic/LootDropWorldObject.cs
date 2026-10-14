using UnityEngine;

/// <summary>
/// 世界掉落物——现在直接实现 IInteractable，跟NPC/设施走同一套
/// InteractableRegistry+SkyPrisonInteractionController，不再各自维护一份独立的
/// 范围检测/E键响应/切换目标(之前拾取和NPC交互是两条完全独立的通道，同时出现在
/// 附近的时候会同时弹提示、同时响应E键，用户明确要求统一成一套，不能各扫各的)。
/// </summary>
[DisallowMultipleComponent]
public class LootDropWorldObject : MonoBehaviour, IInteractable
{
    [Header("掉落数据")]
    [SerializeField] private ItemDefinition itemDefinition;
    [SerializeField] private int count = 1;

    // 2026-08-25：之前这里只存"是哪个物品定义+多少个"，装备类物品（武器/护甲）
    // 丢弃时身上的耐久/弹匣弹药/染色/改装件这些per-实例数据全部没跟着一起打包，
    // 捡回来靠 InventoryRuntime.AddItem 重新 new 一个全新实例，等于每次丢弃都在
    // 清空这些数据（弹匣弹药凭空消失就是这个漏洞的直接后果）。复用存档系统已有的
    // SavedItemEntry 当快照容器（字段刚好完全对得上，不用另外定义一份），只在真的
    // 丢弃"某一个具体实例"（武器/护甲）时才赋值；纯堆叠、无状态的消耗品/弹药/材料
    // 走原来的 itemDefinition+count 那条路，不需要快照。
    [SerializeField] private SavedItemEntry instanceSnapshot;

    [Header("世界表现")]
    [SerializeField] private bool autoDestroyIfInvalid = false;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    public int Count => count;

    /// <summary>掉落物承载的物品。</summary>
    public ItemDefinition Item => itemDefinition;

    // ── IInteractable ─────────────────────────────────────────────────────
    // InteractLabel/CanInteract 这两个是给"退化成普通交互提示"的场景兜底用的——
    // 正常情况下 SkyPrisonInteractionController 认出目标是 LootDropWorldObject
    // 之后，会改用 SkyPrisonItemPickupController 那套带图标/品级颜色的提示条，
    // 不会真的显示这行纯文字。

    public Vector3 InteractPosition => transform.position;

    // Item.displayName 里可能带 {A|B} 这种给注音用的内部标记，这个兜底提示走的是
    // 旧版 UGUI Text，接不上 TMP 的注音预处理器，不去掉的话玩家会看到原始花括号。
    public string InteractLabel => Item != null
        ? $"拾取 {SkyPrisonRubyTextProcessor.StripToPlainText(Item.displayName)}"
        : "拾取";

    public bool CanInteract
    {
        get
        {
            InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
            return inv == null || Item == null || inv.SimulateAdd(Item, count) < count;
        }
    }

    /// <summary>捡起来——原来在 SkyPrisonItemPickupController.TryPickup() 里，统一
    /// 到 IInteractable 体系之后挪到这里，任何调用方(统一的交互控制器)拿到的都是
    /// 同一份逻辑，不用另外区分"这是不是拾取类型"。</summary>
    public void Interact()
    {
        InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv == null || Item == null) return;

        // 带实例快照的掉落物（丢弃武器/护甲那一份具体实例）：不走下面 AddItem 的
        // "按定义+数量重新造一个全新实例"逻辑，直接用快照把耐久/弹匣弹药/染色/
        // 改装件原样插回背包。这类物品 maxStackCount 本来就是1，没有"部分拾取、
        // 部分留在地上"这回事，成功就整个捡起来，背包满就整个失败。
        if (instanceSnapshot != null)
        {
            bool placed = inv.AddEntrySnapshot(itemDefinition, instanceSnapshot);
            if (!placed)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Pickup);
            GetComponent<LootDropVisual>()?.SetSelected(false);
            Destroy(gameObject);
            return;
        }

        int leftover = inv.AddItem(Item, count);
        int accepted = count - leftover;

        if (accepted <= 0)
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return;
        }

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Pickup);

        if (leftover <= 0)
        {
            GetComponent<LootDropVisual>()?.SetSelected(false);
            Destroy(gameObject);
        }
        else
        {
            SetRemaining(leftover);
        }
    }

    private void OnEnable()
    {
        InteractableRegistry.Register(this);
    }

    private void OnDisable()
    {
        InteractableRegistry.Unregister(this);
    }

    private void Start()
    {
        if (autoDestroyIfInvalid && itemDefinition == null)
            Destroy(gameObject);
    }

    /// <summary>拾取后设置剩余数量；归零则销毁世界对象。</summary>
    public void SetRemaining(int remaining)
    {
        count = Mathf.Max(0, remaining);
        if (count == 0)
            Destroy(gameObject);
    }

    public void SetLoot(ItemDefinition item, int amount)
    {
        itemDefinition = item;
        count = Mathf.Max(1, amount);

        if (debugLogs)
            Debug.Log($"[LootDropWorldObject] {name}: {itemDefinition?.name} x{count}", this);
    }

    /// <summary>
    /// 在 pos 处生成一个掉落物 GameObject（无 prefab 依赖，纯代码创建）。
    /// 如果项目有专属 prefab，可在此替换为 Instantiate。
    /// </summary>
    public static LootDropWorldObject SpawnDrop(ItemDefinition def, int amount, Vector3 pos)
    {
        if (def == null || amount <= 0) return null;

        Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
        var go = new GameObject($"Drop_{def.displayName}_x{amount}");
        go.transform.position = pos + offset;

        LootDropWorldObject drop = go.AddComponent<LootDropWorldObject>();
        drop.SetLoot(def, amount);

        // 挂上视觉组件（悬浮/旋转/发光/描边）
        go.AddComponent<LootDropVisual>();

        return drop;
    }

    /// <summary>丢弃一件"具体实例"（武器/护甲这类带耐久/弹匣弹药/染色/改装件的
    /// 装备）——除了物品定义+数量，还把这个 InventoryItemEntry 当前的实例状态打包
    /// 进掉落物，捡回来时原样还原，不会变成一个全新的、状态清零的实例。纯堆叠、
    /// 无状态的消耗品/弹药/材料不要用这个重载，直接用 SpawnDrop(def, amount, pos)
    /// 就够了。</summary>
    public static LootDropWorldObject SpawnDropFromEntry(InventoryItemEntry entry, int amount, Vector3 pos)
    {
        if (entry?.definition == null) return null;

        LootDropWorldObject drop = SpawnDrop(entry.definition, amount, pos);
        if (drop == null) return null;

        drop.instanceSnapshot = new SavedItemEntry(entry) { count = Mathf.Max(1, amount) };
        return drop;
    }
}
