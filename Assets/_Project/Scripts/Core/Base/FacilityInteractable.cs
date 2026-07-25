using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 挂在基地设施 NPC/建筑上，注册为可交互对象。
/// 玩家靠近后按 E，根据 facilityType 打开对应窗口。
/// 窗口 Prefab 通过 Inspector 绑定，由 GameAssetManifest 统一管理后可省略。
/// </summary>
public sealed class FacilityInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private FacilityType facilityType;
    [SerializeField] private string       customLabel; // 留空则用默认标签
    [SerializeField] private GameObject   windowPrefab;

    // ── IInteractable ─────────────────────────────────────────────────────

    public Vector3 InteractPosition => transform.position;

    public string InteractLabel =>
        !string.IsNullOrEmpty(customLabel) ? customLabel : DefaultLabel(facilityType);

    public bool CanInteract => windowPrefab != null;

    public void Interact()
    {
        if (windowPrefab == null)
        {
            Debug.LogWarning($"[FacilityInteractable] {facilityType} 的窗口 Prefab 未绑定。");
            return;
        }

        var manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (manager == null) return;

        manager.Open(windowPrefab);
    }

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void OnEnable()  => InteractableRegistry.Register(this);
    private void OnDisable() => InteractableRegistry.Unregister(this);

    // ── 默认标签 ──────────────────────────────────────────────────────────

    private static string DefaultLabel(FacilityType type) => type switch
    {
        FacilityType.CommandCenter => "进入余穹中枢",
        FacilityType.Workshop      => "进入工房",
        FacilityType.SupplyPost    => "进入补给站",
        FacilityType.AntiqueStreet => "逛古物街",
        FacilityType.PowerStation  => "进入动力站",
        FacilityType.IntelBoard    => "查阅情报板",
        FacilityType.MedStation    => "进入医疗站",
        FacilityType.BeautySalon   => "进入美容室",
        _                          => "交互",
    };
}
