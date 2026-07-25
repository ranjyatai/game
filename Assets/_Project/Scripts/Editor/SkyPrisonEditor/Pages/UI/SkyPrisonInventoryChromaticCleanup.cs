#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using SkyPrison.Runtime.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性清理：背包 prefab 上的 SkyPrisonInventoryChromatic 组件序列化强度是0
    /// （chromaticIntensity=0），全局样式设置里 enableChromaticByDefault 也是0——
    /// 这个效果从头到尾就没让人看见过，但组件本身没跟着一起关掉，LateUpdate和背后
    /// 每0.4秒一次的全屏ScreenCapture一直在跑，只有开销没有效果，是背包"站着不动
    /// 就卡"的真正原因。直接删掉这个组件，不需要在仓库上补一份（补了也是白白加同样
    /// 的开销换同样看不见的效果）。
    /// </summary>
    public static class SkyPrisonInventoryChromaticCleanup
    {
        private const string InventoryPrefabPath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";

        [MenuItem("Tools/Sky Prison/UI/清理背包无效色收差组件")]
        public static void Cleanup()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(InventoryPrefabPath);
            try
            {
                var chromatic = root.GetComponentInChildren<SkyPrisonInventoryChromatic>(true);
                if (chromatic == null)
                {
                    Debug.LogWarning("[SkyPrisonInventoryChromaticCleanup] 背包prefab上没找到SkyPrisonInventoryChromatic组件，可能已经清理过了。");
                    return;
                }

                GameObject host = chromatic.gameObject;
                Object.DestroyImmediate(chromatic, true);
                PrefabUtility.SaveAsPrefabAsset(root, InventoryPrefabPath);
                Debug.Log($"[SkyPrisonInventoryChromaticCleanup] 已从 {host.name} 上删除 SkyPrisonInventoryChromatic 组件并保存 {InventoryPrefabPath}。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
