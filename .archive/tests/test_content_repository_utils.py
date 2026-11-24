from app.services.content_repository import _finalize_filename


def test_finalize_filename_uses_requested_name_when_valid():
    filename = _finalize_filename(" custom/path/name.json ", "fallback.json", required_suffix=".json")
    assert filename == "name.json"


def test_finalize_filename_applies_suffix_and_fallback():
    filename = _finalize_filename(None, "default", required_suffix=".png")
    assert filename == "default.png"


def test_finalize_filename_preserves_existing_suffix_case_insensitive():
    filename = _finalize_filename("IMAGE.PNG", "fallback", required_suffix=".png")
    assert filename == "IMAGE.PNG"
