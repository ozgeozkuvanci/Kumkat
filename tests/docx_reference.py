# DocxImageWriter.cs'nin birebir karşılığı (aynı XML metinleri)
import zipfile, io, sys
from PIL import Image
src=sys.argv[1]; out=sys.argv[2]
im=Image.open(src).convert('RGB'); w,h=im.size; b=io.BytesIO(); im.save(b,'JPEG',quality=92); jpeg=b.getvalue()
PAGE_W,PAGE_H,MARGIN=11906,16838,1134          # twip (A4, 2 cm kenar boşluğu)
cw,ch=PAGE_W-2*MARGIN,PAGE_H-2*MARGIN
s=min(cw/w,ch/h); cx=int(w*s*635); cy=int(h*s*635)  # 1 twip = 635 EMU
name="foto"
CT='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="jpeg" ContentType="image/jpeg"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'
RELS='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
DRELS='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rIdImg1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.jpeg"/></Relationships>'
DOC=('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
 '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">'
 '<w:body><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:drawing>'
 f'<wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:docPr id="1" name="{name}"/>'
 '<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect="1"/></wp:cNvGraphicFramePr>'
 '<a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic>'
 f'<pic:nvPicPr><pic:cNvPr id="1" name="{name}"/><pic:cNvPicPr/></pic:nvPicPr>'
 '<pic:blipFill><a:blip r:embed="rIdImg1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
 f'<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>'
 '</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>'
 f'<w:sectPr><w:pgSz w:w="{PAGE_W}" w:h="{PAGE_H}"/><w:pgMar w:top="{MARGIN}" w:right="{MARGIN}" w:bottom="{MARGIN}" w:left="{MARGIN}" w:header="709" w:footer="709" w:gutter="0"/></w:sectPr>'
 '</w:body></w:document>')
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml',CT); z.writestr('_rels/.rels',RELS)
    z.writestr('word/_rels/document.xml.rels',DRELS); z.writestr('word/document.xml',DOC)
    z.writestr('word/media/image1.jpeg',jpeg)
print(out, w,h, '->', cx,cy,'EMU')
