using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 夜视仪战术识别层：AXIA 作为机器人的"扫描识别"表现。
///
/// 两档：
/// - 扫描框：识别范围内的单位（最多 MaxTargets 个，按距离取近的）各套一个角括号 [ ]，
///   进入时从大缩到贴合（约 0.25 秒），文字在 TextHoldSeconds 后淡出，只留括号。
/// - 锁定环：离鼠标最近的那一个（LockRadiusPixels 内）额外显示分段圆环 + 名字 + 阵营 + 距离，
///   文字常亮。
///
/// 只按阵营分类（HOSTILE / NEUTRAL / FRIENDLY），精英 / BOSS 在 HOSTILE 后面追加威胁字样。
/// 英文名取 UnitDefinition.localizedNames 里 en 开头的条目，没有就退回 unitId。
///
/// 定位方式照抄头顶战斗 HUD：每个目标一张世界空间画布，放在单位身上（OverheadUI 层，
/// 由栈里的 OverheadUICamera 画），和角色走同一条渲染路径，不做任何屏幕坐标换算。
/// 之前用 ScreenSpaceOverlay + 投影算屏幕坐标的版本，单位一动框就脱开，换了几种投影
/// 和时机都对不齐，见 2026-10-03 的排查。
/// 画布位置 / 朝向在 beginCameraRendering 里写（相机此时才被 Cinemachine 摆到最终位置），
/// 内容（尺寸、颜色、文字）在 LateUpdate 里更新。
///
/// 世界空间画布会和画面一起进后处理，被夜视仪的绿色滤镜和曝光再染一遍；线条用的
/// SkyPrison/UI/Additive 着色器乘了 SkyPrisonNightVisionMode 写的补偿向量来抵消。
/// 只展示、不参与任何判定；和 AI 的视线无关。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonNightVisionHud : MonoBehaviour
{
    private const int MaxTargets = 6;
    private const float ScanRadius = 28f;
    private const float RescanInterval = 0.4f;
    private const float ScanInSeconds = 0.25f;
    private const float TextHoldSeconds = 2f;
    private const float TextFadeSeconds = 0.4f;
    private const float LockMarginMeters = 0.6f;

    // 世界空间画布：1 个画布单位 = 1 厘米。
    private const float CanvasUnitToMeter = 0.01f;
    private const float BoxWidthToHeight = 0.45f;
    private const float BoxPadMeters = 0.15f;
    private const float TagFontSize = 20f;
    private const float DetailFontSize = 22f;

    private static readonly Color HostileColor = new Color(1f, 0.33f, 0.18f, 1f);
    private static readonly Color NeutralColor = new Color(0.92f, 0.96f, 1f, 1f);
    private static readonly Color FriendlyColor = new Color(0.30f, 0.92f, 1f, 1f);

    private enum Relation { Hostile, Neutral, Friendly }

    private class Entry
    {
        public UnitDefinitionRuntimeBinder binder;
        public Renderer[] bodyRenderers;
        public RectTransform root;       // 世界空间画布
        public CanvasGroup group;
        public RectTransform box;        // 括号框（扫描缩放作用在这里）
        public BracketGraphic brackets;
        public RingGraphic ring;
        public TextMeshProUGUI tagText;
        public TextMeshProUGUI detailText;
        public float firstSeen;
        public float lockAlpha;
        public float centerHeight;       // 退化路径用：框中心离单位根节点的高度（米）
        public MeshRenderer planeRenderer; // 角色本体网格（Spine）：框贴在它的平面上
        public MeshFilter planeFilter;
        public Vector2 sizeMeters;
        public Relation relation;
        public string englishName;
        public string threatSuffix;
    }

    private Canvas _screenCanvas;
    private CanvasGroup _screenGroup;
    private TextMeshProUGUI _modeText;
    private readonly List<Entry> _active = new List<Entry>();
    private readonly List<Entry> _pool = new List<Entry>();
    private readonly List<UnitDefinitionRuntimeBinder> _candidates = new List<UnitDefinitionRuntimeBinder>();
    private UnitDefinitionRuntimeBinder _player;
    private float _nextRescan;
    private float _lastSeTime;
    private float _weight;
    private float _nextLockDiag;
    private int _overheadLayer = -1;

    private void Awake()
    {
        _overheadLayer = LayerMask.NameToLayer("OverheadUI");
        BuildScreenCanvas();
    }

    private void OnEnable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
    }

    private void OnDisable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
    }

    private void OnDestroy()
    {
        if (_screenCanvas != null)
            Destroy(_screenCanvas.gameObject);
        foreach (Entry e in _active)
            if (e.root != null) Destroy(e.root.gameObject);
        foreach (Entry e in _pool)
            if (e.root != null) Destroy(e.root.gameObject);
    }

    private void LateUpdate()
    {
        _weight = SkyPrisonNightVisionMode.Weight;
        bool show = _weight > 0.01f;
        if (_screenCanvas != null && _screenCanvas.gameObject.activeSelf != show)
            _screenCanvas.gameObject.SetActive(show);
        if (!show)
        {
            ReleaseAll();
            return;
        }

        _screenGroup.alpha = _weight;

        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + RescanInterval;
            RescanCandidates();
        }

        UpdateTargets();
    }

    /// <summary>画布的世界位置 / 朝向只在这里写：此时 Cinemachine 已经把相机摆到这一帧的最终位置。
    /// 世界空间画布整体移动不需要重建网格，渲染时直接用当下的 Transform。</summary>
    private void HandleBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera renderingCam)
    {
        Camera main = Camera.main;
        if (main == null || renderingCam != main || _active.Count == 0)
            return;

        for (int i = 0; i < _active.Count; i++)
        {
            Entry e = _active[i];
            if (e.binder == null)
                continue;
            if (ResolvePlane(e, main, out Vector3 center, out Quaternion rot, out _))
                e.root.SetPositionAndRotation(center, rot);
        }
    }

    private void RescanCandidates()
    {
        _candidates.Clear();
        _player = null;
        var all = FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsSortMode.None);
        int dNoUd = 0, dNotChar = 0, dDisabled = 0;   // TEMP-DIAG-NVSCAN
        for (int i = 0; i < all.Length; i++)
        {
            UnitDefinitionRuntimeBinder b = all[i];
            if (b == null || b.UnitDefinitionAsset == null)
            {
                dNoUd++;
                continue;
            }
            UnitDefinition ud = b.UnitDefinitionAsset;
            if (ud.defineType != UnitDefineType.Character)
            {
                dNotChar++;
                continue;
            }
            // 玩家只当"原点"用，不要求绑定器处于启用状态：实测玩家身上的 Binder/Applier 可能是禁用的，
            // 以前这里一起被跳过，_player 为空，原点退回相机位置（离单位几十米），范围内一个目标都没有，整个识别层不显示。
            if (ud.characterIdentity == CharacterIdentity.Player)
            {
                _player = b;
                continue;
            }
            // 只看物体是否激活，不看 Binder 组件本身启没启用：织面者等单位的 Binder 组件是禁用的（实测日志 disabled=2），
            // 以前这里一起被跳过，识别层对它们一个都不出。
            if (!b.gameObject.activeInHierarchy)
            {
                dDisabled++;
                continue;
            }
            _candidates.Add(b);
        }

        // TEMP-DIAG-NVSCAN：排查"TRACKING 00 但画面里明明有敌人"，确认后删除。
        Debug.Log($"[DIAG-NVSCAN] binders={all.Length} noUd={dNoUd} notChar={dNotChar} disabled={dDisabled} candidates={_candidates.Count} player={(_player != null)}");
    }

    private void UpdateTargets()
    {
        Camera cam = Camera.main;
        Vector3 origin;
        if (_player != null)
            origin = _player.transform.position;
        else if (SkyPrisonPlayerAuthority.CurrentPlayerUnit != null)
            origin = SkyPrisonPlayerAuthority.CurrentPlayerUnit.transform.position;
        else
            origin = cam != null ? cam.transform.position : Vector3.zero;

        // 取范围内最近的 MaxTargets 个。
        var picked = new List<(UnitDefinitionRuntimeBinder b, float dist)>();
        for (int i = 0; i < _candidates.Count; i++)
        {
            UnitDefinitionRuntimeBinder b = _candidates[i];
            if (b == null || !b.gameObject.activeInHierarchy)
                continue;
            float d = Vector3.Distance(origin, b.transform.position);
            if (d > ScanRadius)
                continue;
            picked.Add((b, d));
        }
        picked.Sort((a, c) => a.dist.CompareTo(c.dist));
        if (picked.Count > MaxTargets)
            picked.RemoveRange(MaxTargets, picked.Count - MaxTargets);

        // 回收不在名单里的。
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Entry e = _active[i];
            bool still = false;
            for (int j = 0; j < picked.Count; j++)
                if (picked[j].b == e.binder) { still = true; break; }
            if (!still)
            {
                Release(e);
                _active.RemoveAt(i);
            }
        }

        float now = Time.unscaledTime;
        UnitDefinitionRuntimeBinder lockTarget = null;
        float best = float.MaxValue;
        Vector2 mouse = Input.mousePosition;
        Ray mouseRay = cam != null ? MouseRayThroughLensDistortion(cam, mouse) : default;

        for (int i = 0; i < picked.Count; i++)
        {
            UnitDefinitionRuntimeBinder b = picked[i].b;
            Entry e = _active.Find(x => x.binder == b);
            if (e == null)
            {
                e = Acquire(b);
                e.firstSeen = now;
                _active.Add(e);
                if (e.relation == Relation.Hostile && now - _lastSeTime > 0.6f)
                {
                    _lastSeTime = now;
                    // 敌对单位进入视线：用素材 Notification Alert Enemy Killed 2(没有素材就退回系统切换音)
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.NightVisionTarget);
                }
            }

            if (cam == null || !ResolvePlane(e, cam, out Vector3 center, out Quaternion rot, out Vector2 sizeMeters))
            {
                e.root.gameObject.SetActive(false);
                continue;
            }
            e.root.gameObject.SetActive(true);
            e.root.SetPositionAndRotation(center, rot);
            e.sizeMeters = sizeMeters;

            // 锁定：鼠标射线和框所在平面求交，落在框内（四周放宽一圈）就算指着它。
            // 不走"世界点投影到屏幕再比像素距离"，和框本身用的是同一套几何。
            if (TryHitBox(e, mouseRay, out float score) && score < best)
            {
                best = score;
                lockTarget = b;
            }

            LayoutEntry(e, sizeMeters.x, sizeMeters.y, now, picked[i].dist);
        }

        // TEMP-DIAG-NVLOCK：排查"鼠标放在单位上锁定环不出现"，确认后删除。
        if (Time.unscaledTime >= _nextLockDiag && _active.Count > 0 && cam != null)
        {
            _nextLockDiag = Time.unscaledTime + 1f;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[DIAG-NVLOCK] mouse={mouse} screen={Screen.width}x{Screen.height} camPix={cam.pixelRect} lock={(lockTarget != null ? lockTarget.name : "none")}");
            foreach (Entry de in _active)
            {
                if (de.binder == null) continue;
                Plane pl = new Plane(de.root.forward, de.root.position);
                string local = pl.Raycast(mouseRay, out float enter) ? de.root.InverseTransformPoint(mouseRay.GetPoint(enter)).ToString("F0") : "noHit";
                sb.Append($" | {de.binder.name} centerScreen={cam.WorldToScreenPoint(de.root.position):F0} local={local} size={(de.sizeMeters / CanvasUnitToMeter):F0}");
            }
            Debug.Log(sb.ToString());
        }

        for (int i = 0; i < _active.Count; i++)
        {
            Entry e = _active[i];
            bool isLock = e.binder == lockTarget;
            e.lockAlpha = Mathf.MoveTowards(e.lockAlpha, isLock ? 1f : 0f, 8f * Time.unscaledDeltaTime);
            ApplyLockVisuals(e);
        }

        if (_modeText != null)
            _modeText.text = "OPTICS: NIGHT VISION  //  TRACKING " + _active.Count.ToString("00");
    }

    private void LayoutEntry(Entry e, float widthMeters, float heightMeters, float now, float dist)
    {
        float s = Mathf.Clamp01((now - e.firstSeen) / ScanInSeconds);
        float ease = 1f - (1f - s) * (1f - s);
        float grow = Mathf.Lerp(1.8f, 1f, ease);

        Vector2 size = new Vector2(widthMeters, heightMeters) / CanvasUnitToMeter;
        e.root.sizeDelta = size;
        e.box.sizeDelta = size * grow;
        e.group.alpha = ease * _weight;

        float since = now - e.firstSeen - TextHoldSeconds;
        float textAlpha = since <= 0f ? 1f : Mathf.Clamp01(1f - since / TextFadeSeconds);
        textAlpha = Mathf.Max(textAlpha, e.lockAlpha);

        Color c = ColorOf(e.relation);
        e.brackets.color = c;
        e.tagText.color = new Color(c.r, c.g, c.b, textAlpha);
        e.tagText.text = TagOf(e);

        float ringSize = Mathf.Max(size.x, size.y) * 1.25f;
        e.ring.rectTransform.sizeDelta = new Vector2(ringSize, ringSize);
        e.ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -now * 35f);
        e.detailText.rectTransform.anchoredPosition = new Vector2(ringSize * 0.5f + 16f, 0f);
        e.detailText.text = e.englishName + "\n" + StatusOf(e) + "\nRNG " + dist.ToString("0.0") + "M";
    }

    private void ApplyLockVisuals(Entry e)
    {
        Color c = ColorOf(e.relation);
        bool on = e.lockAlpha > 0.01f;
        e.ring.gameObject.SetActive(on);
        e.detailText.gameObject.SetActive(on);
        e.ring.color = new Color(c.r, c.g, c.b, e.lockAlpha);
        e.detailText.color = new Color(c.r, c.g, c.b, e.lockAlpha);
    }

    /// <summary>
    /// 框贴在角色本体网格（Spine 的 MeshRenderer）自己的平面上：中心、朝向、宽高都取这张网格的
    /// 本地包围盒，和角色共面，任何相机 / 投影下都重合。
    /// 之前框朝着相机、高度取世界包围盒，和角色不在同一个平面上，投影出来偏下、偏短。
    /// 没有网格的单位（纯 Sprite 等）退回：世界包围盒竖直范围 + 朝向相机。
    /// </summary>
    private static bool ResolvePlane(Entry e, Camera cam, out Vector3 center, out Quaternion rot, out Vector2 sizeMeters)
    {
        center = default;
        rot = cam.transform.rotation;
        sizeMeters = default;

        MeshRenderer mr = e.planeRenderer;
        Mesh mesh = e.planeFilter != null ? e.planeFilter.sharedMesh : null;
        if (mr != null && mr.enabled && mr.gameObject.activeInHierarchy && mesh != null && mesh.vertexCount > 0)
        {
            Transform t = mr.transform;
            Bounds lb = mesh.bounds;
            Vector3 lossy = t.lossyScale;
            float h = Mathf.Abs(lb.size.y * lossy.y);
            float w = Mathf.Abs(lb.size.x * lossy.x);
            if (h > 0.05f)
            {
                center = t.TransformPoint(lb.center);
                rot = t.rotation;
                // 画布正面要朝相机：网格节点如果是背对相机摆的，转 180° 再用，不然字是反的。
                if (Vector3.Dot(rot * Vector3.forward, cam.transform.forward) < 0f)
                    rot *= Quaternion.Euler(0f, 180f, 0f);
                h += BoxPadMeters * 2f;
                w = Mathf.Min(w + BoxPadMeters * 2f, h * BoxWidthToHeight * 1.3f);
                sizeMeters = new Vector2(Mathf.Max(w, h * BoxWidthToHeight * 0.8f), h);
                return true;
            }
        }

        // 退化路径
        float minY = float.MaxValue, maxY = float.MinValue;
        bool has = false;
        Renderer[] rs = e.bodyRenderers;
        if (rs != null)
        {
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                Bounds bd = r.bounds;
                minY = Mathf.Min(minY, bd.min.y);
                maxY = Mathf.Max(maxY, bd.max.y);
                has = true;
            }
        }
        if (!has)
            return false;

        float height = Mathf.Max(0.5f, maxY - minY) + BoxPadMeters * 2f;
        Vector3 root = e.binder.transform.position;
        center = new Vector3(root.x, (minY + maxY) * 0.5f, root.z);
        sizeMeters = new Vector2(height * BoxWidthToHeight, height);
        return true;
    }

    /// <summary>
    /// 鼠标射线要先"反算"掉镜头畸变：DefaultVolumeProfile 里的 Lens Distortion（scale 1.62、intensity -0.08）
    /// 会把整幅 3D 画面绕中心放大并做桶形畸变，世界里的东西（角色、头顶 HUD、这里的识别框）都跟着画面走，
    /// 但鼠标坐标没有被畸变。直接用 ScreenPointToRay 打出去，离屏幕中心越远偏得越多——
    /// 表现为"鼠标明明在框里却锁定不了"，最早屏幕层的框偏到角色和中心之间也是这个原因。
    /// 做法：屏幕上看到的位置 = 输出 uv；后处理在输出 uv 处采样的是 DistortUV(输出 uv) 那个位置的原画面，
    /// 所以把鼠标 uv 过一遍和 URP UberPost 完全相同的 DistortUV，就得到它在原始 3D 画面里指着的点。
    /// </summary>
    private static Ray MouseRayThroughLensDistortion(Camera cam, Vector2 mouse)
    {
        Vector2 uv = new Vector2(mouse.x / Mathf.Max(1, Screen.width), mouse.y / Mathf.Max(1, Screen.height));
        var stack = UnityEngine.Rendering.VolumeManager.instance != null ? UnityEngine.Rendering.VolumeManager.instance.stack : null;
        var ld = stack != null ? stack.GetComponent<UnityEngine.Rendering.Universal.LensDistortion>() : null;
        if (ld != null && ld.IsActive())
            uv = DistortUV(uv, ld);
        return cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
    }

    /// <summary>照抄 URP UberPost 的 DistortUV（参数换算同 PostProcessPass.SetupLensDistortion）。</summary>
    private static Vector2 DistortUV(Vector2 uv, UnityEngine.Rendering.Universal.LensDistortion ld)
    {
        float intensity = ld.intensity.value;
        float amount = 1.6f * Mathf.Max(Mathf.Abs(intensity * 100f), 1f);
        float theta = Mathf.Deg2Rad * Mathf.Min(160f, amount);
        float sigma = 2f * Mathf.Tan(theta * 0.5f);
        Vector2 center = ld.center.value * 2f - Vector2.one;
        Vector2 axis = new Vector2(Mathf.Max(ld.xMultiplier.value, 1e-4f), Mathf.Max(ld.yMultiplier.value, 1e-4f));
        float distTheta = intensity >= 0f ? theta : 1f / theta;
        float distScale = 1f / ld.scale.value;

        uv = (uv - new Vector2(0.5f, 0.5f)) * distScale + new Vector2(0.5f, 0.5f);
        Vector2 ruv = Vector2.Scale(axis, uv - new Vector2(0.5f, 0.5f) - center);
        float ru = ruv.magnitude;
        if (ru < 1e-6f)
            return uv;
        if (intensity * 100f > 0f)
        {
            float wu = ru * distTheta;
            ru = Mathf.Tan(wu) * (1f / (ru * sigma));
        }
        else
        {
            ru = (1f / ru) * distTheta * Mathf.Atan(ru * sigma);
        }
        return uv + ruv * (ru - 1f);
    }

    /// <summary>鼠标射线打到框所在平面，落在框内（四周放宽 LockMarginMeters）返回 true，
    /// score 是命中点离框中心的距离（米），越小越优先。</summary>
    private static bool TryHitBox(Entry e, Ray ray, out float score)
    {
        score = float.MaxValue;
        Plane plane = new Plane(e.root.forward, e.root.position);
        if (!plane.Raycast(ray, out float enter))
            return false;
        Vector3 local = e.root.InverseTransformPoint(ray.GetPoint(enter)) * CanvasUnitToMeter;
        float hx = e.sizeMeters.x * 0.5f + LockMarginMeters;
        float hy = e.sizeMeters.y * 0.5f + LockMarginMeters;
        if (Mathf.Abs(local.x) > hx || Mathf.Abs(local.y) > hy)
            return false;
        score = new Vector2(local.x, local.y).magnitude;
        return true;
    }

    private static MeshRenderer PickPlaneRenderer(Renderer[] body, out MeshFilter filter)
    {
        filter = null;
        MeshRenderer best = null;
        float bestH = 0f;
        if (body == null)
            return null;
        foreach (Renderer r in body)
        {
            var mr = r as MeshRenderer;
            if (mr == null)
                continue;
            var f = mr.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null)
                continue;
            float h = mr.bounds.size.y;
            if (h > bestH)
            {
                bestH = h;
                best = mr;
                filter = f;
            }
        }
        return best;
    }

    private static Renderer[] CollectBodyRenderers(UnitDefinitionRuntimeBinder b)
    {
        var list = new List<Renderer>();
        foreach (Renderer r in b.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || IsShadowRenderer(r))
                continue;
            if (!(r is MeshRenderer) && !(r is SpriteRenderer) && !(r is SkinnedMeshRenderer))
                continue;
            // 单位根节点自己身上的 1×1 占位精灵（脚底）不算身体。
            if (r.gameObject == b.gameObject && r is SpriteRenderer)
                continue;
            list.Add(r);
        }
        return list.ToArray();
    }

    private static bool IsShadowRenderer(Renderer r)
    {
        string n = r.gameObject.name;
        return n.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Lamp", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0
            || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }

    private static Color ColorOf(Relation r)
    {
        switch (r)
        {
            case Relation.Hostile: return HostileColor;
            case Relation.Friendly: return FriendlyColor;
            default: return NeutralColor;
        }
    }

    private static string StatusOf(Entry e)
    {
        string s = TagOf(e);
        return string.IsNullOrEmpty(e.threatSuffix) ? s : s + "  //  " + e.threatSuffix;
    }

    private static string TagOf(Entry e)
    {
        return e.relation == Relation.Hostile ? "HOSTILE" : e.relation == Relation.Friendly ? "FRIENDLY" : "NEUTRAL";
    }

    private static Relation RelationOf(UnitDefinition ud, out string threat)
    {
        threat = null;
        switch (ud.characterIdentity)
        {
            case CharacterIdentity.Enemy:
            case CharacterIdentity.NeutralHostile:
                return Relation.Hostile;
            case CharacterIdentity.Elite:
                threat = "THREAT HIGH";
                return Relation.Hostile;
            case CharacterIdentity.Boss:
                threat = "THREAT CRITICAL";
                return Relation.Hostile;
            case CharacterIdentity.Ally:
                return Relation.Friendly;
            default:
                return Relation.Neutral;
        }
    }

    private static string EnglishNameOf(UnitDefinition ud)
    {
        if (ud.localizedNames != null)
        {
            for (int i = 0; i < ud.localizedNames.Count; i++)
            {
                var t = ud.localizedNames[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.text)
                    && !string.IsNullOrEmpty(t.languageCode) && t.languageCode.StartsWith("en", System.StringComparison.OrdinalIgnoreCase))
                    return t.text.ToUpperInvariant();
            }
        }

        if (!string.IsNullOrWhiteSpace(ud.unitId))
            return ud.unitId.Replace('_', ' ').ToUpperInvariant();

        return "UNKNOWN";
    }

    // ---------------- 材质 ----------------

    private static Material _additiveMaterial;

    /// <summary>线条用的加算材质：叠在暗画面上才有发光感，ZTest Always 不会被墙挡住。</summary>
    private static Material AdditiveMaterial
    {
        get
        {
            if (_additiveMaterial == null)
            {
                Shader sh = Shader.Find("SkyPrison/UI/Additive");
                if (sh != null)
                    _additiveMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _additiveMaterial;
        }
    }

    private static Material _additiveTextMaterial;

    /// <summary>文字用的加算 SDF 材质（SkyPrison/UI/TMP Additive，同样 ZTest Always、同样乘夜视仪颜色补偿）。
    /// 按字体图集共用一份；找不到着色器就保持 TMP 默认材质。</summary>
    private static void ApplyAdditiveTextMaterial(TextMeshProUGUI t)
    {
        if (t == null || t.font == null)
            return;
        if (_additiveTextMaterial == null)
        {
            Shader sh = Shader.Find("SkyPrison/UI/TMP Additive");
            if (sh == null)
                return;
            _additiveTextMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _additiveTextMaterial.mainTexture = t.font.atlasTexture;
        }
        t.fontSharedMaterial = _additiveTextMaterial;
    }

    // ---------------- 对象池 / 构建 ----------------

    private Entry Acquire(UnitDefinitionRuntimeBinder b)
    {
        Entry e;
        if (_pool.Count > 0)
        {
            e = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            e = BuildEntry();
        }

        e.binder = b;
        e.bodyRenderers = CollectBodyRenderers(b);
        e.planeRenderer = PickPlaneRenderer(e.bodyRenderers, out e.planeFilter);
        UnitDefinition ud = b.UnitDefinitionAsset;
        e.relation = RelationOf(ud, out e.threatSuffix);
        e.englishName = EnglishNameOf(ud);
        e.lockAlpha = 0f;
        e.root.gameObject.SetActive(true);
        return e;
    }

    private void Release(Entry e)
    {
        e.binder = null;
        e.bodyRenderers = null;
        e.planeRenderer = null;
        e.planeFilter = null;
        if (e.root != null)
            e.root.gameObject.SetActive(false);
        _pool.Add(e);
    }

    private void ReleaseAll()
    {
        for (int i = 0; i < _active.Count; i++)
            Release(_active[i]);
        _active.Clear();
    }

    private void BuildScreenCanvas()
    {
        // 屏幕上方那行状态字不需要贴着单位，留在 Overlay 画布上（不进后处理，颜色不会被染）。
        var go = new GameObject("[SkyPrisonNightVisionHud]");
        DontDestroyOnLoad(go);
        _screenCanvas = go.AddComponent<Canvas>();
        _screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _screenCanvas.sortingOrder = 40;
        _screenGroup = go.AddComponent<CanvasGroup>();
        _screenGroup.interactable = false;
        _screenGroup.blocksRaycasts = false;

        _modeText = MakeText("ModeText", go.transform, 20, FontStyles.Bold);
        _modeText.color = new Color(0.55f, 1f, 0.65f, 0.9f);
        _modeText.alignment = TextAlignmentOptions.Center;
        RectTransform mt = _modeText.rectTransform;
        mt.anchorMin = new Vector2(0.5f, 1f);
        mt.anchorMax = new Vector2(0.5f, 1f);
        mt.pivot = new Vector2(0.5f, 1f);
        mt.anchoredPosition = new Vector2(0f, -24f);
        mt.sizeDelta = new Vector2(900f, 30f);

        go.SetActive(false);
    }

    private Entry BuildEntry()
    {
        var e = new Entry();

        // 世界空间画布：不挂在单位层级下（VisualRoot 会按朝向翻转 scale.x，字会被镜像），
        // 单独放在场景根，每帧在 beginCameraRendering 里跟到单位身上。
        var rootGo = new GameObject("[NightVisionTarget]", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        DontDestroyOnLoad(rootGo);
        e.root = (RectTransform)rootGo.transform;
        e.root.localScale = Vector3.one * CanvasUnitToMeter;
        e.root.pivot = new Vector2(0.5f, 0.5f);
        Canvas canvas = rootGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        // 必须低于头顶战斗 HUD（UnitOverheadUIView 的画布是 150）：血条、名字要压在夜视仪框上面。
        canvas.sortingOrder = 140;
        e.group = rootGo.GetComponent<CanvasGroup>();
        e.group.interactable = false;
        e.group.blocksRaycasts = false;

        var boxGo = new GameObject("Box", typeof(RectTransform));
        boxGo.transform.SetParent(e.root, false);
        e.box = (RectTransform)boxGo.transform;
        e.box.anchorMin = e.box.anchorMax = new Vector2(0.5f, 0.5f);
        e.box.pivot = new Vector2(0.5f, 0.5f);

        var br = new GameObject("Brackets", typeof(RectTransform), typeof(CanvasRenderer), typeof(BracketGraphic));
        br.transform.SetParent(e.box, false);
        Stretch((RectTransform)br.transform);
        e.brackets = br.GetComponent<BracketGraphic>();
        e.brackets.raycastTarget = false;
        if (AdditiveMaterial != null)
            e.brackets.material = AdditiveMaterial;

        e.tagText = MakeText("Tag", e.box, TagFontSize, FontStyles.Bold);
        RectTransform tt = e.tagText.rectTransform;
        tt.anchorMin = new Vector2(0f, 1f);
        tt.anchorMax = new Vector2(0f, 1f);
        tt.pivot = new Vector2(0f, 0f);
        tt.anchoredPosition = new Vector2(0f, 6f);
        tt.sizeDelta = new Vector2(400f, 40f);
        e.tagText.alignment = TextAlignmentOptions.BottomLeft;

        var rg = new GameObject("Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(RingGraphic));
        rg.transform.SetParent(e.root, false);
        RectTransform rr = (RectTransform)rg.transform;
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
        rr.pivot = new Vector2(0.5f, 0.5f);
        e.ring = rg.GetComponent<RingGraphic>();
        e.ring.raycastTarget = false;
        if (AdditiveMaterial != null)
            e.ring.material = AdditiveMaterial;

        e.detailText = MakeText("Detail", e.root, DetailFontSize, FontStyles.Bold);
        RectTransform dt = e.detailText.rectTransform;
        dt.anchorMin = dt.anchorMax = new Vector2(0.5f, 0.5f);
        dt.pivot = new Vector2(0f, 0.5f);
        dt.sizeDelta = new Vector2(600f, 140f);
        e.detailText.alignment = TextAlignmentOptions.MidlineLeft;
        e.detailText.lineSpacing = -8f;

        ApplyAdditiveTextMaterial(e.tagText);
        ApplyAdditiveTextMaterial(e.detailText);

        if (_overheadLayer >= 0)
            SetLayerRecursively(rootGo, _overheadLayer);

        return e;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = style;
        t.characterSpacing = 6f;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ---------------- 矢量图形（不依赖贴图；单位是画布单位 = 厘米） ----------------

    /// <summary>四角 L 形括号。单层细线，靠加算混合在暗画面上提亮。</summary>
    private class BracketGraphic : MaskableGraphic
    {
        private const float CoreThickness = 2.5f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float arm = Mathf.Min(r.width, r.height) * 0.28f;

            DrawLayer(vh, r, arm, CoreThickness, 1f, 0f);
        }

        private void DrawLayer(VertexHelper vh, Rect r, float arm, float t, float alphaScale, float whiten)
        {
            Color c = Color.Lerp(color, Color.white, whiten);
            c.a = color.a * alphaScale;
            Color32 col = c;

            AddCorner(vh, r.xMin, r.yMin, 1f, 1f, arm, t, col);
            AddCorner(vh, r.xMax, r.yMin, -1f, 1f, arm, t, col);
            AddCorner(vh, r.xMin, r.yMax, 1f, -1f, arm, t, col);
            AddCorner(vh, r.xMax, r.yMax, -1f, -1f, arm, t, col);
        }

        // 横线占住角上那一块，竖线从横线的内沿接着往下画——两段不重叠，
        // 加算混合下角上才不会因为画了两次而发亮，整条线是同一个亮度。
        private static void AddCorner(VertexHelper vh, float x, float y, float sx, float sy, float arm, float t, Color32 col)
        {
            float midOffset = CoreThickness * 0.5f;
            float half = t * 0.5f;
            AddQuad(vh, x + sx * (midOffset - half), y + sy * (midOffset - half), x + sx * arm, y + sy * (midOffset + half), col);
            AddQuad(vh, x + sx * (midOffset - half), y + sy * (midOffset + half), x + sx * (midOffset + half), y + sy * arm, col);
        }

        private static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), col, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), col, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), col, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    /// <summary>分段圆环：三段长弧（外细内粗的双弧），外弧两端各折一段径向短线朝外，和弧线连成直角角标；
    /// 圆心一个小圆点。各部分互不重叠，加算混合下亮度均匀。</summary>
    private class RingGraphic : MaskableGraphic
    {
        private const float CoreThickness = 3f;
        private const float ArcSpan = 75f;
        private const float TickLength = 12f;       // 角标朝外折出去的长度（画布单位 = 厘米）
        private const float TickThickness = 8f;     // 角标竖线的粗细，比弧线（CoreThickness）粗，端点更醒目
        private const float CenterDotRadius = 4f;   // 圆心点半径（厘米）
        private const float InnerArcThickness = 7f; // 内侧粗弧的粗细（外弧是 CoreThickness）
        private const float InnerArcGap = 5f;       // 内外两条弧之间的缝
        private const float InnerArcInsetDeg = 9f;  // 内弧两端比外弧各收短的角度

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float outer = Mathf.Min(r.width, r.height) * 0.5f;
            float mid = outer - CoreThickness * 0.5f;
            if (mid <= CoreThickness * 4f)
                return;

            DrawLayer(vh, mid, CoreThickness, 1f, 0f);

            // 圆心标记点：锁定环出现时标出瞄准中心。
            Color32 dot = color;
            AddArc(vh, 0f, CenterDotRadius, 0f, 360f, dot);
        }

        private void DrawLayer(VertexHelper vh, float mid, float t, float alphaScale, float whiten)
        {
            Color c = Color.Lerp(color, Color.white, whiten);
            c.a = color.a * alphaScale;
            Color32 col = c;
            float half = t * 0.5f;

            // 角标：在弧的两个端点各折一段径向短线朝外（背离圆心），和弧线连成一个直角。
            // 内沿和弧的内沿对齐、端面和弧的端面对齐。
            float tickWidthDeg = Mathf.Rad2Deg * (TickThickness / mid);
            float tickInner = mid - half;
            float tickOuter = mid + half + TickLength;

            for (int i = 0; i < 3; i++)
            {
                float start = i * 120f + 10f;
                float end = start + ArcSpan;
                // 弧线在两端让出角标的宽度，角标独占交接处，不重叠（加算下重叠会发亮）。
                AddArc(vh, mid - half, mid + half, start + tickWidthDeg, end - tickWidthDeg, col);
                AddArc(vh, tickInner, tickOuter, end - tickWidthDeg, end, col);

                // 内侧再走一条更粗、两端各收短一点的长弧：外细内粗的双弧。和外弧之间留一道缝，不重叠。
                float innerOuter = mid - half - InnerArcGap;
                AddArc(vh, innerOuter - InnerArcThickness, innerOuter, start + InnerArcInsetDeg, end - InnerArcInsetDeg, col);
                AddArc(vh, tickInner, tickOuter, start, start + tickWidthDeg, col);
            }
        }

        private static void AddArc(VertexHelper vh, float inner, float outer, float deg0, float deg1, Color32 col)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((deg1 - deg0) / 6f));
            int baseIndex = vh.currentVertCount;
            for (int s = 0; s <= steps; s++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(deg0, deg1, (float)s / steps);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(dir * inner, col, Vector2.zero);
                vh.AddVert(dir * outer, col, Vector2.zero);
                if (s > 0)
                {
                    int i = baseIndex + (s - 1) * 2;
                    vh.AddTriangle(i, i + 1, i + 3);
                    vh.AddTriangle(i + 3, i + 2, i);
                }
            }
        }
    }
}
