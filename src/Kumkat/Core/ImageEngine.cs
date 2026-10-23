using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kumkat.Core
{
    /// <summary>
    /// Windows Imaging Component (WPF) ile görsel okuma/yazma.
    /// HEIC/AVIF/WebP okuma, Windows'ta ilgili codec uzantıları kuruluysa çalışır; değilse Converter FFmpeg'e düşer.
    /// Bu sınıftaki tüm metotlar STA iş parçacığında çağrılmalıdır (StaRunner).
    /// </summary>
    internal static class ImageEngine
    {
        public static BitmapSource Load(string path)
        {
            // PSD için Windows'ta codec yok: kendi okuyucumuz (FFmpeg gerekmez)
            if (string.Equals(Path.GetExtension(path), ".psd", StringComparison.OrdinalIgnoreCase))
                return PsdReader.Read(path);

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(fs,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            BitmapSource result = ApplyExifOrientation(frame, ReadOrientation(frame));
            if (result.CanFreeze) result.Freeze();
            return result;
        }

        private static int ReadOrientation(BitmapFrame frame)
        {
            if (frame.Metadata is not BitmapMetadata meta) return 1;
            foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}", "System.Photo.Orientation" })
            {
                try
                {
                    object? v = meta.GetQuery(query);
                    if (v != null) return Convert.ToInt32(v);
                }
                catch
                {
                    // bu biçimde o sorgu yok
                }
            }
            return 1;
        }

        private static BitmapSource ApplyExifOrientation(BitmapSource src, int orientation)
        {
            Transform? t = orientation switch
            {
                2 => new ScaleTransform(-1, 1),
                3 => new RotateTransform(180),
                4 => new ScaleTransform(1, -1),
                5 => Combine(new RotateTransform(90), new ScaleTransform(-1, 1)),
                6 => new RotateTransform(90),
                7 => Combine(new RotateTransform(270), new ScaleTransform(-1, 1)),
                8 => new RotateTransform(270),
                _ => null,
            };
            return t == null ? src : new TransformedBitmap(src, t);
        }

        private static Transform Combine(Transform a, Transform b)
        {
            var g = new TransformGroup();
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }

        public static bool HasAlpha(PixelFormat f)
        {
            return f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Prgba64 ||
                   f == PixelFormats.Rgba64 || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float ||
                   f == PixelFormats.Indexed1 || f == PixelFormats.Indexed2 || f == PixelFormats.Indexed4 ||
                   f == PixelFormats.Indexed8;
        }

        /// <summary>Saydam pikselleri beyaz zemin üzerine birleştirip 24 bit RGB görüntü verir (JPG/BMP/PDF için).</summary>
        public static BitmapSource FlattenOnWhite(BitmapSource src)
        {
            var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            int inStride = w * 4;
            var pixels = new byte[(long)inStride * h];
            bgra.CopyPixels(pixels, inStride, 0);

            int outStride = (w * 3 + 3) & ~3;
            var rgb = new byte[(long)outStride * h];
            for (int y = 0; y < h; y++)
            {
                int si = y * inStride;
                int di = y * outStride;
                for (int x = 0; x < w; x++)
                {
                    int a = pixels[si + 3];
                    int inv = 255 - a;
                    rgb[di] = (byte)((pixels[si] * a + 255 * inv + 127) / 255);
                    rgb[di + 1] = (byte)((pixels[si + 1] * a + 255 * inv + 127) / 255);
                    rgb[di + 2] = (byte)((pixels[si + 2] * a + 255 * inv + 127) / 255);
                    si += 4;
                    di += 3;
                }
            }
            double dpiX = src.DpiX > 1 ? src.DpiX : 96, dpiY = src.DpiY > 1 ? src.DpiY : 96;
            var result = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Bgr24, null, rgb, outStride);
            result.Freeze();
            return result;
        }

        public static void Save(BitmapSource src, string path, string ext)
        {
            BitmapEncoder encoder;
            BitmapSource frame = src;
            switch (RecipeBook.Canonical(ext))
            {
                case "png":
                    encoder = new PngBitmapEncoder();
                    break;
                case "jpg":
                    encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                    frame = FlattenOnWhite(src);
                    break;
                case "bmp":
                    encoder = new BmpBitmapEncoder();
                    frame = FlattenOnWhite(src);
                    break;
                case "tiff":
                    encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
                    break;
                case "gif":
                    encoder = new GifBitmapEncoder();
                    break;
                default:
                    throw new NotSupportedException($"Windows görüntü kodlayıcısı '{ext}' yazamıyor.");
            }

            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fs);
        }

        public static byte[] EncodeJpeg(BitmapSource src, int quality)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(FlattenOnWhite(src)));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
