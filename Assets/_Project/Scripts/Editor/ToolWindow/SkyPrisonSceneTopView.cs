using UnityEditor;
using UnityEngine;

/// <summary>
/// 场景视图一键切到「正交俯视」，再按一次回到切换前的视角。
///
/// Unity 自带的 2D 按钮看的是 XY 垂直切面（给 2D 游戏用的），我们的地图铺在 XZ 地面上，
/// 摆地图要的是「从正上方往下看 + 正交投影」：没有近大远小，墙体对齐、距离判断都准。
/// 地图放置工具的标题栏上有按钮调用这里。
/// </summary>
public static class SkyPrisonSceneTopView
{
    private static readonly Quaternion TopRotation = Quaternion.Euler(90f, 0f, 0f);

    private static bool s_HasSaved;
    private static Vector3 s_SavedPivot;
    private static Quaternion s_SavedRotation;
    private static float s_SavedSize;
    private static bool s_SavedOrthographic;

    /// <summary>当前场景视图是否处在正交俯视（按实际状态判断，手动转歪了就算退出）。</summary>
    public static bool IsActive
    {
        get
        {
            SceneView sv = SceneView.lastActiveSceneView;
            return sv != null && sv.orthographic && !sv.in2DMode
                && Quaternion.Angle(sv.rotation, TopRotation) < 0.5f;
        }
    }

    [MenuItem("天空囚笼/场景视图 俯视 ⇄ 还原 %#t", false, 2)]
    public static void Toggle()
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null)
            return;

        if (IsActive)
            Restore(sv);
        else
            EnterTopView(sv);
    }

    private static void EnterTopView(SceneView sv)
    {
        s_SavedPivot = sv.pivot;
        s_SavedRotation = sv.rotation;
        s_SavedSize = sv.size;
        s_SavedOrthographic = sv.orthographic;
        s_HasSaved = true;

        // 俯视看的是地面：中心点落到地面高度，不然原视角的 pivot 可能悬在半空或埋在地下。
        Vector3 pivot = sv.pivot;
        pivot.y = 0f;

        sv.in2DMode = false;
        sv.LookAt(pivot, TopRotation, sv.size, true, false);
    }

    private static void Restore(SceneView sv)
    {
        if (!s_HasSaved)
        {
            // 没有记录（比如重新编译过）：退回一个常用的 45° 斜视透视。
            sv.LookAt(sv.pivot, Quaternion.Euler(45f, 0f, 0f), sv.size, false, false);
            return;
        }

        // 俯视期间平移过的话，还原时跟着平移过的位置走，只恢复角度、缩放和投影方式。
        Vector3 pivot = sv.pivot;
        pivot.y = s_SavedPivot.y;
        sv.LookAt(pivot, s_SavedRotation, s_SavedSize, s_SavedOrthographic, false);
        s_HasSaved = false;
    }
}
