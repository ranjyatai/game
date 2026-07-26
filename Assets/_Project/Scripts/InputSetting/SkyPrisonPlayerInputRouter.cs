using UnityEngine;

[DefaultExecutionOrder(8500)]
public class SkyPrisonPlayerInputRouter : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V7 - 2026-06-14 - auto input settings runtime mirror";

    [Header("References")]
    [SerializeField] private UnitActionController actionController;
    [SerializeField] private UnitMovementController movementController;
    [SerializeField] private UnitActionModuleRuntime combatModuleRuntime;
    [SerializeField] private SkyPrisonInputSettings inputSettings;
    [SerializeField] private SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1 windowManager;
    [SerializeField] private GameObject inventoryPrefab;
    [SerializeField] private bool autoFindActionController = true;
    [SerializeField] private bool useSkyPrisonInputSettings = true;

    [Header("Dodge Facing Rule")]
    [Tooltip("开启后，前闪/后闪不再按固定 WASD 的上下判断，而是按当前输入方向和角色当前朝向的夹角判断。")] 
    [SerializeField] private bool dodgeRelativeToCurrentFacing = true;
    [Tooltip("输入方向与当前朝向点积小于该值时，判定为后闪。0 表示严格超过 90 度才算后闪；负值更宽松。")] 
    [SerializeField] private float backDodgeDotThreshold = -0.15f;
    [Tooltip("没有方向输入时，默认按角色当前朝向前闪。")] 
    [SerializeField] private bool noInputDodgeUsesCurrentFacingForward = true;

    [Header("Player Ownership Guard")]
    [Tooltip("防呆：只有当前单位确认为玩家输入源时才读取键盘。敌人 / AI 单位不要开启。后续由 UnitDefinition 控制权字段自动下发。")]
    [SerializeField] private bool playerInputEnabled = true;

    [Header("轻/重攻击共享按键长按判定")]
    [Tooltip("轻攻击和重攻击如果绑定了同一个物理键（默认都是鼠标左键），单纯按下/抬起没法\n" +
        "区分点按和长按，需要等一小段时间才能确定意图：按下后在这段时间内松开算轻攻击，\n" +
        "撑过这段时间还按着就当作重攻击(进入蓄力)。J/K这种各自独立绑定的键不受影响，" +
        "按下立即触发。")]
    [SerializeField] private float sharedAttackKeyHoldThreshold = 0.16f;
    private bool _sharedAttackKeyPending = false;
    private float _sharedAttackKeyPressTime = -999f;

    [Header("空中攻击输入缓冲")]
    [Tooltip("跳跃空中攻击要求 CanEnterAerialAttackPublic() 通过——脚刚离地那一下还处于\n" +
        "起跳(Start)阶段，要等真正进入空中(Air)阶段才允许触发，这中间有一小段窗口按\n" +
        "攻击键会被直接拒绝，手感上像是\"跳的瞬间按攻击没反应\"。加个输入缓冲：这段\n" +
        "时间内按键先记下来，一旦真正进入Air阶段就自动补发，不用玩家卡着精确时机\n" +
        "再按一次。")]
    [SerializeField] private float aerialAttackInputBufferSeconds = 0.25f;
    private bool _bufferedAerialAttackPending = false;
    private float _bufferedAerialAttackPressTime = -999f;

    [Header("Fallback Keys")]
    [SerializeField] private bool enableFallbackKeysWhenSettingsMissing = true;
    [SerializeField] private KeyCode fallbackJumpKey = KeyCode.Space;
    [SerializeField] private KeyCode fallbackSprintKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode fallbackSprintSecondaryKey = KeyCode.RightShift;
    [SerializeField] private KeyCode fallbackDodgeKey = KeyCode.None;
    [SerializeField] private KeyCode fallbackLightAttackKey = KeyCode.Mouse0;
    [SerializeField] private KeyCode fallbackHeavyAttackKey = KeyCode.Mouse1;
    [SerializeField] private KeyCode fallbackReloadKey = KeyCode.R;

    [Header("Runtime Debug")]
    [SerializeField] private Vector2 currentMoveInput = Vector2.zero;
    [SerializeField] private bool currentRunHeld = false;
    [SerializeField] private bool currentSneakHeld = false;
    [SerializeField] private float lastSprintTapTime = -999f;
    [SerializeField] private float lastSprintReleaseTime = -999f;
    [SerializeField] private bool sprintReleasedAfterLastTap = true;
    [SerializeField] private float suppressRunUntil = -999f;
    [SerializeField] private string lastInputEvent = "";
    [SerializeField] private Vector2 lastKnownFacingInput = Vector2.right;

    [Header("武器切换滚轮")]
    [Tooltip("两次切换武器之间的最短间隔——鼠标滚轮快速连续滚动时，Input.mouseScrollDelta\n" +
             "在好几帧里都会读到非零值，不加冷却的话一次物理滚动会在这几帧里连续触发好几次\n" +
             "CycleActiveWeapon，只有两把武器时来回切偶数次等于切了个寂寞，表现就是\"一直滚\n" +
             "它就没反应了\"。加这个冷却让每次滚动只切一格，跟卡槽感一致。")]
    [SerializeField] private float weaponCycleCooldown = 0.18f;
    private float _lastWeaponCycleTime = -999f;

    // 手柄用 D-Pad 左/右 切武器（跟滚轮共用同一个 CycleActiveWeapon/冷却），照抄
    // SkyPrisonInventoryGamepad 里读 D-pad 轴的安全读法（轴没配置时返回0，不抛异常）
    // 和"归中态才允许下一次触发"的边沿检测（按住不会连续重复触发，一次按压只切一格）。
    private const string DPadHorizontalAxis = "DPadHorizontal";
    private const float DPadWeaponCycleThreshold = 0.5f;
    private bool _dPadAtRest = true;
    private static bool _dPadAxisMissingLogged;

    private static float ReadDPadHorizontalRaw()
    {
        try { return Input.GetAxisRaw(DPadHorizontalAxis); }
        catch
        {
            if (!_dPadAxisMissingLogged)
            {
                Debug.LogWarning($"[SkyPrisonPlayerInputRouter] 未找到输入轴 \"{DPadHorizontalAxis}\"，请确认 InputManager 已加入 D-pad 轴（重启/刷新工程）。");
                _dPadAxisMissingLogged = true;
            }
            return 0f;
        }
    }

    public string Version => scriptVersion;
    public bool PlayerInputEnabled
    {
        get => playerInputEnabled;
        set => playerInputEnabled = value;
    }

    private void Awake()
    {
        ResolveReferences();
        ResolveInputSettings();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveInputSettings();
        ResetSprintTapState();
    }

    private void OnDisable()
    {
        if (actionController != null)
            actionController.SubmitMoveIntent(Vector2.zero, false, false);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolveReferences();
        AutoAssignDefaultInputSettingsEditor();
    }

    private void OnValidate()
    {
        if (!useSkyPrisonInputSettings)
            return;

        AutoAssignDefaultInputSettingsEditor();
    }

    private void AutoAssignDefaultInputSettingsEditor()
    {
        if (inputSettings != null)
            return;

        SkyPrisonInputSettings asset = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(SkyPrisonInputSettings.DefaultAssetPath);
        if (asset == null)
            return;

        inputSettings = asset;
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void Update()
    {
        // 读条揭幕前彻底屏蔽玩家输入：攻击/移动/背包全部不响应，
        // 避免黑屏期间已经能听到攻击音效、角色已经能动的问题。
        if (SceneLoader.IsAwaitingReveal)
            return;

        ResolveReferences();

        // 菜单类按键（背包/地图）不依赖 actionController，提前处理
        SkyPrisonInputSettings settingsEarly = ResolveInputSettings();
        if (settingsEarly != null && playerInputEnabled)
        {
            // 设置窗口/暂停菜单这类"纯手写全屏窗口"打开时会把 SkyPrisonWindowManager_V1.
            // ExternalBlock 设成 true（跟背包等走 prefab 流程的窗口不是同一套机制，之前这里
            // 只顾着处理 Menu 键的互斥、完全没管背包键，导致设置界面开着的时候按 B 依然能把
            // 背包叠加打开在上面）。背包键也要尊重这个标记，跟 Esc 键一样。
            // 但角色面板是"可以互相切换"的悬浮窗口（跟设置/暂停菜单那种真·模态不是一类），
            // 面板开着时按背包键应该照样能切过去，不该被面板自己造成的 ExternalBlock 挡住。
            bool blockedByRealModal = SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.ExternalBlock && !CharacterPanelController.IsOpen;
            if (settingsEarly.GetActionDown(SkyPrisonInputAction.Inventory) && !blockedByRealModal)
            {
                ToggleInventory();
                lastInputEvent = "Inventory";
            }

            // 角色面板走自己的一套静态 Show/Hide（跟设置/暂停菜单一样是纯代码窗口），
            // 打开时自己会把 ExternalBlock 置 true——如果这里跟背包一样只挡
            // "!ExternalBlock"，面板打开后再按一次 C 会被自己造成的 ExternalBlock 挡住、
            // 关不掉。用 blockedByRealModal 放行"关闭自己/从背包切过来"这些情况，
            // 只有设置/暂停菜单这种真·模态开着时才真正挡住 C 键。
            if (settingsEarly.GetActionDown(SkyPrisonInputAction.CharacterPanel) && !blockedByRealModal)
            {
                // 同理，按 C 是"切换到角色面板"，背包开着的话先关掉背包再开面板。
                if (!CharacterPanelController.IsOpen && windowManager != null && windowManager.IsOpen("inventory"))
                    windowManager.Close("inventory");

                CharacterPanelController.Toggle();
                lastInputEvent = "CharacterPanel";
            }

            // 致命报错弹窗（SkyPrisonErrorReporter.ShowFatalDialog）打开时也会把 ExternalBlock
            // 置 true，但之前这里只挡 PauseMenuController.IsOpen，没查 ExternalBlock——
            // 报错弹窗只监听自己的确认键，完全不管 Esc，导致弹窗开着的时候 Esc 还能把
            // 暂停菜单叠开在报错弹窗后面。报错弹窗的优先级应该最高、拦截一切除了它自己
            // 的确认按钮，这里补上跟 Inventory/CharacterPanel 一样的 blockedByRealModal 检查。
            if (settingsEarly.GetActionDown(SkyPrisonInputAction.Menu) && !PauseMenuController.IsOpen && !blockedByRealModal)
            {
                // 任何走 SkyPrisonWindowManager_V1 的悬浮窗口开着的时候，Esc 都应该先关
                // 窗口，不叠加弹出暂停菜单——之前这里只特判了"inventory"，商店/仓库/
                // 世界地图这些窗口完全没被照顾到，按Esc会直接跳过关窗口去开暂停菜单，
                // 变成"窗口开着+暂停菜单同时叠在一起"这种设计上不该出现的状态（暂停菜单
                // 的 HideAllGameCanvases() 会把窗口的Canvas一起遮住，关暂停菜单时又只
                // 恢复了部分状态，就是之前商店按钮点击失灵那个bug的根源）。改成通用判断，
                // 只要还有窗口开着就先关（正常情况下同一时间只会有一个），全部关掉之后
                // 再按才轮到暂停菜单。
                if (windowManager != null && windowManager.HasAnyWindowOpen())
                {
                    foreach (string key in windowManager.OpenedWindowKeys)
                        windowManager.Close(key);
                }
                else
                {
                    PauseMenuController.Show();
                }
                lastInputEvent = "Menu";
            }
        }

        if (actionController == null)
            return;

        if (!playerInputEnabled)
        {
            currentMoveInput = Vector2.zero;
            currentRunHeld = false;
            currentSneakHeld = false;
            actionController.SubmitMoveIntent(Vector2.zero, false, false);
            return;
        }

        SkyPrisonInputSettings settings = ResolveInputSettings();
        UpdateLastKnownFacingFromMovement();

        if (settings != null)
        {
            // 有窗口打开时进入菜单态：屏蔽攻击 / 闪避，以及手柄奔跑/潜行键（避免与窗口控制冲突）。
            // 键盘绑定的奔跑/潜行仍然放行（Shift / Ctrl 不与背包操作冲突）。
            bool windowBlocking = SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.AnyBlockingWindowOpen;

            // 移动/奔跑/跳跃单独用一个更窄的判断。背包/角色面板这类悬浮窗口打开时人物应该
            // 照样能跑能跳，只有设置/暂停菜单这种真正把 Time.timeScale 冻成 0 的整屏模态
            // 窗口才需要定住角色——用 timeScale 判断而不是 ExternalBlock，是因为角色面板
            // 现在也用 ExternalBlock 挡自己的攻击/闪避（跟悬浮窗口共用同一个标记），如果
            // 移动也看 ExternalBlock 就会被角色面板自己连带挡住，等于回到了那个回归 bug。
            // timeScale 是否被冻住才是"这窗口是不是真模态"的准确信号：暂停菜单/设置窗口
            // 打开时会主动把 timeScale 设成 0，背包/角色面板完全不碰 timeScale。
            // 商店这类真正的全模态窗口用 metadata.lockGameplayInput=true 标记自己需要
            // 连移动一起冻结（跟背包/角色面板不一样，那两个允许开着照样走）——见
            // SkyPrisonWindowManager_V1.AnyMovementLockingWindowOpen 的注释。
            bool movementBlocking = Time.timeScale <= 0f
                || SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.AnyMovementLockingWindowOpen;
            currentMoveInput = movementBlocking ? Vector2.zero : settings.GetMoveVector();

            currentSneakHeld = windowBlocking
                ? settings.GetActionKeyboardOnly(SkyPrisonInputAction.Sneak)
                : settings.GetAction(SkyPrisonInputAction.Sneak);
            bool rawSprintHeld = windowBlocking
                ? settings.GetActionKeyboardOnly(SkyPrisonInputAction.Sprint)
                : settings.GetAction(SkyPrisonInputAction.Sprint);
            bool sprintDown = settings.GetActionDown(SkyPrisonInputAction.Sprint);
            bool sprintUp = settings.GetActionUp(SkyPrisonInputAction.Sprint);

            if (sprintUp)
            {
                sprintReleasedAfterLastTap = true;
                lastSprintReleaseTime = Time.time;
            }

            if (sprintDown && !windowBlocking)
                HandleSprintPressedAsRunOrDodge(settings);

            currentRunHeld = Time.time < suppressRunUntil ? false : rawSprintHeld;
            if (currentSneakHeld)
                currentRunHeld = false;
            if (movementBlocking)
                currentRunHeld = false;

            actionController.SubmitMoveIntent(currentMoveInput, currentRunHeld, currentSneakHeld);

            if (!movementBlocking && settings.GetActionDown(SkyPrisonInputAction.Jump))
            {
                actionController.RequestJump();
                lastInputEvent = "Jump";
            }

            if (!windowBlocking && settings.directDodgeKeyStillAllowed && settings.GetActionDown(SkyPrisonInputAction.Dodge))
            {
                // 攻击取消闪避：攻击状态下按闪避键不走正常的方向闪避判定，改成按玩家
                // 当前是否按着朝向方向的方向键分别接前闪/后闪(前闪正常速度，后闪固定
                // 沿朝向反方向、速度打折、保持朝向不转身)——是否允许由 WeaponCombatModule.
                // allowAttackCancelDodgeBack + 判定帧是否已经结束(后摇阶段)决定，
                // 都在 TryPlayerRequestAttackCancelDodgeBack 里判断，这里不重复检查。
                if (actionController.IsAttacking)
                {
                    bool canCancelToDodgeBack = combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestAttackCancelDodgeBack();
                    lastInputEvent = canCancelToDodgeBack ? "AttackCancelDodgeBack" : "DodgeKey(BlockedByAttack)";
                }
                else
                {
                    RequestDodgeFromCurrentInput("DodgeKey", settings.noMoveInputDodgeForward);
                }
            }

            // 轻攻击和重攻击默认共享同一个物理键（鼠标左键），单靠GetActionDown没法
            // 区分"点一下"和"按住不放"——两个动作会在同一帧一起触发down。需要先挂起
            // 判定，等一小段时间：期间松开就是点按=轻攻击，撑过阈值还按着就是长按=重
            // 攻击(蓄力)。J/K这种各自独立绑定的键不会同帧一起触发down，走下面的立即
            // 分支，不受这个延迟影响。
            bool lightAttackDown = settings.GetActionDown(SkyPrisonInputAction.LightAttack);
            bool heavyAttackDown = settings.GetActionDown(SkyPrisonInputAction.HeavyAttack);

            // 空中攻击：跳跃空中阶段按任意一个攻击键（不分轻/重）都触发同一个专属空中
            // 攻击技能，跳过下面轻/重攻击的连段选定/长按蓄力判定那一整套逻辑——能不能
            // 出招完全交给 CanEnterAerialAttackPublic() 判断（是否在空中阶段、这次
            // 跳跃是否已经用过），这里不用重复检查。
            if (!windowBlocking && actionController.IsJumping && (lightAttackDown || heavyAttackDown))
            {
                bool canAerialAttack = combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestAerialAttack();
                if (canAerialAttack)
                {
                    actionController.RequestAerialAttack();
                    _bufferedAerialAttackPending = false;
                    lastInputEvent = "AerialAttack";
                }
                else
                {
                    // 大概率是脚刚离地还在Start阶段、还没进入Air阶段——先缓冲住，
                    // 下面 TryConsumeBufferedAerialAttack 会在真正进入Air阶段那一帧
                    // 自动补发，不用玩家精确卡时机再按一次。
                    _bufferedAerialAttackPending = true;
                    _bufferedAerialAttackPressTime = Time.time;
                    lastInputEvent = "AerialAttack(Buffered)";
                }
            }
            // 闪避接突刺：闪避快结束的可打断窗口内按任意一个攻击键（不分轻/重）都触发
            // 同一个专属突刺技能，跳过下面轻/重攻击的连段选定/长按蓄力判定那一整套
            // 逻辑——能不能出招完全交给 CanEnterDodgeThrustPublic() 判断（是否在闪避
            // 状态、是否落在窗口内），这里不用重复检查。
            else if (!windowBlocking && actionController.IsDodging && (lightAttackDown || heavyAttackDown))
            {
                bool canDodgeThrust = combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestDodgeThrust();
                if (canDodgeThrust) actionController.RequestDodgeThrust(combatModuleRuntime.CurrentDodgeThrustOpenAfterFraction);
                lastInputEvent = "DodgeThrust";
            }
            // 奔跑接突刺：奔跑(Sprint)状态下按任意一个攻击键都复用同一个突刺技能，不是
            // 停下来打普通轻/重攻击。TryPlayerRequestRunThrust() 直接写在条件里
            // (短路求值)——如果这把武器没配 dodgeThrustAttack(比如空手)会返回false，
            // 整个条件不成立，自然落到下面轻/重攻击的正常分支，不会吞掉这次输入。
            else if (!windowBlocking && actionController.CurrentLocomotion == UnitActionController.UnitLocomotionMode.Sprint
                     && (lightAttackDown || heavyAttackDown)
                     && combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestRunThrust())
            {
                actionController.RequestRunThrust();
                lastInputEvent = "RunThrust";
            }
            else if (!windowBlocking && lightAttackDown && heavyAttackDown)
            {
                _sharedAttackKeyPending = true;
                _sharedAttackKeyPressTime = Time.time;
            }
            else if (!windowBlocking && lightAttackDown)
            {
                bool canAttack = combatModuleRuntime == null || combatModuleRuntime.TryPlayerRequestLightAttack();
                if (canAttack) actionController.RequestLightAttack();
                lastInputEvent = "LightAttack";
            }
            else if (!windowBlocking && heavyAttackDown)
            {
                bool canAttack = combatModuleRuntime == null || combatModuleRuntime.TryPlayerRequestHeavyAttack();
                if (canAttack) actionController.RequestHeavyAttack();
                lastInputEvent = "HeavyAttack";
            }

            // 空中攻击输入缓冲补发：独立于上面那条 if/else-if 链，每帧都检查一次——
            // 跳跃期间(不要求已经进入Air阶段)持续尝试，一旦 CanEnterAerialAttackPublic()
            // 通过(真正进入Air阶段)就立刻补发；缓冲太久(aerialAttackInputBufferSeconds)
            // 还没等到、或者中途落地/不再跳跃了，就放弃，不会隔很久之后冒出一次奇怪的
            // 攻击。
            if (_bufferedAerialAttackPending)
            {
                if (!actionController.IsJumping || Time.time - _bufferedAerialAttackPressTime > aerialAttackInputBufferSeconds)
                {
                    _bufferedAerialAttackPending = false;
                }
                else if (!windowBlocking && combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestAerialAttack())
                {
                    actionController.RequestAerialAttack();
                    _bufferedAerialAttackPending = false;
                    lastInputEvent = "AerialAttack(BufferedFire)";
                }
            }

            if (_sharedAttackKeyPending)
            {
                bool releasedEarly = settings.GetActionUp(SkyPrisonInputAction.LightAttack) || settings.GetActionUp(SkyPrisonInputAction.HeavyAttack);
                bool stillHeld = settings.GetAction(SkyPrisonInputAction.LightAttack) || settings.GetAction(SkyPrisonInputAction.HeavyAttack);

                if (releasedEarly || !stillHeld)
                {
                    _sharedAttackKeyPending = false;
                    if (!windowBlocking)
                    {
                        bool canAttack = combatModuleRuntime == null || combatModuleRuntime.TryPlayerRequestLightAttack();
                        if (canAttack) actionController.RequestLightAttack();
                        lastInputEvent = "LightAttack";
                    }
                }
                else if (windowBlocking)
                {
                    // 长按判定过程中打开了窗口，直接取消，不触发任何攻击。
                    _sharedAttackKeyPending = false;
                }
                else if (Time.time - _sharedAttackKeyPressTime >= sharedAttackKeyHoldThreshold)
                {
                    _sharedAttackKeyPending = false;
                    bool canAttack = combatModuleRuntime == null || combatModuleRuntime.TryPlayerRequestHeavyAttack();
                    if (canAttack) actionController.RequestHeavyAttack();
                    lastInputEvent = "HeavyAttack";
                }
            }

            // 重攻击键松开 = 蓄力释放。窗口挡着的时候不拦截松开事件，避免按住重攻击键
            // 时打开背包，蓄力状态卡住松不开。
            if (settings.GetActionUp(SkyPrisonInputAction.HeavyAttack))
            {
                combatModuleRuntime?.ReleaseChargeAttack();
            }

            // 换弹（默认R）：能不能换（弹匣未满/背包有备用弹药/当前状态允许）完全交给
            // TryPlayerRequestReload() 判断，这里不重复检查——近战武器/没装备武器时
            // combatModuleRuntime 内部会因为 usesAmmo==false 直接返回false，按了没反应，
            // 不会报错也不会误触发任何东西。
            if (!windowBlocking && settings.GetActionDown(SkyPrisonInputAction.Reload))
            {
                bool didReload = combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestReload();
                lastInputEvent = didReload ? "Reload" : "Reload(Blocked)";
            }

            // 鼠标滚轮切换主/副武器——跟攻击键一样，窗口挡着的时候不响应，避免在背包/
            // 角色面板/设置里滚鼠标滚轮的时候意外把武器切了。加冷却防止快速连续滚动
            // 在同一次物理滚动里触发好几次切换（见 weaponCycleCooldown 字段注释）。
            if (!windowBlocking && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f
                && Time.unscaledTime - _lastWeaponCycleTime >= weaponCycleCooldown)
            {
                _lastWeaponCycleTime = Time.unscaledTime;
                EquipmentRuntime.Instance?.CycleActiveWeapon();
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.WeaponSwitch); // 滚轮"卡槽"音效，独立于菜单导航音
                lastInputEvent = "CycleWeapon";
            }

            // 手柄 D-Pad 左/右 切武器——跟滚轮共用同一个 CycleActiveWeapon 和冷却计时，
            // 但触发方式是"归中态才允许下一次触发"的边沿检测（不是滚轮那种连续帧累计），
            // 按住D-pad不会连续重复切换，一次按压只切一格，天然就有卡槽感，不需要额外
            // 的时间冷却，但仍然复用同一个 _lastWeaponCycleTime 防止跟滚轮同帧撞在一起。
            if (!windowBlocking)
            {
                float dPadH = ReadDPadHorizontalRaw();
                bool dPadPressed = Mathf.Abs(dPadH) >= DPadWeaponCycleThreshold;

                if (!dPadPressed)
                {
                    _dPadAtRest = true;
                }
                else if (_dPadAtRest && Time.unscaledTime - _lastWeaponCycleTime >= weaponCycleCooldown)
                {
                    _dPadAtRest = false;
                    _lastWeaponCycleTime = Time.unscaledTime;
                    EquipmentRuntime.Instance?.CycleActiveWeapon();
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.WeaponSwitch);
                    lastInputEvent = "CycleWeapon";
                }
            }

            return;
        }

        if (!enableFallbackKeysWhenSettingsMissing)
            return;

        ReadFallbackInput();
    }

    public void SetPlayerInputEnabled(bool enabled)
    {
        playerInputEnabled = enabled;
        if (!enabled && actionController != null)
            actionController.SubmitMoveIntent(Vector2.zero, false, false);
    }

    private void ToggleInventory()
    {
        if (windowManager == null || inventoryPrefab == null) return;

        if (windowManager.IsOpen("inventory"))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
            windowManager.Close("inventory");
        }
        else
        {
            // 按快捷键切换是"换到这个窗口"，不是叠加——角色面板开着的话先关掉再开背包。
            // 从角色面板里点装备槽打开背包（CharacterPanelController.OpenInventoryToEquip）
            // 走的是完全独立的另一条路径，不受这里影响，那边就是要两个一起开着。
            if (CharacterPanelController.IsOpen)
                CharacterPanelController.Hide();

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            windowManager.Open(inventoryPrefab);
        }
    }

    private void ReadFallbackInput()
    {
        currentMoveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (currentMoveInput.sqrMagnitude > 1f)
            currentMoveInput.Normalize();

        UpdateLastKnownFacingFromMovement();

        bool sprintHeld = Input.GetKey(fallbackSprintKey) || Input.GetKey(fallbackSprintSecondaryKey);
        bool sprintDown = Input.GetKeyDown(fallbackSprintKey) || Input.GetKeyDown(fallbackSprintSecondaryKey);
        bool sprintUp = Input.GetKeyUp(fallbackSprintKey) || Input.GetKeyUp(fallbackSprintSecondaryKey);

        currentSneakHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);

        if (sprintUp)
        {
            sprintReleasedAfterLastTap = true;
            lastSprintReleaseTime = Time.time;
        }

        if (sprintDown)
            HandleSprintPressedFallback();

        currentRunHeld = Time.time < suppressRunUntil ? false : sprintHeld;
        if (currentSneakHeld)
            currentRunHeld = false;

        actionController.SubmitMoveIntent(currentMoveInput, currentRunHeld, currentSneakHeld);

        bool windowBlockingFallback = SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1.AnyBlockingWindowOpen;

        if (!windowBlockingFallback && Input.GetKeyDown(fallbackJumpKey))
        {
            actionController.RequestJump();
            lastInputEvent = "Jump";
        }

        if (!windowBlockingFallback && Input.GetKeyDown(fallbackDodgeKey))
        {
            RequestDodgeFromCurrentInput("DodgeKey", false);
        }

        if (!windowBlockingFallback && Input.GetKeyDown(fallbackLightAttackKey))
        {
            bool canAttack = combatModuleRuntime == null || combatModuleRuntime.TryPlayerRequestLightAttack();
            if (canAttack) actionController.RequestLightAttack();
            lastInputEvent = "LightAttack";
        }

        if (!windowBlockingFallback && Input.GetKeyDown(fallbackHeavyAttackKey))
        {
            bool canAttack = combatModuleRuntime == null || combatModuleRuntime.TryPlayerRequestHeavyAttack();
            if (canAttack) actionController.RequestHeavyAttack();
            lastInputEvent = "HeavyAttack";
        }

        if (Input.GetKeyUp(fallbackHeavyAttackKey))
        {
            combatModuleRuntime?.ReleaseChargeAttack();
        }

        if (!windowBlockingFallback && Input.GetKeyDown(fallbackReloadKey))
        {
            bool didReload = combatModuleRuntime != null && combatModuleRuntime.TryPlayerRequestReload();
            lastInputEvent = didReload ? "Reload" : "Reload(Blocked)";
        }
    }

    private void HandleSprintPressedAsRunOrDodge(SkyPrisonInputSettings settings)
    {
        if (settings == null || !settings.enableSprintDoubleTapDodge)
        {
            lastSprintTapTime = Time.time;
            sprintReleasedAfterLastTap = false;
            lastInputEvent = "SprintDown";
            return;
        }

        float window = Mathf.Max(0.05f, settings.sprintDoubleTapWindow);
        float minRelease = Mathf.Max(0f, settings.sprintDoubleTapMinReleaseTime);

        bool insideWindow = Time.time - lastSprintTapTime <= window;
        bool releasedLongEnough = sprintReleasedAfterLastTap && Time.time - lastSprintReleaseTime >= minRelease;

        if (insideWindow && releasedLongEnough)
        {
            RequestDodgeFromCurrentInput("SprintDoubleTapDodge", settings.noMoveInputDodgeForward);
            ResetSprintTapState();
            suppressRunUntil = Time.time + Mathf.Max(0f, settings.runSuppressAfterDoubleTapDodge);
            return;
        }

        lastSprintTapTime = Time.time;
        sprintReleasedAfterLastTap = false;
        lastInputEvent = "SprintDown";
    }

    private void HandleSprintPressedFallback()
    {
        const float fallbackWindow = 0.26f;
        const float fallbackMinRelease = 0.03f;
        const float fallbackSuppress = 0.08f;

        bool insideWindow = Time.time - lastSprintTapTime <= fallbackWindow;
        bool releasedLongEnough = sprintReleasedAfterLastTap && Time.time - lastSprintReleaseTime >= fallbackMinRelease;

        if (insideWindow && releasedLongEnough)
        {
            RequestDodgeFromCurrentInput("SprintDoubleTapDodge", false);
            ResetSprintTapState();
            suppressRunUntil = Time.time + fallbackSuppress;
            return;
        }

        lastSprintTapTime = Time.time;
        sprintReleasedAfterLastTap = false;
        lastInputEvent = "SprintDown";
    }

    private void RequestDodgeFromCurrentInput(string source, bool noMoveForward)
    {
        if (actionController == null)
            return;

        ResolveReferences();

        Vector2 facing = ResolveCurrentFacingInput();
        Vector2 inputDir = currentMoveInput.sqrMagnitude > 0.0001f ? currentMoveInput.normalized : Vector2.zero;

        if (inputDir.sqrMagnitude > 0.0001f)
        {
            UnitMovementController.DodgeRuntimeState dodgeState = UnitMovementController.DodgeRuntimeState.Forward;

            if (dodgeRelativeToCurrentFacing && facing.sqrMagnitude > 0.0001f)
            {
                float dot = Vector2.Dot(inputDir, facing.normalized);
                dodgeState = dot <= backDodgeDotThreshold
                    ? UnitMovementController.DodgeRuntimeState.Back
                    : UnitMovementController.DodgeRuntimeState.Forward;
            }
            else
            {
                // Fallback only. The preferred production rule is facing-relative.
                dodgeState = inputDir.y < -0.25f
                    ? UnitMovementController.DodgeRuntimeState.Back
                    : UnitMovementController.DodgeRuntimeState.Forward;
            }

            // 位移方向仍然尊重玩家输入；前/后只决定动作语义和动画 Key。
            // 例：角色面朝左，玩家按右 + 闪避 => 向右位移，但播放/标记为后闪。
            actionController.RequestDodge(inputDir, dodgeState);
            lastInputEvent = source + (dodgeState == UnitMovementController.DodgeRuntimeState.Back ? " BackRelativeToFacing" : " ForwardRelativeToFacing");
            return;
        }

        if (noInputDodgeUsesCurrentFacingForward && facing.sqrMagnitude > 0.0001f)
        {
            actionController.RequestDodge(facing.normalized, UnitMovementController.DodgeRuntimeState.Forward);
            lastInputEvent = source + " ForwardCurrentFacingNoInput";
            return;
        }

        actionController.RequestDodge(noMoveForward);
        lastInputEvent = noMoveForward ? source + " ForwardNoInput" : source + " BackNoInput";
    }

    private void UpdateLastKnownFacingFromMovement()
    {
        if (movementController == null)
            ResolveReferences();

        if (movementController == null)
            return;

        Vector2 facing = movementController.FacingInput;
        if (facing.sqrMagnitude > 0.0001f)
            lastKnownFacingInput = facing.normalized;
    }

    private Vector2 ResolveCurrentFacingInput()
    {
        if (movementController == null)
            ResolveReferences();

        // Current frame / movement-controller facing has highest priority.
        if (movementController != null)
        {
            Vector2 facing = movementController.FacingInput;
            if (facing.sqrMagnitude > 0.0001f)
            {
                lastKnownFacingInput = facing.normalized;
                return lastKnownFacingInput;
            }
        }

        // If the player is pressing a direction right now, use it and remember it.
        if (currentMoveInput.sqrMagnitude > 0.0001f)
        {
            lastKnownFacingInput = currentMoveInput.normalized;
            return lastKnownFacingInput;
        }

        // No input: use the last remembered facing.
        // This fixes: character visually facing left, no direction + dodge incorrectly falls back to Vector2.right.
        if (lastKnownFacingInput.sqrMagnitude > 0.0001f)
            return lastKnownFacingInput.normalized;

        return Vector2.right;
    }

    private void ResetSprintTapState()
    {
        lastSprintTapTime = -999f;
        lastSprintReleaseTime = -999f;
        sprintReleasedAfterLastTap = true;
        suppressRunUntil = -999f;
    }

    private void ResolveReferences()
    {
        if (autoFindActionController && actionController == null)
        {
            actionController = GetComponent<UnitActionController>();
            if (actionController == null)
                actionController = GetComponentInParent<UnitActionController>();
            if (actionController == null)
                actionController = GetComponentInChildren<UnitActionController>(true);
        }

        if (movementController == null)
        {
            movementController = GetComponent<UnitMovementController>();
            if (movementController == null)
                movementController = GetComponentInParent<UnitMovementController>();
            if (movementController == null)
                movementController = GetComponentInChildren<UnitMovementController>(true);
        }

        if (combatModuleRuntime == null)
        {
            combatModuleRuntime = GetComponent<UnitActionModuleRuntime>();
            if (combatModuleRuntime == null)
                combatModuleRuntime = GetComponentInParent<UnitActionModuleRuntime>();
            if (combatModuleRuntime == null)
                combatModuleRuntime = GetComponentInChildren<UnitActionModuleRuntime>(true);
        }

        if (windowManager == null)
            windowManager = FindObjectOfType<SkyPrison.Runtime.UI.SkyPrisonWindowManager_V1>();

        if (inventoryPrefab == null)
            inventoryPrefab = Resources.Load<GameObject>("UI/Window/PF_SkyPrisonInventory");
    }

    private SkyPrisonInputSettings ResolveInputSettings()
    {
        if (!useSkyPrisonInputSettings)
            return null;

        if (inputSettings != null)
        {
            inputSettings.EnsureDefaults();
            return inputSettings;
        }

        // Build/runtime path. The editor build preprocessor mirrors the source asset to any Resources folder
        // as SkyPrisonInputSettings.asset, so this works without hand-dragging references on player prefabs.
        inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");

        // Optional alternate location if someone places it under Resources/Settings later.
        if (inputSettings == null)
            inputSettings = Resources.Load<SkyPrisonInputSettings>("Settings/SkyPrisonInputSettings");

#if UNITY_EDITOR
        // Editor-only authoring fallback. This also serializes the reference onto the scene / prefab instance
        // when possible, but Build must rely on explicit prefab reference or the Resources mirror.
        if (inputSettings == null)
        {
            inputSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(SkyPrisonInputSettings.DefaultAssetPath);
            if (inputSettings != null && !Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        if (inputSettings != null)
            inputSettings.EnsureDefaults();

        return inputSettings;
    }
}


// Compatibility shim for older scenes / scripts that still reference SkyPrisonPlayerInputRouter_V3.
// New code should reference SkyPrisonPlayerInputRouter.
public class SkyPrisonPlayerInputRouter_V3 : SkyPrisonPlayerInputRouter { }
