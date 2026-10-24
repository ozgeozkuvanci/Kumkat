using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kumkat.Core
{
    internal sealed class AseFrame
    {
        public AseFrame(byte[] rgba, int durationMs)
        {
            Rgba = rgba;
            DurationMs = durationMs;
        }

        /// <summary>Genişlik × yükseklik × 4 bayt, düz (premultiplied olmayan) RGBA.</summary>
        public byte[] Rgba { get; }
        public int DurationMs { get; }
    }

    internal sealed class AseImage
    {
        public AseImage(int width, int height, List<AseFrame> frames)
        {
            Width = width;
            Height = height;
            Frames = frames;
        }

        public int Width { get; }
        public int Height { get; }
        public List<AseFrame> Frames { get; }
    }

    /// <summary>
    /// Aseprite (.aseprite / .ase) okuyucu. Görünür katmanları her kare için birleştirir.
    /// RGBA / gri / indexed, sıkıştırılmış ve bağlı cel'ler, gruplar, z-sırası ve tilemap katmanları desteklenir.
    /// Karışım modları "normal" olarak uygulanır. tests/aseprite_reference.py ile aynı algoritma.
    /// </summary>
    internal static class AsepriteReader
    {
        private sealed class Layer
        {
            public int Flags, Type, Level, Opacity, Tileset = -1;
            public bool Visible;
            public int EffOpacity = 255;
        }

        private sealed class Cel
        {
            public int X, Y, Opacity, Z, Type, Link, W, H;
            public byte[]? Rgba;
        }

        private sealed class Tileset
        {
            public int TileW, TileH;
            public List<byte[]> Tiles = new List<byte[]>();
        }

        public static AseImage Read(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 128 || U16(d, 4) != 0xA5E0)
                throw new InvalidDataException(Loc.T("Geçerli bir Aseprite dosyası değil."));

            int nframes = U16(d, 6), W = U16(d, 8), H = U16(d, 10), depth = U16(d, 12);
            uint hflags = U32(d, 14);
            int transparentIndex = d[28];
            bool layerOpacityValid = (hflags & 1) != 0;
            int bpp = depth switch
            {
                32 => 4,
                16 => 2,
                8 => 1,
                _ => throw new InvalidDataException(Loc.T("Geçerli bir Aseprite dosyası değil.")),
            };
            if (nframes == 0 || W == 0 || H == 0) throw new InvalidDataException(Loc.T("Aseprite dosyasında kare yok."));

            var palette = new List<byte[]>();
            for (int i = 0; i < 256; i++) palette.Add(new byte[] { 0, 0, 0, 255 });
            var layers = new List<Layer>();
            var tilesets = new Dictionary<int, Tileset>();
            var frames = new List<(int dur, Dictionary<int, Cel> cels)>();

            byte[] ToRgba(byte[] buf, int offset, int n, bool isBackground)
            {
                var o = new byte[n * 4];
                for (int i = 0; i < n; i++)
                {
                    int di = i * 4;
                    if (depth == 32)
                    {
                        Buffer.BlockCopy(buf, offset + i * 4, o, di, 4);
                    }
                    else if (depth == 16)
                    {
                        byte v = buf[offset + i * 2], a = buf[offset + i * 2 + 1];
                        o[di] = v; o[di + 1] = v; o[di + 2] = v; o[di + 3] = a;
                    }
                    else
                    {
                        int ix = buf[offset + i];
                        if (ix == transparentIndex && !isBackground) continue; // saydam
                        byte[] c = ix < palette.Count ? palette[ix] : palette[0];
                        Buffer.BlockCopy(c, 0, o, di, 4);
                    }
                }
                return o;
            }

            int p = 128;
            for (int f = 0; f < nframes; f++)
            {
                if (p + 16 > d.Length) break;
                int fsize = checked((int)U32(d, p));
                int oldChunks = U16(d, p + 6), dur = U16(d, p + 8);
                int newChunks = checked((int)U32(d, p + 12));
                int nchunks = newChunks != 0 ? newChunks : oldChunks;
                int q = p + 16;
                var cels = new Dictionary<int, Cel>();

                for (int ci = 0; ci < nchunks && q + 6 <= d.Length; ci++)
                {
                    int csize = checked((int)U32(d, q));
                    int ctype = U16(d, q + 4);
                    int c = q + 6;
                    int end = Math.Min(d.Length, q + csize);

                    switch (ctype)
                    {
                        case 0x2004: // katman
                        {
                            var L = new Layer
                            {
                                Flags = U16(d, c),
                                Type = U16(d, c + 2),
                                Level = U16(d, c + 4),
                                Opacity = d[c + 12],
                            };
                            int nameLen = U16(d, c + 16);
                            int k = c + 18 + nameLen;
                            if (L.Type == 2 && k + 4 <= end) L.Tileset = (int)U32(d, k);
                            layers.Add(L);
                            break;
                        }
                        case 0x2019: // yeni palet
                        {
                            int size = (int)U32(d, c), first = (int)U32(d, c + 4), last = (int)U32(d, c + 8);
                            while (palette.Count < size) palette.Add(new byte[] { 0, 0, 0, 255 });
                            int k = c + 20;
                            for (int i = first; i <= last && k + 6 <= end; i++)
                            {
                                int eflags = U16(d, k);
                                palette[i] = new[] { d[k + 2], d[k + 3], d[k + 4], d[k + 5] };
                                k += 6;
                                if ((eflags & 1) != 0) k += 2 + U16(d, k);
                            }
                            break;
                        }
                        case 0x0004: // eski palet (0–255)
                        case 0x0011: // eski palet (0–63)
                        {
                            int packets = U16(d, c), k = c + 2, idx = 0;
                            for (int pk = 0; pk < packets && k + 2 <= end; pk++)
                            {
                                idx += d[k];
                                int n = d[k + 1] == 0 ? 256 : d[k + 1];
                                k += 2;
                                for (int i = 0; i < n && k + 3 <= end; i++, idx++, k += 3)
                                {
                                    int r = d[k], g = d[k + 1], b = d[k + 2];
                                    if (ctype == 0x0011) { r = r * 255 / 63; g = g * 255 / 63; b = b * 255 / 63; }
                                    if (idx < palette.Count) palette[idx] = new[] { (byte)r, (byte)g, (byte)b, (byte)255 };
                                }
                            }
                            break;
                        }
                        case 0x2023: // tileset
                        {
                            int id = (int)U32(d, c);
                            uint tflags = U32(d, c + 4);
                            int ntiles = (int)U32(d, c + 8), tw = U16(d, c + 12), th = U16(d, c + 14);
                            int nameLen = U16(d, c + 32); // 4+4+4+2+2+2+14 baytlık başlık
                            int k = c + 34 + nameLen;
                            var ts = new Tileset { TileW = tw, TileH = th };
                            if ((tflags & 1) != 0) k += 8; // dış dosya bağlantısı
                            if ((tflags & 2) != 0 && k + 4 <= end)
                            {
                                int dlen = (int)U32(d, k);
                                byte[] raw = Inflate(d, k + 4, Math.Min(dlen, end - k - 4));
                                int per = tw * th * bpp;
                                for (int t = 0; t < ntiles && (t + 1) * per <= raw.Length; t++)
                                    ts.Tiles.Add(ToRgba(raw, t * per, tw * th, false));
                            }
                            tilesets[id] = ts;
                            break;
                        }
                        case 0x2005: // cel
                        {
                            int li = U16(d, c);
                            var cel = new Cel
                            {
                                X = S16(d, c + 2),
                                Y = S16(d, c + 4),
                                Opacity = d[c + 6],
                                Type = U16(d, c + 7),
                                Z = S16(d, c + 9),
                            };
                            int k = c + 16;
                            bool isBg = li < layers.Count && (layers[li].Flags & 8) != 0;
                            if (cel.Type == 1)
                            {
                                cel.Link = U16(d, k);
                            }
                            else if (cel.Type == 0 || cel.Type == 2)
                            {
                                cel.W = U16(d, k);
                                cel.H = U16(d, k + 2);
                                byte[] raw;
                                int rawOff;
                                if (cel.Type == 2) { raw = Inflate(d, k + 4, end - k - 4); rawOff = 0; }
                                else { raw = d; rawOff = k + 4; }
                                if (raw.Length - rawOff >= cel.W * cel.H * bpp)
                                    cel.Rgba = ToRgba(raw, rawOff, cel.W * cel.H, isBg);
                            }
                            else if (cel.Type == 3 && li < layers.Count)
                            {
                                int tilesW = U16(d, k), tilesH = U16(d, k + 2);
                                uint idMask = U32(d, k + 6), xFlip = U32(d, k + 10), yFlip = U32(d, k + 14);
                                byte[] raw = Inflate(d, k + 32, end - k - 32);
                                if (tilesets.TryGetValue(layers[li].Tileset, out var ts))
                                {
                                    int w = tilesW * ts.TileW, h = tilesH * ts.TileH;
                                    var o = new byte[w * h * 4];
                                    for (int ty = 0; ty < tilesH; ty++)
                                    for (int tx = 0; tx < tilesW; tx++)
                                    {
                                        int ri = (ty * tilesW + tx) * 4;
                                        if (ri + 4 > raw.Length) continue;
                                        uint v = U32(raw, ri);
                                        int tile = (int)(v & idMask);
                                        if (tile == 0 || tile >= ts.Tiles.Count) continue;
                                        byte[] src = ts.Tiles[tile];
                                        for (int yy = 0; yy < ts.TileH; yy++)
                                        for (int xx = 0; xx < ts.TileW; xx++)
                                        {
                                            int sx = (v & xFlip) != 0 ? ts.TileW - 1 - xx : xx;
                                            int sy = (v & yFlip) != 0 ? ts.TileH - 1 - yy : yy;
                                            Buffer.BlockCopy(src, (sy * ts.TileW + sx) * 4, o,
                                                ((ty * ts.TileH + yy) * w + tx * ts.TileW + xx) * 4, 4);
                                        }
                                    }
                                    cel.W = w;
                                    cel.H = h;
                                    cel.Rgba = o;
                                    cel.Type = 0;
                                }
                            }
                            cels[li] = cel;
                            break;
                        }
                    }
                    q += Math.Max(csize, 6);
                }
                frames.Add((dur, cels));
                p += Math.Max(fsize, 16);
            }

            // efektif görünürlük ve opaklık; grup opaklığı eski dosyalarda 0 kayıtlı ve kullanılmaz
            var stack = new List<(int level, bool visible)>();
            foreach (var L in layers)
            {
                while (stack.Count > 0 && stack[stack.Count - 1].level >= L.Level) stack.RemoveAt(stack.Count - 1);
                L.Visible = (L.Flags & 1) != 0 && stack.All(s => s.visible);
                L.EffOpacity = layerOpacityValid ? L.Opacity : 255;
                stack.Add((L.Level, (L.Flags & 1) != 0));
            }

            var result = new List<AseFrame>();
            foreach (var (dur, cels) in frames)
            {
                var canvas = new byte[W * H * 4];
                var order = new List<(int key, int z, int li, Cel cel)>();
                for (int li = 0; li < layers.Count; li++)
                {
                    var L = layers[li];
                    if (L.Type == 1 || !L.Visible) continue;
                    if (!cels.TryGetValue(li, out var cel)) continue;
                    if (cel.Type == 1)
                    {
                        if (cel.Link >= frames.Count || !frames[cel.Link].cels.TryGetValue(li, out var linked) || linked.Rgba == null) continue;
                        cel = new Cel { X = cel.X, Y = cel.Y, Opacity = cel.Opacity, Z = cel.Z, W = linked.W, H = linked.H, Rgba = linked.Rgba };
                    }
                    if (cel.Rgba == null) continue;
                    order.Add((li + cel.Z, cel.Z, li, cel));
                }
                order.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.z.CompareTo(b.z));

                foreach (var (_, _, li, cel) in order)
                {
                    int op = cel.Opacity * layers[li].EffOpacity / 255;
                    byte[] src = cel.Rgba!;
                    for (int yy = 0; yy < cel.H; yy++)
                    {
                        int cy = cel.Y + yy;
                        if (cy < 0 || cy >= H) continue;
                        for (int xx = 0; xx < cel.W; xx++)
                        {
                            int cx = cel.X + xx;
                            if (cx < 0 || cx >= W) continue;
                            int si = (yy * cel.W + xx) * 4;
                            int sa = src[si + 3] * op / 255;
                            if (sa == 0) continue;
                            int di = (cy * W + cx) * 4;
                            int da = canvas[di + 3];
                            int oa = sa + da * (255 - sa) / 255;
                            for (int k = 0; k < 3; k++)
                                canvas[di + k] = (byte)((src[si + k] * sa + canvas[di + k] * da * (255 - sa) / 255) / oa);
                            canvas[di + 3] = (byte)oa;
                        }
                    }
                }
                result.Add(new AseFrame(canvas, dur));
            }

            if (result.Count == 0) throw new InvalidDataException(Loc.T("Aseprite dosyasında kare yok."));
            return new AseImage(W, H, result);
        }

        /// <summary>RGBA kareyi WPF görüntüsüne çevirir; <paramref name="scale"/> &gt; 1 ise piksel piksel (en yakın komşu) büyütür.</summary>
        public static BitmapSource ToBitmap(int w, int h, byte[] rgba, int scale = 1)
        {
            int W = w * scale, H = h * scale, stride = W * 4;
            var bgra = new byte[stride * H];
            for (int y = 0; y < H; y++)
            {
                int sy = y / scale;
                for (int x = 0; x < W; x++)
                {
                    int si = (sy * w + x / scale) * 4, di = y * stride + x * 4;
                    bgra[di] = rgba[si + 2];
                    bgra[di + 1] = rgba[si + 1];
                    bgra[di + 2] = rgba[si];
                    bgra[di + 3] = rgba[si + 3];
                }
            }
            var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Tüm kareleri yan yana tek bir şerit (sprite sheet) olarak birleştirir.</summary>
        public static BitmapSource ToSheet(AseImage img)
        {
            int n = img.Frames.Count, W = img.Width * n, H = img.Height;
            var rgba = new byte[W * H * 4];
            for (int f = 0; f < n; f++)
                for (int y = 0; y < H; y++)
                    Buffer.BlockCopy(img.Frames[f].Rgba, y * img.Width * 4, rgba, (y * W + f * img.Width) * 4, img.Width * 4);
            return ToBitmap(W, H, rgba);
        }

        private static byte[] Inflate(byte[] src, int offset, int count)
        {
            if (count <= 0) return Array.Empty<byte>();
            using var input = new MemoryStream(src, offset, count, false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }

        private static int U16(byte[] d, int i) => d[i] | (d[i + 1] << 8);
        private static int S16(byte[] d, int i) => (short)(d[i] | (d[i + 1] << 8));
        private static uint U32(byte[] d, int i) => (uint)(d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24));
    }
}
