using System.Collections;
using UnityEngine;

/// <summary>
/// 场景物（箱子这类"物品/场景物"分类、血量1、一击即碎）的破坏效果。
///
/// 完全不碰原模型/原材质——不知道具体是什么贴图，硬套自定义着色器风险大，
/// 万一跟原贴图对不上就是新问题。改成"瞬间隐藏 + 现造一批碎块带物理冲量炸开"，
/// 碎块是运行时生成的薄片网格（三角形/不规则四边形挤出一点厚度），不依赖任何
/// 美术资源，Blender 全程不用碰。
///
/// 挂在跟 UnitDeathController 同一个物体上，订阅它的 OnDeathStarted——跟其它
/// Unit（敌人/玩家）走同一套死亡事件，不另起一套触发机制。
/// </summary>
[RequireComponent(typeof(UnitDeathController))]
public sealed class SkyPrisonBreakableCrateEffect : MonoBehaviour
{
    [Header("碎块")]
    [SerializeField, Min(1)] private int debrisCount = 8;
    [Tooltip("碎块薄片的平面半径范围（不是立方体边长）——决定薄片有多大一片。")]
    [SerializeField] private float debrisMinRadius = 0.08f;
    [SerializeField] private float debrisMaxRadius = 0.48f;
    [Tooltip("薄片厚度相对平面半径的比例——木片该是薄的，不是方块，比例太大就变回牛肉粒。")]
    [SerializeField] private Vector2 thicknessRatioRange = new Vector2(0.2f, 0.38f);
    [Range(0f, 1f)]
    [Tooltip("每块碎片是三角形的概率，剩下的是不规则四边形。")]
    [SerializeField] private float triangleChance = 0.5f;

    [Header("飞散初速度")]
    [Tooltip("沿箱子中心向外的初速度区间。之前额外叠加了一份 AddExplosionForce 冲量，两份速度叠在一起才会飞得离谱——现在只保留这一份，直接、可控。")]
    [SerializeField] private Vector2 radialLaunchSpeedRange = new Vector2(1.2f, 2.4f);
    [SerializeField] private float upwardLaunchBoost = 0.6f;
    [SerializeField] private float spinTorque = 1.5f;

    [Header("烟尘特效")]
    [Tooltip("碎裂瞬间在箱子中心生成一份，播完自动销毁（跟着粒子系统自己的 duration 走，不用另外配时长）。留空则不生成。")]
    [SerializeField] private GameObject smokeEffectPrefab;
    [Tooltip("兜底销毁延迟——粒子系统 duration + 最长存活粒子的 lifetime 都算完之后留的余量，避免最后一批粒子还没消失特效就被砍掉。")]
    [SerializeField] private float smokeEffectDestroyDelay = 3f;
    [Tooltip("粒子 startSize 的放大倍数——素材原始尺寸是照近距离游戏调的（0.6 世界单位），这个项目相机常态离物体 ~200 单位远、FOV 只有 30°，原始尺寸投到屏幕上只有十几像素，肉眼看不清。粒子是 World 模拟空间，缩放这个物体的 Transform 不会影响粒子大小，只能改粒子系统自己的 startSize。")]
    [SerializeField] private float smokeSizeMultiplier = 21.6f;

    [Header("清理")]
    [SerializeField] private float debrisLifetime = 4f;
    [SerializeField] private float destroyOriginalDelay = 0.05f; // 留一点余量，确保碎块生成完再销毁原物体

    // 碎块共用同一个材质实例，不用每块单独 new 一个——省内存也省 SetPass Call。
    private static Material _sharedDebrisMaterial;

    private UnitDeathController _deathController;

    private void Awake()
    {
        _deathController = GetComponent<UnitDeathController>();
    }

    private void OnEnable()
    {
        if (_deathController != null)
            _deathController.OnDeathStarted += HandleDeathStarted;
    }

    private void OnDisable()
    {
        if (_deathController != null)
            _deathController.OnDeathStarted -= HandleDeathStarted;
    }

    private void HandleDeathStarted(UnitDeathController controller)
    {
        // UnitDeathController.Kill() 是先 Invoke(OnDeathStarted) 再往下走到
        // disableCollidersOnDeath 那一段——这个回调在同一帧、同步执行，原箱子身上的
        // 实体碰撞体（SolidBlocker）和受击框（CombatHurtbox）这时候还没被禁用。碎块
        // 就诞生在箱子中心，正好被自己一个凹网格实体碰撞体整个包住，物理引擎对"从
        // 一开始就整体嵌在凹网格里"的动态刚体不会算出干净的顶出方向，表现为"炸不开、
        // 挤成一团、只看得到一块"。在生成碎块之前，先把原箱子自己的碰撞体全部关掉，
        // 碎块就不会跟正要被销毁的原物体抢位置。
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            c.enabled = false;

        // 2026-08-19：原箱子销毁排在最前面——之前排在碎块/烟雾特效后面，一次烟雾预制体
        // 引用配错直接抛异常，把这句还没执行到的销毁协程一起带没了，表现成"碎块炸出来
        // 了，完好的箱子却还留在原地"。特效这类"锦上添花"的步骤出错不该连累"必须发生"
        // 的清理逻辑，销毁调用要第一个跑，出问题最多是没特效，不会崩成箱子鬼影。
        StartCoroutine(DestroyOriginalAfterDelay());
        SpawnDebris();
        SpawnSmokeEffect();
    }

    /// <summary>
    /// 手动测试用——不用真的打这个物体，进 Play 之后在 Inspector 里右键这个组件、
    /// 选这一项直接触发碎裂效果。这不是可预览的关键帧动画，是代码驱动的运行时效果，
    /// 只有真正执行到这一步才看得到，没有能在编辑时间轴里拖动查看的地方。
    /// </summary>
    [ContextMenu("测试：立即触发碎裂效果（需要在 Play 模式下）")]
    private void DebugTriggerBreakEffect()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SkyPrisonBreakableCrateEffect] 碎块用了 Rigidbody 物理，" +
                              "必须在 Play 模式下测试才有效果。");
            return;
        }

        HandleDeathStarted(_deathController);
    }

    private IEnumerator DestroyOriginalAfterDelay()
    {
        // 先把原模型的渲染关掉（碰撞/移动这些 UnitDeathController.Kill() 已经处理），
        // 让碎块生成的这一帧不会跟原模型的渲染叠在一起穿帮。
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        yield return new WaitForSeconds(destroyOriginalDelay);
        Destroy(gameObject);
    }

    private void SpawnDebris()
    {
        Vector3 center = transform.position;
        Material mat = GetOrCreateDebrisMaterial();

        // 用实际渲染包围盒撑开碎块的散布范围——固定用一个很小的球半径的话，不管箱子
        // 多大，碎块永远从几乎同一个点冒出来，紧贴在一起，看起来就像"只有一块"。
        // 取不到渲染包围盒（比如渲染器已经被禁用）时退回一个不至于太小的默认值。
        float spawnRadius = 0.35f;
        Bounds? visualBounds = TryGetVisualBounds();
        if (visualBounds.HasValue)
            spawnRadius = Mathf.Max(0.15f, Mathf.Max(visualBounds.Value.extents.x, visualBounds.Value.extents.z) * 0.6f);

        // 2026-08-19：碎块之前直接放在 World3D 层，掉到地面直接穿了过去——查了物理层
        // 碰撞矩阵，World3D × World3D 是 ignore=true（同层互相无视，其余层基本也是
        // ignore=true，只有 Character2D 例外），这层显然是刻意配置成"静态场景之间不
        // 互相产生物理碰撞、只用来挡玩家"，不是漏配。碎块是要落地的动态刚体，不该跟
        // 静态场景共用这层语义。改用专门开的 Debris 层（Layer 6，之前是空位，见
        // ProjectSettings/TagManager.asset），并显式打开 Debris × World3D 的碰撞——
        // 新层默认矩阵状态未必可靠，不赌默认值，直接在这里写死一次。
        int debrisLayer = LayerMask.NameToLayer("Debris");
        int world3DLayer = LayerMask.NameToLayer("World3D");
        if (debrisLayer >= 0 && world3DLayer >= 0)
            Physics.IgnoreLayerCollision(debrisLayer, world3DLayer, false);

        var chips = new GameObject[debrisCount];

        for (int i = 0; i < debrisCount; i++)
        {
            float radius = Random.Range(debrisMinRadius, debrisMaxRadius);
            float thickness = radius * Random.Range(thicknessRatioRange.x, thicknessRatioRange.y);
            bool triangle = Random.value < triangleChance;

            GameObject chip = new GameObject("CrateDebris");
            // 渲染管线的相机剔除遮罩不含 Default，不挂到有效渲染层的话碎块炸开也是看不见
            // 的（跟 Visual 子节点那个问题同源）。优先用专门的 Debris 层，找不到才退回
            // World3D（至少能看见，落地问题另说）。
            chip.layer = debrisLayer >= 0 ? debrisLayer : world3DLayer;

            Vector3 offset = Random.insideUnitSphere * spawnRadius;
            chip.transform.SetPositionAndRotation(center + offset, Random.rotationUniform);

            Mesh mesh = BuildShardMesh(triangle, radius, thickness);
            MeshFilter filter = chip.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = chip.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            MeshCollider col = chip.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = true;
            col.isTrigger = false;

            Rigidbody rb = chip.AddComponent<Rigidbody>();

            // 直接给一个沿"爆心→碎块"方向的初速度——只用这一份，不再叠加
            // AddExplosionForce（两份速度叠在一起是之前"飞得太狠"的直接原因）。
            Vector3 radialDir = offset.sqrMagnitude > 0.0001f ? offset.normalized : Random.onUnitSphere;
            float launchSpeed = Random.Range(radialLaunchSpeedRange.x, radialLaunchSpeedRange.y);
            rb.linearVelocity = radialDir * launchSpeed + Vector3.up * upwardLaunchBoost;
            rb.AddTorque(Random.insideUnitSphere * spinTorque, ForceMode.Impulse);

            chips[i] = chip;
            Destroy(chip, debrisLifetime);
        }

        // 碎块之间互相忽略碰撞——好几块同时从差不多同一个中心区域炸开，靠得太近时会
        // 互相顶撞、抵消掉一部分向外的速度，看起来又像"聚成一团"。它们本来就该各飞
        // 各的，不需要真实的相互碰撞。
        for (int i = 0; i < chips.Length; i++)
        {
            Collider a = chips[i] != null ? chips[i].GetComponent<Collider>() : null;
            if (a == null) continue;
            for (int j = i + 1; j < chips.Length; j++)
            {
                Collider b = chips[j] != null ? chips[j].GetComponent<Collider>() : null;
                if (b != null)
                    Physics.IgnoreCollision(a, b, true);
            }
        }
    }

    private void SpawnSmokeEffect()
    {
        if (smokeEffectPrefab == null)
            return;

        GameObject fx = Instantiate(smokeEffectPrefab, transform.position, Quaternion.identity);

        // 素材包里的粒子系统原始层是 Default——Main Camera 的 CullingMask 只勾了
        // World3D/Character2D/Debris 这几层（跟碎块穿模那次同一个根因），Default 不在
        // 里面，生成了也看不见。递归改到 World3D，跟场景里其它看得见的 3D 物体一致。
        int world3DLayer = LayerMask.NameToLayer("World3D");
        if (world3DLayer >= 0)
            SetLayerRecursive(fx.transform, world3DLayer);

        ScaleParticleStartSize(fx, smokeSizeMultiplier);

        Destroy(fx, smokeEffectDestroyDelay);
    }

    /// <summary>粒子是 World 模拟空间，缩放这个物体的 Transform 对粒子大小没有任何
    /// 影响，必须直接改每个 ParticleSystem 自己的 startSize（Constant/TwoConstants
    /// 两种模式都要处理，素材里两条粒子系统用的是 TwoConstants）。</summary>
    private static void ScaleParticleStartSize(GameObject fx, float multiplier)
    {
        if (multiplier == 1f)
            return;

        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            var startSize = main.startSize;
            switch (startSize.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    startSize.constant *= multiplier;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    startSize.constantMin *= multiplier;
                    startSize.constantMax *= multiplier;
                    break;
                case ParticleSystemCurveMode.Curve:
                    startSize.curveMultiplier *= multiplier;
                    break;
                case ParticleSystemCurveMode.TwoCurves:
                    startSize.curveMultiplier *= multiplier;
                    break;
            }
            main.startSize = startSize;
        }
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
            SetLayerRecursive(child, layer);
    }

    /// <summary>
    /// 造一片挤出过厚度的多边形薄片——三角形(3点)或不规则四边形(4点)。
    /// 点先按角度均匀分布再各自加一点角度/半径抖动，保证连起来是简单多边形（不自交），
    /// 同时形状不规则，不是正三角形/正方形那种一眼假的对称图形。
    /// </summary>
    private static Mesh BuildShardMesh(bool triangle, float radius, float thickness)
    {
        int n = triangle ? 3 : 4;
        float half = thickness * 0.5f;

        Vector2[] points2D = new Vector2[n];
        float sector = 2f * Mathf.PI / n;
        for (int i = 0; i < n; i++)
        {
            float angle = i * sector + Random.Range(-sector * 0.25f, sector * 0.25f);
            float r = radius * Random.Range(0.55f, 1f);
            points2D[i] = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        var vertices = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(points2D[i].x, points2D[i].y, half);       // front ring
            vertices[n + i] = new Vector3(points2D[i].x, points2D[i].y, -half);  // back ring
        }

        int frontCapTris = (n - 2) * 3;
        int backCapTris = (n - 2) * 3;
        int sideTris = n * 6;
        var triangles = new int[frontCapTris + backCapTris + sideTris];
        int t = 0;

        // 前面（+Z，朝外的一面），扇形三角剖分
        for (int i = 1; i < n - 1; i++)
        {
            triangles[t++] = 0;
            triangles[t++] = i;
            triangles[t++] = i + 1;
        }

        // 背面（-Z），绕序反过来
        for (int i = 1; i < n - 1; i++)
        {
            triangles[t++] = n;
            triangles[t++] = n + i + 1;
            triangles[t++] = n + i;
        }

        // 侧面：每条边一个四边形（两个三角形），front[i]-front[j]-back[j]-back[i]
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int fi = i, fj = j, bi = n + i, bj = n + j;

            triangles[t++] = fi;
            triangles[t++] = fj;
            triangles[t++] = bj;

            triangles[t++] = fi;
            triangles[t++] = bj;
            triangles[t++] = bi;
        }

        var mesh = new Mesh { name = triangle ? "CrateDebris_Tri" : "CrateDebris_Quad" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Bounds? TryGetVisualBounds()
    {
        bool has = false;
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            if (!has)
            {
                b = r.bounds;
                has = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }
        return has ? b : (Bounds?)null;
    }

    private static Material GetOrCreateDebrisMaterial()
    {
        if (_sharedDebrisMaterial != null)
            return _sharedDebrisMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");

        _sharedDebrisMaterial = new Material(shader) { name = "M_SkyPrisonCrateDebris_Runtime" };
        if (_sharedDebrisMaterial.HasProperty("_BaseColor"))
            _sharedDebrisMaterial.SetColor("_BaseColor", new Color(0.42f, 0.30f, 0.18f, 1f));
        if (_sharedDebrisMaterial.HasProperty("_Smoothness"))
            _sharedDebrisMaterial.SetFloat("_Smoothness", 0.1f); // 木头不该反光锃亮

        return _sharedDebrisMaterial;
    }
}
