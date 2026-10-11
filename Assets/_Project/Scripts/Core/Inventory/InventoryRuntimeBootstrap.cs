using UnityEngine;

// 常驻运行时单例（由 SkyPrisonRuntimeSystemsBootstrapper 创建，不依赖玩家 prefab）。
// 持有唯一的 InventoryRuntime，其它系统通过 InventoryRuntimeBootstrap.Instance.Inventory 访问。
public class InventoryRuntimeBootstrap : MonoBehaviour
{
    public static InventoryRuntimeBootstrap Instance { get; private set; }

    [SerializeField] private int initialCapacity = 20;
    [SerializeField] private float initialMaxWeight = 50f;

    public InventoryRuntime Inventory { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 背包必须跨场景常驻：它原来挂在每个场景自己的 SkyPrisonRuntimeSystems 下，换图（比如从余穹进 RAID）
        // 场景一卸载背包就跟着销毁、新场景里重新建一个空的——装备 / 仓库 / 快捷栏都是常驻单例，只有背包被漏掉了。
        // 摘出场景根再设为 DontDestroyOnLoad；新场景的启动器用 FindObjectOfType 先找已有实例，不会重复创建。
        if (Application.isPlaying)
        {
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }

        Inventory = GetComponent<InventoryRuntime>();
        if (Inventory == null)
            Inventory = gameObject.AddComponent<InventoryRuntime>();

        // 应用初始容量 / 负重上限（之前的 bug：字段从未生效）。
        ApplyTechTreePerks();
        TechTreeRuntime.OnNodeLevelUp += OnTechNodeLevelUp;
        SaveManager.OnSaveLoaded += ApplyTechTreePerks;

        if (GetComponent<InventoryBurdenSync>() == null)
            gameObject.AddComponent<InventoryBurdenSync>();
    }

    private void OnTechNodeLevelUp(string _, int __) => ApplyTechTreePerks();

    /// <summary>基础容量 / 负重上限 + 科技树「战略家」的加成(背包格子 +N、负重上限 +N)。读档、节点升级时重算。</summary>
    public void ApplyTechTreePerks()
    {
        if (Inventory == null) return;
        Inventory.SetInitialCapacity(initialCapacity + Mathf.RoundToInt(TechTreePerks.Get("inventorySlots")));
        Inventory.SetMaxWeightCapacity(initialMaxWeight + TechTreePerks.Get("carryCapacity"));
    }

    private void OnDestroy()
    {
        TechTreeRuntime.OnNodeLevelUp -= OnTechNodeLevelUp;
        SaveManager.OnSaveLoaded -= ApplyTechTreePerks;
        if (Instance == this)
            Instance = null;
    }
}
