using System;
using System.Windows.Interop;
using Kumkat.Core;

namespace Kumkat.Shell
{
    /// <summary>Genel kısayol tuşu (varsayılan Ctrl+Alt+K) için görünmez mesaj penceresi.</summary>
    internal sealed class HotkeyListener : IDisposable
    {
        private const int HotkeyId = 0x4B4B; // "KK"
        private readonly HwndSource _source;
        private bool _registered;

        public event Action? Pressed;

        public HotkeyListener()
        {
            var p = new HwndSourceParameters("KumkatHotkey")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE: yalnızca mesaj alan pencere
            };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        public bool IsRegistered => _registered;

        /// <param name="vk">Sanal tuş kodu, ör. 'K' = 0x4B.</param>
        public bool Register(uint modifiers, uint vk)
        {
            Unregister();
            _registered = Native.RegisterHotKey(_source.Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, vk);
            if (!_registered) Log.Write("Kısayol kaydedilemedi (başka bir uygulama kullanıyor olabilir).");
            return _registered;
        }

        public void Unregister()
        {
            if (!_registered) return;
            Native.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                Pressed?.Invoke();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister();
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }
}
