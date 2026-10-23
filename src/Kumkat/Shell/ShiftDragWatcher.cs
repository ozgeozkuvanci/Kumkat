using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Kumkat.Core;

namespace Kumkat.Shell
{
    /// <summary>
    /// Düşük seviye fare kancasıyla Explorer'dan (veya masaüstünden) başlayan sürüklemeyi izler.
    /// Sürükleme sırasında Shift basılıysa <see cref="ShiftDragStarted"/> tetiklenir.
    /// Kanca girdiyi asla engellemez, yalnızca gözlemler.
    /// </summary>
    internal sealed class ShiftDragWatcher : IDisposable
    {
        private const int DragThreshold = 12; // fiziksel piksel

        private readonly Dispatcher _dispatcher;
        private readonly Native.LowLevelMouseProc _proc; // GC toplamasın diye alanda tutulur
        private readonly Dictionary<uint, bool> _pidIsShell = new Dictionary<uint, bool>();
        private IntPtr _hook = IntPtr.Zero;

        private bool _down;
        private bool _armed;
        private bool _fired;
        private Native.POINT _downPt;

        public event Action<Native.POINT>? ShiftDragStarted;
        public event Action? MouseReleased;

        public ShiftDragWatcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _proc = HookProc;
        }

        public bool IsRunning => _hook != IntPtr.Zero;

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                Log.Write($"Fare kancası kurulamadı: {Marshal.GetLastWin32Error()}");
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _down = _armed = _fired = false;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                    switch (msg)
                    {
                        case Native.WM_LBUTTONDOWN:
                            _down = true;
                            _fired = false;
                            _downPt = data.pt;
                            _armed = IsShellWindowAt(data.pt);
                            break;

                        case Native.WM_MOUSEMOVE:
                            if (_down && _armed && !_fired && Moved(data.pt) && Native.IsShiftDown())
                            {
                                _fired = true;
                                var pt = data.pt;
                                _dispatcher.InvokeAsync(() => ShiftDragStarted?.Invoke(pt));
                            }
                            break;

                        case Native.WM_LBUTTONUP:
                            if (_fired) _dispatcher.InvokeAsync(() => MouseReleased?.Invoke());
                            _down = _armed = _fired = false;
                            break;
                    }
                }
                catch
                {
                    // kanca içinde asla istisna fırlatma
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool Moved(Native.POINT p)
        {
            int dx = p.X - _downPt.X, dy = p.Y - _downPt.Y;
            return dx * dx + dy * dy >= DragThreshold * DragThreshold;
        }

        /// <summary>Tıklanan pencere Explorer'a (dosya gezgini ya da masaüstü) mi ait?</summary>
        private bool IsShellWindowAt(Native.POINT pt)
        {
            IntPtr hwnd = Native.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
            Native.GetWindowThreadProcessId(root == IntPtr.Zero ? hwnd : root, out uint pid);
            if (pid == 0) return false;

            if (_pidIsShell.TryGetValue(pid, out bool cached)) return cached;
            bool isShell;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                isShell = string.Equals(p.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                isShell = false;
            }
            if (_pidIsShell.Count > 64) _pidIsShell.Clear();
            _pidIsShell[pid] = isShell;
            return isShell;
        }

        public void Dispose() => Stop();
    }
}
