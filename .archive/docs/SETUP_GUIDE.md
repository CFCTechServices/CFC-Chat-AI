# ⚙️ Setup Guide

For the most up-to-date and comprehensive instructions on setting up the local development environment, running the application, and understanding the CI/CD pipeline, please refer to:

👉 [**DEVELOPMENT_SETUP.md**](./DEVELOPMENT_SETUP.md)

### Quick Start (Local)

1.  `python -m venv .venv`
2.  `source .venv/bin/activate` (or `.venv\Scripts\activate` on Windows)
3.  `pip install -r requirements.txt`
4.  `cp .env.example .env` (Add your API keys)
5.  `uvicorn main:app --reload`

Access the UI at [http://localhost:8000](http://localhost:8000).
