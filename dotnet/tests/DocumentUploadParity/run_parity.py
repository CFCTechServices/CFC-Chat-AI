"""Run the offline upload investigation twice and regenerate its single report.

Prerequisites and exact commands are documented in the generated report. The
.NET runner refuses to run outside a Docker container with no network interface.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tempfile
from urllib.parse import unquote

from comparison import CaptureError, MISSING, compare, display, storage_object, validate

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
FIXTURES = HERE / "fixtures"
BUILD = HERE / "Runner" / "bin" / "Debug" / "net10.0"
REPORT = ROOT / "dotnet" / "docs" / "tests" / "document-upload-parity-report.md"
IMAGE = "mcr.microsoft.com/dotnet/aspnet@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586"


@dataclass(frozen=True)
class Case:
    key: str
    title: str
    description: str
    fixture: str
    filename: str
    failure: str = "none"


CASES = [
    Case("baseline", "Baseline EML", "One small email exercises the original, section, chunk and vector paths.", "baseline.eml", "baseline.eml"),
    Case("multi", "Multi-section DOCX", "Two headings exercise section boundaries, chunk order and references.", "multi-section.docx", "multi-section.docx"),
    Case("image", "Embedded image DOCX", "A one-pixel image exercises image artifacts and chunk image references.", "embedded-image.docx", "embedded-image.docx"),
    Case("txt", "TXT upload", "The same plain text reaches C# processing but Python's processor does not support TXT.", "unsupported.txt", "unsupported.txt"),
    Case("filename", "Filename normalization", "A valid email is submitted with spaces, mixed case and a non-ASCII filename.", "baseline.eml", "Café Guide V2.EML"),
    Case("fail-original", "Injected original storage failure", "The original upload attempt fails; continuation and compensating attempts remain visible.", "baseline.eml", "baseline.eml", "storage-original"),
    Case("fail-chunks", "Injected chunk upsert failure", "The chunk upsert attempt fails; later operations and compensation are recorded.", "baseline.eml", "baseline.eml", "chunks-upsert"),
    Case("fail-vectors", "Injected vector upsert failure", "The vector boundary fails after chunk upsert; later operations and compensation are recorded.", "baseline.eml", "baseline.eml", "vectors-upsert"),
]


def child_environment():
    keep = {k: os.environ[k] for k in ("SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP") if k in os.environ}
    keep.update({"PYTHONPATH": str(ROOT), "PYTHONHASHSEED": "0", "PYTHONNOUSERSITE": "1"})
    return keep


def execute(command, output, env=None):
    # Never reuse a prior run's capture if this subprocess fails before writing.
    output.unlink(missing_ok=True)
    try:
        completed = subprocess.run(command, cwd=ROOT, env=env, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace", timeout=120)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return {"harness_error": type(exc).__name__, "detail": str(exc)}
    try:
        result = json.loads(output.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        # Logs are not provider evidence and may contain platform paths. Do not
        # dump inherited environment or arbitrary subprocess logs into the report.
        return {"harness_error": "RunnerOutputMissing", "detail": f"Runner exited {completed.returncode} without valid capture JSON."}
    if completed.returncode and "harness_error" not in result:
        result["harness_error"] = "RunnerExit"
        result["detail"] = f"Runner exited {completed.returncode}; see capture invariants."
    return result


def run_case(case, tmp):
    py_out, cs_out = tmp / (case.key + "-python.json"), tmp / (case.key + "-csharp.json")
    py = execute([sys.executable, str(HERE / "python_runner.py"), str(FIXTURES / case.fixture),
                  case.filename, case.failure, str(py_out)], py_out, child_environment())
    # No repo or host .env is mounted. The only writable bind mount is the
    # per-run temporary capture directory. --pull=never keeps execution offline.
    docker = ["docker", "run", "--rm", "--pull=never", "--network", "none", "--read-only",
              "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--pids-limit", "128",
              "--tmpfs", "/tmp:rw,nosuid,size=64m", "--workdir", "/tmp", "-e", "UPLOAD_PARITY_ISOLATED=1",
              "--mount", f"type=bind,source={BUILD},target=/app,readonly",
              "--mount", f"type=bind,source={FIXTURES},target=/fixtures,readonly",
              "--mount", f"type=bind,source={tmp},target=/out", IMAGE,
              "dotnet", "/app/CFC.ChatAI.UploadParity.Runner.dll", "/fixtures/" + case.fixture,
              case.filename, case.failure, "/out/" + cs_out.name]
    cs = execute(docker, cs_out)
    return py, cs


def missing_expected(case, py, cs):
    # These assertions are about the endpoint and captured attempts, not remote side effects.
    # Failure cases are validated by their actual hook.
    if case.key not in {"baseline", "multi", "image", "filename"}:
        return []
    expected = {
        "Python": {"storage-original", "storage-section", "chunks-upsert", "index-list", "index-describe", "vectors-upsert"},
        "C#": {"storage-original", "chunks-upsert", "vectors-upsert"},
    }
    if case.key == "image":
        expected["Python"].add("storage-image")
    errors = []
    for name, cap in (("Python", py), ("C#", cs)):
        found = {op.get("kind") for op in cap.get("operations", [])}
        missing = expected[name] - found
        if missing:
            errors.append(name + " missing expected capture: " + ", ".join(sorted(missing)))
        if cap.get("status") != 200:
            errors.append(name + " baseline fixture did not complete successfully")
    response = py.get("response")
    ingestion = response.get("ingestion") if isinstance(response, dict) else None
    if not isinstance(ingestion, dict) or ingestion.get("success") is not True:
        errors.append("Python ingestion did not report success")
    if case.key == "filename":
        if not isinstance(ingestion, dict) or ingestion.get("doc_id") != "café-guide-v2":
            errors.append("Python did not report the expected Unicode ID")
        response = cs.get("response")
        if not isinstance(response, dict) or response.get("document_id") != "café-guide-v2":
            errors.append("C# did not report the expected Unicode ID")
        obj = original(cs)
        path = obj.get("path", "") if isinstance(obj, dict) else ""
        if unquote(path) != "/storage/v1/object/parity-bucket/docs/!u-Y2Fmw6ktZ3VpZGUtdjI/original/document.eml":
            errors.append("C# did not attempt the expected ASCII-safe original object key")
    return errors


def evaluate(case, py, cs):
    errors = []
    for name, capture in (("Python", py), ("C#", cs)):
        try:
            validate(capture)
            if capture["failure_requested"] != case.failure:
                raise CaptureError("Runner did not use the case's requested failure target")
        except (CaptureError, TypeError, KeyError) as exc:
            errors.append(f"{name}: {exc}")
    if errors:
        return {"outcome": "ERROR", "errors": errors, "comparison": None}
    errors.extend(missing_expected(case, py, cs))
    if errors:
        return {"outcome": "ERROR", "errors": errors, "comparison": None}
    try:
        comparison = compare(py, cs)
    except (CaptureError, TypeError, KeyError, ValueError) as exc:
        return {"outcome": "ERROR", "errors": [f"Comparison: {exc}"], "comparison": None}
    # The C# fallback cannot observe namespace, so an otherwise identical case
    # cannot honestly be reported as a complete MATCH.
    if not comparison.differences:
        return {"outcome": "ERROR", "errors": ["No differences found, but C# namespace is unobservable at the fallback boundary."], "comparison": comparison}
    return {"outcome": "DIFFERENT", "errors": [], "comparison": comparison}


def md(value):
    return value.replace("|", "\\|").replace("\n", "<br>")


def code(value):
    return "`" + md(display(value)).replace("`", "&#96;") + "`"


def original(capture):
    for op in capture.get("operations", []):
        if op.get("kind") == "storage-original":
            try:
                return storage_object(op)
            except CaptureError:
                return {"capture_error": "Original storage body was malformed"}
    return MISSING


def operation_sequence(capture):
    if not capture.get("operations"):
        return "No provider operation captured."
    return " → ".join(op.get("kind", "unknown") + (" [injected failure]" if op.get("result") == "injected-failure" else "")
                      for op in capture["operations"])


def short_summary(py, cs, comparison):
    response = py.get("response")
    ingestion = response.get("ingestion") if isinstance(response, dict) else None
    ingestion_status = ingestion.get("success", MISSING) if isinstance(ingestion, dict) else MISSING
    def count(cap, kind):
        return sum(op.get("kind") == kind for op in cap.get("operations", []))
    summary = (f"Python HTTP {py.get('status', '?')} / ingestion success {display(ingestion_status)}; "
               f"C# HTTP {cs.get('status', '?')}. Section/image storage attempts: "
               f"Python {count(py, 'storage-section')}/{count(py, 'storage-image')}, "
               f"C# {count(cs, 'storage-section')}/{count(cs, 'storage-image')}.")
    if comparison and "chunk_rows: MATCH" in comparison.notes and "vectors: MATCH" in comparison.notes:
        summary += " Chunk rows and test vectors match after ID validation and aliasing."
    return summary


def safe_version(package):
    try:
        return importlib.metadata.version(package)
    except importlib.metadata.PackageNotFoundError:
        return "not installed"


def revision():
    try:
        return subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "unavailable"


def render(results, stability):
    lines = [
        "# Document upload request parity report", "",
        "Synthetic, in-process upload investigation. `DIFFERENT` is an observation, not a failing test. "
        "A canned response records an **attempt**, not a remote side effect. No real provider writes occur.", "",
        "| Case | Outcome | Operations Python / C# | Differences |",
        "| --- | --- | ---: | ---: |",
    ]
    for case, py, cs, result in results:
        count = len(result["comparison"].differences) if result["comparison"] else "unknown"
        ops = f"{len(py['operations']) if 'operations' in py else '?'} / {len(cs['operations']) if 'operations' in cs else '?'}"
        lines.append(f"| {case.title} | {result['outcome']} | {ops} | {count} |")
    lines.extend(["| Optional real model | SKIPPED | 0 / 0 | not compared |", "",
                  "## Legend and capture limits", "",
                  "- Python `POST /api/files/upload` and C# `POST /api/ingest/dev` use real endpoint, processor and ingestion/handler paths. "
                  "The latter is Development-only, opt-in, loopback-only and skips the authenticated route's duplicate check.",
                  "- Supabase on both sides and Python Pinecone are **transport-level**: real adapters/SDKs serialize request bodies before the test transport captures them. "
                  "The handler observes HTTP requests before final socket framing; no actual HTTP transmission is claimed. "
                  "Python Storage uses multipart; its parts below are parsed from the serialized bytes. Binary bytes are represented by size and SHA-256, not reproduced.",
                  "- C# Pinecone is **application-level**, not SDK HTTP JSON: it records `VectorRecord`s at the internal vector-store seam. "
                  "C# SDK serialization, HTTP headers, SDK batching and remote effects are not proven. The seam does not expose the namespace; "
                  "`parity-namespace` is configured on both sides but only Python's serialized value is observable. "
                  "A missing C# Pinecone HTTP capture means the SDK was bypassed, not that production would send no request.",
                  "- SDK reconnaissance: Python `VectorStore` lists indexes and may create one before resolving `Index(name)`; SDK 10.0.0 "
                  "uses HTTP for discovery and upserts. List and describe are captured and answered with canned responses pointing to a synthetic host; "
                  "unexpected creation/discovery is blocked. C# SDK `Pinecone.Client` 4.0.2 exposes `ClientOptions.HttpClient` for control-plane HTTP "
                  "and `GrpcOptions` for data-plane gRPC, but the production wrapper constructs its own client without injected options. "
                  "A safe serialized gRPC/discovery interception was not established, so the existing internal vector seam is used instead.",
                  "- Failure injection returns canned HTTP 503 for Supabase and non-retryable HTTP 400 for Python Pinecone, "
                  "so an SDK retry cannot turn the injected vector failure into a later success. The C# vector seam throws a test exception. "
                  "Each requested hook is verified against the recorded attempted operation; compensation is also only an attempt.",
                  "- The four-dimensional vectors are deterministic **test values**, not model embeddings: UTF-8 byte length, byte sum modulo 997, "
                  "position-weighted byte sum modulo 997, and ASCII-space count. Inputs and all values appear in the captures; equality is exact.",
                  "- Before per-side section/chunk aliases are assigned, section-to-row and row-to-vector references are validated where observable. "
                  "C# emits no section artifacts, so its section IDs are first observed in rows and empty sections are unobservable. Different chunk boundaries are displayed as unmatched ordered lists. "
                  "Document text, paths, filenames, field presence, types, request order and batches are not normalized. "
                  "Multipart boundary strings are normalized only in comparisons; original IDs and boundaries remain in details. "
                  "Object key order is ignored; array order is retained. `<MISSING>` and `null` are distinct; "
                  "`<NOT OBSERVABLE>` denotes the C# SDK capture limitation, not an absent request. Difference counts include these coverage gaps.",
                  "- Each Python process removes inherited credentials, disables dotenv before imports, blocks socket/DNS calls and intercepts unexpected HTTP. "
                  "On Windows, its event loop's private `socketpair` alone is allowed to use loopback. "
                  "The C# runner runs in a Docker container with `--network none`, only loopback, no repository mount, and a read-only root. "
                  "Both deliberately test an unhandled request and outbound denial. Unknown traffic fails the harness.", ""])
    for case, py, cs, result in results:
        note = (" Both endpoints must accept the file and report `café-guide-v2`. "
                "C# intentionally encodes the new original's storage key to meet Supabase's ASCII key restriction."
                if case.key == "filename" else "")
        lines.extend([f"## {case.title}", "", case.description + f" Fixture: `{case.fixture}` as `{case.filename}`. "
                      "Capture: Supabase transport; Pinecone Python transport / C# application boundary." + note, "",
                      f"**Outcome:** {result['outcome']}. " + (" ".join(result["errors"]) if result["errors"] else short_summary(py, cs, result["comparison"])), "",
                      "| Observation | Python | C# |", "| --- | --- | --- |",
                      f"| Endpoint status | {code(py.get('status', MISSING))} | {code(cs.get('status', MISSING))} |",
                      f"| Ordered attempted operations | {md(operation_sequence(py))} | {md(operation_sequence(cs))} |",
                      f"| Original object and options | {code(original(py))} | {code(original(cs))} |",
                      f"| Failure hook intercepted | {code(py.get('failure_intercepted', MISSING))} | {code(cs.get('failure_intercepted', MISSING))} |", ""])
        comparison = result["comparison"]
        if comparison:
            lines.append("**Checks:** " + "; ".join(comparison.notes) + ".")
            lines.extend(["", "| Difference (normalized only as described above) | Python | C# |", "| --- | --- | --- |"])
            for path, left, right in comparison.differences:
                lines.append(f"| {md(path)} | {code(left)} | {code(right)} |")
            if not comparison.differences:
                lines.append("| None at captured level | — | — |")
        else:
            lines.append("Comparison unavailable; missing evidence is not a match.")
        for name, capture in (("Python", py), ("C#", cs)):
            lines.extend(["", f"<details><summary>{name}: complete sanitized capture</summary>", "", "```json",
                          json.dumps(capture, indent=2, ensure_ascii=False, sort_keys=True), "```", "</details>"])
        lines.append("")
    lines.extend(["## Optional real-model case", "", "**SKIPPED:** matching Python and C# model versions/configuration and numeric tolerance were not established. "
                  "The deterministic test vectors above are not substituted as evidence of real-model parity.", "",
                  "## Reproduction and verification", "",
                  f"- Revision: `{revision()}` plus the working-tree harness/fixture changes; Python `{platform.python_version()}`, "
                  f"`fastapi` {safe_version('fastapi')}, `supabase` {safe_version('supabase')}, `storage3` {safe_version('storage3')}, "
                  f"`postgrest` {safe_version('postgrest')}, `httpx` {safe_version('httpx')}, `pinecone` {safe_version('pinecone')}, "
                  f"`python-docx` {safe_version('python-docx')}; Python host `{platform.system()}`; .NET SDK 10.0.401 and Linux ASP.NET/.NET runtime 10.0.15 in `{IMAGE}`.",
                  "- All fixtures are synthetic. DOCX fixtures can be regenerated by `generate_docx_fixtures.py` with python-docx 1.2.0. "
                  "Both hosts receive the same bytes and `file` field for each case.",
                  "- The report was regenerated from scratch. " + ("Two offline runs had identical normalized comparisons; original random IDs and multipart boundaries remain visible below." if stability else "Normalized comparisons were not stable; this is a harness error."),
                  "- Prerequisites: Docker with the pinned image already present, .NET packages in the local NuGet cache, and the pinned Python packages in the local `uv` cache. "
                  "Image/package acquisition is a separate setup step; no provider credentials are required. The commands below are the recorded Windows Git Bash/MSYS offline run. "
                  "The runner fixes `PYTHONHASHSEED=0` in each Python child and performs two complete suites per invocation.", "",
                  "```bash",
                  "# One-time image acquisition was: docker pull mcr.microsoft.com/dotnet/aspnet:10.0",
                  "# Offline setup from already-populated caches:",
                  "uv venv --offline --python \"$HOME/AppData/Roaming/uv/python/cpython-3.14-windows-x86_64-none/python.exe\" \"$(cygpath -aw /tmp/cfc-upload-parity-verified)\"",
                  "uv pip install --offline --python \"$(cygpath -aw /tmp/cfc-upload-parity-verified/Scripts/python.exe)\" -r dotnet/tests/DocumentUploadParity/requirements-parity.txt",
                  "empty=\"$(mktemp -d)\"",
                  "dotnet restore dotnet/tests/DocumentUploadParity/Runner/CFC.ChatAI.UploadParity.Runner.csproj --source \"$(cygpath -w \"$empty\")\"",
                  "rm -rf \"$empty\"",
                  "dotnet build dotnet/tests/DocumentUploadParity/Runner/CFC.ChatAI.UploadParity.Runner.csproj --no-restore",
                  "/tmp/cfc-upload-parity-verified/Scripts/python.exe -m unittest discover -s dotnet/tests/DocumentUploadParity -p 'test_*.py'",
                  "dotnet test dotnet/tests/CFC.ChatAI.DocumentProcessor.Tests/CFC.ChatAI.DocumentProcessor.Tests.csproj --no-restore --filter FullyQualifiedName~UploadParityFixtureTests",
                  "PYTHONHASHSEED=0 /tmp/cfc-upload-parity-verified/Scripts/python.exe dotnet/tests/DocumentUploadParity/run_parity.py",
                  "rm -rf /tmp/cfc-upload-parity-verified",
                  "```", "", "Fixture hashes:", "",
                  "| Fixture | SHA-256 |", "| --- | --- |"])
    for path in sorted(FIXTURES.glob("*")):
        if path.is_file() and not path.name.startswith("."):
            lines.append(f"| `{path.name}` | `{hashlib.sha256(path.read_bytes()).hexdigest()}` |")
    lines.append("")
    return "\n".join(lines)


def validate_report(output, results):
    """Fail if a case or any complete capture was omitted or truncated."""
    if [case.key for case, _, _, _ in results] != [case.key for case in CASES]:
        raise ValueError("Report cases are missing or out of order")
    if output.count("<details><summary>") != 2 * len(results) or output.count("</details>") != 2 * len(results):
        raise ValueError("Report capture details are malformed")
    if output.count("```") != 4 * len(results) + 2:
        raise ValueError("Report code fences are malformed")
    previous = -1
    for case, py, cs, result in results:
        heading = "## " + case.title + "\n"
        position = output.find(heading)
        if position <= previous or output.count(heading) != 1:
            raise ValueError("Report case heading is missing, duplicated or out of order")
        previous = position
        if f"| {case.title} | {result['outcome']} |" not in output:
            raise ValueError("Report summary row is missing")
        next_heading = output.find("\n## ", position + len(heading))
        section = output[position:next_heading if next_heading >= 0 else None]
        for cap in (py, cs):
            if json.dumps(cap, indent=2, ensure_ascii=False, sort_keys=True) not in section:
                raise ValueError("Report capture details are incomplete or truncated")
    if "## Optional real-model case\n" not in output or "| Optional real model | SKIPPED |" not in output:
        raise ValueError("Real-model skip is missing")


def main():
    if not (BUILD / "CFC.ChatAI.UploadParity.Runner.dll").exists():
        raise SystemExit("Build the C# parity runner first (see report prerequisites).")
    runs = []
    with tempfile.TemporaryDirectory(prefix="upload-parity-captures-") as tmp_name:
        tmp = Path(tmp_name)
        for iteration in range(2):
            results = []
            for case in CASES:
                print(f"run {iteration + 1}/2: {case.key}", flush=True)
                py, cs = run_case(case, tmp)
                results.append((case, py, cs, evaluate(case, py, cs)))
            runs.append(results)
        signatures = [[(c.key, result["outcome"], result["errors"],
                        result["comparison"].normalized if result["comparison"] else None)
                       for c, _, _, result in run] for run in runs]
        stable = signatures[0] == signatures[1]
        output = render(runs[-1], stable)
        malformed = None
        try:
            validate_report(output, runs[-1])
        except ValueError as exc:
            malformed = str(exc)
            output = "# Document upload request parity report\n\n**ERROR:** Malformed generated report: " + malformed + "\n\n" + output
        REPORT.parent.mkdir(parents=True, exist_ok=True)
        # Atomic replace prevents stale appended results or a half-written report.
        temp_report = REPORT.with_suffix(".md.tmp")
        try:
            temp_report.write_text(output, encoding="utf-8", newline="\n")
            temp_report.replace(REPORT)
        finally:
            temp_report.unlink(missing_ok=True)
        if malformed or not stable or any(result["outcome"] == "ERROR" for _, _, _, result in runs[-1]):
            raise SystemExit("Parity harness failed; the report contains the errors.")
    print(f"Report written: {REPORT}; normalized comparisons stable across both runs.")


if __name__ == "__main__":
    main()
