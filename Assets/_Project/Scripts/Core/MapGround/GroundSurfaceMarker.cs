using UnityEngine;

/// <summary>
/// 地面表面标记。挂在模型斜坡、桥、楼梯、特殊平台或 BaseGroundBlock 上。
/// 角色脚底检测命中模型表面时，优先读取这个组件的 SurfaceType。
/// </summary>
public class GroundSurfaceMarker : MonoBehaviour
{
    public GroundSurfaceType surfaceType = GroundSurfaceType.Concrete;

    /// <summary>
    /// 脚步声用的地表材质定义。留空则这个表面不产生地表音层，只剩基础鞋声。
    ///
    /// 不能靠 surfaceType 反查——枚举到定义是一对多：surfaceType=Concrete 底下就有
    /// 「马路-水泥1 / 马路-柏油 / 马路-水泥2 / 地板-石块」四个不同定义，反查不出唯一解。
    /// 所以这里必须直接引用具体资产。surfaceType 保留给别处用
    /// （BaseGroundBlock 判草地走的是它）。
    /// </summary>
    public GroundSurfaceMaterialDefinition surfaceDefinition;

    public bool isBaseGround = false;
    public bool overrideGroundHeight = false;
    public float groundHeightOffset = 0f;
}
