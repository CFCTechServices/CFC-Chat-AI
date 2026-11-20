"""Processor-only smoke checks; these are not upload or provider parity tests.

Run from the repository root with python-docx installed:
    python -m unittest discover -s dotnet/tests/DocumentUploadParity -p 'test_*.py'
"""
from io import BytesIO
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from zipfile import ZipFile

from docx import Document

from app.services.document_processor import DocumentProcessor

FIXTURES = Path(__file__).parent / "fixtures"


class PythonFixtureTests(unittest.TestCase):
    def test_baseline_email_is_processable(self):
        result = DocumentProcessor().process_document(FIXTURES / "baseline.eml")
        self.assertTrue(result["success"], result.get("error"))
        self.assertEqual("baseline", result["doc_id"])
        self.assertEqual(1, len(result["sections"]))
        self.assertEqual(1, len(result["chunks"]))
        self.assertIn("This is synthetic content", result["chunks"][0]["text"])

    def test_multi_section_docx(self):
        result = DocumentProcessor().process_document(FIXTURES / "multi-section.docx")
        self.assertTrue(result["success"], result.get("error"))
        self.assertEqual(["Overview", "Details"], [s["title"] for s in result["sections"]])
        self.assertEqual(2, len(result["chunks"]))
        self.assertEqual([s["section_id"] for s in result["sections"]],
                         [c["section_id"] for c in result["chunks"]])

    def test_embedded_image_docx(self):
        result = DocumentProcessor().process_document(FIXTURES / "embedded-image.docx")
        self.assertTrue(result["success"], result.get("error"))
        self.assertEqual(1, len(result["images"]))
        self.assertEqual(1, len(result["chunks"]))
        self.assertEqual(1, len(result["chunks"][0]["image_paths"]))
        self.assertIn(result["images"][0]["image_id"], result["chunks"][0]["image_paths"][0])

    def test_python_keeps_ordered_references_on_the_chunks_that_encounter_them(self):
        with ZipFile(FIXTURES / "embedded-image.docx") as archive:
            png = archive.read("word/media/image1.png")
        doc = Document()
        doc.add_heading("Illustration", level=1)
        doc.add_picture(BytesIO(png))  # leading image-only paragraph
        first = doc.add_paragraph("before " * 90)
        first.add_run().add_picture(BytesIO(png))  # mixed text/image paragraph
        doc.add_paragraph("after " * 115)
        doc.add_picture(BytesIO(png))
        with TemporaryDirectory() as temp:
            path = Path(temp) / "split.docx"
            doc.save(path)
            result = DocumentProcessor().process_document(path)
        self.assertTrue(result["success"], result.get("error"))
        self.assertEqual(1, len(result["images"]))
        first, second = result["chunks"]
        self.assertTrue(first["text"].startswith("before"))
        self.assertTrue(second["text"].startswith("after"))
        ref = "images/" + result["images"][0]["image_id"] + ".png"
        self.assertEqual([ref, ref], first["image_paths"])
        self.assertEqual([ref], second["image_paths"])

    def test_txt_is_unsupported(self):
        result = DocumentProcessor().process_document(FIXTURES / "unsupported.txt")
        self.assertFalse(result["success"])
        self.assertEqual("Unsupported file type: .txt", result["error"])


if __name__ == "__main__":
    unittest.main()
