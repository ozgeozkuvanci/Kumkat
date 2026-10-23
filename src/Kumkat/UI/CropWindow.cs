using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapePath = System.Windows.Shapes.Path;
using ShapeRect = System.Windows.Shapes.Rectangle;
using ShapeLine = System.Windows.Shapes.Line;

namespace Kumkat.UI
{
    /// <summary>
    /// Serbest kırpma: önizleme üzerinde fareyle alan çizilir, köşe/kenar tutamaklarıyla boyutlandırılır,
    /// içinden tutup taşınır. Oran 1:1, 4:3, 16:9, 9:16 olarak kilitlenebilir.
    /// Sonuç <see cref="Result"/>: görüntüye göre 0–1 arası oranlar (x, y, genişlik, yükseklik).
    /// </summary>
    internal sealed class CropWindow : Window
    {
        private enum Mode { None, New, Move, Resize }

        // tutamaklar: 0 sol-üst, 1 üst, 2 sağ-üst, 3 sağ, 4 sağ-alt, 5 alt, 6 sol-alt, 7 sol
        private static readonly int[] HX = { -1, 0, 1, 1, 1, 0, -1, -1 };
        private static readonly int[] HY = { -1, -1, -1, 0, 1, 1, 1, 0 };
        private const double HandleSize = 10;
        private const double MinSel = 12;

        // Kumkat paleti
        private static readonly Brush Bg = Theme.Solid(Theme.Ink);
        private static readonly Brush Bar = Theme.Solid(Theme.Cloud);
        private static readonly Brush Dim = Theme.Solid(Theme.Ink, 0xB0);
        private static readonly Brush Accent = Theme.Solid(Theme.Orange);
        private static readonly Brush AccentHover = Theme.Solid(Theme.Aperol);
        private static readonly Brush Thirds = Theme.Solid(Theme.Mimosa, 0x90);
        private static readonly Brush Fore = Theme.Solid(Colors.White);
        private static readonly Brush Muted = Theme.Solid(Theme.Cloud, 0xC0);
        private static readonly Brush ChipBg = Theme.Solid(Colors.White, 0x80);
        private static readonly Brush ChipHover = Theme.Solid(Colors.White);
        private static readonly Brush ChipOn = Theme.Solid(Theme.Mimosa);
        private static readonly Brush ChipText = Theme.Solid(Theme.Ink);
        private static readonly Brush ChipOnText = Theme.Solid(Theme.Rind);

        private readonly int _pixW, _pixH;
        private readonly double _w, _h; // görüntünün ekrandaki boyutu (DIP)
        private readonly Canvas _canvas;
        private readonly ShapePath _shade;
        private readonly ShapeRect _border;
        private readonly ShapeLine[] _thirds = new ShapeLine[4];
        private readonly ShapeRect[] _handles = new ShapeRect[8];
        private readonly TextBlock _sizeText;
        private readonly List<Border> _ratioChips = new List<Border>();

        private Rect _sel;
        private double? _ratio; // genişlik / yükseklik (null = serbest)
        private Mode _mode;
        private int _handle;
        private Point _start;
        private Rect _startSel;
        private bool _closing;

        /// <summary>Kırp'a basıldıysa seçilen alan (0–1 oranları); iptalde null.</summary>
        public Rect? Result { get; private set; }

        public CropWindow(BitmapSource image, string fileName, int fileCount)
        {
            _pixW = image.PixelWidth;
            _pixH = image.PixelHeight;

            Title = "Kumkat — Kırp: " + fileName;
            Background = Bg;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = true;
            Topmost = true;
            UseLayoutRounding = true;

            // ekranın çoğunu kaplayacak ama taşmayacak boyut
            Rect work = SystemParameters.WorkArea;
            double maxW = Math.Max(320, work.Width * 0.86);
            double maxH = Math.Max(240, work.Height * 0.86 - 110);
            double scale = Math.Min(Math.Min(maxW / _pixW, maxH / _pixH), 3.0);
            _w = Math.Max(1, Math.Round(_pixW * scale));
            _h = Math.Max(1, Math.Round(_pixH * scale));

            // ---------------------------------------------------- çizim alanı
            _canvas = new Canvas { Width = _w, Height = _h, ClipToBounds = true, Background = Brushes.Black, Cursor = Cursors.Cross };
            var img = new Image { Source = image, Width = _w, Height = _h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            _canvas.Children.Add(img);

            _shade = new ShapePath { Fill = Dim, IsHitTestVisible = false };
            _canvas.Children.Add(_shade);

            for (int i = 0; i < 4; i++)
            {
                _thirds[i] = new ShapeLine { Stroke = Thirds, StrokeThickness = 1, IsHitTestVisible = false };
                _canvas.Children.Add(_thirds[i]);
            }

            _border = new ShapeRect { Stroke = Theme.Solid(Theme.Mimosa), StrokeThickness = 2, IsHitTestVisible = false };
            _canvas.Children.Add(_border);

            for (int i = 0; i < 8; i++)
            {
                _handles[i] = new ShapeRect
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Fill = Theme.Solid(Theme.Mimosa),
                    Stroke = Theme.Solid(Theme.Aperol),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                _canvas.Children.Add(_handles[i]);
            }

            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            _canvas.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && _sel.Contains(e.GetPosition(_canvas))) Confirm(); };

            // ---------------------------------------------------- üst çubuk
            var bar = new DockPanel { Background = Bar, LastChildFill = false };
            var ratios = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 9, 12, 9), VerticalAlignment = VerticalAlignment.Center };
            AddRatio(ratios, "Serbest", null, true);
            AddRatio(ratios, "1:1", 1.0, false);
            AddRatio(ratios, "4:3", 4.0 / 3, false);
            AddRatio(ratios, "3:2", 3.0 / 2, false);
            AddRatio(ratios, "16:9", 16.0 / 9, false);
            AddRatio(ratios, "9:16", 9.0 / 16, false);
            AddRatio(ratios, "Orijinal", (double)_pixW / _pixH, false);
            DockPanel.SetDock(ratios, Dock.Left);
            bar.Children.Add(ratios);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 9, 12, 9), VerticalAlignment = VerticalAlignment.Center };
            var cancel = MakeButton("İptal", false, CloseOnce);
            var ok = MakeButton(fileCount > 1 ? $"Kırp ({fileCount} dosya)" : "Kırp", true, Confirm);
            actions.Children.Add(cancel);
            actions.Children.Add(ok);
            DockPanel.SetDock(actions, Dock.Right);
            bar.Children.Add(actions);

            // ---------------------------------------------------- alt bilgi
            _sizeText = new TextBlock { Foreground = Theme.Solid(Theme.Mimosa), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var hint = new TextBlock
            {
                Foreground = Muted,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Text = "Sürükleyerek alan çiz · köşelerden boyutlandır · içinden taşı · oklar: kaydır · Enter: kırp · Esc: iptal" +
                       (fileCount > 1 ? " · aynı oransal alan tüm dosyalara uygulanır" : ""),
            };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 12, 8) };
            footer.Children.Add(_sizeText);
            footer.Children.Add(hint);

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(bar);
            root.Children.Add(footer);
            root.Children.Add(new Border { Child = _canvas, Margin = new Thickness(12, 4, 12, 0) });
            Content = root;

            KeyDown += OnKey;
            Loaded += (_, __) => { Activate(); Focus(); };

            // başlangıç: kenarlardan %8 içeride
            _sel = new Rect(_w * 0.08, _h * 0.08, _w * 0.84, _h * 0.84);
            UpdateVisuals();
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Palete uyan, yuvarlak köşeli düğme (Windows'un mavi vurgusu olmadan).</summary>
        private static Border MakeButton(string text, bool primary, Action onClick)
        {
            Brush normal = primary ? Accent : ChipBg, hover = primary ? AccentHover : ChipHover;
            var b = new Border
            {
                Background = normal,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 6, 18, 7),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = primary ? Fore : ChipText,
                    FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 13,
                },
            };
            b.MouseEnter += (_, __) => b.Background = hover;
            b.MouseLeave += (_, __) => b.Background = normal;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            return b;
        }

        private void AddRatio(Panel host, string label, double? ratio, bool isChecked)
        {
            var text = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = isChecked ? ChipOnText : ChipText };
            var chip = new Border
            {
                Child = text,
                Background = isChecked ? ChipOn : ChipBg,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 4, 12, 5),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Tag = isChecked,
            };
            chip.MouseEnter += (_, __) => { if (!(chip.Tag is true)) chip.Background = ChipHover; };
            chip.MouseLeave += (_, __) => { if (!(chip.Tag is true)) chip.Background = ChipBg; };
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                foreach (var other in _ratioChips)
                {
                    bool on = ReferenceEquals(other, chip);
                    other.Tag = on;
                    other.Background = on ? ChipOn : ChipBg;
                    if (other.Child is TextBlock t) t.Foreground = on ? ChipOnText : ChipText;
                }
                SetRatio(ratio);
            };
            _ratioChips.Add(chip);
            host.Children.Add(chip);
        }

        // ------------------------------------------------------------ oran

        private void SetRatio(double? ratio)
        {
            _ratio = ratio;
            if (ratio == null) return;

            // mevcut merkezde, sığan en büyük o oranlı alan
            double r = ratio.Value;
            Point c = new Point(_sel.X + _sel.Width / 2, _sel.Y + _sel.Height / 2);
            double w = _w, h = _w / r;
            if (h > _h) { h = _h; w = _h * r; }
            double x = Clamp(c.X - w / 2, 0, _w - w), y = Clamp(c.Y - h / 2, 0, _h - h);
            _sel = new Rect(x, y, w, h);
            UpdateVisuals();
        }

        // ------------------------------------------------------------- fare

        private int HandleAt(Point p)
        {
            for (int i = 0; i < 8; i++)
            {
                Point h = HandlePoint(i);
                if (Math.Abs(p.X - h.X) <= HandleSize && Math.Abs(p.Y - h.Y) <= HandleSize) return i;
            }
            return -1;
        }

        private Point HandlePoint(int i) =>
            new Point(_sel.X + _sel.Width * (HX[i] + 1) / 2.0, _sel.Y + _sel.Height * (HY[i] + 1) / 2.0);

        private static Cursor CursorFor(int handle)
        {
            switch (handle)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                case 3: case 7: return Cursors.SizeWE;
                default: return Cursors.Cross;
            }
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount > 1) return;
            Point p = ClampPoint(e.GetPosition(_canvas));
            _start = p;
            _startSel = _sel;
            int h = HandleAt(p);
            if (h >= 0) { _mode = Mode.Resize; _handle = h; }
            else if (_sel.Contains(p)) _mode = Mode.Move;
            else _mode = Mode.New;
            _canvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            Point p = ClampPoint(e.GetPosition(_canvas));
            switch (_mode)
            {
                case Mode.None:
                    int h = HandleAt(p);
                    _canvas.Cursor = h >= 0 ? CursorFor(h) : _sel.Contains(p) ? Cursors.SizeAll : Cursors.Cross;
                    return;
                case Mode.Move:
                    double nx = Clamp(_startSel.X + (p.X - _start.X), 0, _w - _startSel.Width);
                    double ny = Clamp(_startSel.Y + (p.Y - _start.Y), 0, _h - _startSel.Height);
                    _sel = new Rect(nx, ny, _startSel.Width, _startSel.Height);
                    break;
                case Mode.New:
                    _sel = FromAnchor(_start, p);
                    break;
                case Mode.Resize:
                    _sel = Resize(_handle, p);
                    break;
            }
            UpdateVisuals();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_mode == Mode.None) return;
            _mode = Mode.None;
            _canvas.ReleaseMouseCapture();
            if (_sel.Width < MinSel || _sel.Height < MinSel) _sel = _startSel; // kazara tıklama: önceki alana dön
            UpdateVisuals();
        }

        /// <summary>Sabit köşeden (anchor) fareye uzanan dikdörtgen; oran kilitliyse oran korunur, görüntüden taşmaz.</summary>
        private Rect FromAnchor(Point anchor, Point p)
        {
            double sx = p.X >= anchor.X ? 1 : -1, sy = p.Y >= anchor.Y ? 1 : -1;
            double w = Math.Abs(p.X - anchor.X), h = Math.Abs(p.Y - anchor.Y);
            if (_ratio is double r)
            {
                double maxW = sx > 0 ? _w - anchor.X : anchor.X;
                double maxH = sy > 0 ? _h - anchor.Y : anchor.Y;
                if (w / Math.Max(h, 0.0001) > r) w = h * r; else h = w / r;
                if (w > maxW) { w = maxW; h = w / r; }
                if (h > maxH) { h = maxH; w = h * r; }
            }
            double x = sx > 0 ? anchor.X : anchor.X - w;
            double y = sy > 0 ? anchor.Y : anchor.Y - h;
            return new Rect(x, y, w, h);
        }

        private Rect Resize(int handle, Point p)
        {
            Rect s = _startSel;
            int hx = HX[handle], hy = HY[handle];

            // köşe: karşı köşe sabit
            if (hx != 0 && hy != 0)
            {
                var anchor = new Point(hx < 0 ? s.Right : s.Left, hy < 0 ? s.Bottom : s.Top);
                return FromAnchor(anchor, p);
            }

            if (hx != 0) // sol / sağ kenar
            {
                double left = hx < 0 ? Math.Min(p.X, s.Right - MinSel) : s.Left;
                double right = hx > 0 ? Math.Max(p.X, s.Left + MinSel) : s.Right;
                double w = right - left, h = s.Height, y = s.Y;
                if (_ratio is double r)
                {
                    h = w / r;
                    double cy = s.Y + s.Height / 2;
                    if (h > _h) { h = _h; w = h * r; if (hx < 0) left = right - w; }
                    y = Clamp(cy - h / 2, 0, _h - h);
                }
                return new Rect(left, y, w, h);
            }
            else // üst / alt kenar
            {
                double top = hy < 0 ? Math.Min(p.Y, s.Bottom - MinSel) : s.Top;
                double bottom = hy > 0 ? Math.Max(p.Y, s.Top + MinSel) : s.Bottom;
                double h = bottom - top, w = s.Width, x = s.X;
                if (_ratio is double r)
                {
                    w = h * r;
                    double cx = s.X + s.Width / 2;
                    if (w > _w) { w = _w; h = w / r; if (hy < 0) top = bottom - h; }
                    x = Clamp(cx - w / 2, 0, _w - w);
                }
                return new Rect(x, top, w, h);
            }
        }

        // ---------------------------------------------------------- klavye

        private void OnKey(object sender, KeyEventArgs e)
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            double dx = 0, dy = 0;
            switch (e.Key)
            {
                case Key.Enter: Confirm(); e.Handled = true; return;
                case Key.Escape: CloseOnce(); e.Handled = true; return;
                case Key.Left: dx = -step; break;
                case Key.Right: dx = step; break;
                case Key.Up: dy = -step; break;
                case Key.Down: dy = step; break;
                default: return;
            }
            _sel = new Rect(Clamp(_sel.X + dx, 0, _w - _sel.Width), Clamp(_sel.Y + dy, 0, _h - _sel.Height), _sel.Width, _sel.Height);
            UpdateVisuals();
            e.Handled = true;
        }

        // ---------------------------------------------------------- çizim

        private void UpdateVisuals()
        {
            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
            shade.Children.Add(new RectangleGeometry(new Rect(0, 0, _w, _h)));
            shade.Children.Add(new RectangleGeometry(_sel));
            _shade.Data = shade;

            Canvas.SetLeft(_border, _sel.X);
            Canvas.SetTop(_border, _sel.Y);
            _border.Width = _sel.Width;
            _border.Height = _sel.Height;

            for (int i = 0; i < 2; i++)
            {
                double fx = _sel.X + _sel.Width * (i + 1) / 3.0;
                double fy = _sel.Y + _sel.Height * (i + 1) / 3.0;
                SetLine(_thirds[i], fx, _sel.Top, fx, _sel.Bottom);
                SetLine(_thirds[i + 2], _sel.Left, fy, _sel.Right, fy);
            }

            for (int i = 0; i < 8; i++)
            {
                Point h = HandlePoint(i);
                Canvas.SetLeft(_handles[i], h.X - HandleSize / 2);
                Canvas.SetTop(_handles[i], h.Y - HandleSize / 2);
            }

            Rect f = Fractions();
            int pw = (int)Math.Round(f.Width * _pixW), ph = (int)Math.Round(f.Height * _pixH);
            _sizeText.Text = $"{pw} × {ph} px";
        }

        private static void SetLine(ShapeLine l, double x1, double y1, double x2, double y2)
        {
            l.X1 = x1;
            l.Y1 = y1;
            l.X2 = x2;
            l.Y2 = y2;
        }

        // -------------------------------------------------------- yardımcı

        private Rect Fractions()
        {
            double x = Clamp(_sel.X / _w, 0, 1), y = Clamp(_sel.Y / _h, 0, 1);
            double w = Clamp(_sel.Width / _w, 0, 1 - x), h = Clamp(_sel.Height / _h, 0, 1 - y);
            return new Rect(x, y, w, h);
        }

        private Point ClampPoint(Point p) => new Point(Clamp(p.X, 0, _w), Clamp(p.Y, 0, _h));

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

        private void Confirm()
        {
            if (_closing || _sel.Width < 2 || _sel.Height < 2) return;
            Result = Fractions();
            CloseOnce();
        }

        private void CloseOnce()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }
    }
}
