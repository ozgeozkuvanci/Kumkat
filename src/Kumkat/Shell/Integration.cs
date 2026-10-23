using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Kumkat.Core;

namespace Kumkat.Shell
{
    /// <summary>Gönder menüsü kısayolu, klasik sağ tık menüsü ve Windows ile başlatma (hepsi kullanıcı düzeyinde, yönetici gerekmez).</summary>
    internal static class Integration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string VerbKey = @"Software\Classes\*\shell\Kumkat";
        private const string AppName = "Kumkat";

        private static string Exe => AppPaths.ExePath;

        // ------------------------------------------------------- Gönder (SendTo)

        private static string SendToLink =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Kumkat ile dönüştür.lnk");

        public static bool SendToInstalled => File.Exists(SendToLink);

        public static void SetSendTo(bool enable)
        {
            if (!enable)
            {
                if (File.Exists(SendToLink)) File.Delete(SendToLink);
                return;
            }

            Type? t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new InvalidOperationException("WScript.Shell kullanılamıyor.");
            object? shellObj = Activator.CreateInstance(t);
            if (shellObj == null) throw new InvalidOperationException("WScript.Shell oluşturulamadı.");
            try
            {
                dynamic shell = shellObj;
                dynamic link = shell.CreateShortcut(SendToLink);
                link.TargetPath = Exe;
                link.IconLocation = Exe + ",0";
                link.Description = "Seçili dosyaları Kumkat ile dönüştür";
                link.WorkingDirectory = Path.GetDirectoryName(Exe);
                link.Save();
                Marshal.FinalReleaseComObject(link);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellObj);
            }
        }

        // ------------------------------------------------- klasik sağ tık menüsü

        public static bool ContextMenuInstalled
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(VerbKey + @"\command");
                return k != null;
            }
        }

        public static void SetContextMenu(bool enable)
        {
            if (!enable)
            {
                Registry.CurrentUser.DeleteSubKeyTree(VerbKey, false);
                return;
            }
            using (var verb = Registry.CurrentUser.CreateSubKey(VerbKey))
            {
                verb.SetValue("", "Kumkat ile dönüştür");
                verb.SetValue("Icon", "\"" + Exe + "\",0");
            }
            using (var cmd = Registry.CurrentUser.CreateSubKey(VerbKey + @"\command"))
            {
                cmd.SetValue("", "\"" + Exe + "\" \"%1\"");
            }
        }

        // ------------------------------------------------- Windows ile başlat

        public static bool StartupEnabled
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(AppName) is string;
            }
        }

        public static void SetStartup(bool enable)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable) k.SetValue(AppName, "\"" + Exe + "\"");
            else k.DeleteValue(AppName, false);
        }

        /// <summary>Kurulum programı çağırır: seçilenleri açar, seçilmeyenleri kapatır.</summary>
        public static void ApplySetup(bool startup, bool sendTo, bool contextMenu)
        {
            Try("setup-startup", () => SetStartup(startup));
            Try("setup-sendto", () => SetSendTo(sendTo));
            Try("setup-context", () => SetContextMenu(contextMenu));
        }

        /// <summary>Kaldırma sırasında çağrılır: Kumkat'ın Windows'a eklediği her şeyi siler.</summary>
        public static void RemoveAll() => ApplySetup(false, false, false);

        private static void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error(what, ex); }
        }

        /// <summary>Uygulama başka bir klasöre taşındıysa kayıtlı yolları güncelle.</summary>
        public static void RefreshPaths()
        {
            try
            {
                if (StartupEnabled) SetStartup(true);
                if (ContextMenuInstalled) SetContextMenu(true);
                if (SendToInstalled) SetSendTo(true);
            }
            catch (Exception ex)
            {
                Log.Error("integration-refresh", ex);
            }
        }
    }
}
