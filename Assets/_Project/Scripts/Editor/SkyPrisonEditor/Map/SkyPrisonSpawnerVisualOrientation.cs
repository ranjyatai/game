using UnityEngine;

/// <summary>
/// 孵化器视觉模型的朝向——唯一事实来源。
///
/// 模型是 Blender 出的，Blender 是 Z-up、Unity 是 Y-up，所以要绕 X 补 90 才是正朝上。
///
/// 层级长这样，模型不是根节点、是孙节点——它在 Hierarchy 里也叫 UnitSpawner，
/// 容易和根节点搞混，但带 MeshFilter 的是里面这个：
///   UnitSpawner            根，0,0,0，缩放 1
///     BoxRange / SphereRange
///     VisualRoot           0,0,0，缩放 1
///       UnitSpawner        模型，90,0,0，缩放 100，带 MeshFilter
///
/// 放置时调 Apply 强制写死，不依赖预制体有没有被人改过。
/// </summary>
public static class SkyPrisonSpawnerVisualOrientation
{
    /// <summary>
    /// Blender Z-up -> Unity Y-up 的修正角。
    /// 绕 X 转 +90（不是 -90——那是反方向，模型会朝下）。
    /// </summary>
    public static readonly Vector3 ModelLocalEuler = new Vector3(90f, 0f, 0f);

    private const string VisualRootName = "VisualRoot";

    /// <summary>
    /// 把孵化器实例底下所有视觉模型的本地旋转设成 ModelLocalEuler。
    /// 返回实际改动的节点数。
    ///
    /// 作用于 VisualRoot 的每一个直接子节点，而不是只认第一个——
    /// 视觉将来换成多段模型时不用回来改这里。
    /// </summary>
    public static int Apply(Transform spawnerRoot)
    {
        if (spawnerRoot == null)
            return 0;

        Transform visualRoot = spawnerRoot.Find(VisualRootName);
        if (visualRoot == null)
        {
            // 找不到就静默返回 0 的话，调用方会以为"已处理"。必须说出来。
            Debug.LogWarning(
                $"[SpawnerOrientation] {spawnerRoot.name} 底下找不到「{VisualRootName}」，" +
                $"朝向修正没有生效。直接子节点：{DescribeChildren(spawnerRoot)}", spawnerRoot);
            return 0;
        }

        int changed = 0;
        for (int i = 0; i < visualRoot.childCount; i++)
        {
            Transform model = visualRoot.GetChild(i);
            if (model == null)
                continue;

            // 直接比欧拉角会被 (0,0,0) 和 (360,360,360) 这种等价表示骗过去，
            // 也会被浮点误差骗过去。比四元数夹角才是可靠的判据。
            Quaternion target = Quaternion.Euler(ModelLocalEuler);

            // 比四元数夹角，不比欧拉角——欧拉角会被 (0,0,0)/(360,360,360) 这类等价表示
            // 和浮点误差骗过去。
            if (Quaternion.Angle(model.localRotation, target) < 0.01f)
                continue;

            model.localRotation = target;
            changed++;
        }

        return changed;
    }

    private static string DescribeChildren(Transform parent)
    {
        if (parent == null || parent.childCount == 0)
            return "(无)";

        var names = new string[parent.childCount];
        for (int i = 0; i < parent.childCount; i++)
            names[i] = parent.GetChild(i) != null ? parent.GetChild(i).name : "<null>";
        return string.Join(", ", names);
    }
}
