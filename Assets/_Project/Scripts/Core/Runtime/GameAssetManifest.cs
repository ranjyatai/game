using UnityEngine;

/// <summary>
/// 游戏关键 asset 清单。放在 Resources/GameAssetManifest.asset。
/// 由编辑器工具自动填充，运行时通过 GameAssetLoader 读取。
/// 不要手动修改此文件的字段值，统一由 GameAssetManifestBuilder 维护。
/// </summary>
[CreateAssetMenu(
    fileName = "GameAssetManifest",
    menuName  = "Sky Prison/Game Asset Manifest",
    order     = 1)]
public class GameAssetManifest : ScriptableObject
{
    [Header("自动填充 — 不要手动修改")]
    public ItemRegistry       itemRegistry;
    public TechTreeGraphAsset techTreeGraph;
    // 后续新增全局 asset 在这里加字段，Builder 里加对应扫描逻辑即可
}
