#!/usr/bin/env python3
"""recipes.json içindeki ffmpeg / soffice tariflerini gerçek dosyalarla çalıştırır.

Kullanım: python tests/run_recipes.py <örnek_dosyalar_klasörü> <çıktı_klasörü>
Uygulamanın kullandığı argüman listesiyle birebir aynı çağrıyı yapar
(kabuk yok, argümanlar liste olarak gider; C# tarafındaki ArgumentList ile aynı).
"""
import json, os, shutil, subprocess, sys, pathlib, tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
RECIPES = json.loads((ROOT / "src/Kumkat/recipes.json").read_text(encoding="utf-8"))
SAMPLES = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2]); OUT.mkdir(parents=True, exist_ok=True)

SAMPLE_FOR = {
    "image": ["foto.png", "foto.psd"], "video": ["klip.mov"], "audio": ["ses.wav"],
    "pdf": ["sunum.pdf"], "document": ["not.docx", "not.txt"], "sheet": ["tablo.xlsx"],
    "slides": ["sunum.pptx"], "csv": ["tablo.csv"],
}
SAME_IMAGE_WRITABLE = {"png", "jpg", "jpeg", "bmp", "tif", "tiff", "webp"}
lo_profile = pathlib.Path(tempfile.mkdtemp(prefix="kumkat_lo_")).as_uri()

def probe(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "stream=codec_name,width,height",
                        "-of", "csv=p=0", str(path)], capture_output=True, text=True)
    return r.stdout.strip().replace("\n", " | ")

ok = fail = skip = 0
for g in RECIPES["groups"]:
    for sample in SAMPLE_FOR.get(g["id"], []):
        src = SAMPLES / sample
        in_ext = src.suffix[1:].lower()
        for a in g["actions"]:
            if a["engine"] not in ("ffmpeg", "soffice"):
                skip += 1; continue
            # RecipeBook.ResolveOutputExt ile aynı kural
            ext = a["ext"]
            if ext == "{same}":
                ext = in_ext if not (g["id"] == "image" and in_ext not in SAME_IMAGE_WRITABLE) else "jpg"
            if ext == in_ext and not a.get("suffix"):
                continue  # aynı formata dönüştürme menüde gösterilmez
            outdir = pathlib.Path(tempfile.mkdtemp(prefix="kumkat_out_"))
            final = OUT / f"{src.stem}{a.get('suffix', '')}__{g['id']}_{a['id']}.{ext}"
            out = outdir / f"{src.stem}.{ext}"
            args = [x.replace("{in}", str(src)).replace("{out}", str(out))
                     .replace("{outdir}", str(outdir)).replace("{lo_profile}", lo_profile)
                     .replace("{fx}", "0.1").replace("{fy}", "0.2").replace("{fw}", "0.5").replace("{fh}", "0.6")
                    for x in a["args"]]
            exe = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-y"] if a["engine"] == "ffmpeg" else ["soffice"]
            r = subprocess.run(exe + args, capture_output=True, text=True, timeout=300)
            produced = out if out.exists() else None
            if produced and produced.stat().st_size > 0 and r.returncode == 0:
                shutil.move(str(produced), final)
                info = probe(final) if a["engine"] == "ffmpeg" else f"{final.stat().st_size} B"
                print(f"OK   {g['id']:8} {sample:11} -> {a['id']:6} {info}")
                ok += 1
            else:
                print(f"FAIL {g['id']:8} {sample:11} -> {a['id']:6} rc={r.returncode} {r.stderr.strip()[-300:]}")
                fail += 1
            shutil.rmtree(outdir, ignore_errors=True)

print(f"\n{ok} başarılı, {fail} hatalı, {skip} uygulama-içi motor (burada test edilmedi)")
sys.exit(1 if fail else 0)
