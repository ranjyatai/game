#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SkyPrison.Editor.UI
{
    /// <summary>一次性把新拷进项目的锁头图标按 UI Sprite 规范导入（Unity默认导入的贴图
    /// 类型是Default，不是Sprite，直接拖进Image组件用不了），设置跟其它窗口图标
    /// （UIWindow_Default_Close.png）同一套导入参数。</summary>
    public static class SkyPrisonImportLockIconAsSprite
    {
        private const string IconPath = "Assets/_Project/UIUX/Window/Styles/Default/Sprites/UIWindow_Default_Lock.png";

        [MenuItem("Tools/Sky Prison/UI/导入锁头图标为Sprite")]
        public static void Import()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[SkyPrisonImportLockIconAsSprite] 找不到贴图导入器：{IconPath}");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            Debug.Log($"[SkyPrisonImportLockIconAsSprite] 已把 {IconPath} 设置为 Sprite 类型。");
        }
    }
}
#endif
