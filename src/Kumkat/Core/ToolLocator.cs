using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kumkat.Core
{
    /// <summary>FFmpeg, LibreOffice ve Ghostscript'in yerini bulur.</summary>
    internal sealed class ToolLocator
    {
        private readonly Settings _settings;

        public ToolLocator(Settings settings)
        {
            _settings = settings;
            Refresh();
        }

        public string? Ffmpeg { get; private set; }
        public string? Soffice { get; private set; }
        public string? Ghostscript { get; private set; }

        public void Refresh()
        {
            Ffmpeg = FirstExisting(_settings.FfmpegPath) ?? FindFfmpeg();
            Soffice = FirstExisting(_settings.SofficePath) ?? FindSoffice();
            Ghostscript = FirstExisting(_settings.GhostscriptPath) ?? FindGhostscript();
            Log.Write($"Araçlar: ffmpeg={Ffmpeg ?? "-"} soffice={Soffice ?? "-"} gs={Ghostscript ?? "-"}");
        }

        /// <summary>Motor için eksik aracın adı; eksik yoksa null.</summary>
        public string? MissingToolFor(string engine, string? via = null)
        {
            switch (engine)
            {
                case "ffmpeg": return Ffmpeg == null ? "FFmpeg" : null;
                case "soffice": return Soffice == null ? "LibreOffice" : null;
                case "gs": return Ghostscript == null ? "Ghostscript" : null;
                case "aseprite":
                    return via == "ffmpeg" && Ffmpeg == null ? "FFmpeg" : null;
                case "data":
                    if (via != null && via.Contains("soffice")) return Soffice == null ? "LibreOffice" : null;
                    return null;
                default: return null; // image, img2pdf, pdf: Windows'un kendi bileşenleri
            }
        }

        private static string? FirstExisting(params string?[] paths)
        {
            foreach (var p in paths)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }
            return null;
        }

        private static IEnumerable<string> PathDirs()
        {
            // Süreç başladıktan sonra kurulan araçları da görmek için PATH'i kayıt defterinden taze oku.
            var all = new List<string>();
            foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string? value = null;
                try { value = Environment.GetEnvironmentVariable("PATH", target); } catch { }
                if (string.IsNullOrEmpty(value)) continue;
                foreach (var part in value.Split(';'))
                {
                    string d = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
                    if (d.Length > 0 && !all.Contains(d, StringComparer.OrdinalIgnoreCase)) all.Add(d);
                }
            }
            return all;
        }

        private static string? OnPath(params string[] exeNames)
        {
            foreach (var dir in PathDirs())
            {
                foreach (var exe in exeNames)
                {
                    try
                    {
                        string p = Path.Combine(dir, exe);
                        if (File.Exists(p)) return p;
                    }
                    catch { /* geçersiz PATH girdisi */ }
                }
            }
            return null;
        }

        private static string? SearchUnder(string root, string fileName, int maxDepth)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                return SearchRec(new DirectoryInfo(root), fileName, maxDepth);
            }
            catch
            {
                return null;
            }
        }

        private static string? SearchRec(DirectoryInfo dir, string fileName, int depth)
        {
            try
            {
                var hit = dir.GetFiles(fileName).FirstOrDefault();
                if (hit != null) return hit.FullName;
                if (depth <= 0) return null;
                foreach (var sub in dir.GetDirectories().OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var r = SearchRec(sub, fileName, depth - 1);
                    if (r != null) return r;
                }
            }
            catch { /* erişim yok */ }
            return null;
        }

        private static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        private static string? FindFfmpeg()
        {
            string local = Env(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Env(Environment.SpecialFolder.ProgramFiles);
            return FirstExisting(
                       Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
                       Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
                       Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),   // winget, kullanıcı kapsamı
                       Path.Combine(programFiles, "WinGet", "Links", "ffmpeg.exe"),         // winget, yönetici kapsamı
                       @"C:\ffmpeg\bin\ffmpeg.exe",
                       Path.Combine(Env(Environment.SpecialFolder.UserProfile), "scoop", "shims", "ffmpeg.exe"),
                       @"C:\ProgramData\chocolatey\bin\ffmpeg.exe")
                   ?? OnPath("ffmpeg.exe")
                   ?? SearchUnder(Path.Combine(local, "Microsoft", "WinGet", "Packages"), "ffmpeg.exe", 5)
                   ?? SearchUnder(Path.Combine(programFiles, "WinGet", "Packages"), "ffmpeg.exe", 5)
                   ?? SearchUnder(Path.Combine(programFiles, "ffmpeg"), "ffmpeg.exe", 3);
        }

        private static string? FindSoffice()
        {
            // soffice.com konsol alt sistemidir ve dönüşüm bitene kadar bekler; .exe'yi yedek olarak kullan.
            var roots = new[]
            {
                Path.Combine(Env(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program"),
                Path.Combine(Env(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program"),
            };
            foreach (var r in roots)
            {
                var hit = FirstExisting(Path.Combine(r, "soffice.com"), Path.Combine(r, "soffice.exe"));
                if (hit != null) return hit;
            }
            return OnPath("soffice.com", "soffice.exe");
        }

        private static string? FindGhostscript()
        {
            return OnPath("gswin64c.exe", "gswin32c.exe")
                   ?? SearchUnder(Path.Combine(Env(Environment.SpecialFolder.ProgramFiles), "gs"), "gswin64c.exe", 3)
                   ?? SearchUnder(Path.Combine(Env(Environment.SpecialFolder.ProgramFilesX86), "gs"), "gswin32c.exe", 3);
        }
    }
}
