using UnityEngine;

/// <summary>
/// 放在 Resources/SkyPrison/ 下的轻量引导 asset。
/// 只持有对 LocalizationProjectSettings 的引用，供运行时自动加载。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Localization/Runtime Config", fileName = "LocalizationRuntimeConfig")]
public class LocalizationRuntimeConfig : ScriptableObject
{
    public LocalizationProjectSettings settings;
}
