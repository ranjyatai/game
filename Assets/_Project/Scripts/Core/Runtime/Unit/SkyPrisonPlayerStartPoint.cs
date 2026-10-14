using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 玩家初始点：地图里不再手摆 AXIA，而是摆一个初始点，选中它设置「初始单位」。
/// 进场景时在这个点按所选单位的预制体生成玩家。
///
/// 规则：
/// - 全项目只能有一个玩家初始点（天空囚笼只有一个出生点），摆放工具和编辑器保护会拦住重复摆放。
///   万一还是出现了多个，运行时报错并只用名字排序第一个，不会生成多个玩家。
/// - 场景里已经有 Tag=Player 的角色（旧地图手摆的 AXIA）就不再生成，避免出现两个玩家。
/// - 位置和朝向（Y 轴）取这个物体自己的 Transform。
/// - 「新游戏出生点覆盖」（SaveManager.SetSpawnOverride，主菜单设置里那套）仍然优先：
///   它在场景加载完成后才应用，会把已经生成好的玩家再挪过去。
/// - 初始条件：AXIA 没有（留空）。别的单位如果出场自带状态/装备，做成触发包填在这里，生成后执行一次。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public sealed class SkyPrisonPlayerStartPoint : MonoBehaviour
{
    [Header("初始单位")]
    [Tooltip("进场景时在这里生成哪个单位作为玩家（取它的预制体）。")]
    public UnitDefinition initialUnit;

    [Header("初始条件")]
    [Tooltip("生成后执行一次的触发包（初始状态、装备、剧情标记等）。AXIA 没有初始条件，留空。")]
    [SerializeField, TriggerPackageNamePicker] private TriggerPackage initialTrigger;

    public UnitDefinition InitialUnit => initialUnit;

    private void Awake()
    {
        if (!Application.isPlaying)
            return;
        TrySpawn();
    }

    private void TrySpawn()
    {
        // 已经有玩家（旧地图手摆的，或者另一个初始点刚生成的）：不重复生成。
        if (GameObject.FindGameObjectWithTag("Player") != null)
            return;

        SkyPrisonPlayerStartPoint chosen = PickChosen();
        if (chosen != this)
            return;

        if (SpawnPlayer(initialUnit, transform, this) == null)
            return;

        if (initialTrigger != null)
            StartCoroutine(RunInitialTrigger(initialTrigger));
    }

    /// <summary>
    /// 在 at 的位置和 Y 朝向生成玩家，并把相机 / 跟随代理指向它。RAID 落点（SkyPrisonRaidArrivalPoint）共用这一份。
    /// 单位或预制体为空时返回 null 并报警告。
    /// </summary>
    public static GameObject SpawnPlayer(UnitDefinition unit, Transform at, Object context)
    {
        if (unit == null || unit.prefab == null)
        {
            Debug.LogWarning($"[PlayerStartPoint] '{context?.name}' 没有设置单位，或者该单位没有预制体，没有生成玩家。", context);
            return null;
        }

        Transform parent = null;
        GameObject runtimeRoot = GameObject.Find("PlayerRuntime");
        if (runtimeRoot != null)
            parent = runtimeRoot.transform;

        Quaternion rot = Quaternion.Euler(0f, at.eulerAngles.y, 0f);
        GameObject player = Instantiate(unit.prefab, at.position, rot, parent);
        player.name = "Player";
        player.tag = "Player";
        player.SetActive(true);

        RetargetCameras(player.transform);
        return player;
    }

    /// <summary>
    /// 场景里的相机 / 跟随代理原来跟随的是手摆的那个玩家；运行时生成的新玩家不是它。
    /// 手摆玩家删掉之后这些引用是空的，这里统一指向新玩家：
    /// - 虚拟相机：跟随目标为空（并且带需要目标的组件，比如 PositionComposer）、已经失效（物体未激活）、
    ///   或者是别的 Player 标签物体时，改跟随新玩家。没有目标且不需要目标的相机（过场固定机位）不动。
    /// - CameraFollowProxy：玩家引用为空或失效时补上。
    /// - 音频监听锚点自己会按标签找玩家，不用处理。
    /// </summary>
    private static void RetargetCameras(Transform player)
    {
        CinemachineCamera[] cams = FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cams.Length; i++)
        {
            CinemachineCamera cam = cams[i];
            if (cam == null)
                continue;
            Transform current = cam.Follow;
            bool needsTarget = cam.GetComponent<CinemachinePositionComposer>() != null || cam.GetComponent<CinemachineFollow>() != null;
            if (NeedsRetarget(current, player, needsTarget))
                cam.Follow = player;
            if (cam.LookAt != null && NeedsRetarget(cam.LookAt, player, false))
                cam.LookAt = player;
        }

        CameraFollowProxy[] proxies = FindObjectsByType<CameraFollowProxy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < proxies.Length; i++)
            if (proxies[i] != null)
                proxies[i].SetPlayerIfMissing(player);
    }

    private static bool NeedsRetarget(Transform current, Transform player, bool targetRequired)
    {
        if (current == null)
            return targetRequired;
        if (current == player)
            return false;
        return !current.gameObject.activeInHierarchy || current.CompareTag("Player");
    }

    private IEnumerator RunInitialTrigger(TriggerPackage package)
    {
        // 等一帧：玩家身上的各个系统（背包、状态、权威）要先初始化完。
        yield return null;
        yield return TriggerPackageRuntime.ExecuteImmediatelyCoroutine(package);
    }

    private static SkyPrisonPlayerStartPoint PickChosen()
    {
        // 不能用 OnEnable 里登记的静态列表：Awake 比 OnEnable 先跑，此时列表还是空的。
        // 直接查场景里所有处于激活状态的初始点（别的初始点此时可能还没跑 Awake，也找得到）。
        SkyPrisonPlayerStartPoint[] all = FindObjectsByType<SkyPrisonPlayerStartPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        SkyPrisonPlayerStartPoint best = null;
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            SkyPrisonPlayerStartPoint p = all[i];
            if (p == null || !p.enabled)
                continue;
            count++;
            if (best == null || string.CompareOrdinal(p.name, best.name) < 0)
                best = p;
        }
        if (count > 1)
            Debug.LogError($"[PlayerStartPoint] 场景里有 {count} 个玩家初始点，只能有一个。这次用「{best.name}」，请删掉多余的。", best);
        return best;
    }

#if UNITY_EDITOR
    // ── 场景视图标记 ───────────────────────────────────────────
    // 初始点全项目只有一个，也是整张地图最重要的一个点，所以做得比别的标记显眼：
    // 荧光绿（地图里别的东西没用这个色）、脚下实心圈、AXIA 碰撞胶囊大小的轮廓（一眼看出人会站在哪、多高）、
    // 粗朝向箭头、竖到高空的信标线 + 顶部星标（俯视、拉远时也找得到）。
    private static readonly Color MarkerColor = new Color(0.25f, 1f, 0.55f, 1f);
    private const float CapsuleHeight = 4.2f;   // AXIA 碰撞胶囊实测高度
    private const float CapsuleRadius = 0.8f;
    private const float BeaconHeight = 60f;

    // 不用 OnDrawGizmos：Scene 视图的 Gizmos 开关关掉（这个项目里经常关）就一个都不画了。
    // 改由编辑器脚本 SkyPrisonPlayerStartPointHierarchyMarker 在 Scene 视图回调里调用，不受开关影响。
    public void DrawEditorMarker(bool selected)
    {
        Vector3 p = transform.position;
        Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Vector3 fwd = rot * Vector3.forward;
        Vector3 right = rot * Vector3.right;
        float size = Mathf.Max(1f, UnityEditor.HandleUtility.GetHandleSize(p));
        float ringR = Mathf.Max(1.4f, size * 0.12f);
        Color c = MarkerColor;
        // 选中时画得更亮更粗一些；没选中也要看得见。
        float a = selected ? 1f : 0.85f;

        // 脚下：实心半透明圆 + 双圈
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.22f * a);
        UnityEditor.Handles.DrawSolidDisc(p + Vector3.up * 0.03f, Vector3.up, ringR);
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, a);
        UnityEditor.Handles.DrawWireDisc(p + Vector3.up * 0.03f, Vector3.up, ringR, selected ? 4f : 3f);
        UnityEditor.Handles.DrawWireDisc(p + Vector3.up * 0.03f, Vector3.up, ringR * 0.55f, 2f);

        // 角色轮廓：胶囊线框（底部贴脚）
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.9f * a);
        Vector3 bottom = p + Vector3.up * CapsuleRadius;
        Vector3 top = p + Vector3.up * (CapsuleHeight - CapsuleRadius);
        UnityEditor.Handles.DrawWireDisc(bottom, Vector3.up, CapsuleRadius);
        UnityEditor.Handles.DrawWireDisc(top, Vector3.up, CapsuleRadius);
        UnityEditor.Handles.DrawWireArc(top, right, fwd, 180f, CapsuleRadius);
        UnityEditor.Handles.DrawWireArc(top, fwd, -right, 180f, CapsuleRadius);
        UnityEditor.Handles.DrawLine(bottom + right * CapsuleRadius, top + right * CapsuleRadius);
        UnityEditor.Handles.DrawLine(bottom - right * CapsuleRadius, top - right * CapsuleRadius);
        UnityEditor.Handles.DrawLine(bottom + fwd * CapsuleRadius, top + fwd * CapsuleRadius);
        UnityEditor.Handles.DrawLine(bottom - fwd * CapsuleRadius, top - fwd * CapsuleRadius);

        // 朝向：粗箭头（在地面上，比胶囊更长，俯视时也清楚）
        float arrowLen = ringR * 1.9f;
        Vector3 tip = p + fwd * arrowLen + Vector3.up * 0.05f;
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, a);
        UnityEditor.Handles.DrawAAPolyLine(6f, p + Vector3.up * 0.05f, tip);
        UnityEditor.Handles.DrawAAPolyLine(6f, tip, tip - fwd * (arrowLen * 0.28f) + right * (arrowLen * 0.2f));
        UnityEditor.Handles.DrawAAPolyLine(6f, tip, tip - fwd * (arrowLen * 0.28f) - right * (arrowLen * 0.2f));

        // 信标：竖直虚线到高空，顶部一个星标
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.55f * a);
        UnityEditor.Handles.DrawDottedLine(p + Vector3.up * CapsuleHeight, p + Vector3.up * BeaconHeight, 6f);
        Vector3 beaconTop = p + Vector3.up * BeaconHeight;
        float starR = Mathf.Max(1.2f, UnityEditor.HandleUtility.GetHandleSize(beaconTop) * 0.09f);
        UnityEditor.Handles.color = new Color(c.r, c.g, c.b, a);
        UnityEditor.Handles.DrawSolidDisc(beaconTop, SceneViewForward(), starR);

        // 名字牌
        string unit = initialUnit != null ? (string.IsNullOrEmpty(initialUnit.displayName) ? initialUnit.name : initialUnit.displayName) : "（未设置单位）";
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = c;
        UnityEditor.Handles.Label(p + Vector3.up * (CapsuleHeight + 1.2f), "★ 玩家初始点 · " + unit, style);
    }

    private static Vector3 SceneViewForward()
    {
        var sv = UnityEditor.SceneView.currentDrawingSceneView;
        return sv != null && sv.camera != null ? -sv.camera.transform.forward : Vector3.up;
    }
#endif
}
