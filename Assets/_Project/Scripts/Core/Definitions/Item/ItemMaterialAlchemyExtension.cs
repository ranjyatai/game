using System;
using System.Collections.Generic;
using UnityEngine;

// 只有强化熔炉会读这份数据——决定这个素材扔进熔炉之后，跟目标装备/跟熔炉里
// 其它素材的契合度。跟 ItemEquipmentExtension.alchemyPreferredTags 共用同一个
// MaterialAffinityTagWeight 类型（定义在 MaterialAffinityDatabase.cs 里），
// 标签key要对得上 MaterialAffinityDatabase.tags 里注册的那些。
[Serializable]
public class ItemMaterialAlchemyExtension
{
    [Tooltip("这个素材携带的相性标签（金属/生物/能源……），配合权重决定它跟装备/\n" +
             "其它素材的契合度。留空=这个素材没有特殊相性，只贡献下面的基础效力。")]
    public List<MaterialAffinityTagWeight> affinityTags = new List<MaterialAffinityTagWeight>();

    [Tooltip("不考虑相性时，这个素材本身对成功率的基础贡献——对应\"一般素材丢一个\n" +
             "大概+10%\"这种基准线，具体换算成百分比的公式由强化系统运行时决定，\n" +
             "这里只是素材自己的原始效力值。")]
    public float basePotency = 10f;
}
