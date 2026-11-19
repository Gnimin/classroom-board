using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NEWCLASSroom.Models;
using NEWCLASSroom.Services;

namespace NEWCLASSroom.Views
{
    /// <summary>
    /// 班牌窗口的公共逻辑：刷新数据、切换主题、挂到桌面层、屏保和收起。
    /// 具体的排版交给两个派生类——宽屏用 SignboardWindow，右侧停靠用 NarrowBoardWindow，
    /// 两边 XAML 各自按自己的形状设计，这里只管把数据填进去。
    /// 构造函数是 protected，等于只能被派生类使用；不标 abstract 是为了让 XAML 能把它当作根元素。
    /// </summary>
    public class BoardWindowBase : Window
    {
        // 由派生类在 InitializeComponent 之后调用 BindElements 绑定
        protected Grid CanvasRoot;
        protected Grid ContentRoot;
        protected Border StatusCard;
        protected Border TimetableCard;

        protected TextBlock TxtSchool;
        protected TextBlock TxtClass;
        protected TextBlock TxtDate;
        protected TextBlock TxtWeekday;
        protected TextBlock TxtClock;
        protected TextBlock TxtSeconds;
        protected TextBlock TxtAmPm;
        protected Border BadgeBox;
        protected TextBlock TxtBadge;
        protected TextBlock TxtSub;
        protected TextBlock TxtHeadline;
        protected TextBlock TxtCountdown;
        protected Border ProgressTrack;
        protected Border ProgressFill;
        protected StackPanel TimetableRows;
        protected TextBlock TxtTimetableHint;
        protected TextBlock TxtDutyTitle;
        protected TextBlock TxtDutyDay;
        protected StackPanel DutyPanel;
        protected TextBlock TxtLanUrl;
        protected TextBlock TxtModeHint;
        protected Border ToastBox;
        protected TextBlock TxtToast;

        private readonly AppState _state;
        private readonly bool _screenSaver;
        private readonly bool _narrow;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _toastTimer;
        private DisplayHost _display;
        private bool _dirty = true;
        private int _lastRowCount = -1;
        private string _lastCurrentId = "";
        private string _themeId = "";
        private bool _closingForReal;

        // 这些都跟着主题走，切换主题时整体重建
        private Brush _brushTextMain;
        private Brush _brushTextMuted;
        private Brush _brushAccent;
        private Brush _brushCurrentRow;
        private Brush _brushCurrentRowText;
        private Brush _brushChip;
        private Brush _badgeLesson;
        private Brush _badgeBreak;
        private Brush _badgeLunch;
        private Brush _badgeEnd;

        protected BoardWindowBase(AppState state, bool screenSaver, bool narrow)
        {
            _state = state;
            _screenSaver = screenSaver;
            _narrow = narrow;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Tick += (s, e) => Render();

            _toastTimer = new DispatcherTimer();
            _toastTimer.Interval = TimeSpan.FromSeconds(2.4);
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                FadeToast(0);
            };
        }

        /// <summary>派生类在 InitializeComponent 之后调用一次，把控件和定时器都挂上。</summary>
        protected void Initialize()
        {
            BindElements();
            ApplyTheme(BoardTheme.Get(_state.Data.Settings.Theme));

            Loaded += OnLoaded;
            Closing += OnClosing;
            _state.DataChanged += OnDataChanged;
        }

        /// <summary>按名字取控件，派生类的 XAML 决定它们长什么样、摆在哪里。</summary>
        private T Find<T>(string name) where T : class
        {
            return FindName(name) as T;
        }

        /// <summary>把 XAML 里声明过的控件抓到字段里，找不到的保持为空，用的时候要判空。</summary>
        protected virtual void BindElements()
        {
            CanvasRoot = Find<Grid>("CanvasRoot");
            ContentRoot = Find<Grid>("ContentRoot");
            StatusCard = Find<Border>("StatusCard");
            TimetableCard = Find<Border>("TimetableCard");

            TxtSchool = Find<TextBlock>("TxtSchool");
            TxtClass = Find<TextBlock>("TxtClass");
            TxtDate = Find<TextBlock>("TxtDate");
            TxtWeekday = Find<TextBlock>("TxtWeekday");
            TxtClock = Find<TextBlock>("TxtClock");
            TxtSeconds = Find<TextBlock>("TxtSeconds");
            TxtAmPm = Find<TextBlock>("TxtAmPm");
            BadgeBox = Find<Border>("BadgeBox");
            TxtBadge = Find<TextBlock>("TxtBadge");
            TxtSub = Find<TextBlock>("TxtSub");
            TxtHeadline = Find<TextBlock>("TxtHeadline");
            TxtCountdown = Find<TextBlock>("TxtCountdown");
            ProgressTrack = Find<Border>("ProgressTrack");
            ProgressFill = Find<Border>("ProgressFill");
            TimetableRows = Find<StackPanel>("TimetableRows");
            TxtTimetableHint = Find<TextBlock>("TxtTimetableHint");
            TxtDutyTitle = Find<TextBlock>("TxtDutyTitle");
            TxtDutyDay = Find<TextBlock>("TxtDutyDay");
            DutyPanel = Find<StackPanel>("DutyPanel");
            TxtLanUrl = Find<TextBlock>("TxtLanUrl");
            TxtModeHint = Find<TextBlock>("TxtModeHint");
            ToastBox = Find<Border>("ToastBox");
            TxtToast = Find<TextBlock>("TxtToast");
        }

        /// <summary>数据变了，下次刷新时重建课表。</summary>
        private void OnDataChanged(object sender, EventArgs e)
        {
            _dirty = true;
        }

        #region 主题

        /// <summary>由一个颜色生成冻结的纯色画刷。</summary>
        private static SolidColorBrush Solid(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>生成斜向渐变画刷。</summary>
        private static LinearGradientBrush Gradient(Color top, Color mid, Color bottom)
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(top, 0));
            brush.GradientStops.Add(new GradientStop(mid, 0.55));
            brush.GradientStops.Add(new GradientStop(bottom, 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>生成纵向渐变画刷。</summary>
        private static LinearGradientBrush VerticalGradient(Color top, Color bottom)
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(0, 1);
            brush.GradientStops.Add(new GradientStop(top, 0));
            brush.GradientStops.Add(new GradientStop(bottom, 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>生成横向渐变画刷。</summary>
        private static LinearGradientBrush HorizontalGradient(Color start, Color end)
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 0);
            brush.GradientStops.Add(new GradientStop(start, 0));
            brush.GradientStops.Add(new GradientStop(end, 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>把整套配色写进资源字典，界面立刻跟着变。</summary>
        private void ApplyTheme(BoardTheme theme)
        {
            _themeId = theme.Id;

            Resources["WindowBackgroundBrush"] = Solid(theme.WindowBackground);
            Resources["BoardBackground"] = Gradient(theme.BackgroundTop, theme.BackgroundMid, theme.BackgroundBottom);
            Resources["CardBrush"] = VerticalGradient(theme.CardTop, theme.CardBottom);
            Resources["AccentBrush"] = HorizontalGradient(theme.AccentStart, theme.AccentEnd);
            Resources["TextPrimary"] = Solid(theme.TextPrimary);
            Resources["TextMuted"] = Solid(theme.TextMuted);
            Resources["TextFaint"] = Solid(theme.TextFaint);
            Resources["CardBorder"] = Solid(theme.CardBorder);
            Resources["TrackBrush"] = Solid(theme.Track);
            Resources["ToastBrush"] = Solid(theme.Toast);
            Resources["BadgeBrush"] = Solid(theme.BadgeLesson);

            _brushTextMain = Solid(theme.TextPrimary);
            _brushTextMuted = Solid(theme.TextMuted);
            _brushAccent = Solid(theme.AccentStart);
            _brushCurrentRow = Solid(theme.CurrentRow);
            _brushCurrentRowText = Solid(theme.CurrentRowText);
            _brushChip = Solid(theme.ChipBackground);
            _badgeLesson = Solid(theme.BadgeLesson);
            _badgeBreak = Solid(theme.BadgeBreak);
            _badgeLunch = Solid(theme.BadgeLunch);
            _badgeEnd = Solid(theme.BadgeEnd);
        }

        /// <summary>设置里的主题变了就重新套用一次。</summary>
        private void SyncTheme()
        {
            string wanted = _state.Data.Settings.Theme;
            if (string.IsNullOrEmpty(wanted)) wanted = "Dark";
            if (string.Equals(wanted, _themeId, StringComparison.OrdinalIgnoreCase)) return;

            ApplyTheme(BoardTheme.Get(wanted));
            _dirty = true;
        }

        #endregion

        /// <summary>摆到目标屏幕并挂进桌面层。</summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            System.Drawing.Rectangle bounds = DisplayHost.GetBoardBounds(
                _state.Data.Settings.DisplayMode, _state.Data.Settings.MonitorIndex);

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.X;
            Top = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;

            if (_screenSaver)
            {
                Topmost = true;
                Cursor = System.Windows.Input.Cursors.None;
                Activate();
            }
            else
            {
                _display = new DisplayHost();
                _display.Apply(this, _state.Data.Settings);
                if (!string.IsNullOrEmpty(_display.LastError) && _display.Mode == "BottomDocked"
                    && _state.Data.Settings.DisplayMode == "Wallpaper")
                {
                    ShowToast("壁纸层挂载失败，已退回置底模式");
                }
            }

            Render();
            _timer.Start();
        }

        /// <summary>拦截关闭，改成收起。</summary>
        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closingForReal) return;
            e.Cancel = true;
            HideBoard();
        }

        /// <summary>班牌当前是否被用户收起了。</summary>
        public bool IsHidden { get; private set; }

        /// <summary>
        /// 收起班牌。必须先停掉看门狗并把窗口从桌面层摘出来，
        /// 否则看门狗会把已经隐藏的窗口重新显示出来，屏幕上就是一整块黑。
        /// </summary>
        public void HideBoard()
        {
            if (IsHidden) return;
            IsHidden = true;
            _timer.Stop();

            Hide();
            if (_display != null) _display.Suspend();
        }

        /// <summary>重新显示班牌并恢复桌面层。</summary>
        public void ShowBoard()
        {
            if (!IsHidden)
            {
                Show();
                return;
            }

            IsHidden = false;
            Show();
            if (_display != null) _display.Resume();
            _timer.Start();
            Render();
        }

        /// <summary>真正销毁窗口。</summary>
        public void ClosePermanently()
        {
            _closingForReal = true;
            _timer.Stop();
            _toastTimer.Stop();
            _state.DataChanged -= OnDataChanged;
            if (_display != null)
            {
                _display.Stop();
                _display = null;
            }
            Close();
        }

        /// <summary>刷新时钟、状态卡、课表和值日。</summary>
        private void Render()
        {
            SyncTheme();

            DateTime now = DateTime.Now;
            BoardSnapshot snap = _state.CurrentSnapshot();
            AppData data = _state.Data;

            SetText(TxtSchool, data.Class.SchoolName ?? "");
            SetText(TxtClass, data.Class.ClassName ?? "");
            SetText(TxtDate, now.ToString("yyyy年M月d日"));
            SetText(TxtWeekday, ScheduleEngine.FullDayName((int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek));

            SetText(TxtClock, now.ToString("HH:mm"));
            SetText(TxtSeconds, ":" + now.ToString("ss"));
            SetText(TxtAmPm, now.Hour < 12 ? "上午" : (now.Hour < 18 ? "下午" : "晚上"));
            if (TxtSeconds != null)
                TxtSeconds.Visibility = data.Settings.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;

            SetText(TxtBadge, snap.BadgeText);
            SetText(TxtHeadline, snap.HeadlineText);
            SetText(TxtSub, snap.SubText);
            SetText(TxtCountdown, snap.CountdownText);

            if (BadgeBox != null) BadgeBox.Background = BadgeBrushFor(snap.State);
            HeadlineSize(snap.HeadlineText);

            if (ProgressTrack != null)
            {
                ProgressTrack.Visibility = snap.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
                if (snap.ShowProgress && ProgressFill != null)
                {
                    double trackWidth = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : 520;
                    ProgressFill.Width = Math.Max(0, Math.Min(1, snap.Progress)) * trackWidth;
                }
            }

            SetText(TxtDutyDay, ScheduleEngine.FullDayName((int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek));
            RenderDuty(snap.DutyMembers);

            string currentId = snap.CurrentPeriod != null ? snap.CurrentPeriod.Id : "-";
            if (_dirty || snap.Rows.Count != _lastRowCount || currentId != _lastCurrentId)
            {
                RenderTimetable(snap.Rows);
                _dirty = false;
                _lastRowCount = snap.Rows.Count;
                _lastCurrentId = currentId;
            }

            if (_screenSaver) SetText(TxtLanUrl, "屏保模式 · 移动鼠标即可退出");
            else SetText(TxtLanUrl, _state.ServerStarted ? "手机端 " + _state.LanUrl : "手机端未启动");

            SetText(TxtModeHint, _display != null ? DisplayHost.ModeDisplayName(_display.Mode) : "");
        }

        private static void SetText(TextBlock block, string text)
        {
            if (block != null) block.Text = text;
        }

        /// <summary>按状态挑角标颜色。</summary>
        private Brush BadgeBrushFor(BoardState state)
        {
            switch (state)
            {
                case BoardState.InLesson: return _badgeLesson;
                case BoardState.InLunch: return _badgeLunch;
                case BoardState.AfterSchool: return _badgeEnd;
                default: return _badgeBreak;
            }
        }

        /// <summary>按标题字数调字号，窄布局整体小一档。</summary>
        private void HeadlineSize(string text)
        {
            if (TxtHeadline == null) return;

            int length = string.IsNullOrEmpty(text) ? 1 : text.Length;

            if (_narrow)
            {
                if (length <= 4) TxtHeadline.FontSize = 62;
                else if (length <= 8) TxtHeadline.FontSize = 48;
                else if (length <= 14) TxtHeadline.FontSize = 36;
                else TxtHeadline.FontSize = 28;
                return;
            }

            if (length <= 4) TxtHeadline.FontSize = 104;
            else if (length <= 8) TxtHeadline.FontSize = 84;
            else if (length <= 14) TxtHeadline.FontSize = 62;
            else TxtHeadline.FontSize = 48;
        }

        /// <summary>重建今日课表的每一行。</summary>
        private void RenderTimetable(List<TimetableRow> rows)
        {
            if (TimetableRows == null) return;

            TimetableRows.Children.Clear();
            SetText(TxtTimetableHint, "共 " + rows.Count + " 节");

            if (rows.Count == 0)
            {
                TextBlock empty = new TextBlock();
                empty.Text = "还没有课表";
                empty.FontSize = _narrow ? 16 : 22;
                empty.Foreground = _brushTextMuted;
                empty.Margin = new Thickness(4, 20, 0, 0);
                TimetableRows.Children.Add(empty);
                return;
            }

            // 行高按课表区域的实际高度来定
            double available = _narrow ? 700.0 : 600.0;
            FrameworkElement host = TimetableRows.Parent as FrameworkElement;
            if (host != null && host.ActualHeight > 80) available = host.ActualHeight - 34;

            // 窄版里课表独占一整列，行高可以铺满；宽屏要留出与左栏对齐的余地
            double slot = Math.Min(_narrow ? 80.0 : 58.0, available / rows.Count);
            double gap = rows.Count > 9 ? 3 : (_narrow ? 4 : 6);
            double height = Math.Max(_narrow ? 24 : 30, slot - gap);

            foreach (TimetableRow row in rows)
            {
                TimetableRows.Children.Add(BuildRow(row, height, gap));
            }
        }

        /// <summary>画出一行课表。</summary>
        private Border BuildRow(TimetableRow row, double height, double gap)
        {
            Border container = new Border();
            container.Height = height;
            container.CornerRadius = new CornerRadius(10);
            container.Margin = new Thickness(0, 0, 0, gap);
            container.Background = row.IsCurrent ? _brushCurrentRow : Brushes.Transparent;
            // 上过的课压暗但不能压到看不清
            container.Opacity = row.IsPast ? 0.55 : 1.0;

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_narrow ? 84 : 168) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_narrow ? 66 : 112) });

            // 窄布局行高低，两行文字必须跟着缩小留出余量
            double minName = _narrow ? 9 : 14;
            double minTime = _narrow ? 8 : 12;
            double minSubject = _narrow ? 12 : 16;
            double minTeacher = _narrow ? 9 : 13;

            if (row.IsCurrent)
            {
                Border bar = new Border();
                bar.Width = 4;
                bar.CornerRadius = new CornerRadius(2);
                bar.Background = _brushAccent;
                bar.Margin = new Thickness(0, 6, 0, 6);
                Grid.SetColumn(bar, 0);
                grid.Children.Add(bar);
            }

            StackPanel left = new StackPanel();
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(_narrow ? 8 : 14, 0, 0, 0);

            TextBlock name = new TextBlock();
            name.Text = row.Period.Name;
            name.FontSize = Math.Max(minName, height * 0.32);
            name.Foreground = row.IsCurrent ? _brushCurrentRowText : _brushTextMuted;
            name.FontWeight = row.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;

            TextBlock time = new TextBlock();
            // 窄布局只显示开始时间，整段时间塞不进一行的宽度
            time.Text = _narrow ? row.Period.Start : row.TimeText;
            time.FontSize = Math.Max(minTime, height * 0.24);
            time.Foreground = _brushTextMuted;
            time.Opacity = 0.9;

            left.Children.Add(name);
            left.Children.Add(time);
            Grid.SetColumn(left, 1);
            grid.Children.Add(left);

            TextBlock subject = new TextBlock();
            subject.Text = row.Subject;
            subject.FontSize = Math.Max(minSubject, height * 0.42);
            subject.FontWeight = FontWeights.Bold;
            subject.Foreground = row.IsCurrent ? _brushCurrentRowText : _brushTextMain;
            subject.VerticalAlignment = VerticalAlignment.Center;
            subject.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(subject, 2);
            grid.Children.Add(subject);

            TextBlock teacher = new TextBlock();
            teacher.Text = row.Teacher;
            teacher.FontSize = Math.Max(minTeacher, height * 0.27);
            teacher.Foreground = _brushTextMuted;
            teacher.VerticalAlignment = VerticalAlignment.Center;
            teacher.HorizontalAlignment = HorizontalAlignment.Right;
            teacher.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(teacher, 3);
            grid.Children.Add(teacher);

            container.Child = grid;
            return container;
        }

        /// <summary>重建值日名单。</summary>
        private void RenderDuty(List<DutyMember> members)
        {
            if (DutyPanel == null) return;

            DutyPanel.Children.Clear();
            if (members == null || members.Count == 0)
            {
                TextBlock none = new TextBlock();
                none.Text = "今天没有安排值日";
                none.FontSize = _narrow ? 14 : 20;
                none.Foreground = _brushTextMuted;
                none.VerticalAlignment = VerticalAlignment.Center;
                DutyPanel.Children.Add(none);
                return;
            }

            foreach (DutyMember member in members)
            {
                Border chip = new Border();
                chip.Background = _brushChip;
                chip.CornerRadius = new CornerRadius(_narrow ? 9 : 12);
                chip.Padding = new Thickness(_narrow ? 11 : 18, _narrow ? 5 : 10, _narrow ? 11 : 18, _narrow ? 5 : 10);
                chip.Margin = new Thickness(0, 0, _narrow ? 7 : 12, 6);

                StackPanel stack = new StackPanel();
                stack.Orientation = Orientation.Horizontal;

                TextBlock name = new TextBlock();
                name.Text = member.Name;
                name.FontSize = _narrow ? 15 : 23;
                name.FontWeight = FontWeights.Bold;
                name.Foreground = _brushTextMain;
                stack.Children.Add(name);

                if (!string.IsNullOrWhiteSpace(member.Task))
                {
                    TextBlock task = new TextBlock();
                    task.Text = member.Task;
                    task.FontSize = _narrow ? 10 : 16;
                    task.Foreground = _brushTextMuted;
                    task.VerticalAlignment = VerticalAlignment.Center;
                    task.Margin = new Thickness(_narrow ? 5 : 10, 4, 0, 0);
                    stack.Children.Add(task);
                }

                chip.Child = stack;
                DutyPanel.Children.Add(chip);
            }
        }

        /// <summary>数据或设置改过之后立刻重画一次。</summary>
        public void RefreshNow()
        {
            _dirty = true;
            Render();
        }

        /// <summary>弹出短提示。</summary>
        public void ShowToast(string text)
        {
            if (ToastBox == null) return;

            SetText(TxtToast, text);
            FadeToast(1);
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        /// <summary>把提示淡入或淡出。</summary>
        private void FadeToast(double target)
        {
            if (ToastBox == null) return;
            DoubleAnimation animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(220));
            ToastBox.BeginAnimation(OpacityProperty, animation);
        }
    }
}
