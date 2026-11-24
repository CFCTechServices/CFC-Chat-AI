"""
CLI utility used by the video ingestion flow to transcribe a local file with
Whisper, emit txt/srt/vtt/json transcripts, and build a lightweight markdown
summary. Related runtime pieces live in app/api/endpoints/videos.py and
app/services/supabase_content_repository.py.
"""

import os
import json
import argparse
from datetime import timedelta

# --- transcription deps ---
import whisper

# --- summarization deps ---
import re
import math
import numpy as np
from sklearn.feature_extraction.text import TfidfVectorizer

# ---------- helpers ----------
def hhmmss(seconds: float) -> str:
    """Return an SRT-style timestamp (00:00:00,000)."""
    td = timedelta(seconds=float(seconds))
    total_seconds = int(td.total_seconds())
    ms = int((float(seconds) - int(seconds)) * 1000)
    h = total_seconds // 3600
    m = (total_seconds % 3600) // 60
    s = total_seconds % 60
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"

def vtt_ts(seconds: float) -> str:
    """Return a WebVTT-style timestamp (00:00:00.000)."""
    td = timedelta(seconds=float(seconds))
    total_seconds = int(td.total_seconds())
    ms = int((float(seconds) - int(seconds)) * 1000)
    h = total_seconds // 3600
    m = (total_seconds % 3600) // 60
    s = total_seconds % 60
    return f"{h:02d}:{m:02d}:{s:02d}.{ms:03d}"

def write_txt(segments, path_txt):
    """Write plain text transcript with inline timestamps."""
    with open(path_txt, "w", encoding="utf-8") as f:
        for seg in segments:
            f.write(f"[{vtt_ts(seg['start'])} --> {vtt_ts(seg['end'])}]  {seg['text'].strip()}\n")

def write_json(segments, path_json):
    """Persist raw Whisper segments for downstream processing."""
    with open(path_json, "w", encoding="utf-8") as f:
        json.dump(segments, f, ensure_ascii=False, indent=2)

def write_srt(segments, path_srt):
    """Render the transcript in SubRip (.srt) format."""
    with open(path_srt, "w", encoding="utf-8") as f:
        for i, seg in enumerate(segments, 1):
            f.write(str(i) + "\n")
            f.write(f"{hhmmss(seg['start'])} --> {hhmmss(seg['end'])}\n")
            f.write(seg["text"].strip() + "\n\n")

def write_vtt(segments, path_vtt):
    """Render the transcript in WebVTT format."""
    with open(path_vtt, "w", encoding="utf-8") as f:
        f.write("WEBVTT\n\n")
        for seg in segments:
            f.write(f"{vtt_ts(seg['start'])} --> {vtt_ts(seg['end'])}\n")
            f.write(seg["text"].strip() + "\n\n")

# ---------- simple topic grouper ----------
def group_by_gap(segments, gap_seconds=20.0, max_section_minutes=8):
    """
    Join consecutive captions into larger sections when there's a long pause or
    the current section exceeds a max duration. Provides chunked context for
    summarization.
    """
    sections = []
    if not segments:
        return sections

    current = {"start": segments[0]["start"], "end": segments[0]["end"], "texts": [segments[0]["text"]]}
    last_end = segments[0]["end"]
    max_len = max_section_minutes * 60.0

    for seg in segments[1:]:
        s, e, t = seg["start"], seg["end"], seg["text"]
        boundary = (s - last_end) > gap_seconds or (e - current["start"]) > max_len

        if boundary:
            current["end"] = last_end
            sections.append(current)
            current = {"start": s, "end": e, "texts": [t]}
        else:
            current["end"] = e
            current["texts"].append(t)
        last_end = e

    sections.append(current)
    return sections

def summarize_section(text, max_bullets=4):
    """Select the most contentful sentences from a section using TF-IDF scores."""
    sents = re.split(r"(?<=[.!?])\s+", text.strip())
    sents = [s.strip() for s in sents if s.strip()]
    if not sents:
        return []

    vec = TfidfVectorizer(stop_words="english").fit_transform(sents)
    scores = np.asarray(vec.sum(axis=1)).ravel()
    # always include the first sentence; then best scoring
    picks = [0]
    rest = np.argsort(scores)[::-1]
    for idx in rest:
        if idx not in picks:
            picks.append(idx)
        if len(picks) >= max_bullets:
            break
    picks = sorted(picks)
    return [sents[i] for i in picks]

def write_topic_summary(sections, out_path, markdown=True):
    """Write the final summary file with timestamps and bullet points."""
    lines = []
    title = "# Topic Summary" if markdown else "Topic Summary"
    lines.append(title)
    lines.append("")

    for i, sec in enumerate(sections, 1):
        start = str(timedelta(seconds=int(sec["start"])))
        end = str(timedelta(seconds=int(sec["end"])))
        header = f"## {i}. Section ({start}–{end})" if markdown else f"{i}. Section ({start}–{end})"
        lines.append(header)
        if markdown:
            lines.append(f"**Time:** {start}–{end}")
        joined = " ".join(sec["texts"]).strip()
        bullets = summarize_section(joined, max_bullets=4)
        if bullets:
            if markdown:
                lines.append("**Key Points:**")
            for b in bullets:
                lines.append(f"- {b}")
        lines.append("")

    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

# ---------- main ----------
def main():
    """CLI entry point: transcribe, emit artifacts, and build the summary file."""
    ap = argparse.ArgumentParser(description="Transcribe and summarize a video with Whisper (Python).")
    ap.add_argument("--input", "-i", required=True, help="Path to video/audio file (e.g., C:\\path\\to\\file.mp4)")
    ap.add_argument("--model", default="small", help="Whisper model: tiny/base/small/medium/large (CPU users: small/base recommended)")
    ap.add_argument("--language", default=None, help="Force language code like 'en' (optional)")
    ap.add_argument("--gap", type=float, default=20.0, help="Pause (seconds) that starts a new topic section")
    ap.add_argument("--max_section_minutes", type=float, default=8.0, help="Maximum section length before splitting")
    ap.add_argument("--outdir", default=None, help="Folder to write outputs (default: same as input)")
    ap.add_argument("--markdown", action="store_true", help="Write summary as .md (default off writes .txt)")
    args = ap.parse_args()

    inpath = os.path.abspath(args.input)
    assert os.path.exists(inpath), f"File not found: {inpath}"

    outdir = args.outdir or os.path.dirname(inpath)
    os.makedirs(outdir, exist_ok=True)

    base = os.path.splitext(os.path.basename(inpath))[0]
    path_txt  = os.path.join(outdir, f"{base}.txt")
    path_srt  = os.path.join(outdir, f"{base}.srt")
    path_vtt  = os.path.join(outdir, f"{base}.vtt")
    path_json = os.path.join(outdir, f"{base}.json")
    path_sum  = os.path.join(outdir, f"{base}_topics.md" if args.markdown else f"{base}_topics.txt")

    # --- 1) load model
    print(f"[whisper] loading model: {args.model}")
    model = whisper.load_model(args.model)

    # --- 2) transcribe
    print(f"[whisper] transcribing: {inpath}")
    result = model.transcribe(
        inpath,
        language=args.language,     # None lets whisper auto-detect
        verbose=False,
        word_timestamps=False,      # set True if you need per-word timing
        condition_on_previous_text=True
    )

    # segments: start, end, text
    segments = [{"start": float(s["start"]), "end": float(s["end"]), "text": s["text"]} for s in result["segments"]]

    # --- 3) write outputs
    print("[write] saving .txt/.srt/.vtt/.json")
    write_txt(segments, path_txt)
    write_srt(segments, path_srt)
    write_vtt(segments, path_vtt)
    write_json(segments, path_json)

    # --- 4) summarize into topics
    print("[summary] building topic sections…")
    sections = group_by_gap(segments, gap_seconds=args.gap, max_section_minutes=args.max_section_minutes)
    write_topic_summary(sections, path_sum, markdown=args.markdown)

    print("\nDone! Files written to:")
    print(" -", path_txt)
    print(" -", path_srt)
    print(" -", path_vtt)
    print(" -", path_json)
    print(" -", path_sum)

if __name__ == "__main__":
    main()
