import pytest

from app.utils import text_processing


def test_split_into_chunks_returns_single_chunk_for_short_text():
    text = "Short sentence."
    chunks = text_processing.split_into_chunks(text, chunk_size=100, overlap=10)
    assert chunks == [text]


def test_split_into_chunks_respects_chunk_size_and_overlap():
    text = (
        "Sentence one ends here. Sentence two follows closely. "
        "Finally, sentence three closes the thought."
    )
    chunks = text_processing.split_into_chunks(text, chunk_size=60, overlap=10)

    assert len(chunks) == 2
    assert chunks[0].startswith("Sentence one")
    assert chunks[0].endswith(".")
    assert chunks[1].startswith("Sentence two") or chunks[1].startswith("Sentence three")
    # Ensure overlap kept enough context between consecutive chunks.
    assert "Sentence two" in chunks[0] or "Sentence two" in chunks[1]


def test_clean_text_normalizes_whitespace_and_symbols():
    dirty = " Line  one \n\n Line   two\t!!!@@ "
    cleaned = text_processing.clean_text(dirty)
    assert cleaned == "Line one Line two!!!"


@pytest.mark.parametrize(
    "text,expected",
    [
        ("Hello world", {"word_count": 2, "char_count": 11, "has_tables": False, "has_code": False, "has_urls": False}),
        ("Row | Column", {"has_tables": True}),
        ("Visit http://example.com", {"has_urls": True}),
        ("def function(): pass", {"has_code": True}),
    ],
)
def test_extract_metadata_from_text_flags_expected_properties(text, expected):
    metadata = text_processing.extract_metadata_from_text(text)
    for key, value in expected.items():
        assert metadata[key] == value
