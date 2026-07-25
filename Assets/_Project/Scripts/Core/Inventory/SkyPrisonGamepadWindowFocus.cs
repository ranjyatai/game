using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 背包和仓库可以同时打开（"转移物品"双窗并排），但各自的手柄输入组件
    /// （SkyPrisonInventoryGamepad / StashInventoryGamepad）之前是完全独立、各管各的——
    /// 两个窗口都开着时，十字键会同时挪动两边的焦点格、A会同时抓取两边的物品，
    /// 用户反馈"两边光标同时在起作用"。加一个Y键切换"当前手柄操作的是哪个窗口"，
    /// 没被选中的那个窗口暂停响应导航/操作输入（但保留自己的高亮显示逻辑复位）。
    /// 只开一个窗口时不受影响，永远算自己是活跃的。
    /// </summary>
    public static class SkyPrisonGamepadWindowFocus
    {
        public enum Target { Inventory, Stash }

        // 之前先后用过 JoystickButton3(Y，跟背包热键冲突) 和 JoystickButton8——用户
        // 指出 JoystickButton8 在PS4原生模式下就是 Share 键，而 InventoryItemDetailController
        // 的"显示详情"本来就用这同一个 Share 键(_btnBack)，两个功能撞在一起，导致
        // 按Share既切焦点又弹详情。换成 JoystickButton9(PS4原生=Options/Xbox=R3)——
        // 项目里只有"潜行"这个纯玩法动作绑在这个键上，菜单打开时玩法输入本来就该被
        // 屏蔽，不会跟"仅双窗口都打开时才生效"的切换焦点冲突，也不跟详情/批量送仓库
        // 这些手柄键重叠。
        private const KeyCode ToggleKey = KeyCode.JoystickButton9;

        private static Target _active = Target.Inventory;
        private static int _lastToggleFrame = -1;

        /// <summary>
        /// otherWindowOpen：另一个窗口现在是不是也开着。两个组件同一帧都会调用这个方法，
        /// 用 frame 号守卫保证同一帧内切换判定只真正执行一次，不会因为两边都检测到按键
        /// 就来回切两次抵消掉。
        /// </summary>
        public static bool IsActive(Target self, bool otherWindowOpen)
        {
            if (!otherWindowOpen)
                return true;

            if (_lastToggleFrame != Time.frameCount && Input.GetKeyDown(ToggleKey))
            {
                _lastToggleFrame = Time.frameCount;
                _active = _active == Target.Inventory ? Target.Stash : Target.Inventory;
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }

            return _active == self;
        }
    }
}
