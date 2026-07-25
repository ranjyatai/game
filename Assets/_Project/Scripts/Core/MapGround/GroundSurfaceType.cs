using UnityEngine;

public enum GroundSurfaceType
{
    [InspectorName("未指定")]
    Default = 0,

    [InspectorName("石质地面 / 水泥")]
    Concrete = 1,

    [InspectorName("金属地面")]
    Metal = 2,

    [InspectorName("木质地面")]
    Wood = 3,

    [InspectorName("泥土地面")]
    Dirt = 4,

    [InspectorName("草地摩擦")]
    Grass = 5,

    [InspectorName("沙地颗粒")]
    Sand = 6,

    [InspectorName("浅水水花")]
    Water = 7,

    [InspectorName("玻璃 / 硬质")]
    Glass = 8,
}
