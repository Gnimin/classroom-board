namespace NEWCLASSroom.Views
{
    /// <summary>宽屏班牌窗口：左边时钟和当前状态，右边今日课表，底部一条值日。</summary>
    public partial class SignboardWindow : BoardWindowBase
    {
        /// <summary>初始化界面并挂上公共逻辑。</summary>
        public SignboardWindow(AppState state, bool screenSaver)
            : base(state, screenSaver, false)
        {
            InitializeComponent();
            Initialize();
        }
    }
}
