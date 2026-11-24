"""Regenerate the checked-in synthetic DOCX fixtures with python-docx 1.2.0.

Normalizing ZIP metadata makes repeated generation byte-for-byte reproducible with
that version. The PNG is a synthetic, transparent 1x1 pixel.
"""
import base64
from io import BytesIO
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

from docx import Document
from docx.shared import Inches

HERE = Path(__file__).parent / "fixtures"
PIXEL = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/"
    "aXcAAAAASUVORK5CYII="
)


def save(document, name):
    raw = BytesIO()
    document.save(raw)
    raw.seek(0)
    with ZipFile(raw) as source, ZipFile(HERE / name, "w", compression=ZIP_DEFLATED) as target:
        for path in sorted(source.namelist()):
            info = ZipInfo(path, (1980, 1, 1, 0, 0, 0))
            info.compress_type = ZIP_DEFLATED
            info.external_attr = 0o600 << 16
            target.writestr(info, source.read(path))


def main():
    multi = Document()
    multi.add_heading("Overview", level=1)
    multi.add_paragraph("Synthetic first section.")
    multi.add_heading("Details", level=2)
    multi.add_paragraph("Synthetic second section.")
    save(multi, "multi-section.docx")

    image = Document()
    image.add_heading("Illustration", level=1)
    image.add_paragraph("Synthetic text before the image.")
    image.add_picture(BytesIO(PIXEL), width=Inches(1))
    image.add_paragraph("Synthetic text after the image.")
    save(image, "embedded-image.docx")


if __name__ == "__main__":
    main()
