using UnityEngine;

/// <summary>
/// 挂到场景任意 GameObject，Build 运行后查 Player.log 定位掉落物不显示的原因。
/// 路径：%APPDATA%\..\LocalLow\[公司名]\[游戏名]\Player.log（Windows）
/// </summary>
public class LootDropDiagnostic : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("========== [LootDropDiag] START ==========");

        // 1. Library 加载
        var lib = LootDropModelLibrary.Instance;
        if (lib == null)
        {
            Debug.LogError("[LootDropDiag] ❌ LootDropModelLibrary 加载失败！" +
                           "请确认 .asset 文件在任意 Resources 文件夹内，文件名不限。");
            return;
        }
        Debug.Log($"[LootDropDiag] ✓ Library 加载成功：{lib.name}");

        // 2. Hologram 材质
        if (lib.hologramMaterial == null)
            Debug.LogWarning("[LootDropDiag] ⚠ hologramMaterial 为空，模型会用原始材质。");
        else
        {
            Debug.Log($"[LootDropDiag] ✓ hologramMaterial：{lib.hologramMaterial.name}");
            if (lib.hologramMaterial.shader == null)
                Debug.LogError("[LootDropDiag] ❌ hologramMaterial.shader 为 null（被 strip 了）！");
            else
                Debug.Log($"[LootDropDiag] ✓ hologram shader：{lib.hologramMaterial.shader.name}");
        }

        // 3. VFX Shader
        if (lib.vfxHueShiftShader == null)
            Debug.LogWarning("[LootDropDiag] ⚠ vfxHueShiftShader 为空（VFX 染色会跳过，不影响模型）。");
        else
            Debug.Log($"[LootDropDiag] ✓ vfxHueShiftShader：{lib.vfxHueShiftShader.name}");

        // 4. 模型条目
        int modelCount = 0;
        foreach (var e in lib.generalEntries)
            if (e.model != null) modelCount++;
        foreach (var e in lib.equipmentEntries)
            if (e.model != null) modelCount++;
        Debug.Log($"[LootDropDiag] 模型条目有值数量：{modelCount}（为 0 则不会显示任何掉落模型）");

        // 5. VFX Prefab
        Debug.Log(lib.beaconVFXPrefab != null
            ? $"[LootDropDiag] ✓ beaconVFXPrefab：{lib.beaconVFXPrefab.name}"
            : "[LootDropDiag] beaconVFXPrefab 为空（使用自搓粒子）");

        // 6. 场景中的 LootDropVisual
        var visuals = FindObjectsByType<LootDropVisual>(FindObjectsSortMode.None);
        Debug.Log($"[LootDropDiag] 场景中 LootDropVisual 数量：{visuals.Length}");

        // 7. 相机 Culling Mask
        var cam = Camera.main;
        if (cam == null)
            Debug.LogError("[LootDropDiag] ❌ 找不到 Main Camera！");
        else
        {
            int world3DLayer = LayerMask.NameToLayer("World3D");
            bool renders = (cam.cullingMask & (1 << world3DLayer)) != 0;
            Debug.Log($"[LootDropDiag] Main Camera cullingMask={cam.cullingMask}，" +
                      $"World3D(layer {world3DLayer})={( renders ? "✓ 包含" : "❌ 不包含！模型不可见")}");
        }

        Debug.Log("========== [LootDropDiag] END ==========");
    }
}
