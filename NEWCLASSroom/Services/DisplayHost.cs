using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NEWCLASSroom.Models;

namespace NEWCLASSroom.Services
{
    /// <summary>
    /// 负责把班牌窗口“按”到桌面层，有两种做法：
    /// Wallpaper —— 挂到资源管理器桌面后面的 WorkerW，图标和所有窗口都在它上面，最像真壁纸；
    /// BottomDocked —— 普通窗口但永远压在 Z 序最底、不抢焦点、不进 Alt+Tab，兼容性最好。
    /// </summary>
    public class DisplayHost
    {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;

        private const int WS_CHILD = 0x40000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_POPUP = unchecked((int)0x80000000);

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_UPDATENOW = 0x0100;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);

        private Window _window;
        private IntPtr _hwnd;
        private DispatcherTimer _watchdog;
        private bool _attachToDesktop;
        private bool _active;
        private int _width;
        private int _height;
        private int _x;
        private int _y;

        public string Mode { get; private set; }
        public bool DesktopAttachSucceeded { get; private set; }
        public string LastError { get; private set; }

        /// <summary>默认先按置底窗口模式初始化。</summary>
        public DisplayHost()
        {
            Mode = "BottomDocked";
        }

        /// <summary>取指定显示器的屏幕区域。</summary>
        public static System.Drawing.Rectangle GetMonitorBounds(int index)
        {
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0)
                return new System.Drawing.Rectangle(0, 0, 1920, 1080);
            if (index < 0 || index >= screens.Length) index = 0;
            return screens[index].Bounds;
        }

        /// <summary>按显示模式算出班牌该占用的区域，右侧停靠时只取右半边。</summary>
        public static System.Drawing.Rectangle GetBoardBounds(string mode, int index)
        {
            System.Drawing.Rectangle screen = GetMonitorBounds(index);
            if (mode == "RightDocked")
            {
                int width = screen.Width / 2;
                return new System.Drawing.Rectangle(screen.X + screen.Width - width, screen.Y, width, screen.Height);
            }
            return screen;
        }

        public static int MonitorCount
        {
            get { return System.Windows.Forms.Screen.AllScreens.Length; }
        }

        /// <summary>在窗口句柄就绪、且已经 Show 之后调用。</summary>
        public void Apply(Window window, AppSettings settings)
        {
            _window = window;
            _hwnd = new WindowInteropHelper(window).Handle;

            System.Drawing.Rectangle bounds = GetBoardBounds(settings.DisplayMode, settings.MonitorIndex);
            _x = bounds.X; _y = bounds.Y; _width = bounds.Width; _height = bounds.Height;

            // WindowStyle / ResizeMode 只能在 Show 之前设置，这里的窗口样式已经在 XAML 里定好了
            window.Left = _x;
            window.Top = _y;
            window.Width = _width;
            window.Height = _height;

            _active = true;

            if (settings.DisplayMode == "Wallpaper")
            {
                Mode = "Wallpaper";
                DesktopAttachSucceeded = TryAttachToDesktop();
                if (!DesktopAttachSucceeded)
                {
                    Mode = "BottomDocked";
                    ApplyBottomDocked(true);
                }
            }
            else if (settings.DisplayMode == "RightDocked")
            {
                Mode = "RightDocked";
                ApplyBottomDocked(true);
            }
            else
            {
                Mode = "BottomDocked";
                ApplyBottomDocked(true);
            }

            StartWatchdog();
        }

        /// <summary>暂时收起班牌时调用：停掉看门狗并脱离桌面层，否则桌面上会留下一块黑。</summary>
        public void Suspend()
        {
            _active = false;
            StopWatchdog();
            DetachFromDesktop();
            RefreshDesktop();
        }

        /// <summary>重新显示班牌时调用，按当前模式重新挂回去。</summary>
        public void Resume()
        {
            if (_hwnd == IntPtr.Zero) return;
            _active = true;

            if (Mode == "Wallpaper")
            {
                if (!TryAttachToDesktop())
                {
                    Mode = "BottomDocked";
                    ApplyBottomDocked(false);
                }
            }
            else
            {
                ApplyBottomDocked(false);
            }

            StartWatchdog();
        }

        /// <summary>把窗口设成置底、不抢焦点、不进切换列表。</summary>
        private void ApplyBottomDocked(bool showWindow)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            uint flags = SWP_NOACTIVATE;
            if (showWindow) flags |= SWP_SHOWWINDOW;
            SetWindowPos(_hwnd, HWND_BOTTOM, _x, _y, _width, _height, flags);
        }

        /// <summary>尝试把窗口挂到资源管理器的桌面层里。</summary>
        private bool TryAttachToDesktop()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman == IntPtr.Zero)
                {
                    LastError = "找不到桌面窗口 Progman";
                    return false;
                }

                IntPtr result;
                // 让资源管理器把桌面背景窗口拆成 Progman + WorkerW 两层
                SendMessageTimeout(progman, 0x052C, new IntPtr(0), IntPtr.Zero, 0, 1000, out result);
                SendMessageTimeout(progman, 0x052C, new IntPtr(0x0D), IntPtr.Zero, 0, 1000, out result);
                SendMessageTimeout(progman, 0x052C, new IntPtr(0x0D), new IntPtr(1), 0, 1000, out result);

                IntPtr workerw = IntPtr.Zero;
                EnumWindows((top, param) =>
                {
                    if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    {
                        IntPtr candidate = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                        if (candidate != IntPtr.Zero) workerw = candidate;
                    }
                    return true;
                }, IntPtr.Zero);

                if (workerw == IntPtr.Zero)
                {
                    LastError = "没有找到 WorkerW 层（Windows 版本差异）";
                    return false;
                }

                IntPtr previous = SetParent(_hwnd, workerw);
                if (previous == IntPtr.Zero)
                {
                    LastError = "SetParent 失败";
                    return false;
                }

                int style = GetWindowLong(_hwnd, GWL_STYLE);
                style = (style & ~WS_POPUP) | WS_CHILD | WS_CLIPCHILDREN;
                SetWindowLong(_hwnd, GWL_STYLE, style);
                _attachToDesktop = true;

                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, _width, _height,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        /// <summary>把窗口从桌面层摘出来，恢复成普通窗口。</summary>
        private void DetachFromDesktop()
        {
            if (_hwnd == IntPtr.Zero) return;
            try
            {
                if (!_attachToDesktop) return;

                SetParent(_hwnd, IntPtr.Zero);
                int style = GetWindowLong(_hwnd, GWL_STYLE);
                style = (style & ~WS_CHILD) | WS_POPUP;
                SetWindowLong(_hwnd, GWL_STYLE, style);
                SetWindowPos(_hwnd, IntPtr.Zero, _x, _y, _width, _height,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                _attachToDesktop = false;
            }
            catch { }
        }

        /// <summary>脱离桌面层之后让壁纸重新画一遍，避免桌面上留下黑块。</summary>
        private static void RefreshDesktop()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                {
                    RedrawWindow(progman, IntPtr.Zero, IntPtr.Zero,
                        RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);

                    IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                    {
                        RedrawWindow(defView, IntPtr.Zero, IntPtr.Zero,
                            RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
                    }
                }

                IntPtr worker = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
                if (worker != IntPtr.Zero)
                {
                    RedrawWindow(worker, IntPtr.Zero, IntPtr.Zero,
                        RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
                }
            }
            catch { }
        }

        /// <summary>启动看门狗，防止窗口被别的操作挤出桌面层。</summary>
        private void StartWatchdog()
        {
            StopWatchdog();
            _watchdog = new DispatcherTimer();
            _watchdog.Interval = TimeSpan.FromMilliseconds(900);
            _watchdog.Tick += (s, e) => KeepInPlace();
            _watchdog.Start();
        }

        /// <summary>停掉看门狗定时器。</summary>
        private void StopWatchdog()
        {
            if (_watchdog != null)
            {
                _watchdog.Stop();
                _watchdog = null;
            }
        }

        /// <summary>Win+D 或资源管理器重排之后，把窗口重新按回桌面层。</summary>
        private void KeepInPlace()
        {
            // 用户主动隐藏班牌时绝对不能碰窗口：
            // 一旦把已隐藏的窗口重新显示出来，WPF 并不会重绘，屏幕上就是整块黑。
            if (!_active || _hwnd == IntPtr.Zero) return;

            try
            {
                if (_attachToDesktop)
                {
                    IntPtr parent = GetParent(_hwnd);
                    if (parent == IntPtr.Zero)
                    {
                        _attachToDesktop = false;
                        TryAttachToDesktop();
                    }
                    else
                    {
                        // 已经在桌面层里，只钉住 Z 序，不要反复触发尺寸变化
                        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    }
                }
                else
                {
                    // 压回 Z 序最底，但不要改变位置和尺寸，避免触发重排
                    SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                        SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOMOVE);
                }
            }
            catch { }
        }

        /// <summary>彻底停止：关看门狗、脱离桌面层、刷新桌面。</summary>
        public void Stop()
        {
            _active = false;
            StopWatchdog();
            DetachFromDesktop();
            RefreshDesktop();
        }

        /// <summary>把模式代号翻译成中文名。</summary>
        public static string ModeDisplayName(string mode)
        {
            if (mode == "Wallpaper") return "桌面壁纸层";
            if (mode == "RightDocked") return "右侧停靠";
            return "置底窗口";
        }
    }
}
