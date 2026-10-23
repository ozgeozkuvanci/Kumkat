using System;
using System.IO;
using System.Text;

namespace Kumkat.Core
{
    internal static class AppPaths
    {
        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kumkat");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string TempRoot
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "Kumkat");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string NewTempDir()
        {
            string dir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Kumkat.exe");

        /// <summary>"klasör\ad.uzantı" yoksa onu, varsa "ad (1).uzantı", "ad (2).uzantı" ... döndürür.</summary>
        public static string UniqueFile(string dir, string stem, string ext)
        {
            string candidate = Path.Combine(dir, stem + "." + ext);
            int i = 1;
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{stem} ({i}).{ext}");
                i++;
            }
            return candidate;
        }

        public static string UniqueDir(string dir, string name)
        {
            string candidate = Path.Combine(dir, name);
            int i = 1;
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{name} ({i})");
                i++;
            }
            return candidate;
        }

        public static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* geçici klasör, önemsiz */ }
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string file = Path.Combine(AppPaths.DataDir, "kumkat.log");
                    var fi = new FileInfo(file);
                    if (fi.Exists && fi.Length > 2_000_000) fi.Delete();
                    File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch
            {
                // log yazılamazsa uygulama durmasın
            }
        }

        public static void Error(string context, Exception ex) => Write($"HATA [{context}] {ex}");
    }
}
