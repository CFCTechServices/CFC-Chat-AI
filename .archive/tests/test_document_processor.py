from app.services.document_processor import DocumentProcessor


def test_table_to_text_renders_rows_with_separators():
    rows = [["Header 1", "Header 2"], ["Value A", "Value B"]]
    processor = DocumentProcessor()

    rendered = processor._table_to_text(rows)

    expected = "Header 1 | Header 2\nValue A | Value B"
    assert rendered == expected


def test_build_chunks_aggregates_text_and_images():
    sections = [
        {
            "section_id": "sec-1",
            "blocks": [
                {"type": "text", "text": "First block of text."},
                {"type": "image", "path": "images/img-1.png"},
                {"type": "text", "text": "Second block adds more detail."},
            ],
        }
    ]

    processor = DocumentProcessor()
    chunks = processor._build_chunks(sections, max_chars=200)

    assert len(chunks) == 1
    chunk = chunks[0]
    assert chunk["section_id"] == "sec-1"
    assert "First block of text." in chunk["text"]
    assert "Second block adds more detail." in chunk["text"]
    assert chunk["image_paths"] == ["images/img-1.png"]
