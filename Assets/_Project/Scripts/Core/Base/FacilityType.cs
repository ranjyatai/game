/// <summary>
/// 基地设施类型枚举。
/// 每个值对应一个 FacilityDefinition asset。
/// </summary>
public enum FacilityType
{
    CommandCenter = 0,  // 余穹中枢：主线推进核心，升级要求其他设施等级作前置

    Workshop      = 1,  // 工房：打造武器和防具
    SupplyPost    = 2,  // 供给所：购买日常所需品
    AntiqueStreet = 3,  // 古董街：随机稀有物品购买
    PowerStation  = 4,  // 发电区：剧情推进，中期解锁地铁
    IntelBoard    = 5,  // 情报板：接取额外赏金任务
    MedStation    = 6,  // 医疗站：剧情推进
    BeautySalon   = 7,  // 美容室：换发型/内衣Skin，不参与等级升级
}
