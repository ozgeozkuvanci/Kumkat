#!/usr/bin/env python3
"""Core/AsepriteReader.cs ile aynı algoritma: .aseprite/.ase dosyasının görünür katmanlarını birleştirip
her kare için RGBA görüntü üretir. Kullanım: python aseprite_reference.py dosya.aseprite çıktı_öneki"""
import struct, sys, zlib

def read(path):
    d = open(path, 'rb').read()
    if len(d) < 128 or struct.unpack_from('<H', d, 4)[0] != 0xA5E0:
        raise ValueError('Geçerli bir Aseprite dosyası değil.')
    nframes, W, H, depth = struct.unpack_from('<HHHH', d, 6)
    hflags = struct.unpack_from('<I', d, 14)[0]
    transp_idx = d[28]
    layer_opacity_valid = bool(hflags & 1)
    has_uuid = bool(hflags & 4)
    palette = [(0, 0, 0, 255)] * 256
    layers = []          # dict: flags, type, level, blend, opacity, tileset, visible (efektif)
    tilesets = {}        # id -> (tw, th, [tile rgba bytes])
    frames = []          # (duration, {layer: cel})
    p = 128
    bpp = {32: 4, 16: 2, 8: 1}[depth]

    def to_rgba(buf, n, is_bg):
        out = bytearray(n * 4)
        for i in range(n):
            if depth == 32:
                out[i*4:i*4+4] = buf[i*4:i*4+4]
            elif depth == 16:
                v, a = buf[i*2], buf[i*2+1]
                out[i*4:i*4+4] = bytes((v, v, v, a))
            else:
                ix = buf[i]
                if ix == transp_idx and not is_bg:
                    out[i*4:i*4+4] = b'\0\0\0\0'
                else:
                    out[i*4:i*4+4] = bytes(palette[ix])
        return out

    for f in range(nframes):
        fsize, magic, old_chunks, dur = struct.unpack_from('<IHHH', d, p)
        new_chunks = struct.unpack_from('<I', d, p + 12)[0]
        nchunks = new_chunks if new_chunks else old_chunks
        q = p + 16
        cels = {}
        for _ in range(nchunks):
            csize, ctype = struct.unpack_from('<IH', d, q)
            c = q + 6
            if ctype == 0x2004:  # katman
                lflags, ltype, level, _dw, _dh, blend, opacity = struct.unpack_from('<HHHHHHB', d, c)
                nlen = struct.unpack_from('<H', d, c + 16)[0]
                k = c + 18 + nlen
                tileset = struct.unpack_from('<I', d, k)[0] if ltype == 2 else None
                layers.append(dict(flags=lflags, type=ltype, level=level, blend=blend, opacity=opacity, tileset=tileset))
            elif ctype == 0x2019:  # yeni palet
                size, first, last = struct.unpack_from('<III', d, c)
                if size > len(palette): palette += [(0, 0, 0, 255)] * (size - len(palette))
                k = c + 20
                for i in range(first, last + 1):
                    eflags = struct.unpack_from('<H', d, k)[0]
                    palette[i] = tuple(d[k+2:k+6]); k += 6
                    if eflags & 1: k += 2 + struct.unpack_from('<H', d, k)[0]
            elif ctype in (0x0004, 0x0011):  # eski palet
                packets = struct.unpack_from('<H', d, c)[0]; k = c + 2; idx = 0
                for _ in range(packets):
                    idx += d[k]; n = d[k+1] or 256; k += 2
                    for i in range(n):
                        r, g, b = d[k:k+3]; k += 3
                        if ctype == 0x0011: r, g, b = (r * 255 // 63, g * 255 // 63, b * 255 // 63)
                        if idx < len(palette): palette[idx] = (r, g, b, 255)
                        idx += 1
            elif ctype == 0x2023:  # tileset
                tid, tflags, ntiles, tw, th = struct.unpack_from('<IIIHH', d, c)
                nlen = struct.unpack_from('<H', d, c + 32)[0]   # 4+4+4+2+2+2+14 bayt başlık
                k = c + 34 + nlen
                tiles = []
                if tflags & 1: k += 8
                if tflags & 2:
                    dlen = struct.unpack_from('<I', d, k)[0]
                    raw = zlib.decompress(d[k+4:k+4+dlen])
                    per = tw * th * bpp
                    for t in range(ntiles):
                        tiles.append(to_rgba(raw[t*per:(t+1)*per], tw * th, False))
                tilesets[tid] = (tw, th, tiles)
            elif ctype == 0x2005:  # cel
                li, x, y, opacity, ctyp, z = struct.unpack_from('<HhhBHh', d, c)
                k = c + 16
                cel = dict(x=x, y=y, opacity=opacity, z=z, type=ctyp)
                if ctyp == 1:
                    cel['link'] = struct.unpack_from('<H', d, k)[0]
                elif ctyp in (0, 2):
                    w, h = struct.unpack_from('<HH', d, k)
                    raw = d[k+4:q+csize]
                    if ctyp == 2: raw = zlib.decompress(raw)
                    is_bg = bool(layers[li]['flags'] & 8) if li < len(layers) else False
                    cel.update(w=w, h=h, rgba=to_rgba(raw, w * h, is_bg))
                elif ctyp == 3:
                    tw_, th_, bits, idmask, xf, yf, df = struct.unpack_from('<HHHIIII', d, k)
                    raw = zlib.decompress(d[k+32:q+csize])
                    ts = tilesets.get(layers[li]['tileset'])
                    if ts:
                        tsw, tsh, tiles = ts
                        w, h = tw_ * tsw, th_ * tsh
                        out = bytearray(w * h * 4)
                        for ty in range(th_):
                            for tx in range(tw_):
                                v = struct.unpack_from('<I', raw, (ty * tw_ + tx) * 4)[0]
                                tile = v & idmask
                                if tile == 0 or tile >= len(tiles): continue
                                src = tiles[tile]
                                for yy in range(tsh):
                                    for xx in range(tsw):
                                        sx = tsw - 1 - xx if v & xf else xx
                                        sy = tsh - 1 - yy if v & yf else yy
                                        o = ((ty * tsh + yy) * w + tx * tsw + xx) * 4
                                        out[o:o+4] = src[(sy * tsw + sx) * 4:(sy * tsw + sx) * 4 + 4]
                        cel.update(w=w, h=h, rgba=out, type=0)
                cels[li] = cel
            q += csize
        frames.append((dur, cels))
        p += fsize

    # efektif görünürlük ve opaklık (gruplar dahil)
    stack = []
    for L in layers:
        while stack and stack[-1][0] >= L['level']: stack.pop()
        parent_vis = all(s[1] for s in stack)
        L['visible'] = bool(L['flags'] & 1) and parent_vis
        L['eff_opacity'] = L['opacity'] if layer_opacity_valid else 255
        for s in stack: L['eff_opacity'] = L['eff_opacity'] * s[2] // 255
        # grup opaklığı eski dosyalarda 0 kayıtlı ve Aseprite onu kullanmaz: yalnızca görünürlüğü miras alınır
        stack.append((L['level'], bool(L['flags'] & 1), 255))

    images = []
    for fi, (dur, cels) in enumerate(frames):
        canvas = bytearray(W * H * 4)
        order = []
        for li, L in enumerate(layers):
            if L['type'] == 1 or not L['visible']: continue
            cel = cels.get(li)
            if cel is None: continue
            if cel['type'] == 1:
                link = frames[cel['link']][1].get(li)
                if not link or 'rgba' not in link: continue
                cel = dict(link, x=cel['x'], y=cel['y'], opacity=cel['opacity'], z=cel['z'])
            if 'rgba' not in cel: continue
            order.append((li + cel['z'], cel['z'], li, cel))
        order.sort(key=lambda t: (t[0], t[1]))
        for _, _, li, cel in order:
            op = cel['opacity'] * layers[li]['eff_opacity'] // 255
            w, h, src = cel['w'], cel['h'], cel['rgba']
            for yy in range(h):
                cy = cel['y'] + yy
                if cy < 0 or cy >= H: continue
                for xx in range(w):
                    cx = cel['x'] + xx
                    if cx < 0 or cx >= W: continue
                    si = (yy * w + xx) * 4
                    sa = src[si+3] * op // 255
                    if sa == 0: continue
                    di = (cy * W + cx) * 4
                    da = canvas[di+3]
                    oa = sa + da * (255 - sa) // 255
                    for k in range(3):
                        canvas[di+k] = (src[si+k] * sa + canvas[di+k] * da * (255 - sa) // 255) // oa
                    canvas[di+3] = oa
        images.append((dur, bytes(canvas)))
    return W, H, images

if __name__ == '__main__':
    from PIL import Image
    W, H, imgs = read(sys.argv[1])
    for i, (dur, rgba) in enumerate(imgs):
        Image.frombytes('RGBA', (W, H), rgba).save(f'{sys.argv[2]}-{i+1:03}.png')
    print(f'{W}x{H}, {len(imgs)} kare, süreler {[d for d, _ in imgs]}')
