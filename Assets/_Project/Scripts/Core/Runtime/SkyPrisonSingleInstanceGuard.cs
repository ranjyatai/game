using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

/// <summary>
/// 防止同时开两份游戏进程。用 Windows 系统级命名 Mutex，在场景加载之前、
/// 尽量早的时机检测——发现已经有一份在跑，直接弹窗提示并退出，不进入任何场景。
/// 只在 Standalone Windows 正式 Build 生效，Editor 里不拦（不然没法同时开
/// 多个 Play 会话/多份工程副本做测试）。
/// </summary>
public static class SkyPrisonSingleInstanceGuard
{
    private const string MutexName = "SkyPrison_SingleInstance_9F3D2C7A-6B1E-4E9A-9D4F-2A1B3C4D5E6F";

    private static Mutex _mutex;

#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0;
    private const uint MB_ICONWARNING = 0x30;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void EnsureSingleInstance()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        bool createdNew;
        try
        {
            _mutex = new Mutex(true, MutexName, out createdNew);
        }
        catch (Exception)
        {
            // Mutex 创建本身失败（极少见，比如系统权限异常）——这道保险不应该反过来把
            // 正常玩家挡在门外，失败就直接放行，当没有这个检测。
            return;
        }

        if (createdNew)
        {
            Application.quitting += ReleaseMutex;
            return;
        }

        // 没抢到 Mutex，说明已经有一份在跑。弹原生 MessageBox 再退出——这个时机比场景加载
        // 还早，Unity 自己的 UI/Canvas 都还没初始化，只能用 Win32 API 直接弹窗。
        MessageBoxW(IntPtr.Zero, "游戏已经在运行了，请先关闭已经打开的窗口。", "Sky Prison", MB_ICONWARNING | MB_OK);

        // Environment.Exit 立即终止进程，不会再继续往后跑任何初始化/加载任何场景。
        Environment.Exit(0);
#endif
    }

    private static void ReleaseMutex()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        if (_mutex == null)
            return;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (Exception)
        {
        }
#endif
    }
}
