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

    [Tooltip("按E时打开的窗口Prefab。只有本来就存在专属窗口的设施才需要配(补给站/" +
        "古物街走商店窗口)。没有专属窗口的设施改配下面的触发包。")]
    [SerializeField] private GameObject   windowPrefab;

    [Tooltip("按E时执行的触发包——不用为每个设施单独做UI，直接复用逻辑句式系统里" +
        "已有的全部动作(act_open_shop / act_show_dialogue_line / act_set_variable_* " +
        "等等)。还没做出专属界面的设施先配一句台词占位，也比按了没反应强。\n" +
        "两个都配了的话，窗口优先，触发包不执行。")]
    [SerializeField] private TriggerPackage triggerPackage;

    // ── IInteractable ─────────────────────────────────────────────────────

    public Vector3 InteractPosition => transform.position;

    public string InteractLabel =>
        !string.IsNullOrEmpty(customLabel) ? customLabel : DefaultLabel(facilityType);

    public bool CanInteract => windowPrefab != null || triggerPackage != null;

    public void Interact()
    {
        // 统一交互控制器不再无差别硬播 Open 音效，改由各类型自己在 Interact() 里播，
        // 保持原有"按E开设施窗口有声音"的体验。
        if (windowPrefab != null)
        {
            var manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
            if (manager == null) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            manager.Open(windowPrefab);
            return;
        }

        if (triggerPackage != null)
        {
            // 跟任务目标的NPC回应演出同一个走法：手动立刻执行，完全独立于场景
            // 自动生成、按动机+条件驱动的那一套触发器，不会互相干扰。
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            StartCoroutine(TriggerPackageRuntime.ExecuteImmediatelyCoroutine(triggerPackage));
            return;
        }

        Debug.LogWarning($"[FacilityInteractable] {facilityType} 既没绑窗口Prefab也没绑触发包，按E不会有任何反应。");
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
