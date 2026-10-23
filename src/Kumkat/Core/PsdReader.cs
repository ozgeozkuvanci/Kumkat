using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kumkat.Core
{
    /// <summary>
    /// Photoshop PSD dosyasının birleşik (düzleştirilmiş) görüntüsünü harici araç olmadan okur.
    /// Desteklenen: Gri / RGB / CMYK, 8 veya 16 bit, sıkıştırmasız veya RLE (Photoshop varsayılanı).
    /// Katmanlar okunmaz; Photoshop'un "Uyumluluğu en üst düzeye çıkar" ile kaydettiği birleşik görüntü kullanılır.
    /// </summary>
    internal static class PsdReader
    {
        public static BitmapSource Read(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            int p = 0;

            if (d.Length < 26 || d[0] != (byte)'8' || d[1] != (byte)'B' || d[2] != (byte)'P' || d[3] != (byte)'S')
                throw new InvalidDataException("Geçerli bir PSD dosyası değil.");
            int version = U16(d, 4);
            if (version == 2) throw new NotSupportedException("PSB (büyük belge) biçimi desteklenmiyor.");
            if (version != 1) throw new InvalidDataException("Bilinmeyen PSD sürümü.");

            int channels = U16(d, 12);
            int h = checked((int)U32(d, 14));
            int w = checked((int)U32(d, 18));
            int depth = U16(d, 22);
            int mode = U16(d, 24);
            p = 26;

            // renk modu verisi, görüntü kaynakları, katman/maske bölümü: atla
            for (int s = 0; s < 3; s++)
            {
                long len = U32(d, p);
                p = checked((int)(p + 4 + len));
            }

            int compression = U16(d, p);
            p += 2;

            int need = mode switch
            {
                1 => 1, // gri
                3 => 3, // RGB
                4 => 4, // CMYK
                _ => throw new NotSupportedException($"Bu PSD renk modu desteklenmiyor ({mode}). RGB, CMYK veya Gri olmalı."),
            };
            if (depth != 8 && depth != 16) throw new NotSupportedException($"{depth} bit PSD desteklenmiyor (8 veya 16 olmalı).");
            if (channels < need) throw new InvalidDataException("PSD kanal sayısı eksik.");
            if (w <= 0 || h <= 0) throw new InvalidDataException("PSD boyutu geçersiz.");

            int bpc = depth / 8;
            int rowBytes = w * bpc;
            var planes = new byte[need][];

            if (compression == 0)
            {
                for (int c = 0; c < need; c++)
                {
                    planes[c] = new byte[(long)rowBytes * h];
                    Buffer.BlockCopy(d, p, planes[c], 0, planes[c].Length);
                    p += planes[c].Length;
                }
            }
            else if (compression == 1)
            {
                int countsStart = p;
                p += 2 * channels * h;
                for (int c = 0; c < need; c++)
                {
                    var plane = new byte[(long)rowBytes * h];
                    for (int y = 0; y < h; y++)
                    {
                        int n = U16(d, countsStart + 2 * (c * h + y));
                        UnpackBits(d, p, n, plane, y * rowBytes, rowBytes);
                        p += n;
                    }
                    planes[c] = plane;
                }
            }
            else
            {
                throw new NotSupportedException("Bu PSD sıkıştırma türü desteklenmiyor (ZIP).");
            }

            int stride = (w * 3 + 3) & ~3;
            var bgr = new byte[(long)stride * h];
            for (int y = 0; y < h; y++)
            {
                int di = y * stride;
                int si = y * w * bpc; // 16 bitte ilk (yüksek) bayt alınır
                for (int x = 0; x < w; x++, di += 3, si += bpc)
                {
                    int r, g, b;
                    if (mode == 1)
                    {
                        r = g = b = planes[0][si];
                    }
                    else if (mode == 3)
                    {
                        r = planes[0][si];
                        g = planes[1][si];
                        b = planes[2][si];
                    }
                    else
                    {
                        // PSD'de CMYK ters saklanır (255 = mürekkep yok)
                        int k = planes[3][si];
                        r = (planes[0][si] * k + 127) / 255;
                        g = (planes[1][si] * k + 127) / 255;
                        b = (planes[2][si] * k + 127) / 255;
                    }
                    bgr[di] = (byte)b;
                    bgr[di + 1] = (byte)g;
                    bgr[di + 2] = (byte)r;
                }
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, bgr, stride);
            bmp.Freeze();
            return bmp;
        }

        private static void UnpackBits(byte[] src, int pos, int count, byte[] dst, int dstPos, int rowLen)
        {
            int end = Math.Min(src.Length, pos + count);
            int written = 0;
            while (pos < end && written < rowLen)
            {
                int header = src[pos++];
                if (header < 128)
                {
                    int n = Math.Min(header + 1, Math.Min(rowLen - written, end - pos));
                    Buffer.BlockCopy(src, pos, dst, dstPos + written, n);
                    pos += header + 1;
                    written += n;
                }
                else if (header > 128)
                {
                    if (pos >= end) break;
                    byte v = src[pos++];
                    int n = Math.Min(257 - header, rowLen - written);
                    for (int i = 0; i < n; i++) dst[dstPos + written + i] = v;
                    written += n;
                }
                // 128: boş işlem
            }
        }

        private static int U16(byte[] d, int i) => (d[i] << 8) | d[i + 1];

        private static uint U32(byte[] d, int i) =>
            ((uint)d[i] << 24) | ((uint)d[i + 1] << 16) | ((uint)d[i + 2] << 8) | d[i + 3];
    }
}
