using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kumkat.Core
{
    /// <summary>Bir dosyayı bir tarife göre dönüştürür. Çıktı, girdinin yanına benzersiz adla yazılır.</summary>
    internal sealed class Converter
    {
        private static readonly string[] FfmpegPrefix = { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" };
        private static readonly TimeSpan LongTimeout = TimeSpan.FromHours(3);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMinutes(10);

        // ffmpeg'e vermeden önce kendimiz (Windows codec'leri / PsdReader) PNG'ye çevirdiğimiz biçimler
        private static readonly HashSet<string> PreDecodeWithWic = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heic", "heif", "psd" };

        private readonly ToolLocator _tools;
        private readonly SemaphoreSlim _sofficeGate = new SemaphoreSlim(1, 1); // LibreOffice aynı anda tek örnek

        public Converter(ToolLocator tools)
        {
            _tools = tools;
        }

        /// <returns>Oluşan dosyanın (veya çok sayfalı çıktılarda klasörün) tam yolu.</returns>
        public async Task<string> ConvertAsync(string input, RecipeGroup group, RecipeAction action, CancellationToken ct)
        {
            input = Path.GetFullPath(input);
            if (!File.Exists(input)) throw new FileNotFoundException("Dosya bulunamadı.", input);

            string inExt = RecipeBook.ExtOf(input);
            string outExt = RecipeBook.ResolveOutputExt(group, action, inExt);
            string dir = Path.GetDirectoryName(input) ?? AppPaths.TempRoot;
            string stem = Path.GetFileNameWithoutExtension(input) + (action.Suffix ?? "");
            string tmp = AppPaths.NewTempDir();

            try
            {
                string produced;
                switch (action.Engine)
                {
                    case "ffmpeg":
                        produced = await FfmpegAsync(input, inExt, outExt, action.Args, tmp, ct).ConfigureAwait(false);
                        break;
                    case "soffice":
                        produced = await SofficeAsync(input, outExt, action.Args, tmp, ct).ConfigureAwait(false);
                        break;
                    case "gs":
                        produced = await GhostscriptAsync(input, outExt, action.Args, tmp, ct).ConfigureAwait(false);
                        break;
                    case "image":
                        produced = await ImageAsync(input, outExt, tmp, ct).ConfigureAwait(false);
                        break;
                    case "img2pdf":
                        produced = await ImageToPdfAsync(input, tmp, ct).ConfigureAwait(false);
                        break;
                    case "img2docx":
                        produced = await ImageToDocxAsync(input, tmp, ct).ConfigureAwait(false);
                        break;
                    case "pdf":
                        return await PdfPagesAsync(input, outExt, dir, stem, tmp, ct).ConfigureAwait(false);
                    case "data":
                        produced = await DataAsync(input, group, action, outExt, tmp, ct).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException($"Bilinmeyen motor: {action.Engine}");
                }

                string final = AppPaths.UniqueFile(dir, stem, outExt);
                File.Move(produced, final);
                return final;
            }
            finally
            {
                AppPaths.TryDeleteDir(tmp);
            }
        }

        private static List<string> Substitute(IEnumerable<string> args, string input, string output, string outdir)
        {
            string loProfile = new Uri(Path.Combine(AppPaths.TempRoot, "lo_profile") + Path.DirectorySeparatorChar).AbsoluteUri;
            return args.Select(a => a.Replace("{in}", input)
                                     .Replace("{out}", output)
                                     .Replace("{outdir}", outdir)
                                     .Replace("{lo_profile}", loProfile))
                       .ToList();
        }

        private static void EnsureProduced(string path, ProcessResult r, string tool)
        {
            if (r.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 0) return;
            string detail = ProcessRunner.Tail(r.StdErr.Length > 0 ? r.StdErr : r.StdOut, 400);
            throw new InvalidOperationException($"{tool} dönüştüremedi (kod {r.ExitCode}). {detail}".Trim());
        }

        // ----------------------------------------------------------------- FFmpeg

        private async Task<string> FfmpegAsync(string input, string inExt, string outExt, List<string>? args, string tmp, CancellationToken ct)
        {
            string ffmpeg = _tools.Ffmpeg ?? throw new InvalidOperationException("FFmpeg bulunamadı.");
            if (args == null || args.Count == 0) throw new InvalidOperationException("Tarifte ffmpeg argümanı yok.");

            string source = input;
            if (PreDecodeWithWic.Contains(inExt))
            {
                try
                {
                    string decoded = Path.Combine(tmp, "decoded.png");
                    await StaRunner.Run(() => ImageEngine.Save(ImageEngine.Load(input), decoded, "png")).ConfigureAwait(false);
                    source = decoded;
                }
                catch (Exception ex)
                {
                    Log.Write($"WIC ön çözme başarısız, ffmpeg doğrudan denenecek: {ex.Message}");
                }
            }

            string output = Path.Combine(tmp, "out." + outExt);
            var full = FfmpegPrefix.Concat(Substitute(args, source, output, tmp));
            var r = await ProcessRunner.RunAsync(ffmpeg, full, LongTimeout, ct).ConfigureAwait(false);
            EnsureProduced(output, r, "FFmpeg");
            return output;
        }

        private async Task<string> FfmpegDecodeToPngAsync(string input, string tmp, CancellationToken ct)
        {
            string ffmpeg = _tools.Ffmpeg ?? throw new InvalidOperationException("Bu görsel biçimini okumak için FFmpeg gerekli.");
            string output = Path.Combine(tmp, "decoded-ff.png");
            var args = FfmpegPrefix.Concat(new[] { "-i", input, "-frames:v", "1", output });
            var r = await ProcessRunner.RunAsync(ffmpeg, args, ShortTimeout, ct).ConfigureAwait(false);
            EnsureProduced(output, r, "FFmpeg");
            return output;
        }

        // ------------------------------------------------------------ LibreOffice

        private async Task<string> SofficeAsync(string input, string outExt, List<string>? args, string tmp, CancellationToken ct)
        {
            string soffice = _tools.Soffice ?? throw new InvalidOperationException("LibreOffice bulunamadı.");
            if (args == null || args.Count == 0) throw new InvalidOperationException("Tarifte soffice argümanı yok.");

            // LibreOffice çıktıyı girdinin adıyla yazar: tahmin edilebilir olsun diye sabit adla kopyala.
            string srcDir = Path.Combine(tmp, "src");
            string outDir = Path.Combine(tmp, "out");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(outDir);
            string copy = Path.Combine(srcDir, "kumkat_in" + Path.GetExtension(input).ToLowerInvariant());
            File.Copy(input, copy, true);

            string expected = Path.Combine(outDir, "kumkat_in." + outExt);
            await _sofficeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var r = await ProcessRunner.RunAsync(soffice, Substitute(args, copy, expected, outDir), ShortTimeout, ct).ConfigureAwait(false);

                // soffice.exe bazı kurulumlarda işi bitirmeden döner: dosyanın oluşmasını kısa süre bekle
                for (int i = 0; i < 40 && !File.Exists(expected); i++)
                    await Task.Delay(250, ct).ConfigureAwait(false);

                if (!File.Exists(expected))
                {
                    var any = Directory.GetFiles(outDir, "*." + outExt).FirstOrDefault();
                    if (any != null) expected = any;
                }
                // LibreOffice bazen dosyayı üretip sıfır olmayan kodla çıkar: belirleyici olan dosyanın varlığı
                if (File.Exists(expected) && new FileInfo(expected).Length > 0) return expected;
                string detail = ProcessRunner.Tail(r.StdErr.Length > 0 ? r.StdErr : r.StdOut, 400);
                throw new InvalidOperationException($"LibreOffice dönüştüremedi (kod {r.ExitCode}). {detail}".Trim());
            }
            finally
            {
                _sofficeGate.Release();
            }
        }

        // ------------------------------------------------------------ Ghostscript

        private async Task<string> GhostscriptAsync(string input, string outExt, List<string>? args, string tmp, CancellationToken ct)
        {
            string gs = _tools.Ghostscript ?? throw new InvalidOperationException("Ghostscript bulunamadı.");
            if (args == null) throw new InvalidOperationException("Tarifte gs argümanı yok.");
            string output = Path.Combine(tmp, "out." + outExt);
            var r = await ProcessRunner.RunAsync(gs, Substitute(args, input, output, tmp), LongTimeout, ct).ConfigureAwait(false);
            EnsureProduced(output, r, "Ghostscript");

            // Küçültme işe yaramadıysa (çıktı daha büyükse) bunu bildir
            if (new FileInfo(output).Length >= new FileInfo(input).Length)
                throw new InvalidOperationException("PDF zaten sıkıştırılmış; daha küçük bir sürüm üretilemedi.");
            return output;
        }

        // ------------------------------------------------------------ Görseller

        private async Task<string> ImageAsync(string input, string outExt, string tmp, CancellationToken ct)
        {
            string output = Path.Combine(tmp, "out." + outExt);
            try
            {
                await StaRunner.Run(() => ImageEngine.Save(ImageEngine.Load(input), output, outExt)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ör. Windows'ta WebP/HEIC/AVIF codec'i yoksa: FFmpeg ile çöz, WIC ile yaz
                Log.Write($"WIC okuyamadı ({ex.Message}), FFmpeg ile denenecek.");
                string decoded = await FfmpegDecodeToPngAsync(input, tmp, ct).ConfigureAwait(false);
                await StaRunner.Run(() => ImageEngine.Save(ImageEngine.Load(decoded), output, outExt)).ConfigureAwait(false);
            }
            return output;
        }

        private async Task<string> ImageToPdfAsync(string input, string tmp, CancellationToken ct)
        {
            string source = input;
            PdfImageWriter.PageImage page;
            try
            {
                page = await StaRunner.Run(() => MakePage(source)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Write($"WIC okuyamadı ({ex.Message}), FFmpeg ile denenecek.");
                source = await FfmpegDecodeToPngAsync(input, tmp, ct).ConfigureAwait(false);
                page = await StaRunner.Run(() => MakePage(source)).ConfigureAwait(false);
            }

            string output = Path.Combine(tmp, "out.pdf");
            PdfImageWriter.Write(output, new[] { page });
            return output;
        }

        private async Task<string> ImageToDocxAsync(string input, string tmp, CancellationToken ct)
        {
            string source = input;
            PdfImageWriter.PageImage page;
            try
            {
                page = await StaRunner.Run(() => MakePage(source, 92)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Write($"WIC okuyamadı ({ex.Message}), FFmpeg ile denenecek.");
                source = await FfmpegDecodeToPngAsync(input, tmp, ct).ConfigureAwait(false);
                page = await StaRunner.Run(() => MakePage(source, 92)).ConfigureAwait(false);
            }

            string output = Path.Combine(tmp, "out.docx");
            DocxImageWriter.Write(output, page.Jpeg, page.Width, page.Height, Path.GetFileNameWithoutExtension(input));
            return output;
        }

        private static PdfImageWriter.PageImage MakePage(string path, int quality = 90)
        {
            var bmp = ImageEngine.Load(path);
            return new PdfImageWriter.PageImage(ImageEngine.EncodeJpeg(bmp, quality), bmp.PixelWidth, bmp.PixelHeight);
        }

        // ----------------------------------------------------------------- PDF

        private async Task<string> PdfPagesAsync(string input, string outExt, string dir, string stem, string tmp, CancellationToken ct)
        {
            var pngs = await PdfEngine.RenderPagesToPngAsync(input, tmp, 2.0, ct).ConfigureAwait(false);
            if (pngs.Count == 0) throw new InvalidOperationException("PDF'te sayfa yok.");

            var pages = new List<string>();
            foreach (var png in pngs)
            {
                if (RecipeBook.Canonical(outExt) == "png")
                {
                    pages.Add(png);
                }
                else
                {
                    string converted = Path.ChangeExtension(png, outExt);
                    await StaRunner.Run(() => ImageEngine.Save(ImageEngine.Load(png), converted, outExt)).ConfigureAwait(false);
                    pages.Add(converted);
                }
            }

            if (pages.Count == 1)
            {
                string single = AppPaths.UniqueFile(dir, stem, outExt);
                File.Move(pages[0], single);
                return single;
            }

            string folder = AppPaths.UniqueDir(dir, $"{stem} ({outExt.ToUpperInvariant()})");
            Directory.CreateDirectory(folder);
            foreach (var p in pages) File.Move(p, Path.Combine(folder, Path.GetFileName(p)));
            return folder;
        }

        // ---------------------------------------------------------------- Veri

        private async Task<string> DataAsync(string input, RecipeGroup group, RecipeAction action, string outExt, string tmp, CancellationToken ct)
        {
            if (action.Via == "html>soffice-pdf")
            {
                string html = Path.Combine(tmp, "kumkat_md.html");
                await Task.Run(() => DataEngine.Convert(group.Id, action.Id, input, html), ct).ConfigureAwait(false);
                var args = new List<string>
                {
                    "--headless", "--norestore", "-env:UserInstallation={lo_profile}",
                    "--convert-to", "pdf:writer_web_pdf_Export", "--outdir", "{outdir}", "{in}",
                };
                return await SofficeAsync(html, "pdf", args, tmp, ct).ConfigureAwait(false);
            }

            string output = Path.Combine(tmp, "out." + outExt);
            await Task.Run(() => DataEngine.Convert(group.Id, action.Id, input, output), ct).ConfigureAwait(false);
            return output;
        }
    }
}
