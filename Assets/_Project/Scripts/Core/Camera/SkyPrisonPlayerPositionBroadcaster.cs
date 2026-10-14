using UnityEngine;

/// <summary>
/// 每帧把玩家世界坐标写成全局着色器变量，供"相机挡视线时淡出候选建筑"用
/// （SkyPrisonSightlineFade.hlsl）。
///
/// 这是这套方案里唯一的 CPU 工作，而且是常数开销——不管场景里有多少候选建筑，
/// 这里都只写一次。候选建筑自己的着色器读这个全局变量、自己判断"我挡没挡"，
/// 不需要 CPU 逐个检测候选物体、不需要物理射线、不需要遍历。
///
/// 挂在 RenderPipelineManager.beginCameraRendering 上而不是 LateUpdate——
/// 跟今天头顶UI踩过的坑同一个原因：LateUpdate 读到的是 Cinemachine 还没在这一帧
/// 定位完之前的旧值，如果相机跟随玩家有阻尼/平滑，用旧位置算出来的淡出会跟这一帧
/// 实际渲染出的画面对不上，产生一帧的滞后感。玩家自己的位置理论上不受相机时机
/// 影响，但保持跟其它系统一致的写法，不留特例。
/// </summary>
public sealed class SkyPrisonPlayerPositionBroadcaster : MonoBehaviour
{
    private static readonly int PlayerWorldPosId = Shader.PropertyToID("_SkyPrison_PlayerWorldPos");

    [Tooltip("留空则用 SkyPrisonPlayerAuthority.CurrentPlayerUnit 自动找。")]
    [SerializeField] private Transform playerTransform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("~SkyPrisonPlayerPositionBroadcaster");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.DontSave; // 注意：不能用HideAndDontSave——
        // 今天AudioListener那次事故就是这个flag导致FindObjectsOfType看不见自己
        // 上一帧创建的实例，判定"没有可用实例"又建一个，每帧泄漏一个。这里虽然
        // 没有类似的每帧重建逻辑，但保持同一条纪律，不留隐患。
        go.AddComponent<SkyPrisonPlayerPositionBroadcaster>();
    }

    private void OnEnable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
    }

    private void OnDisable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
    }

    private void HandleBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
    {
        if (playerTransform == null)
        {
            var playerUnit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (playerUnit != null)
                playerTransform = playerUnit.transform;
        }

        if (playerTransform == null)
            return;

        Shader.SetGlobalVector(PlayerWorldPosId, playerTransform.position);
    }
}
