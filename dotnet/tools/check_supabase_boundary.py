#!/usr/bin/env python3
"""Enforce the dependency boundary around CFC.ChatAI.Supabase."""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "dotnet/src/CFC.ChatAI.Supabase/CFC.ChatAI.Supabase.csproj"
SOURCE = PROJECT.parent



def main() -> int:
    errors: list[str] = []

    try:
        project = ET.parse(PROJECT).getroot()
    except (OSError, ET.ParseError) as exc:
        print(f"Could not read Supabase project file: {exc}", file=sys.stderr)
        return 2

    for element in project.iter():
        if element.tag.rsplit("}", 1)[-1] == "ProjectReference":
            reference = element.attrib.get("Include", "<missing Include>")
            errors.append(f"{PROJECT.relative_to(ROOT)} has a project reference: {reference}")

    using_pattern = re.compile(
        r"^\s*(?:global\s+)?using\s+(?:static\s+)?(CFC\.ChatAI\.[A-Za-z0-9_.]+)",
        re.MULTILINE,
    )
    namespace_pattern = re.compile(
        r"^\s*namespace\s+(CFC\.ChatAI\.[A-Za-z0-9_.]+)", re.MULTILINE
    )
    for path in sorted(SOURCE.rglob("*.cs")):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8")
        relative = path.relative_to(ROOT)
        for namespace in using_pattern.findall(text) + namespace_pattern.findall(text):
            if namespace != "CFC.ChatAI.Supabase" and not namespace.startswith(
                "CFC.ChatAI.Supabase."
            ):
                errors.append(f"{relative} references non-Supabase namespace {namespace}")

    if errors:
        print("Supabase architecture boundary check failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("Supabase architecture boundary check passed (no project references or non-Supabase CFC.ChatAI namespaces).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
