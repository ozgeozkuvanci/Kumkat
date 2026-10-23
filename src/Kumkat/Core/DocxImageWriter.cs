using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Kumkat.Core
{
    /// <summary>
    /// Bir JPEG görüntüyü A4 sayfaya (2 cm kenar boşluğu, oran korunarak) yerleştirilmiş en küçük geçerli .docx olarak yazar.
    /// Word gerekmez; tests/ altındaki Python karşılığıyla aynı XML'i üretir.
    /// </summary>
    internal static class DocxImageWriter
    {
        private const int PageW = 11906, PageH = 16838, Margin = 1134; // twip
        private const long EmuPerTwip = 635;

        public static void Write(string path, byte[] jpeg, int width, int height, string title)
        {
            double cw = PageW - 2 * Margin, ch = PageH - 2 * Margin;
            double scale = Math.Min(cw / width, ch / height);
            long cx = (long)(width * scale * EmuPerTwip);
            long cy = (long)(height * scale * EmuPerTwip);
            string name = SecurityElement.Escape(title) ?? "resim";
            string ext = cx.ToString(CultureInfo.InvariantCulture), eyt = cy.ToString(CultureInfo.InvariantCulture);

            const string contentTypes =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>";

            const string rootRels =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>";

            const string docRels =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rIdImg1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.jpeg\"/>" +
                "</Relationships>";

            string document =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                "<w:body><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>" +
                $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{ext}\" cy=\"{eyt}\"/><wp:docPr id=\"1\" name=\"{name}\"/>" +
                "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
                "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
                $"<pic:nvPicPr><pic:cNvPr id=\"1\" name=\"{name}\"/><pic:cNvPicPr/></pic:nvPicPr>" +
                "<pic:blipFill><a:blip r:embed=\"rIdImg1\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
                $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{ext}\" cy=\"{eyt}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
                "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>" +
                $"<w:sectPr><w:pgSz w:w=\"{PageW}\" w:h=\"{PageH}\"/><w:pgMar w:top=\"{Margin}\" w:right=\"{Margin}\" w:bottom=\"{Margin}\" w:left=\"{Margin}\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr>" +
                "</w:body></w:document>";

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            AddText(zip, "[Content_Types].xml", contentTypes);
            AddText(zip, "_rels/.rels", rootRels);
            AddText(zip, "word/_rels/document.xml.rels", docRels);
            AddText(zip, "word/document.xml", document);
            var img = zip.CreateEntry("word/media/image1.jpeg", CompressionLevel.NoCompression);
            using (var s = img.Open()) s.Write(jpeg, 0, jpeg.Length);
        }

        private static void AddText(ZipArchive zip, string entryName, string text)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var s = entry.Open();
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }
    }
}
