"""Scan a legacy DOC/DOCX file for paragraphs that reference embedded images.

This helper is handy when diagnosing why a particular document failed to
surface image placeholders during ingestion. It inspects the raw `v:imagedata`
relationships that older DOC files still use.

Usage:
    python -m scripts.doc_debug.check_doc_image_refs --doc path/to/file.DOC
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Iterable

from docx import Document

# Ensure the repository root is on sys.path when executed directly.
REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.append(str(REPO_ROOT))


_NAMESPACES = {
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
    "v": "urn:schemas-microsoft-com:vml",
    "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
}


def _vml_image_refs(paragraph) -> Iterable[str]:
    """Yield the relationship ids for legacy VML images within a paragraph."""
    for imagedata in paragraph._p.xpath(".//v:imagedata", namespaces=_NAMESPACES):
        rel_id = imagedata.get(f"{{{_NAMESPACES['r']}}}id")
        if rel_id:
            yield rel_id


def inspect_document(doc_path: Path) -> None:
    """Print a summary of paragraphs that reference legacy VML images."""
    document = Document(doc_path)
    matches = 0

    for idx, paragraph in enumerate(document.paragraphs):
        refs = list(_vml_image_refs(paragraph))
        if refs:
            matches += 1
            print(f"Paragraph {idx} references {len(refs)} image(s):")
            for rel_id in refs:
                print(f"  - r:id = {rel_id}")

    print(f"Total paragraphs with legacy image references: {matches}")


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Inspect a DOC/DOCX file for legacy VML image references."
    )
    parser.add_argument(
        "--doc",
        type=Path,
        required=True,
        help="Path to the DOC or DOCX file to inspect.",
    )
    return parser


def main() -> None:
    parser = _build_parser()
    args = parser.parse_args()
    inspect_document(args.doc.expanduser())


if __name__ == "__main__":
    main()
