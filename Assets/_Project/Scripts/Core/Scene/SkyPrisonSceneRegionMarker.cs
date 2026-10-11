using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 地图上的命名区域（类似WC3 WE的Region）——挂在放置工具建出来的区域物体上，
/// 靠同物体上的 BoxCollider(isTrigger) 定义范围。跟 SkyPrisonSceneUnitMarker
/// 是同一套"场景引用靠稳定GUID解析"思路，但那个是单位专用、绑死了
/// UnitDefinitionRuntimeBinder，区域没有对应的Definition资产，需要自己一份。
///
/// Cylinder 形状复用同一个 BoxCollider 的 size(x当直径、y当高)，不额外切换成
/// CapsuleCollider——Unity 原生没有真正的"平顶圆柱"碰撞体，CapsuleCollider两端
/// 是半球会跟预期的圆柱不一样，而这里的判定本来就是自己算的(不依赖物理引擎的
/// 精确碰撞形状)，用同一个盒子当"外接包围盒"更省事，摆放/缩放操作也不用切换
/// 组件。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class SkyPrisonSceneRegionMarker : MonoBehaviour
{
    public enum RegionShape
    {
        [InspectorName("方块")]
        Box,
        [InspectorName("圆柱")]
        Cylinder,
    }

    [SerializeField] private string sceneRegionGuid;
    [SerializeField] private string regionName = "Region";
    [SerializeField] private RegionShape shape = RegionShape.Box;

    private BoxCollider boxCollider;

    public string SceneRegionGuid
    {
        get
        {
            EnsureGuidRuntime();
            return sceneRegionGuid;
        }
    }

    public string RegionName => string.IsNullOrWhiteSpace(regionName) ? gameObject.name : regionName;

    public void SetRegionName(string value) => regionName = value;

    public RegionShape Shape => shape;

    public void SetShape(RegionShape value) => shape = value;

    public BoxCollider BoxCollider
    {
        get
        {
            if (boxCollider == null)
                boxCollider = GetComponent<BoxCollider>();
            return boxCollider;
        }
    }

    /// <summary>Box形状时按局部空间(考虑旋转)判定，不用 Collider.bounds 那种世界轴对齐
    /// 包围盒——区域转个角度之后 Collider.bounds 会比实际范围大一圈，靠近区域外角
    /// 附近就会判定成"在里面"其实还没进去。Cylinder形状按局部XZ平面的圆心距离+Y
    /// 高度区间判定，同样先转到局部空间再算，跟着物体一起转。</summary>
    public bool ContainsPoint(Vector3 worldPoint)
    {
        BoxCollider bc = BoxCollider;
        if (bc == null)
            return false;

        Vector3 local = transform.InverseTransformPoint(worldPoint) - bc.center;

        if (shape == RegionShape.Cylinder)
        {
            float radius = Mathf.Max(bc.size.x, bc.size.z) * 0.5f;
            float halfHeight = bc.size.y * 0.5f;
            float horizontalDistSq = local.x * local.x + local.z * local.z;
            return horizontalDistSq <= radius * radius && Mathf.Abs(local.y) <= halfHeight;
        }

        Vector3 half = bc.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private bool wasPlayerInside;
    private TriggerPackageRuntime cachedTriggerRuntime;

    private void Awake()
    {
        EnsureGuidRuntime();
        if (BoxCollider != null)
            BoxCollider.isTrigger = true;
    }

    // 用轮询而不是Unity物理OnTriggerEnter/Exit——Cylinder形状底层还是同一个
    // BoxCollider(只是数值上当圆柱算)，真物理触发判定的是盒子的四个角，跟
    // ContainsPoint()算出来的圆不是一回事，会出现"物理说进了、数学说还没进"的
    // 不一致。轮询直接复用同一份ContainsPoint，保证跟 cond_player_in_region
    // 永远看到同一个答案。区域数量不会多到轮询有性能问题。
    private void Update()
    {
        if (!Application.isPlaying)
            return;

        GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
        bool isInside = playerGo != null && ContainsPoint(playerGo.transform.position);

        if (isInside == wasPlayerInside)
            return;

        wasPlayerInside = isInside;
        FireRegionMotive(isInside ? "event_player_enter_region" : "event_player_leave_region");
    }

    private void FireRegionMotive(string motiveTemplateId)
    {
        if (cachedTriggerRuntime == null)
            cachedTriggerRuntime = FindObjectOfType<TriggerPackageRuntime>();

        cachedTriggerRuntime?.FireMotiveEvent(motiveTemplateId, SceneRegionGuid);
    }

    private void EnsureGuidRuntime()
    {
        if (string.IsNullOrWhiteSpace(sceneRegionGuid))
            sceneRegionGuid = System.Guid.NewGuid().ToString();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        EnsureGuidEditor();
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnValidate()
    {
        EnsureGuidEditor();
        if (BoxCollider != null)
            BoxCollider.isTrigger = true;
    }

    private void EnsureGuidEditor()
    {
        if (string.IsNullOrWhiteSpace(sceneRegionGuid))
            sceneRegionGuid = GUID.Generate().ToString();
    }

    private void OnDrawGizmos()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc == null)
            return;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (shape == RegionShape.Cylinder)
            DrawWireCylinder(bc.center, Mathf.Max(bc.size.x, bc.size.z) * 0.5f, bc.size.y);
        else
            Gizmos.DrawWireCube(bc.center, bc.size);

        Gizmos.matrix = old;

        Handles.Label(transform.position + Vector3.up * (bc.size.y * 0.5f + 0.3f), RegionName);
    }

    private static void DrawWireCylinder(Vector3 center, float radius, float height)
    {
        const int segments = 24;
        Vector3 top = center + new Vector3(0f, height * 0.5f, 0f);
        Vector3 bottom = center - new Vector3(0f, height * 0.5f, 0f);

        Vector3 prevTop = top + new Vector3(radius, 0f, 0f);
        Vector3 prevBottom = bottom + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 nextTop = top + offset;
            Vector3 nextBottom = bottom + offset;

            Gizmos.DrawLine(prevTop, nextTop);
            Gizmos.DrawLine(prevBottom, nextBottom);
            if (i % 6 == 0)
                Gizmos.DrawLine(nextTop, nextBottom);

            prevTop = nextTop;
            prevBottom = nextBottom;
        }
    }
#endif
}
