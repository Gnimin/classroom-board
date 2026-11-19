using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NEWCLASSroom.Models;
using NEWCLASSroom.Services;
using NEWCLASSroom.Views;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace NEWCLASSroom
{
    /// <summary>
    /// 程序入口：单实例守护、托盘、班牌窗口、消息弹窗、空闲全屏都从这里串起来。
    /// </summary>
    public partial class App : Application
    {
        private Mutex _mutex;
        private WinForms.NotifyIcon _tray;
        private BoardWindowBase _board;
        private BoardWindowBase _screensaver;
        private SettingsWindow _settingsWindow;
        private IdleWatcher _idle;
        private DispatcherTimer _idleRetry;
        private bool _showingOverlay;
        private bool _boardVisible = true;
        private WinForms.ToolStripMenuItem _toggleItem;
        private string _appliedMode;
        private int _appliedMonitor = -1;

        public AppState State { get; private set; }

        /// <summary>程序入口：单实例检查、加载数据、创建班牌窗口和托盘。</summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool created;
            _mutex = new Mutex(true, "NEWCLASSroom_Board_SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("班牌已经在运行了，请在右下角托盘图标里操作。", "班级班牌",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) => { };

            State = new AppState();
            _appliedMode = State.Data.Settings.DisplayMode;
            _appliedMonitor = State.Data.Settings.MonitorIndex;

            // 数据版本兼容：老配置文件里没有的字段用默认值补上
            State.StartServices();

            _board = CreateBoard(false);
            _board.Show();

            BuildTray();
            StartIdleWatcher();

            State.Messages.Enqueued += (s, args) => Dispatcher.BeginInvoke(new Action(ShowNextMessage));
            State.SettingsChanged += (s, args) => Dispatcher.BeginInvoke(new Action(ApplySettingsChanged));
        }

        /// <summary>设置改完之后，只处理真正需要重排窗口的那几项。</summary>
        private void ApplySettingsChanged()
        {
            AppSettings settings = State.Data.Settings;
            if (_idle != null) _idle.ThresholdMinutes = settings.IdleMinutes;

            // 关掉无操作全屏之后，正在显示的屏保要马上收回
            if (!settings.EnableIdleFullscreen) HideScreensaver();

            if (settings.DisplayMode != _appliedMode || settings.MonitorIndex != _appliedMonitor)
            {
                _appliedMode = settings.DisplayMode;
                _appliedMonitor = settings.MonitorIndex;
                RebuildBoard();
                Toast("显示方式已切换");
            }
            else if (_board != null && _boardVisible)
            {
                _board.RefreshNow();
            }

            RefreshSettingsWindow();
        }

        /// <summary>创建一个班牌窗口，screenSaver 为真时是全屏屏保版。</summary>
        private BoardWindowBase CreateBoard(bool screenSaver)
        {
            // 右侧停靠用专门排版的窄版界面，其余情况用宽屏界面
            if (!screenSaver && State.Data.Settings.DisplayMode == "RightDocked")
                return new NarrowBoardWindow(State, false);

            return new SignboardWindow(State, screenSaver);
        }

        /// <summary>兜底处理未捕获的界面异常，避免程序直接崩掉。</summary>
        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            try
            {
                MessageBox.Show("班牌遇到一个错误，已记录。\r\n\r\n" + e.Exception.Message,
                    "班级班牌", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        }

        #region 托盘

        /// <summary>创建托盘图标和右键菜单。</summary>
        private void BuildTray()
        {
            _tray = new WinForms.NotifyIcon();
            _tray.Icon = AppIcons.CreateTrayIcon();
            _tray.Text = "班级班牌";
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ToggleBoard();

            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
            menu.Font = new Drawing.Font("Microsoft YaHei UI", 9f);

            WinForms.ToolStripMenuItem header = new WinForms.ToolStripMenuItem(
                (State.Data.Class.ClassName ?? "班级") + " · 班牌");
            header.Enabled = false;
            menu.Items.Add(header);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            WinForms.ToolStripMenuItem toggle = new WinForms.ToolStripMenuItem("收起班牌");
            toggle.Click += (s, e) => ToggleBoard();
            menu.Items.Add(toggle);
            _toggleItem = toggle;

            WinForms.ToolStripMenuItem remount = new WinForms.ToolStripMenuItem("重新挂到桌面");
            remount.Click += (s, e) => RebuildBoard();
            menu.Items.Add(remount);

            WinForms.ToolStripMenuItem settings = new WinForms.ToolStripMenuItem("设置…");
            settings.Click += (s, e) => OpenSettings();
            menu.Items.Add(settings);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            WinForms.ToolStripMenuItem openWeb = new WinForms.ToolStripMenuItem("手机端：" + ShortUrl());
            openWeb.Click += (s, e) => OpenWeb();
            menu.Items.Add(openWeb);

            WinForms.ToolStripMenuItem copyUrl = new WinForms.ToolStripMenuItem("复制手机端地址");
            copyUrl.Click += (s, e) => CopyUrl();
            menu.Items.Add(copyUrl);

            WinForms.ToolStripMenuItem dataDir = new WinForms.ToolStripMenuItem("打开数据目录");
            dataDir.Click += (s, e) => OpenDataDirectory();
            menu.Items.Add(dataDir);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            WinForms.ToolStripMenuItem exit = new WinForms.ToolStripMenuItem("退出");
            exit.Click += (s, e) => QuitApp();
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
        }

        /// <summary>取一段截短后的手机端地址，用于菜单显示。</summary>
        private string ShortUrl()
        {
            string url = State.PreferredUrl;
            if (url == null) return "未启动";
            return url.Length > 44 ? url.Substring(0, 44) + "…" : url;
        }

        /// <summary>切换班牌的显示与收起状态。</summary>
        private void ToggleBoard()
        {
            SetBoardVisible(!_boardVisible);
        }

        /// <summary>显示或收起班牌。收起之后如果开了“无操作全屏”，空闲时班牌会自己全屏出现。</summary>
        public void SetBoardVisible(bool visible)
        {
            if (_board == null) return;

            _boardVisible = visible;
            State.BoardVisible = visible;

            if (visible)
            {
                HideScreensaver();
                _board.ShowBoard();
                Toast("班牌已显示");
            }
            else
            {
                HideScreensaver();
                _board.HideBoard();
                if (State.Data.Settings.EnableIdleFullscreen)
                    NotifyTray("班牌已收起", State.Data.Settings.IdleMinutes + " 分钟无操作会自动全屏显示，动一下鼠标就消失。");
                else
                    NotifyTray("班牌已收起", "右键托盘图标可以随时重新显示。");
            }

            RefreshSettingsWindow();
            UpdateTrayText();
        }

        /// <summary>根据当前状态更新托盘菜单里那一项的文案。</summary>
        private void UpdateTrayText()
        {
            if (_toggleItem == null) return;
            _toggleItem.Text = _boardVisible ? "收起班牌" : "显示班牌";
        }

        /// <summary>设置窗口开着的话，让它刷新一下班牌状态。</summary>
        private void RefreshSettingsWindow()
        {
            if (_settingsWindow != null) _settingsWindow.RefreshBoardState();
        }

        /// <summary>关掉旧窗口重建一个，用于切换显示方式或重新挂载桌面。</summary>
        private void RebuildBoard()
        {
            try
            {
                bool wasVisible = _boardVisible;
                if (_board != null)
                {
                    _board.ClosePermanently();
                    _board = null;
                }
                _board = CreateBoard(false);
                _board.Show();
                if (!wasVisible) _board.HideBoard();
            }
            catch { }
        }

        /// <summary>打开设置窗口，已经开着就切到前面。</summary>
        private void OpenSettings()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(State);
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        /// <summary>用资源管理器打开数据目录。</summary>
        private void OpenDataDirectory()
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", State.Storage.DataDirectory);
            }
            catch { }
        }

        /// <summary>用默认浏览器打开手机端地址。</summary>
        public void OpenWeb()
        {
            try
            {
                System.Diagnostics.Process.Start(State.PreferredUrl);
            }
            catch { }
        }

        /// <summary>把手机端地址复制到剪贴板。</summary>
        public void CopyUrl()
        {
            try
            {
                Clipboard.SetText(State.PreferredUrl);
                Toast("手机端地址已复制");
            }
            catch { }
        }

        /// <summary>在班牌上闪一条短提示。</summary>
        public void Toast(string text)
        {
            if (_board != null) _board.ShowToast(text);
        }

        /// <summary>班牌被藏起来的时候窗口上看不到提示，改用托盘的横幅通知。</summary>
        private void NotifyTray(string title, string text)
        {
            try
            {
                if (_tray != null)
                    _tray.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.Info);
            }
            catch { }
        }

        /// <summary>退出程序：先关窗口、停服务，再结束进程。</summary>
        private void QuitApp()
        {
            try { if (_tray != null) _tray.Visible = false; } catch { }
            try { if (_screensaver != null) { _screensaver.ClosePermanently(); _screensaver = null; } } catch { }
            try { if (_board != null) { _board.ClosePermanently(); _board = null; } } catch { }
            try { State.PersistNow(); } catch { }
            try { State.Dispose(); } catch { }
            Shutdown();
        }

        #endregion

        #region 无操作全屏

        /// <summary>启动空闲检测，用来触发屏保式全屏。</summary>
        private void StartIdleWatcher()
        {
            _idle = new IdleWatcher();
            _idle.ThresholdMinutes = State.Data.Settings.IdleMinutes;
            _idle.Idled += (s, e) => Dispatcher.BeginInvoke(new Action(EnterScreenSaver));
            _idle.Returned += (s, e) => Dispatcher.BeginInvoke(new Action(ExitScreenSaver));
            _idle.Start();
        }

        /// <summary>
        /// 只有班牌被收起时，空闲全屏才有意义——班牌本来就摆在桌面上时不需要再弹一次。
        /// 另外前台要是全屏应用（放视频、PPT 放映），也不能弹，否则正好盖住老师正在讲的内容。
        /// </summary>
        private void EnterScreenSaver()
        {
            if (_boardVisible) return;
            if (!State.Data.Settings.EnableIdleFullscreen) return;
            if (_screensaver != null) return;

            if (FullscreenGuard.ShouldSuppress())
            {
                // 有人在放视频/放课件，先不弹；过几秒再判断一次
                StartIdleRetry();
                return;
            }

            StopIdleRetry();
            try
            {
                _screensaver = CreateBoard(true);
                _screensaver.Show();
            }
            catch { }
        }

        /// <summary>被全屏应用挡住时，启动五秒一次的复查定时器。</summary>
        private void StartIdleRetry()
        {
            if (_idleRetry == null)
            {
                _idleRetry = new DispatcherTimer();
                _idleRetry.Interval = TimeSpan.FromSeconds(5);
                _idleRetry.Tick += (s, e) => EnterScreenSaver();
            }
            if (!_idleRetry.IsEnabled) _idleRetry.Start();
        }

        /// <summary>停掉屏保复查定时器。</summary>
        private void StopIdleRetry()
        {
            if (_idleRetry != null && _idleRetry.IsEnabled) _idleRetry.Stop();
        }

        /// <summary>检测到鼠标键盘就立刻收起来，班牌回到隐藏状态。</summary>
        private void ExitScreenSaver()
        {
            HideScreensaver();
        }

        /// <summary>关闭并释放屏保窗口。</summary>
        private void HideScreensaver()
        {
            StopIdleRetry();
            if (_screensaver == null) return;
            try
            {
                _screensaver.ClosePermanently();
            }
            catch { }
            _screensaver = null;
        }

        #endregion

        #region 消息弹窗

        /// <summary>从队列取一条消息弹出，弹完再取下一题。</summary>
        private void ShowNextMessage()
        {
            if (_showingOverlay) return;
            BannerMessage message = State.Messages.Dequeue();
            if (message == null) return;

            _showingOverlay = true;
            MessageOverlay overlay = new MessageOverlay(message, State);
            overlay.Closed += (s, e) =>
            {
                _showingOverlay = false;
                ShowNextMessage();
            };
            try
            {
                overlay.Show();
            }
            catch
            {
                _showingOverlay = false;
            }
        }

        #endregion

        /// <summary>程序结束前的清理：托盘、服务、互斥量。</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            try { if (_idle != null) _idle.Stop(); } catch { }
            try { if (State != null) State.Dispose(); } catch { }
            try { if (_mutex != null) _mutex.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
