using System;
using System.Collections.Generic;
using UnityEngine;

public enum InventorySortField
{
    AcquireTime,
    Weight,
    Category,
    Name,
    Value,
    Level
}

public enum InventoryFilterTab
{
    All,
    Consumable,
    Material,
    Equipment,
    Quest,
    KeyItem
}

/// <summary>
/// 固定槽位背包：slots 始终是 capacity 长度的数组，元素可为 null（空格）。
/// 物品可放在任意槽位（允许中间留空洞），拖拽 = 落到目标格（空位移动 / 有物则交换）。
/// 整理(TidyUp) 才会把物品压缩到前面。
/// </summary>
public class InventoryRuntime : MonoBehaviour
{
    [SerializeField] private int capacity = 20;

    // 固定长度 = capacity，null 表示空格。
    private readonly List<InventoryItemEntry> slots = new List<InventoryItemEntry>();

    [SerializeField] private float maxWeightCapacity = 50f;

    public int Capacity => capacity;
    public IReadOnlyList<InventoryItemEntry> Slots { get { EnsureSize(); return slots; } }
    public float MaxWeightCapacity => maxWeightCapacity;

    public int UsedSlots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) n++;
            return n;
        }
    }

    public float TotalWeight
    {
        get
        {
            float total = 0f;
            foreach (var e in slots)
                if (e?.definition != null) total += e.definition.weightOrQuartz * e.count;
            return total;
        }
    }

    public event Action OnInventoryChanged;
    public event Action<ItemDefinition, int> OnItemGained;

    private void Awake() => EnsureSize();

    // ── Add ──────────────────────────────────────────────────────────────

    // 只读模拟：返回无法放入的剩余数量，不修改状态。
    public int SimulateAdd(ItemDefinition def, int amount)
    {
        if (def == null || amount <= 0) return amount;
        EnsureSize();
        int remaining = amount;
        if (def.maxStackCount > 1)
        {
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                var entry = slots[i];
                if (entry == null || entry.definition != def || entry.IsStackFull) continue;
                remaining -= Mathf.Min(remaining, entry.StackRoom);
            }
        }
        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            if (slots[i] != null) continue;
            remaining -= Mathf.Min(remaining, Mathf.Max(1, def.maxStackCount));
        }
        return remaining;
    }

    // Returns leftover count that could not fit.
    public int AddItem(ItemDefinition def, int amount)
    {
        if (def == null || amount <= 0) return amount;
        EnsureSize();

        int remaining = amount;

        // 1) 先并入已有未满的同种堆叠
        if (def.maxStackCount > 1)
        {
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                var entry = slots[i];
                if (entry == null || entry.definition != def || entry.IsStackFull) continue;
                int fit = Mathf.Min(remaining, entry.StackRoom);
                entry.count += fit;
                entry.isNew = true;
                remaining -= fit;
            }
        }

        // 2) 放进最靠前的空格
        while (remaining > 0)
        {
            int empty = FirstEmptyIndex();
            if (empty < 0) break;
            int take = Mathf.Min(remaining, def.maxStackCount);
            slots[empty] = new InventoryItemEntry(def, take) { isNew = true };
            remaining -= take;
        }

        int gained = amount - remaining;
        if (gained > 0)
        {
            OnInventoryChanged?.Invoke();
            OnItemGained?.Invoke(def, gained);
        }

        return remaining;
    }

    /// <summary>把一个已经存在的具体实例（带着它自己的耐久/染色/词条数据）原样塞进
    /// 一个空格——不能用 AddItem(definition, amount)：那个会新建一个全新entry（丢光
    /// 这件的耐久/染色/词条数据），如果 maxStackCount>1 还会把数量直接合并进背包里
    /// 已有的同款堆叠，混进另一件独立实例的count里，后续按引用删除时会牵连着把
    /// 混进来的这部分也一起删掉。卸装/换装把某一件具体实例放回背包时必须用这个。
    /// 背包满（没有空格）返回 false，实例保持不动（调用方自己决定要不要中止换装）。</summary>
    public bool AddExactEntry(InventoryItemEntry entry)
    {
        if (entry == null) return false;
        EnsureSize();

        int empty = FirstEmptyIndex();
        if (empty < 0) return false;

        // 装备卸下来把这个实例原样放回背包时，它的 count 很可能已经被上一次
        // RemoveExactEntry（装备走它时）扣到了 0——这个 entry 对象本身继续作为
        // "已装备"的引用活着，count=0 这个状态会一直留在它身上，被 AddExactEntry
        // 放回背包却从来没恢复过。留着 count=0 的话，下次再想把它从背包移除（比如
        // 再装备一次）会在 RemoveExactEntry 里被 amount<=0 直接拒绝、什么都不删，
        // 变成同一个物理实例又装备又留在背包的重复bug。这里放回背包时强制修复到
        // 至少 1，因为装备类物品本来就是不可堆叠的单件实例，绝不该是 0。
        if (entry.count <= 0) entry.count = 1;

        entry.isNew = true;
        slots[empty] = entry;
        OnInventoryChanged?.Invoke();
        return true;
    }

    // ── Remove ───────────────────────────────────────────────────────────

    public bool RemoveItem(ItemDefinition def, int amount)
    {
        if (def == null || amount <= 0) return false;
        if (CountItem(def) < amount) return false;

        int remaining = amount;
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var entry = slots[i];
            if (entry == null || entry.definition != def) continue;
            int take = Mathf.Min(remaining, entry.count);
            entry.count -= take;
            remaining -= take;
            if (entry.count <= 0) slots[i] = null;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>按具体实例（不是按物品种类）移除——武器/装备这类不可堆叠道具，背包里
    /// 同款可能同时存在好几件独立实例（比如一把装备着、一把备用），RemoveItem(def,
    /// amount) 是按种类从列表末尾找任意一堆来扣，找到的很可能不是这一件具体实例，
    /// 会把另一件误删、这一件反而留在背包原位没删（换装时那把"同时出现在已装备和
    /// 背包"的bug就是这么来的）。换装/使用某一件具体实例时必须用这个方法，按引用
    /// 精确定位到它在 slots 里的位置再扣，不看种类匹配到谁就扣谁。</summary>
    public bool RemoveExactEntry(InventoryItemEntry entry, int amount)
    {
        if (entry == null || amount <= 0) return false;

        int index = slots.IndexOf(entry);
        if (index < 0) return false;

        int take = Mathf.Min(amount, entry.count);
        entry.count -= take;
        if (entry.count <= 0) slots[index] = null;

        OnInventoryChanged?.Invoke();
        return true;
    }

    // ── 弹药（口径级别，不是按具体弹药物品种类）──────────────────────────────
    // 弹药只分两级口径（AmmoCaliberType），背包里可能同时有好几种不同的具体弹药道具
    // 共用同一个口径（比如以后加了"劣质小口径"/"优质小口径"两种物品），扣弹药按口径
    // 汇总扣减，不认具体是哪个 ItemDefinition，扣的时候从随便哪一堆扣都行。

    private bool IsAmmoOfCaliber(ItemDefinition def, AmmoCaliberType caliber)
        => def != null
        && def.category == ItemCategory.Material
        && def.materialSubCategory == MaterialSubCategory.Ammunition
        && def.ammo != null
        && def.ammo.caliber == caliber;

    public int GetAmmoCount(AmmoCaliberType caliber)
    {
        int total = 0;
        foreach (var e in slots)
            if (e?.definition != null && IsAmmoOfCaliber(e.definition, caliber))
                total += e.count;
        return total;
    }

    /// <summary>按口径扣减弹药，弹药不够（汇总数量小于amount）时完全不扣、返回false。</summary>
    public bool TryConsumeAmmo(AmmoCaliberType caliber, int amount)
    {
        if (amount <= 0) return true;
        if (GetAmmoCount(caliber) < amount) return false;

        int remaining = amount;
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var entry = slots[i];
            if (entry?.definition == null || !IsAmmoOfCaliber(entry.definition, caliber)) continue;
            int take = Mathf.Min(remaining, entry.count);
            entry.count -= take;
            remaining -= take;
            if (entry.count <= 0) slots[i] = null;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool DiscardSlot(int slotIndex, int amount)
    {
        if (!HasItemAt(slotIndex)) return false;
        var entry = slots[slotIndex];
        if (!entry.CanDiscard) return false;

        amount = Mathf.Clamp(amount, 1, entry.count);
        entry.count -= amount;
        if (entry.count <= 0) slots[slotIndex] = null;

        OnInventoryChanged?.Invoke();
        return true;
    }

    // ── Stack / placement operations ──────────────────────────────────────

    // Merge src into dst（同种、dst 未满）。src 清空后置 null（不挪位）。
    public bool MergeSlots(int srcIndex, int dstIndex)
    {
        if (!HasItemAt(srcIndex) || !HasItemAt(dstIndex)) return false;
        if (srcIndex == dstIndex) return false;

        var src = slots[srcIndex];
        var dst = slots[dstIndex];
        if (src.definition != dst.definition) return false;
        if (dst.IsStackFull) return false;

        int fit = Mathf.Min(src.count, dst.StackRoom);
        dst.count += fit;
        src.count -= fit;
        if (src.count <= 0) slots[srcIndex] = null;

        OnInventoryChanged?.Invoke();
        return fit > 0;
    }

    // 自由放置：把 from 落到 to。to 为空 → 移动过去；to 有物 → 两格交换。
    public bool MoveSlot(int from, int to)
    {
        if (!HasItemAt(from)) return false;
        if (!IsValidIndex(to) || from == to) return false;

        var a = slots[from];
        slots[from] = slots[to];
        slots[to] = a;

        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>跨背包/仓库转移：把本实例 srcIndex 格的物品送到另一个 InventoryRuntime
    /// (比如仓库当前页) 的 targetIndex 格。同种且未满 → 合并；目标为空 → 移动过去；
    /// 目标是别的物品 → 两边交换。MoveSlot/MergeSlots 都只能在同一个 InventoryRuntime
    /// 内部操作，跨实例拖拽(背包拖进仓库格子)需要单独这一个方法。</summary>
    public bool TransferSlotTo(int srcIndex, InventoryRuntime target, int targetIndex)
    {
        if (target == null || target == this) return false;
        if (!HasItemAt(srcIndex)) return false;
        if (!target.IsValidIndex(targetIndex)) return false;

        var src = slots[srcIndex];
        var dst = target.slots[targetIndex];

        if (dst != null && src.definition == dst.definition && src.definition.maxStackCount > 1 && !dst.IsStackFull)
        {
            int fit = Mathf.Min(src.count, dst.StackRoom);
            dst.count += fit;
            src.count -= fit;
            if (src.count <= 0) slots[srcIndex] = null;
        }
        else
        {
            slots[srcIndex] = dst;       // 目标为空则为 null；否则把目标原物品换回源格
            target.slots[targetIndex] = src;
        }

        OnInventoryChanged?.Invoke();
        target.OnInventoryChanged?.Invoke();
        return true;
    }

    // Split amount from src into the first empty slot. Returns false if none.
    public bool SplitSlot(int srcIndex, int amount)
    {
        if (!HasItemAt(srcIndex)) return false;
        int empty = FirstEmptyIndex();
        if (empty < 0) return false;

        var src = slots[srcIndex];
        if (src.definition == null || src.definition.maxStackCount <= 1) return false;
        amount = Mathf.Clamp(amount, 1, src.count - 1);
        if (amount <= 0) return false;

        src.count -= amount;
        slots[empty] = new InventoryItemEntry(src.definition, amount);

        OnInventoryChanged?.Invoke();
        return true;
    }

    // ── Query ─────────────────────────────────────────────────────────────

    public int CountItem(ItemDefinition def)
    {
        int total = 0;
        foreach (var e in slots)
            if (e != null && e.definition == def) total += e.count;
        return total;
    }

    public bool HasItem(ItemDefinition def, int amount = 1) => CountItem(def) >= amount;

    // ── Sort / Tidy ─────────────────────────────────────────────────────────

    public void Sort(InventorySortField field, bool ascending)
    {
        CompactAndSort(field, ascending, false);
        OnInventoryChanged?.Invoke();
    }

    /// <summary>整理：合并零散同种堆叠 → 排序 → 压缩到前面（空格挪到末尾）。</summary>
    public void TidyUp(InventorySortField field, bool ascending)
    {
        CompactAndSort(field, ascending, true);
        OnInventoryChanged?.Invoke();
    }

    // 收集非空条目（可选合并堆叠）→ 排序 → 回填到前面，其余置 null。
    private void CompactAndSort(InventorySortField field, bool ascending, bool mergeStacks)
    {
        EnsureSize();

        var items = new List<InventoryItemEntry>();
        for (int i = 0; i < slots.Count; i++)
            if (slots[i] != null) items.Add(slots[i]);

        if (mergeStacks) MergeStacks(items);

        items.Sort((a, b) =>
        {
            int cmp = CompareEntries(a, b, field);
            return ascending ? cmp : -cmp;
        });

        for (int i = 0; i < slots.Count; i++)
            slots[i] = i < items.Count ? items[i] : null;
    }

    private static int CompareEntries(InventoryItemEntry a, InventoryItemEntry b, InventorySortField field)
    {
        ItemDefinition da = a?.definition;
        ItemDefinition db = b?.definition;
        if (da == null && db == null) return 0;
        if (da == null) return 1;   // 空条目排到最后
        if (db == null) return -1;

        int cmp = field switch
        {
            InventorySortField.AcquireTime => a.acquireTime.CompareTo(b.acquireTime),
            InventorySortField.Weight      => da.weightOrQuartz.CompareTo(db.weightOrQuartz),
            InventorySortField.Category    => da.category.CompareTo(db.category),
            InventorySortField.Value       => da.value.CompareTo(db.value),
            InventorySortField.Level       => da.itemLevel.CompareTo(db.itemLevel),
            InventorySortField.Name        => string.Compare(da.displayName, db.displayName, StringComparison.Ordinal),
            _                              => 0
        };
        if (cmp != 0) return cmp;

        cmp = da.itemId.CompareTo(db.itemId);
        if (cmp != 0) return cmp;
        return a.acquireTime.CompareTo(b.acquireTime);
    }

    // 把相同 definition 的可堆叠条目从后往前并入靠前未满堆叠（在压缩列表上操作）。
    private static void MergeStacks(List<InventoryItemEntry> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            InventoryItemEntry dst = items[i];
            if (dst?.definition == null || dst.definition.maxStackCount <= 1 || dst.IsStackFull)
                continue;

            for (int j = i + 1; j < items.Count; j++)
            {
                InventoryItemEntry src = items[j];
                if (src?.definition != dst.definition) continue;

                int fit = Mathf.Min(src.count, dst.StackRoom);
                if (fit <= 0) continue;

                dst.count += fit;
                src.count -= fit;
                if (src.count <= 0) { items.RemoveAt(j); j--; }
                if (dst.IsStackFull) break;
            }
        }
    }

    // ── Filter (view-only, does not modify slots) ─────────────────────────

    public List<int> GetFilteredIndices(InventoryFilterTab tab)
    {
        var result = new List<int>();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && MatchesFilter(slots[i], tab))
                result.Add(i);
        }
        return result;
    }

    private static bool MatchesFilter(InventoryItemEntry entry, InventoryFilterTab tab)
    {
        if (entry?.definition == null) return false;
        return tab switch
        {
            // ItemCategory.Consumable/Material 是靠 category 字段单独判断的，但那字段
            // 只对 majorCategory==General 的物品有意义——装备类物品的 category 从没被
            // 归一化过，随便一把没手动改过 category 的武器都会顶着默认值 Consumable(0)，
            // 不先排除装备类的话"消耗品"分类Tab里会混进武器/防具。
            InventoryFilterTab.All        => true,
            InventoryFilterTab.Consumable => entry.definition.IsGeneralItem && entry.definition.category == ItemCategory.Consumable,
            InventoryFilterTab.Material   => entry.definition.IsGeneralItem && entry.definition.category == ItemCategory.Material,
            InventoryFilterTab.Equipment  => entry.definition.majorCategory == ItemMajorCategory.Equipment,
            InventoryFilterTab.Quest      => entry.definition.category == ItemCategory.Quest,
            InventoryFilterTab.KeyItem    => entry.definition.isKeyItem,
            _                             => true
        };
    }

    /// <summary>角色面板点某个装备槽呼出背包时用——强制只亮起能装进那个槽的物品，不走
    /// FilterTab 那套大分类。武器/副武器两个槽位共用同一批"武器"物品，任何一把武器
    /// 两边都能塞得进去，所以这两个槽当目标时互相也算匹配。</summary>
    public List<int> GetIndicesForEquipSlot(EquipmentSlotType targetSlot)
    {
        var result = new List<int>();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i]?.definition == null || !slots[i].definition.IsEquipmentItem) continue;
            EquipmentSlotType itemSlot = slots[i].definition.equipment.slot;
            bool isWeaponPair = (targetSlot == EquipmentSlotType.Weapon || targetSlot == EquipmentSlotType.WeaponSecondary)
                             && (itemSlot == EquipmentSlotType.Weapon || itemSlot == EquipmentSlotType.WeaponSecondary);
            if (itemSlot == targetSlot || isWeaponPair)
                result.Add(i);
        }
        return result;
    }

    /// <summary>能绑到快捷物品槽的物品：消耗品分类、且不是复活道具（复活道具走濒死
    /// 弹窗那条单独的使用路径，不塞进战斗中的快捷栏）。</summary>
    public List<int> GetIndicesForQuickSlot()
    {
        var result = new List<int>();
        for (int i = 0; i < slots.Count; i++)
        {
            var def = slots[i]?.definition;
            if (def == null) continue;
            // ItemCategory.Consumable 刚好是枚举默认值(0)——装备类物品的 category 字段
            // 从来没被 NormalizeMajorCategoryRules 归一化过（那个方法只处理
            // majorCategory==General 的情况），所以任何没手动改过 category 的装备
            // 都会顶着默认值 Consumable，单看 category 字段会被误判成消耗品。必须先
            // 确认是一般道具（IsGeneralItem）才能再看 category，不然武器/防具会漏进
            // 快捷物品候选列表。
            if (!def.IsGeneralItem || def.category != ItemCategory.Consumable) continue;
            if (def.general != null && def.general.isReviveItem) continue;
            result.Add(i);
        }
        return result;
    }

    // ── Capacity ──────────────────────────────────────────────────────────

    public void ExpandCapacity(int additionalSlots)
    {
        capacity += Mathf.Max(0, additionalSlots);
        EnsureSize();
        OnInventoryChanged?.Invoke();
    }

    public void SetInitialCapacity(int cap)
    {
        capacity = Mathf.Max(1, cap);
        EnsureSize();
        OnInventoryChanged?.Invoke();
    }

    public void SetMaxWeightCapacity(float max)
    {
        maxWeightCapacity = Mathf.Max(1f, max);
        OnInventoryChanged?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // 保证 slots 长度 == capacity（扩容补 null；缩容只去掉尾部空格）。
    private void EnsureSize()
    {
        while (slots.Count < capacity) slots.Add(null);
        while (slots.Count > capacity && slots[slots.Count - 1] == null)
            slots.RemoveAt(slots.Count - 1);
    }

    private int FirstEmptyIndex()
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i] == null) return i;
        return -1;
    }

    private bool IsValidIndex(int index) => index >= 0 && index < slots.Count;
    private bool HasItemAt(int index) => IsValidIndex(index) && slots[index] != null;

    // ── 存档对接 ──────────────────────────────────────────────────────────

    /// <summary>将当前背包序列化为存档列表。</summary>
    public List<SavedItemEntry> Serialize()
    {
        var result = new List<SavedItemEntry>();
        foreach (var slot in slots)
        {
            if (slot == null || slot.definition == null || slot.count <= 0) continue;
            result.Add(new SavedItemEntry(slot));
        }
        return result;
    }

    /// <summary>从存档列表还原背包内容。registry 用于 itemKey→ItemDefinition 查找。</summary>
    public void Deserialize(List<SavedItemEntry> saved, ItemRegistry registry)
    {
        for (int i = 0; i < slots.Count; i++) slots[i] = null;

        if (saved == null || registry == null) return;

        foreach (var entry in saved)
        {
            if (string.IsNullOrEmpty(entry.itemKey) || entry.count <= 0) continue;
            if (!registry.TryFind(entry.itemKey, out var def))
            {
                Debug.LogWarning($"[InventoryRuntime] 找不到物品定义：{entry.itemKey}，跳过。");
                continue;
            }

            // 优先堆叠到已有格
            if (def.maxStackCount > 1)
            {
                bool stacked = false;
                foreach (var slot in slots)
                {
                    if (slot?.definition != def || slot.IsStackFull) continue;
                    int add = Mathf.Min(slot.StackRoom, entry.count);
                    slot.count += add;
                    stacked = add >= entry.count;
                    break;
                }
                if (stacked) continue;
            }

            int idx = FirstEmptyIndex();
            if (idx < 0)
            {
                Debug.LogWarning($"[InventoryRuntime] 背包已满，{entry.itemKey} ×{entry.count} 无法还原。");
                continue;
            }
            var newEntry = new InventoryItemEntry(def, entry.count);
            if (entry.durability >= 0)          newEntry.currentDurability = entry.durability;
            if (entry.rolledBonuses?.Count > 0) newEntry.rolledBonuses     = entry.rolledBonuses;
            if (entry.installedMods?.Count > 0) newEntry.installedMods     = entry.installedMods;
            if (entry.dyeColors?.Length == 3)   newEntry.dyeColors         = entry.dyeColors;
            if (!string.IsNullOrEmpty(entry.transmogSourceItemKey)) newEntry.transmogSourceItemKey = entry.transmogSourceItemKey;
            newEntry.isIdentified = entry.isIdentified;
            slots[idx] = newEntry;
        }

        OnInventoryChanged?.Invoke();
    }
}
