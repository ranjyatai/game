using UnityEngine;

/// <summary>
/// RAID 落点：从出发点传送进 RAID 本时，玩家在这里出现。每张 RAID 地图摆一个。
///
/// 和「玩家初始点」（SkyPrisonPlayerStartPoint，全项目唯一的新游戏出生点）是两回事：
/// 玩家不跨场景保留，每进一张图都要重新生成，RAID 图就靠这个点生成。
/// 生成逻辑和初始点共用 SkyPrisonPlayerStartPoint.SpawnPlayer。
/// 场景里已经有 Tag=Player 的角色就不再生成。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public sealed class SkyPrisonRaidArrivalPoint : MonoBehaviour
{
    [Tooltip("进图时生成哪个单位作为玩家（取它的预制体）。")]
    public UnitDefinition playerUnit;

    private void Awake()
    {
        if (!Application.isPlaying)
            return;
        if (GameObject.FindGameObjectWithTag("Player") != null)
            return;
        SkyPrisonPlayerStartPoint.SpawnPlayer(playerUnit, transform, this);
    }

#if UNITY_EDITOR
    // 不用 OnDrawGizmos：这个项目里 Scene 视图的 Gizmos 开关经常关着。
    [UnityEditor.InitializeOnLoadMethod]
    private static void HookSceneGui() => UnityEditor.SceneView.duringSceneGui += _ => DrawMarkers();

    private static void DrawMarkers()
    {
        var points = FindObjectsByType<SkyPrisonRaidArrivalPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var c = new Color(1f, 0.75f, 0.2f, 1f);
        foreach (var p in points)
        {
            Vector3 pos = p.transform.position + Vector3.up * 0.05f;
            Vector3 fwd = Quaternion.Euler(0f, p.transform.eulerAngles.y, 0f) * Vector3.forward;
            UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.25f);
            UnityEditor.Handles.DrawSolidDisc(pos, Vector3.up, 1.6f);
            UnityEditor.Handles.color = c;
            UnityEditor.Handles.DrawWireDisc(pos, Vector3.up, 1.6f, 3f);
            UnityEditor.Handles.DrawAAPolyLine(5f, pos, pos + fwd * 3f);
            UnityEditor.Handles.DrawDottedLine(pos, pos + Vector3.up * 40f, 6f);
            UnityEditor.Handles.Label(pos + Vector3.up * 4.5f, "RAID 落点");
        }
    }
#endif
}
