namespace NEWCLASSroom.Views
{
    /// <summary>右侧停靠模式专用的窄版界面：左栏时钟、当前状态和值日，右栏整条今日课表。</summary>
    public partial class NarrowBoardWindow : BoardWindowBase
    {
        /// <summary>初始化界面并挂上公共逻辑。</summary>
        public NarrowBoardWindow(AppState state, bool screenSaver)
            : base(state, screenSaver, true)
        {
            InitializeComponent();
            Initialize();
        }
    }
}
