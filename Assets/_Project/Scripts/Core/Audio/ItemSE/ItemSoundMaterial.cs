/// <summary>
/// 物品的声音材质类型。决定拿起/放下时播放哪一套音效。
/// 在 ItemDefinition 里配置，运行时由 SkyPrisonItemMaterialSoundTable 查表播放。
/// </summary>
public enum ItemSoundMaterial
{
    [UnityEngine.InspectorName("默认")]           Default = 0,
    [UnityEngine.InspectorName("金属")]           Metal,
    [UnityEngine.InspectorName("粉末 / 颗粒")]    Powder,
    [UnityEngine.InspectorName("布料 / 皮革")]    Fabric,
    [UnityEngine.InspectorName("液体容器")]       Liquid,
    [UnityEngine.InspectorName("纸张 / 书册")]    Paper,
    [UnityEngine.InspectorName("塑料 / 包装袋")] Plastic,
    [UnityEngine.InspectorName("骨质 / 骨制品")] Bone,
    [UnityEngine.InspectorName("科技物 / 电子元件")] Tech,
}
