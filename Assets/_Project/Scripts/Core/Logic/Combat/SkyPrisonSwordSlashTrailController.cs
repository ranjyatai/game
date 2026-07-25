using UnityEngine;
using Spine;
using Spine.Unity;
using Effekseer;

/// <summary>
/// 剑挥砍特效——只在当前生效武器类别是 WeaponCategoryType.Sword 时启用，复用
/// UnitCombatHitbox 判定开关用的同一对 hit_start/hit_end Spine事件（见
/// UnitActionModuleRuntime），不需要美术在动画里另外加新标记。
///
/// 2026-07-17：改用 Effekseer 播放美术做好的成品特效（VFX_Sword_Slash_01，含真正的
/// 背景扭曲，来自用户自己的Effekseer资产库），取代早期试验版的自研TrailRenderer+
/// 自定义扭曲Shader方案——见 feedback-asset-store-integration-workflow 记忆
/// "2026-07-17实测补充"：触发时机/锚点追踪这两块基建保留复用，只换了"怎么画出来"
/// 这一层。
///
/// 2026-07-17（第二次）：锚点优先读 Spine Point Attachment——固定插槽
/// "weapon_fx_tip"下，每把武器自己的Skin里放一个同名（"fx_tip"）的Point，
/// 位置直接摆在这把武器视觉上的刀尖，不用再猜偏移量（Point是Skin的一部分，
/// 天然跟着换武器切换，道理跟 weaponJudgmentSlotName 那个判定框插槽一样）。
/// 找不到这个Point时（比如还没来得及给某把旧武器加），自动退回旧的
/// "BoneFollower + ItemEquipmentExtension.weaponSlashEffectTipOffset手动偏移量"
/// 方案——两套锚点逻辑并存，不用等所有武器都补完Point才能用。
///
/// 2026-07-18：特效资产改成从当前技能读（SkillDefinition.swingVFX），不再是所有剑类
/// 武器攻击都固定播 VFX_Sword_Slash_01。不同技能（剑击一段/剑击蓄力等）现在可以各配
/// 各的特效，某个技能没配就不播——effectAssetOverride 字段降级成手动兜底，技能没配
/// 时才用得到。剑击一段这个技能本身已经把 swingVFX 指回 VFX_Sword_Slash_01，行为跟
/// 改之前一致。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonSwordSlashTrailController : MonoBehaviour
{
    [SerializeField] private UnitActionModuleRuntime actionModule;

    [Header("Spine Event Key（跟 UnitCombatHitbox 判定开关用同一对）")]
    [SerializeField] private string hitStartEventKey = "hit_start";
    [SerializeField] private string hitEndEventKey   = "hit_end";

    [Header("锚点 · Point Attachment（首选）")]
    [Tooltip("固定插槽名——这个插槽本身要在骨架里建一次（骨架级别，所有武器共用），\n" +
             "每把武器再各自在自己的Skin里往这个插槽下放一个Point Attachment。")]
    [SerializeField] private string fxTipSlotName = "weapon_fx_tip";
    [Tooltip("Point Attachment的名字——所有武器统一用这同一个名字，代码才能不用\n" +
             "关心当前具体是哪把武器，直接按插槽+这个名字查。")]
    [SerializeField] private string fxTipAttachmentName = "fx_tip";

    [Header("锚点 · 骨骼+偏移量（兜底，武器还没加Point时用）")]
    [Tooltip("2026-07-17：改成从 ItemEquipmentExtension.weaponSlashEffectTipOffset 按\n" +
             "当前装备的具体武器读取——不同剑刀刃长度/骨骼朝向不一样，写死一个全局值\n" +
             "换武器就会错位。这里留一个兜底默认值，只在武器数据没配置时用。\n" +
             "2026-07-17（第二次）：现在只在骨架里找不到 fxTipSlotName/fxTipAttachmentName\n" +
             "对应的Point Attachment时才会用到这一整套。")]
    [SerializeField] private Vector3 fallbackTipOffset = new Vector3(-1f, 0f, 0f);

    [Header("Effekseer 特效")]
    [Tooltip("2026-07-18：特效改成从当前技能(SkillDefinition.swingVFX)读取，不同技能可以配\n" +
        "不同特效，不再是同武器类别共用同一个。这个字段只在技能没配 swingVFX 时才会用到\n" +
        "（手动兜底覆盖），正常情况应该留空，让技能数据决定播什么。")]
    [SerializeField] private EffekseerEffectAsset effectAssetOverride;
    [Tooltip("排查用：关掉之后播放时不套用骨骼当前旋转，用默认朝向——用来判断特效\n" +
             "看不见是不是因为挥砍动画这一帧骨骼转到了刁钻角度，把特效转得看不见了。")]
    [SerializeField] private bool applyBoneRotation = true;
    [Tooltip("角色朝右时是靠 spineRoot.localScale.x 取负值做镜像翻转的（见\n" +
             "SpineAnimationDriver_Current.ApplyFacing）。这个修正只在Point Attachment\n" +
             "锚点路径上生效——那条路径是自己拼世界旋转的，不会感知镜像。骨骼+偏移量\n" +
             "兜底路径用BoneFollower跟踪，BoneFollower自己内部已经无条件处理过镜像了\n" +
             "（BoneFollower.cs:230），这条路径不能再套用这个修正，否则两次取反会正好\n" +
             "抵消，等于没修正（2026-07-17踩过：朝右挥砍方向不对，就是这个原因，不是\n" +
             "缺修正，是修正跟BoneFollower自己的重复了）。数学上做的是绕X轴镜像：\n" +
             "(x,y,z,w)→(x,-y,-z,w)。")]
    [SerializeField] private bool correctRotationForFacingMirror = true;
    [SerializeField] private SpineAnimationDriver_Current facingDriver;
    [Tooltip("特效整体缩放——原始特效是照着来源项目的角色比例做的，跟这边角色大小\n" +
             "不一定匹配，太大/太小都在这里调，不用跑去改Effekseer工程文件本身。\n" +
             "2026-07-17实测0.5比较合适。")]
    [SerializeField] private Vector3 effectScale = new Vector3(0.5f, 0.5f, 0.5f);
    [Tooltip("2026-07-17（第三次排查，已确认）：单纯修正旋转角度改不对镜像问题——挥砍\n" +
             "弧线本身是不对称（手性）的形状，角色镜像后手臂的挥砍轨迹是原来的镜像图形，\n" +
             "不是单纯换个方向的同一个图形，旋转再怎么调都做不出「镜像」这个效果，只有\n" +
             "真的把某个轴的缩放取负（做一次几何翻转）才行。朝右(Facing==-1)时，这三个\n" +
             "分量会分别乘到 effectScale 上再传给SetScale。实测确认是Y轴——「上下颠倒」\n" +
             "这个症状本来就是直接指向Unity的Y轴（Y-up），不用一个个试。")]
    [SerializeField] private Vector3 facingMirrorScaleSign = new Vector3(1f, -1f, 1f);
    [Tooltip("特效播放速度——1=特效原速。实测比Spine手臂挥舞的实际速度快，调小让\n" +
             "两者的节奏对上（比如0.7~0.8），不用去改Effekseer工程文件本身的帧数。")]
    [SerializeField] private float playbackSpeed = 1f;
    [Tooltip("EffekseerHandle.layer 不显式设置时默认是0（Default层）——Effekseer插件\n" +
             "底层真的会拿这个layer去跟Camera.cullingMask做比对过滤（见\n" +
             "EffekseerRenderPath.cs），之前只能靠给Main Camera的Culling Mask加\n" +
             "Default层来让特效显示，等于把整个Default层都放开了。这里改成显式指定\n" +
             "一个摄像机本来就包含的层（默认World3D），特效不再依赖Default，Main\n" +
             "Camera的Culling Mask可以把Default摘掉。")]
    [SerializeField] private string effectRenderLayerName = "World3D";

    [Header("调试")]
    [Tooltip("排查看不到效果时用，看不到问题所在了之后关掉，不然每次触发都刷日志。")]
    [SerializeField] private bool debugLogs = false;
    [Tooltip("特效播放的瞬间自动暂停编辑器——排查锚点位置对不对时用，不用抢时间手动切\n" +
             "Scene视图，暂停之后随便慢慢看。调好位置之后记得关掉，不然平时测试也会被打断。")]
    [SerializeField] private bool pauseOnTrigger = false;

    private SkeletonAnimation _skeletonAnimation;
    private GameObject _anchorGo;
    private BoneFollower _boneFollower;
    private Transform _tipTransform;
    private string _currentBoneName;
    private EffekseerHandle _currentHandle;
    private Vector3 _debugLastWorldPosition;
    private Quaternion _debugLastWorldRotation;
    private bool _hasDebugLastPosition;
    private int _resolvedEffectRenderLayer = -1;

    private void Awake()
    {
        if (actionModule == null) actionModule = GetComponent<UnitActionModuleRuntime>();
        if (facingDriver == null) facingDriver = GetComponentInParent<SpineAnimationDriver_Current>();
        if (facingDriver == null) facingDriver = GetComponentInChildren<SpineAnimationDriver_Current>();
    }

    private void Update()
    {
        // UnitActionModuleRuntime 的 SkeletonAnimation 是由 Applier 在 Awake 之后才显式
        // 传进去的（避免拿到 outline proxy 骨架），这里没有事件可以订阅这个时机，
        // 用一次性的引用比对代替——便宜（一次引用比较），只在真正变化时才重新订阅。
        SkeletonAnimation current = actionModule != null ? actionModule.SkeletonAnimation : null;
        if (current == _skeletonAnimation) return;

        if (_skeletonAnimation != null)
            _skeletonAnimation.AnimationState.Event -= HandleSpineEvent;

        _skeletonAnimation = current;

        if (_skeletonAnimation != null)
            _skeletonAnimation.AnimationState.Event += HandleSpineEvent;
    }

    private void OnDisable()
    {
        if (_skeletonAnimation != null)
            _skeletonAnimation.AnimationState.Event -= HandleSpineEvent;
        _skeletonAnimation = null;

        if (_anchorGo != null)
        {
            Destroy(_anchorGo);
            _anchorGo = null;
            _boneFollower = null;
            _tipTransform = null;
        }
    }

    private void HandleSpineEvent(TrackEntry trackEntry, Spine.Event e)
    {
        if (e == null) return;
        if (debugLogs) Debug.Log($"[SwordSlashTrail] Spine事件：{e.Data.Name}（动画clip={trackEntry?.Animation?.Name ?? "NULL"}，" +
            $"TrackTime={trackEntry?.TrackTime.ToString("F3") ?? "NULL"}，AnimationStart={trackEntry?.AnimationStart.ToString("F3") ?? "NULL"}，" +
            $"MixDuration={trackEntry?.MixDuration.ToString("F3") ?? "NULL"}）", this);

        if (e.Data.Name == hitStartEventKey)
        {
            if (IsActiveSwordEquipped(out ItemEquipmentExtension weaponExt))
                PlaySlashEffect(weaponExt);
        }
        // hit_end 不需要处理——Effekseer特效是自带时长的一次性播放，不是需要手动
        // 开关的常驻拖尾，播完自己结束。
    }

    // 只在"我是玩家控制的这个单位"且"当前生效武器是剑类别"时启用——EquipmentRuntime
    // 是玩家单人份的静态实例，敌人也会挂这个组件（跟 WeaponVisualRuntime 一样全Character
    // 单位通用），必须先确认这就是玩家自己（同 WeaponVisualRuntime.BelongsToEquipmentSingleton）。
    private bool IsActiveSwordEquipped(out ItemEquipmentExtension weaponExt)
    {
        weaponExt = null;

        GameObject playerGo = SkyPrisonPlayerAuthority.Instance != null
            ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject
            : null;
        if (playerGo == null)
        {
            if (debugLogs) Debug.Log("[SwordSlashTrail] 拒绝：找不到 SkyPrisonPlayerAuthority.CurrentPlayerGameObject", this);
            return false;
        }
        if (!transform.IsChildOf(playerGo.transform) && transform != playerGo.transform)
        {
            if (debugLogs) Debug.Log("[SwordSlashTrail] 拒绝：这个单位不是玩家自己", this);
            return false;
        }

        var eq = EquipmentRuntime.Instance;
        if (eq == null)
        {
            if (debugLogs) Debug.Log("[SwordSlashTrail] 拒绝：EquipmentRuntime.Instance 为空", this);
            return false;
        }

        var entry = eq.GetEquipped(eq.ActiveWeaponSlot);
        var ext = entry?.definition?.equipment;
        if (ext == null)
        {
            if (debugLogs) Debug.Log($"[SwordSlashTrail] 拒绝：当前生效槽（{eq.ActiveWeaponSlot}）没有装备", this);
            return false;
        }
        if (ext.weaponCategory != WeaponCategoryType.Sword)
        {
            if (debugLogs) Debug.Log($"[SwordSlashTrail] 拒绝：当前武器类别是「{ext.weaponCategory}」，不是Sword", this);
            return false;
        }
        if (string.IsNullOrEmpty(ext.weaponSkinName))
        {
            if (debugLogs) Debug.Log("[SwordSlashTrail] 拒绝：这把剑的 weaponSkinName 是空的", this);
            return false;
        }

        weaponExt = ext;
        return true;
    }

    private void PlaySlashEffect(ItemEquipmentExtension weaponExt)
    {
        if (_skeletonAnimation == null)
        {
            if (debugLogs) Debug.Log("[SwordSlashTrail] PlaySlashEffect：_skeletonAnimation 还没绑定", this);
            return;
        }

        EffekseerEffectAsset effect = ResolveEffectAsset();
        if (effect == null)
        {
            // 当前这个技能没配 swingVFX（也没有手动兜底覆盖）——不是错误，就是这个技能
            // 本来就不需要专属特效（比如蓄力刺击可能只想要挥砍音效，不需要轨迹特效）。
            if (debugLogs) Debug.Log("[SwordSlashTrail] 当前技能未配置 swingVFX，且无手动兜底覆盖，跳过播放。", this);
            return;
        }

        bool usedPointAttachment = TryGetFxTipWorldTransform(out Vector3 worldPos, out Quaternion worldRot);

        if (!usedPointAttachment)
        {
            // Point Attachment没找到（这把武器的Skin还没加，或者骨架里压根还没建这个
            // 插槽）——退回旧的"骨骼+每把武器手调偏移量"方案，保证还没来得及改的
            // 旧武器不受影响。
            string boneName = weaponExt.weaponSkinName;
            Vector3 tipOffset = weaponExt.weaponSlashEffectTipOffset != Vector3.zero
                ? weaponExt.weaponSlashEffectTipOffset
                : fallbackTipOffset;

            EnsureAnchor(boneName, tipOffset);
            if (_tipTransform == null) return;

            worldPos = _tipTransform.position;
            worldRot = _tipTransform.rotation;
        }

        // 技能自己的额外偏移修正(SkillDefinition.swingVfxOffset)——共用锚点(不管是
        // Point Attachment 还是骨骼+偏移量兜底)对某些姿势特殊的技能(比如空中挥砍)
        // 可能对不上，这里叠加一次单独修正，不影响其它技能共用的锚点。用
        // TransformVector(不是TransformPoint)转到世界空间，会自动跟着
        // skeletonTransform当前的镜像缩放正确翻转方向，不用再手动处理左右镜像。
        SkillDefinition currentSkill = actionModule?.CurrentSkill;
        if (currentSkill != null && currentSkill.swingVfxOffset != Vector3.zero)
            worldPos += _skeletonAnimation.transform.TransformVector(currentSkill.swingVfxOffset);

        _debugLastWorldPosition = worldPos;
        _debugLastWorldRotation = worldRot;
        _hasDebugLastPosition = true;

        // 同一把剑连续挥砍太快时，先停掉上一次还没播完的，避免叠加出一堆重叠的
        // 特效实例——一次攻击对应一次干净的播放。
        if (_currentHandle.exists)
            _currentHandle.Stop();

        _currentHandle = EffekseerSystem.PlayEffect(effect, worldPos);
        _currentHandle.layer = ResolveEffectRenderLayer();
        if (applyBoneRotation)
        {
            // 镜像修正只在Point Attachment路径上做——那条路径是我们自己拼的
            // (skeletonTransform.rotation * Quaternion.Euler(...))，完全没有
            // 感知spineRoot镜像这件事。骨骼+偏移量兜底路径用的是BoneFollower，
            // 它自己内部（BoneFollower.cs:230）已经无条件处理过镜像了，这里如果
            // 再修正一次，两次取反会正好抵消，等于没修正。
            if (usedPointAttachment && correctRotationForFacingMirror && facingDriver != null && facingDriver.Facing == -1)
                worldRot = new Quaternion(worldRot.x, -worldRot.y, -worldRot.z, worldRot.w);
            _currentHandle.SetRotation(worldRot);
        }

        Vector3 appliedScale = effectScale;
        if (facingDriver != null && facingDriver.Facing == -1)
            appliedScale = Vector3.Scale(appliedScale, facingMirrorScaleSign);
        _currentHandle.SetScale(appliedScale);
        _currentHandle.speed = playbackSpeed;

        if (debugLogs) Debug.Log($"[SwordSlashTrail] 特效已播放（{(usedPointAttachment ? "Point Attachment锚点" : "骨骼+偏移量兜底")}），" +
            $"世界坐标={worldPos}，朝向={(facingDriver != null ? facingDriver.Facing.ToString() : "无facingDriver")}", this);

        if (pauseOnTrigger) Debug.Break();
    }

    // 优先锚点：固定插槽(fxTipSlotName)下查当前生效Skin里名叫fxTipAttachmentName的
    // Point Attachment。Point是Skin的一部分（跟判定用的Boundingbox attachment同一套
    // 机制），随着 Skeleton.SetSkin 自动切换到当前装备的这把武器自己的位置，不需要
    // 任何按武器手调的偏移量。骨架里还没建这个插槽，或者当前武器的Skin还没加这个
    // Point时，返回false，调用方退回旧的骨骼+偏移量方案。
    private bool TryGetFxTipWorldTransform(out Vector3 worldPosition, out Quaternion worldRotation)
    {
        worldPosition = default;
        worldRotation = default;

        Skeleton skeleton = _skeletonAnimation.Skeleton;
        Slot slot = skeleton.FindSlot(fxTipSlotName);
        if (slot == null) return false;

        Attachment attachment = skeleton.GetAttachment(slot.Data.Index, fxTipAttachmentName);
        if (!(attachment is PointAttachment point)) return false;

        Bone bone = slot.Bone;
        point.ComputeWorldPosition(bone.AppliedPose, out float localX, out float localY);
        float rotationDeg = point.ComputeWorldRotation(bone.AppliedPose);

        Transform skeletonTransform = _skeletonAnimation.transform;
        worldPosition = skeletonTransform.TransformPoint(new Vector3(localX, localY, 0f));
        worldRotation = skeletonTransform.rotation * Quaternion.Euler(0f, 0f, rotationDeg);
        return true;
    }

    private int ResolveEffectRenderLayer()
    {
        if (_resolvedEffectRenderLayer < 0)
        {
            _resolvedEffectRenderLayer = LayerMask.NameToLayer(effectRenderLayerName);
            if (_resolvedEffectRenderLayer < 0)
            {
                Debug.LogWarning($"[SwordSlashTrail] 找不到名为「{effectRenderLayerName}」的Layer，特效会退回Effekseer默认的0号(Default)层。", this);
                _resolvedEffectRenderLayer = 0;
            }
        }
        return _resolvedEffectRenderLayer;
    }

    private EffekseerEffectAsset ResolveEffectAsset()
    {
        EffekseerEffectAsset skillVfx = actionModule != null ? actionModule.CurrentSkill?.swingVFX : null;
        if (skillVfx != null)
            return skillVfx;

        return effectAssetOverride;
    }

    private void EnsureAnchor(string boneName, Vector3 tipOffset)
    {
        if (_anchorGo == null)
        {
            // SkeletonAnimation(SkeletonAnimationBase) 跟 BoneFollower 要的 SkeletonRenderer
            // 是同一个GameObject上两个不同的组件（SkeletonAnimationBase内部也是
            // GetComponent<ISkeletonRenderer>()拿的，见其Renderer属性），不能把
            // SkeletonAnimation直接赋给SkeletonRenderer类型的字段。
            SkeletonRenderer skeletonRenderer = _skeletonAnimation.GetComponent<SkeletonRenderer>();
            if (skeletonRenderer == null)
            {
                Debug.LogWarning("[SkyPrisonSwordSlashTrailController] 骨架GameObject上找不到SkeletonRenderer组件，特效锚点挂不上去。", this);
                return;
            }

            // 不能直接挂骨架根节点/挂在本物体身上——道理跟 WeaponVisualRuntime 里判定
            // 挂件的注释一样：BoneFollower 需要独立的挂载物才能干净地跟着骨骼走。
            _anchorGo = new GameObject("SwordSlashEffectAnchor");
            _anchorGo.transform.SetParent(_skeletonAnimation.transform.parent ?? _skeletonAnimation.transform, false);

            _boneFollower = _anchorGo.AddComponent<BoneFollower>();
            _boneFollower.skeletonRenderer   = skeletonRenderer;
            _boneFollower.followBoneRotation = true;
            _boneFollower.followZPosition    = false;
            // 注意：BoneFollower.cs:230（skeletonLossyScale.x*y<0 时把
            // boneWorldRotation取反）是无条件执行的，不受任何公开字段控制，没法
            // 关掉——它自己已经正确处理了spineRoot.localScale.x镜像翻转这件事。
            // PlaySlashEffect 里的 correctRotationForFacingMirror 修正只应该用在
            // 没有BoneFollower兜底的Point Attachment路径上，这条路径（骨骼+偏移量）
            // 不能再手动修正一次，两边各修正一次会正好互相抵消（等于完全没修正）。

            var tipGo = new GameObject("SwordSlashEffectTip");
            tipGo.transform.SetParent(_anchorGo.transform, false);
            _tipTransform = tipGo.transform;
        }

        // 每次都重新套用一次——不能只在锚点第一次创建时赋值一次就不管了，不然
        // Inspector里改偏移量之后不会有任何效果（调试时改了半天数值结果毫无
        // 反应，就是这个原因）。现在这个偏移是当前装备的具体武器给的，不是
        // 固定值，换武器时也会跟着更新。
        _tipTransform.localPosition = tipOffset;

        if (_currentBoneName != boneName)
        {
            _currentBoneName        = boneName;
            _boneFollower.boneName  = boneName;
            _boneFollower.Initialize();
        }
    }

    // 一直显示（不用选中这个动态生成的物体），方便在Scene视图里直接看清楚
    // 特效实际会播放在角色身上哪个位置——给还在用兜底方案的旧武器调
    // weaponSlashEffectTipOffset 时对着这个小球看，比"打一下看游戏画面有没有
    // 效果"这种间接方式直接得多。走Point Attachment的武器理论上不需要再调，
    // 但一样画出来方便一眼确认Point摆的位置对不对。
    private void OnDrawGizmos()
    {
        Vector3 position;
        Vector3 direction;
        if (_hasDebugLastPosition)
        {
            position = _debugLastWorldPosition;
            direction = _debugLastWorldRotation * Vector3.right;
        }
        else if (_tipTransform != null)
        {
            position = _tipTransform.position;
            direction = _tipTransform.right;
        }
        else
        {
            return;
        }

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(position, 0.15f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(position, position + direction * 0.5f);
    }
}
