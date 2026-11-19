"""Processor-only smoke checks; these are not upload or provider parity tests.

Run from the repository root with python-docx installed:
    python -m unittest discover -s dotnet/tests/DocumentUploadParity -p 'test_*.py'
"""
from pathlib import Path
import unittest

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

    def test_txt_is_unsupported(self):
        result = DocumentProcessor().process_document(FIXTURES / "unsupported.txt")
        self.assertFalse(result["success"])
        self.assertEqual("Unsupported file type: .txt", result["error"])


if __name__ == "__main__":
    unittest.main()
