using System;
using System.IO;
using SkiaSharp;

namespace leeyez_kai.Services
{
    /// <summary>
    /// 画像デコード専用クラス — 全形式 SkiaSharp 統一（libjpeg-turbo, SIMD最適化）
    /// </summary>
    public static class ImageDecoder
    {
        /// <summary>最速デコード: 全形式 SkiaSharp → SKBitmap返却</summary>
        public static SKBitmap? FastDecode(Stream stream, string ext, int maxW, int maxH, out int origW, out int origH)
        {
            origW = 0; origH = 0;
            try
            {
                stream.Position = 0;
                return SkiaDecode(stream, maxW, maxH, out origW, out origH);
            }
            catch (Exception ex)
            {
                Logger.Log($"FastDecode failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>SkiaSharpデコード — JPEG はスケール付きデコードで高速化</summary>
        private static SKBitmap? SkiaDecode(Stream stream, int maxW, int maxH, out int origW, out int origH)
        {
            origW = 0; origH = 0;

            MemoryStream? tempMs = null;
            Stream src;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                src = stream;
            }
            else
            {
                tempMs = new MemoryStream();
                stream.CopyTo(tempMs);
                tempMs.Position = 0;
                src = tempMs;
            }

            try
            {
                using var codec = SKCodec.Create(src);
                if (codec == null) return null;

                origW = codec.Info.Width;
                origH = codec.Info.Height;

                float scale = Math.Min(1.0f, Math.Min((float)maxW / origW, (float)maxH / origH));

                // 通常デコード（全形式共通）
                var decodeInfo = new SKImageInfo(origW, origH, SKColorType.Bgra8888, SKAlphaType.Premul);
                var skBmp = new SKBitmap(decodeInfo);

                if (codec.FrameCount > 1)
                    codec.GetPixels(decodeInfo, skBmp.GetPixels(), new SKCodecOptions(0));
                else
                    codec.GetPixels(decodeInfo, skBmp.GetPixels());

                if (scale < 0.95f)
                {
                    int dstW = Math.Max(1, (int)(origW * scale));
                    int dstH = Math.Max(1, (int)(origH * scale));
                    var resizeInfo = new SKImageInfo(dstW, dstH, SKColorType.Bgra8888, SKAlphaType.Premul);
                    var resized = skBmp.Resize(resizeInfo, SKFilterQuality.Medium);
                    skBmp.Dispose();
                    return resized;
                }
                return skBmp;
            }
            finally { tempMs?.Dispose(); }
        }
    }
}
