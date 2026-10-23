using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Kumkat.Core;

namespace Kumkat.Shell
{
    /// <summary>Öndeki Explorer penceresinde seçili dosyaları Shell.Application COM nesnesiyle okur.</summary>
    internal static class ExplorerSelection
    {
        public static List<string> GetSelectedFiles(IntPtr foregroundWindow)
        {
            var result = new List<string>();
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return result;

            object? shellObj = null;
            try
            {
                shellObj = Activator.CreateInstance(shellType);
                if (shellObj == null) return result;
                dynamic shell = shellObj;
                dynamic windows = shell.Windows();
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic? w = null;
                    try { w = windows.Item(i); } catch { }
                    if (w == null) continue;

                    long hwnd;
                    try { hwnd = Convert.ToInt64(w.HWND); } catch { continue; }
                    if (new IntPtr(hwnd) != foregroundWindow) continue;

                    try
                    {
                        dynamic items = w.Document.SelectedItems();
                        int n = items.Count;
                        for (int k = 0; k < n; k++)
                        {
                            dynamic item = items.Item(k);
                            string path = item.Path;
                            if (!string.IsNullOrEmpty(path) && File.Exists(path)) result.Add(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Explorer seçimi okunamadı: {ex.Message}");
                    }

                    // Windows 11 sekmeleri aynı HWND'yi paylaşır: seçimi olan ilk sekmeyi al
                    if (result.Count > 0) break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("explorer-selection", ex);
            }
            finally
            {
                if (shellObj != null && Marshal.IsComObject(shellObj)) Marshal.FinalReleaseComObject(shellObj);
            }
            return result;
        }
    }
}
