using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace NEWCLASSroom.Services
{
    /// <summary>手机端页面以嵌入资源的形式打包进 exe，不依赖外部文件。</summary>
    public static class WebAssets
    {
        private const string ResourceName = "NEWCLASSroom.Web.index.html";
        private static string _cache;

        /// <summary>取出内嵌的手机端页面，读不到时给一个提示页。</summary>
        public static string IndexHtml()
        {
            if (_cache != null) return _cache;

            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            _cache = reader.ReadToEnd();
                            return _cache;
                        }
                    }
                }
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = asm.GetManifestResourceStream(name))
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            _cache = reader.ReadToEnd();
                            return _cache;
                        }
                    }
                }
            }
            catch { }

            _cache = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>班牌</title></head>" +
                     "<body style=\"font-family:sans-serif;padding:24px\"><h2>手机端页面缺失</h2>" +
                     "<p>请检查程序包是否完整。</p></body></html>";
            return _cache;
        }
    }
}
