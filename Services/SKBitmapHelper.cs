using System;
using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace leeyez_kai.Services
{
    /// <summary>
    /// SKBitmap ↔ GDI+ 変換ヘルパー（WinForms API が GDI+ Bitmap を要求する箇所用）
    /// </summary>
    public static class SKBitmapHelper
    {
        /// <summary>SKBitmap → GDI+ Bitmap変換（クリップボード・ImageList等WinForms API用）</summary>
        public static Bitmap ToGdiBitmap(SKBitmap skBitmap)
        {
            var bmp = new Bitmap(skBitmap.Width, skBitmap.Height, PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                int byteCount = data.Stride * data.Height;
                if (skBitmap.ColorType == SKColorType.Bgra8888)
                {
                    var src = skBitmap.GetPixels();
                    unsafe
                    {
                        Buffer.MemoryCopy(src.ToPointer(), data.Scan0.ToPointer(),
                            byteCount, Math.Min(byteCount, skBitmap.RowBytes * skBitmap.Height));
                    }
                }
                else
                {
                    using var converted = skBitmap.Copy(SKColorType.Bgra8888);
                    if (converted != null)
                    {
                        var src = converted.GetPixels();
                        unsafe
                        {
                            Buffer.MemoryCopy(src.ToPointer(), data.Scan0.ToPointer(),
                                byteCount, Math.Min(byteCount, converted.RowBytes * converted.Height));
                        }
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        /// <summary>SKBitmapをGDI+ Graphics上に描画</summary>
        public static void DrawSKBitmap(Graphics g, SKBitmap skBitmap, Rectangle destRect)
        {
            using var gdiBmp = ToGdiBitmap(skBitmap);
            g.DrawImage(gdiBmp, destRect);
        }
    }
}
