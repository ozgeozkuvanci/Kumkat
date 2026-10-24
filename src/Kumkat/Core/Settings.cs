using System;
using System.IO;
using System.Text.Json;

namespace Kumkat.Core
{
    internal sealed class Settings
    {
        public bool ShiftDragEnabled { get; set; } = true;
        public bool HotkeyEnabled { get; set; } = true;
        public bool FirstRunDone { get; set; }

        /// <summary>Windows ile başlatma bir kez ayarlandı mı (ilk çalıştırmada varsayılan olarak açılır; sonra kullanıcının seçimi korunur).</summary>
        public bool StartupConfigured { get; set; }

        /// <summary>"tr", "ar" (Fas Arapçası) veya null = Windows diline göre.</summary>
        public string? Language { get; set; }

        /// <summary>Boş bırakılırsa otomatik bulunur.</summary>
        public string? FfmpegPath { get; set; }
        public string? SofficePath { get; set; }
        public string? GhostscriptPath { get; set; }

        private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options);
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                Log.Error("settings-load", ex);
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            }
            catch (Exception ex)
            {
                Log.Error("settings-save", ex);
            }
        }
    }
}
