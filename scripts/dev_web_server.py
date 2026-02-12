"""
Lightweight FastAPI server to preview the static web UI without backend features.

Serves the contents of the project's `web` directory at `/ui`.
Use when you want to run the web UI without configuring Pinecone or other services.
"""

from pathlib import Path
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import RedirectResponse
import uvicorn


BASE_DIR = Path(__file__).resolve().parents[1]
WEB_DIR = BASE_DIR / "web"

app = FastAPI(title="CFC Chat UI Preview", version="dev")

# Serve static UI under /ui
app.mount("/ui", StaticFiles(directory=WEB_DIR, html=True), name="ui")

# CORS: open for local preview
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/")
def root():
    return RedirectResponse(url="/ui/")


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=5173, reload=False, log_level="info")
