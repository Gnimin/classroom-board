using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using NEWCLASSroom.Models;
using NEWCLASSroom.Services;
using WinForms = System.Windows.Forms;

namespace NEWCLASSroom.Views
{
    /// <summary>电脑端的设置窗口。日常改课表、值日走手机端，这里主要管显示和网络。</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppState _state;
        private List<System.Speech.Synthesis.VoiceInfo> _voices;

        /// <summary>绑定数据并订阅隧道状态变化。</summary>
        public SettingsWindow(AppState state)
        {
            InitializeComponent();
            _state = state;
            Loaded += OnLoaded_;

            _state.Tunnel.Changed += OnTunnelChanged;
            Closed += (s, e) => { _state.Tunnel.Changed -= OnTunnelChanged; };
        }

        /// <summary>把当前设置填进各个控件。</summary>
        private void OnLoaded_(object sender, RoutedEventArgs e)
        {
            AppSettings settings = _state.Data.Settings;

            CmbDisplayMode.SelectedIndex = DisplayModeToIndex(settings.DisplayMode);
            CmbTheme.SelectedIndex = settings.Theme == "Light" ? 1 : 0;

            CmbMonitor.Items.Clear();
            WinForms.Screen[] screens = WinForms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                string label = (i + 1) + ". " + screens[i].DeviceName + "  " +
                               screens[i].Bounds.Width + "×" + screens[i].Bounds.Height +
                               (screens[i].Primary ? "（主屏）" : "");
                CmbMonitor.Items.Add(label);
            }
            int monitorIndex = settings.MonitorIndex;
            if (monitorIndex < 0 || monitorIndex >= CmbMonitor.Items.Count) monitorIndex = 0;
            CmbMonitor.SelectedIndex = monitorIndex;

            ChkIdle.IsChecked = settings.EnableIdleFullscreen;
            TxtIdleMinutes.Text = settings.IdleMinutes.ToString();
            ChkAutoStart.IsChecked = settings.AutoStart || AutoStart.IsEnabled();

            TxtSchool.Text = _state.Data.Class.SchoolName;
            TxtClass.Text = _state.Data.Class.ClassName;

            TxtDuration.Text = settings.MessageDurationSeconds.ToString();
            ChkSpeech.IsChecked = settings.SpeechEnabled;

            _voices = SpeechService.InstalledVoices();
            CmbVoice.Items.Clear();
            CmbVoice.Items.Add("（自动选择中文语音）");
            int selectedVoice = 0;
            for (int i = 0; i < _voices.Count; i++)
            {
                string culture = _voices[i].Culture != null ? _voices[i].Culture.DisplayName : "";
                CmbVoice.Items.Add(_voices[i].Name + "  ·  " + culture);
                if (_voices[i].Name == settings.SpeechVoice) selectedVoice = i + 1;
            }
            CmbVoice.SelectedIndex = selectedVoice;

            TxtRate.Text = settings.SpeechRate.ToString();
            TxtVolume.Text = settings.SpeechVolume.ToString();

            if (!_state.Speech.IsAvailable)
                TxtVoiceStatus.Text = "当前系统无法初始化语音引擎：" + _state.Speech.LastError;
            else if (_voices.Count == 0)
                TxtVoiceStatus.Text = "系统里没有可用的语音包，可以到「设置 → 时间和语言 → 语音」里安装中文语音。";
            else
                TxtVoiceStatus.Text = "已找到 " + _voices.Count + " 个语音包。中文语音一般叫 Huihui 或 Kangkai。";

            TxtPort.Text = settings.Port.ToString();
            TxtPin.Text = settings.Pin;
            TxtTunnelCmd.Text = settings.TunnelCommand;
            TxtManualUrl.Text = settings.PublicUrl;
            ChkTunnelAuto.IsChecked = settings.TunnelAutoStart;

            TxtDisplayStatus.Text = "当前显示方式：" + DisplayHost.ModeDisplayName(settings.DisplayMode) +
                                    "；显示器数量：" + screens.Length + " 块";

            RefreshNetworkInfo();
            RefreshTunnelInfo();
            RefreshBoardState();
        }

        /// <summary>“无操作全屏”只在班牌收起时才有意义，没收起就不让改。</summary>
        public void RefreshBoardState()
        {
            bool hidden = !_state.BoardVisible;

            ChkIdle.IsEnabled = hidden;
            TxtIdleMinutes.IsEnabled = hidden;
            BtnToggleBoard.Content = hidden ? "显示班牌" : "收起班牌";

            if (hidden)
                TxtIdleHint.Text = "班牌已收起，这项设置在生效中。";
            else
                TxtIdleHint.Text = "班牌正摆在桌面上，先收起来才能设置。收起后空闲时才自动全屏。";
        }

        /// <summary>点击按钮收起或显示班牌。</summary>
        private void OnToggleBoard(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            if (app != null) app.SetBoardVisible(!_state.BoardVisible);
            RefreshBoardState();
        }

        /// <summary>刷新局域网地址、服务状态和二维码。</summary>
        private void RefreshNetworkInfo()
        {
            TxtLanUrl.Text = _state.LanUrl;
            TxtPublicUrl.Text = string.IsNullOrWhiteSpace(_state.PublicUrl) ? "（未开启外网隧道）" : _state.PublicUrl;

            TxtServerStatus.Text = _state.ServerStarted
                ? "手机端服务已启动，端口 " + _state.Data.Settings.Port + "。手机连同一个 WiFi，扫码即可。"
                : "手机端服务没有启动：" + (_state.Server.LastError ?? "未知原因");

            string url = _state.PreferredUrl;
            BitmapSource qr = QrService.Create(url, 6);
            ImgQr.Source = qr;
            if (qr == null) ImgQr.ToolTip = "二维码生成失败，可手动输入 " + url;
        }

        /// <summary>刷新隧道状态、日志和公网地址。</summary>
        private void RefreshTunnelInfo()
        {
            TunnelHost tunnel = _state.Tunnel;
            if (tunnel.IsRunning)
            {
                TxtTunnelState.Text = string.IsNullOrWhiteSpace(tunnel.PublicUrl)
                    ? "隧道已启动，正在等待公网地址…"
                    : "隧道运行中，公网地址：" + tunnel.PublicUrl;
            }
            else if (!string.IsNullOrWhiteSpace(tunnel.LastError))
            {
                TxtTunnelState.Text = "隧道未运行：" + tunnel.LastError;
            }
            else
            {
                TxtTunnelState.Text = "隧道未运行。";
            }

            List<string> log = tunnel.Log;
            int start = Math.Max(0, log.Count - 40);
            List<string> tail = log.GetRange(start, log.Count - start);
            TxtTunnelLog.Text = string.Join(Environment.NewLine, tail);
            TxtTunnelLog.ScrollToEnd();

            TxtPublicUrl.Text = string.IsNullOrWhiteSpace(_state.PublicUrl) ? "（未开启外网隧道）" : _state.PublicUrl;
            string url = _state.PublicUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                BitmapSource qr = QrService.Create(url, 6);
                if (qr != null) ImgQr.Source = qr;
            }
        }

        /// <summary>隧道状态变化时回到界面线程刷新。</summary>
        private void OnTunnelChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshTunnelInfo));
        }

        /// <summary>校验并把界面上的值写回设置。</summary>
        private void OnSave(object sender, RoutedEventArgs e)
        {
            AppSettings settings = _state.Data.Settings;
            int oldPort = settings.Port;
            string oldMode = settings.DisplayMode;
            int oldMonitor = settings.MonitorIndex;

            settings.DisplayMode = IndexToDisplayMode(CmbDisplayMode.SelectedIndex);
            settings.Theme = CmbTheme.SelectedIndex == 1 ? "Light" : "Dark";
            settings.MonitorIndex = Math.Max(0, CmbMonitor.SelectedIndex);
            settings.EnableIdleFullscreen = ChkIdle.IsChecked == true;
            settings.IdleMinutes = ParseInt(TxtIdleMinutes.Text, 5, 1, 600);
            settings.AutoStart = ChkAutoStart.IsChecked == true;

            _state.Data.Class.SchoolName = TxtSchool.Text.Trim();
            _state.Data.Class.ClassName = TxtClass.Text.Trim();

            settings.MessageDurationSeconds = ParseInt(TxtDuration.Text, 15, 3, 600);
            settings.SpeechEnabled = ChkSpeech.IsChecked == true;
            settings.SpeechVoice = CmbVoice.SelectedIndex <= 0 ? "" :
                (_voices != null && CmbVoice.SelectedIndex - 1 < _voices.Count
                    ? _voices[CmbVoice.SelectedIndex - 1].Name : "");
            settings.SpeechRate = ParseInt(TxtRate.Text, 0, -10, 10);
            settings.SpeechVolume = ParseInt(TxtVolume.Text, 100, 0, 100);

            settings.Port = ParseInt(TxtPort.Text, 8420, 1024, 65535);
            settings.Pin = TxtPin.Text.Trim();
            settings.TunnelCommand = TxtTunnelCmd.Text.Trim();
            settings.TunnelAutoStart = ChkTunnelAuto.IsChecked == true;
            settings.PublicUrl = NormalizeUrl(TxtManualUrl.Text);
            TxtManualUrl.Text = settings.PublicUrl;

            bool autoStartOk = AutoStart.Set(settings.AutoStart);

            _state.PersistNow();

            if (settings.Port != oldPort)
            {
                _state.RestartServer();
            }

            _state.NotifySettingsChanged();

            RefreshNetworkInfo();
            TxtDisplayStatus.Text = "当前显示方式：" + DisplayHost.ModeDisplayName(settings.DisplayMode);

            if (!autoStartOk && settings.AutoStart)
                TxtSaveHint.Text = "设置已保存，但开机自启写入失败（可能被安全软件拦截）";
            else if (settings.DisplayMode != oldMode || settings.MonitorIndex != oldMonitor)
                TxtSaveHint.Text = "已保存，班牌正在按新的显示方式重新摆放";
            else
                TxtSaveHint.Text = "已保存";
        }

        /// <summary>把显示模式代号换算成下拉框的序号。</summary>
        private static int DisplayModeToIndex(string mode)
        {
            if (mode == "RightDocked") return 1;
            if (mode == "Wallpaper") return 2;
            return 0;
        }

        /// <summary>把下拉框的序号换算成显示模式代号。</summary>
        private static string IndexToDisplayMode(int index)
        {
            if (index == 1) return "RightDocked";
            if (index == 2) return "Wallpaper";
            return "BottomDocked";
        }

        /// <summary>把文本框内容转成限定范围的整数。</summary>
        private static int ParseInt(string text, int fallback, int min, int max)
        {
            int value;
            if (!int.TryParse((text ?? "").Trim(), out value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>补上协议头，否则二维码扫出来会被手机当成一段文字而不是网址。</summary>
        private static string NormalizeUrl(string raw)
        {
            string value = (raw ?? "").Trim();
            if (value.Length == 0) return "";
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return value.TrimEnd('/');
            return ("http://" + value).TrimEnd('/');
        }

        /// <summary>发一条测试消息看看弹窗效果。</summary>
        private void OnTestMessage(object sender, RoutedEventArgs e)
        {
            BannerMessage message = new BannerMessage();
            message.Text = "测试消息：李明同学，请到办公室来一下";
            message.DurationSeconds = ParseInt(TxtDuration.Text, 15, 3, 600);
            message.Source = "本机";
            _state.PushMessage(message);
        }

        /// <summary>用当前的语音参数试听一句。</summary>
        private void OnTestVoice(object sender, RoutedEventArgs e)
        {
            AppSettings settings = _state.Data.Settings;
            AppSettings temp = new AppSettings();
            temp.SpeechEnabled = true;
            temp.SpeechRate = ParseInt(TxtRate.Text, 0, -10, 10);
            temp.SpeechVolume = ParseInt(TxtVolume.Text, 100, 0, 100);
            temp.SpeechVoice = CmbVoice.SelectedIndex <= 0 ? "" :
                (_voices != null && CmbVoice.SelectedIndex - 1 < _voices.Count
                    ? _voices[CmbVoice.SelectedIndex - 1].Name : "");

            _state.Speech.Speak("现在播放一条班级通知，请同学们回到座位。", temp);
            TxtVoiceStatus.Text = "已发送试听，如果没声音请检查系统音量或语音包。";
            GC.KeepAlive(settings);
        }

        /// <summary>用浏览器打开手机端地址。</summary>
        private void OnOpenWeb(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(_state.PreferredUrl);
            }
            catch (Exception ex)
            {
                TxtServerStatus.Text = "打不开浏览器：" + ex.Message;
            }
        }

        /// <summary>复制手机端地址到剪贴板。</summary>
        private void OnCopyUrl(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_state.PreferredUrl);
                TxtServerStatus.Text = "地址已复制到剪贴板：" + _state.PreferredUrl;
            }
            catch { }
        }

        /// <summary>用填写的命令启动隧道。</summary>
        private void OnTunnelStart(object sender, RoutedEventArgs e)
        {
            string command = TxtTunnelCmd.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                TxtTunnelState.Text = "请先填写启动命令。示例：cloudflared tunnel --url http://127.0.0.1:{port}";
                return;
            }
            _state.Data.Settings.TunnelCommand = command;
            _state.Save();
            _state.Tunnel.Start(command, _state.Data.Settings.Port);
            RefreshTunnelInfo();
        }

        /// <summary>停止正在运行的隧道。</summary>
        private void OnTunnelStop(object sender, RoutedEventArgs e)
        {
            _state.Tunnel.Stop();
            RefreshTunnelInfo();
        }

        /// <summary>把启动命令恢复成默认的便携路径。</summary>
        private void OnTunnelDefault(object sender, RoutedEventArgs e)
        {
            TxtTunnelCmd.Text = AppSettings.DefaultTunnelCommand;
            TxtTunnelState.Text = "已恢复默认命令，指向软件目录下的 frp 文件夹。";
        }

        /// <summary>打开数据目录。</summary>
        private void OnOpenData(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", _state.Storage.DataDirectory);
            }
            catch { }
        }

        /// <summary>关闭设置窗口。</summary>
        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
