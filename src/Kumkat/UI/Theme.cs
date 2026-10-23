using System.Windows;
using System.Windows.Media;

namespace Kumkat.UI
{
    /// <summary>
    /// Kumkat paleti: Cloud #CDD0DB · Azul #9197AA · Mimosa #F7B557 · Orange #E27921 · Aperol #C1521E.
    /// Halka kesilmiş bir kumkat gibi çizilir: kabuk (Aperol→Orange), ak (Cloud), dilimler (Mimosa).
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Cloud = Color.FromRgb(0xCD, 0xD0, 0xDB);
        public static readonly Color Azul = Color.FromRgb(0x91, 0x97, 0xAA);
        public static readonly Color Mimosa = Color.FromRgb(0xF7, 0xB5, 0x57);
        public static readonly Color Orange = Color.FromRgb(0xE2, 0x79, 0x21);
        public static readonly Color Aperol = Color.FromRgb(0xC1, 0x52, 0x1E);

        // türetilmiş tonlar
        public static readonly Color MimosaLight = Color.FromRgb(0xFC, 0xD6, 0x93);
        public static readonly Color OrangeLight = Color.FromRgb(0xF0, 0x9A, 0x45);
        public static readonly Color AzulLight = Color.FromRgb(0xB4, 0xB8, 0xC7);
        public static readonly Color AzulDeep = Color.FromRgb(0x5F, 0x65, 0x78);
        public static readonly Color Ink = Color.FromRgb(0x3B, 0x3F, 0x4C);      // koyu gri-mavi metin
        public static readonly Color Rind = Color.FromRgb(0x5A, 0x24, 0x0E);     // dilim üstündeki koyu metin
        public static readonly Color RindSoft = Color.FromRgb(0x8E, 0x45, 0x1C);
        public static readonly Color Pith = Color.FromRgb(0xF4, 0xF1, 0xEC);     // ak kısmın en açık tonu

        public static SolidColorBrush Solid(Color c, byte alpha = 0xFF)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        /// <summary>Merkezi (cx, cy) olan, mutlak koordinatlı radyal degrade (tüm dilimler aynı meyveden gelsin diye).</summary>
        public static Brush Radial(double cx, double cy, double radius, Color inner, Color outer, double innerStop = 0.0)
        {
            var b = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(cx, cy),
                GradientOrigin = new Point(cx, cy),
                RadiusX = radius,
                RadiusY = radius,
            };
            b.GradientStops.Add(new GradientStop(inner, innerStop));
            b.GradientStops.Add(new GradientStop(outer, 1.0));
            b.Freeze();
            return b;
        }
    }
}
