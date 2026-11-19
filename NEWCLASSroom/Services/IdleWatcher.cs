using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace NEWCLASSroom.Services
{
    /// <summary>检测鼠标键盘空闲了多久，用来决定什么时候进入屏保式全屏。</summary>
    public class IdleWatcher
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private readonly DispatcherTimer _timer;
        private bool _isIdle;

        /// <summary>空闲多久算“长时间没动”，单位分钟。</summary>
        public int ThresholdMinutes { get; set; }

        /// <summary>进入空闲状态（该全屏显示了）。</summary>
        public event EventHandler Idled;

        /// <summary>用户回来了（该退出全屏）。</summary>
        public event EventHandler Returned;

        public bool IsIdle { get { return _isIdle; } }

        /// <summary>初始化空闲检测，默认阈值五分钟。</summary>
        public IdleWatcher()
        {
            ThresholdMinutes = 5;
            _timer = new DispatcherTimer();
            // 250ms 一次，屏保出现后一动鼠标就能立刻收起来，占用的 CPU 可以忽略不计
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Tick += OnTick;
        }

        /// <summary>开始轮询空闲时间。</summary>
        public void Start()
        {
            _timer.Start();
        }

        /// <summary>停止轮询并清掉空闲状态。</summary>
        public void Stop()
        {
            _timer.Stop();
            _isIdle = false;
        }

        /// <summary>取系统记录的已经多久没有输入。</summary>
        public static TimeSpan GetIdleTime()
        {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            uint tick = (uint)Environment.TickCount;
            uint idle = tick - info.dwTime;
            return TimeSpan.FromMilliseconds(idle);
        }

        /// <summary>每次轮询判断是否进入或退出空闲。</summary>
        private void OnTick(object sender, EventArgs e)
        {
            TimeSpan idle = GetIdleTime();
            bool shouldBeIdle = idle.TotalMinutes >= ThresholdMinutes && ThresholdMinutes > 0;

            if (shouldBeIdle && !_isIdle)
            {
                _isIdle = true;
                EventHandler handler = Idled;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            else if (!shouldBeIdle && _isIdle)
            {
                _isIdle = false;
                EventHandler handler = Returned;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>手动重置空闲判定，例如用户刚点过“知道了”。</summary>
        public void Reset()
        {
            _isIdle = false;
        }
    }
}
