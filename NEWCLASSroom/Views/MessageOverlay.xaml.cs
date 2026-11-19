using System;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NEWCLASSroom.Models;
using NEWCLASSroom.Services;

namespace NEWCLASSroom.Views
{
    /// <summary>老师消息的大字弹窗，配合语音播报，到点自动淡出。</summary>
    public partial class MessageOverlay : Window
    {
        private readonly BannerMessage _message;
        private readonly AppState _state;
        private readonly DispatcherTimer _timer;
        private int _remaining;
        private bool _closing;

        /// <summary>准备一条消息的弹窗。</summary>
        public MessageOverlay(BannerMessage message, AppState state)
        {
            InitializeComponent();
            _message = message;
            _state = state;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTickTick;

            Loaded += OnLoaded;
        }

        /// <summary>摆到目标屏幕上并写入消息内容。</summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            System.Drawing.Rectangle bounds = DisplayHost.GetMonitorBounds(_state.Data.Settings.MonitorIndex);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.X;
            Top = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;
            Topmost = true;

            TxtMessage.Text = _message.Text;
            TxtMessage.FontSize = PickFontSize(_message.Text);
            TxtTime.Text = _message.CreatedAt.ToString("HH:mm");
            TxtTime.Visibility = Visibility.Visible;

            if (_message.Urgent)
            {
                TxtBadge.Text = "紧急通知";
                BadgeBox.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x41, 0x3A));
                AckButton.Visibility = Visibility.Visible;
                TxtAutoClose.Text = "这条消息需要点击“知道了”才会关闭";
                TxtAutoClose.FontSize = 20;
            }
            else
            {
                TxtBadge.Text = "老师留言";
                BadgeBox.Background = new SolidColorBrush(Color.FromRgb(0x4D, 0x8D, 0xFF));
                AckButton.Visibility = Visibility.Collapsed;

                AppSettings settings = _state.Data.Settings;
                _remaining = _message.DurationSeconds > 0
                    ? _message.DurationSeconds
                    : Math.Max(5, settings.MessageDurationSeconds);
                UpdateCountdownText();
                _timer.Start();
            }

            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));

            AnnounceAsync();
        }

        /// <summary>播放提示音并朗读消息。</summary>
        private void AnnounceAsync()
        {
            try
            {
                AppSettings settings = _state.Data.Settings;
                if (!settings.SpeechEnabled) return;

                if (_message.Urgent)
                    SystemSounds.Exclamation.Play();
                else
                    SystemSounds.Asterisk.Play();

                _state.Speech.Speak(_message.Text, settings);
            }
            catch { }
        }

        /// <summary>按字数挑一个合适的字号。</summary>
        private static double PickFontSize(string text)
        {
            int length = string.IsNullOrEmpty(text) ? 1 : text.Length;
            if (length <= 6) return 210;
            if (length <= 12) return 160;
            if (length <= 24) return 118;
            if (length <= 48) return 88;
            if (length <= 90) return 68;
            return 54;
        }

        /// <summary>刷新几秒后自动关闭这行提示。</summary>
        private void UpdateCountdownText()
        {
            TxtAutoClose.Text = _remaining + " 秒后自动关闭";
            TxtAutoClose.FontSize = 18;
        }

        /// <summary>每秒递减，归零就关闭。</summary>
        private void OnTickTick(object sender, EventArgs e)
        {
            _remaining--;
            if (_remaining <= 0)
            {
                _timer.Stop();
                CloseSmooth();
                return;
            }
            UpdateCountdownText();
        }

        /// <summary>点知道了之后关闭弹窗。</summary>
        private void OnAckClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseSmooth();
        }

        /// <summary>淡出之后再真正关闭。</summary>
        private void CloseSmooth()
        {
            if (_closing) return;
            _closing = true;
            _timer.Stop();

            DoubleAnimation fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (s, e) =>
            {
                try { Close(); }
                catch { }
            };
            BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>关闭时停掉计时和朗读。</summary>
        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            try { _state.Speech.Stop(); } catch { }
            base.OnClosed(e);
        }
    }
}
