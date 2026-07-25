using UnityEngine;

/// <summary>
/// 地面表面标记。挂在模型斜坡、桥、楼梯、特殊平台或 BaseGroundBlock 上。
/// 角色脚底检测命中模型表面时，优先读取这个组件的 SurfaceType。
/// </summary>
public class GroundSurfaceMarker : MonoBehaviour
{
    public GroundSurfaceType surfaceType = GroundSurfaceType.Concrete;
    public bool isBaseGround = false;
    public bool overrideGroundHeight = false;
    public float groundHeightOffset = 0f;
}
