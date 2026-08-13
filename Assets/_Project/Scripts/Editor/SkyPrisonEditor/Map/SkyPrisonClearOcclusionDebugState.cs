using UnityEditor;
using UnityEngine;

/// <summary>
/// 清掉遮挡诊断残留在 Renderer 上的 MaterialPropertyBlock。
///
/// 为什么需要这个菜单：诊断值是通过 MaterialPropertyBlock 写在 Renderer 上的，
/// 它不属于任何脚本，脚本消失了它还在。OcclusionPathCompareHUD 只在场景加载时
/// 创建一次，编译触发的 Domain Reload 会销毁它却不会重建——于是没有任何人再去
/// 写这些属性，角色被永久钉死在最后一次写入的诊断模式上，渲染成纯黑剪影。
///
/// 这种状态用「退出 Play 再进」解决不了（PropertyBlock 在编辑态也在），
/// 也不该让用户手动去每个 Renderer 上点 Clear。
/// </summary>
public static class SkyPrisonClearOcclusionDebugState
{
    private const string LogPrefix = "[SkyPrison 遮挡诊断清理]";

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/清除诊断残留（角色发黑时用）")]
    public static void Run()
    {
        int cleared = 0;

        foreach (UnitOcclusionMaterialReceiver receiver in
                 Object.FindObjectsByType<UnitOcclusionMaterialReceiver>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (receiver == null)
                continue;

            foreach (Renderer r in receiver.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.HasPropertyBlock())
                    continue;

                // 整块清掉而不是把诊断项写 0：环境光接收器等系统也往同一个 block
                // 写属性，但它们每帧都会重写，清空不会造成持久损失；而诊断项一旦
                // 残留就没人再写，只能靠这里彻底移除。
                r.SetPropertyBlock(null);
                EditorUtility.SetDirty(r);
                cleared++;
            }
        }

        Debug.Log($"{LogPrefix} 已清除 {cleared} 个渲染体上的 PropertyBlock。" +
                  "角色应立刻恢复正常着色；若仍发黑，说明问题不在诊断残留。");
    }
}
