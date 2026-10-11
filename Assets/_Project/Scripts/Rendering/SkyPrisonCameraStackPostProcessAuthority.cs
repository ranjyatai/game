using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 强制"后处理必须跑在相机栈的最后一台相机上"。
///
/// 背景（2026-08-04 定位到的头顶UI偏移根因）：
/// URP 是"为开启了后处理的那台相机单独跑一次后处理 pass"，不是"对整个相机栈的
/// 最终合成结果跑一次"。本项目的栈是
///     Main Camera(Base, 画角色/场景) → GamePlayCamera(Overlay) → OverheadUICamera(Overlay, 画头顶UI)
/// 后处理原本开在 Base 相机上，于是实际顺序变成：
///     画角色 → 对角色做后处理 → Overlay 把头顶UI画在"已经后处理完"的画面上
/// 头顶UI 从来没进过那条 pass，两条路径的最终像素位置对不齐。表现是：玩家一移动，
/// NPC 头顶的名字就相对 NPC 本体左右上下飘移，且离屏幕中心越远偏得越多。
///
/// 这个偏移在 CPU 端完全测不出来——锚点相对角色根节点恒定、两台相机的
/// worldToCameraMatrix/projectionMatrix 在渲染时刻逐位相同、精灵图顶点着色器是纯
/// UnityObjectToClipPos，全都干净。差异只存在于"谁进过后处理 pass"。
///
/// 正确做法就是 URP 文档说的：相机栈里只在最后一台相机上开后处理，这样先把
/// 角色和 UI 全部合成完，再对整幅画面做一次后处理。
///
/// 放在代码里而不是靠场景里的相机勾选框，是因为这个约束一旦被人在 Inspector 里
/// 改回去，症状会以"UI 莫名其妙飘"的形式出现，而且极难查（这次查了整整一天）。
/// </summary>
public static class SkyPrisonCameraStackPostProcessAuthority
{
    /// <summary>栈尾相机用的抗锯齿。不能用 TAA：TAA 靠运动矢量做历史重投影，
    /// 而 Spine 精灵图走的自定义遮挡合成着色器不写运动矢量。</summary>
    private const AntialiasingMode StackTailAntialiasing =
        AntialiasingMode.SubpixelMorphologicalAntiAliasing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        UnityEngine.Rendering.RenderPipelineManager.beginFrameRendering -= HandleBeginFrame;
        UnityEngine.Rendering.RenderPipelineManager.beginFrameRendering += HandleBeginFrame;

        Apply();
    }

    /// <summary>每帧把 Base 相机的投影参数同步给栈里的 Overlay 相机。
    ///
    /// 必须每帧做，不能只在场景加载时做一次：Base 相机(Main Camera)的镜头是被
    /// CinemachineBrain 每帧从虚拟相机的 Lens 覆盖写入的，而 Overlay 相机不归
    /// Cinemachine 管，各自守着自己序列化的那份数值。只要两边对不上，同一个世界
    /// 点在两台相机下就会投影到不同的屏幕位置——角色由 Base 画、头顶UI由 Overlay
    /// 画，于是UI相对角色漂移，且离屏幕中心越远偏得越多。这个症状 2026-08-04
    /// 查了整整一天，根因是后处理挂错相机；投影参数不同步会复现一模一样的现象，
    /// 所以在这里一并锁死。
    ///
    /// 结果就是：正交尺寸只需要在虚拟相机的 Lens 上改一处，整个栈自动跟随。</summary>
    private static void HandleBeginFrame(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera[] cameras)
    {
        if (cameras == null) return;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera baseCam = cameras[i];
            if (baseCam == null) continue;

            UniversalAdditionalCameraData data = baseCam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null || data.renderType != CameraRenderType.Base) continue;

            var stack = data.cameraStack;
            if (stack == null) continue;

            for (int j = 0; j < stack.Count; j++)
            {
                Camera overlay = stack[j];
                if (overlay == null || overlay == baseCam) continue;

                overlay.orthographic = baseCam.orthographic;
                overlay.orthographicSize = baseCam.orthographicSize;
                overlay.fieldOfView = baseCam.fieldOfView;
                overlay.nearClipPlane = baseCam.nearClipPlane;
                overlay.farClipPlane = baseCam.farClipPlane;
            }
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    /// <summary>场景里的相机栈可能在运行中被重建（换图、切演出相机），
    /// 需要重新套用时外部可以直接调这个。</summary>
    public static void Apply()
    {
        foreach (Camera cam in Camera.allCameras)
        {
            if (cam == null) continue;

            UniversalAdditionalCameraData baseData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (baseData == null || baseData.renderType != CameraRenderType.Base) continue;

            // 栈尾 = 列表里最后一台还活着的 Overlay 相机；没有 Overlay 就是 Base 自己。
            Camera tail = cam;
            var stack = baseData.cameraStack;
            if (stack != null)
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i] != null) { tail = stack[i]; break; }
                }
            }

            // 栈里除栈尾以外的相机一律不许自己跑后处理。
            SetPostProcessing(cam, cam == tail);
            if (stack != null)
            {
                for (int i = 0; i < stack.Count; i++)
                {
                    if (stack[i] != null)
                        SetPostProcessing(stack[i], stack[i] == tail);
                }
            }
        }
    }

    private static void SetPostProcessing(Camera cam, bool enabled)
    {
        UniversalAdditionalCameraData data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) return;

        data.renderPostProcessing = enabled;
        data.antialiasing = enabled ? StackTailAntialiasing : AntialiasingMode.None;
    }
}
