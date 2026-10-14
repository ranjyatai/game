using UnityEngine;

/// <summary>
/// 挂在 3D 通道运行时实例化出来的模型根节点上，记录来源 prefab——
/// UnitDefinitionRuntimeBinder.RefreshModel3DBindingsIfPossible 靠它判断
/// "Model3DRoot 下现有的实例是不是当前 UnitDefinition.model3DPrefab"，是就不用
/// 重新实例化（Awake/OnEnable/OnValidate 会反复调用这个刷新方法），换了模型资产
/// 才重建。
/// </summary>
public sealed class SkyPrisonModel3DVisualInstanceMarker : MonoBehaviour
{
    public GameObject SourcePrefab;
}
