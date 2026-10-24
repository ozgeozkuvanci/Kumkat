using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kumkat.Core
{
    /// <summary>
    /// Arayüz dili. Anahtar Türkçe metnin kendisidir; Darija karşılığı yoksa Türkçe gösterilir.
    /// "ar" = Fas Arapçası (الدارجة المغربية), sağdan sola.
    /// </summary>
    internal static class Loc
    {
        public const string Turkish = "tr";
        public const string Darija = "ar";

        public static string Lang { get; private set; } = Turkish;

        public static bool Rtl => Lang == Darija;

        /// <param name="setting">"tr", "ar" veya null/"auto" (Windows diline ve bölgesine göre).</param>
        public static void Apply(string? setting)
        {
            Lang = setting == Turkish || setting == Darija ? setting : Detect();
        }

        /// <summary>Windows arayüzü Arapça ya da bölge/biçim Fas ise Darija, değilse Türkçe.</summary>
        public static string Detect()
        {
            try
            {
                if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar") return Darija;
                if (CultureInfo.CurrentCulture.Name.EndsWith("-MA", StringComparison.OrdinalIgnoreCase)) return Darija;
                if (RegionInfo.CurrentRegion.TwoLetterISORegionName == "MA") return Darija;
            }
            catch
            {
                // kültür bilgisi okunamazsa varsayılan
            }
            return Turkish;
        }

        public static string T(string tr)
        {
            if (Lang == Darija && Ar.TryGetValue(tr, out var ar)) return ar;
            return tr;
        }

        public static string F(string trFormat, params object?[] args) =>
            string.Format(CultureInfo.CurrentCulture, T(trFormat), args);

        private static readonly Dictionary<string, string> Ar = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ------------------------------------------------------------ tepsi menüsü
            ["Dosya seç ve dönüştür…"] = "ختار فيشي وبدّلو…",
            ["Shift + sürükle ile menü aç"] = "حلّ المينو بـ Shift + الجرّ",
            ["{0} kısayolu (Explorer seçimi)"] = "الاختصار {0} (اللي مختار ف Explorer)",
            ["Sağ tık › Gönder › Kumkat"] = "كليك ليمن › صيفط لـ › كومكات",
            ["Sağ tık menüsü (klasik / \"Diğer seçenekler\")"] = "مينو الكليك ليمن (الكلاسيكي / \"خيارات أخرى\")",
            ["Windows ile başlat"] = "شعّل مع ويندوز",
            ["Araçlar"] = "الأدوات",
            ["ses, video, WebP/AVIF/ICO"] = "الصوت، الفيديو، WebP/AVIF/ICO",
            ["Word, Excel, PowerPoint, PDF→DOCX"] = "Word، Excel، PowerPoint، PDF→DOCX",
            ["PDF sıkıştırma (isteğe bağlı)"] = "ضغط PDF (اختياري)",
            ["✓ {0} — {1}"] = "✓ {0} — {1}",
            ["✗ {0} yok — {1}"] = "✗ {0} ماكاينش — {1}",
            ["    {0}'i kur (winget)"] = "    ركّب {0} (winget)",
            ["Yeniden tara"] = "قلّب عاوتاني",
            ["Araçlar tarandı"] = "تقلّبو الأدوات",
            ["var"] = "كاين",
            ["yok"] = "ماكاينش",
            ["Gelişmiş"] = "خيارات أخرى",
            ["Tarifleri düzenle (recipes.json)…"] = "بدّل الوصفات (recipes.json)…",
            ["Tarifleri yeniden yükle"] = "عاود حمّل الوصفات",
            ["Günlüğü aç"] = "حلّ السجل",
            ["Ayarlar klasörü"] = "دوسي الإعدادات",
            ["Çıkış"] = "خرج",
            ["Otomatik (Windows dili)"] = "أوتوماتيك (لغة ويندوز)",

            // --------------------------------------------------------- bildirimler
            ["Kumkat hazır"] = "كومكات واجد",
            ["Explorer'da Shift basılıyken bir dosyayı sürükle ya da dosyayı seçip {0}'ya bas."] =
                "ف Explorer شدّ Shift وجرّ شي فيشي، ولا ختار الفيشي وورك على {0}.",
            [" Eksik araçlar için tepsi menüsü › Araçlar."] = " للأدوات اللي ناقصين: المينو اللي حدا الساعة › الأدوات.",
            ["Kısayol alınamadı"] = "ما قدرتش ناخد الاختصار",
            ["{0} başka bir uygulama tarafından kullanılıyor."] = "{0} مستعمل من شي برنامج آخر.",
            ["Ayar değiştirilemedi"] = "ما تبدّلش الإعداد",
            ["winget başlatılamadı"] = "winget ما بغاش يخدم",
            ["Tarifler yüklendi"] = "الوصفات تحمّلو",
            ["{0} dönüşüm hazır."] = "{0} تبديل واجد.",
            ["recipes.json okunamadı"] = "ما قدرتش نقرا recipes.json",
            ["Dönüştürülecek dosyaları seç"] = "ختار الفيشيات اللي بغيتي تبدّل",
            ["Tüm dosyalar (*.*)|*.*"] = "كاع الفيشيات (*.*)|*.*",
            ["Seçili dosya yok"] = "ماكاين حتى فيشي مختار",
            ["Explorer'da dosyaları seçip {0}'ya bas (masaüstü için önce Ctrl+C)."] =
                "ختار الفيشيات ف Explorer وورك على {0} (للبيرو دير Ctrl+C قبل).",
            ["Desteklenmeyen dosya"] = "فيشي ما مدعومش",
            [".{0} için tanımlı dönüşüm yok. (Gelişmiş › Tarifleri düzenle)"] =
                "ماكاين حتى تبديل لـ .{0}. (خيارات أخرى › بدّل الوصفات)",
            [" (+{0} farklı tür atlandı)"] = " (+{0} من نوع آخر تخطّاو)",
            ["önizleme hazırlanıyor…"] = "كنوجد المعاينة…",
            ["Kırpma açılamadı"] = "ما تحلّش القصّ",
            [" ({0:0.#} sn)"] = " ({0:0.#} ث)",
            ["{0} dosya {1} oldu"] = "{0} فيشيات ولاو {1}",
            ["✓ Hazır"] = "✓ واجد",
            [" — göstermek için tıkla"] = " — كليكي باش تشوفو",
            ["{0} tamam, {1} hata"] = "{0} مزيانين، {1} فيهم مشكل",
            ["Dönüştürülemedi"] = "ما تبدّلش",
            ["Kumkat — {0} dosya sırada"] = "كومكات — {0} فيشيات كيتسناو",
            ["Kumkat — {0}"] = "كومكات — {0}",

            // ------------------------------------------------------ dairesel menü
            ["Bırak"] = "طلق",
            ["Dosyayı bir dilime bırak"] = "طلق الفيشي فوق شي طرف",
            ["Bu dosya türü desteklenmiyor"] = "هاد النوع ديال الفيشيات ما مدعومش",
            ["Kullanılamıyor"] = "ما خدّامش",
            ["{0} gerekli"] = "خاص {0}",
            ["{0} dosya"] = "{0} فيشيات",
            ["→ "] = "← ",
            ["araç"] = "أداة",
            ["Sayfa {0}"] = "صفحة {0}",

            // sayfalar
            ["Dönüştür"] = "بدّل",
            ["Kırp · Döndür"] = "قصّ · دوّر",
            ["Boyut · Temizle"] = "القياس · نقّي",
            ["Boyut · Hız"] = "القياس · السرعة",

            // dilim adları ve alt yazıları (recipes.json)
            ["Serbest"] = "حر",
            ["Küçük"] = "صغير",
            ["Sessiz"] = "بلا صوت",
            ["Meta"] = "البيانات",
            ["Kapak"] = "الغلاف",
            ["S/B"] = "ك/ب",
            ["%50"] = "50%",
            ["%25"] = "25%",
            ["Word"] = "وورد",
            ["Küçült"] = "صغّر",
            ["Temizle"] = "نقّي",
            ["Sıkıştır"] = "ضغط",
            ["Sesi al"] = "خرّج الصوت",
            ["Sesi sil"] = "حيّد الصوت",
            ["Sayfalar"] = "الصفحات",
            ["Tablo"] = "جدول",
            ["Düzenle"] = "رتّب",
            ["Kırp…"] = "قصّ…",
            ["Kare"] = "مربّع",
            ["Yatay"] = "عرضي",
            ["Dikey"] = "طولي",
            ["Klasik"] = "كلاسيكي",
            ["Sağa döndür"] = "دوّر ليمن",
            ["Sola döndür"] = "دوّر ليسر",
            ["Ayna"] = "مراية",
            ["Siyah-beyaz"] = "كحل و بيض",
            ["Uzun kenar"] = "الجنب الطويل",
            ["Boyut"] = "القياس",
            ["Hızlandır"] = "زرّب",
            ["İlk kare"] = "أول تصويرة",

            // Aseprite
            ["Animasyon · Büyüt"] = "أنيماسيون · كبّر",
            ["Animasyon"] = "أنيماسيون",
            ["Şerit"] = "شريط",
            ["Şerit ×4"] = "شريط ×4",
            ["Kareler"] = "التصاور",
            ["Tek tek"] = "وحدة بوحدة",
            ["Piksel"] = "بيكسل",
            ["{0} (kareler)"] = "{0} (التصاور)",

            // ------------------------------------------------------- kırpma penceresi
            ["Kumkat — Kırp: {0}"] = "كومكات — قصّ: {0}",
            ["Orijinal"] = "الأصلي",
            ["İptal"] = "لغي",
            ["Kırp"] = "قصّ",
            ["Kırp ({0} dosya)"] = "قصّ ({0} فيشيات)",
            ["Sürükleyerek alan çiz · köşelerden boyutlandır · içinden taşı · oklar: kaydır · Enter: kırp · Esc: iptal"] =
                "جرّ باش ترسم المساحة · كبّر من الزوايا · حرّك من الوسط · الأسهم: زحّف · Enter: قصّ · Esc: لغي",
            [" · aynı oransal alan tüm dosyalara uygulanır"] = " · نفس المساحة غادي تتطبّق على كاع الفيشيات",

            // ------------------------------------------------------------- hatalar
            ["Dosya bulunamadı."] = "الفيشي ما تلقاش.",
            ["{0} bulunamadı."] = "{0} ماكاينش.",
            ["{0} dönüştüremedi (kod {1})."] = "{0} ما قدرش يبدّل (الكود {1}).",
            ["Bu görsel biçimini okumak için FFmpeg gerekli."] = "باش نقرا هاد النوع ديال التصاور خاص FFmpeg.",
            ["PDF zaten sıkıştırılmış; daha küçük bir sürüm üretilemedi."] = "الـ PDF ديجا مضغوط؛ ما قدرتش نصغّرو كثر.",
            ["PDF'te sayfa yok."] = "الـ PDF ما فيه حتى صفحة.",
            ["Geçerli bir PSD dosyası değil."] = "هادا ماشي فيشي PSD صحيح.",
            ["PSB (büyük belge) biçimi desteklenmiyor."] = "PSB (الوثيقة الكبيرة) ما مدعومش.",
            ["Bilinmeyen PSD sürümü."] = "نسخة PSD ما معروفاش.",
            ["Bu PSD renk modu desteklenmiyor ({0}). RGB, CMYK veya Gri olmalı."] =
                "هاد الوضع ديال الألوان ف PSD ما مدعومش ({0}). خاصو يكون RGB، CMYK ولا رمادي.",
            ["{0} bit PSD desteklenmiyor (8 veya 16 olmalı)."] = "PSD ب {0} بيت ما مدعومش (خاصو 8 ولا 16).",
            ["PSD kanal sayısı eksik."] = "الكانالات ديال PSD ناقصين.",
            ["PSD boyutu geçersiz."] = "القياس ديال PSD غالط.",
            ["Bu PSD sıkıştırma türü desteklenmiyor (ZIP)."] = "هاد النوع ديال الضغط ف PSD ما مدعومش (ZIP).",
            ["Geçerli bir Aseprite dosyası değil."] = "هادا ماشي فيشي Aseprite صحيح.",
            ["Aseprite dosyasında kare yok."] = "فيشي Aseprite ما فيه حتى تصويرة.",
            ["JSON bir dizi (veya dizi içeren bir nesne) olmalı."] = "الـ JSON خاصو يكون ليستة (ولا أوبجي فيه ليستة).",
        };

        /// <summary>Testler için: çevirisi tanımlı anahtarlar.</summary>
        internal static IEnumerable<string> Keys => Ar.Keys;
    }
}
