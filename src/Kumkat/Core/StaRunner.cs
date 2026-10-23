using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kumkat.Core
{
    /// <summary>WPF görüntüleme API'lerini arka planda ayrı bir STA iş parçacığında çalıştırır.</summary>
    internal static class StaRunner
    {
        public static Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
                finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Kumkat-STA";
            thread.Start();
            return tcs.Task;
        }

        public static Task Run(Action work) => Run<bool>(() => { work(); return true; });
    }
}
