using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace leeyez_kai.Services
{
    /// <summary>
    /// 画像デコード専用クラス — 描画ロジックから完全分離
    /// GDI+直接デコード(JPG/PNG/BMP) + SkiaSharp(WebP/AVIF)
    /// </summary>
    public static class ImageDecoder
    {
        private static readonly HashSet<string> GdiNativeExts = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".ico" };

        /// <summary>最速デコード: GDI+直接 or SkiaSharp</summary>
        public static Bitmap? FastDecode(Stream stream, string ext, int maxW, int maxH, out int origW, out int origH)
        {
            origW = 0; origH = 0;
            try
            {
                stream.Position = 0;
                return GdiNativeExts.Contains(ext)
                    ? GdiDecode(stream, maxW, maxH, out origW, out origH)
                    : SkiaDecode(stream, maxW, maxH, out origW, out origH);
            }
            catch (Exception ex)
            {
                Logger.Log($"FastDecode failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>GDI+直接デコード（JPG/PNG/BMP/GIF/TIFF）</summary>
        private static Bitmap? GdiDecode(Stream stream, int maxW, int maxH, out int origW, out int origH)
        {
            origW = 0; origH = 0;

            // GDI+はSeekableなStreamが必要。FileStreamはそのまま渡せる。
            // 書庫からのMemoryStreamもそのまま渡せる。
            // SeekできないStreamだけMemoryStreamにコピー。
            Stream src;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                src = stream;
            }
            else
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                src = ms;
            }

            using var original = new Bitmap(src);
            origW = original.Width;
            origH = original.Height;

            float scale = Math.Min(1.0f, Math.Min((float)maxW / origW, (float)maxH / origH));
            int dstW = scale >= 0.95f ? origW : Math.Max(1, (int)(origW * scale));
            int dstH = scale >= 0.95f ? origH : Math.Max(1, (int)(origH * scale));

            // 等倍かつ既にPArgbなら直接クローン（コピー不要）
            if (dstW == origW && dstH == origH && original.PixelFormat == PixelFormat.Format32bppPArgb)
                return (Bitmap)original.Clone();

            var result = new Bitmap(dstW, dstH, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = dstW == origW && dstH == origH
                    ? InterpolationMode.Default
                    : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(original, 0, 0, dstW, dstH);
            }
            return result;
        }

        /// <summary>SkiaSharpデコード（WebP/AVIF等）— アニメーションWebPは最初のフレームだけ高速デコード</summary>
        private static Bitmap? SkiaDecode(Stream stream, int maxW, int maxH, out int origW, out int origH)
        {
            origW = 0; origH = 0;

            // SeekableならそのままSKCodecに渡す、そうでなければMemoryStreamにコピー
            Stream src;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                src = stream;
            }
            else
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                src = ms;
            }

            using var codec = SKCodec.Create(src);
            if (codec == null) return null;

            origW = codec.Info.Width;
            origH = codec.Info.Height;

            var decodeInfo = new SKImageInfo(origW, origH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var skBmp = new SKBitmap(decodeInfo);

            if (codec.FrameCount > 1)
            {
                // アニメーション: 最初のフレームだけデコード
                codec.GetPixels(decodeInfo, skBmp.GetPixels(), new SKCodecOptions(0));
            }
            else
            {
                // 静止画: 通常デコード
                codec.GetPixels(decodeInfo, skBmp.GetPixels());
            }

            float scale = Math.Min(1.0f, Math.Min((float)maxW / origW, (float)maxH / origH));

            if (scale < 0.95f)
            {
                int dstW = Math.Max(1, (int)(origW * scale));
                int dstH = Math.Max(1, (int)(origH * scale));
                var resizeInfo = new SKImageInfo(dstW, dstH, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var resized = skBmp.Resize(resizeInfo, SKFilterQuality.Medium);
                if (resized != null) return SKBitmapToGdiBitmap(resized);
            }
            return SKBitmapToGdiBitmap(skBmp);
        }

        /// <summary>SKBitmap → GDI+ Bitmap変換</summary>
        public static Bitmap SKBitmapToGdiBitmap(SKBitmap skBitmap)
        {
            var bmp = new Bitmap(skBitmap.Width, skBitmap.Height, PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                using var converted = skBitmap.Copy(SKColorType.Bgra8888);
                if (converted != null)
                {
                    var src = converted.GetPixels();
                    int byteCount = data.Stride * data.Height;
                    unsafe
                    {
                        Buffer.MemoryCopy(src.ToPointer(), data.Scan0.ToPointer(),
                            byteCount, Math.Min(byteCount, converted.RowBytes * converted.Height));
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
