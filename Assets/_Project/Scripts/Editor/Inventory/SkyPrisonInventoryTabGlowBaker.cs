using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SkyPrison.EditorTools
{
    /// <summary>
    /// 把背包筛选标签的旧版 UI.Text 烤成 TextMeshPro + 沿字形蓝色辉光，直接写进预制体。
    /// 这样工作台(编辑态)和运行时显示一致(所见即所得),不再依赖运行时改样式。
    ///
    /// 菜单：Sky Prison/背包/烤入标签辉光 (TMP)
    /// </summary>
    public static class SkyPrisonInventoryTabGlowBaker
    {
        private const string MainPrefabPath     = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";
        private const string MirrorPrefabPath   = "Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab";
        public  const string FontPath           = "Assets/_Project/UIUX/Fonts/TMP/msyh SDF.asset";
        private const string GlowMaterialPath   = "Assets/_Project/Materials/UI/Window/M_Inventory_TabGlow.mat";

        public  const int    SelectedTabIndex = 0;
        public  const float  TabFontSize      = 18f;
        public  static readonly Color SelectedFace = SkyPrison.Runtime.UI.SkyPrisonUIPalette.White;
        public  static readonly Color NormalColor  = SkyPrison.Runtime.UI.SkyPrisonUIPalette.White; // 未选中也用白
        private static readonly Color GlowColor    = SkyPrison.Runtime.UI.SkyPrisonUIPalette.ColdGreen;

        [MenuItem("Sky Prison/背包/烤入标签辉光 (TMP)")]
        public static void Bake()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null)
            {
                EditorUtility.DisplayDialog("烤入失败", $"找不到字体:\n{FontPath}", "确定");
                return;
            }

            Material glowMat = GetOrCreateGlowMaterial(font);

            int total = 0;
            total += BakePrefab(MainPrefabPath, font, glowMat);
            total += BakePrefab(MirrorPrefabPath, font, glowMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"已烤入 {total} 个标签(含两份预制体)。\n工作台与运行时现在一致。", "好");
        }

        // 用字体的图集材质为蓝本，建一个带柔和外发光 + 白色字面的材质资源(build 安全)。
        public static Material GetOrCreateGlowMaterial(TMP_FontAsset font)
        {
            EnsureFolder("Assets/_Project/Materials/UI/Window");

            Material glowMat = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (glowMat == null)
            {
                glowMat = new Material(font.material);
                AssetDatabase.CreateAsset(glowMat, GlowMaterialPath);
            }
            else
            {
                glowMat.shader = font.material.shader;
                glowMat.CopyPropertiesFromMaterial(font.material);
            }

            glowMat.SetColor(ShaderUtilities.ID_FaceColor, SelectedFace);

            // 用 Glow(沿字形发光)。它的 _ScaleRatioB 从字体图集材质继承有效，能可靠显示;
            // underlay 需要 _ScaleRatioC 且受字体 SDF 边距限制，之前实测在动态字体上不显示，故不用。
            glowMat.EnableKeyword(ShaderUtilities.Keyword_Glow);
            glowMat.SetColor(ShaderUtilities.ID_GlowColor, GlowColor);
            glowMat.SetFloat(ShaderUtilities.ID_GlowPower, 1f);
            glowMat.SetFloat(ShaderUtilities.ID_GlowOuter, 1f);
            glowMat.SetFloat(ShaderUtilities.ID_GlowInner, 0f);
            glowMat.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
            ShaderUtilities.UpdateShaderRatios(glowMat);

            EditorUtility.SetDirty(glowMat);
            return glowMat;
        }

        private static int BakePrefab(string path, TMP_FontAsset font, Material glowMat)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogWarning($"[TabGlowBaker] 打不开预制体:{path}");
                return 0;
            }

            Transform filterBar = FindDeep(root.transform, "FilterBar");
            int count = 0;
            if (filterBar != null)
            {
                // 按层级顺序收集标签
                var tabs = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in filterBar)
                    if (child.name.StartsWith("FilterTab"))
                        tabs.Add(child);

                for (int i = 0; i < tabs.Count; i++)
                {
                    if (ConvertTab(tabs[i], i == SelectedTabIndex, font, glowMat))
                        count++;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            return count;
        }

        // 把单个标签的旧版 Text 换成 TMP;选中项用辉光材质。
        private static bool ConvertTab(Transform tab, bool selected, TMP_FontAsset font, Material glowMat)
        {
            // 已是 TMP 则只更新样式
            TextMeshProUGUI existing = tab.GetComponent<TextMeshProUGUI>();
            string label;

            if (existing != null)
            {
                label = existing.text;
            }
            else
            {
                Text legacy = tab.GetComponent<Text>();
                label = legacy != null ? legacy.text : tab.name.Replace("FilterTab_", "");
                if (legacy != null) Object.DestroyImmediate(legacy, true);
                existing = tab.gameObject.AddComponent<TextMeshProUGUI>();
            }

            existing.font          = font;
            existing.text          = label;
            existing.fontSize      = TabFontSize;
            existing.alignment     = TextAlignmentOptions.Center;
            existing.raycastTarget = true; // 标题/标签区保留点击能力
            existing.color         = selected ? SelectedFace : NormalColor;
            existing.fontSharedMaterial = selected ? glowMat : font.material;

            EditorUtility.SetDirty(existing);
            return true;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
