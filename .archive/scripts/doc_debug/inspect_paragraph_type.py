"""Inspect python-docx paragraph internals for a converted legacy document.

This script is mainly useful when we need a quick reminder of the underlying
type hierarchy for `Paragraph._p`, e.g., when deciding which lxml APIs are
available for XPath queries.

Usage:
    python -m scripts.doc_debug.inspect_paragraph_type --doc path/to/file.DOC
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

# Ensure project imports resolve when executed directly.
REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.append(str(REPO_ROOT))

from app.services.document_processor import DocumentProcessor
from docx import Document


def inspect_paragraph(doc_path: Path) -> None:
    """Convert the document and print details about the first paragraph."""
    processor = DocumentProcessor()
    converted_docx = processor._convert_doc_to_docx(doc_path)
    document = Document(converted_docx)
    paragraph = document.paragraphs[0]

    print(f"Paragraph type: {type(paragraph)}")
    print(f"Underlying _p type: {type(paragraph._p)}")
    print(f"Has 'element' attribute on _p: {hasattr(paragraph._p, 'element')}")
    print("Method resolution order for _p:")
    for cls in paragraph._p.__class__.__mro__:
        print(f"  - {cls}")


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Display python-docx paragraph internals for a DOC/DOCX file."
    )
    parser.add_argument(
        "--doc",
        type=Path,
        required=True,
        help="Path to the legacy document to inspect.",
    )
    return parser


def main() -> None:
    parser = _build_parser()
    args = parser.parse_args()
    inspect_paragraph(args.doc.expanduser())


if __name__ == "__main__":
    main()
