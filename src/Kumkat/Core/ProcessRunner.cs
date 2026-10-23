using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kumkat.Core
{
    internal sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string stdErr, string stdOut)
        {
            ExitCode = exitCode;
            StdErr = stdErr;
            StdOut = stdOut;
        }

        public int ExitCode { get; }
        public string StdErr { get; }
        public string StdOut { get; }
    }

    internal static class ProcessRunner
    {
        /// <summary>Kabuk kullanmadan çalıştırır; argümanlar tek tek geçer (tırnak sorunu yok).</summary>
        public static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = AppPaths.TempRoot,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            Log.Write($"> {exe} {string.Join(" ", psi.ArgumentList)}");

            using var p = new Process { StartInfo = psi };
            var err = new StringBuilder();
            var output = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };

            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await p.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException($"{System.IO.Path.GetFileName(exe)} {timeout.TotalMinutes:0} dakikada bitmedi.");
            }

            // Asenkron okuyucuların kalanını boşaltması için
            p.WaitForExit();

            string e2, o2;
            lock (err) e2 = err.ToString();
            lock (output) o2 = output.ToString();
            if (p.ExitCode != 0) Log.Write($"  çıkış kodu {p.ExitCode}: {Tail(e2, 600)}");
            return new ProcessResult(p.ExitCode, e2, o2);
        }

        public static string Tail(string s, int max)
        {
            s = s.Trim();
            return s.Length <= max ? s : "…" + s.Substring(s.Length - max);
        }
    }
}
