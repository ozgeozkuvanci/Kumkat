using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Kumkat.Core;
using Kumkat.Shell;
using Kumkat.UI;
using Forms = System.Windows.Forms;

namespace Kumkat
{
    /// <summary>Sistem tepsisinde yaşayan ana uygulama: menüler, kısayol, Shift+sürükle, iş kuyruğu.</summary>
    internal sealed class TrayApp : IDisposable
    {
        public static string PipeName => "Kumkat.Pipe." + Environment.UserName.Replace(' ', '_');

        private const uint HotkeyVk = 0x4B; // K
        private const string HotkeyText = "Ctrl+Alt+K";

        private readonly Dispatcher _dispatcher;
        private readonly Settings _settings;
        private readonly ToolLocator _tools;
        private readonly Converter _converter;
        private readonly SemaphoreSlim _jobGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

        private RecipeBook _book;
        private Forms.NotifyIcon? _tray;
        private HotkeyListener? _hotkey;
        private ShiftDragWatcher? _dragWatcher;
        private RadialMenuWindow? _openMenu;
        private RadialMenuWindow? _dropOverlay;

        private readonly List<string> _pending = new List<string>();
        private DispatcherTimer? _batchTimer;

        private string? _lastOutput;
        private DateTime _lastToolScan = DateTime.MinValue;
        private int _queued;

        public TrayApp()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _settings = Settings.Load();
            Loc.Apply(_settings.Language);
            _tools = new ToolLocator(_settings);
            _converter = new Converter(_tools);
            _book = RecipeBook.Load();
        }

        // --------------------------------------------------------------- başlat

        public void Start()
        {
            _tray = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Kumkat",
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip(),
            };
            _tray.ContextMenuStrip.Opening += (_, __) => BuildMenu();
            BuildMenu();
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) PickFilesAndConvert();
            };
            _tray.BalloonTipClicked += (_, __) => RevealLastOutput();

            _hotkey = new HotkeyListener();
            _hotkey.Pressed += OnHotkey;
            if (_settings.HotkeyEnabled) _hotkey.Register(Native.MOD_CONTROL | Native.MOD_ALT, HotkeyVk);

            _dragWatcher = new ShiftDragWatcher(_dispatcher);
            _dragWatcher.ShiftDragStarted += OnShiftDrag;
            _dragWatcher.MouseReleased += OnDragReleased;
            _dragWatcher.WheelDuringDrag = delta =>
            {
                var overlay = _dropOverlay;
                if (overlay == null) return false;
                _ = _dispatcher.InvokeAsync(() => overlay.Wheel(delta));
                return true;
            };
            if (_settings.ShiftDragEnabled) _dragWatcher.Start();

            _batchTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(350) };
            _batchTimer.Tick += (_, __) => FlushPending();

            Task.Run(() => PipeLoopAsync(_shutdown.Token));
            Integration.RefreshPaths();

            // Windows açılınca otomatik başla: ilk çalıştırmada varsayılan olarak açık.
            // Kullanıcı tepsi menüsünden ya da kurulumda kapattıysa ona dokunma.
            if (!_settings.StartupConfigured)
            {
                try { Integration.SetStartup(true); }
                catch (Exception ex) { Log.Error("startup-default", ex); }
                _settings.StartupConfigured = true;
                _settings.Save();
            }

            if (!_settings.FirstRunDone)
            {
                _settings.FirstRunDone = true;
                _settings.Save();
                Notify(Loc.T("Kumkat hazır"),
                    Loc.F("Explorer'da Shift basılıyken bir dosyayı sürükle ya da dosyayı seçip {0}'ya bas.", HotkeyText) +
                    (_tools.Ffmpeg == null || _tools.Soffice == null ? Loc.T(" Eksik araçlar için tepsi menüsü › Araçlar.") : ""),
                    Forms.ToolTipIcon.Info);
            }
        }

        private static System.Drawing.Icon LoadIcon()
        {
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kumkat.kumkat.ico");
                if (s != null) return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
            }
            catch (Exception ex)
            {
                Log.Error("icon", ex);
            }
            return System.Drawing.SystemIcons.Application;
        }

        // ----------------------------------------------------------- tepsi menüsü

        private void BuildMenu()
        {
            if (_tray?.ContextMenuStrip == null) return;
            var items = _tray.ContextMenuStrip.Items;
            items.Clear();
            _tray.ContextMenuStrip.RightToLeft = Loc.Rtl ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;

            items.Add(new Forms.ToolStripMenuItem(Loc.T("Dosya seç ve dönüştür…"), null, (_, __) => PickFilesAndConvert()) { Font = Bold() });
            items.Add(new Forms.ToolStripSeparator());

            items.Add(Check(Loc.T("Shift + sürükle ile menü aç"), _settings.ShiftDragEnabled, on =>
            {
                _settings.ShiftDragEnabled = on;
                if (on) _dragWatcher?.Start(); else _dragWatcher?.Stop();
            }));
            items.Add(Check(Loc.F("{0} kısayolu (Explorer seçimi)", HotkeyText), _settings.HotkeyEnabled, on =>
            {
                _settings.HotkeyEnabled = on;
                if (on)
                {
                    if (_hotkey != null && !_hotkey.Register(Native.MOD_CONTROL | Native.MOD_ALT, HotkeyVk))
                        Notify(Loc.T("Kısayol alınamadı"), Loc.F("{0} başka bir uygulama tarafından kullanılıyor.", HotkeyText), Forms.ToolTipIcon.Warning);
                }
                else _hotkey?.Unregister();
            }));
            items.Add(Check(Loc.T("Sağ tık › Gönder › Kumkat"), Integration.SendToInstalled, on => Integration.SetSendTo(on)));
            items.Add(Check(Loc.T("Sağ tık menüsü (klasik / \"Diğer seçenekler\")"), Integration.ContextMenuInstalled, on => Integration.SetContextMenu(on)));
            items.Add(Check(Loc.T("Windows ile başlat"), Integration.StartupEnabled, on =>
            {
                Integration.SetStartup(on);
                _settings.StartupConfigured = true;
            }));
            items.Add(new Forms.ToolStripSeparator());

            var toolsMenu = new Forms.ToolStripMenuItem(Loc.T("Araçlar"));
            AddToolStatus(toolsMenu, "FFmpeg", _tools.Ffmpeg, Loc.T("ses, video, WebP/AVIF/ICO"), "Gyan.FFmpeg");
            AddToolStatus(toolsMenu, "LibreOffice", _tools.Soffice, Loc.T("Word, Excel, PowerPoint, PDF→DOCX"), "TheDocumentFoundation.LibreOffice");
            AddToolStatus(toolsMenu, "Ghostscript", _tools.Ghostscript, Loc.T("PDF sıkıştırma (isteğe bağlı)"), "ArtifexSoftware.GhostScript");
            toolsMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            toolsMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T("Yeniden tara"), null, (_, __) =>
            {
                _tools.Refresh();
                string Has(string? p) => Loc.T(p != null ? "var" : "yok");
                Notify(Loc.T("Araçlar tarandı"),
                    $"FFmpeg: {Has(_tools.Ffmpeg)} · LibreOffice: {Has(_tools.Soffice)} · Ghostscript: {Has(_tools.Ghostscript)}",
                    Forms.ToolTipIcon.Info);
            }));
            items.Add(toolsMenu);

            var advanced = new Forms.ToolStripMenuItem(Loc.T("Gelişmiş"));
            advanced.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T("Tarifleri düzenle (recipes.json)…"), null, (_, __) => EditRecipes()));
            advanced.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T("Tarifleri yeniden yükle"), null, (_, __) => ReloadRecipes()));
            advanced.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T("Günlüğü aç"), null, (_, __) => OpenWithShell(Path.Combine(AppPaths.DataDir, "kumkat.log"))));
            advanced.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T("Ayarlar klasörü"), null, (_, __) => OpenWithShell(AppPaths.DataDir)));
            items.Add(advanced);

            var langMenu = new Forms.ToolStripMenuItem("Dil / اللغة");
            AddLanguage(langMenu, Loc.T("Otomatik (Windows dili)"), null);
            AddLanguage(langMenu, "Türkçe", Loc.Turkish);
            AddLanguage(langMenu, "الدارجة المغربية", Loc.Darija);
            items.Add(langMenu);

            items.Add(new Forms.ToolStripSeparator());
            items.Add(new Forms.ToolStripMenuItem(Loc.T("Çıkış"), null, (_, __) => Exit()));
        }

        private void AddLanguage(Forms.ToolStripMenuItem parent, string text, string? lang)
        {
            var item = new Forms.ToolStripMenuItem(text) { Checked = _settings.Language == lang };
            item.Click += (_, __) =>
            {
                _settings.Language = lang;
                _settings.Save();
                Loc.Apply(lang);
                BuildMenu(); // menü bir sonraki açılışta yeni dilde
            };
            parent.DropDownItems.Add(item);
        }

        private static System.Drawing.Font Bold() => new System.Drawing.Font(Forms.SystemInformation.MenuFont, System.Drawing.FontStyle.Bold);

        private Forms.ToolStripMenuItem Check(string text, bool isChecked, Action<bool> apply)
        {
            var item = new Forms.ToolStripMenuItem(text) { Checked = isChecked, CheckOnClick = true };
            item.CheckedChanged += (_, __) =>
            {
                try
                {
                    apply(item.Checked);
                    _settings.Save();
                }
                catch (Exception ex)
                {
                    Log.Error("menu-toggle", ex);
                    Notify(Loc.T("Ayar değiştirilemedi"), ex.Message, Forms.ToolTipIcon.Error);
                }
            };
            return item;
        }

        private void AddToolStatus(Forms.ToolStripMenuItem parent, string name, string? path, string purpose, string wingetId)
        {
            if (path != null)
            {
                parent.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.F("✓ {0} — {1}", name, purpose)) { Enabled = false, ToolTipText = path });
            }
            else
            {
                parent.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.F("✗ {0} yok — {1}", name, purpose)) { Enabled = false });
                parent.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.F("    {0}'i kur (winget)", name), null, (_, __) => WingetInstall(wingetId)));
            }
        }

        private void WingetInstall(string id)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = true,
                    Arguments = $"/c winget install --id {id} -e --accept-source-agreements --accept-package-agreements & echo. & echo Bitti. Kumkat tepsi menusu ^> Araclar ^> Yeniden tara & pause",
                    WorkingDirectory = Path.GetTempPath(),
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Notify(Loc.T("winget başlatılamadı"), ex.Message, Forms.ToolTipIcon.Error);
            }
        }

        private void EditRecipes()
        {
            string path = RecipeBook.UserRecipesPath;
            if (!File.Exists(path)) File.WriteAllText(path, RecipeBook.ReadEmbeddedRecipes(), new UTF8Encoding(false));
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch
            {
                OpenWithShell(path);
            }
        }

        private void ReloadRecipes()
        {
            try
            {
                _book = RecipeBook.Load();
                Notify(Loc.T("Tarifler yüklendi"), Loc.F("{0} dönüşüm hazır.", _book.File.Groups.Sum(g => g.Actions.Count)), Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Notify(Loc.T("recipes.json okunamadı"), ex.Message, Forms.ToolTipIcon.Error);
            }
        }

        private static void OpenWithShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("open", ex); }
        }

        // ---------------------------------------------------------- giriş yolları

        /// <summary>Komut satırından, Gönder menüsünden veya ikinci örnekten gelen dosyalar.</summary>
        public void EnqueueFromOutside(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (!_pending.Contains(p, StringComparer.OrdinalIgnoreCase)) _pending.Add(p);
            }
            // klasik sağ tık menüsü çoklu seçimde her dosya için ayrı süreç açar: kısa süre toplayıp tek menü göster
            _batchTimer?.Stop();
            _batchTimer?.Start();
        }

        private void FlushPending()
        {
            _batchTimer?.Stop();
            if (_pending.Count == 0) return;
            var files = _pending.ToList();
            _pending.Clear();
            ShowMenuFor(files);
        }

        private void PickFilesAndConvert()
        {
            using var dlg = new Forms.OpenFileDialog
            {
                Title = Loc.T("Dönüştürülecek dosyaları seç"),
                Multiselect = true,
                Filter = Loc.T("Tüm dosyalar (*.*)|*.*"),
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK && dlg.FileNames.Length > 0)
                ShowMenuFor(dlg.FileNames);
        }

        private void OnHotkey()
        {
            IntPtr fg = Native.GetForegroundWindow();
            var files = ExplorerSelection.GetSelectedFiles(fg);

            if (files.Count == 0)
            {
                // yedek: panoya kopyalanmış dosyalar (Ctrl+C)
                try
                {
                    if (System.Windows.Clipboard.ContainsFileDropList())
                        files = System.Windows.Clipboard.GetFileDropList().Cast<string>().Where(File.Exists).ToList();
                }
                catch (Exception ex)
                {
                    Log.Write($"Pano okunamadı: {ex.Message}");
                }
            }

            if (files.Count == 0)
            {
                Notify(Loc.T("Seçili dosya yok"), Loc.F("Explorer'da dosyaları seçip {0}'ya bas (masaüstü için önce Ctrl+C).", HotkeyText), Forms.ToolTipIcon.Info);
                return;
            }
            ShowMenuFor(files);
        }

        // ------------------------------------------------------------- menüler

        private MenuModel? BuildModel(IReadOnlyList<string> rawFiles)
        {
            var files = rawFiles.Where(File.Exists).ToList();
            RecipeGroup? group = null;
            foreach (var f in files)
            {
                group = _book.GroupFor(f);
                if (group != null) break;
            }
            if (group == null) return null;

            var same = files.Where(f => _book.GroupFor(f)?.Id == group.Id).ToList();
            string inExt = RecipeBook.ExtOf(same[0]);
            // Kumkat açıkken FFmpeg/LibreOffice kurulmuş olabilir: eksik araç varsa yeniden ara
            if ((_tools.Ffmpeg == null || _tools.Soffice == null || _tools.Ghostscript == null) &&
                DateTime.Now - _lastToolScan > TimeSpan.FromSeconds(5))
            {
                _lastToolScan = DateTime.Now;
                _tools.Refresh();
            }
            var entries = _book.EntriesFor(group, inExt, _tools);
            if (entries.Count == 0) return null;

            // tarifleri halka sayfalarına böl; boş sayfaları atla
            var pages = new List<MenuPage>();
            int pageCount = Math.Max(1, entries.Max(e => e.Page) + 1);
            for (int i = 0; i < pageCount; i++)
            {
                var onPage = entries.Where(e => e.Page == i).ToList();
                if (onPage.Count == 0) continue;
                string pageTitle = group.Pages != null && i < group.Pages.Count ? Loc.T(group.Pages[i])
                                 : i == 0 ? Loc.T("Dönüştür")
                                 : Loc.F("Sayfa {0}", i + 1);
                pages.Add(new MenuPage(pageTitle, onPage));
            }

            string sub = RadialMenuWindow.DescribeFiles(same);
            int skipped = files.Count - same.Count;
            if (skipped > 0) sub += Loc.F(" (+{0} farklı tür atlandı)", skipped);
            return new MenuModel(same, pages, RadialMenuWindow.TitleFor(same), sub);
        }

        public void ShowMenuFor(IReadOnlyList<string> files)
        {
            var model = BuildModel(files);
            if (model == null)
            {
                string ext = files.Count > 0 ? RecipeBook.ExtOf(files[0]).ToUpperInvariant() : "?";
                Notify(Loc.T("Desteklenmeyen dosya"), Loc.F(".{0} için tanımlı dönüşüm yok. (Gelişmiş › Tarifleri düzenle)", ext), Forms.ToolTipIcon.Warning);
                return;
            }

            _openMenu?.Cancel();
            var menu = new RadialMenuWindow(dropMode: false);
            menu.SetModel(model);
            menu.Finished += (entry, m) =>
            {
                if (ReferenceEquals(_openMenu, menu)) _openMenu = null;
                if (entry != null && m != null) Start(m.Files, entry);
            };
            _openMenu = menu;
            menu.ShowAtCursor();
        }

        private void OnShiftDrag(Native.POINT pt)
        {
            if (_dropOverlay != null) return;
            _openMenu?.Cancel();

            var overlay = new RadialMenuWindow(dropMode: true) { FilesProvider = BuildModel };
            overlay.Finished += (entry, m) =>
            {
                if (ReferenceEquals(_dropOverlay, overlay)) _dropOverlay = null;
                if (entry != null && m != null) Start(m.Files, entry);
            };
            _dropOverlay = overlay;
            overlay.ShowAt(pt);
        }

        private void OnDragReleased()
        {
            var overlay = _dropOverlay;
            if (overlay == null) return;
            // Bırakma (Drop) olayı fare bırakıldıktan hemen sonra gelir; ona fırsat ver, gelmezse kapat
            var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                if (ReferenceEquals(_dropOverlay, overlay)) overlay.Cancel();
            };
            timer.Start();
        }

        // ------------------------------------------------------------ iş kuyruğu

        private void Start(IReadOnlyList<string> files, MenuEntry entry)
        {
            if (entry.Action.Interactive == "crop") StartCrop(files, entry);
            else RunJob(files, entry);
        }

        /// <summary>Serbest kırpma: önizlemede alan seçtir, sonra seçilen oranlarla tarifi çalıştır.</summary>
        private async void StartCrop(IReadOnlyList<string> files, MenuEntry entry)
        {
            try
            {
                UpdateTrayText(Loc.T("önizleme hazırlanıyor…"));
                var preview = await _converter.LoadPreviewAsync(files[0], entry.Group, _shutdown.Token);
                UpdateTrayText();

                var win = new CropWindow(preview, Path.GetFileName(files[0]), files.Count);
                win.ShowDialog();
                if (win.Result is System.Windows.Rect r)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var vars = new Dictionary<string, string>
                    {
                        ["fx"] = r.X.ToString("0.######", inv),
                        ["fy"] = r.Y.ToString("0.######", inv),
                        ["fw"] = r.Width.ToString("0.######", inv),
                        ["fh"] = r.Height.ToString("0.######", inv),
                    };
                    RunJob(files, entry, vars);
                }
            }
            catch (Exception ex)
            {
                UpdateTrayText();
                Log.Error("crop", ex);
                Notify(Loc.T("Kırpma açılamadı"), ex.Message, Forms.ToolTipIcon.Error);
            }
        }

        private async void RunJob(IReadOnlyList<string> files, MenuEntry entry, IReadOnlyDictionary<string, string>? vars = null)
        {
            Interlocked.Add(ref _queued, files.Count);
            UpdateTrayText();
            await _jobGate.WaitAsync();
            try
            {
                var outputs = new List<string>();
                var errors = new List<string>();
                var sw = Stopwatch.StartNew();

                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    UpdateTrayText($"{entry.Label} {i + 1}/{files.Count}: {Path.GetFileName(file)}");
                    try
                    {
                        string output = await Task.Run(() => _converter.ConvertAsync(file, entry.Group, entry.Action, _shutdown.Token, vars));
                        outputs.Add(output);
                        Log.Write($"OK  {file} -> {output}");
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                        Log.Error($"dönüşüm {file} -> {entry.Action.Id}", ex);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _queued);
                    }
                }

                if (outputs.Count > 0) _lastOutput = outputs[outputs.Count - 1];
                string took = sw.Elapsed.TotalSeconds < 1 ? "" : Loc.F(" ({0:0.#} sn)", sw.Elapsed.TotalSeconds);

                if (errors.Count == 0)
                {
                    string what = outputs.Count == 1 ? Path.GetFileName(outputs[0]) : Loc.F("{0} dosya {1} oldu", outputs.Count, entry.Label);
                    Notify(Loc.T("✓ Hazır") + took, what + Loc.T(" — göstermek için tıkla"), Forms.ToolTipIcon.Info);
                }
                else
                {
                    string head = outputs.Count > 0 ? Loc.F("{0} tamam, {1} hata", outputs.Count, errors.Count) : Loc.T("Dönüştürülemedi");
                    Notify(head, string.Join("\n", errors.Take(3)), Forms.ToolTipIcon.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Error("job", ex);
            }
            finally
            {
                _jobGate.Release();
                UpdateTrayText();
            }
        }

        private void UpdateTrayText(string? status = null)
        {
            if (_tray == null) return;
            string text = status != null ? Loc.F("Kumkat — {0}", status)
                        : _queued > 0 ? Loc.F("Kumkat — {0} dosya sırada", _queued) : "Kumkat";
            _tray.Text = text.Length > 120 ? text.Substring(0, 119) + "…" : text;
        }

        private void RevealLastOutput()
        {
            string? path = _lastOutput;
            if (path == null) return;
            try
            {
                if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                else if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("reveal", ex);
            }
        }

        private void Notify(string title, string text, Forms.ToolTipIcon icon)
        {
            if (_tray == null) return;
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = string.IsNullOrWhiteSpace(text) ? " " : text;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(4000);
        }

        // ------------------------------------------------------- ikinci örnek

        private async Task PipeLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                    var paths = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    if (paths.Count > 0) _ = _dispatcher.InvokeAsync(() => EnqueueFromOutside(paths));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("pipe", ex);
                    try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>Çalışan örneğe dosya yollarını iletir. Çalışan örnek yoksa false.</summary>
        public static bool SendToRunningInstance(IReadOnlyList<string> files)
        {
            try
            {
                // tepsideki örnek menüyü öne getirebilsin
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000);
                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                writer.Write(string.Join("\n", files));
                writer.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("pipe-client", ex);
                return false;
            }
        }

        // --------------------------------------------------------------- çıkış

        private void Exit()
        {
            Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (_shutdown.IsCancellationRequested) return;
            _shutdown.Cancel();
            _dragWatcher?.Dispose();
            _hotkey?.Dispose();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
        }
    }
}
