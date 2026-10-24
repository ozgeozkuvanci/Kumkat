using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Kumkat.Core;

namespace Kumkat
{
    public static class Program
    {
        private const string MutexName = @"Local\Kumkat.Tray.Singleton";

        [STAThread]
        public static int Main(string[] args)
        {
            // Kurulum / kaldırma programının çağırdığı komutlar: arayüz açmadan işini yap ve çık
            if (args.Contains("--cleanup", StringComparer.OrdinalIgnoreCase))
            {
                Kumkat.Shell.Integration.RemoveAll();
                return 0;
            }
            int setupAt = Array.FindIndex(args, a => string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase));
            if (setupAt >= 0)
            {
                var opts = args.Skip(setupAt + 1).Select(a => a.ToLowerInvariant()).ToList();
                // kurulumda seçilen dil: "lang=ar" / "lang=tr" (yoksa Windows diline göre)
                string? lang = opts.FirstOrDefault(o => o.StartsWith("lang=", StringComparison.Ordinal))?.Substring(5);
                if (lang == Loc.Turkish || lang == Loc.Darija)
                {
                    var s = Settings.Load();
                    s.Language = lang;
                    s.Save();
                }
                Kumkat.Shell.Integration.ApplySetup(
                    startup: opts.Contains("startup"),
                    sendTo: opts.Contains("sendto"),
                    contextMenu: opts.Contains("ctxmenu"));
                return 0;
            }

            var files = args
                .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
                .Select(a => { try { return Path.GetFullPath(a.Trim('"')); } catch { return ""; } })
                .Where(File.Exists)
                .ToList();

            using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // Tepside zaten çalışıyor: dosyaları ona ver ve çık (Gönder menüsü / sağ tık / ikinci çift tıklama)
                if (files.Count > 0) TrayApp.SendToRunningInstance(files);
                return 0;
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (_, e) => Log.Error("winforms", e.Exception);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Yakalanmamış hata: {e.ExceptionObject}");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error("task", e.Exception);
                e.SetObserved();
            };

            Log.Write($"Kumkat başlıyor ({string.Join(" ", args)})");
            using var tray = new TrayApp();
            app.Startup += (_, __) =>
            {
                tray.Start();
                if (files.Count > 0) tray.EnqueueFromOutside(files);
            };
            return app.Run();
        }

        private static void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("ui", e.Exception);
            e.Handled = true; // tepsi uygulaması tek bir hatada kapanmasın
        }
    }
}
