using System.Collections.Generic;
using System.Windows.Media;

namespace NEWCLASSroom.Services
{
    /// <summary>班牌配色。深色适合教室大屏和投影，浅色适合老师自己的显示器。</summary>
    public class BoardTheme
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public Color BackgroundTop { get; set; }
        public Color BackgroundMid { get; set; }
        public Color BackgroundBottom { get; set; }

        public Color CardTop { get; set; }
        public Color CardBottom { get; set; }
        public Color CardBorder { get; set; }

        public Color TextPrimary { get; set; }
        public Color TextMuted { get; set; }
        public Color TextFaint { get; set; }

        public Color AccentStart { get; set; }
        public Color AccentEnd { get; set; }

        /// <summary>今日课表里“当前这一节”的高亮底色和文字色。</summary>
        public Color CurrentRow { get; set; }
        public Color CurrentRowText { get; set; }

        public Color ChipBackground { get; set; }
        public Color Track { get; set; }
        public Color Toast { get; set; }

        /// <summary>状态角标的底色：上课 / 课间 / 午休 / 放学。</summary>
        public Color BadgeLesson { get; set; }
        public Color BadgeBreak { get; set; }
        public Color BadgeLunch { get; set; }
        public Color BadgeEnd { get; set; }

        /// <summary>窗口本身的底色，Viewbox 留边时露出来的就是它。</summary>
        public Color WindowBackground { get; set; }

        /// <summary>深色主题的调色板。</summary>
        public static BoardTheme Dark()
        {
            BoardTheme t = new BoardTheme();
            t.Id = "Dark";
            t.Name = "深色（适合教室大屏）";

            // 背景压得很深，卡片提亮一档，靠明度差把两块区域分开
            t.BackgroundTop = Color.FromRgb(0x0D, 0x15, 0x24);
            t.BackgroundMid = Color.FromRgb(0x09, 0x0F, 0x1B);
            t.BackgroundBottom = Color.FromRgb(0x0F, 0x18, 0x2A);

            t.CardTop = Color.FromRgb(0x1E, 0x2A, 0x45);
            t.CardBottom = Color.FromRgb(0x18, 0x22, 0x38);
            t.CardBorder = Color.FromRgb(0x39, 0x4A, 0x72);

            // 正文用纯白，次要信息也保持在容易读的亮度，别缩字号就发灰
            t.TextPrimary = Colors.White;
            t.TextMuted = Color.FromRgb(0xC6, 0xD3, 0xE8);
            t.TextFaint = Color.FromRgb(0x97, 0xA7, 0xC2);

            t.AccentStart = Color.FromRgb(0x5A, 0x9C, 0xFF);
            t.AccentEnd = Color.FromRgb(0x7A, 0x79, 0xF0);

            t.CurrentRow = Color.FromRgb(0x27, 0x45, 0x76);
            t.CurrentRowText = Colors.White;

            t.ChipBackground = Color.FromRgb(0x28, 0x36, 0x54);
            t.Track = Color.FromRgb(0x2C, 0x3C, 0x5C);
            t.Toast = Color.FromArgb(0xE6, 0x11, 0x1A, 0x2E);

            t.BadgeLesson = Color.FromRgb(0x2F, 0x7B, 0xF0);
            t.BadgeBreak = Color.FromRgb(0x6A, 0x69, 0xE0);
            t.BadgeLunch = Color.FromRgb(0x18, 0x9A, 0x70);
            t.BadgeEnd = Color.FromRgb(0x5E, 0x6B, 0x80);

            t.WindowBackground = Color.FromRgb(0x0B, 0x11, 0x1D);
            return t;
        }

        /// <summary>浅色主题的调色板。</summary>
        public static BoardTheme Light()
        {
            BoardTheme t = new BoardTheme();
            t.Id = "Light";
            t.Name = "浅色（适合普通显示器）";

            t.BackgroundTop = Color.FromRgb(0xF7, 0xFA, 0xFF);
            t.BackgroundMid = Color.FromRgb(0xEC, 0xF2, 0xFA);
            t.BackgroundBottom = Color.FromRgb(0xE5, 0xEC, 0xF7);

            t.CardTop = Colors.White;
            t.CardBottom = Color.FromRgb(0xFA, 0xFC, 0xFF);
            t.CardBorder = Color.FromRgb(0xC6, 0xD2, 0xE4);

            t.TextPrimary = Color.FromRgb(0x0E, 0x17, 0x26);
            t.TextMuted = Color.FromRgb(0x42, 0x51, 0x6B);
            t.TextFaint = Color.FromRgb(0x5E, 0x6C, 0x84);

            t.AccentStart = Color.FromRgb(0x2F, 0x6B, 0xFF);
            t.AccentEnd = Color.FromRgb(0x5A, 0x5B, 0xE6);

            t.CurrentRow = Color.FromRgb(0xDF, 0xEB, 0xFF);
            t.CurrentRowText = Color.FromRgb(0x0A, 0x18, 0x2E);

            t.ChipBackground = Color.FromRgb(0xE8, 0xEF, 0xF9);
            t.Track = Color.FromRgb(0xD3, 0xDD, 0xEC);
            t.Toast = Color.FromArgb(0xE6, 0x14, 0x1E, 0x30);

            t.BadgeLesson = Color.FromRgb(0x2F, 0x6B, 0xFF);
            t.BadgeBreak = Color.FromRgb(0x5A, 0x5B, 0xE6);
            t.BadgeLunch = Color.FromRgb(0x10, 0x84, 0x5C);
            t.BadgeEnd = Color.FromRgb(0x6B, 0x7A, 0x93);

            t.WindowBackground = Color.FromRgb(0xF1, 0xF5, 0xFB);
            return t;
        }

        /// <summary>列出所有可选主题。</summary>
        public static List<BoardTheme> All()
        {
            List<BoardTheme> list = new List<BoardTheme>();
            list.Add(Dark());
            list.Add(Light());
            return list;
        }

        /// <summary>按编号取主题，不认识的一律给深色。</summary>
        public static BoardTheme Get(string id)
        {
            return string.Equals(id, "Light", System.StringComparison.OrdinalIgnoreCase) ? Light() : Dark();
        }
    }
}
