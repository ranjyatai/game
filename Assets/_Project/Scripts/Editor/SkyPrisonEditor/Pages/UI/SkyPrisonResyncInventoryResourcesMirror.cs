#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 一次性修复：背包实际存在两份 prefab——真正的源
    /// (Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab，日常编辑/
    /// 这次会话里改角标大小、色收差清理等都是改这一份) 和
    /// Resources 镜像 (Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab，
    /// 独立打开背包的热键路径靠 Resources.Load 加载的就是这一份)。仓库那边
    /// "同时打开背包"用的是直接 AssetDatabase 引用源prefab，两条路径读的是不同文件——
    /// 镜像早就没跟着源同步过，两边角标大小对不上正是这个原因（不是运行时缩放逻辑
    /// 的问题）。仓库自己的生成脚本每次都会重新拷镜像，背包没有对应的自动化，
    /// 手动补一次。
    /// </summary>
    public static class SkyPrisonResyncInventoryResourcesMirror
    {
        private const string SourcePath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";
        private const string MirrorPath = "Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab";

        [MenuItem("Tools/Sky Prison/UI/同步背包Resources镜像")]
        public static void Resync()
        {
            AssetDatabase.DeleteAsset(MirrorPath);
            if (AssetDatabase.CopyAsset(SourcePath, MirrorPath))
            {
                AssetDatabase.ImportAsset(MirrorPath);
                Debug.Log($"[SkyPrisonResyncInventoryResourcesMirror] 已把 {SourcePath} 同步覆盖到 {MirrorPath}。");
            }
            else
            {
                Debug.LogError($"[SkyPrisonResyncInventoryResourcesMirror] 拷贝失败：{SourcePath} → {MirrorPath}");
            }
        }
    }
}
#endif
