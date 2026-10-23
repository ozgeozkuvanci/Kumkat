using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kumkat.Core
{
    /// <summary>JPEG görüntülerden (her biri bir sayfa, A4'e sığdırılmış) en küçük geçerli PDF'i yazar.</summary>
    internal static class PdfImageWriter
    {
        public sealed class PageImage
        {
            public PageImage(byte[] jpeg, int width, int height)
            {
                Jpeg = jpeg;
                Width = width;
                Height = height;
            }

            public byte[] Jpeg { get; }
            public int Width { get; }
            public int Height { get; }
        }

        private const double A4W = 595.28, A4H = 841.89, Margin = 24;

        private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static void Write(string path, IReadOnlyList<PageImage> pages)
        {
            if (pages.Count == 0) throw new ArgumentException("Sayfa yok.");

            // Nesne numaraları: 1 katalog, 2 sayfalar, sonra her sayfa için (sayfa, görüntü, içerik)
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            var offsets = new List<long>();
            int objCount = 2 + pages.Count * 3;
            for (int i = 0; i < objCount; i++) offsets.Add(0);

            void Ascii(string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                fs.Write(b, 0, b.Length);
            }

            Ascii("%PDF-1.4\n");
            fs.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);

            offsets[0] = fs.Position;
            Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            var kids = new StringBuilder();
            for (int i = 0; i < pages.Count; i++) kids.Append(3 + i * 3).Append(" 0 R ");
            offsets[1] = fs.Position;
            Ascii($"2 0 obj\n<< /Type /Pages /Kids [ {kids}] /Count {pages.Count} >>\nendobj\n");

            for (int i = 0; i < pages.Count; i++)
            {
                var img = pages[i];
                int pageObj = 3 + i * 3, imgObj = pageObj + 1, contentObj = pageObj + 2;

                bool landscape = img.Width > img.Height;
                double pw = landscape ? A4H : A4W, ph = landscape ? A4W : A4H;
                double scale = Math.Min((pw - 2 * Margin) / img.Width, (ph - 2 * Margin) / img.Height);
                double dw = img.Width * scale, dh = img.Height * scale;
                double x = (pw - dw) / 2, y = (ph - dh) / 2;

                offsets[pageObj - 1] = fs.Position;
                Ascii($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pw)} {N(ph)}] " +
                      $"/Resources << /XObject << /Im{i} {imgObj} 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");

                offsets[imgObj - 1] = fs.Position;
                Ascii($"{imgObj} 0 obj\n<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} " +
                      $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {img.Jpeg.Length} >>\nstream\n");
                fs.Write(img.Jpeg, 0, img.Jpeg.Length);
                Ascii("\nendstream\nendobj\n");

                string content = $"q {N(dw)} 0 0 {N(dh)} {N(x)} {N(y)} cm /Im{i} Do Q";
                offsets[contentObj - 1] = fs.Position;
                Ascii($"{contentObj} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
            }

            long xref = fs.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(objCount + 1).Append('\n');
            sb.Append("0000000000 65535 f \n");
            foreach (var off in offsets) sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(objCount + 1).Append(" /Root 1 0 R >>\nstartxref\n")
              .Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
            Ascii(sb.ToString());
        }
    }
}
