using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战术地图上的区域标注：地图里放一块矩形(位置 = 物体位置，朝向 = 物体的 Y 旋转，大小 = size)，
/// 小地图上就会画出一块带名字的色块(危险区、资源区、据点……用途不限)。
/// 不绑死「浅 / 中 / 深」三段式：显示名、颜色、强度、环境危害都是自由填的，图例按场景里实际存在的区域自动生成。
/// 区域是情报，不受「走过才显示」限制——进图前就能看到规划用的分区；只有地形细节需要探索。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonMapZone : MonoBehaviour
{
    public enum Hazard { None = 0, Heat = 1, Shock = 2, Corrosion = 3, Freeze = 4 }

    [Tooltip("显示在小地图上的名字，例如「低危区」「冷却塔」；叫什么都行。")]
    public string label = "新区域";
    [Tooltip("矩形大小(米)：x = 沿本地 X 的宽，y = 沿本地 Z 的深。")]
    public Vector2 size = new Vector2(40f, 40f);
    [Tooltip("色块颜色(同名同色的区域在图例里合并成一项)。")]
    public Color color = new Color(0.42f, 0.92f, 0.68f, 1f);
    [Range(0f, 1f), Tooltip("强度：越大色块越浓，用来表达「越危险越醒目」；不用分档，连续调。")]
    public float intensity = 0.5f;
    [Tooltip("这个区域的环境危害(会在名字下面标出)，没有就选 None。")]
    public Hazard hazard = Hazard.None;
    [Tooltip("危害的自定义说明，填了就覆盖上面的预设名称(例如「瘴气」)。")]
    public string hazardNote;

    public static readonly List<SkyPrisonMapZone> All = new List<SkyPrisonMapZone>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    public string HazardText
    {
        get
        {
            if (!string.IsNullOrEmpty(hazardNote)) return hazardNote;
            switch (hazard)
            {
                case Hazard.Heat: return "灼热";
                case Hazard.Shock: return "电磁";
                case Hazard.Corrosion: return "腐蚀";
                case Hazard.Freeze: return "冻结";
                default: return "";
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var m = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
        var old = Gizmos.matrix; Gizmos.matrix = m;
        var c = color; c.a = 0.12f + 0.2f * intensity;
        Gizmos.color = c; Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, 0.1f, size.y));
        c.a = 0.9f; Gizmos.color = c; Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.1f, size.y));
        Gizmos.matrix = old;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, label);
    }
#endif
}
