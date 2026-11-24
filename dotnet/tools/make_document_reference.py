"""
Runs the Python DocumentProcessor over every file in the C# test project's
TestData/documents folder and saves the results as document_reference.json.
The C# DocumentParityTests compare against that file.

Run from the repo root:  .venv\Scripts\python dotnet\tools\make_document_reference.py
"""
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))  # lets "from app.services ..." work when run as a script

from app.services import document_processor as dp

# On Windows, Python would convert .doc files with Microsoft Word, but C# uses LibreOffice.
# Force Python to use LibreOffice too, so we compare the processors, not two different converters.
dp._HAS_WIN32 = False

TEST_DATA = REPO / "dotnet" / "tests" / "CFC.ChatAI.DocumentProcessor.Tests" / "TestData"
FIXTURES = TEST_DATA / "documents"


def norm(s):
    """Collapse all whitespace to single spaces, the same as Normalize() in the C# code."""
    return re.sub(r"\s+", " ", s or "").strip()


docs = []
for path in sorted(FIXTURES.iterdir()):
    if not path.is_file():
        continue
    print(f"Processing {path.name} ...")
    r = dp.process_document(path)

    if not r.get("success"):
        print(f"  Python failed: {r.get('error')}")
        docs.append({"file": path.name, "success": False, "error": r.get("error")})
        continue

    # Chunks point to their section by a random GUID. Swap that for the section title,
    # which is something C# and Python can both produce.
    by_id = {s["section_id"]: s for s in r["sections"]}
    chunks = []
    for c in r["chunks"]:
        section = by_id.get(c["section_id"])
        chunks.append({
            "section_title": section["title"] if section else "Untitled",
            "text": c["text"],
            "image_count": len(c["image_paths"]),
        })

    # C# drops sections that contain no text, so only keep sections that produced text.
    with_text = {c["section_id"] for c in r["chunks"] if c["text"]}
    sections = [
        {"title": s["title"], "level": s["level"]}
        for s in r["sections"]
        if s["section_id"] in with_text
    ]

    docs.append({
        "file": path.name,
        "success": True,
        "doc_id": r["doc_id"],
        "sections": sections,
        "chunks": chunks,
        "full_text": norm(" ".join(c["text"] for c in r["chunks"])),
        "image_count": len(r["images"]),
    })
    print(f"  {len(sections)} sections, {len(chunks)} chunks, {len(r['images'])} images")

out = TEST_DATA / "document_reference.json"
out.write_text(json.dumps({
    "python_pdf_path": "structured" if dp._HAS_PYMUPDF else "flat",
    "ocr_available": dp._HAS_PDF_OCR,
    "documents": docs,
}, indent=1, ensure_ascii=False), encoding="utf-8")

print(f"\nWrote {len(docs)} documents to {out}")