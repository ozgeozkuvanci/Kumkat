using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinPdf = global::Windows.Data.Pdf;
using WinStorage = global::Windows.Storage;
using WinStreams = global::Windows.Storage.Streams;

namespace Kumkat.Core
{
    /// <summary>PDF sayfalarını Windows'un yerleşik PDF motoruyla (Windows.Data.Pdf) PNG olarak çizer.</summary>
    internal static class PdfEngine
    {
        /// <param name="scale">1.0 = 96 DPI; 2.0 ≈ 192 DPI.</param>
        public static async Task<List<string>> RenderPagesToPngAsync(string pdfPath, string outDir, double scale, CancellationToken ct)
        {
            var result = new List<string>();
            WinStorage.StorageFile file = await WinStorage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            WinPdf.PdfDocument doc = await WinPdf.PdfDocument.LoadFromFileAsync(file);

            for (uint i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using WinPdf.PdfPage page = doc.GetPage(i);
                var options = new WinPdf.PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                    BackgroundColor = new global::Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 },
                };

                using var mem = new WinStreams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(mem, options);

                uint size = (uint)mem.Size;
                var bytes = new byte[size];
                using (var reader = new WinStreams.DataReader(mem.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync(size);
                    reader.ReadBytes(bytes);
                }

                string png = Path.Combine(outDir, $"sayfa-{i + 1:000}.png");
                await File.WriteAllBytesAsync(png, bytes, ct);
                result.Add(png);
            }
            return result;
        }
    }
}
