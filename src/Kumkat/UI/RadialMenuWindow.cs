using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Kumkat.Core;
using ShapePath = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Kumkat.UI
{
    /// <summary>Menüde gösterilecek dosyalar ve dilimler.</summary>
    internal sealed class MenuModel
    {
        public MenuModel(IReadOnlyList<string> files, List<MenuEntry> entries, string title, string sub)
        {
            Files = files;
            Entries = entries;
            Title = title;
            Sub = sub;
        }

        public IReadOnlyList<string> Files { get; }
        public List<MenuEntry> Entries { get; }
        public string Title { get; }
        public string Sub { get; }
    }

    /// <summary>
    /// İmlecin etrafında açılan dairesel format menüsü.
    /// Normal kip: fare veya ok tuşları + Enter ile seçilir, Esc iptal eder.
    /// Bırakma kipi (Shift+sürükle): dosya bir dilimin üzerine bırakılınca o dönüşüm başlar.
    /// </summary>
    internal sealed class RadialMenuWindow : Window
    {
        private const double Dim = 380;
        private const double C = Dim / 2;
        private const double Ri = 62;
        private const double Ro = 162;
        private const double GapDeg = 1.6;

        private static readonly Brush RingBrush = Frozen(Color.FromArgb(0xF2, 0x1E, 0x20, 0x25));
        private static readonly Brush SectorBrush = Frozen(Color.FromArgb(0xF2, 0x27, 0x2A, 0x31));
        private static readonly Brush ToolBrush = Frozen(Color.FromArgb(0xF2, 0x22, 0x31, 0x36));
        private static readonly Brush HoverBrush = Frozen(Color.FromRgb(0xFF, 0x7A, 0x1A));
        private static readonly Brush DisabledHoverBrush = Frozen(Color.FromRgb(0x4A, 0x4D, 0x55));
        private static readonly Brush StrokeBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        private static readonly Brush CenterBrush = Frozen(Color.FromRgb(0x14, 0x15, 0x19));
        private static readonly Brush Accent = Frozen(Color.FromRgb(0xFF, 0x8C, 0x33));
        private static readonly Brush TextBrush = Frozen(Colors.White);
        private static readonly Brush SubBrush = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        private static readonly Brush ToolTextBrush = Frozen(Color.FromRgb(0x7F, 0xD6, 0xC8));
        private static readonly Brush HitBrush = Frozen(Color.FromArgb(1, 0, 0, 0)); // görünmez ama fare/sürükleme yakalar

        private readonly bool _dropMode;
        private readonly Grid _root;
        private readonly Canvas _canvas;
        private readonly List<ShapePath> _sectors = new List<ShapePath>();
        private readonly List<TextBlock[]> _labels = new List<TextBlock[]>();
        private readonly TextBlock _centerTitle;
        private readonly TextBlock _centerSub;

        private MenuModel? _model;
        private int _hover = -1;
        private bool _finished;
        private bool _downSeen; // bu pencerede başlamış bir tıklama mı?
        private Native.POINT _anchor;

        /// <summary>Seçim yapıldığında (veya iptalde entry=null) bir kez çağrılır.</summary>
        public event Action<MenuEntry?, MenuModel?>? Finished;

        /// <summary>Bırakma kipinde, sürüklenen dosyalar ilk görüldüğünde menüyü kurmak için çağrılır.</summary>
        public Func<IReadOnlyList<string>, MenuModel?>? FilesProvider { get; set; }

        public RadialMenuWindow(bool dropMode)
        {
            _dropMode = dropMode;

            Title = "Kumkat";
            Width = Dim;
            Height = Dim;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = !dropMode;
            WindowStartupLocation = WindowStartupLocation.Manual;
            AllowDrop = dropMode;
            UseLayoutRounding = true;

            _canvas = new Canvas { Width = Dim, Height = Dim, Background = HitBrush };

            _centerTitle = new TextBlock
            {
                Foreground = TextBrush,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _centerSub = new TextBlock
            {
                Foreground = SubBrush,
                FontSize = 10.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 30,
            };

            _root = new Grid
            {
                Width = Dim,
                Height = Dim,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.86, 0.86),
                Opacity = 0,
            };
            _root.Children.Add(_canvas);
            Content = _root;

            MouseMove += (_, e) => UpdateHover(e.GetPosition(_canvas));
            MouseLeftButtonDown += (_, __) => _downSeen = true;
            MouseLeftButtonUp += OnClick;
            MouseRightButtonUp += (_, __) => Complete(null);
            KeyDown += OnKey;
            Deactivated += (_, __) => { if (!_dropMode) Complete(null); };
            Loaded += (_, __) => AnimateIn();

            if (dropMode)
            {
                DragEnter += OnDragEnter;
                DragOver += OnDragOver;
                DragLeave += (_, __) => SetHover(-1);
                Drop += OnDrop;
            }

            SetModel(null, dropMode ? "Bırak" : "", dropMode ? "Dosyayı bir dilime bırak" : "");
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ------------------------------------------------------------ kurulum

        public void SetModel(MenuModel? model) => SetModel(model, model?.Title ?? "", model?.Sub ?? "");

        private void SetModel(MenuModel? model, string title, string sub)
        {
            _model = model;
            _hover = -1;
            _sectors.Clear();
            _labels.Clear();
            _canvas.Children.Clear();

            var shadowHost = new Ellipse
            {
                Width = Ro * 2,
                Height = Ro * 2,
                Fill = RingBrush,
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(shadowHost, C - Ro);
            Canvas.SetTop(shadowHost, C - Ro);
            _canvas.Children.Add(shadowHost);

            var entries = model?.Entries ?? new List<MenuEntry>();
            int n = entries.Count;
            if (n > 0)
            {
                double step = 360.0 / n;
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    double mid = i * step;
                    var path = new ShapePath
                    {
                        Data = SectorGeometry(mid - step / 2 + GapDeg / 2, mid + step / 2 - GapDeg / 2, Ri + 3, Ro - 3),
                        Fill = entry.IsTool ? ToolBrush : SectorBrush,
                        Stroke = StrokeBrush,
                        StrokeThickness = 1,
                        IsHitTestVisible = false,
                        Opacity = entry.Enabled ? 1.0 : 0.55,
                    };
                    _canvas.Children.Add(path);
                    _sectors.Add(path);

                    var label = new TextBlock
                    {
                        Text = entry.Label,
                        FontSize = n > 10 ? 13 : 15,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = entry.Enabled ? TextBrush : SubBrush,
                        TextAlignment = TextAlignment.Center,
                    };
                    var subLabel = new TextBlock
                    {
                        Text = entry.Sub ?? (entry.IsTool ? "araç" : ""),
                        FontSize = 10,
                        Foreground = entry.IsTool ? ToolTextBrush : SubBrush,
                        TextAlignment = TextAlignment.Center,
                        Visibility = string.IsNullOrEmpty(entry.Sub) && !entry.IsTool ? Visibility.Collapsed : Visibility.Visible,
                    };
                    var stack = new StackPanel { Width = 86, IsHitTestVisible = false, Opacity = entry.Enabled ? 1.0 : 0.5 };
                    stack.Children.Add(label);
                    stack.Children.Add(subLabel);
                    stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Point p = Polar((Ri + Ro) / 2 + 4, mid);
                    Canvas.SetLeft(stack, p.X - 43);
                    Canvas.SetTop(stack, p.Y - stack.DesiredSize.Height / 2);
                    _canvas.Children.Add(stack);
                    _labels.Add(new[] { label, subLabel });
                }
            }

            var center = new Ellipse
            {
                Width = (Ri - 4) * 2,
                Height = (Ri - 4) * 2,
                Fill = CenterBrush,
                Stroke = Accent,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(center, C - Ri + 4);
            Canvas.SetTop(center, C - Ri + 4);
            _canvas.Children.Add(center);

            // metin blokları önceki kurulumdaki panelden ayrılmalı (WPF'de bir öğenin tek ebeveyni olur)
            (_centerTitle.Parent as Panel)?.Children.Remove(_centerTitle);
            (_centerSub.Parent as Panel)?.Children.Remove(_centerSub);
            var centerStack = new StackPanel { Width = (Ri - 12) * 2, IsHitTestVisible = false };
            centerStack.Children.Add(_centerTitle);
            centerStack.Children.Add(_centerSub);
            _canvas.Children.Add(centerStack);
            ShowCenter(title, sub);
        }

        private void ShowCenter(string title, string sub)
        {
            _centerTitle.Text = title;
            _centerSub.Text = sub;
            if (_centerTitle.Parent is StackPanel sp)
            {
                sp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(sp, C - sp.Width / 2);
                Canvas.SetTop(sp, C - sp.DesiredSize.Height / 2);
            }
        }

        private static Point Polar(double r, double deg)
        {
            double rad = (deg - 90) * Math.PI / 180.0; // 0° = yukarı, saat yönünde
            return new Point(C + r * Math.Cos(rad), C + r * Math.Sin(rad));
        }

        private static Geometry SectorGeometry(double a0, double a1, double ri, double ro)
        {
            bool large = (a1 - a0) > 180;
            var fig = new PathFigure { StartPoint = Polar(ro, a0), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new ArcSegment(Polar(ro, a1), new Size(ro, ro), 0, large, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(Polar(ri, a1), true));
            fig.Segments.Add(new ArcSegment(Polar(ri, a0), new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        // ------------------------------------------------------- konum / gösterme

        /// <summary>Pencereyi verilen ekran noktasını (fiziksel piksel) merkez alarak gösterir.</summary>
        public void ShowAt(Native.POINT pt)
        {
            _anchor = pt;
            var (scale, _) = Native.MonitorAt(pt);
            Left = pt.X / scale - Dim / 2;
            Top = pt.Y / scale - Dim / 2;
            SourceInitialized += (_, __) => PlaceExactly();
            Show();
            if (!_dropMode)
            {
                Activate();
                Focus();
                Keyboard.Focus(this);
                // Dışarıya tıklama da bize gelsin (iptal): odak alınamasa bile menü asılı kalmaz
                Mouse.Capture(this, CaptureMode.SubTree);
                LostMouseCapture += (_, __) =>
                {
                    if (!_finished && !IsActive) Complete(null);
                };
            }
        }

        public void ShowAtCursor()
        {
            Native.GetCursorPos(out var pt);
            ShowAt(pt);
        }

        private void PlaceExactly()
        {
            var (scale, work) = Native.MonitorAt(_anchor);
            int size = (int)Math.Round(Dim * scale);
            int x = _anchor.X - size / 2;
            int y = _anchor.Y - size / 2;
            // halka (pencerenin tamamı değil) ekranda kalsın
            int slack = (int)((Dim / 2 - Ro - 6) * scale);
            x = Math.Max(work.Left - slack, Math.Min(x, work.Right - size + slack));
            y = Math.Max(work.Top - slack, Math.Min(y, work.Bottom - size + slack));
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        private void AnimateIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(140);
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            if (_root.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, dur) { EasingFunction = ease });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, dur) { EasingFunction = ease });
            }
        }

        // ------------------------------------------------------------- seçim

        private int IndexAt(Point p)
        {
            int n = _sectors.Count;
            if (n == 0) return -1;
            double dx = p.X - C, dy = p.Y - C;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < Ri || dist > Ro + 26) return -1;
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI + 90.0;
            double step = 360.0 / n;
            deg = ((deg + step / 2) % 360 + 360) % 360;
            return Math.Min(n - 1, (int)(deg / step));
        }

        private void UpdateHover(Point p) => SetHover(IndexAt(p));

        private void SetHover(int index)
        {
            if (index == _hover) return;
            var entries = _model?.Entries;
            if (entries == null) return;

            if (_hover >= 0 && _hover < _sectors.Count)
            {
                var old = entries[_hover];
                _sectors[_hover].Fill = old.IsTool ? ToolBrush : SectorBrush;
                _labels[_hover][1].Foreground = old.IsTool ? ToolTextBrush : SubBrush;
            }

            _hover = index;
            if (_hover >= 0 && _hover < _sectors.Count)
            {
                var e = entries[_hover];
                _sectors[_hover].Fill = e.Enabled ? HoverBrush : DisabledHoverBrush;
                if (e.Enabled) _labels[_hover][1].Foreground = TextBrush;
                ShowCenter(e.Enabled ? "→ " + e.Label : e.Label, e.Enabled ? (e.Sub ?? _model!.Sub) : (e.DisabledReason ?? "Kullanılamıyor"));
            }
            else if (_model != null)
            {
                ShowCenter(_model.Title, _model.Sub);
            }
        }

        private void Step(int delta)
        {
            var entries = _model?.Entries;
            if (entries == null || entries.Count == 0) return;
            int n = entries.Count;
            int i = _hover < 0 ? (delta > 0 ? -1 : 0) : _hover;
            for (int k = 0; k < n; k++)
            {
                i = ((i + delta) % n + n) % n;
                if (entries[i].Enabled) { SetHover(i); return; }
            }
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Complete(null);
                    break;
                case Key.Enter:
                case Key.Space:
                    ChooseHovered();
                    break;
                case Key.Right:
                case Key.Down:
                case Key.Tab:
                    Step(+1);
                    break;
                case Key.Left:
                case Key.Up:
                    Step(-1);
                    break;
                default:
                    int digit = e.Key >= Key.D1 && e.Key <= Key.D9 ? e.Key - Key.D1
                              : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
                    if (digit >= 0 && _model != null && digit < _model.Entries.Count)
                    {
                        SetHover(digit);
                        ChooseHovered();
                    }
                    break;
            }
            e.Handled = true;
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (!_downSeen) return; // ör. dosya iletişim kutusundaki çift tıklamanın artığı
            _downSeen = false;
            int index = IndexAt(e.GetPosition(_canvas));
            if (index >= 0)
            {
                SetHover(index);
                ChooseHovered();
            }
            else
            {
                Complete(null); // merkez ya da dışarısı: iptal
            }
        }

        private void ChooseHovered()
        {
            if (_model == null || _hover < 0 || _hover >= _model.Entries.Count) return;
            var entry = _model.Entries[_hover];
            if (!entry.Enabled)
            {
                Native.MessageBeep(0);
                return;
            }
            Complete(entry);
        }

        // ------------------------------------------------------- bırakma kipi

        private static IReadOnlyList<string>? FilesFrom(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
            return data.GetData(DataFormats.FileDrop) as string[];
        }

        private static DragDropEffects SafeEffect(DragEventArgs e, bool accept)
        {
            if (!accept) return DragDropEffects.None;
            // ÖNEMLİ: Shift+sürükle Explorer'da "taşı" demektir. Taşı döndürürsek Explorer kaynağı siler.
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            return DragDropEffects.None;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (_model == null)
            {
                var files = FilesFrom(e.Data);
                if (files != null && FilesProvider != null)
                {
                    var model = FilesProvider(files);
                    if (model != null) SetModel(model);
                    else ShowCenter("?", "Bu dosya türü desteklenmiyor");
                }
            }
            OnDragOver(sender, e);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateHover(e.GetPosition(_canvas));
            bool ok = _model != null && _hover >= 0 && _model.Entries[_hover].Enabled;
            e.Effects = SafeEffect(e, ok);
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            UpdateHover(e.GetPosition(_canvas));
            bool ok = _model != null && _hover >= 0 && _model.Entries[_hover].Enabled;
            e.Effects = SafeEffect(e, ok);
            e.Handled = true;
            // Explorer'ın sürükle-bırak döngüsünü bekletmemek için seçimi bir sonraki turda işle
            Dispatcher.InvokeAsync(() =>
            {
                if (ok) ChooseHovered();
                else Complete(null);
            });
        }

        // ------------------------------------------------------------- kapanış

        public void Cancel() => Complete(null);

        private void Complete(MenuEntry? entry)
        {
            if (_finished) return;
            _finished = true;
            try
            {
                Finished?.Invoke(entry, _model);
            }
            catch (Exception ex)
            {
                Log.Error("menu-finished", ex);
            }

            var fade = new DoubleAnimation(_root.Opacity, 0, TimeSpan.FromMilliseconds(entry == null ? 90 : 160));
            fade.Completed += (_, __) => Close();
            _root.BeginAnimation(OpacityProperty, fade);
        }

        public static string DescribeFiles(IReadOnlyList<string> files)
        {
            return files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} dosya";
        }

        public static string TitleFor(IReadOnlyList<string> files)
        {
            var exts = files.Select(f => RecipeBook.Canonical(RecipeBook.ExtOf(f)).ToUpperInvariant()).Distinct().ToList();
            return exts.Count == 1 ? exts[0] : string.Join("·", exts.Take(3));
        }
    }
}
