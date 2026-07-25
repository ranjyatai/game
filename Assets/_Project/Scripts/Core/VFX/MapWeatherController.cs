using RaindropFX;
using SkyPrison.Runtime.VFX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

/// <summary>
/// 地图天气特效播放器。不用手动挂——地图编辑器"天气"表单配好开启/类型/强度后，
/// 运行时会从 MapWeatherRegistry（编辑器保存 MapDefinition 时自动同步）按当前场景
/// 自动生成天气特效实例，跟地图BGM（MapBGMController/MapBGMRegistry）同一套模式。
/// 进场景自动生成，场景本身销毁时随场景一起清理，不用额外处理。
/// </summary>
[DisallowMultipleComponent]
public class MapWeatherController : MonoBehaviour
{
    public static MapWeatherController Instance { get; private set; }

    private const string RuntimeObjectName = "SkyPrisonMapWeather";
    private static bool _sceneLoadedSubscribed;

    // Falling Ash 系列预制体根节点带了烘焙好的 -90°X轴旋转，换算下来局部Z轴
    // （原始 Shape Box 那个"5"的窄轴）才是世界坐标里真正管上下的那个轴——也就是说
    // 这个特效"能往下掉的空间"在世界里只有薄薄5个单位，直接摆在地图边界中心（贴近
    // 地面高度）粒子一冒出来几乎立刻就会因为重力沉到地表以下，肉眼只看得到刚生成
    // 那零点几秒的自转，之后就沉没消失，看起来像"卡在原地打转"。往上抬高生成点，
    // 让整个下落窗口留在地面以上，才能看见真正的飘落轨迹。
    private const float SkyHeightOffset = 20f;

    // RaindropFX_GPU.distortion 是屏幕融合效果强度，官方示例默认值是 1.0，官方
    // 滑条范围给到 0~10 但那是给美术拉极端效果用的，正常镜头湿润程度用不到那么高。
    // 强度(0~1) 映射到 0~这个上限，1.2 大概是下很大的雨、镜头明显花掉的量级。
    private const float MaxLensDistortion = 1.2f;
    private const string RainLensProfileResourceName = "SkyPrisonRainLensWetnessProfile";

    // 关闭域重载（Enter Play Mode Options）时静态字段不会自动归零，这里手动重置，
    // 避免上一次 Play 残留的订阅状态导致本次不再生成节点——跟 MapBGMController 同一
    // 个坑，见 misdiagnosed_bgm_probe_clip 那次排查记录。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _sceneLoadedSubscribed = false;
        MapWeatherRegistry.ClearCache();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateAfterSceneLoad()
    {
        if (!Application.isPlaying)
            return;

        EnsureForActiveScene();

        if (!_sceneLoadedSubscribed)
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            _sceneLoadedSubscribed = true;
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.isPlaying)
            EnsureForActiveScene();
    }

    private static void EnsureForActiveScene()
    {
        if (Instance != null)
            return; // 已经有一个了

        Scene scene = SceneManager.GetActiveScene();

        MapWeatherRegistry registry = MapWeatherRegistry.LoadOrNull();
        MapWeatherRegistry.Entry entry = registry != null ? registry.GetForScene(scene.name, scene.path) : null;

        // 镜头湿润(RaindropFX)挂在一个跨场景常驻的共享 Volume Profile 资产上——上一张
        // 地图如果下过雨，这个 Profile 的 enable/distortion 会一直停在"开"的状态，不会
        // 自己复位。之前只在"这张地图确实需要下雨"时才去处理它，不需要就直接跳过，
        // 导致换到不下雨的地图后雨滴效果原样留着。现在不管这张地图要不要下雨、甚至
        // 压根没有在天气注册表里（entry为null），每次进场景都无条件显式设置一次
        // （没有强度就传0，等于强制关掉），该开开、该关关，不会有跨地图残留状态。
        float lensWetnessIntensity = entry?.lensWetnessIntensity ?? 0f;
        bool hasParticles = entry != null && entry.weatherPrefab != null;

        GameObject controllerGo = new GameObject(RuntimeObjectName);
        MapWeatherController controller = controllerGo.AddComponent<MapWeatherController>();
        controller.Setup(entry, hasParticles, lensWetnessIntensity);
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Rain VFX[URP]资产暴露出来的Vector3属性名，World Rain系列prefab（World Rain
    // Splash Spawner脚本）都是按这个名字读写生成范围盒子的，跟贴图/预制体本身没关系，
    // 是资产作者定死的字符串，改不了。
    private const string RainBoxSizeProperty = "Rain Particle Spawn Box Size";

    private void Setup(MapWeatherRegistry.Entry entry, bool hasParticles, float lensWetnessIntensity)
    {
        if (hasParticles)
            SpawnParticles(entry);

        ApplyLensWetness(lensWetnessIntensity);

        if (entry != null && entry.isHeavyRain)
        {
            GameObject lightningGo = new GameObject("SkyPrisonHeavyRainLightning");
            lightningGo.transform.SetParent(transform, false);
            var lightning = lightningGo.AddComponent<SkyPrisonHeavyRainLightning>();
            lightning.Configure(entry.thunderClips);
        }

        if (entry != null && entry.ambientClip != null)
            PlayAmbient(entry.ambientClip, entry.ambientVolume);
    }

    // 雨天环境音（BGS）——常驻循环播放，音量在构建注册表时就按降雨强度算好了，
    // 这里只负责播，不用再关心强度/天气类型。
    // 跟 MapBGMController 同一个坑——用户反馈"读条界面时候BGS就已经漏出来了"。天气
    // 系统是 AfterSceneLoad 就立刻生成的，比读条界面真正淡出/消失要早得多，之前没等
    // 场景/摄像机/读条动画走完就直接播，声音会在还在读条的时候透出来。参考
    // MapBGMController 的做法——不是真的等一个"读条结束"事件（这个项目里没有这种
    // 事件），而是先等一小段固定延迟，让读条动画有时间盖住这几百毫秒。
    private const float AmbientStartDelay = 0.6f;

    private void PlayAmbient(AudioClip clip, float volume)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 环境音，不做3D定位
        source.volume = 0f;
        StartCoroutine(PlayAmbientDelayed(source, volume));
    }

    private System.Collections.IEnumerator PlayAmbientDelayed(AudioSource source, float volume)
    {
        yield return new WaitForSeconds(AmbientStartDelay);
        if (source == null) yield break;
        source.Play();
        yield return FadeAmbientVolume(source, volume, 2f);
    }

    private System.Collections.IEnumerator FadeAmbientVolume(AudioSource source, float target, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (source == null) yield break;
            source.volume = Mathf.Lerp(0f, target, t / duration);
            yield return null;
        }
        if (source != null) source.volume = target;
    }

    private void SpawnParticles(MapWeatherRegistry.Entry entry)
    {
        GameObject go = Instantiate(entry.weatherPrefab, transform, false);

        // Rain VFX[URP]资产走的是VFX Graph（VisualEffect组件），不是传统ParticleSystem，
        // 范围盒子是一个暴露出来的Vector3属性，不是ParticleSystem.ShapeModule，
        // 上面那套FitShapeToMapBounds对它不生效，得单独按VisualEffect的API处理。
        VisualEffect[] vfxList = go.GetComponentsInChildren<VisualEffect>(true);
        bool isVFXBased = vfxList.Length > 0;

        // SkyHeightOffset(20) 是照 Falling Ash 那种烘焙了-90°X旋转的扬尘素材调的，
        // 硬套到雨水VFX Graph上导致下落起点比资产自己demo场景高了一大截——雨滴的
        // 速度/生命周期(1~1.8秒、Y方向-6~-10)是照那个更低的原装高度配的，起点抬这么
        // 高会导致粒子还没落到地面生命周期就到了，飘在半空中消失，形成一条"悬空雨带"
        // （用户截图里那个卡在半空的现象）。雨水直接用地图边界中心，不额外加高，让
        // 素材自己预制体内部已经调好的子物体局部偏移去决定实际生成高度。
        go.transform.position = isVFXBased ? entry.boundsCenter : entry.boundsCenter + Vector3.up * SkyHeightOffset;

        ParticleSystem ps = go.GetComponentInChildren<ParticleSystem>(true);
        if (ps != null)
            FitShapeToMapBounds(ps, entry.boundsSize);

        foreach (VisualEffect vfx in vfxList)
        {
            // 雨滴的下落速度/生命周期是素材照自己demo场景的生成高度调的，这个高度是
            // 预制体内部子物体自带的局部偏移（我们没有去改它），跟地图实际地面高度对
            // 不上，导致粒子还没"落地"生命周期就到了，直接穿过地板消失，看不到落地
            // 溅起的效果（用户反馈"雨水穿过地板"）。用子物体相对根节点的实际高度差，
            // 反推出一个能让雨滴刚好落到地面附近的速度/生命周期，而不是照搬demo场景
            // 的固定数值。
            float spawnHeightAboveGround = vfx.transform.position.y - go.transform.position.y;
            FitVFXBoxToMapBounds(vfx, entry.boundsSize, spawnHeightAboveGround);
        }

        // 外部素材包自带的 Layer 大概率是 Default，主摄像机的 Culling Mask 不一定包含它
        // （跟脚步/血液扬尘特效同一个坑）——统一切到 World3D，跟其它 3D 特效用同一个
        // 已确认在主摄像机可见范围内的图层。
        int world3DLayer = LayerMask.NameToLayer("World3D");
        if (world3DLayer >= 0)
            SetLayerRecursive(go, world3DLayer);
    }

    // VFX Graph暴露出来另一个独立的"VFX Bounds Size/Center"属性，专门管裁剪范围，
    // 跟"Rain Particle Spawn Box Size"（管生成范围）是两码事——之前只放大了生成范围，
    // 裁剪盒子还停在资产demo场景原装的一个35x20x35小盒子，Unity按这个小盒子跟摄像机
    // 视锥做碰撞检测，判定"看不见"直接把整个渲染器裁掉，粒子其实一直在模拟只是
    // 没画出来。这次两个盒子都要跟着地图尺寸放大。
    private const string VFXBoundsSizeProperty = "VFX Bounds Size";
    private const string VFXBoundsCenterProperty = "VFX Bounds Center Coordinates";

    // 素材自带一个"离摄像机太近就淡出"的效果（避免近距离FPS/TPS视角雨滴糊镜头）。
    // 真实属性名带着[FADED, VISIBLE]后缀，之前代码里漏了这截导致HasVector2一直是
    // false、这个"修复"根本没生效。另外数值含义也搞反过——(X,Y)=(淡出距离,完全可见
    // 距离)，即"超过Y才完全可见"，之前误当成"超过Y就淡出"，把Y改成100000反而变成
    // "要离镜头10万单位才可见"，比原装的(0.5,3)还离谱。这次改成(0, 0.01)：淡出距离
    // 定在几乎贴脸的0，可见距离定在0.01，等于不管多近多远都视为"已经过了可见距离"，
    // 相当于关掉这个只为近距离视角设计的效果。
    private const string RainCameraFadeDistanceProperty = "Rain Particle Camera Fade Distance [FADED, VISIBLE]";
    private const string RainParticleSizeProperty = "Rain Particle Size";

    // 跟ParticleSystem那套不一样，World Rain的Box没有烘焙旋转的坑（Character Follow
    // 那个变体才会跟摄像机走，World变体固定摆在世界里，局部轴就是世界轴）——直接把
    // 水平两个轴（X/Z）设成地图X/Z两边里较大的那个，垂直轴(Y，下落窗口厚度)保留素材
    // 原本调好的值不动。
    // 素材demo原装的生成盒子很小（20x20），密度（Spawn Rate）是照这个小面积调的。
    // 放大盒子盖住整张地图后，同样的Spawn Rate摊到大了30多倍的面积上，单位面积粒子
    // 密度骤降——稀疏的随机分布视觉上不是"均匀小雨"，而是东一簇西一簇的（用户反馈
    // "一簇一簇不随机"），本质是采样点太稀疏。跟ParticleSystem那套一样，按面积放大
    // 倍数同步把发射率也放大，保持单位面积粒子密度跟素材原装的一致。
    private const string RainSpawnRateProperty = "Rain Particle Spawn Rate";

    private const string RainLifetimeProperty = "Rain Particle Lifetime [MIN, MAX] [Random]";
    private const string RainVelocityMinProperty = "Rain Particle Velocity [MIN]";
    private const string RainVelocityMaxProperty = "Rain Particle Velocity [MAX]";

    private static void FitVFXBoxToMapBounds(VisualEffect vfx, Vector3 mapBoundsSize, float spawnHeightAboveGround)
    {
        float targetHorizontal = Mathf.Max(mapBoundsSize.x, mapBoundsSize.z);

        int spawnBoxId = Shader.PropertyToID(RainBoxSizeProperty);
        if (vfx.HasVector3(spawnBoxId))
        {
            Vector3 box = vfx.GetVector3(spawnBoxId);
            float oldArea = box.x * box.z;
            box.x = targetHorizontal;
            box.z = targetHorizontal;
            vfx.SetVector3(spawnBoxId, box);

            float newArea = box.x * box.z;
            int rateId = Shader.PropertyToID(RainSpawnRateProperty);
            if (oldArea > 0.001f && vfx.HasFloat(rateId))
            {
                float densityMultiplier = Mathf.Clamp(newArea / oldArea, 1f, 200f);
                vfx.SetFloat(rateId, vfx.GetFloat(rateId) * densityMultiplier);
            }
        }

        // 裁剪盒子留足够的水平/垂直余量（乘1.5是防止"水平刚好卡边缘被裁掉一半"），
        // 垂直方向留够 SkyHeightOffset(20) 上下的下落窗口，中心留在原点(0,0,0)——
        // 组件本身的Transform已经整体抬高到了SkyHeightOffset的位置，裁剪盒子只要
        // 相对自己中心留够上下余量即可，不需要额外偏移。
        int boundsSizeId = Shader.PropertyToID(VFXBoundsSizeProperty);
        if (vfx.HasVector3(boundsSizeId))
            vfx.SetVector3(boundsSizeId, new Vector3(targetHorizontal * 1.5f, 80f, targetHorizontal * 1.5f));

        int boundsCenterId = Shader.PropertyToID(VFXBoundsCenterProperty);
        if (vfx.HasVector3(boundsCenterId))
            vfx.SetVector3(boundsCenterId, Vector3.zero);

        int fadeDistId = Shader.PropertyToID(RainCameraFadeDistanceProperty);
        if (vfx.HasVector2(fadeDistId))
            vfx.SetVector2(fadeDistId, new Vector2(0f, 0.01f));

        // 素材默认 Rain Particle Size 是0.1，是照近距离FPS/TPS视角调的，正常游戏画面
        // 里的俯视角摄像机小到几乎看不清——用户实测把这个值改成1效果正好，直接定成
        // 常驻默认值，不用每次进游戏都手动调。
        int sizeId = Shader.PropertyToID(RainParticleSizeProperty);
        if (vfx.HasFloat(sizeId))
            vfx.SetFloat(sizeId, 1f);

        // 素材默认的速度(-8~-14)/生命周期(1~1.8s)是照它自己demo场景的生成高度调的，
        // 换到这张地图后生成高度对不上——下落距离(速度×生命周期)比实际到地面的高度
        // 大了不少，粒子还没"死"就已经穿过地板飞到看不见的地方了，看起来像没有落地
        // 效果（用户反馈"雨水穿过地板"）。这里反过来算：拿生成点到地面的实际高度差
        // 反推一套速度/生命周期，让粒子平均下落距离刚好等于这个高度，而不是照搬
        // demo场景写死的数值。
        float groundHeight = Mathf.Max(1f, spawnHeightAboveGround);
        const float lifetimeMin = 0.9f, lifetimeMax = 1.1f;
        float velocitySlow = -(groundHeight / lifetimeMax); // 生命周期长的那一档，速度要慢一点才不会提前落地太多
        float velocityFast = -(groundHeight / lifetimeMin); // 生命周期短的那一档，速度要快一点才能落到地

        int lifeId = Shader.PropertyToID(RainLifetimeProperty);
        if (vfx.HasVector2(lifeId))
            vfx.SetVector2(lifeId, new Vector2(lifetimeMin, lifetimeMax));

        int velMinId = Shader.PropertyToID(RainVelocityMinProperty);
        if (vfx.HasVector3(velMinId))
            vfx.SetVector3(velMinId, new Vector3(0f, velocitySlow, 0f));

        int velMaxId = Shader.PropertyToID(RainVelocityMaxProperty);
        if (vfx.HasVector3(velMaxId))
            vfx.SetVector3(velMaxId, new Vector3(0f, velocityFast, 0f));

        // 角色用SortingGroup+假Z轴算出来的sortingOrder做2.5D前后排序（见
        // UnitSpriteSortingController），VFX Graph走的是完全独立的一套排序，两边不会
        // 真正按深度互相比较——之前雨的排序值默认是0，永远比角色的SortingGroup小，
        // 所以雨永远在角色后面（用户反馈"雨永远在角色后面，跑不到前面来"）。真要按
        // 深度精确交错成本很高（要跟角色用同一套假Z换算），这里按前景大气层处理，
        // 固定摆在最前面——很多2.5D/俯视角游戏本来就是把雨雪当前景大气效果处理。
        // sortingOrder写死一个较大但没超过项目自己踩过坑的16位上限(32767)的安全值。
        var vfxRenderer = vfx.GetComponent<Renderer>();
        if (vfxRenderer != null)
            vfxRenderer.sortingOrder = 30000;
    }

    // 之前试过"哪根局部轴最大就按哪根轴的比例整体等比缩放"，翻车了两次：
    // 1) 素材的 Box 三个局部轴原始尺寸完全不成比例，等比缩放会把最小的那根轴顺带撑大
    //    几十倍，粒子密度（发射率/最大数量都是照原始体积调的，不会跟着放大）瞬间被
    //    稀释到几乎看不见。
    // 2) 更根本的问题：素材根节点带了烘焙好的旋转（比如 Falling Ash 系列是 -90°X），
    //    局部 XYZ 轴跟世界 XYZ 轴对不上——局部Z轴实际对应的是世界的"上下"，不是
    //    "地图深度"，瞎按局部轴套地图 X/Z 尺寸必然错。
    // 这次不再猜局部轴对应关系，而是拿这个粒子系统当前实际的旋转，把每根局部轴
    // 变换到世界空间，用它主要落在哪根世界轴上（X/Y/Z 里绝对值最大的分量）来判断
    // 用途：落在世界Y(垂直)上的那根局部轴是"下落窗口"，不去动它，保留素材原本调好
    // 的厚度；落在世界X/Z(水平)上的那两根局部轴，直接把 Box 尺寸设成地图对应边长，
    // 保证水平方向真正盖满地图四个角。同时按"水平覆盖面积放大了多少倍"同步放大
    // 发射率和最大粒子数，避免范围变大但粒子数量没跟上导致的稀疏感。
    private static void FitShapeToMapBounds(ParticleSystem ps, Vector3 mapBoundsSize)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        Vector3 baseScale = shape.scale;
        Transform t = ps.transform;

        float[] baseArr = { baseScale.x, baseScale.y, baseScale.z };
        float[] targetArr = { baseScale.x, baseScale.y, baseScale.z };
        Vector3[] localAxes = { Vector3.right, Vector3.up, Vector3.forward };

        float horizontalAreaBefore = 1f;
        float horizontalAreaAfter = 1f;
        bool anyHorizontalAxis = false;

        for (int i = 0; i < 3; i++)
        {
            Vector3 worldDir = t.TransformDirection(localAxes[i]);
            float absX = Mathf.Abs(worldDir.x);
            float absY = Mathf.Abs(worldDir.y);
            float absZ = Mathf.Abs(worldDir.z);

            if (absY >= absX && absY >= absZ)
                continue; // 这根局部轴对应世界垂直方向——下落窗口，保持原样不动

            // 不猜这根轴具体对应世界X还是Z——猜错了就会出现"某个方向盖住了、另一个
            // 方向没盖够"（实测出现过）。两根水平轴统一按地图X/Z两个边长里较大的那个
            // 放大，不管它俩实际对应哪个世界方向，都肯定够大，宁可稍微多盖出去一圈。
            float targetWorldSize = Mathf.Max(mapBoundsSize.x, mapBoundsSize.z);
            if (baseArr[i] > 0.001f && targetWorldSize > 0.001f)
            {
                targetArr[i] = targetWorldSize;
                horizontalAreaBefore *= baseArr[i];
                horizontalAreaAfter *= targetWorldSize;
                anyHorizontalAxis = true;
            }
        }

        shape.scale = new Vector3(targetArr[0], targetArr[1], targetArr[2]);

        if (!anyHorizontalAxis || horizontalAreaBefore <= 0.001f)
            return;

        // 覆盖面积放大了多少倍，发射率/最大粒子数就跟着放大多少倍，保持单位面积内
        // 的粒子密度跟素材原本设计的一致，不会因为盒子变大而看着变稀疏。
        float densityMultiplier = Mathf.Clamp(horizontalAreaAfter / horizontalAreaBefore, 1f, 200f);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTimeMultiplier *= densityMultiplier;

        ParticleSystem.MainModule main = ps.main;
        main.maxParticles = Mathf.Min(200000, Mathf.RoundToInt(main.maxParticles * densityMultiplier));
    }

    // 驱动 RaindropFX Pro（购买的镜头雨滴插件）实现"镜头湿润"效果。这个插件是 URP
    // Volume 框架的组件（VolumeComponent），不是挂在摄像机上的脚本——要让它生效，
    // 需要场景里有一个启用了对应 override 的 Volume。这里专门建一个只属于天气系统的
    // 全局 Volume，引用项目自己的 SkyPrisonRainLensWetnessProfile.asset（复制自插件
    // 官方示例 Profile，保留了贴图/参数等美术已经调好的部分，只把 enable/distortion
    // 交给这里动态控制），不跟地图本身的 postProcessProfile 混在一起，避免互相干扰。
    private void ApplyLensWetness(float intensity)
    {
        VolumeProfile profile = Resources.Load<VolumeProfile>(RainLensProfileResourceName);
        if (profile == null)
        {
            Debug.LogWarning($"[MapWeatherController] Resources 里找不到 {RainLensProfileResourceName}，镜头湿润效果不会生效。", this);
            return;
        }

        if (!profile.TryGet(out RaindropFX_GPU raindrop))
        {
            Debug.LogWarning("[MapWeatherController] SkyPrisonRainLensWetnessProfile 里没有 RaindropFX_GPU 组件，镜头湿润效果不会生效。", this);
            return;
        }

        raindrop.enable.overrideState = true;
        raindrop.distortion.overrideState = true;

        GameObject volumeGo = new GameObject("SkyPrisonRainLensVolume");
        volumeGo.transform.SetParent(transform, false);
        Volume volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.weight = 1f;
        volume.priority = 0f;
        volume.profile = profile;

        // 没有雨的地图要立刻显式关掉（不用等，本来就该是0，跟之前"跨地图残留"那个
        // 坑的修复逻辑一致）。有雨的地图则是本次要修的——之前进场景立刻把扭曲拉到
        // 目标强度，跟读条动画完全没错开，用户反馈"进入地图屏幕有很长的拖尾"，
        // 就是读条一消失就已经是满强度扭曲的画面。改成跟BGS环境音同一套节奏：
        // 先等一小段延迟让读条动画盖住，再慢慢淡入到目标强度。
        if (intensity <= 0.001f)
        {
            raindrop.enable.value = false;
            raindrop.distortion.value = 0f;
            return;
        }

        raindrop.enable.value = true;
        raindrop.distortion.value = 0f;
        StartCoroutine(FadeLensDistortion(raindrop, Mathf.Clamp01(intensity) * MaxLensDistortion));
    }

    private System.Collections.IEnumerator FadeLensDistortion(RaindropFX_GPU raindrop, float target)
    {
        yield return new WaitForSeconds(AmbientStartDelay);

        float t = 0f;
        const float duration = 1.5f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (raindrop == null) yield break;
            raindrop.distortion.value = Mathf.Lerp(0f, target, t / duration);
            yield return null;
        }
        if (raindrop != null) raindrop.distortion.value = target;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
