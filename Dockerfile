FROM python:3.11-slim

WORKDIR /app

# System dependencies:
#   ffmpeg — required by openai-whisper for audio/video processing
#   build-essential — needed to compile some Python C extensions
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
        build-essential \
    && rm -rf /var/lib/apt/lists/*

# Install Python dependencies before copying application code so that
# Docker can cache this layer when only source files change.
#
# Install CPU-only torch first so that sentence-transformers and openai-whisper
# don't pull the full CUDA build (~2.5 GB). The CPU wheel is ~250 MB and works
# fine on a VM without a GPU.
COPY requirements.txt .
RUN pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu
RUN pip install --no-cache-dir -r requirements.txt

# Copy application source
COPY . .

# Pre-create data directories so the container can start without a volume mount
RUN mkdir -p \
    data/documents/docx \
    data/documents/doc \
    data/videos/transcripts \
    data/processed/content_repository

EXPOSE 8000

# Run with gunicorn + uvicorn workers.
# Workers: 2 is a safe default for a VM with heavy ML models in memory.
# Increase if your VM has plenty of RAM and the workload is mostly I/O-bound.
# Timeout: 120s to accommodate whisper transcription on longer videos.
CMD ["gunicorn", "main:app", \
     "--workers", "2", \
     "--worker-class", "uvicorn.workers.UvicornWorker", \
     "--bind", "0.0.0.0:8000", \
     "--timeout", "120", \
     "--log-level", "info"]
