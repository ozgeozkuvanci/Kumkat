# Kumkat 🍊

Windows için **tek hareketle, tamamen yerel** dosya dönüştürücü. Dosyayı açmaya, bir siteye yüklemeye, "dışa aktar" menüsü aramaya gerek yok. İmlecin etrafında dairesel bir format menüsü açılır, birini seçersin, dönüştürülmüş dosya orijinalin yanına düşer.

Hiçbir dosya internete gitmez. Uygulama sistem tepsisinde yaşar.

## Nasıl kullanılır

| Yol | Ne yaparsın |
|---|---|
| **Shift + sürükle** | Explorer'da (veya masaüstünde) bir dosyayı sürüklemeye başla, **Shift**'e bas → imlecin etrafında halka açılır. Dosyayı bir dilimin üzerine bırak. Ortaya bırakırsan iptal. |
| **Ctrl + Alt + K** | Explorer'da dosya(ları) seç, kısayola bas → halka imlecin yanında açılır. Fare, **ok tuşları + Enter** ya da **1–9** ile seç, **Esc** ile iptal. Masaüstündeki dosyalar için önce **Ctrl+C**. |
| **Sağ tık › Gönder › Kumkat** | Tepsi menüsünden açılır; çoklu seçimde hepsini tek seferde alır. |
| **Klasik sağ tık menüsü** | İsteğe bağlı (Windows 11'de "Diğer seçenekleri göster" altında). |
| **Tepsi simgesine tıkla** | Dosya seçme penceresi. |

**Fare tekerleği** halkanın sayfalarını çevirir (Shift+sürüklerken de çalışır; PageUp/PageDown da olur). Görsel ve videolarda üç sayfa var:

| Sayfa | Görsel | Video |
|---|---|---|
| Dönüştür | PNG, JPG, WebP, AVIF, PDF, DOCX, ICO, BMP, TIFF | MP4, WebM, MOV, MKV, GIF, MP3 |
| Kırp · Döndür | **Serbest kırp** (önizlemede fareyle alan seç), 1:1, 16:9, 9:16, 4:3 (ortadan), sağa/sola döndür, ayna, siyah-beyaz | aynısı (önizleme ilk kareden) |
| Boyut · Temizle / Hız | %50, %25, uzun kenar 1920 / 1080, JPG sıkıştır, meta veriyi temizle | sıkıştır, 720p, 2× hız, sesi sil, kapak karesi |

Kumkat ilk çalıştığında kendini Windows açılışına ekler (hangi dilde olursa olsun); bilgisayar her açıldığında tepside hazır bekler. Kapatmak için tepsi menüsünden **Windows ile başlat** işaretini kaldır ya da kurulumda o seçeneği boş bırak.

Bitince bir bildirim çıkar; tıklarsan Explorer yeni dosyayı seçili halde açar. Aynı adda dosya varsa `ad (1).webp` gibi yeni bir ad verilir, orijinale asla dokunulmaz.

> Shift+sürükle Explorer'da normalde "taşı" demektir. Kumkat bırakma sırasında her zaman "kopyala" yanıtı verir, böylece Explorer kaynak dosyayı silmez.

## Desteklenen dönüşümler

| Girdi | Çıktılar | Araçlar (turkuaz dilimler) |
|---|---|---|
| Görsel: PNG, JPG, HEIC, WebP, AVIF, BMP, GIF, TIFF, ICO, PSD | PNG, JPG, WebP, AVIF, PDF, DOCX (Word sayfasına yerleştirilmiş görsel), ICO, BMP, TIFF | %50 küçült, meta veriyi (EXIF/konum) temizle |
| Video: MP4, MOV, MKV, AVI, WebM, WMV… | MP4, WebM, MOV, MKV, GIF, MP3 (sesi al) | Sıkıştır (≤720p), sesi sil |
| Ses: MP3, WAV, M4A, FLAC, OGG, OPUS, WMA… | MP3, M4A, WAV, FLAC, OGG, OPUS | |
| PDF | PNG / JPG (her sayfa), DOCX | Sıkıştır (Ghostscript varsa) |
| Belge: DOCX, DOC, ODT, RTF, TXT, HTML | PDF, DOCX, ODT, RTF, TXT, HTML | |
| Tablo: XLSX, XLS, ODS | XLSX, CSV, PDF, ODS | |
| Sunum: PPTX, PPT, ODP, KEY | PDF, PPTX, ODP, PNG (kapak) | |
| CSV / TSV | XLSX, JSON, Markdown tablo, PDF | |
| JSON | CSV | Düzenle (girintile), küçült |
| Markdown | HTML, PDF, TXT | |

Aynı türden birden çok dosya seçilirse hepsi sırayla dönüştürülür.

## Dil

Kumkat Türkçe ve **Fas Arapçası (الدارجة المغربية)** ile geliyor. Windows'un dili Arapça ya da bölgesi Fas ise Kumkat kendiliğinden Darija açılıyor. Tepsi menüsündeki **Dil / اللغة** bölümünden değiştirilebilir. Kurulum sihirbazında da bir "Darija" seçeneği var. Çeviriler `src/Kumkat/Core/Loc.cs` dosyasında; Türkçe metin anahtar olarak kullanılıyor, Darija karşılığı yoksa Türkçe görünüyor.

## Gerekenler

- Windows 10 (1809+) veya 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Ses/video ve WebP/AVIF/ICO için **FFmpeg**, Office belgeleri için **LibreOffice**, PDF sıkıştırma için (isteğe bağlı) **Ghostscript**

Eksik araç gerektiren dilimler gri görünür; üzerine gelince neyin eksik olduğu yazar. Tepsi menüsü › **Araçlar**'dan tek tıkla kurabilirsin; ya da:

```
winget install --id Gyan.FFmpeg -e
winget install --id TheDocumentFoundation.LibreOffice -e
winget install --id ArtifexSoftware.GhostScript -e
```

Kurduktan sonra Araçlar › **Yeniden tara**. PNG/JPG/BMP/TIFF/GIF dönüşümleri, görsel→PDF ve PDF→PNG/JPG hiçbir ek araç istemez (Windows'un kendi bileşenlerini kullanır).

## Kurulum dosyası

```
build-installer.cmd
```

`dist\Kumkat-Kurulum-0.1.0.exe` üretir. Kurulumda şu seçenekler sorulur: Windows ile başlat, Gönder menüsü, klasik sağ tık menüsü, FFmpeg (varsayılan açık) ve LibreOffice. Kaldırma işlemi Ayarlar › Uygulamalar'dan yapılır ve Kumkat'ın eklediği her şeyi temizler.

Derlemek için bir kerelik Inno Setup gerekir: `winget install --id JRSoftware.InnoSetup -e`

Not: Exe dijital olarak imzalı değil. Bu yüzden ilk açışta "Windows bilgisayarınızı korudu" uyarısı çıkabilir; **Ek bilgi › Yine de çalıştır** denmesi yeterli.

## Derleme

```
build.cmd
```

ya da

```
dotnet publish src\Kumkat\Kumkat.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Çıktı: `publish\Kumkat.exe` (tek dosya). Çalıştırınca tepsiye yerleşir; tepsi menüsünden "Windows ile başlat"ı açabilirsin.

Depo GitHub'a gönderilirse `.github/workflows/build.yml` her push'ta Windows'ta derleyip `Kumkat.exe`'yi artifact olarak yükler ve tüm FFmpeg/LibreOffice tariflerini Linux'ta gerçek dosyalarla dener.

## Kendi dönüşümlerini eklemek

Tepsi › Gelişmiş › **Tarifleri düzenle** gömülü `recipes.json`'ı `%AppData%\Kumkat\recipes.json` konumuna kopyalayıp açar. Bir FFmpeg tarifi şöyle görünür:

```json
{ "id": "webp", "label": "WEBP", "ext": "webp", "engine": "ffmpeg",
  "args": ["-i", "{in}", "-frames:v", "1", "-c:v", "libwebp", "-quality", "85", "{out}"] }
```

`{in}`, `{out}`, `{outdir}`, `{lo_profile}` yer tutucuları doldurulur; argümanlar kabuk olmadan tek tek geçer (tırnak derdi yok). `"tool": true` dilimi araç olarak boyar, `"suffix"` çıktı adına ek koyar, `"ext": "{same}"` girdiyle aynı uzantıyı kullanır. Kaydedip Gelişmiş › **Tarifleri yeniden yükle**.

Tarifleri test etmek için (FFmpeg + LibreOffice kurulu bir makinede):

```
python tests/make_samples.py samples
python tests/run_recipes.py samples out
```

## Yapı

```
src/Kumkat/
  Program.cs              tek örnek, ikinci örnekten gelen dosyaları borudan iletir
  TrayApp.cs              tepsi menüsü, kısayol, Shift+sürükle, iş kuyruğu, bildirimler
  recipes.json            tüm dönüşüm tarifleri (exe'ye gömülü)
  UI/RadialMenuWindow.cs  dairesel menü (fare, klavye, sürükle-bırak)
  Shell/                  fare kancası, kısayol, Explorer seçimi, Gönder/sağ tık/başlangıç
  Core/                   Converter + motorlar: FFmpeg, LibreOffice, Ghostscript,
                          WIC görsel, Windows.Data.Pdf, görsel→PDF yazıcı, CSV/JSON/Markdown
tests/                    tarif testleri
```

Ayarlar ve günlük: `%AppData%\Kumkat\` (settings.json, kumkat.log).

## Bilinen sınırlar

- Shift+sürükle yalnızca Explorer ve masaüstünden başlayan sürüklemelerde açılır (yanlışlıkla tetiklenmesin diye).
- Windows 11'de aynı pencerede birden çok sekme açıksa kısayol, seçimi olan ilk sekmeyi alır.
- HEIC/AVIF/WebP okuma önce Windows codec'leriyle denenir, olmazsa FFmpeg'e düşer.
- Görsel/PSD → DOCX görseli A4 bir Word sayfasına yerleştirir; içindeki yazılar düzenlenebilir metne dönüşmez (OCR yok).
- PSD → PNG/JPG/PDF/BMP/TIFF kendi okuyucusuyla, FFmpeg olmadan çalışır (RGB/CMYK/Gri, 8/16 bit). PSD → WebP/AVIF/ICO ve küçültme araçları FFmpeg ister.
- PSD dosyalarından Photoshop'un kaydettiği birleşik (düzleştirilmiş) görüntü okunur; katmanlar ayrı ayrı alınmaz. PSD "Uyumluluğu en üst düzeye çıkar" kapalı kaydedildiyse bu görüntü boş/beyaz olabilir. PSB (büyük belge) desteklenmez.
- LibreOffice'in PDF→DOCX dönüşümü düzeni korur ama metni çerçeveler içinde verir; düzenlenebilir akıcı metin beklemeyin.
