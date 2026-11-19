using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace NEWCLASSroom.Services
{
    /// <summary>把手机端访问地址生成二维码，方便老师扫码。</summary>
    public static class QrService
    {
        /// <summary>把一段文字生成二维码图片。</summary>
        public static BitmapSource Create(string text, int pixelsPerModule)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                if (pixelsPerModule < 2) pixelsPerModule = 6;

                using (QRCodeGenerator generator = new QRCodeGenerator())
                using (QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
                using (PngByteQRCode png = new PngByteQRCode(data))
                {
                    byte[] bytes = png.GetGraphic(pixelsPerModule);
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = new MemoryStream(bytes);
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
