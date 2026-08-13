#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 遮挡判定两条路径的运行时对比工具。
///
/// 旧路径：CPU 每帧逐三角面射线求交决定授权，再由 RT 管线合成 _OcclusionTex。
///         开销随遮挡物数量和精度线性增长——实测一台叉车单帧 15.9ms。
/// 新路径：角色着色器直接采样 _CameraDepthTexture 做逐像素深度比较。
///         开销与场景复杂度无关，且比三角面更精确。
///
/// 这次改的是判定的根本机制，不是调参数。两条路径在什么情况下不一致、哪一侧更准，
/// 必须在同一位置同一帧来回切才看得清；分两次 Play 靠记忆比较会漏掉细微差别。
///
/// 三个深度参数是按几何关系估的初值、没有画面依据，所以也做成运行时可调——
/// 看着画面改比来回出 Build 快得多。
///
/// 不留任何序列化痕迹：运行时创建、DontSave、只在 Development Build 编译。
/// 上一次留下的 debugLogs 是序列化字段，被存进场景又打进 Build，白测了十小时。
/// </summary>
public sealed class OcclusionPathCompareHUD : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F9;
    private const KeyCode PanelKey = KeyCode.F8;
    private const string CompositeShaderName = "SpineOcclusionComposite";

    private static readonly int UseSceneDepthId = Shader.PropertyToID("_SkyPrison_UseSceneDepthOcclusion");
    private static readonly int BiasId = Shader.PropertyToID("_SkyPrison_SceneDepthBias");
    private static readonly int SoftnessId = Shader.PropertyToID("_SkyPrison_SceneDepthSoftness");
    private static readonly int FootScaleId = Shader.PropertyToID("_SkyPrison_SceneDepthFootScale");

    private bool useSceneDepth;
    private bool showPanel = true;
    private float bias = 0.05f;
    private float softness = 0.15f;
    private float footScale = 0.7f;

    private readonly List<Renderer> targets = new List<Renderer>();
    private MaterialPropertyBlock block;
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("~OcclusionPathCompareHUD");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<OcclusionPathCompareHUD>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            useSceneDepth = !useSceneDepth;
            Apply();
        }

        if (Input.GetKeyDown(PanelKey))
            showPanel = !showPanel;

        // 角色的渲染体是运行时生成的（换装/重建都会换），定期重扫才跟得上。
        if (Time.unscaledTime >= nextScanTime)
        {
            nextScanTime = Time.unscaledTime + 1f;
            Rescan();
            Apply();
        }
    }

    private void Rescan()
    {
        targets.Clear();
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null)
                continue;

            Material[] mats = r.sharedMaterials;
            if (mats == null)
                continue;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader == null)
                    continue;
                if (!mats[i].shader.name.Contains(CompositeShaderName))
                    continue;

                targets.Add(r);
                break;
            }
        }
    }

    private void Apply()
    {
        block ??= new MaterialPropertyBlock();

        foreach (Renderer r in targets)
        {
            if (r == null)
                continue;

            // 读改写：环境光接收器等系统也往同一个 block 写属性，
            // 直接 SetPropertyBlock 会把它们的值整块冲掉。
            r.GetPropertyBlock(block);
            block.SetFloat(UseSceneDepthId, useSceneDepth ? 1f : 0f);
            block.SetFloat(BiasId, bias);
            block.SetFloat(SoftnessId, softness);
            block.SetFloat(FootScaleId, footScale);
            r.SetPropertyBlock(block);
        }
    }

    private void OnGUI()
    {
        if (!showPanel)
            return;

        const float w = 380f;
        GUILayout.BeginArea(new Rect(12, 12, w, 210), GUI.skin.box);

        GUI.color = useSceneDepth ? Color.green : Color.yellow;
        GUILayout.Label(useSceneDepth
            ? "遮挡判定：场景深度（GPU 逐像素，CPU 归零）"
            : "遮挡判定：CPU 三角面射线（旧路径）");
        GUI.color = Color.white;

        GUILayout.Label($"F9 切换路径    F8 隐藏面板    命中 {targets.Count} 个渲染体");
        GUILayout.Space(6);

        if (!useSceneDepth)
        {
            GUILayout.Label("切到新路径后下面三项才生效。");
            GUILayout.EndArea();
            return;
        }

        float oldBias = bias, oldSoft = softness, oldFoot = footScale;

        GUILayout.Label($"深度阈值 Bias  {bias:F3}   （太小→贴地物件误挡脚）");
        bias = GUILayout.HorizontalSlider(bias, 0f, 1f);

        GUILayout.Label($"软过渡 Softness  {softness:F3}   （太小→遮挡边界锯齿）");
        softness = GUILayout.HorizontalSlider(softness, 0.001f, 1f);

        GUILayout.Label($"脚部补偿 FootScale  {footScale:F2}   （太小→头穿出矮物件）");
        footScale = GUILayout.HorizontalSlider(footScale, 0f, 2f);

        if (!Mathf.Approximately(oldBias, bias)
            || !Mathf.Approximately(oldSoft, softness)
            || !Mathf.Approximately(oldFoot, footScale))
            Apply();

        GUILayout.EndArea();
    }
}
#endif
