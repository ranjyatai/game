using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LootDropWorldObject : MonoBehaviour
{
    // 世界中所有活动掉落物的注册表，拾取系统按此遍历，避免每帧 FindObjectsOfType。
    public static readonly List<LootDropWorldObject> Active = new List<LootDropWorldObject>();

    [Header("掉落数据")]
    [SerializeField] private ScriptableObject itemDefinition;
    [SerializeField] private int count = 1;

    [Header("世界表现")]
    [SerializeField] private bool autoDestroyIfInvalid = false;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    public ScriptableObject ItemDefinition => itemDefinition;
    public int Count => count;

    /// <summary>掉落物承载的物品（类型化）。非 ItemDefinition 时为 null。</summary>
    public ItemDefinition Item => itemDefinition as ItemDefinition;

    private void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
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

    public void SetLoot(ScriptableObject item, int amount)
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
}
