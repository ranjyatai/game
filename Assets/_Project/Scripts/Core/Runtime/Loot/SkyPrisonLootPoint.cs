using UnityEngine;

/// <summary>
/// 资源箱候选点位：手动摆在地图里（放置工具「资源箱点位」书签）。
/// 每一把进图时，SkyPrisonLootDirector 按地图编辑器里的配置（每种箱子最多几个、最小间距），
/// 从「同一种箱子」的候选点位里随机抽，抽中的点生成箱子，没抽中的这把不出。
/// 点位本身只是个空物体，没有任何碰撞和渲染。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonLootPoint : MonoBehaviour
{
    [Tooltip("这个点位给哪一种箱子用（单位定义，比如物资箱 A）。每种箱子只从自己的点位里抽，A 和 B 互不占用。")]
    public UnitDefinition box;

    [Tooltip("抽选权重，默认 1。越大越容易被抽中。")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("可选：区块名，只用于整理和以后做分区保底，不影响抽选。")]
    public string zone = "";
}
