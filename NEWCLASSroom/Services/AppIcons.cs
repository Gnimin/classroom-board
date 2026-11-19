using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace NEWCLASSroom.Services
{
    /// <summary>运行时画出托盘图标，省得带一堆二进制资源。</summary>
    public static class AppIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        /// <summary>画一个 32 像素的托盘图标。</summary>
        public static Icon CreateTrayIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);

                        using (GraphicsPath path = RoundedRect(new Rectangle(1, 1, 30, 30), 8))
                        using (LinearGradientBrush brush = new LinearGradientBrush(
                            new Rectangle(0, 0, 32, 32),
                            Color.FromArgb(56, 132, 255),
                            Color.FromArgb(86, 74, 220),
                            45f))
                        {
                            g.FillPath(brush, path);
                        }

                        using (Font font = new Font("Microsoft YaHei", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("班", font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                        }
                    }

                    IntPtr handle = bmp.GetHicon();
                    try
                    {
                        using (Icon temp = Icon.FromHandle(handle))
                        {
                            return (Icon)temp.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        /// <summary>画一个 64 像素的窗口图标。</summary>
        public static Icon CreateAppIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(64, 64))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.Clear(Color.Transparent);
                        using (GraphicsPath path = RoundedRect(new Rectangle(2, 2, 60, 60), 16))
                        using (LinearGradientBrush brush = new LinearGradientBrush(
                            new Rectangle(0, 0, 64, 64),
                            Color.FromArgb(56, 132, 255),
                            Color.FromArgb(86, 74, 220),
                            45f))
                        {
                            g.FillPath(brush, path);
                        }
                        using (Font font = new Font("Microsoft YaHei", 30f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("班", font, Brushes.White, new RectangleF(0, 2, 64, 64), sf);
                        }
                    }
                    IntPtr handle = bmp.GetHicon();
                    try
                    {
                        using (Icon temp = Icon.FromHandle(handle))
                        {
                            return (Icon)temp.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        /// <summary>生成一个圆角矩形的路径。</summary>
        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
