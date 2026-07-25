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

        Inventory = GetComponent<InventoryRuntime>();
        if (Inventory == null)
            Inventory = gameObject.AddComponent<InventoryRuntime>();

        // 应用初始容量 / 负重上限（之前的 bug：字段从未生效）。
        Inventory.SetInitialCapacity(initialCapacity);
        Inventory.SetMaxWeightCapacity(initialMaxWeight);

        if (GetComponent<InventoryBurdenSync>() == null)
            gameObject.AddComponent<InventoryBurdenSync>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
