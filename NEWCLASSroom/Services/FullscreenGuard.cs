using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NEWCLASSroom.Services
{
    /// <summary>
    /// 判断前台是不是有全屏应用（放视频、放 PPT 放映、全屏游戏）。
    /// 有的话就不要弹屏保班牌，否则正好把老师正在讲的内容盖住。
    /// </summary>
    public static class FullscreenGuard
    {
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // SHQueryUserNotificationState 的返回值
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        /// <summary>true 表示前台有全屏应用，此时不该弹屏保。</summary>
        public static bool ShouldSuppress()
        {
            try
            {
                // 系统自己知道得更准：全屏 D3D 应用、PPT 放映都会体现在这个状态里
                int state;
                if (SHQueryUserNotificationState(out state) == 0)
                {
                    if (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE)
                        return true;
                }
            }
            catch { }

            return IsForegroundWindowFullscreen();
        }

        /// <summary>兜底：前台窗口是否严丝合缝地盖住了一整块显示器。最大化窗口不算。</summary>
        private static bool IsForegroundWindowFullscreen()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;

                // 桌面本身不是“全屏应用”
                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                string cls = className.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                    return false;

                // 自己程序开的窗口不算
                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId == (uint)Process.GetCurrentProcess().Id) return false;

                RECT rect;
                if (!GetWindowRect(hwnd, out rect)) return false;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;

                MONITORINFO info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref info)) return false;

                RECT screen = info.rcMonitor;
                bool covers = rect.Left <= screen.Left + 1 &&
                              rect.Top <= screen.Top + 1 &&
                              rect.Right >= screen.Right - 1 &&
                              rect.Bottom >= screen.Bottom - 1;
                return covers;
            }
            catch
            {
                return false;
            }
        }
    }
}
