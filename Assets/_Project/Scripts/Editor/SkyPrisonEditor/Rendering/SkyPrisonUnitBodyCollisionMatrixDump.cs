using UnityEditor;
using UnityEngine;

/// <summary>
/// 2026-08-19：一次性诊断——箱子 CombatHurtbox 从来没触发过 OnTriggerEnter，怀疑物理
/// 层碰撞矩阵把 UnitBody 跟武器 Hitbox 所在层之间的碰撞关掉了。直接读
/// Physics.GetIgnoreLayerCollision 的真值，不去手解 ProjectSettings 里那串压缩过的
/// 十六进制矩阵——格式不透明，手解容易解错。确认完可以删。
/// </summary>
public static class SkyPrisonUnitBodyCollisionMatrixDump
{
    [MenuItem("天空囚笼/调试/打印 UnitBody 碰撞矩阵", false, 227)]
    public static void Dump()
    {
        DumpLayer("UnitBody");
    }

    [MenuItem("天空囚笼/调试/打印 World3D 碰撞矩阵", false, 228)]
    public static void DumpWorld3D()
    {
        DumpLayer("World3D");
    }

    private static void DumpLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
        {
            Debug.LogError($"[碰撞矩阵] 找不到 {layerName} 层");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[碰撞矩阵] {layerName}(层{layer}) 与各层的碰撞状态（true=会碰撞，false=被忽略）：");
        for (int i = 0; i < 32; i++)
        {
            string otherName = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(otherName)) continue;
            bool ignored = Physics.GetIgnoreLayerCollision(layer, i);
            sb.AppendLine($"  {layerName} × {otherName}(层{i}): ignore={ignored}");
        }
        Debug.Log(sb.ToString());
    }
}
