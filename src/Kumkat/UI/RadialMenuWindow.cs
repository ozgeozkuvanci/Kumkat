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
        public MenuModel(IReadOnlyList<string> files, List<MenuPage> pages, string title, string sub)
        {
            Files = files;
            Pages = pages;
            Title = title;
            Sub = sub;
        }

        public IReadOnlyList<string> Files { get; }
        public List<MenuPage> Pages { get; }
        public string Title { get; }
        public string Sub { get; }
    }

    /// <summary>Halkanın bir sayfası (fare tekerleğiyle geçilir).</summary>
    internal sealed class MenuPage
    {
        public MenuPage(string title, List<MenuEntry> entries)
        {
            Title = title;
            Entries = entries;
        }

        public string Title { get; }
        public List<MenuEntry> Entries { get; }
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
        private const double Ri = 56;        // ortadaki göbek (dosya türü/adı yazar)
        private const double Ro = 150;       // dilimlerin dış yarıçapı
        private const double PithBand = 14;  // krem ak bant
        private const double Peel = 12;      // kabuk kalınlığı
        private const double Membrane = 6;   // dilimler arasındaki zar (sabit genişlik)
        private const double Round = 12;     // dilim köşelerinin yuvarlaklığı

        // Kumkat paleti (bkz. Theme.cs) — gerçek bir portakal dilimi gibi
        private const double Rind0 = Ro + PithBand;          // kabuğun iç kenarı
        private const double Rind1 = Ro + PithBand + Peel;   // kabuğun dış kenarı
        private static readonly Brush PeelBrush = Theme.Radial3(C, C, Rind1, Theme.OrangeLight, 0.88, Theme.Orange, 0.95, Theme.Aperol);
        private static readonly Brush PithBrush = Theme.Radial3(C, C, Rind0, Theme.Cream, 0.30, Theme.Cream, 0.86, Theme.CreamDeep);
        private static readonly Brush SegmentBrush = Theme.Radial3(C, C, Ro, Theme.Juice, 0.38, Theme.Orange, 0.85, Theme.JuiceDeep);
        private static readonly Brush HoverBrush = Theme.Radial3(C, C, Ro, Theme.Orange, 0.38, Theme.Aperol, 0.85, Theme.AperolDeep);
        private static readonly Brush DisabledBrush = Theme.Radial(C, C, Ro, Theme.AzulLight, Theme.Azul, 0.35);
        private static readonly Brush DisabledHoverBrush = Theme.Solid(Theme.AzulDeep);
        private static readonly Brush CoreBrush = Theme.Radial3(C, C, Ri, Colors.White, 0.0, Theme.Cream, 0.6, Theme.CreamDeep);
        private static readonly Brush TitleBrush = Theme.Solid(Theme.Aperol);
        private static readonly Brush CenterSubBrush = Theme.Solid(Theme.Ink);
        private static readonly Brush LabelBrush = Theme.Solid(Colors.White);
        private static readonly Brush LabelSubBrush = Theme.Solid(Color.FromRgb(0xFF, 0xF0, 0xDC));
        private static readonly Brush LabelDisabledBrush = Theme.Solid(Theme.Ink, 0xC0);
        private static readonly Brush LabelHoverBrush = Theme.Solid(Colors.White);
        private static readonly Brush LabelHoverSubBrush = Theme.Solid(Color.FromRgb(0xFF, 0xE4, 0xCC));
        private static readonly Brush PillBrush = Theme.Solid(Theme.Ink, 0xEE);
        private static readonly Brush PillTextBrush = Theme.Solid(Theme.Cream);
        private static readonly Brush HitBrush = Frozen(Color.FromArgb(1, 0, 0, 0)); // görünmez ama fare/sürükleme yakalar

        private readonly bool _dropMode;
        private readonly Grid _root;
        private readonly Canvas _canvas;
        private readonly List<ShapePath> _sectors = new List<ShapePath>();
        private readonly List<TextBlock[]> _labels = new List<TextBlock[]>();
        private readonly TextBlock _centerTitle;
        private readonly TextBlock _centerSub;

        private static readonly List<MenuEntry> NoEntries = new List<MenuEntry>();

        private MenuModel? _model;
        private int _page;
        private int _wheelAccum;
        private DateTime _lastPageTurn = DateTime.MinValue;
        private Canvas? _ring; // dilimler + etiketler (sayfa değişiminde döndürülerek canlandırılır)
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
                FlowDirection = Loc.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Foreground = TitleBrush,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _centerSub = new TextBlock
            {
                FlowDirection = Loc.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Foreground = CenterSubBrush,
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
            MouseWheel += (_, e) =>
            {
                Wheel(e.Delta);
                e.Handled = true;
            };
            Deactivated += (_, __) => { if (!_dropMode) Complete(null); };
            Loaded += (_, __) => AnimateIn();

            if (dropMode)
            {
                DragEnter += OnDragEnter;
                DragOver += OnDragOver;
                DragLeave += (_, __) => SetHover(-1);
                Drop += OnDrop;
            }

            SetModel(null, dropMode ? Loc.T("Bırak") : "", dropMode ? Loc.T("Dosyayı bir dilime bırak") : "");
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ------------------------------------------------------------ kurulum

        public void SetModel(MenuModel? model) => SetModel(model, model?.Title ?? "", model?.Sub ?? "");

        /// <summary>Şu anki sayfanın dilimleri.</summary>
        private List<MenuEntry> Cur =>
            _model == null || _model.Pages.Count == 0 ? NoEntries : _model.Pages[Math.Min(_page, _model.Pages.Count - 1)].Entries;

        private void SetModel(MenuModel? model, string title, string sub)
        {
            _model = model;
            _page = 0;
            Rebuild(title, sub);
        }

        private void Rebuild(string title, string sub)
        {
            _hover = -1;
            _sectors.Clear();
            _labels.Clear();
            _canvas.Children.Clear();

            // kabuk
            var peel = new Ellipse
            {
                Width = Rind1 * 2,
                Height = Rind1 * 2,
                Fill = PeelBrush,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35, Color = Theme.Rind },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(peel, C - Rind1);
            Canvas.SetTop(peel, C - Rind1);
            _canvas.Children.Add(peel);

            // krem ak bant
            var pith = new Ellipse { Width = Rind0 * 2, Height = Rind0 * 2, Fill = PithBrush, IsHitTestVisible = false };
            Canvas.SetLeft(pith, C - Rind0);
            Canvas.SetTop(pith, C - Rind0);
            _canvas.Children.Add(pith);

            _ring = new Canvas { Width = Dim, Height = Dim, IsHitTestVisible = false, RenderTransform = new RotateTransform(0, C, C) };
            _canvas.Children.Add(_ring);

            var entries = Cur;
            int n = entries.Count;
            if (n == 0)
            {
                // henüz dosya yok (bırakma kipi): süs dilimleri
                for (int i = 0; i < 8; i++)
                {
                    var deco = MakeSegment(i * 45.0, 45.0, SegmentBrush);
                    deco.Opacity = 0.75;
                    _ring.Children.Add(deco);
                }
            }
            else
            {
                double step = 360.0 / n;
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    double mid = i * step;
                    var path = MakeSegment(mid, step, entry.Enabled ? SegmentBrush : DisabledBrush);
                    _ring.Children.Add(path);
                    _sectors.Add(path);

                    var label = new TextBlock
                    {
                        FlowDirection = Loc.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        Text = entry.Label,
                        FontSize = n > 10 ? 13 : 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = entry.Enabled ? LabelBrush : LabelDisabledBrush,
                        TextAlignment = TextAlignment.Center,
                    };
                    var subLabel = new TextBlock
                    {
                        FlowDirection = Loc.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        Text = entry.Sub ?? (entry.IsTool ? Loc.T("araç") : ""),
                        FontSize = 10,
                        Foreground = entry.Enabled ? LabelSubBrush : LabelDisabledBrush,
                        TextAlignment = TextAlignment.Center,
                        Visibility = string.IsNullOrEmpty(entry.Sub) && !entry.IsTool ? Visibility.Collapsed : Visibility.Visible,
                    };
                    var stack = new StackPanel
                    {
                        Width = 84,
                        IsHitTestVisible = false,
                        Effect = entry.Enabled ? new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.55, Color = Theme.Rind } : null,
                    };
                    stack.Children.Add(label);
                    stack.Children.Add(subLabel);
                    stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Point p = Polar((Ri + Ro) / 2 + 8, mid);
                    Canvas.SetLeft(stack, p.X - 42);
                    Canvas.SetTop(stack, p.Y - stack.DesiredSize.Height / 2);
                    _ring.Children.Add(stack);
                    _labels.Add(new[] { label, subLabel });
                }
            }

            // çekirdek (meyvenin ortası)
            var center = new Ellipse
            {
                Width = (Ri - 2) * 2,
                Height = (Ri - 2) * 2,
                Fill = CoreBrush,
                Stroke = null,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(center, C - Ri + 2);
            Canvas.SetTop(center, C - Ri + 2);
            _canvas.Children.Add(center);


            // metin blokları önceki kurulumdaki panelden ayrılmalı (WPF'de bir öğenin tek ebeveyni olur)
            (_centerTitle.Parent as Panel)?.Children.Remove(_centerTitle);
            (_centerSub.Parent as Panel)?.Children.Remove(_centerSub);
            var centerStack = new StackPanel { Width = (Ri - 12) * 2, IsHitTestVisible = false };
            centerStack.Children.Add(_centerTitle);
            centerStack.Children.Add(_centerSub);
            _canvas.Children.Add(centerStack);
            ShowCenter(title, sub);
            AddPagePill();
        }

        /// <summary>Halkanın altında sayfa göstergesi: "Kırp · Döndür  ○ ● ○".</summary>
        private void AddPagePill()
        {
            if (_model == null || _model.Pages.Count <= 1) return;
            string dots = string.Join(" ", Enumerable.Range(0, _model.Pages.Count).Select(i => i == _page ? "●" : "○"));
            var text = new TextBlock
            {
                FlowDirection = Loc.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Text = $"{_model.Pages[_page].Title}   {dots}",
                Foreground = PillTextBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            var pill = new Border
            {
                Background = PillBrush,
                BorderBrush = Theme.Solid(Theme.Mimosa),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 2, 10, 3),
                Child = text,
                IsHitTestVisible = false,
                ToolTip = null,
            };
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(pill, C - pill.DesiredSize.Width / 2);
            Canvas.SetTop(pill, Math.Min(Dim - pill.DesiredSize.Height - 1, C + Rind1 - pill.DesiredSize.Height / 2 - 2));
            _canvas.Children.Add(pill);
        }

        // ------------------------------------------------------- sayfa değiştirme

        /// <summary>Fare tekerleği: dokunmatik yüzeylerin küçük adımlarını biriktirir, bir sayfa = 120 birim.</summary>
        public void Wheel(int delta)
        {
            if (_model == null || _model.Pages.Count <= 1) return;
            _wheelAccum += delta;
            if (Math.Abs(_wheelAccum) < 120) return;
            int dir = _wheelAccum < 0 ? +1 : -1; // aşağı = sonraki sayfa
            _wheelAccum = 0;
            if ((DateTime.Now - _lastPageTurn).TotalMilliseconds < 140) return; // hızlı tekerlekte sayfa atlamasın
            ChangePage(dir);
        }

        public void ChangePage(int dir)
        {
            if (_model == null || _model.Pages.Count <= 1 || _finished) return;
            int n = _model.Pages.Count;
            _page = ((_page + dir) % n + n) % n;
            _lastPageTurn = DateTime.Now;

            Point? mouse = null;
            try { mouse = Mouse.GetPosition(_canvas); } catch { }
            Rebuild(_model.Title, _model.Sub);
            if (mouse.HasValue) UpdateHover(mouse.Value);

            // dilimler dönerek gelsin
            if (_ring?.RenderTransform is RotateTransform rt)
            {
                var anim = new DoubleAnimation(dir * -28, 0, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                rt.BeginAnimation(RotateTransform.AngleProperty, anim);
                _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(170)));
            }
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

        /// <summary>
        /// Meyve dilimi: kenarları dilim ekseninden <paramref name="d"/> kadar kaydırılmış paralel doğrular
        /// (böylece aradaki zar her yerde aynı kalınlıkta), iç ve dış kenarı yay.
        /// </summary>
        private static Geometry SegmentGeometry(double midDeg, double stepDeg, double ri, double ro, double d)
        {
            double a0 = midDeg - stepDeg / 2, a1 = midDeg + stepDeg / 2;
            double Off(double r) => Math.Asin(Math.Min(1.0, d / r)) * 180.0 / Math.PI;
            double o0 = a0 + Off(ro), o1 = a1 - Off(ro), i0 = a0 + Off(ri), i1 = a1 - Off(ri);
            if (i1 <= i0) { double m = (a0 + a1) / 2; i0 = m - 0.5; i1 = m + 0.5; } // çok dar dilim: içte sivri bitsin

            var fig = new PathFigure { StartPoint = Polar(ro, o0), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new ArcSegment(Polar(ro, o1), new Size(ro, ro), 0, (o1 - o0) > 180, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(Polar(ri, i1), true));
            fig.Segments.Add(new ArcSegment(Polar(ri, i0), new Size(ri, ri), 0, (i1 - i0) > 180, SweepDirection.Counterclockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        private static ShapePath MakeSegment(double midDeg, double stepDeg, Brush brush)
        {
            // Aynı fırçayla kalın, yuvarlak birleşimli çizgi: şekil yarım çizgi kadar büyür ve köşeleri yuvarlanır.
            // Bu yüzden geometri o kadar içeriden çizilir.
            double half = Round / 2;
            return new ShapePath
            {
                Data = SegmentGeometry(midDeg, stepDeg, Ri + 2 + half, Ro - half, Membrane / 2 + half),
                Fill = brush,
                Stroke = brush,
                StrokeThickness = Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
        }

        private static void Paint(ShapePath segment, Brush brush)
        {
            segment.Fill = brush;
            segment.Stroke = brush;
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
            if (_model == null) return;
            var entries = Cur;

            if (_hover >= 0 && _hover < _sectors.Count)
            {
                var old = entries[_hover];
                Paint(_sectors[_hover], old.Enabled ? SegmentBrush : DisabledBrush);
                _labels[_hover][0].Foreground = old.Enabled ? LabelBrush : LabelDisabledBrush;
                _labels[_hover][1].Foreground = old.Enabled ? LabelSubBrush : LabelDisabledBrush;
            }

            _hover = index;
            if (_hover >= 0 && _hover < _sectors.Count)
            {
                var e = entries[_hover];
                Paint(_sectors[_hover], e.Enabled ? HoverBrush : DisabledHoverBrush);
                _labels[_hover][0].Foreground = LabelHoverBrush;
                _labels[_hover][1].Foreground = e.Enabled ? LabelHoverSubBrush : LabelHoverBrush;
                ShowCenter(e.Enabled ? Loc.T("→ ") + e.Label : e.Label, e.Enabled ? (e.Sub ?? _model!.Sub) : (e.DisabledReason ?? Loc.T("Kullanılamıyor")));
            }
            else if (_model != null)
            {
                ShowCenter(_model.Title, _model.Sub);
            }
        }

        private void Step(int delta)
        {
            var entries = Cur;
            if (entries.Count == 0) return;
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
                case Key.PageDown:
                    ChangePage(+1);
                    break;
                case Key.PageUp:
                    ChangePage(-1);
                    break;
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
                    if (digit >= 0 && _model != null && digit < Cur.Count)
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
            if (_model == null || _hover < 0 || _hover >= Cur.Count) return;
            var entry = Cur[_hover];
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
                    else ShowCenter("?", Loc.T("Bu dosya türü desteklenmiyor"));
                }
            }
            OnDragOver(sender, e);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateHover(e.GetPosition(_canvas));
            bool ok = _model != null && _hover >= 0 && _hover < Cur.Count && Cur[_hover].Enabled;
            e.Effects = SafeEffect(e, ok);
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            UpdateHover(e.GetPosition(_canvas));
            bool ok = _model != null && _hover >= 0 && _hover < Cur.Count && Cur[_hover].Enabled;
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
            return files.Count == 1 ? Path.GetFileName(files[0]) : Loc.F("{0} dosya", files.Count);
        }

        public static string TitleFor(IReadOnlyList<string> files)
        {
            var exts = files.Select(f => RecipeBook.Canonical(RecipeBook.ExtOf(f)).ToUpperInvariant()).Distinct().ToList();
            return exts.Count == 1 ? exts[0] : string.Join("·", exts.Take(3));
        }
    }
}
